using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Ashburn.Noise
{
    /// <summary>
    /// Draws the ring of hearing around the player: a circle that ripples toward whatever just made
    /// a sound, coloured by who made it.
    ///
    /// This is the player's only bearing while the lights are out. The flashlight shows one narrow
    /// wedge, but sound arrives from every direction at once, so the ring has to answer three
    /// questions at a glance — which way, how loud, and who.
    ///
    /// Colour carries the last one, and it is deliberately unreliable. Each sound is judged on its
    /// own distance: close enough to place, a partner reads green and the monster reads red; too
    /// far, and it collapses to yellow — something is out there, and that is all the headset will
    /// say. So a monster at arm's length stays red while a partner shouting from across the map is
    /// only a yellow smear, which is the reading that matters when both happen at once.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class NoiseRing : MonoBehaviour
    {
        struct Ripple
        {
            public float Angle;      // radians, world space
            public float Strength;   // 0..1 at birth
            public float Distance;   // world units to whatever made it, at birth
            public float BornAt;
            public NoiseKind Kind;
        }

        [Header("Shape")]
        [Tooltip("Radius of the resting circle, in world units.")]
        [SerializeField] float radius = 2.2f;

        [Tooltip("Line thickness, in world units. About a pixel and a half at PPU 32.")]
        [SerializeField] float thickness = 0.05f;

        [Tooltip("Segments around the circle. More is smoother and costs more.")]
        [SerializeField, Range(24, 256)] int segments = 96;

        [Tooltip("Squashes the circle vertically, matching the foreshortening of a top-down view.")]
        [SerializeField, Range(0.4f, 1f)] float verticalScale = 0.75f;

        [Header("Ripples")]
        [Tooltip("How far the loudest sound pushes the line out, in world units.")]
        [SerializeField] float maxDisplacement = 0.55f;

        [Tooltip("Seconds a ripple takes to fade out.")]
        [SerializeField] float rippleLifetime = 0.9f;

        [Tooltip("How wide a ripple spreads around its direction, in degrees.")]
        [SerializeField] float rippleSpread = 38f;

        [Tooltip("Waves along the ripple. Higher looks more agitated.")]
        [SerializeField] float rippleFrequency = 3f;

        [Tooltip("Times the spike shakes over its lifetime. A narrow ripple has almost no width " +
                 "for the spatial waves to show in, so this is what makes it read as violent.")]
        [SerializeField] float rippleOscillations = 4f;

        [Header("Colour")]
        [SerializeField] Color allyColour = new(0.35f, 1f, 0.45f);
        [SerializeField] Color monsterColour = new(1f, 0.25f, 0.25f);
        [Tooltip("Used for every source once the partner is too far away to tell sounds apart.")]
        [SerializeField] Color unknownColour = new(1f, 0.9f, 0.3f);
        [SerializeField] Color restColour = new(0.5f, 0.55f, 0.7f);

        [Tooltip("Brightness of the circle when nothing is making noise.")]
        [SerializeField, Range(0f, 1f)] float restAlpha = 0.18f;

        [Header("Partner")]
        [Tooltip("The other player. Left empty it is looked for, which is the normal case: this " +
                 "component rides the character prefab and a prefab cannot point at somebody who " +
                 "is created when the game starts. Set it only to override that.")]
        [SerializeField] Transform ally;

        [Tooltip("Past this distance a sound is too far to place, and reads as unknown. Judged per " +
                 "sound, against whatever made it — a monster at arm's length is red even with the " +
                 "partner across the map.")]
        [FormerlySerializedAs("allyRecognitionRange")]
        [SerializeField] float recognitionRange = 5f;

        readonly List<Ripple> _ripples = new();
        Mesh _mesh;
        Vector3[] _vertices;
        Color[] _colours;
        NoiseEars _ears;
        World.MapPresence _presence;

        /// <summary>True while the partner is close enough to be told apart by colour.</summary>
        public bool AllyInRange
        {
            get
            {
                var partner = Ally();
                return partner != null &&
                       Vector2.Distance(partner.position, transform.position) <= recognitionRange;
            }
        }

        /// <summary>
        /// The partner, found rather than assigned.
        ///
        /// Nothing could assign it. This rides the character prefab, and the other player is created
        /// when the room fills up — so the field serialised as None on every character ever spawned
        /// and the colours collapsed to unknown for the whole game. Green and red were unreachable.
        ///
        /// Asked for again whenever the answer has gone: a partner who disconnects leaves a
        /// destroyed reference behind, and one who rejoins is a different object.
        /// </summary>
        Transform Ally()
        {
            if (ally != null)
                return ally;

            // Only ever runs on the viewer — the ring is switched off on a partner's copy — so any
            // character that is not the viewer is the partner.
            var here = _presence != null ? _presence.Zone : null;

            foreach (var rig in Player.PlayerRig.All)
            {
                if (rig == null || rig.IsViewer)
                    continue;

                // Before the distance, and for the reason NoiseEars checks it first: a partner in
                // another building is not somebody standing far away, and the two must not read the
                // same. Their map sits a thousand units off, so distance alone would answer
                // correctly today and stop doing so the moment the spacing is tuned.
                var theirs = rig.GetComponentInParent<World.MapPresence>();
                if (here != null && theirs != null && theirs.Zone != here)
                    continue;

                return ally = rig.transform;
            }

            return null;
        }

        void Awake()
        {
            BuildMesh();
            _ears = GetComponentInParent<NoiseEars>();
            _presence = GetComponentInParent<World.MapPresence>();
        }

        void OnEnable()
        {
            if (_ears != null)
                _ears.Heard += OnHeard;
            else
                NoiseBus.Heard += OnNoiseDirect;
        }

        void OnDisable()
        {
            if (_ears != null)
                _ears.Heard -= OnHeard;
            else
                NoiseBus.Heard -= OnNoiseDirect;

            _ripples.Clear();
        }

        void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
        }

        void OnHeard(NoiseEvent noise, float strength) => AddRipple(noise, strength);

        // Without ears of its own the ring falls back to raw bus traffic, so it still works when
        // dropped onto something that is not a listener. The map rule applies here too: this path
        // skips NoiseEars, and skipping the filter with it would leave one way for a sound to cross
        // between maps.
        void OnNoiseDirect(NoiseEvent noise)
        {
            if (noise.Map != Ashburn.World.MapZone.IdOf(this))
                return;

            var distance = Vector2.Distance(noise.Position, transform.position);
            if (distance > noise.Range)
                return;

            AddRipple(noise, Mathf.Clamp01(1f - distance / noise.Range));
        }

        void AddRipple(NoiseEvent noise, float strength)
        {
            // The player's own footsteps would swamp the ring with a ripple under their feet and
            // tell them nothing they did not already know.
            if (noise.Kind == NoiseKind.Self)
                return;

            var offset = noise.Position - (Vector2)transform.position;
            if (offset.sqrMagnitude < 1e-6f)
                return;

            _ripples.Add(new Ripple
            {
                Angle = Mathf.Atan2(offset.y, offset.x),
                Strength = strength,
                // Measured now rather than looked up later: whatever made this has moved on by the
                // time the ripple fades, and the colour should say how far away it was when it
                // made the sound.
                Distance = offset.magnitude,
                BornAt = Time.time,
                Kind = noise.Kind,
            });
        }

        void LateUpdate()
        {
            for (var i = _ripples.Count - 1; i >= 0; i--)
                if (Time.time - _ripples[i].BornAt > rippleLifetime)
                    _ripples.RemoveAt(i);

            Redraw();
        }

        void BuildMesh()
        {
            _mesh = new Mesh { name = "Noise Ring" };
            _mesh.MarkDynamic();

            var vertexCount = segments * 2;
            _vertices = new Vector3[vertexCount];
            _colours = new Color[vertexCount];

            var triangles = new int[segments * 6];
            for (var i = 0; i < segments; i++)
            {
                var inner = i * 2;
                var outer = inner + 1;
                var nextInner = ((i + 1) % segments) * 2;
                var nextOuter = nextInner + 1;

                var t = i * 6;
                triangles[t + 0] = inner;
                triangles[t + 1] = outer;
                triangles[t + 2] = nextOuter;
                triangles[t + 3] = inner;
                triangles[t + 4] = nextOuter;
                triangles[t + 5] = nextInner;
            }

            _mesh.vertices = _vertices;
            _mesh.colors = _colours;
            _mesh.triangles = triangles;
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        void Redraw()
        {
            var spread = Mathf.Max(rippleSpread * Mathf.Deg2Rad, 0.01f);

            for (var i = 0; i < segments; i++)
            {
                var angle = i / (float)segments * Mathf.PI * 2f;

                var push = 0f;
                var weight = 0f;
                var tint = Color.clear;

                foreach (var ripple in _ripples)
                {
                    var age = Mathf.Clamp01((Time.time - ripple.BornAt) / rippleLifetime);
                    var fade = 1f - age;

                    // How much this segment faces the sound. Squaring the falloff keeps the far
                    // side of the ring calm instead of everything wobbling at once.
                    var delta = Mathf.DeltaAngle(ripple.Angle * Mathf.Rad2Deg, angle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    var lobe = Mathf.Exp(-(delta * delta) / (spread * spread));
                    if (lobe < 0.001f)
                        continue;

                    var amount = ripple.Strength * fade * lobe;

                    // The wave travels outward as it ages, so a ripple reads as something arriving
                    // rather than the line simply swelling.
                    push += amount * Mathf.Cos(delta * rippleFrequency - age * Mathf.PI * 2f * rippleOscillations);
                    weight += amount;
                    tint += ColourFor(ripple.Kind, ripple.Distance <= recognitionRange) * amount;
                }

                var colour = weight > 0.0001f ? tint / weight : restColour;
                colour.a = Mathf.Clamp01(restAlpha + weight);

                var r = radius + push * maxDisplacement;
                var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle) * verticalScale, 0f);

                _vertices[i * 2 + 0] = direction * (r - thickness * 0.5f);
                _vertices[i * 2 + 1] = direction * (r + thickness * 0.5f);
                _colours[i * 2 + 0] = colour;
                _colours[i * 2 + 1] = colour;
            }

            _mesh.vertices = _vertices;
            _mesh.colors = _colours;
            _mesh.RecalculateBounds();
        }

        Color ColourFor(NoiseKind kind, bool recognisable)
        {
            if (!recognisable)
                return unknownColour;

            return kind == NoiseKind.Monster ? monsterColour : allyColour;
        }
    }
}

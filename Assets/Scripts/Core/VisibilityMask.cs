using System.Collections.Generic;
using Ashburn.World;
using UnityEngine;

namespace Ashburn.Core
{
    /// <summary>
    /// Blacks out everything the player cannot actually see, leaving holes where they can.
    ///
    /// This is the raycast-based approach: a fan of rays measures how far sight reaches in every
    /// direction, and the mesh built from those hits is the shape of the darkness. It sits on top
    /// of the URP 2D lights rather than replacing them — the lamps, the dim global light and the
    /// beam still do the lighting, and this only decides what is allowed to be seen at all. Both
    /// halves are needed: with no light under it the mask cuts a beam-shaped hole onto black.
    ///
    /// Two ranges feed it. A short one all around, for the arm's length a person can make out in
    /// the dark, and a long one inside the flashlight's cone. Walls cut both. The result is that
    /// the beam becomes the only way to see anything beyond a couple of steps, which is the rule
    /// the whole game is built on.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DefaultExecutionOrder(960)]
    public class VisibilityMask : MonoBehaviour
    {
        [Header("Sight")]
        [Tooltip("How far the player can make out shapes with no beam on them, in world units.")]
        [SerializeField] float ambientRadius = 2.4f;

        [Tooltip("How far sight reaches inside the flashlight cone.")]
        [SerializeField] float beamRange = 7.5f;

        [Tooltip("Half the cone's width, in degrees. Match the flashlight cookie.")]
        [SerializeField] float beamHalfAngle = 24f;

        [Tooltip("The beam. Its local +Y is the direction it points. Leave empty for no cone.")]
        [SerializeField] Transform beam;

        [Header("Rays")]
        [Tooltip("Rays around the full circle. More follows wall corners more tightly.")]
        [SerializeField, Range(32, 512)] int rayCount = 180;

        [Tooltip("What blocks sight. Characters must be excluded or they hide each other.")]
        [SerializeField] LayerMask blockers = ~0;

        [Tooltip("How far past a wall sight still creeps, so the cut is not razor sharp.")]
        [SerializeField, Range(0f, 1f)] float wallBleed = 0.25f;

        [Tooltip("Where the eye sits relative to this object. Must be inside the character's " +
                 "collider: the object's own origin is at the feet, which a wall is allowed to " +
                 "occupy. Match the collider's Offset.")]
        [SerializeField] Vector2 eyeOffset = new(0f, 0.15f);

        [Header("Darkness")]
        [Tooltip("Colour painted over everything unseen. Alpha below one leaves a hint of shape.")]
        [SerializeField] Color darkness = new(0f, 0f, 0.02f, 0.93f);

        [Tooltip("Smallest radius of the outer edge. The real one grows to cover the screen " +
                 "whenever the camera is not sitting on the player, so this is a floor rather than " +
                 "the answer.")]
        [SerializeField] float outerRadius = 26f;

        [Tooltip("Extra world units past the corner of the screen, so a camera easing into place " +
                 "never briefly outruns the darkness.")]
        [SerializeField] float outerMargin = 4f;

        [Tooltip("Width of the fade between seen and unseen, in world units.")]
        [SerializeField, Range(0f, 2f)] float edgeSoftness = 0.6f;

        [Header("Room light")]
        [Tooltip("Lift the mask entirely while the building has power. This is the reward for " +
                 "finding the breaker: light everywhere, and no need for the torch.")]
        [SerializeField] bool liftWhenPowered = true;

        [Tooltip("An extra light that lifts the mask while it is active, for a lit area that is " +
                 "not the main grid. Only usable on a mask placed in the scene — see below.")]
        [SerializeField] GameObject roomLight;

        MeshRenderer _renderer;
        MapPresence _presence;
        Camera _camera;
        Mesh _mesh;
        Vector3[] _vertices;
        Color[] _colours;
        Transform _self;
        float[] _reach;

        readonly List<RaycastHit2D> _hits = new();
        readonly List<Collider2D> _overlaps = new();
        ContactFilter2D _filter;

        void Awake()
        {
            _self = transform;
            _renderer = GetComponent<MeshRenderer>();
            _presence = GetComponentInParent<MapPresence>();

            _filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = blockers,

                // A room trigger, a doorway, an interaction zone: none of them are walls, and
                // sight has to pass through all of them. Left on, the trigger the player is
                // standing inside answers every ray at zero distance and the world goes black.
                useTriggers = false,
            };

            Build();
        }

        void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
        }

        void Build()
        {
            _mesh = new Mesh { name = "Visibility Mask" };
            _mesh.MarkDynamic();
            _reach = new float[rayCount];

            // Three rings: the sight boundary, a softened step just outside it, and the far edge.
            // The middle ring is what turns a hard cut into a fade.
            var vertexCount = rayCount * 3;
            _vertices = new Vector3[vertexCount];
            _colours = new Color[vertexCount];

            var triangles = new int[rayCount * 12];
            for (var i = 0; i < rayCount; i++)
            {
                var a = i * 3;
                var b = ((i + 1) % rayCount) * 3;
                var t = i * 12;

                // inner -> middle
                triangles[t + 0] = a; triangles[t + 1] = a + 1; triangles[t + 2] = b + 1;
                triangles[t + 3] = a; triangles[t + 4] = b + 1; triangles[t + 5] = b;
                // middle -> outer
                triangles[t + 6] = a + 1; triangles[t + 7] = a + 2; triangles[t + 8] = b + 2;
                triangles[t + 9] = a + 1; triangles[t + 10] = b + 2; triangles[t + 11] = b + 1;
            }

            _mesh.vertices = _vertices;
            _mesh.colors = _colours;
            _mesh.triangles = triangles;
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        /// <summary>
        /// True while the player can see without the torch.
        ///
        /// The power is looked up rather than held as a reference because this component rides on
        /// the player prefab, and a prefab cannot point at a light sitting in a scene —
        /// <c>roomLight</c> serialises as None on every spawned player, which is why restoring the
        /// power used to change nothing.
        ///
        /// It is looked up per frame, and by the map the character is standing in: they walk from
        /// a lit house into a dark street, and the answer has to change with them.
        /// </summary>
        bool Lit
        {
            get
            {
                if (liftWhenPowered)
                {
                    var grid = PowerGrid.For(_presence != null ? _presence.Zone : null);
                    if (grid != null && grid.IsPowered)
                        return true;
                }

                return roomLight != null && roomLight.activeInHierarchy;
            }
        }

        void LateUpdate()
        {
            // With the room lit there is nothing to hide, and skipping the fan saves the rays too.
            var lit = Lit;
            if (_renderer != null)
                _renderer.enabled = !lit;

            if (lit)
                return;

            CastFan();
            Redraw();
        }

        void CastFan()
        {
            // Not the object's position: that sits at the feet, below the collider, and a wall is
            // entitled to occupy it. Pressed against one, every ray then started inside the wall,
            // came back at distance zero, and sight shut down to wallBleed in all 180 directions at
            // once — the flashlight looked like it had failed. The collider's centre is the one
            // point physics guarantees is never inside a wall, so the eye goes there.
            var origin = (Vector2)_self.position + eyeOffset;
            var haveBeam = beam != null && beam.gameObject.activeInHierarchy;

            // Belt and braces: if the eye does end up buried — a corner the body was pushed into —
            // the rays are all answering at zero and none of them mean anything. Fall back to the
            // arm's-length bubble, which is neither blind nor an x-ray through the wall.
            var buried = Physics2D.OverlapPoint(origin, _filter, _overlaps) > 0;
            var beamAngle = haveBeam ? Mathf.Atan2(beam.up.y, beam.up.x) * Mathf.Rad2Deg : 0f;

            for (var i = 0; i < rayCount; i++)
            {
                var degrees = i / (float)rayCount * 360f;
                var direction = new Vector2(
                    Mathf.Cos(degrees * Mathf.Deg2Rad),
                    Mathf.Sin(degrees * Mathf.Deg2Rad));

                // Inside the cone sight runs long; everywhere else it is arm's length.
                var want = ambientRadius;
                if (haveBeam && Mathf.Abs(Mathf.DeltaAngle(beamAngle, degrees)) <= beamHalfAngle)
                    want = Mathf.Max(want, beamRange);

                if (buried)
                {
                    _reach[i] = Mathf.Min(want, ambientRadius);
                    continue;
                }

                var nearest = want;
                var count = Physics2D.Raycast(origin, direction, _filter, _hits, want);
                for (var h = 0; h < count; h++)
                {
                    // Hits are not documented as sorted, so take the closest rather than the
                    // first, or a far wall could cut sight before a near one.
                    if (_hits[h].distance < nearest)
                        nearest = _hits[h].distance;
                }

                _reach[i] = nearest >= want ? want : Mathf.Min(want, nearest + wallBleed);
            }
        }

        /// <summary>
        /// How far out the darkness has to reach to still cover the screen.
        ///
        /// The mesh is centred on the player, but the camera is not always: a room camera framing a
        /// corridor sits wherever the room needs it, and the player can be right at the edge of
        /// their own view. A fixed radius then falls short on the far side and the map shows
        /// through unmasked — which reads as the darkness being broken rather than as the camera
        /// having moved. Measured from where the camera actually is, it cannot fall short.
        /// </summary>
        float OuterReach()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null)
                    return outerRadius;
            }

            var halfHeight = _camera.orthographic
                ? _camera.orthographicSize
                : Mathf.Abs(_camera.transform.position.z - _self.position.z);

            var halfWidth = halfHeight * _camera.aspect;
            var cornerFromCamera = Mathf.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight);
            var cameraFromPlayer = Vector2.Distance(_camera.transform.position, _self.position);

            return Mathf.Max(outerRadius, cameraFromPlayer + cornerFromCamera + outerMargin);
        }

        void Redraw()
        {
            var clear = new Color(darkness.r, darkness.g, darkness.b, 0f);
            var outer = OuterReach();

            // The mesh lives in this object's local space, so the hole has to be built around the
            // same point the rays were cast from or the darkness would sit a step off the sight.
            var eye = new Vector3(eyeOffset.x, eyeOffset.y, 0f);

            for (var i = 0; i < rayCount; i++)
            {
                var degrees = i / (float)rayCount * 360f;
                var direction = new Vector3(
                    Mathf.Cos(degrees * Mathf.Deg2Rad),
                    Mathf.Sin(degrees * Mathf.Deg2Rad), 0f);

                var v = i * 3;
                _vertices[v + 0] = eye + direction * _reach[i];
                _vertices[v + 1] = eye + direction * (_reach[i] + edgeSoftness);
                _vertices[v + 2] = eye + direction * outer;

                _colours[v + 0] = clear;
                _colours[v + 1] = darkness;
                _colours[v + 2] = darkness;
            }

            _mesh.vertices = _vertices;
            _mesh.colors = _colours;
            _mesh.RecalculateBounds();
        }
    }
}

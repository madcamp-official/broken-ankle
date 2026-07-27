using System.Collections.Generic;
using UnityEngine;

namespace Ashburn.Core
{
    /// <summary>
    /// Blacks out everything the player cannot actually see, leaving holes where they can.
    ///
    /// This is the raycast-based approach: a fan of rays measures how far sight reaches in every
    /// direction, and the mesh built from those hits is the shape of the darkness. It sits on top
    /// of the URP 2D lights rather than replacing them — the lamp, the global light and the beam
    /// still do the lighting, and this only decides what is allowed to be seen at all.
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

        [Header("Darkness")]
        [Tooltip("Colour painted over everything unseen. Alpha below one leaves a hint of shape.")]
        [SerializeField] Color darkness = new(0f, 0f, 0.02f, 0.93f);

        [Tooltip("Radius of the outer edge. Must comfortably cover the screen.")]
        [SerializeField] float outerRadius = 26f;

        [Tooltip("Width of the fade between seen and unseen, in world units.")]
        [SerializeField, Range(0f, 2f)] float edgeSoftness = 0.6f;

        [Header("Room light")]
        [Tooltip("While this is active the mask stops drawing, so repairing the power means seeing " +
                 "the whole room without a flashlight. Leave empty to always mask.")]
        [SerializeField] GameObject roomLight;

        MeshRenderer _renderer;
        Mesh _mesh;
        Vector3[] _vertices;
        Color[] _colours;
        Transform _self;
        float[] _reach;

        readonly List<RaycastHit2D> _hits = new();
        ContactFilter2D _filter;

        void Awake()
        {
            _self = transform;
            _renderer = GetComponent<MeshRenderer>();

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

        void LateUpdate()
        {
            // With the room lit there is nothing to hide, and skipping the fan saves the rays too.
            var lit = roomLight != null && roomLight.activeInHierarchy;
            if (_renderer != null)
                _renderer.enabled = !lit;

            if (lit)
                return;

            CastFan();
            Redraw();
        }

        void CastFan()
        {
            var origin = (Vector2)_self.position;
            var haveBeam = beam != null && beam.gameObject.activeInHierarchy;
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

        void Redraw()
        {
            var clear = new Color(darkness.r, darkness.g, darkness.b, 0f);

            for (var i = 0; i < rayCount; i++)
            {
                var degrees = i / (float)rayCount * 360f;
                var direction = new Vector3(
                    Mathf.Cos(degrees * Mathf.Deg2Rad),
                    Mathf.Sin(degrees * Mathf.Deg2Rad), 0f);

                var v = i * 3;
                _vertices[v + 0] = direction * _reach[i];
                _vertices[v + 1] = direction * (_reach[i] + edgeSoftness);
                _vertices[v + 2] = direction * outerRadius;

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

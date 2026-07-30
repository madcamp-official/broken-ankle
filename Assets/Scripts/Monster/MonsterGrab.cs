using Ashburn.Noise;
using Ashburn.World;
using UnityEngine;

namespace Ashburn.Monster
{
    /// <summary>
    /// Grabs a player on contact and hauls them back to the nest.
    ///
    /// The drag is the window the whole co-op loop lives in. Being caught is not the end, it is a
    /// countdown that a partner can still beat, so the trip home is deliberately slow and noisy
    /// enough to be followed.
    /// </summary>
    [RequireComponent(typeof(MonsterAI))]
    public class MonsterGrab : MonoBehaviour
    {
        [Tooltip("How close it has to get to grab, in world units.")]
        [SerializeField] float grabRadius = 0.55f;

        [Tooltip("Where captives are taken. Reaching it ends the run for whoever is being carried.")]
        [SerializeField] Transform nest;

        [Tooltip("Speed while hauling. Slower than a chase, so a rescue is possible.")]
        [SerializeField] float dragSpeed = 1.9f;

        [Tooltip("Where the captive is held relative to the monster, in world units.")]
        [SerializeField] Vector2 carryOffset = new(0f, -0.4f);

        [Tooltip("Layers to look for players on.")]
        [SerializeField] LayerMask playerLayers = ~0;

        [Header("Noise")]
        [Tooltip("How far the sound of the drag carries, in world units.")]
        [SerializeField] float dragNoiseRange = 12f;

        [Tooltip("Seconds between those sounds.")]
        [SerializeField] float dragNoiseInterval = 0.55f;

        MonsterAI _ai;
        Rigidbody2D _body;
        Captive _carried;
        float _nextDragNoiseAt;
        readonly Collider2D[] _hits = new Collider2D[8];

        public bool IsCarrying => _carried != null;

        void Awake()
        {
            _ai = GetComponent<MonsterAI>();
            _body = GetComponent<Rigidbody2D>();

            // Said here rather than discovered later. Grabbing switches the AI off and hands
            // steering to the haul, so with nowhere to haul to the monster stops dead the first
            // time it touches somebody — which looks exactly like the pathfinding having failed,
            // and sends you looking in the wrong place for it.
            if (nest == null)
                Debug.LogError($"{nameof(MonsterGrab)} on '{name}' has no nest. It will not take " +
                               "anybody until one is set. The greybox marks the spot with an N.", this);
        }

        void Update()
        {
            if (_carried == null)
            {
                TryGrab();
                return;
            }

            // The captive was freed by a partner, or destroyed. Either way, back to hunting.
            if (!_carried.IsHeld)
            {
                Stop();
                return;
            }

            _carried.transform.position = (Vector2)transform.position + carryOffset;
            EmitDragNoise();

            if (nest != null && Vector2.Distance(transform.position, nest.position) < 0.6f)
            {
                _carried.MarkLost();
                Stop();
            }
        }

        void FixedUpdate()
        {
            if (_carried == null || nest == null)
                return;

            // Steering is taken over from the AI while hauling: it has one place to be.
            var direction = ((Vector2)nest.position - _body.position).normalized;
            _body.linearVelocity = direction * dragSpeed;
        }

        void TryGrab()
        {
            // Walking through somebody is a bug you can see. Freezing on top of them with the AI
            // switched off is a bug that looks like three other bugs.
            if (nest == null)
                return;

            var count = Physics2D.OverlapCircleNonAlloc(transform.position, grabRadius, _hits, playerLayers);
            for (var i = 0; i < count; i++)
            {
                var captive = _hits[i].GetComponentInParent<Captive>();
                if (captive == null || captive.IsHeld)
                    continue;

                _carried = captive;
                captive.Seize(transform);
                _ai.enabled = false;
                return;
            }
        }

        /// <summary>
        /// Keeps the haul audible.
        ///
        /// Grabbing switches <see cref="MonsterAI"/> off, and the noise the monster makes was
        /// switched off with it — so the one stretch of the game that is supposed to be followable
        /// was the only silent thing in the house. A partner cannot beat a countdown they cannot
        /// hear, and the drag is slow and loud on purpose.
        /// </summary>
        void EmitDragNoise()
        {
            if (Time.time < _nextDragNoiseAt)
                return;

            _nextDragNoiseAt = Time.time + dragNoiseInterval;
            NoiseBus.Emit(transform.position, dragNoiseRange, NoiseKind.Monster, MapZone.IdOf(this));
        }

        void Stop()
        {
            _carried = null;
            _ai.enabled = true;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, grabRadius);
            if (nest != null)
                Gizmos.DrawLine(transform.position, nest.position);
        }
    }
}

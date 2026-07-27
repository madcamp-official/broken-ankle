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

        MonsterAI _ai;
        Rigidbody2D _body;
        Captive _carried;
        readonly Collider2D[] _hits = new Collider2D[8];

        public bool IsCarrying => _carried != null;

        void Awake()
        {
            _ai = GetComponent<MonsterAI>();
            _body = GetComponent<Rigidbody2D>();
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

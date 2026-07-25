using Morrow.Noise;
using UnityEngine;

namespace Morrow.Monster
{
    public enum MonsterState
    {
        /// <summary>Wandering with nothing on its mind.</summary>
        Patrol,

        /// <summary>Heard something, stopped, staring that way. Not yet convinced.</summary>
        Alert,

        /// <summary>Convinced. Walking to where the sound came from.</summary>
        Chase,

        /// <summary>Arrived and found nothing. Sweeping the area before giving up.</summary>
        Search,
    }

    /// <summary>
    /// The monster's ear-driven behaviour.
    ///
    /// Suspicion is the spine of it. A single sound raises it a little and it drains on its own,
    /// so one unlucky footstep is survivable, but moving again before it drains is not. That is
    /// what makes the choice between crouching and running matter: the crouch's short noise range
    /// often fails to reach at all, while a sprint tops the gauge from across the room.
    ///
    /// Navigation is deliberately dumb — steer at the target, feel ahead with a few rays, slide
    /// around what it touches. A NavMesh would have to be rebaked every time the level changes,
    /// and the level is being built in parallel.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(NoiseEars))]
    public class MonsterAI : MonoBehaviour
    {
        [Header("Suspicion")]
        [Tooltip("Added per sound, scaled by how strongly it was heard.")]
        [SerializeField] float suspicionPerNoise = 0.55f;

        [Tooltip("Drained per second while nothing new is heard.")]
        [SerializeField] float suspicionDecay = 0.22f;

        [Tooltip("Above this the monster commits and walks over. Below it only turns to look.")]
        [SerializeField, Range(0.1f, 2f)] float chaseThreshold = 0.75f;

        [Header("Movement")]
        [SerializeField] float patrolSpeed = 1.6f;
        [SerializeField] float chaseSpeed = 3.2f;
        [SerializeField] float searchSpeed = 2f;

        [Tooltip("Layers the monster cannot walk through.")]
        [SerializeField] LayerMask obstacles = ~0;

        [Tooltip("How far ahead it feels for walls, in world units.")]
        [SerializeField] float feelerLength = 1.2f;

        [Header("Search")]
        [Tooltip("How far around the noise it pokes about before giving up, in world units.")]
        [SerializeField] float searchRadius = 3.5f;

        [Tooltip("Seconds spent sweeping before returning to patrol.")]
        [SerializeField] float searchDuration = 6f;

        [Header("Own noise")]
        [Tooltip("How far the monster's own footsteps carry. This is what the ring draws in red.")]
        [SerializeField] float footstepRange = 14f;

        [SerializeField] float footstepInterval = 0.7f;

        /// <summary>Current behaviour, for animation, audio and debugging.</summary>
        public MonsterState State { get; private set; } = MonsterState.Patrol;

        /// <summary>0..1-ish gauge. Reaching <see cref="chaseThreshold"/> starts a chase.</summary>
        public float Suspicion { get; private set; }

        /// <summary>Where it is heading right now, whatever the reason.</summary>
        public Vector2 Destination { get; private set; }

        Rigidbody2D _body;
        NoiseEars _ears;
        Vector2 _patrolDirection;
        Vector2 _lastNoisePosition;
        float _stateEnteredAt;
        float _nextFootstepAt;
        float _nextWanderAt;

        void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _body.gravityScale = 0f;
            _body.freezeRotation = true;

            _ears = GetComponent<NoiseEars>();
            _patrolDirection = Random.insideUnitCircle.normalized;
            Destination = _body.position;
        }

        void OnEnable() => _ears.Heard += OnHeard;

        void OnDisable() => _ears.Heard -= OnHeard;

        void OnHeard(NoiseEvent noise, float strength)
        {
            Suspicion += suspicionPerNoise * strength;
            _lastNoisePosition = noise.Position;

            // A fresh sound while already searching means the trail is warm again.
            if (Suspicion >= chaseThreshold)
                Enter(MonsterState.Chase);
            else if (State == MonsterState.Patrol)
                Enter(MonsterState.Alert);
        }

        void Update()
        {
            Suspicion = Mathf.Max(0f, Suspicion - suspicionDecay * Time.deltaTime);
            Think();
            EmitFootsteps();
        }

        void FixedUpdate() => _body.linearVelocity = Steer();

        void Enter(MonsterState state)
        {
            State = state;
            _stateEnteredAt = Time.time;

            if (state == MonsterState.Chase || state == MonsterState.Alert)
                Destination = _lastNoisePosition;
        }

        void Think()
        {
            switch (State)
            {
                case MonsterState.Alert:
                    // Standing still and listening. Either the gauge climbs and it commits, or it
                    // drains and the monster shrugs it off.
                    if (Suspicion <= 0.01f)
                        Enter(MonsterState.Patrol);
                    break;

                case MonsterState.Chase:
                    Destination = _lastNoisePosition;
                    if (Vector2.Distance(_body.position, Destination) < 0.6f)
                        Enter(MonsterState.Search);
                    break;

                case MonsterState.Search:
                    if (Time.time - _stateEnteredAt > searchDuration)
                    {
                        Suspicion = 0f;
                        Enter(MonsterState.Patrol);
                    }
                    else if (Time.time >= _nextWanderAt ||
                             Vector2.Distance(_body.position, Destination) < 0.5f)
                    {
                        // Poke about near where the sound was rather than standing on the spot.
                        Destination = _lastNoisePosition + Random.insideUnitCircle * searchRadius;
                        _nextWanderAt = Time.time + 1.5f;
                    }
                    break;

                default:
                    if (Time.time >= _nextWanderAt)
                    {
                        _patrolDirection = Random.insideUnitCircle.normalized;
                        _nextWanderAt = Time.time + Random.Range(2f, 4f);
                    }
                    Destination = _body.position + _patrolDirection * 3f;
                    break;
            }
        }

        Vector2 Steer()
        {
            if (State == MonsterState.Alert)
                return Vector2.zero;

            var speed = State == MonsterState.Chase ? chaseSpeed
                      : State == MonsterState.Search ? searchSpeed
                      : patrolSpeed;

            var desired = Destination - _body.position;
            if (desired.sqrMagnitude < 0.0001f)
                return Vector2.zero;

            desired.Normalize();

            // Feel ahead and to both sides. Sliding along the first free direction is enough to get
            // around a wall without a path, and it never gets stuck facing a corner.
            if (Blocked(desired))
            {
                var left = Rotate(desired, 55f);
                var right = Rotate(desired, -55f);
                if (!Blocked(left)) desired = left;
                else if (!Blocked(right)) desired = right;
                else
                {
                    desired = -desired;
                    _patrolDirection = desired;
                }
            }

            return desired * speed;
        }

        bool Blocked(Vector2 direction)
        {
            var hit = Physics2D.Raycast(_body.position, direction, feelerLength, obstacles);
            return hit.collider != null && !hit.collider.transform.IsChildOf(transform);
        }

        static Vector2 Rotate(Vector2 v, float degrees)
        {
            var r = degrees * Mathf.Deg2Rad;
            var c = Mathf.Cos(r);
            var s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        void EmitFootsteps()
        {
            if (_body.linearVelocity.sqrMagnitude < 0.05f)
                return;

            if (Time.time < _nextFootstepAt)
                return;

            // Faster movement means more frequent steps, so a charging monster is audibly closer
            // to arriving than a wandering one.
            var pace = footstepInterval * (patrolSpeed / Mathf.Max(_body.linearVelocity.magnitude, 0.01f));
            _nextFootstepAt = Time.time + Mathf.Clamp(pace, 0.15f, footstepInterval);
            NoiseBus.Emit(_body.position, footstepRange, NoiseKind.Monster);
        }
    }
}

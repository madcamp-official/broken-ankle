using System.Collections.Generic;
using Ashburn.World;
using Ashburn.Noise;
using Ashburn.Player;
using UnityEngine;

namespace Ashburn.Monster
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
    /// Getting there is a <see cref="NavGrid"/>'s job. Steering alone was tried and could not do
    /// it: the straight line to a sound in the next room runs through a wall, and no local rule
    /// for going round a wall can find a doorway it cannot see. The feelers are still here, but
    /// only for what the grid does not know about — the other monster, a player in the way, a door
    /// that shut since the map loaded.
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

        [Tooltip("Layers the monster cannot walk through. Characters do not belong here: they are " +
                 "things to walk into, not walls, and a layer with a moving body on it would be " +
                 "baked into the map's shape.")]
        [SerializeField] LayerMask obstacles = 1;

        [Tooltip("How far ahead it feels for things the map does not know about, in world units.")]
        [SerializeField] float feelerLength = 1.2f;

        [Header("Navigation")]
        [Tooltip("Half the body's width, used to decide what it fits through. Zero measures the " +
                 "collider, which is the answer that cannot drift out of date.")]
        [SerializeField] float agentRadius;

        [Tooltip("Navigation grid resolution, in world units. Must be finer than the narrowest " +
                 "gap the monster is meant to fit through, or that gap reads as solid.")]
        [SerializeField] float navCellSize = NavGrid.DefaultCellSize;

        [Tooltip("Seconds between route recalculations while chasing something that keeps moving.")]
        [SerializeField] float repathInterval = 0.35f;

        [Tooltip("How far the target may drift before the route is rebuilt early, in world units.")]
        [SerializeField] float repathTolerance = 0.75f;

        [Tooltip("Seconds of being pushed against something before it gives up on the route and " +
                 "asks for another.")]
        [SerializeField] float stuckPatience = 0.4f;

        [Header("Search")]
        [Tooltip("How far around the noise it pokes about before giving up, in world units.")]
        [SerializeField] float searchRadius = 3.5f;

        [Tooltip("Seconds spent sweeping before returning to patrol.")]
        [SerializeField] float searchDuration = 6f;

        [Header("Light")]
        [Tooltip("Suspicion added per second while a beam is on it, scaled by how squarely it is " +
                 "lit. Zero makes the monster blind, which is how it behaved before.")]
        [SerializeField] float suspicionPerLitSecond = 0.9f;

        [Header("Own noise")]
        [Tooltip("How far the monster's own footsteps carry. This is what the ring draws in red.")]
        [SerializeField] float footstepRange = 14f;

        [SerializeField] float footstepInterval = 0.7f;

        [Tooltip("How far the sound it makes standing still carries. Shorter than a footfall on " +
                 "purpose: it is breathing, not boots, and it should place the monster in the room " +
                 "rather than across the house.")]
        [SerializeField] float idleRange = 8f;

        [Tooltip("Seconds between those. A monster that has stopped is the one worth locating, and " +
                 "in silence a player walks into it.")]
        [SerializeField] float idleInterval = 2.2f;

        [Tooltip("Fraction the idle gap is randomised by, so it never settles into a metronome.")]
        [SerializeField, Range(0f, 1f)] float idleJitter = 0.35f;

        [Header("Story state")]
        [Tooltip("When set, the monster remains visible but inert until this WorldState flag exists.")]
        [SerializeField] string activationFlag;

        [Tooltip("When set, the monster becomes inert after this WorldState flag exists.")]
        [SerializeField] string deactivationFlag;

        /// <summary>Current behaviour, for animation, audio and debugging.</summary>
        public MonsterState State { get; private set; } = MonsterState.Patrol;

        /// <summary>0..1-ish gauge. Reaching <see cref="chaseThreshold"/> starts a chase.</summary>
        public float Suspicion { get; private set; }

        /// <summary>Where it is heading right now, whatever the reason.</summary>
        public Vector2 Destination { get; private set; }

        /// <summary>How fast it is really travelling, which is not what it asked for while stuck.</summary>
        public float Speed { get; private set; }

        /// <summary>Whether a beam is on it this frame, for animation, audio and debugging.</summary>
        public bool IsLit { get; private set; }

        /// <summary>Whether it has a route to <see cref="Destination"/> rather than a straight line.</summary>
        public bool HasPath => _pathIndex < _path.Count;

        /// <summary>Whether story progression currently permits this monster to act.</summary>
        public bool IsDormant =>
            (!string.IsNullOrEmpty(activationFlag) && !WorldState.Has(activationFlag)) ||
            (!string.IsNullOrEmpty(deactivationFlag) && WorldState.Has(deactivationFlag));

        /// <summary>The waypoint being steered at, for gizmos.</summary>
        Vector2 Waypoint => HasPath ? _path[_pathIndex] : Destination;

        /// <summary>
        /// Whether there is nothing further to walk towards: either close enough to the
        /// destination, or at the end of the only route there was to it.
        ///
        /// The route only counts as spent if it was built for roughly where the monster is trying
        /// to get to now. Without that check, a destination picked this frame is judged against
        /// the exhausted route to the last one, every frame reads as having arrived, and the
        /// monster picks a new destination on every update without moving towards any of them.
        /// </summary>
        bool Arrived =>
            Vector2.Distance(_body.position, Destination) < ArrivalDistance ||
            (_path.Count > 0 &&
             _pathIndex >= _path.Count &&
             Vector2.Distance(_pathGoal, Destination) <= repathTolerance);

        const float ArrivalDistance = 0.6f;
        const float WaypointDistance = 0.35f;

        // Widening deflections, tried in order and all on one side before the other. The old code
        // tried left then right from scratch every step, which at a corner picks a different side
        // every step and goes nowhere.
        static readonly float[] Deflections = { 30f, 55f, 85f, 120f, 155f };

        Rigidbody2D _body;
        NoiseEars _ears;
        NavGrid _grid;
        ContactFilter2D _feelerFilter;
        readonly RaycastHit2D[] _feelerHits = new RaycastHit2D[8];
        readonly List<Vector2> _path = new();

        Vector2 _patrolDirection;
        Vector2 _lastNoisePosition;
        Vector2 _previousPosition;
        Vector2 _pathGoal;
        Vector2 _centreOffset;
        float _radius;
        float _stateEnteredAt;
        float _nextNoiseAt;
        float _nextWanderAt;
        float _nextRepathAt;
        float _stuckFor;
        int _pathIndex;
        int _slideSign;
        bool _warnedUnzoned;

        void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _body.gravityScale = 0f;
            _body.freezeRotation = true;

            _ears = GetComponent<NoiseEars>();
            _patrolDirection = Random.insideUnitCircle.normalized;
            Destination = _body.position;
            _previousPosition = _body.position;
            MeasureBody();

            // Triggers are left out on purpose, and it is not a detail. The greybox lays a trigger
            // volume over every room for the cameras, on the same layer as the walls, and the
            // project has Queries Hit Triggers on. A feeler that counts those as solid reports a
            // wall in every direction for as long as the monster is indoors.
            _feelerFilter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = obstacles,
                useTriggers = false,
            };

            // Worked out now rather than on the first step. MapZone has already moved the map to
            // its slot by here — it claims one ahead of everything else on purpose — so the walls
            // are where they will stay, and the map load absorbs the cost instead of the first
            // chase paying it as a stutter.
            Grid();
        }

        void OnEnable()
        {
            _ears.Heard += OnHeard;

            // MonsterGrab switches this off to haul somebody home. Coming back, the route was
            // built for a position several rooms ago and the last known position is stale enough
            // to read as having crossed the map in one step.
            _previousPosition = _body.position;
            _nextRepathAt = 0f;
            _stuckFor = 0f;
        }

        void OnDisable() => _ears.Heard -= OnHeard;

        void OnHeard(NoiseEvent noise, float strength)
        {
            if (IsDormant)
                return;

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
            if (IsDormant)
            {
                Suspicion = 0f;
                IsLit = false;
                Speed = 0f;
                return;
            }

            // Drained first, then topped up by anything happening now, then acted on. A beam held
            // steadily on the monster has to out-run the decay to mean anything, which is the same
            // bargain a sound makes.
            Suspicion = Mathf.Max(0f, Suspicion - suspicionDecay * Time.deltaTime);
            SenseLight();
            Think();
            EmitOwnNoise();
        }

        /// <summary>
        /// Being caught in a flashlight, which counts for the same as being heard.
        ///
        /// Not the same as a sound in one respect, and it is the interesting one: a noise says
        /// where the noise was, but a beam says where the person holding it is standing. Getting
        /// spotted gives the monster the player, not the player's last footfall.
        ///
        /// Charged per second rather than per event, because light is not an event. Long enough in
        /// the beam and the gauge crosses on its own; a glance across the room and the decay eats
        /// it. That is the whole cost of using the torch, and it is meant to be a real one.
        /// </summary>
        void SenseLight()
        {
            IsLit = false;

            if (suspicionPerLitSecond <= 0f || FlashlightOcclusion.Lit.Count == 0)
                return;

            var here = MapZone.IdOf(this);
            var strongest = 0f;
            var holder = Vector2.zero;

            foreach (var beam in FlashlightOcclusion.Lit)
            {
                // Before the geometry, and deliberately, for the reason the noise bus checks it
                // first: this is a rule about which world the light is shining in. Two maps pushed
                // close together must not be able to open a hole between them.
                if (beam == null || MapZone.IdOf(beam) != here)
                    continue;

                if (!beam.Illuminates(Centre, out var strength) || strength <= strongest)
                    continue;

                strongest = strength;
                holder = beam.transform.position;
            }

            if (strongest <= 0f)
                return;

            IsLit = true;
            Suspicion += suspicionPerLitSecond * strongest * Time.deltaTime;
            _lastNoisePosition = holder;

            if (Suspicion >= chaseThreshold)
                Enter(MonsterState.Chase);
            else if (State == MonsterState.Patrol)
                Enter(MonsterState.Alert);
        }

        void FixedUpdate()
        {
            if (IsDormant)
            {
                _body.linearVelocity = Vector2.zero;
                _previousPosition = _body.position;
                Speed = 0f;
                return;
            }

            // Measured before the new velocity is set, so this is what the last step actually
            // achieved rather than what it was told to do.
            var moved = (_body.position - _previousPosition).magnitude;
            Speed = moved / Time.fixedDeltaTime;
            _previousPosition = _body.position;

            TrackStuck();
            _body.linearVelocity = Steer();
        }

        /// <summary>Assigns story gates to a monster spawned by the map placement director.</summary>
        public void ConfigureStoryState(string activatesOn, string deactivatesOn = null)
        {
            activationFlag = activatesOn;
            deactivationFlag = deactivatesOn;
        }

        /// <summary>Resets physics and navigation state after a runtime-authored placement.</summary>
        public void RefreshAfterPlacement()
        {
            _body.position = transform.position;
            _body.linearVelocity = Vector2.zero;
            Destination = _body.position;
            _previousPosition = _body.position;
            _lastNoisePosition = _body.position;
            _path.Clear();
            _pathIndex = 0;
            _nextRepathAt = 0f;
        }

        void Enter(MonsterState state)
        {
            var changed = State != state;
            State = state;

            if (state == MonsterState.Chase || state == MonsterState.Alert)
                Destination = _lastNoisePosition;

            // Only on a real change. A beam held on the monster asks to enter Chase on every
            // frame it is lit, and throwing the route away that often would rebuild it from
            // scratch sixty times a second and never walk any of it.
            if (!changed)
                return;

            _stateEnteredAt = Time.time;
            _nextRepathAt = 0f;
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

                    // Arrived, or got as close as the map allows. The second case is a sound from
                    // somewhere the monster cannot reach at all — through a wall, or across a
                    // locked door — and without it the monster would grind against that wall for
                    // as long as the noise kept coming.
                    if (Arrived)
                        Enter(MonsterState.Search);
                    break;

                case MonsterState.Search:
                    if (Time.time - _stateEnteredAt > searchDuration)
                    {
                        Suspicion = 0f;
                        Enter(MonsterState.Patrol);
                    }
                    else if (Time.time >= _nextWanderAt || Arrived)
                    {
                        // Poke about near where the sound was rather than standing on the spot.
                        Destination = Reachable(_lastNoisePosition + Random.insideUnitCircle * searchRadius);
                        _nextWanderAt = Time.time + 1.5f;
                        _nextRepathAt = 0f;
                    }
                    break;

                default:
                    if (Time.time >= _nextWanderAt || Arrived)
                    {
                        // Somewhere it could actually get to, rather than a point three units
                        // ahead that is as likely as not to be inside a wall. A patrol that picks
                        // real destinations walks the house instead of bouncing off the furniture.
                        _patrolDirection = Random.insideUnitCircle.normalized;
                        Destination = Reachable(_body.position + _patrolDirection * Random.Range(4f, 9f));
                        _nextWanderAt = Time.time + Random.Range(3f, 6f);
                        _nextRepathAt = 0f;
                    }
                    break;
            }
        }

        #region Navigation

        /// <summary>
        /// The nearest point to <paramref name="wanted"/> that a body this size could stand on,
        /// or <paramref name="wanted"/> itself when there is no map to ask.
        /// </summary>
        Vector2 Reachable(Vector2 wanted)
        {
            var grid = Grid();
            return grid != null && grid.TryNearestFree(wanted, out var free) ? free : wanted;
        }

        NavGrid Grid()
        {
            if (_grid != null)
                return _grid;

            var zone = MapZone.Of(this);
            if (zone == null)
            {
                if (!_warnedUnzoned)
                {
                    _warnedUnzoned = true;
                    Debug.LogWarning($"'{name}' is outside every {nameof(MapZone)}, so there is no " +
                                     "map to find a way through and no map to be heard in. Move it " +
                                     "under the map's root.", this);
                }

                return null;
            }

            _grid = NavGrid.For(zone, _radius, obstacles, navCellSize);
            return _grid;
        }

        /// <summary>Rebuilds the route when it has run out, gone stale, or stopped working.</summary>
        void EnsurePath()
        {
            if (Time.time < _nextRepathAt &&
                HasPath &&
                Vector2.Distance(_pathGoal, Destination) <= repathTolerance)
                return;

            _nextRepathAt = Time.time + repathInterval;
            _pathGoal = Destination;
            _pathIndex = 0;

            var grid = Grid();
            if (grid == null || !grid.TryFindPath(_body.position, Destination, _path))
                _path.Clear();
        }

        /// <summary>
        /// The point to steer at. Waypoints already reached are dropped, and running out of them
        /// leaves the destination itself — which is either a step away or somewhere unreachable
        /// that <see cref="Arrived"/> will notice.
        /// </summary>
        Vector2 NextWaypoint()
        {
            EnsurePath();

            while (_pathIndex < _path.Count &&
                   Vector2.Distance(_body.position, _path[_pathIndex]) < WaypointDistance)
                _pathIndex++;

            return HasPath ? _path[_pathIndex] : Destination;
        }

        Vector2 Steer()
        {
            if (State == MonsterState.Alert)
                return Vector2.zero;

            var speed = State == MonsterState.Chase ? chaseSpeed
                      : State == MonsterState.Search ? searchSpeed
                      : patrolSpeed;

            var desired = NextWaypoint() - _body.position;
            if (desired.sqrMagnitude < 0.0001f)
                return Vector2.zero;

            return Avoid(desired.normalized) * speed;
        }

        /// <summary>
        /// Bends the heading around whatever the route did not account for.
        ///
        /// Which way it bends is remembered for as long as it stays blocked. Deciding afresh every
        /// step is what made the old version shudder in place: at a corner the two sides swap over
        /// constantly, and a monster that reverses every fiftieth of a second is a monster that
        /// never leaves the spot.
        /// </summary>
        Vector2 Avoid(Vector2 desired)
        {
            if (!Blocked(desired))
            {
                _slideSign = 0;
                return desired;
            }

            if (_slideSign == 0)
                _slideSign = Cross(_body.linearVelocity, desired) >= 0f ? 1 : -1;

            foreach (var angle in Deflections)
            {
                var open = Rotate(desired, angle * _slideSign);
                if (!Blocked(open))
                    return open;
            }

            foreach (var angle in Deflections)
            {
                var open = Rotate(desired, -angle * _slideSign);
                if (!Blocked(open))
                {
                    _slideSign = -_slideSign;
                    return open;
                }
            }

            return -desired;
        }

        /// <summary>
        /// Whether a body this size could move off in that direction.
        ///
        /// A circle rather than a line, because the body is not a line: a zero-width ray slips
        /// through a doorway at an angle the shoulders never fit through, reports the way clear,
        /// and leaves the monster jammed in the frame with nothing in the logic to notice.
        ///
        /// Every hit is looked at, not just the nearest. A cast that starts touching something is
        /// the normal state of affairs for a monster against a wall, and taking only the closest
        /// hit means one discarded self-hit hides everything behind it.
        /// </summary>
        bool Blocked(Vector2 direction)
        {
            var count = Physics2D.CircleCast(_body.position, _radius * 0.85f, direction,
                                             _feelerFilter, _feelerHits, feelerLength);

            for (var i = 0; i < count; i++)
            {
                var hit = _feelerHits[i].collider;
                if (hit != null && !hit.transform.IsChildOf(transform))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Notices when the monster is walking hard into something and getting nowhere, and shakes
        /// it loose: a fresh route, and the other way round the obstacle next time.
        /// </summary>
        void TrackStuck()
        {
            var wanted = _body.linearVelocity.magnitude;
            if (wanted > 0.1f && Speed < wanted * 0.25f)
            {
                _stuckFor += Time.fixedDeltaTime;
                if (_stuckFor < stuckPatience)
                    return;

                _stuckFor = 0f;
                _slideSign = _slideSign != 0 ? -_slideSign : 1;
                _nextRepathAt = 0f;
                return;
            }

            _stuckFor = 0f;
        }

        /// <summary>
        /// Reads the body's size and where it sits from the collider, which is the answer that
        /// cannot drift out of date when somebody resizes the monster.
        /// </summary>
        void MeasureBody()
        {
            var collider = GetComponent<Collider2D>();
            if (collider == null)
            {
                Debug.LogWarning($"'{name}' has no collider to measure, so it will navigate as " +
                                 "though it were half a unit across.", this);
                _radius = 0.25f;
                return;
            }

            var extents = collider.bounds.extents;
            _radius = agentRadius > 0f ? agentRadius : Mathf.Max(extents.x, extents.y);

            // The body sits above the transform's origin — the origin is at its feet. Rotation is
            // frozen, so this offset is the same in world space, and a beam should be measured
            // against the monster rather than against the floor under it.
            _centreOffset = collider.offset;
        }

        /// <summary>Where the body actually is, as opposed to where its feet are.</summary>
        Vector2 Centre => _body.position + _centreOffset;

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static Vector2 Rotate(Vector2 v, float degrees)
        {
            var r = degrees * Mathf.Deg2Rad;
            var c = Mathf.Cos(r);
            var s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        #endregion

        /// <summary>
        /// The monster giving itself away, walking or not.
        ///
        /// One place decides that a sound happened, so walking and standing produce a single
        /// stream rather than two that can overlap. Standing still used to be silent, which made
        /// the most dangerous thing in the house — one that has stopped, and is listening —
        /// the one thing the hearing ring could not show.
        /// </summary>
        void EmitOwnNoise()
        {
            if (Time.time < _nextNoiseAt)
                return;

            // Measured movement, not asked-for movement. A monster wedged against a doorframe used
            // to keep stamping on the spot, which puts a red ring on the player's screen saying
            // something is walking when nothing is.
            var walking = Speed >= 0.2f;

            if (walking)
            {
                // Faster movement means more frequent steps, so a charging monster is audibly
                // closer to arriving than a wandering one.
                var pace = footstepInterval * (patrolSpeed / Mathf.Max(Speed, 0.01f));
                _nextNoiseAt = Time.time + Mathf.Clamp(pace, 0.15f, footstepInterval);
            }
            else
            {
                // Jittered, or it becomes a metronome a player can tune out and eventually stop
                // hearing altogether.
                _nextNoiseAt = Time.time + idleInterval * (1f + Random.Range(-idleJitter, idleJitter));
            }

            NoiseBus.Emit(_body.position, walking ? footstepRange : idleRange,
                          NoiseKind.Monster, MapZone.IdOf(this));
        }

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying)
                return;

            Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.9f);
            var from = (Vector3)_body.position;
            for (var i = _pathIndex; i < _path.Count; i++)
            {
                var to = (Vector3)_path[i];
                Gizmos.DrawLine(from, to);
                Gizmos.DrawWireSphere(to, 0.1f);
                from = to;
            }

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(Waypoint, 0.15f);
        }
    }
}

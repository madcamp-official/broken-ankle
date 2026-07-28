using System;
using Ashburn.World;
using UnityEngine;

namespace Ashburn.Noise
{
    /// <summary>
    /// The listening half of the noise system. Filters the bus down to sounds this object could
    /// actually have heard — the right kind, and close enough — and reports the loudest recent one.
    ///
    /// Deliberately has no opinion about what to do next. Chasing, investigating and giving up
    /// belong to the monster's behaviour, which can be written against <see cref="Heard"/> without
    /// this component changing.
    /// </summary>
    public class NoiseEars : MonoBehaviour
    {
        [Tooltip("Which sources this listener reacts to. A monster ignores its own footsteps.")]
        [SerializeField] bool hearSelf = true;
        [SerializeField] bool hearAlly = true;
        [SerializeField] bool hearMonster;

        [Tooltip("Multiplies every sound's range. Above one is sharp hearing, below one is dull.")]
        [SerializeField] float sensitivity = 1f;

        [Tooltip("Seconds a sound stays the 'last heard' one before it is forgotten.")]
        [SerializeField] float memory = 4f;

        /// <summary>Raised for every sound this listener could hear, with its strength 0..1.</summary>
        public event Action<NoiseEvent, float> Heard;

        /// <summary>Where the most recent audible sound came from, or null once it is forgotten.</summary>
        public Vector2? LastHeardPosition { get; private set; }

        /// <summary>Strength of that sound at the moment it was heard, 0..1.</summary>
        public float LastHeardStrength { get; private set; }

        float _lastHeardTime = float.NegativeInfinity;
        MapPresence _presence;
        MapZone _fixedZone;
        bool _warnedUnzoned;

        void Awake()
        {
            // A character carries its map with it; a listener bolted to something that does not
            // move belongs to whichever map it was built into.
            _presence = GetComponentInParent<MapPresence>();
            if (_presence == null)
                _fixedZone = MapZone.Of(this);
        }

        /// <summary>The map this listener is in, or <see cref="MapZone.Unzoned"/>.</summary>
        int MapId
        {
            get
            {
                if (_presence != null)
                    return _presence.MapId;

                return _fixedZone != null ? _fixedZone.MapId : MapZone.Unzoned;
            }
        }

        void OnEnable() => NoiseBus.Heard += OnNoise;

        void OnDisable() => NoiseBus.Heard -= OnNoise;

        void Update()
        {
            if (LastHeardPosition.HasValue && Time.time - _lastHeardTime > memory)
                LastHeardPosition = null;
        }

        void OnNoise(NoiseEvent noise)
        {
            if (!Accepts(noise.Kind))
                return;

            // Before the distance check, and deliberately: this is a rule about which world the
            // sound happened in, not about how far away it was. Two maps pushed close together, or
            // a range tuned up, must not be able to open a hole between them.
            var here = MapId;
            if (noise.Map != here)
            {
                if (noise.Map == MapZone.Unzoned && !_warnedUnzoned)
                {
                    _warnedUnzoned = true;
                    Debug.LogWarning($"'{name}' dropped a sound that belongs to no map. Whatever " +
                                     $"emitted it is outside every {nameof(MapZone)}, so it can " +
                                     "never be heard. Move it under the map's root.", this);
                }

                return;
            }

            var reach = noise.Range * sensitivity;
            if (reach <= 0f)
                return;

            var distance = Vector2.Distance(noise.Position, transform.position);
            if (distance > reach)
                return;

            // Linear rather than inverse-square: this drives a readout a player has to act on,
            // and inverse-square makes everything past a couple of units read as nothing.
            var strength = Mathf.Clamp01(1f - distance / reach);

            _lastHeardTime = Time.time;
            LastHeardPosition = noise.Position;
            LastHeardStrength = strength;
            Heard?.Invoke(noise, strength);
        }

        bool Accepts(NoiseKind kind)
        {
            switch (kind)
            {
                case NoiseKind.Self: return hearSelf;
                case NoiseKind.Ally: return hearAlly;
                case NoiseKind.Monster: return hearMonster;
                default: return false;
            }
        }
    }
}

using System;
using UnityEngine;

namespace Morrow.Noise
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

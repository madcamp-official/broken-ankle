using UnityEngine;

namespace Ashburn.Noise
{
    /// <summary>
    /// Emits a sound on a timer from wherever it sits. Stands in for anything that makes noise on
    /// its own — the monster's footsteps, a dripping pipe, a partner in another room — so the
    /// listening side can be built and felt before those things exist.
    /// </summary>
    public class NoiseBeacon : MonoBehaviour
    {
        [SerializeField] NoiseKind kind = NoiseKind.Monster;

        [Tooltip("How far each sound carries, in world units.")]
        [SerializeField] float range = 12f;

        [Tooltip("Seconds between sounds. Randomised a little so it never falls into a rhythm.")]
        [SerializeField] float interval = 2f;

        [SerializeField, Range(0f, 1f)] float intervalJitter = 0.35f;

        float _nextTime;

        void OnEnable() => _nextTime = Time.time + interval;

        void Update()
        {
            if (Time.time < _nextTime)
                return;

            _nextTime = Time.time + interval * (1f + Random.Range(-intervalJitter, intervalJitter));
            NoiseBus.Emit(transform.position, range, kind);
        }
    }
}

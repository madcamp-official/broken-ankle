using UnityEngine;

namespace Ashburn.Radio
{
    /// <summary>
    /// The headset, sitting between a partner's voice and the ear that hears it.
    ///
    /// Everything the story says the headset does to a voice happens here, on the listening side.
    /// Nothing modified is ever sent over the network: each player transmits their real voice, and
    /// each machine lies to its own player about what arrived. That is both what SENTIL built the
    /// thing to do and, conveniently, the cheap way to do it — two players can be told two
    /// different things without sending a byte more.
    ///
    /// Attach to the same object as the <see cref="AudioSource"/> a partner's voice plays through.
    /// Unity hands that source's output to <see cref="OnAudioFilterRead"/> before it reaches the
    /// speakers, which is the only place a live stream can be got at sample by sample.
    ///
    /// The radio character — the narrow band, the squashed dynamics — is always on, because a
    /// walkie-talkie always sounds like one. <see cref="Interference"/> is the part that moves:
    /// hiss, crushing and dropouts ride on it, so distance alone can take a voice from clear to
    /// unintelligible without anything else being switched.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class RadioDsp : MonoBehaviour
    {
        [Header("Delay")]
        [Tooltip("How late the partner's voice arrives. Zero is honest. Seconds.")]
        [Range(0f, 3f)]
        [SerializeField] float delaySeconds;

        [Tooltip("Ceiling for the delay line, fixed at startup because the buffer cannot be resized " +
                 "while the audio thread is reading it. Raise before play, not during.")]
        [SerializeField] float maxDelaySeconds = 3f;

        [Header("Pitch")]
        [Tooltip("1 leaves the voice alone. Below 1 drops it. Used to make an archived line sound " +
                 "like it is coming from something other than the person who said it.")]
        [Range(0.5f, 2f)]
        [SerializeField] float pitch = 1f;

        [Tooltip("Grain length for the pitch shifter. Short smears consonants, long flutters vowels. " +
                 "Around 60 ms is the usual compromise. Seconds.")]
        [Range(0.02f, 0.15f)]
        [SerializeField] float grainSeconds = 0.06f;

        [Header("Radio character (always on)")]
        [Tooltip("Everything below this is cut. A real handset gives up somewhere near 300 Hz.")]
        [SerializeField] float highPassHz = 320f;

        [Tooltip("Everything above this is cut. This is what makes speech sound like it came out " +
                 "of a small speaker.")]
        [SerializeField] float lowPassHz = 3000f;

        [Tooltip("Soft clipping. Flattens the difference between a whisper and a shout, the way " +
                 "a compressed radio link does.")]
        [Range(1f, 12f)]
        [SerializeField] float drive = 3.5f;

        [Tooltip("Makes up the level lost to clipping and filtering.")]
        [Range(0f, 4f)]
        [SerializeField] float outputGain = 1.6f;

        [Header("Interference (scaled by Interference)")]
        [Tooltip("Noise floor at full interference.")]
        [Range(0f, 0.5f)]
        [SerializeField] float hiss;

        [Tooltip("Extra samples each one is held for at full interference. Digital break-up.")]
        [Range(0, 16)]
        [SerializeField] int crush = 5;

        [Tooltip("Chance per audio block of the signal cutting out, at full interference.")]
        [Range(0f, 0.5f)]
        [SerializeField] float dropoutChance = 0.05f;

        [SerializeField] Vector2 dropoutSeconds = new Vector2(0.04f, 0.22f);

        [Tooltip("Seconds for a change in Interference to take effect. Instant changes click.")]
        [SerializeField] float interferenceSmoothing = 0.35f;

        [Header("Archive")]
        [Tooltip("Optional. Records the partner's real voice as it arrives, before any of the " +
                 "above touches it, so it can be handed back to them later.")]
        [SerializeField] VoiceArchive archive;

        /// <summary>
        /// How badly the link is broken, 0 to 1. Driven by <see cref="HeadsetLink"/> from the
        /// distance between the two players, but anything may write it — a scripted moment, a
        /// floor the signal does not reach.
        /// </summary>
        public float Interference { get; set; }

        /// <summary>Seconds of delay. Settable at runtime, up to the ceiling set before play.</summary>
        public float DelaySeconds
        {
            get => delaySeconds;
            set => delaySeconds = Mathf.Clamp(value, 0f, maxDelaySeconds);
        }

        /// <summary>Playback rate. 1 is untouched.</summary>
        public float Pitch
        {
            get => pitch;
            set => pitch = Mathf.Clamp(value, 0.5f, 2f);
        }

        /// <summary>
        /// One clip queued for injection. Held as a single object so the audio thread can pick up
        /// the samples and the read position together, rather than catching one without the other.
        /// </summary>
        class Injection
        {
            public float[] Samples;
            public int Head;
        }

        volatile Injection _injection;

        // Everything below belongs to the audio thread once Awake has finished with it.
        float[] _ring;
        int _write;
        float[] _mono;

        int _sampleRate;
        int _grainSamples;
        float _phaseA, _phaseB;
        float _posA, _posB;

        float _hp1X, _hp1Y, _hp2X, _hp2Y;
        float _lp1, _lp2;
        float _hpCoef, _lpCoef;

        float _held;
        int _holdLeft;

        float _gate = 1f, _gateTarget = 1f;
        int _dropoutLeft;

        float _smoothed;
        uint _rng = 0x9E3779B9u;

        void Awake()
        {
            _sampleRate = AudioSettings.outputSampleRate;

            _grainSamples = Mathf.Max(64, Mathf.RoundToInt(grainSeconds * _sampleRate));

            // Room for the longest delay, plus a grain, plus the slack a grain reading backwards
            // at half speed can open up before it is reset.
            var length = Mathf.CeilToInt(maxDelaySeconds * _sampleRate) + _grainSamples * 3 + 1;
            _ring = new float[length];

            // Generous, so the common case never reallocates on the audio thread.
            _mono = new float[AudioSettings.GetConfiguration().dspBufferSize * 4];

            _phaseB = _grainSamples * 0.5f;

            var hpRc = 1f / (2f * Mathf.PI * Mathf.Max(1f, highPassHz));
            var lpRc = 1f / (2f * Mathf.PI * Mathf.Max(1f, lowPassHz));
            var dt = 1f / _sampleRate;
            _hpCoef = hpRc / (hpRc + dt);
            _lpCoef = dt / (lpRc + dt);
        }

        /// <summary>
        /// Plays mono samples through this headset as though they had just come over the air.
        /// Used by <see cref="VoiceArchive"/> to hand a partner their own voice back: because the
        /// clip takes the same path as a live transmission, it arrives with the same hiss and the
        /// same crushing, and there is nothing in the sound to tell the two apart.
        /// </summary>
        public void Inject(float[] monoSamples)
        {
            if (monoSamples == null || monoSamples.Length == 0)
                return;

            _injection = new Injection { Samples = monoSamples, Head = 0 };
        }

        /// <summary>True while an injected clip is still playing.</summary>
        public bool IsInjecting => _injection != null;

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (_ring == null || channels <= 0)
                return;

            var frames = data.Length / channels;

            // Per block rather than per sample: cheap, and neither is fast enough to hear.
            var blockSeconds = (float)frames / _sampleRate;
            var smoothing = interferenceSmoothing <= 0f
                ? 1f
                : Mathf.Clamp01(blockSeconds / interferenceSmoothing);
            _smoothed += (Mathf.Clamp01(Interference) - _smoothed) * smoothing;

            UpdateDropout(frames);

            if (_mono.Length < frames)
                _mono = new float[frames];

            var injection = _injection;
            var delaySamples = Mathf.Clamp(delaySeconds, 0f, maxDelaySeconds) * _sampleRate;
            var shifting = Mathf.Abs(pitch - 1f) > 0.001f;
            var hissNow = hiss * _smoothed;
            var crushNow = Mathf.RoundToInt(crush * _smoothed);
            var gateCoef = Mathf.Clamp01(120f / _sampleRate);

            for (var i = 0; i < frames; i++)
            {
                var input = 0f;
                for (var c = 0; c < channels; c++)
                    input += data[i * channels + c];
                input /= channels;

                if (injection != null)
                {
                    if (injection.Head < injection.Samples.Length)
                        input += injection.Samples[injection.Head++];
                    else
                        injection = _injection = null;
                }

                // The archive stores what was really said, not what the headset made of it.
                _mono[i] = input;

                _ring[_write] = input;
                _write = _write + 1 == _ring.Length ? 0 : _write + 1;

                var v = shifting ? ReadGrains(delaySamples) : ReadAt(_write - 1 - delaySamples);

                // Two poles each side. One is too gentle to read as a small speaker.
                v = HighPass(v);
                if (hissNow > 0f)
                    v += (NextFloat() * 2f - 1f) * hissNow;
                v = SoftClip(v * drive);
                v = LowPass(v);

                if (crushNow > 0)
                {
                    if (_holdLeft-- <= 0)
                    {
                        _held = v;
                        _holdLeft = crushNow;
                    }

                    v = _held;
                }

                _gate += (_gateTarget - _gate) * gateCoef;
                v *= _gate * outputGain;

                for (var c = 0; c < channels; c++)
                    data[i * channels + c] = v;
            }

            if (archive != null)
                archive.Write(_mono, frames);
        }

        /// <summary>
        /// Reads the delay line at two points at once, each running at <see cref="pitch"/> while
        /// the write head runs at 1, and crossfades between them. Left alone a read head running
        /// at a different rate walks into the write head and tears; restarting each one every grain
        /// keeps the drift bounded to the length of a grain. The two windows are triangles half a
        /// grain out of phase, which sum to exactly 1, so nothing pumps.
        /// </summary>
        float ReadGrains(float delaySamples)
        {
            var grain = _grainSamples;
            var start = _write - 1 - delaySamples;

            _posA += pitch;
            _posB += pitch;

            if (++_phaseA >= grain)
            {
                _phaseA = 0f;
                _posA = start;
            }

            if (++_phaseB >= grain)
            {
                _phaseB = 0f;
                _posB = start;
            }

            var wA = 1f - Mathf.Abs(2f * _phaseA / grain - 1f);
            var wB = 1f - Mathf.Abs(2f * _phaseB / grain - 1f);

            return ReadAt(_posA) * wA + ReadAt(_posB) * wB;
        }

        /// <summary>Linear interpolation into the ring, position counted in samples from its start.</summary>
        float ReadAt(float position)
        {
            var length = _ring.Length;

            position %= length;
            if (position < 0f)
                position += length;

            var index = (int)position;
            var frac = position - index;
            var next = index + 1 == length ? 0 : index + 1;

            return _ring[index] * (1f - frac) + _ring[next] * frac;
        }

        float HighPass(float x)
        {
            _hp1Y = _hpCoef * (_hp1Y + x - _hp1X);
            _hp1X = x;

            _hp2Y = _hpCoef * (_hp2Y + _hp1Y - _hp2X);
            _hp2X = _hp1Y;

            return _hp2Y;
        }

        float LowPass(float x)
        {
            _lp1 += (x - _lp1) * _lpCoef;
            _lp2 += (_lp1 - _lp2) * _lpCoef;
            return _lp2;
        }

        /// <summary>
        /// A rational stand-in for tanh. Same shape, none of the cost, and this runs tens of
        /// thousands of times a second.
        /// </summary>
        static float SoftClip(float x)
        {
            if (x < -3f) return -1f;
            if (x > 3f) return 1f;

            return x * (27f + x * x) / (27f + 9f * x * x);
        }

        void UpdateDropout(int frames)
        {
            if (_dropoutLeft > 0)
            {
                _dropoutLeft -= frames;
                if (_dropoutLeft <= 0)
                    _gateTarget = 1f;

                return;
            }

            if (_smoothed <= 0f || NextFloat() > dropoutChance * _smoothed)
                return;

            var seconds = Mathf.Lerp(dropoutSeconds.x, dropoutSeconds.y, NextFloat());
            _dropoutLeft = Mathf.Max(1, Mathf.RoundToInt(seconds * _sampleRate));
            _gateTarget = 0f;
        }

        /// <summary>
        /// Xorshift, because the audio thread must not touch UnityEngine.Random and cannot afford
        /// the locking inside System.Random shared across threads.
        /// </summary>
        float NextFloat()
        {
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 16777215f;
        }
    }
}

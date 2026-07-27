using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Ashburn.Radio
{
    /// <summary>
    /// Keeps what the partner actually said, and gives it back later.
    ///
    /// The server under Ashburn never deleted a sound it removed; it filed the original away to
    /// study how people reacted to it. This is that, running in a player's headset. Every
    /// transmission a partner makes is kept for a few minutes, and a phrase can be handed back at
    /// a moment when they did not say it.
    ///
    /// It is the partner's real voice, so there is nothing wrong with it to notice — no impression,
    /// no synthesis, none of the tells that give a fake away. What makes it frightening is only
    /// that it arrives at the wrong time, which is the whole of what the experiment does. It also
    /// costs nothing: no model, no recording session, and the vocabulary is whatever the players
    /// happen to say to each other.
    ///
    /// Recording runs on the audio thread and replaying runs on the main thread, so the two are
    /// kept apart carefully. See the notes on <see cref="Write"/> and <see cref="TryReplay"/>.
    /// </summary>
    public class VoiceArchive : MonoBehaviour
    {
        [Header("Memory")]
        [Tooltip("How far back the archive reaches. Older audio is written over. Seconds.")]
        [SerializeField] float memorySeconds = 120f;

        [Tooltip("How many phrases to keep pointers to. The oldest is dropped past this.")]
        [SerializeField] int maxPhrases = 24;

        [Header("What counts as a phrase")]
        [Tooltip("Level above which the partner is taken to be speaking rather than not.")]
        [Range(0.001f, 0.2f)]
        [SerializeField] float speechThreshold = 0.02f;

        [Tooltip("Quiet for this long ends a phrase. Too short and it cuts between words. Seconds.")]
        [SerializeField] float silenceEndsPhrase = 0.4f;

        [Tooltip("Anything shorter is a cough or a knock, not something worth repeating. Seconds.")]
        [SerializeField] float minPhraseSeconds = 0.5f;

        [Tooltip("Anything longer is cut here. A phrase handed back should be a line, not a speech. Seconds.")]
        [SerializeField] float maxPhraseSeconds = 4f;

        [Header("Playback")]
        [Tooltip("Where a recovered phrase is played. Normally the same headset it was recorded from.")]
        [SerializeField] RadioDsp output;

        [Tooltip("Pitch to play a recovered phrase at. Slightly under 1 is enough to make a familiar " +
                 "voice sit wrong without making it unrecognisable. Restored afterwards.")]
        [Range(0.5f, 2f)]
        [SerializeField] float replayPitch = 0.94f;

        /// <summary>How many phrases are currently held.</summary>
        public int PhraseCount
        {
            get
            {
                lock (_phrases)
                    return _phrases.Count;
            }
        }

        /// <summary>One stored phrase, as a span of the running sample count.</summary>
        readonly struct Phrase
        {
            public readonly long Start;
            public readonly long End;

            public Phrase(long start, long end)
            {
                Start = start;
                End = end;
            }

            public int Length => (int)(End - Start);
        }

        readonly List<Phrase> _phrases = new List<Phrase>();

        float[] _buffer;
        int _sampleRate;

        // Total samples ever written. Monotonic, so a phrase can be checked against it to find out
        // whether the ring has come round and written over it yet.
        long _written;

        // Audio thread only.
        float _envelope;
        bool _speaking;
        long _phraseStart;
        int _silence;
        int _silenceSamples;
        int _minSamples;
        int _maxSamples;
        float _envelopeCoef;

        float _restorePitch;
        bool _pitchOverridden;

        void Awake()
        {
            _sampleRate = AudioSettings.outputSampleRate;
            _buffer = new float[Mathf.CeilToInt(memorySeconds * _sampleRate)];

            _silenceSamples = Mathf.RoundToInt(silenceEndsPhrase * _sampleRate);
            _minSamples = Mathf.RoundToInt(minPhraseSeconds * _sampleRate);
            _maxSamples = Mathf.RoundToInt(maxPhraseSeconds * _sampleRate);

            // Roughly a 20 ms follower: fast enough to catch the start of a word, slow enough not
            // to chatter between syllables.
            _envelopeCoef = Mathf.Clamp01(1f / (0.02f * _sampleRate));
        }

        void Update()
        {
            // Put the headset back once a recovered phrase has finished, so a real transmission
            // afterwards is not still pitched.
            if (!_pitchOverridden || output == null || output.IsInjecting)
                return;

            output.Pitch = _restorePitch;
            _pitchOverridden = false;
        }

        /// <summary>
        /// Files a block of the partner's voice. Called from the audio thread by
        /// <see cref="RadioDsp"/>, with the signal as it arrived and before the headset has done
        /// anything to it.
        ///
        /// The sample buffer is written without locking: it is only ever written here, and a reader
        /// checks afterwards whether the ring came round mid-read rather than holding it up. The
        /// short lock is on the phrase list alone, which is touched a handful of times a minute at
        /// the edges of speech.
        /// </summary>
        public void Write(float[] mono, int count)
        {
            if (_buffer == null)
                return;

            var length = _buffer.Length;

            for (var i = 0; i < count; i++)
            {
                var sample = mono[i];

                _buffer[(int)(_written % length)] = sample;
                _written++;

                var level = sample < 0f ? -sample : sample;
                _envelope += (level - _envelope) * _envelopeCoef;

                if (!_speaking)
                {
                    if (_envelope <= speechThreshold)
                        continue;

                    _speaking = true;
                    _phraseStart = _written;
                    _silence = 0;
                    continue;
                }

                _silence = _envelope > speechThreshold ? 0 : _silence + 1;

                if (_silence >= _silenceSamples)
                {
                    // Do not keep the trailing silence that ended it.
                    Close(_written - _silence);
                }
                else if (_written - _phraseStart >= _maxSamples)
                {
                    Close(_written);
                }
            }
        }

        void Close(long end)
        {
            _speaking = false;

            if (end - _phraseStart < _minSamples)
                return;

            lock (_phrases)
            {
                _phrases.Add(new Phrase(_phraseStart, end));
                if (_phrases.Count > maxPhrases)
                    _phrases.RemoveAt(0);
            }
        }

        /// <summary>
        /// Plays a phrase the partner said earlier back to this player, as though it had just come
        /// over the radio. Returns false when there is nothing worth playing yet.
        ///
        /// Call from the main thread. When to call it is deliberately not decided here — the
        /// experiment picks its moments, and in a session that means the host says when so that
        /// both players' headsets are lying to a plan rather than at random.
        /// </summary>
        public bool TryReplay()
        {
            if (output == null || output.IsInjecting)
                return false;

            if (!TryTakePhrase(out var samples))
                return false;

            if (!_pitchOverridden)
            {
                _restorePitch = output.Pitch;
                _pitchOverridden = true;
            }

            output.Pitch = replayPitch;
            output.Inject(samples);
            return true;
        }

        /// <summary>
        /// Lifts a random phrase out of the ring. The write head is read before and after copying:
        /// if it came far enough round in between to have overwritten what was being copied, the
        /// copy is torn and the phrase is dropped rather than played. Cheaper than making the audio
        /// thread wait, and a lost phrase costs nothing.
        /// </summary>
        bool TryTakePhrase(out float[] samples)
        {
            samples = null;

            if (_buffer == null)
                return false;

            Phrase phrase;
            int index;

            lock (_phrases)
            {
                if (_phrases.Count == 0)
                    return false;

                index = Random.Range(0, _phrases.Count);
                phrase = _phrases[index];
            }

            var length = _buffer.Length;
            var count = phrase.Length;

            if (count <= 0 || count > length)
                return false;

            var before = Interlocked.Read(ref _written);
            if (before - phrase.Start > length)
            {
                Forget(index, phrase);
                return false;
            }

            var copy = new float[count];
            for (var i = 0; i < count; i++)
                copy[i] = _buffer[(int)((phrase.Start + i) % length)];

            var after = Interlocked.Read(ref _written);
            if (after - phrase.Start > length)
            {
                Forget(index, phrase);
                return false;
            }

            samples = copy;
            return true;
        }

        void Forget(int index, Phrase phrase)
        {
            lock (_phrases)
            {
                // The list may have moved under us; only remove it if it is still the same one.
                if (index < _phrases.Count && _phrases[index].Start == phrase.Start)
                    _phrases.RemoveAt(index);
            }
        }
    }
}

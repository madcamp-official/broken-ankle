using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ashburn.Radio
{
    /// <summary>
    /// Feeds this machine's microphone back into its own headset, so the radio can be heard and
    /// tuned by one person with no network and no SDK.
    ///
    /// Not part of the game. It exists because <see cref="RadioDsp"/> and <see cref="VoiceArchive"/>
    /// are the parts worth getting right by ear — how far a voice has to be before it breaks up,
    /// how long a phrase should be before it is worth keeping — and waiting on two machines and a
    /// room connection to hear any of that would mean tuning it once, badly.
    ///
    /// Put it on the same object as a <see cref="RadioDsp"/>. Wear headphones: without them the
    /// microphone hears the speakers and the loop screams.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class RadioLoopback : MonoBehaviour
    {
        [Tooltip("Off by default so this never ships live in a scene by accident.")]
        [SerializeField] bool enableOnStart;

        [Tooltip("Empty uses the system default microphone.")]
        [SerializeField] string device;

        [Header("Keys")]
        [Tooltip("Stands in for push-to-talk. The microphone runs the whole time; this only unmutes it.")]
        [SerializeField] Key pushToTalk = Key.V;

        [Tooltip("Plays back a phrase the archive kept, the way the game will later.")]
        [SerializeField] Key replay = Key.B;

        [Tooltip("Drives Interference from the keyboard when there is no partner to walk away from.")]
        [SerializeField] Key moreInterference = Key.RightBracket;

        [SerializeField] Key lessInterference = Key.LeftBracket;

        [SerializeField] VoiceArchive archive;

        AudioSource _source;
        RadioDsp _dsp;
        string _device;

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            _dsp = GetComponent<RadioDsp>();

            // The headset is in the player's ears, not out in the level, and the loopback has no
            // partner standing anywhere to pan towards.
            _source.spatialBlend = 0f;
        }

        void Start()
        {
            if (enableOnStart)
                Begin();
        }

        /// <summary>Opens the microphone and starts the loop.</summary>
        public void Begin()
        {
            if (Microphone.devices.Length == 0)
            {
                Debug.LogWarning($"{nameof(RadioLoopback)}: no microphone found.", this);
                return;
            }

            _device = string.IsNullOrEmpty(device) ? Microphone.devices[0] : device;
            _source.clip = Microphone.Start(_device, true, 1, AudioSettings.outputSampleRate);
            _source.loop = true;
            _source.mute = true;

            StartCoroutine(PlayWhenReady());
        }

        /// <summary>
        /// Waits for the recording head to leave zero before playing. Spinning on it is the usual
        /// way and it usually returns at once, but a microphone that never starts would hang the
        /// editor, and this costs a frame or two instead.
        /// </summary>
        IEnumerator PlayWhenReady()
        {
            var deadline = Time.realtimeSinceStartup + 2f;

            while (Microphone.GetPosition(_device) <= 0)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Debug.LogWarning($"{nameof(RadioLoopback)}: '{_device}' never started. " +
                                     "Check Windows microphone privacy settings.", this);
                    yield break;
                }

                yield return null;
            }

            _source.Play();

            Debug.Log($"{nameof(RadioLoopback)}: listening on '{_device}'. Hold {pushToTalk} to talk, " +
                      $"{replay} to replay, {lessInterference}/{moreInterference} for range.", this);
        }

        void OnDisable()
        {
            if (!string.IsNullOrEmpty(_device))
                Microphone.End(_device);
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || _source.clip == null)
                return;

            _source.mute = !keyboard[pushToTalk].isPressed;

            if (_dsp != null)
            {
                if (keyboard[moreInterference].isPressed)
                    _dsp.Interference = Mathf.Clamp01(_dsp.Interference + Time.deltaTime);

                if (keyboard[lessInterference].isPressed)
                    _dsp.Interference = Mathf.Clamp01(_dsp.Interference - Time.deltaTime);
            }

            if (archive != null && keyboard[replay].wasPressedThisFrame && !archive.TryReplay())
                Debug.Log($"{nameof(RadioLoopback)}: nothing in the archive yet. Say something first.", this);
        }
    }
}

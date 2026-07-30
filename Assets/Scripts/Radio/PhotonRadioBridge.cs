using Ashburn.Player;
using Photon.Voice.PUN;
using Photon.Voice.Unity;
using UnityEngine;

namespace Ashburn.Radio
{
    /// <summary>
    /// Hands the open channel to Photon Voice, and nothing else.
    ///
    /// This is deliberately the only file in the radio that knows a voice SDK exists. What the
    /// headset does to a voice is decided on the listening machine by <see cref="RadioDsp"/>, so
    /// what goes over the wire is the player's real voice and the transport stays replaceable.
    ///
    /// Sits on the local character alongside <see cref="RadioTransmitter"/>. The receiving side
    /// needs no code: PhotonVoiceView spawns a Speaker on the partner, and a <see cref="RadioDsp"/>
    /// on that Speaker's object filters it on the way out.
    /// </summary>
    [RequireComponent(typeof(RadioTransmitter))]
    public class PhotonRadioBridge : MonoBehaviour
    {
        [Tooltip("The Recorder this character transmits through. Usually the one PhotonVoiceView " +
                 "is using; left empty it is looked for on this object.")]
        [SerializeField] Recorder recorder;

        RadioTransmitter _transmitter;

        void Awake()
        {
            _transmitter = GetComponent<RadioTransmitter>();

            if (recorder == null)
                recorder = GetComponent<Recorder>();

            if (recorder == null)
                Debug.LogError($"{nameof(PhotonRadioBridge)} on '{name}' has no Recorder.", this);
        }

        void OnEnable()
        {
            _transmitter.Transmitting += Apply;

            // The microphone runs the whole time so there is no delay opening the channel, but
            // nothing leaves the machine until the key is down.
            Apply(_transmitter.IsTransmitting);
        }

        void OnDisable()
        {
            _transmitter.Transmitting -= Apply;
            Apply(false);
            _transmitter.IsReceiving = false;
        }

        void Apply(bool transmitting)
        {
            if (recorder != null)
                recorder.TransmitEnabled = transmitting;
        }

        /// <summary>
        /// Tells the handset when somebody else's voice is actually coming out of it.
        ///
        /// Read from the speaker rather than from a flag sent alongside the player's position.
        /// Photon Voice already knows whether audio is arriving and playing, and asking it costs no
        /// bandwidth and no version bump — a bool added to the position packet would have broken
        /// every client that had not updated, for something the voice layer was already tracking.
        ///
        /// Any partner speaking counts. With two players there is only one, and with more the
        /// handset is no quieter for the second voice.
        /// </summary>
        void Update()
        {
            var receiving = false;

            foreach (var rig in PlayerRig.All)
            {
                if (rig == null || rig.IsViewer)
                    continue;

                var view = rig.GetComponent<PhotonVoiceView>();
                if (view == null || !view.IsSpeaking)
                    continue;

                receiving = true;
                break;
            }

            _transmitter.IsReceiving = receiving;
        }

        void OnDestroy()
        {
            // Nothing is going to clear it once this is gone, and a handset left believing it is
            // playing somebody's voice would go on announcing itself in silence.
            if (_transmitter != null)
                _transmitter.IsReceiving = false;
        }
    }
}

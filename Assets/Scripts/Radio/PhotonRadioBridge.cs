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
        }

        void Apply(bool transmitting)
        {
            if (recorder != null)
                recorder.TransmitEnabled = transmitting;
        }
    }
}

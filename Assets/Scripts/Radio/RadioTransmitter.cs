using System;
using Ashburn.Noise;
using Ashburn.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ashburn.Radio
{
    /// <summary>
    /// The handset. Held down, it opens the channel; let go, it closes.
    ///
    /// Push-to-talk rather than an open microphone because talking has to be a decision. The thing
    /// hunting the players was built to find whoever is making noise and is told to weigh speech
    /// above footsteps, so a key that is costly to press is the point of the mechanic, not a
    /// concession to bandwidth. Every second of transmission goes onto the <see cref="NoiseBus"/>
    /// at a range between walking and running: saying something is about as loud as being seen.
    ///
    /// Knows nothing about Photon. It announces that the channel is open and something else
    /// decides what that means — a voice SDK, a test loopback, or nothing at all.
    /// </summary>
    public class RadioTransmitter : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("Drag Assets/InputSystem_Actions here. The Player/PushToTalk action is read from it.")]
        [SerializeField] InputActionAsset inputActions;

        [Header("Noise")]
        [Tooltip("How far a voice on the radio carries, in world units. Between a walk and a run: " +
                 "quieter than being chased, louder than being careful.")]
        [SerializeField] float range = 11f;

        [Tooltip("Seconds between noise events while the channel is open.")]
        [SerializeField] float interval = 0.35f;

        [Tooltip("Whether holding the key is heard at all. Off for a handset with a dead battery.")]
        [SerializeField] bool makesNoise = true;

        /// <summary>Raised when the channel opens and again when it closes.</summary>
        public event Action<bool> Transmitting;

        /// <summary>True while the key is held.</summary>
        public bool IsTransmitting { get; private set; }

        InputAction _pushToTalk;
        InputActionAsset _ownedActions;
        PlayerRig _rig;
        float _nextNoiseTime;

        void Awake()
        {
            _rig = GetComponent<PlayerRig>();

            if (inputActions == null)
            {
                Debug.LogError($"{nameof(RadioTransmitter)} on '{name}' has no Input Actions asset assigned.", this);
                return;
            }

            // Private copy per character, for the same reason as the other input readers: actions
            // belong to the asset, so a partner switching this component off would close the
            // viewer's channel with it.
            inputActions = _ownedActions = Instantiate(inputActions);

            _pushToTalk = inputActions.FindAction("Player/PushToTalk", throwIfNotFound: false);
            if (_pushToTalk == null)
                Debug.LogError("Could not find the 'Player/PushToTalk' action in the assigned asset.", this);
        }

        void OnDestroy()
        {
            if (_ownedActions != null)
                Destroy(_ownedActions);
        }

        void OnEnable()
        {
            if (_pushToTalk == null)
                return;

            _pushToTalk.started += OnPressed;
            _pushToTalk.canceled += OnReleased;
            _pushToTalk.Enable();
        }

        void OnDisable()
        {
            if (_pushToTalk != null)
            {
                _pushToTalk.started -= OnPressed;
                _pushToTalk.canceled -= OnReleased;
                _pushToTalk.Disable();
            }

            // A component switched off mid-sentence must not leave the channel open behind it.
            if (IsTransmitting)
                Apply(false);
        }

        void OnPressed(InputAction.CallbackContext _) => Apply(true);

        void OnReleased(InputAction.CallbackContext _) => Apply(false);

        /// <summary>Opens or closes the channel from somewhere other than the key.</summary>
        public void Apply(bool on)
        {
            if (IsTransmitting == on)
                return;

            IsTransmitting = on;

            // The first word out of the handset should be heard, not wait out a timer.
            if (on)
                _nextNoiseTime = 0f;

            Transmitting?.Invoke(on);
        }

        void Update()
        {
            if (!IsTransmitting || !makesNoise || Time.time < _nextNoiseTime)
                return;

            _nextNoiseTime = Time.time + interval;

            // Read per emission rather than cached: a spawner may settle which character this is
            // after Awake has already run.
            var kind = _rig != null && !_rig.IsViewer ? NoiseKind.Ally : NoiseKind.Self;
            NoiseBus.Emit(transform.position, range, kind);
        }
    }
}

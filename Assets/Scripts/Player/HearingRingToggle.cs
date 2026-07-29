using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ashburn.Player
{
    /// <summary>
    /// Switches the ring of hearing on and off.
    ///
    /// The ring used to be a passenger on the flashlight, on the theory that sound is only worth
    /// drawing while the beam is showing a single narrow wedge. In play that is exactly backwards:
    /// the moment the light goes out is the moment the player most needs to know which direction
    /// something is coming from, and killing both with one key left them with nothing at all.
    ///
    /// So the two are separate switches now. What the player gives up by running dark is the beam,
    /// and whether to also give up the ring is their own call.
    /// </summary>
    public class HearingRingToggle : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("Drag Assets/InputSystem_Actions here. The Player/ToggleHearing action is read from it.")]
        [SerializeField] InputActionAsset inputActions;

        [Header("Targets")]
        [Tooltip("The ring. Switched on and off by itself, independently of the flashlight.")]
        [SerializeField] GameObject hearingRing;

        [SerializeField] bool startOn = true;

        /// <summary>Raised whenever the ring is switched, with its new state.</summary>
        public event Action<bool> Switched;

        public bool IsOn { get; private set; }

        InputAction _toggleAction;
        InputActionAsset _ownedActions;

        void Awake()
        {
            // Here rather than in Start, because Start does not run on a component that is switched
            // off — which this one is on every character but the viewer's. Anything asking a
            // partner whether their headset is on would otherwise be told no, whatever startOn
            // says, and a partner would stand there with their headset apparently in their hand.
            IsOn = startOn;

            if (inputActions == null)
            {
                Debug.LogError($"{nameof(HearingRingToggle)} on '{name}' has no Input Actions asset assigned.", this);
                return;
            }

            // Private copy per character, for the same reason as every other input reader here:
            // actions belong to the asset, so this component switching off on a partner would
            // disable the shared action and take the viewer's key with it.
            inputActions = _ownedActions = Instantiate(inputActions);

            _toggleAction = inputActions.FindAction("Player/ToggleHearing", throwIfNotFound: false);
            if (_toggleAction == null)
                Debug.LogError("Could not find the 'Player/ToggleHearing' action in the assigned asset.", this);
        }

        void OnDestroy()
        {
            if (_ownedActions != null)
                Destroy(_ownedActions);
        }

        void Start() => Apply(startOn);

        void OnEnable()
        {
            if (_toggleAction == null)
                return;

            _toggleAction.performed += OnToggle;
            _toggleAction.Enable();
        }

        void OnDisable()
        {
            if (_toggleAction == null)
                return;

            _toggleAction.performed -= OnToggle;
            _toggleAction.Disable();
        }

        void OnToggle(InputAction.CallbackContext _) => Apply(!IsOn);

        /// <summary>Lets other systems kill the ring — deafened by a blast, a scripted moment.</summary>
        public void Apply(bool on)
        {
            IsOn = on;

            if (hearingRing != null)
                hearingRing.SetActive(on);

            Switched?.Invoke(on);
        }
    }
}

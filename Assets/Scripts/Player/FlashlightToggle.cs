using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ashburn.Player
{
    /// <summary>
    /// Switches the flashlight on and off, and tells anything that cares.
    ///
    /// The switch is what puts the player into the game's core state: running dark, seeing nothing,
    /// and giving away nothing. It used to carry the hearing ring with it, which meant going dark
    /// also blinded the player to sound — see <see cref="HearingRingToggle"/>, which owns the ring
    /// on its own key now.
    /// </summary>
    public class FlashlightToggle : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("Drag Assets/InputSystem_Actions here. The Player/ToggleFlashlight action is read from it.")]
        [SerializeField] InputActionAsset inputActions;

        [Header("Targets")]
        [Tooltip("The beam. Switched off with the flashlight.")]
        [SerializeField] GameObject beam;

        [SerializeField] bool startOn = true;

        /// <summary>Raised whenever the light is switched, with its new state.</summary>
        public event Action<bool> Switched;

        public bool IsOn { get; private set; }

        InputAction _toggleAction;
        InputActionAsset _ownedActions;

        void Awake()
        {
            if (inputActions == null)
            {
                Debug.LogError($"{nameof(FlashlightToggle)} on '{name}' has no Input Actions asset assigned.", this);
                return;
            }

            // Private copy per character. Actions belong to the asset, so this component switching
            // off — which it does on every character that is not the viewer — would otherwise
            // disable the shared action and take the viewer's flashlight key with it.
            inputActions = _ownedActions = Instantiate(inputActions);

            _toggleAction = inputActions.FindAction("Player/ToggleFlashlight", throwIfNotFound: false);
            if (_toggleAction == null)
                Debug.LogError("Could not find the 'Player/ToggleFlashlight' action in the assigned asset.", this);
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

        /// <summary>Lets other systems kill the light — a dead battery, a scripted moment.</summary>
        public void Apply(bool on)
        {
            IsOn = on;

            if (beam != null)
                beam.SetActive(on);

            Switched?.Invoke(on);
        }
    }
}

using System;
using UnityEngine;

namespace Ashburn.World
{
    /// <summary>
    /// Whether the building has power, and what that looks like.
    ///
    /// The breaker used to do this itself, through a UnityEvent holding three SetActive calls
    /// pointed at lights in the scene. That worked exactly once: a UnityEvent cannot reference a
    /// scene object from a prefab, so the moment the breaker became a prefab the wiring was gone,
    /// and it went silently — an unlit room looks the same as a room whose switch is broken.
    ///
    /// Power is also world state rather than an event, which is what Multiplayer.MD section 6 asks
    /// for: a client joining later has to be told the lights are already on, and an event that has
    /// already happened cannot tell them. Holding it here as a value, with a change notification on
    /// the side, is the shape that survives being networked.
    /// </summary>
    public class PowerGrid : MonoBehaviour
    {
        [Tooltip("Lit while the power is on. The main lights, working lamps.")]
        [SerializeField] GameObject[] whenPowered;

        [Tooltip("Lit while the power is off. The dim global light the building starts under.")]
        [SerializeField] GameObject[] whenDark;

        [Tooltip("Whether the building starts with power. Off is the normal opening.")]
        [SerializeField] bool startPowered;

        /// <summary>
        /// The grid in the current scene. Found this way so a prop can throw the switch without
        /// holding a reference to it, which is what let the breaker ship as a prefab.
        /// </summary>
        public static PowerGrid Current { get; private set; }

        /// <summary>True while the building has power.</summary>
        public bool IsPowered { get; private set; }

        /// <summary>Raised whenever the power changes, with its new state.</summary>
        public event Action<bool> Changed;

        void Awake()
        {
            if (Current != null && Current != this)
                Debug.LogWarning($"A second {nameof(PowerGrid)} on '{name}'. The last one wins.", this);

            Current = this;
        }

        void Start() => Apply(startPowered);

        void OnDestroy()
        {
            if (Current == this)
                Current = null;
        }

        /// <summary>Switches the power. Safe to call with the state it is already in.</summary>
        public void SetPowered(bool powered)
        {
            if (IsPowered == powered)
                return;

            Apply(powered);
        }

        /// <summary>For a UnityEvent or a button that only ever turns it on.</summary>
        public void TurnOn() => SetPowered(true);

        void Apply(bool powered)
        {
            IsPowered = powered;

            foreach (var go in whenPowered)
                if (go != null)
                    go.SetActive(powered);

            foreach (var go in whenDark)
                if (go != null)
                    go.SetActive(!powered);

            Changed?.Invoke(powered);
        }

        // A static outlives a play session when the editor skips its domain reload, which would
        // leave the next run pointing at a grid from the previous one.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad() => Current = null;
    }
}

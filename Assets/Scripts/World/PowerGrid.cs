using System;
using System.Collections.Generic;
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
        /// The grid belonging to one map.
        ///
        /// Per map rather than one for the game: several maps are loaded at once, and the house
        /// having its power back has nothing to do with the street outside. It was a singleton
        /// while only one map could exist, and the day a second one loaded the two would have
        /// fought over which was <c>Current</c> — silently, since a dark map and a map whose switch
        /// went to the wrong building look the same.
        ///
        /// Found by map rather than held as a reference so a prop can throw the switch without
        /// knowing anything, which is what let the breaker ship as a prefab.
        /// </summary>
        public static PowerGrid For(MapZone zone)
        {
            if (zone == null)
                return null;

            _grids.TryGetValue(zone, out var grid);
            return grid;
        }

        /// <summary>The grid of the map this object is in, or null.</summary>
        public static PowerGrid Of(Component component) => For(MapZone.Of(component));

        static readonly Dictionary<MapZone, PowerGrid> _grids = new();

        MapZone _zone;

        /// <summary>True while the building has power.</summary>
        public bool IsPowered { get; private set; }

        /// <summary>Raised whenever the power changes, with its new state.</summary>
        public event Action<bool> Changed;

        void Awake()
        {
            _zone = MapZone.Of(this);
            if (_zone == null)
            {
                Debug.LogError($"{nameof(PowerGrid)} on '{name}' is outside every " +
                               $"{nameof(MapZone)}, so no breaker can find it.", this);
                return;
            }

            if (_grids.TryGetValue(_zone, out var existing) && existing != this)
                Debug.LogWarning($"A second {nameof(PowerGrid)} in map '{_zone.Id}'. The last " +
                                 "one wins.", this);

            _grids[_zone] = this;
        }

        void Start() => Apply(startPowered);

        void OnDestroy()
        {
            if (_zone != null && _grids.TryGetValue(_zone, out var registered) && registered == this)
                _grids.Remove(_zone);
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
        // leave the next run pointing at grids from the previous one.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad() => _grids.Clear();
    }
}

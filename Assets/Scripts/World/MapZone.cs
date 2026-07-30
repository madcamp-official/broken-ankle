using System.Collections.Generic;
using UnityEngine;

namespace Ashburn.World
{
    /// <summary>
    /// One map, and everything in it.
    ///
    /// A map is a scene, and several of them are loaded at once — the players can be in different
    /// ones, and the game has to hold both. Two scenes authored around the origin would land on top
    /// of each other, so every map claims a slot on load and moves itself there. Authoring stays at
    /// the origin, which is what you want when opening a map on its own to work on it.
    ///
    /// Everything belonging to the map must sit under this object. Anything left outside stays at
    /// the origin while the map walks off to its slot, which looks exactly like the map having a
    /// hole in it.
    ///
    /// The slot is claimed in Awake, ahead of everything else in the scene, so no component ever
    /// reads a position that is about to change underneath it.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public class MapZone : MonoBehaviour
    {
        /// <summary>The map id of something that does not belong to any map.</summary>
        public const int Unzoned = -1;

        [Tooltip("What doors ask for. Leave empty to use the scene's name, which is what a door " +
                 "names anyway.")]
        [SerializeField] string id;

        [Tooltip("World units between one map's slot and the next. Must comfortably exceed the " +
                 "largest map, or two of them overlap and sight lines run from one into the other.")]
        [SerializeField] float slotSpacing = 1000f;

        static readonly List<MapZone> _loaded = new();
        static readonly List<bool> _slotsTaken = new();

        int _slot = -1;

        /// <summary>The name this map is known by. Matches the scene it lives in.</summary>
        public string Id => string.IsNullOrEmpty(id) ? gameObject.scene.name : id;

        /// <summary>
        /// Identifies the map to systems that must not reach across one — the noise bus above all.
        /// Only meaningful while the map is loaded; slots are reused once it is gone.
        ///
        /// Local to this machine. It is the slot, and two clients claim slots in the order they
        /// happened to open maps in, so it must never cross the wire. Use <see cref="SharedId"/>.
        /// </summary>
        public int MapId => _slot;

        /// <summary>
        /// A number for this map that means the same thing on every machine.
        ///
        /// The slot cannot do this job. It is claimed in load order, and two players who walked
        /// into the same house by different routes give it different numbers — which is exactly how
        /// a partner ends up standing a thousand units away with nothing reporting an error.
        /// Derived from the name instead, which both machines got from the same commit.
        /// </summary>
        public int SharedId => SharedIdOf(Id);

        /// <summary>Where this map's origin sits in world space, which is its slot.</summary>
        public Vector2 Origin => transform.position;

        /// <summary>
        /// The shared number for a map name.
        ///
        /// FNV-1a rather than <c>string.GetHashCode</c>, which is not promised to be stable between
        /// runs or platforms, or the scene's build index, which changes the day somebody reorders
        /// the build list and would silently move every player.
        /// </summary>
        public static int SharedIdOf(string mapName)
        {
            if (string.IsNullOrEmpty(mapName))
                return 0;

            unchecked
            {
                const uint offset = 2166136261;
                const uint prime = 16777619;

                var hash = offset;
                foreach (var c in mapName)
                {
                    hash ^= c;
                    hash *= prime;
                }

                return (int)hash;
            }
        }

        /// <summary>The loaded map with this shared number, or null when it is not open here.</summary>
        public static MapZone FindShared(int sharedId)
        {
            foreach (var zone in _loaded)
                if (zone != null && zone.SharedId == sharedId)
                    return zone;

            return null;
        }

        /// <summary>Every map currently in memory.</summary>
        public static IReadOnlyList<MapZone> Loaded => _loaded;

        /// <summary>The map an object belongs to, or null if it belongs to none.</summary>
        public static MapZone Of(Component component) =>
            component == null ? null : component.GetComponentInParent<MapZone>(true);

        /// <summary>
        /// The map to tag something this object does with — a sound, above all.
        ///
        /// A character answers from the map it walked into, which is not the map it was built in;
        /// anything that cannot move answers from the map it is part of. Asked for at the moment it
        /// is needed rather than cached, because for a character the answer changes.
        /// </summary>
        public static int IdOf(Component component)
        {
            if (component == null)
                return Unzoned;

            var presence = component.GetComponentInParent<MapPresence>();
            if (presence != null)
                return presence.MapId;

            var zone = Of(component);
            return zone != null ? zone.MapId : Unzoned;
        }

        /// <summary>The loaded map with this name, or null.</summary>
        public static MapZone Find(string mapId)
        {
            if (string.IsNullOrEmpty(mapId))
                return null;

            foreach (var zone in _loaded)
                if (zone != null && zone.Id == mapId)
                    return zone;

            return null;
        }

        void Awake()
        {
            if (transform.parent != null)
                Debug.LogWarning($"{nameof(MapZone)} on '{name}' is not a root object. It moves " +
                                 "itself to its slot, and a parent will drag it somewhere else.", this);

            _slot = ClaimSlot();
            _loaded.Add(this);

            // Along one axis rather than a grid: only the maps the players are standing in stay
            // loaded, so this counts in single figures and never runs far enough out for the
            // precision of a float to start showing.
            transform.position = new Vector3(_slot * slotSpacing, 0f, 0f);
        }

        void OnDestroy()
        {
            _loaded.Remove(this);

            if (_slot >= 0 && _slot < _slotsTaken.Count)
                _slotsTaken[_slot] = false;

            _slot = Unzoned;
        }

        static int ClaimSlot()
        {
            for (var i = 0; i < _slotsTaken.Count; i++)
            {
                if (_slotsTaken[i])
                    continue;

                _slotsTaken[i] = true;
                return i;
            }

            _slotsTaken.Add(true);
            return _slotsTaken.Count - 1;
        }

        // Statics outlive a play session when the editor skips its domain reload, and a slot table
        // left full from the last run would push the first map of this one out into nowhere.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad()
        {
            _loaded.Clear();
            _slotsTaken.Clear();
        }
    }
}

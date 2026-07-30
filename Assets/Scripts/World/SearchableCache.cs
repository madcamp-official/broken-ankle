using System;
using Ashburn.Cutscenes;
using Ashburn.Interaction;
using Ashburn.Noise;
using Ashburn.Player;
using Photon.Pun;
using UnityEngine;

namespace Ashburn.World
{
    /// <summary>
    /// A row of places one thing is hidden in, and the searching that finds it.
    ///
    /// The hospital's record room is shelves with the labels gone: the chart the pair came for is
    /// in one of them and there is no way to tell which from outside. Everything here exists to
    /// make that a few seconds of looking rather than a puzzle — a wrong shelf costs a line of
    /// dialogue and the noise of pulling a drawer out, which in this building is not nothing.
    ///
    /// One component on a parent, with a child per shelf. Each child is given a
    /// <see cref="Spot"/> at load, so building another cache is copying empty objects with
    /// colliders under a new parent rather than filling in an index by hand on each of them — an
    /// index that would silently point two shelves at the same slot the first time somebody
    /// duplicated one.
    /// </summary>
    public class SearchableCache : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Unique within the whole game. It seeds which shelf holds the thing, and it is " +
                 "what remembers the cache has been emptied. 'hospital_records'.")]
        [SerializeField] string id;

        [Header("Prompts")]
        [SerializeField] string prompt = "뒤져 본다";

        [Header("Dialogue")]
        [Tooltip("Played by the shelf that has it.")]
        [SerializeField] string foundEventId;

        [Tooltip("Played by a shelf that does not, one after another so the same line does not " +
                 "come back twice in a row. Leave empty for a cache that says nothing when it misses.")]
        [SerializeField] string[] emptyEventIds;

        [Header("World state")]
        [Tooltip("Raised when the thing is found. This is what the rest of the story reads.")]
        [SerializeField] string raiseFlagOnFound;

        [Header("Who may search")]
        [Tooltip("-1 for either of them, 0 for Nathan, 1 for Grant.")]
        [SerializeField] int requiredSlot = PlayerRole.Anyone;

        [Header("Noise")]
        [Tooltip("How far pulling a drawer out carries. Searching a room is not a quiet thing to do.")]
        [SerializeField] float noiseRange = 6f;

        Transform[] _spots;
        int _holder = -1;
        int _misses;

        string FoundFlag => "cache:" + CacheId;
        string CacheId => string.IsNullOrEmpty(id) ? name : id;

        /// <summary>Whether the thing has already been dug out, by either of them.</summary>
        public bool IsEmptied => WorldState.Has(FoundFlag);

        void Awake()
        {
            var count = transform.childCount;
            if (count == 0)
            {
                Debug.LogError($"{nameof(SearchableCache)} '{name}' has no shelves under it, so " +
                               "there is nowhere for anything to be.", this);
                return;
            }

            _spots = new Transform[count];
            for (var i = 0; i < count; i++)
            {
                _spots[i] = transform.GetChild(i);
                var spot = _spots[i].gameObject.AddComponent<Spot>();
                spot.Bind(this, i);
            }
        }

        /// <summary>
        /// Which shelf has it, decided once and then remembered.
        ///
        /// Off the room's name rather than off <see cref="UnityEngine.Random"/>, so both machines
        /// land on the same shelf without a message passing between them. A pick published by the
        /// host would be a race against the players walking in, and a pick made independently on
        /// each machine would put the chart in two different drawers — the partner would find it
        /// again in a shelf this one had already reported empty.
        ///
        /// Asked for late, at the first search, because a map can be open before the room is
        /// joined and a pick made then would be seeded on nothing.
        /// </summary>
        int Holder
        {
            get
            {
                if (_holder < 0)
                    _holder = Mathf.Abs(StableHash(CacheId) ^ SessionSeed) % _spots.Length;

                return _holder;
            }
        }

        static int SessionSeed
        {
            get
            {
                if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
                    return StableHash(PhotonNetwork.CurrentRoom.Name);

                return OfflineSeed;
            }
        }

        // Fixed for the length of one offline run so a shelf does not change its mind between two
        // searches, and different between runs so the room is worth walking twice. Both characters
        // in the split-keyboard test are in this process, so they agree.
        static int _offlineSeed;

        static int OfflineSeed
        {
            get
            {
                if (_offlineSeed == 0)
                    _offlineSeed = UnityEngine.Random.Range(1, int.MaxValue);

                return _offlineSeed;
            }
        }

        // Not string.GetHashCode: that one is deliberately randomised per process in modern .NET,
        // which is the one property this must not have. The two machines have to agree.
        static int StableHash(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            unchecked
            {
                var hash = 23;
                foreach (var c in value)
                    hash = hash * 31 + c;

                return hash;
            }
        }

        // Targetable by the wrong one of the two on purpose, so they are told whose job it is
        // instead of walking into a shelf that gives no prompt at all. Interact refuses.
        bool CanTarget() => !IsEmptied && !DialogueManager.IsPlaying;

        bool CanSearch(GameObject interactor) =>
            CanTarget() && PlayerRole.Matches(interactor, requiredSlot);

        string PromptFor(GameObject interactor) =>
            PlayerRole.Matches(interactor, requiredSlot) ? prompt : PlayerRole.Refusal(requiredSlot);

        void Search(int index, GameObject interactor, Vector3 at)
        {
            if (!CanSearch(interactor))
                return;

            NoiseBus.Emit(at, noiseRange, NoiseKind.Self, MapZone.IdOf(this));

            var manager = DialogueManager.Ensure();
            var map = MapZone.IdOf(this);

            if (index == Holder)
            {
                if (!string.IsNullOrEmpty(foundEventId))
                    manager.TryPlay(foundEventId, lockInput: true, emitNoise: false, noiseRange: 0f,
                                    at, map, raiseFlagOnComplete: null);

                // Raised here rather than handed to the dialogue as its completion flag, because
                // the dialogue is allowed to refuse. A beat already seen, or one the progression is
                // not ready for, would otherwise leave the chart in a drawer that has just told the
                // player it is empty and will not open again.
                WorldState.Raise(raiseFlagOnFound);
                WorldState.Raise(FoundFlag);
                return;
            }

            if (emptyEventIds == null || emptyEventIds.Length == 0)
                return;

            var line = emptyEventIds[_misses % emptyEventIds.Length];
            _misses++;

            manager.TryPlay(line, lockInput: true, emitNoise: false, noiseRange: 0f, at, map,
                            raiseFlagOnComplete: null);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.55f, 0.8f, 1f, 0.8f);

            for (var i = 0; i < transform.childCount; i++)
                Gizmos.DrawWireCube(transform.GetChild(i).position, new Vector3(0.9f, 0.9f, 0f));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad() => _offlineSeed = 0;

        /// <summary>
        /// One shelf. Added by its parent at load rather than placed by hand, so the shelves cannot
        /// disagree with each other about which index they are.
        /// </summary>
        [RequireComponent(typeof(Collider2D))]
        public class Spot : MonoBehaviour, IInteractable
        {
            SearchableCache _cache;
            int _index;

            // Whoever is standing here, remembered from the last CanInteract so Prompt can name
            // the right person. IInteractable.Prompt takes no argument, and the alternative is a
            // shelf that tells Grant it is Nathan's job by staying silent. PlayerInteractor asks
            // every frame, so this is never stale for the player actually reading it; in the
            // offline split-keyboard test two characters can overwrite each other's answer within
            // a frame, which costs a wrong name on a prompt and nothing else.
            GameObject _asking;

            internal void Bind(SearchableCache cache, int index)
            {
                _cache = cache;
                _index = index;
            }

            public string Prompt => _cache != null ? _cache.PromptFor(_asking) : string.Empty;

            public bool CanInteract(GameObject interactor)
            {
                _asking = interactor;
                return _cache != null && _cache.CanTarget();
            }

            public void Interact(GameObject interactor) =>
                _cache?.Search(_index, interactor, transform.position);
        }
    }
}

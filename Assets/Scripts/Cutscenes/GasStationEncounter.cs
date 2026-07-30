using Ashburn.Interaction;
using Ashburn.Monster;
using Ashburn.Player;
using Ashburn.World;
using UnityEngine;

namespace Ashburn.Cutscenes
{
    /// <summary>
    /// Runs the gas-station sound-lure tutorial on the village map.
    ///
    /// The station is part of Village Map rather than a separate scene, so its story objects are
    /// authored at runtime beside the bone tile. This keeps the hand-painted map untouched while
    /// still giving the encounter real world positions and a real interactable reward.
    /// </summary>
    public sealed class GasStationEncounter : MonoBehaviour
    {
        const string FirstSeenFlag = "dialogue:gas_first_monster_001";
        const string LurePlanFlag = "dialogue:gas_lure_plan_001";

        MonsterAI _monster;
        MapZone _zone;
        Vector2 _bonePosition;

        public void Configure(MonsterAI monster, MapZone zone, Vector2 bonePosition)
        {
            _monster = monster;
            _zone = zone;
            _bonePosition = bonePosition;

            if (GetComponentInChildren<GasKeycardPickup>(true) != null)
                return;

            var pickupObject = new GameObject("KEY_gas_station_sentil_card");
            pickupObject.transform.SetParent(transform, worldPositionStays: false);
            pickupObject.transform.position = bonePosition;

            var trigger = pickupObject.AddComponent<CircleCollider2D>();
            trigger.isTrigger = true;
            trigger.radius = 0.65f;

            pickupObject.AddComponent<GasKeycardPickup>().Configure(monster);
        }

        void Update()
        {
            if (_monster == null || _zone == null ||
                !WorldState.Has(StoryProgression.PoliceInvestigationComplete) ||
                WorldState.Has(StoryProgression.GasStationComplete) ||
                DialogueManager.IsPlaying)
            {
                return;
            }

            var player = NearestControlledPlayer();
            if (player == null || Vector2.Distance(player.transform.position, _bonePosition) > 10f)
                return;

            if (!WorldState.Has(FirstSeenFlag))
            {
                if (Play("gas_first_monster_001", emitNoise: true, noiseRange: 8f))
                {
                    WorldState.Raise(FirstSeenFlag);
                    WorldState.Raise("GasMonsterReleased");
                }
                return;
            }

            if (!WorldState.Has(LurePlanFlag) &&
                Play("gas_lure_plan_001", emitNoise: false, noiseRange: 0f))
            {
                WorldState.Raise(LurePlanFlag);
            }
        }

        PlayerRig NearestControlledPlayer()
        {
            PlayerRig nearest = null;
            var best = float.MaxValue;

            foreach (var rig in PlayerRig.All)
            {
                if (rig == null || !rig.IsControlled)
                    continue;

                var presence = rig.GetComponent<MapPresence>();
                if (presence == null || presence.Zone != _zone)
                    continue;

                var distance = Vector2.SqrMagnitude(
                    (Vector2)rig.transform.position - _bonePosition);
                if (distance >= best)
                    continue;

                best = distance;
                nearest = rig;
            }

            return nearest;
        }

        bool Play(string eventId, bool emitNoise, float noiseRange)
        {
            return DialogueManager.Ensure().TryPlay(
                eventId,
                lockInput: true,
                emitNoise: emitNoise,
                noiseRange: noiseRange,
                noisePosition: _bonePosition,
                map: _zone.MapId,
                raiseFlagOnComplete: null);
        }
    }

    /// <summary>Keycard hidden beside the gas-station bone and protected by the nearby warden.</summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class GasKeycardPickup : MonoBehaviour, IInteractable
    {
        const string LurePlanFlag = "dialogue:gas_lure_plan_001";
        const string UsedFlag = "interact:gas_keycard_001";
        const float SafeDistance = 5f;

        static Sprite _glintSprite;

        MonsterAI _monster;
        GameObject _asking;
        Transform _glint;

        public string Prompt
        {
            get
            {
                if (!PlayerRole.Matches(_asking, PlayerRole.Nathan))
                    return PlayerRole.Refusal(PlayerRole.Nathan);

                return MonsterIsClear()
                    ? "뼈 옆의 센틸 키카드를 집는다"
                    : "소음 관리자를 반대편으로 유인해야 한다";
            }
        }

        public void Configure(MonsterAI monster)
        {
            _monster = monster;
            CreateGlint();
        }

        public bool CanInteract(GameObject interactor)
        {
            _asking = interactor;
            return !DialogueManager.IsPlaying &&
                   WorldState.Has(LurePlanFlag) &&
                   !WorldState.Has(UsedFlag);
        }

        public void Interact(GameObject interactor)
        {
            if (!CanInteract(interactor) ||
                !PlayerRole.Matches(interactor, PlayerRole.Nathan) ||
                !MonsterIsClear())
            {
                return;
            }

            var manager = DialogueManager.Ensure();
            if (!manager.TryPlay(
                    "gas_keycard_001",
                    lockInput: true,
                    emitNoise: false,
                    noiseRange: 0f,
                    noisePosition: transform.position,
                    map: MapZone.IdOf(this),
                    raiseFlagOnComplete: "HasSentilKeycard"))
            {
                return;
            }

            WorldState.Raise(UsedFlag);
            WorldState.Raise(WorldState.KeyFlag("SentilKeycard"));
            WorldState.Raise(WorldState.CarryFlag(PlayerRole.Nathan, "SentilKeycard"));

            if (_glint != null)
                _glint.gameObject.SetActive(false);

            var trigger = GetComponent<Collider2D>();
            if (trigger != null)
                trigger.enabled = false;
        }

        void Update()
        {
            if (_glint == null)
                return;

            var pulse = 1f + Mathf.Sin(Time.time * 5f) * 0.18f;
            _glint.localScale = Vector3.one * pulse;
        }

        bool MonsterIsClear()
        {
            return _monster == null ||
                   Vector2.Distance(_monster.transform.position, transform.position) >= SafeDistance;
        }

        void CreateGlint()
        {
            var visual = new GameObject("Keycard_Glint");
            visual.transform.SetParent(transform, worldPositionStays: false);
            visual.transform.localPosition = new Vector3(0f, 0.25f, 0f);

            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = GlintSprite();
            renderer.color = new Color(1f, 0.82f, 0.18f, 0.95f);
            if (SortingLayer.NameToID("Object") != 0)
                renderer.sortingLayerName = "Object";
            renderer.sortingOrder = 50;

            _glint = visual.transform;
        }

        static Sprite GlintSprite()
        {
            if (_glintSprite != null)
                return _glintSprite;

            var texture = new Texture2D(5, 5, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var clear = new Color(0f, 0f, 0f, 0f);
            var pixels = new Color[25];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = clear;

            var gold = Color.white;
            pixels[2] = gold;
            pixels[7] = gold;
            pixels[10] = gold;
            pixels[11] = gold;
            pixels[12] = gold;
            pixels[13] = gold;
            pixels[14] = gold;
            pixels[17] = gold;
            pixels[22] = gold;
            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            _glintSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 5f, 5f),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 5f);
            _glintSprite.hideFlags = HideFlags.HideAndDontSave;
            return _glintSprite;
        }
    }
}

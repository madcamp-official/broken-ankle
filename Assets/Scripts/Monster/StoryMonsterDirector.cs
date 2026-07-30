using System.Collections;
using System.Collections.Generic;
using Ashburn.Cutscenes;
using Ashburn.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Ashburn.Monster
{
    /// <summary>Places authored story monsters whenever their additive map is loaded.</summary>
    public sealed class StoryMonsterDirector : MonoBehaviour
    {
        [SerializeField] GameObject monsterPrefab;

        readonly struct Placement
        {
            public Placement(
                string scene,
                string name,
                Vector2 localPosition,
                string activationFlag = null,
                string deactivationFlag = "TruthDevicePlayed",
                string spawnFlag = null,
                string protectionStartFlag = null,
                string protectionEndFlag = null)
            {
                Scene = scene;
                Name = name;
                LocalPosition = localPosition;
                ActivationFlag = activationFlag;
                DeactivationFlag = deactivationFlag;
                SpawnFlag = spawnFlag;
                ProtectionStartFlag = protectionStartFlag;
                ProtectionEndFlag = protectionEndFlag;
            }

            public string Scene { get; }
            public string Name { get; }
            public Vector2 LocalPosition { get; }
            public string ActivationFlag { get; }
            public string DeactivationFlag { get; }
            public string SpawnFlag { get; }
            public string ProtectionStartFlag { get; }
            public string ProtectionEndFlag { get; }
        }

        static readonly Placement[] Placements =
        {
            new(
                "Corp_Lobby",
                "Warden_Corp_FirstEncounter",
                new Vector2(18f, 10.8f),
                activationFlag: "Story:CorpFirstWardenAwake",
                deactivationFlag: "Story:CorpFirstWardenEscaped",
                protectionStartFlag: "Story:CorpFirstWardenAwake",
                protectionEndFlag: "Story:CorpFirstWardenEscaped"),

            new(
                "Village Map",
                "Warden_GasStation_Bone",
                new Vector2(72.5f, 8f),
                activationFlag: "GasMonsterReleased",
                deactivationFlag: "TruthDevicePlayed",
                spawnFlag: StoryProgression.PoliceInvestigationComplete),

            new("Greybox_Hanger", "Warden_Hangar_A1", new Vector2(-13f, 5.5f),
                StoryProgression.HangarComplete),
            new("Greybox_Hanger", "Warden_Hangar_A2", new Vector2(-8f, 5.5f),
                StoryProgression.HangarComplete),
            new("Greybox_Hanger", "Warden_Hangar_A3", new Vector2(-3f, 5.5f),
                StoryProgression.HangarComplete),
            new("Greybox_Hanger", "Warden_Hangar_A4", new Vector2(2f, 5.5f),
                StoryProgression.HangarComplete),
            new("Greybox_Hanger", "Warden_Hangar_A5", new Vector2(7f, 5.5f),
                StoryProgression.HangarComplete),
            new("Greybox_Hanger", "Warden_Hangar_A6", new Vector2(12f, 5.5f),
                StoryProgression.HangarComplete),
            new("Greybox_Hanger", "Warden_Hangar_B1", new Vector2(-13f, 1.5f),
                StoryProgression.HangarComplete),
            new("Greybox_Hanger", "Warden_Hangar_B2", new Vector2(-8f, 1.5f),
                StoryProgression.HangarComplete),
            new("Greybox_Hanger", "Warden_Hangar_B3", new Vector2(-3f, 1.5f),
                StoryProgression.HangarComplete),
            new("Greybox_Hanger", "Warden_Hangar_B4", new Vector2(2f, 1.5f),
                StoryProgression.HangarComplete),
            new("Greybox_Hanger", "Warden_Hangar_B5", new Vector2(7f, 1.5f),
                StoryProgression.HangarComplete),
            new("Greybox_Hanger", "Warden_Hangar_B6", new Vector2(12f, 1.5f),
                StoryProgression.HangarComplete),

            new("Greybox_CityHall_2F", "Warden_CityHall_01", new Vector2(-15f, 7f),
                StoryProgression.HangarComplete),
            new("Greybox_CityHall_2F", "Warden_CityHall_02", new Vector2(-5f, 10f),
                StoryProgression.HangarComplete),
            new("Greybox_CityHall_2F", "Warden_CityHall_03", new Vector2(0f, 3f),
                StoryProgression.HangarComplete),
            new("Greybox_CityHall_2F", "Warden_CityHall_04", new Vector2(0f, -3f),
                StoryProgression.HangarComplete),
            new("Greybox_CityHall_2F", "Warden_CityHall_05", new Vector2(-5f, -10f),
                StoryProgression.HangarComplete),
            new("Greybox_CityHall_2F", "Warden_CityHall_06", new Vector2(5f, -10f),
                StoryProgression.HangarComplete),
        };

        static readonly Vector2[] ClearPositionOffsets =
        {
            Vector2.zero,
            new(0.75f, 0f),
            new(-0.75f, 0f),
            new(0f, 0.75f),
            new(0f, -0.75f),
            new(1.25f, 0f),
            new(-1.25f, 0f),
            new(0f, 1.25f),
            new(0f, -1.25f),
        };

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            WorldState.Set += OnWorldFlagSet;
            SpawnForLoadedScenes();
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            WorldState.Set -= OnWorldFlagSet;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => SpawnFor(scene);

        void OnWorldFlagSet(string _) => SpawnForLoadedScenes();

        void SpawnForLoadedScenes()
        {
            for (var index = 0; index < SceneManager.sceneCount; index++)
                SpawnFor(SceneManager.GetSceneAt(index));
        }

        void SpawnFor(Scene scene)
        {
            if (!scene.isLoaded || monsterPrefab == null)
                return;

            var zone = FindZone(scene);
            if (zone == null)
                return;

            foreach (var placement in Placements)
            {
                if (placement.Scene != scene.name ||
                    Contains(scene, placement.Name) ||
                    (!string.IsNullOrEmpty(placement.SpawnFlag) &&
                     !WorldState.Has(placement.SpawnFlag)))
                {
                    continue;
                }

                var monster = Instantiate(monsterPrefab, zone.transform, false);
                monster.name = placement.Name;
                monster.transform.position = FindClearPosition(
                    zone.transform.TransformPoint(placement.LocalPosition));

                var ai = monster.GetComponent<MonsterAI>();
                if (ai != null)
                {
                    ai.ConfigureStoryState(
                        placement.ActivationFlag,
                        placement.DeactivationFlag);
                    ai.RefreshAfterPlacement();
                }

                monster.GetComponent<MonsterStrike>()?.ConfigureStoryProtection(
                    placement.ProtectionStartFlag,
                    placement.ProtectionEndFlag);
            }

            if (scene.name != "Village Map")
                return;

            EnsureGasStationEncounter(scene, zone);

            if (WorldState.Has(StoryProgression.HangarComplete) &&
                !Contains(scene, "Warden_Village_Crowd"))
            {
                StartCoroutine(SpawnVillageCrowd(scene, zone));
            }
        }

        void EnsureGasStationEncounter(Scene scene, MapZone zone)
        {
            if (Contains(scene, "EVENT_GasStationEncounter"))
                return;

            var monster = FindMonster(scene, "Warden_GasStation_Bone");
            if (monster == null)
                return;

            var host = new GameObject("EVENT_GasStationEncounter");
            host.transform.SetParent(zone.transform, worldPositionStays: false);
            host.AddComponent<GasStationEncounter>().Configure(
                monster,
                zone,
                zone.transform.TransformPoint(new Vector2(72.5f, 8f)));
        }

        IEnumerator SpawnVillageCrowd(Scene scene, MapZone zone)
        {
            var parent = new GameObject("Warden_Village_Crowd");
            parent.transform.SetParent(zone.transform, worldPositionStays: false);

            // Village maps keep their visual/collision children asleep while nobody is in them.
            // Sampling before they wake would treat every building as open road.
            while (scene.isLoaded && zone != null && !RoadGeometryReady(zone))
                yield return null;

            if (!scene.isLoaded || zone == null)
                yield break;

            Physics2D.SyncTransforms();
            var candidates = RoadCandidates(zone);
            candidates.Sort((a, b) => PositionHash(a).CompareTo(PositionHash(b)));

            const int wanted = 36;
            const float spacing = 3.25f;
            var placed = new List<Vector2>(wanted);

            foreach (var position in candidates)
            {
                if (placed.Count >= wanted)
                    break;

                if (Blocked(position) || TooClose(position, placed, spacing))
                    continue;

                var monster = Instantiate(monsterPrefab, parent.transform, false);
                monster.name = $"Warden_Village_{placed.Count + 1:00}";
                monster.transform.position = position;

                var ai = monster.GetComponent<MonsterAI>();
                if (ai != null)
                {
                    ai.ConfigureStoryState(null, "TruthDevicePlayed");
                    ai.ConfigureCrowdMode(placed.Count);
                    ai.RefreshAfterPlacement();
                }

                placed.Add(position);

                // Instantiation, animator setup and rigidbody registration are spread out so the
                // first frame back from the hangar does not pay for all 36 at once.
                if (placed.Count % 2 == 0)
                    yield return null;
            }

            if (placed.Count < wanted)
            {
                Debug.LogWarning(
                    $"Village crowd placed {placed.Count}/{wanted} wardens on clear road cells.",
                    zone);
            }
        }

        static bool RoadGeometryReady(MapZone zone)
        {
            foreach (var tilemap in zone.GetComponentsInChildren<Tilemap>(true))
                if (IsRoad(tilemap) && tilemap.isActiveAndEnabled)
                    return true;

            return false;
        }

        static List<Vector2> RoadCandidates(MapZone zone)
        {
            var candidates = new List<Vector2>();
            var seen = new HashSet<Vector2Int>();

            foreach (var tilemap in zone.GetComponentsInChildren<Tilemap>(true))
            {
                if (!IsRoad(tilemap))
                    continue;

                foreach (var cell in tilemap.cellBounds.allPositionsWithin)
                {
                    if (!tilemap.HasTile(cell))
                        continue;

                    var world = (Vector2)tilemap.GetCellCenterWorld(cell);
                    var key = new Vector2Int(
                        Mathf.RoundToInt(world.x * 2f),
                        Mathf.RoundToInt(world.y * 2f));

                    if (seen.Add(key))
                        candidates.Add(world);
                }
            }

            return candidates;
        }

        static bool IsRoad(Tilemap tilemap)
        {
            return tilemap != null &&
                   (tilemap.name.Contains("Road") || tilemap.name.Contains("Asphalt"));
        }

        static int PositionHash(Vector2 position)
        {
            unchecked
            {
                var x = Mathf.RoundToInt(position.x * 2f);
                var y = Mathf.RoundToInt(position.y * 2f);
                return (x * 73856093) ^ (y * 19349663);
            }
        }

        static bool TooClose(Vector2 position, List<Vector2> placed, float spacing)
        {
            var minimum = spacing * spacing;
            foreach (var other in placed)
                if ((other - position).sqrMagnitude < minimum)
                    return true;

            return false;
        }

        static MapZone FindZone(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var zone = root.GetComponent<MapZone>();
                if (zone != null)
                    return zone;
            }

            return null;
        }

        static bool Contains(Scene scene, string objectName)
        {
            foreach (var root in scene.GetRootGameObjects())
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == objectName)
                    return true;

            return false;
        }

        static MonsterAI FindMonster(Scene scene, string objectName)
        {
            foreach (var root in scene.GetRootGameObjects())
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == objectName)
                    return child.GetComponent<MonsterAI>();

            return null;
        }

        static Vector2 FindClearPosition(Vector2 intended)
        {
            foreach (var offset in ClearPositionOffsets)
            {
                var candidate = intended + offset;
                if (!Blocked(candidate))
                    return candidate;
            }

            Debug.LogWarning(
                $"No completely clear story-monster position near {intended}; using authored point.");
            return intended;
        }

        static bool Blocked(Vector2 point)
        {
            foreach (var hit in Physics2D.OverlapCircleAll(point, 0.4f))
            {
                if (hit != null && hit.enabled && !hit.isTrigger)
                    return true;
            }

            return false;
        }
    }
}

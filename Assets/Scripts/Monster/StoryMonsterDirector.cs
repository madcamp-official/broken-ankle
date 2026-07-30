using Ashburn.Cutscenes;
using Ashburn.World;
using UnityEngine;
using UnityEngine.SceneManagement;

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
                string deactivationFlag = "TruthDevicePlayed")
            {
                Scene = scene;
                Name = name;
                LocalPosition = localPosition;
                ActivationFlag = activationFlag;
                DeactivationFlag = deactivationFlag;
            }

            public string Scene { get; }
            public string Name { get; }
            public Vector2 LocalPosition { get; }
            public string ActivationFlag { get; }
            public string DeactivationFlag { get; }
        }

        static readonly Placement[] Placements =
        {
            new(
                "Corp_Lobby",
                "Warden_Corp_FirstEncounter",
                new Vector2(18f, 10.8f),
                "Story:CorpFirstWardenAwake",
                "Story:CorpFirstWardenEscaped"),

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
            SpawnForLoadedScenes();
        }

        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => SpawnFor(scene);

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
                if (placement.Scene != scene.name || Contains(scene, placement.Name))
                    continue;

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
            }
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

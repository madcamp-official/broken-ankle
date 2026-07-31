using System.Collections.Generic;
using Ashburn.Core;
using Ashburn.Interaction;
using Ashburn.Player;
using Ashburn.World;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Ashburn.EditorTools
{
    /// <summary>
    /// Turns a hand-built scene into a map the game can load.
    ///
    /// The greybox builder does this as a side effect of generating a level, which is no use to a
    /// scene somebody drew by hand: building over it is exactly what must not happen. This does the
    /// same conversion and touches nothing that is already there.
    ///
    /// What a map needs, and what a scene made before the maps were split does not have: one root
    /// carrying a <see cref="MapZone"/> with everything under it, no camera or spawner of its own,
    /// a light that stops at the map's edge instead of covering the whole game, somewhere to stand,
    /// and an entry for a door in another map to aim at.
    /// </summary>
    public static class MapSceneSetup
    {
        const string SystemsScenePath = "Assets/Scenes/Systems.unity";

        /// <summary>
        /// Physics layer for colliders that stop movement but not sight. The player's Blockers
        /// mask must leave it out, which it does: the mask covers layers 0, 1, 2, 4 and 5.
        /// </summary>
        const string SightPassLayer = "SightPass";

        [MenuItem("Ashburn/Make This Scene A Map")]
        static void Convert()
        {
            var scene = SceneManager.GetActiveScene();

            if (scene.path == SystemsScenePath)
            {
                EditorUtility.DisplayDialog("That is the systems scene",
                    "The systems scene holds the camera and the spawner, not a map.", "OK");
                return;
            }

            if (Object.FindFirstObjectByType<MapZone>(FindObjectsInactive.Include) != null)
            {
                EditorUtility.DisplayDialog("Already a map",
                    $"'{scene.name}' already has a MapZone.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Make this scene a map?",
                    $"'{scene.name}' gets a map root with everything under it. Its camera, " +
                    "spawner and fade are removed — those live in the systems scene — and a " +
                    "global light becomes one that stops at this map's edge.\n\n" +
                    "Nothing you built is moved or deleted.", "Convert", "Cancel"))
                return;

            var report = Apply(scene);
            Debug.Log(report);
        }

        /// <summary>
        /// The conversion itself, with nothing to click.
        ///
        /// Split out from the menu item so a scene can be converted without a person present. The
        /// dialogs above are the whole difference: they are there to stop somebody running this on
        /// the systems scene by accident, which is a question about intent rather than a step of the
        /// work. Returns what it did, for whoever wants to log it.
        /// </summary>
        public static string Apply(Scene scene)
        {
            var removed = Strip();
            var entries = ConvertArrivalMarks();
            var doors = ConvertTransitions();
            var root = Adopt(scene);
            var bounds = Extent(root);
            var lights = LocaliseLights(root, bounds);
            EnsureMarks(root, bounds);
            EnsureInBuildList(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root.gameObject;

            return $"'{scene.name}' is a map now. Removed: " +
                   $"{(removed.Count > 0 ? string.Join(", ", removed) : "nothing")}. " +
                   $"Converted {doors} door(s) and {entries} arrival mark(s). " +
                   $"Localised {lights} light(s). Check the SpawnPoints and MapEntry under " +
                   $"{root.name} — they are at the map's centre until you move them.";
        }

        /// <summary>
        /// Drops a <see cref="MapEntry"/> on the selected object, which is how a door in another
        /// map is aimed at a particular spot in this one.
        ///
        /// Right-click the building you want people to arrive outside, and the mark lands on it —
        /// there is no other way to say "the front of the hospital" to a tool, and typing
        /// coordinates read off a sprite is how a door ends up opening into a wall.
        ///
        /// Its id starts as the object's name because that is nearly always what the door will ask
        /// for, and it is one field to correct when it is not.
        /// </summary>
        [MenuItem("GameObject/Ashburn/Map Entry Here", false, 10)]
        static void EntryHere(MenuCommand command)
        {
            var picked = command.context as GameObject ?? Selection.activeGameObject;
            if (picked == null)
                return;

            var zone = MapZone.Of(picked.transform);
            if (zone == null)
            {
                EditorUtility.DisplayDialog("Not in a map",
                    $"'{picked.name}' is not under a MapZone. Run Ashburn > Make This Scene A Map " +
                    "first, or an arrival here would have no map to arrive in.", "OK");
                return;
            }

            var entry = new GameObject("MapEntry");
            Undo.RegisterCreatedObjectUndo(entry, "Map Entry Here");
            entry.transform.SetParent(zone.transform, false);
            entry.transform.position = picked.transform.position;
            entry.AddComponent<MapEntry>();

            var serialized = new SerializedObject(entry.GetComponent<MapEntry>());
            serialized.FindProperty("id").stringValue = picked.name;
            serialized.ApplyModifiedProperties();

            Selection.activeGameObject = entry;
            EditorSceneManager.MarkSceneDirty(picked.scene);

            Debug.Log($"Added a MapEntry called '{picked.name}' at {entry.transform.position}. " +
                      "Nudge it clear of the building, and set the door's Target Entry to match.",
                      entry);
        }

        /// <summary>
        /// Moves the selection onto the layer sight ignores.
        ///
        /// A collider does two jobs at once — it stops the player walking through something, and it
        /// stops them seeing past it — and until now every collider in a hand-built map did both,
        /// because they all sit on Default and Default is a blocker. Walk into a building's
        /// passage and the walls around it cut the beam to arm's length, which is correct for a
        /// wall and wrong for the archway you are standing under.
        ///
        /// The layer changes only what sight does. Movement is untouched: the physics matrix still
        /// has this layer colliding with the player, so a wall put here is still a wall to walk
        /// into, just not one to be blinded by.
        /// </summary>
        [MenuItem("GameObject/Ashburn/Sight Passes Through", false, 11)]
        static void SightPassesThrough(MenuCommand command)
        {
            // Unity runs this once per selected object. Do the whole selection on the first call.
            if (command.context != null && command.context != Selection.activeGameObject)
                return;

            var layer = LayerMask.NameToLayer(SightPassLayer);
            if (layer < 0)
            {
                EditorUtility.DisplayDialog("No SightPass layer",
                    $"Add a physics layer called '{SightPassLayer}' in Project Settings > Tags " +
                    "and Layers, then make sure the player's Blockers mask excludes it.", "OK");
                return;
            }

            var touched = 0;
            foreach (var go in Selection.gameObjects)
            {
                foreach (var child in go.GetComponentsInChildren<Transform>(true))
                {
                    if (child.gameObject.layer == layer)
                        continue;

                    Undo.RecordObject(child.gameObject, "Sight Passes Through");
                    child.gameObject.layer = layer;
                    touched++;
                }

                EditorSceneManager.MarkSceneDirty(go.scene);
            }

            Debug.Log($"{touched} object(s) moved to '{SightPassLayer}'. They still stop the " +
                      "player; they no longer stop the beam.");
        }

        /// <summary>Takes out everything that belongs to the systems scene.</summary>
        static List<string> Strip()
        {
            var removed = new List<string>();

            Take<PlayerSpawner>(removed);

            // A character dropped into the scene to walk around while building it. Left in, the map
            // brings its own second player along every time it is loaded: two viewers, two beams,
            // and two darkness masks stacked one over the other, so everything one of them can see
            // is still blacked out by the other.
            Take<PlayerRig>(removed);

            Take<ScreenFade>(removed);
            Take<RoomCamera>(removed);
            Take<CinemachineCamera>(removed);
            Take<CinemachineBrain>(removed);
            Take<Camera>(removed);
            Take<AudioListener>(removed);

            return removed;
        }

        static void Take<T>(List<string> removed) where T : Component
        {
            foreach (var found in Object.FindObjectsByType<T>(FindObjectsInactive.Include,
                                                              FindObjectsSortMode.None))
            {
                if (found == null)
                    continue;

                var host = found.gameObject;
                var target = PrefabUtility.IsPartOfPrefabInstance(host)
                    ? PrefabUtility.GetOutermostPrefabInstanceRoot(host)
                    : Root(host);

                removed.Add(target.name);
                Undo.DestroyObjectImmediate(target);
            }
        }

        static GameObject Root(GameObject go)
        {
            var t = go.transform;
            while (t.parent != null)
                t = t.parent;

            return t.gameObject;
        }

        /// <summary>
        /// Turns the markers a scene built before the split arrived at into <see cref="MapEntry"/>.
        ///
        /// Those scenes placed an empty called Spawn_FromSomewhere and had the departing side name
        /// it in a string, which is the same idea as an entry and reached it by <c>GameObject.Find</c>
        /// — a search of every loaded scene at once. With the maps side by side that finds whichever
        /// one loaded first, so the name has to become something scoped to a map, and the name itself
        /// is kept as the id so the doors pointing at it still line up.
        ///
        /// Objects carrying a <see cref="SpawnPoint"/> are left alone. Those say where a map starts,
        /// not where an arrival lands, and the two are different marks that happen to look alike.
        /// </summary>
        static int ConvertArrivalMarks()
        {
            var converted = 0;

            foreach (var scene in new[] { SceneManager.GetActiveScene() })
            foreach (var root in scene.GetRootGameObjects())
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var go = transform.gameObject;
                if (!go.name.StartsWith("Spawn_"))
                    continue;

                if (go.GetComponent<SpawnPoint>() != null || go.GetComponent<MapEntry>() != null)
                    continue;

                var entry = Undo.AddComponent<MapEntry>(go);
                var serialized = new SerializedObject(entry);
                serialized.FindProperty("id").stringValue = go.name;
                serialized.ApplyModifiedProperties();
                converted++;
            }

            return converted;
        }

        /// <summary>
        /// Replaces every <see cref="SceneTransition"/> with a <see cref="MapDoor"/>.
        ///
        /// They read as the same thing and are not. A transition loads its destination over the top
        /// of everything — <see cref="LoadSceneMode.Single"/>, then it deletes every other object
        /// tagged Player so the arrival is alone. In a game where two people walk around together
        /// that is not a door, it is the other player being destroyed because somebody used the
        /// stairs. A door hands the traveller to <see cref="MapTravel"/>, which loads the map
        /// alongside the one being left and leaves whoever stayed behind standing in it.
        ///
        /// Everything the transition knew survives: the same destination, the same arrival mark,
        /// the same prompt, and locked stays locked.
        /// </summary>
        static int ConvertTransitions()
        {
            var converted = 0;

            foreach (var old in Object.FindObjectsByType<SceneTransition>(FindObjectsInactive.Include,
                                                                         FindObjectsSortMode.None))
            {
                var from = new SerializedObject(old);
                var map = from.FindProperty("sceneName").stringValue;
                var arrival = from.FindProperty("destinationSpawnName").stringValue;
                var prompt = from.FindProperty("prompt").stringValue;
                var locked = from.FindProperty("locked").boolValue;
                var barrier = from.FindProperty("lockBarrier").objectReferenceValue;

                var host = old.gameObject;
                Undo.DestroyObjectImmediate(old);

                var door = Undo.AddComponent<MapDoor>(host);
                var to = new SerializedObject(door);
                to.FindProperty("targetMap").stringValue = map;
                to.FindProperty("targetEntry").stringValue =
                    string.IsNullOrWhiteSpace(arrival) ? "Default" : arrival;
                if (!string.IsNullOrWhiteSpace(prompt))
                    to.FindProperty("prompt").stringValue = prompt;
                to.FindProperty("locked").boolValue = locked;
                to.ApplyModifiedProperties();

                converted++;

                // A transition switched its barrier's collider on and off to match the lock. A door
                // has no such wire, so the barrier is now a plain collider somebody has to deal with
                // when the door is opened — worth saying out loud rather than leaving to be found by
                // walking into it.
                if (barrier != null)
                    Debug.LogWarning($"'{host.name}' had a lock barrier ({barrier.name}) that the " +
                                     "transition enabled and disabled for itself. MapDoor does not " +
                                     "do that. Drive it from whatever unlocks the door.", host);
            }

            return converted;
        }

        /// <summary>
        /// Puts one root over the whole map. Parenting keeps world positions, so the scene looks
        /// identical afterwards — the difference only shows at runtime, when the zone moves the
        /// whole map to its slot and anything left outside would have stayed behind.
        /// </summary>
        static MapZone Adopt(Scene scene)
        {
            var root = new GameObject(scene.name);
            Undo.RegisterCreatedObjectUndo(root, "Make Map");
            var zone = root.AddComponent<MapZone>();

            foreach (var go in scene.GetRootGameObjects())
                if (go != root)
                    Undo.SetTransformParent(go.transform, root.transform, "Make Map");

            return zone;
        }

        /// <summary>Points a light at every sorting layer there is.</summary>
        static void LightEveryLayer(Light2D light)
        {
            var serialized = new SerializedObject(light);
            var array = serialized.FindProperty("m_ApplyToSortingLayers");
            var layers = SortingLayer.layers;

            array.arraySize = layers.Length;
            for (var i = 0; i < layers.Length; i++)
                array.GetArrayElementAtIndex(i).intValue = layers[i].id;

            serialized.ApplyModifiedProperties();
        }

        /// <summary>Everything drawn in the map, in world space.</summary>
        static Bounds Extent(MapZone root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.one * 20f);

            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);

            return bounds;
        }

        /// <summary>
        /// Turns global lights into lights the size of this map, and hands them to a PowerGrid.
        ///
        /// A Global light in URP 2D means the entire scene, and with two maps loaded at once that
        /// is both of them: one map's lighting becomes the other's. A point light with no falloff
        /// inside its radius is a global light with an edge.
        /// </summary>
        static int LocaliseLights(MapZone root, Bounds bounds)
        {
            var radius = bounds.extents.magnitude + 4f;
            var localised = 0;
            GameObject dark = null;

            foreach (var light in root.GetComponentsInChildren<Light2D>(true))
            {
                if (light.lightType != Light2D.LightType.Global)
                    continue;

                Undo.RecordObject(light, "Make Map");
                light.lightType = Light2D.LightType.Point;
                light.pointLightInnerRadius = radius;
                light.pointLightOuterRadius = radius;
                light.pointLightInnerAngle = 360f;
                light.pointLightOuterAngle = 360f;
                light.falloffIntensity = 0f;
                light.transform.position = bounds.center;

                // A Light2D lights the sorting layers it is told to, and the default is Default
                // alone. The tilemaps are on Background, Floor, Wall and Object, so a light left
                // on the default reaches none of the map it is supposed to be lighting.
                LightEveryLayer(light);

                dark = light.gameObject;
                localised++;
            }

            if (root.GetComponentInChildren<PowerGrid>(true) != null || dark == null)
                return localised;

            // The light that was already on becomes the dark state, and a brighter copy of it
            // becomes what the breaker turns on. Whoever built the map chose the dim value.
            var lit = Object.Instantiate(dark, dark.transform.parent);
            lit.name = "Light_Map_PowerOn";
            var litLight = lit.GetComponent<Light2D>();
            litLight.intensity = Mathf.Max(litLight.intensity * 6f, 1f);
            lit.SetActive(false);

            dark.name = "Light_Map_Dark";

            var host = new GameObject("PowerGrid");
            host.transform.SetParent(root.transform, false);
            var grid = host.AddComponent<PowerGrid>();

            var serialized = new SerializedObject(grid);
            var powered = serialized.FindProperty("whenPowered");
            powered.arraySize = 1;
            powered.GetArrayElementAtIndex(0).objectReferenceValue = lit;

            var darkArray = serialized.FindProperty("whenDark");
            darkArray.arraySize = 1;
            darkArray.GetArrayElementAtIndex(0).objectReferenceValue = dark;
            serialized.ApplyModifiedProperties();

            return localised;
        }

        /// <summary>
        /// Gives the map somewhere to start and somewhere to arrive, if it has neither.
        ///
        /// Placed at the centre rather than guessed at: the middle of a map is usually walkable and
        /// always obvious, which is what you want from something you are meant to move.
        /// </summary>
        static void EnsureMarks(MapZone root, Bounds bounds)
        {
            if (root.GetComponentInChildren<SpawnPoint>(true) == null)
            {
                var holder = new GameObject("SpawnPoints");
                holder.transform.SetParent(root.transform, false);

                for (var i = 0; i < 2; i++)
                {
                    var point = new GameObject($"SpawnPoint {i + 1}");
                    point.transform.SetParent(holder.transform, false);
                    point.transform.position = bounds.center + Vector3.right * (i * 1.2f - 0.6f);
                    point.AddComponent<SpawnPoint>();
                }
            }

            if (root.GetComponentInChildren<MapEntry>(true) == null)
            {
                var entry = new GameObject("MapEntry");
                entry.transform.SetParent(root.transform, false);
                entry.transform.position = bounds.center + Vector3.down * 1.5f;
                entry.AddComponent<MapEntry>();
            }
        }

        static void EnsureInBuildList(Scene scene)
        {
            if (string.IsNullOrEmpty(scene.path))
            {
                Debug.LogWarning("Save the scene before making it a map, or it cannot be added to " +
                                 "the build's scene list and no door will be able to load it.");
                return;
            }

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(entry => entry.path == scene.path))
                return;

            scenes.Add(new EditorBuildSettingsScene(scene.path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}

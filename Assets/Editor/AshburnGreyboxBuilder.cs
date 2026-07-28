using System.Collections.Generic;
using System.IO;
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
    /// Builds the level geometry from <see cref="GreyboxSettings.layout"/>.
    ///
    /// A wall here is three components that all have to agree — a collider for movement, the same
    /// collider for the flashlight (<c>FlashlightOcclusion</c> raycasts rather than using URP's
    /// shadows, which cannot be combined with a light cookie), and a ShadowCaster2D for the lamps.
    /// Three rooms is on the order of twenty wall segments, and one of them silently missing a
    /// collider is a bug that looks like a lighting bug. So walls are never assembled here: they
    /// are copies of a template prefab that is known to be right.
    ///
    /// Runs of wall and floor are merged into rectangles before anything is created. A cell each
    /// would be about a thousand objects, a thousand colliders for the beam's ray fan to sort
    /// through every frame, and an unreadable hierarchy.
    /// </summary>
    class AshburnGreyboxBuilder : EditorWindow
    {
        const string SettingsPath = "Assets/Scenes/GreyboxMap.asset";
        const string TemplateFolder = "Assets/Prefabs/Level";

        GreyboxSettings _settings;
        SerializedObject _serialized;
        Vector2 _scroll;
        string _status;

        [MenuItem("Ashburn/Greybox Map Builder")]
        static void Open() => GetWindow<AshburnGreyboxBuilder>("Greybox").Show();

        void OnEnable() => Load();

        void Load()
        {
            _settings = AssetDatabase.LoadAssetAtPath<GreyboxSettings>(SettingsPath);
            if (_settings == null)
            {
                _settings = CreateInstance<GreyboxSettings>();
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                AssetDatabase.CreateAsset(_settings, SettingsPath);
                AssetDatabase.SaveAssets();
            }

            _serialized = _settings != null ? new SerializedObject(_settings) : null;
        }

        void OnGUI()
        {
            // A throw out of OnGUI takes the rest of the window with it — every button below the
            // line it failed on simply is not drawn, and a tool that has lost its settings looks
            // like a tool whose buttons were never written. Say so instead.
            if (_serialized == null || _settings == null)
                Load();

            if (_serialized == null || _settings == null)
            {
                EditorGUILayout.HelpBox(
                    $"Could not load {SettingsPath}. If it exists, its script reference is broken " +
                    "— select it and check the Inspector shows Greybox Settings.", MessageType.Error);

                if (GUILayout.Button("Try again"))
                    Load();

                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            _serialized.Update();

            var property = _serialized.GetIterator();
            property.NextVisible(true);
            while (property.NextVisible(false))
            {
                // Drawn as a dropdown of map names below, where it means something.
                if (property.name == "selected")
                    continue;

                EditorGUILayout.PropertyField(property, true);
            }

            _serialized.ApplyModifiedProperties();

            EditorGUILayout.Space();

            if (_settings.maps == null || _settings.maps.Length == 0)
            {
                EditorGUILayout.HelpBox("Add a map above before building.", MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            var names = new string[_settings.maps.Length];
            for (var i = 0; i < names.Length; i++)
                names[i] = string.IsNullOrEmpty(_settings.maps[i].name) ? $"Map {i}" : _settings.maps[i].name;

            var picked = EditorGUILayout.Popup("Build which map",
                Mathf.Clamp(_settings.selected, 0, names.Length - 1), names);

            if (picked != _settings.selected)
            {
                _settings.selected = picked;
                EditorUtility.SetDirty(_settings);
            }

            EditorGUILayout.Space();

            if (_settings.wall == null || _settings.floor == null)
            {
                EditorGUILayout.HelpBox(
                    "Wall and floor templates are required. Refresh Templates saves Wall_Top, " +
                    "Floor, Pillar_A, BreakerBox, Nest and Light_Lamp out of the open scene as " +
                    $"prefabs. Anything not in the scene falls back to {TemplateFolder}.",
                    MessageType.Info);
            }

            // Always offered, not only when templates are missing: re-running is how a changed wall,
            // a lamp that did not exist the first time, or a new component on the breaker gets
            // picked up. Objects missing from the scene keep whatever prefab is already on disk.
            if (GUILayout.Button("Refresh Templates", GUILayout.Height(24f)))
                CaptureTemplates();

            using (new EditorGUI.DisabledScope(_settings.wall == null || _settings.floor == null))
            {
                if (GUILayout.Button("Build Map", GUILayout.Height(28f)))
                    Build(_settings);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Maps hold nothing but the map. The spawner, the camera, the fade and the controls " +
                "overlay live in the systems scene, which is the one you press Play in — it opens " +
                "the starting map itself and loads the others as the players walk into them.",
                MessageType.None);

            if (GUILayout.Button("Build Systems Scene", GUILayout.Height(24f)))
                BuildSystemsScene(_settings);

            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, MessageType.None);

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Builds the scene the game is actually started from.
        ///
        /// It holds no level at all. Everything in it outlives every map — the camera the players
        /// are watched through, the fade that covers a map change, the spawner that decides who
        /// exists — and all of it used to be duplicated into every map scene, which was harmless
        /// only while exactly one map could be loaded.
        /// </summary>
        void BuildSystemsScene(GreyboxSettings s)
        {
            const string path = SystemsScenePath;

            if (File.Exists(path) &&
                !EditorUtility.DisplayDialog("Rebuild the systems scene?",
                    $"{path} already exists and will be replaced. Anything you added to it by hand " +
                    "is lost.", "Replace", "Cancel"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            if (s.cameraRig != null)
            {
                var rig = (GameObject)PrefabUtility.InstantiatePrefab(s.cameraRig);
                var camera = rig.GetComponentInChildren<CinemachineCamera>(true);
                if (camera != null && camera.GetComponent<RoomCamera>() == null)
                    camera.gameObject.AddComponent<RoomCamera>();
            }
            else
            {
                Debug.LogWarning("No camera rig set on the builder, so the systems scene has no " +
                                 "camera. Set Assets/Prefabs/CameraRig on the builder and rebuild.");
            }

            var spawner = new GameObject(nameof(PlayerSpawner)).AddComponent<PlayerSpawner>();
            var serialized = new SerializedObject(spawner);
            serialized.FindProperty("playerPrefab").objectReferenceValue = s.playerPrefab;

            // The first map in the list, which is the one whose spawn marks a new project has.
            var start = s.maps != null && s.maps.Length > 0 ? s.maps[0].name : string.Empty;
            serialized.FindProperty("startingMap").stringValue = start;
            serialized.ApplyModifiedProperties();

            // On its own object, never shared: it survives every map change, and anything sitting
            // with it would be dragged along too.
            new GameObject(nameof(ScreenFade)).AddComponent<ScreenFade>();
            new GameObject(nameof(ControlsOverlay)).AddComponent<ControlsOverlay>();

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            EditorSceneManager.SaveScene(scene, path);

            // First in the list: this is what a build starts on, and the maps are only ever
            // reached from it.
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(entry => entry.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            _status = $"Built {path}. Press Play from here; it opens '{start}' itself.";
            Debug.Log(_status);
        }

        /// <summary>
        /// Saves the hand-built objects in the open scene as prefabs and points the settings at
        /// them. Done once: after this the builder works in any scene, including an empty one.
        /// </summary>
        void CaptureTemplates()
        {
            Directory.CreateDirectory(TemplateFolder);

            _settings.wall = Capture("Wall_Top", "Wall");
            _settings.floor = Capture("Floor", "Floor");
            _settings.pillar = Capture("Pillar_A", "Pillar");
            _settings.breakerBox = Capture("BreakerBox", "BreakerBox");
            _settings.nest = Capture("Nest", "Nest");
            _settings.lamp = Capture("Light_Lamp", "Lamp");

            // Already prefabs, so they only need finding once.
            if (_settings.playerPrefab == null)
                _settings.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");

            if (_settings.cameraRig == null)
                _settings.cameraRig = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/CameraRig.prefab");

            // The scene's breaker raises a UnityEvent holding SetActive calls on lights, and a
            // prefab cannot keep references into a scene, so that wiring does not survive being
            // captured. Breaker asks PowerGrid instead and needs nothing wired.
            if (_settings.breakerBox != null)
                SwapForBreaker($"{TemplateFolder}/BreakerBox.prefab");

            EditorUtility.SetDirty(_settings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _status = _settings.wall == null
                ? $"No wall template. Open a scene with a Wall_Top in it, or put a Wall.prefab in {TemplateFolder}."
                : $"Templates ready in {TemplateFolder}. Breaker swapped in, prefabs linked.";
        }

        /// <summary>
        /// Saves a hand-built scene object as a template prefab. Falls back to the prefab already
        /// on disk when the scene object is gone, which it will be as soon as somebody replaces
        /// the hand-built level with a generated one — losing the templates at that point would
        /// mean the tool destroys its own inputs the first time it succeeds.
        /// </summary>
        static GameObject Capture(string sceneName, string prefabName)
        {
            var path = $"{TemplateFolder}/{prefabName}.prefab";
            var source = GameObject.Find(sceneName);

            if (source == null)
            {
                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (existing == null)
                    Debug.LogWarning($"'{sceneName}' is not in the open scene and there is no " +
                                     $"{path} to fall back on.");

                return existing;
            }

            // Saved from a copy, so the scene object is left alone and the prefab does not inherit
            // whatever position and scale that one happened to be sitting at.
            var copy = Instantiate(source);
            copy.name = prefabName;
            copy.transform.position = Vector3.zero;
            copy.transform.rotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;

            var renderer = copy.GetComponent<SpriteRenderer>();
            if (renderer != null && renderer.drawMode != SpriteDrawMode.Simple)
                renderer.size = Vector2.one;

            var prefab = PrefabUtility.SaveAsPrefabAsset(copy, path);
            DestroyImmediate(copy);
            return prefab;
        }

        /// <summary>
        /// The scene the game is started from. Named here because two things need to agree on it:
        /// the button that builds it, and the guard that stops a map being built on top of it.
        /// </summary>
        const string SystemsScenePath = "Assets/Scenes/Systems.unity";

        static void Build(GreyboxSettings s)
        {
            var map = s.Selected;
            if (map == null)
            {
                Debug.LogWarning("No map selected.");
                return;
            }

            var grid = Parse(map.layout, out var cols, out var rows);
            if (cols == 0 || rows == 0)
            {
                Debug.LogWarning($"'{map.name}' has an empty layout.");
                return;
            }

            var scene = SceneManager.GetActiveScene();

            // Building a map into the systems scene destroys it: the level goes in, and then
            // StripSystems takes the camera, the spawner and the fade straight back out again,
            // because from in here they look exactly like the leftovers it exists to remove. The
            // result is a scene with no camera at all, and the reason is three steps back.
            if (scene.path == SystemsScenePath)
            {
                EditorUtility.DisplayDialog(
                    "That is the systems scene",
                    $"{scene.name} holds the camera, the spawner and the fade — not a map.\n\n" +
                    "Open the map's own scene and build there. Building here would strip the very " +
                    "things this scene exists to hold.", "OK");
                return;
            }

            // Not fatal, only easy to do by accident: the dropdown and the open scene are two
            // separate choices and nothing has ever tied them together.
            if (!string.IsNullOrEmpty(scene.name) && !string.Equals(scene.name, map.name,
                    System.StringComparison.OrdinalIgnoreCase) &&
                !EditorUtility.DisplayDialog(
                    "Build into this scene?",
                    $"The open scene is '{scene.name}' but the map selected is '{map.name}'.\n\n" +
                    $"Its level will be replaced with '{map.name}'.",
                    $"Build {map.name} here", "Cancel"))
                return;

            // Two roots, and the difference between them matters the moment anybody starts
            // decorating. The map root is the map's identity — it carries the zone, it is what
            // moves to its slot, and it survives every rebuild along with everything hand-placed
            // under it. The generated root holds only what this tool made, and is thrown away and
            // remade each time. Putting the zone on the generated one, as it was, meant one press
            // of Build Map deleted the entire map including a week of art.
            var mapRoot = MapRoot(scene, s);

            for (var i = mapRoot.childCount - 1; i >= 0; i--)
                if (mapRoot.GetChild(i).name == s.rootName)
                    Undo.DestroyObjectImmediate(mapRoot.GetChild(i).gameObject);

            var root = new GameObject(s.rootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Greybox");
            root.transform.SetParent(mapRoot, false);

            var walls = new GameObject("Walls").transform;
            var floors = new GameObject("Floors").transform;
            var props = new GameObject("Props").transform;
            var spawns = new GameObject("SpawnPoints").transform;
            var rooms = new GameObject("Rooms").transform;
            walls.SetParent(root.transform, false);
            floors.SetParent(root.transform, false);
            props.SetParent(root.transform, false);
            spawns.SetParent(root.transform, false);
            rooms.SetParent(root.transform, false);

            // Anything a player can stand on. Doorways are openings in a wall, so they are floor
            // too — and they must be, or the beam would stop in an empty doorframe.
            var walkable = new[] { '.', '+', 'O', 'B', 'N', 'L', 'D', 'E', '1', '2' };

            foreach (var rect in Merge(grid, cols, rows, c => c == '#'))
                Place(s.wall, walls, s, rect, cols, rows, scaled: true);

            // The floor is flat colour with no collider on it, and the walls cover it, so the only
            // thing a dozen separate rectangles buys is a dozen lines in the hierarchy. One tiled
            // sprite across the whole map draws the same picture — as long as the map has no holes,
            // because a hole is somewhere the floor is meant to be missing rather than hidden.
            if (s.singleFloor && !HasHoles(grid, cols, rows))
            {
                var whole = Place(s.floor, floors, s, new RectInt(0, 0, cols, rows), cols, rows, scaled: false);
                if (whole != null)
                    whole.name = "Floor";
            }
            else
            {
                foreach (var rect in Merge(grid, cols, rows, c => System.Array.IndexOf(walkable, c) >= 0))
                    Place(s.floor, floors, s, rect, cols, rows, scaled: false);
            }

            var spawnPoints = new List<Transform>();
            var lamps = new List<GameObject>();

            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < cols; x++)
                {
                    var cell = new RectInt(x, y, 1, 1);
                    switch (grid[x, y])
                    {
                        case 'O': Place(s.pillar, props, s, cell, cols, rows, scaled: false); break;
                        case 'N': Place(s.nest, props, s, cell, cols, rows, scaled: false); break;
                        case 'B': Place(s.breakerBox, props, s, cell, cols, rows, scaled: false); break;
                        case 'L':
                            lamps.Add(s.lamp != null
                                ? Place(s.lamp, props, s, cell, cols, rows, scaled: false)
                                : MakeLamp(cell, props, s, cols, rows));
                            break;
                        case 'D': MakeMapDoor(cell, props, s, map, cols, rows); break;
                        case 'E': MakeMapEntry(cell, props, s, cols, rows); break;
                        case '1':
                        case '2':
                            var point = new GameObject($"SpawnPoint {grid[x, y]}");
                            point.transform.SetParent(spawns, false);
                            point.transform.position = WorldOf(cell, cols, rows, s.cellSize);

                            // Marked rather than listed on the spawner. The spawner lives in the
                            // systems scene now and outlives every map, so it cannot hold a
                            // reference into one — it asks the map it is opening instead.
                            point.AddComponent<SpawnPoint>();
                            spawnPoints.Add(point.transform);
                            break;
                    }
                }
            }

            // Doorways are walls as far as rooms are concerned: without that the three rooms and
            // the corridor are one connected blob, because they are joined through the doors.
            var roomRects = FindRooms(grid, cols, rows, s.minRoomCells);
            for (var i = 0; i < roomRects.Count; i++)
                MakeRoom(roomRects[i], rooms, s, cols, rows, i);

            if (s.wirePower)
                WirePower(root.transform, lamps, s, cols, rows);

            if (s.stripSystems)
                StripSystems();

            // Into the map root, never the generated one: a stray adopted into the geometry
            // would be deleted by the next rebuild along with it.
            AdoptStrays(mapRoot.gameObject);

            if (s.addToBuildList)
                EnsureInBuildList(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;

            Debug.Log($"Greybox '{map.name}' built: {cols}x{rows} cells, {walls.childCount} walls, " +
                      $"{floors.childCount} floors, {props.childCount} props, " +
                      $"{spawnPoints.Count} spawn points.");
        }

        static char[,] Parse(string layout, out int cols, out int rows)
        {
            var lines = (layout ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');

            // Trailing blank lines are how a text area ends, not part of the map.
            var last = lines.Length - 1;
            while (last >= 0 && lines[last].Trim().Length == 0)
                last--;

            rows = last + 1;
            cols = 0;
            for (var y = 0; y < rows; y++)
                cols = Mathf.Max(cols, lines[y].Length);

            var grid = new char[Mathf.Max(cols, 1), Mathf.Max(rows, 1)];
            for (var y = 0; y < rows; y++)
                for (var x = 0; x < cols; x++)
                    grid[x, y] = x < lines[y].Length ? lines[y][x] : ' ';

            return grid;
        }

        /// <summary>
        /// Greedy rectangle cover: take the longest run to the right, then push it down as far as
        /// every column stays free. Not optimal, but rooms are rectangles, so on this kind of map
        /// it finds them.
        /// </summary>
        static IEnumerable<RectInt> Merge(char[,] grid, int cols, int rows, System.Func<char, bool> match)
        {
            var used = new bool[cols, rows];
            var result = new List<RectInt>();

            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < cols; x++)
                {
                    if (used[x, y] || !match(grid[x, y]))
                        continue;

                    var w = 0;
                    while (x + w < cols && !used[x + w, y] && match(grid[x + w, y]))
                        w++;

                    var h = 1;
                    while (y + h < rows)
                    {
                        var wholeRow = true;
                        for (var i = 0; i < w && wholeRow; i++)
                            wholeRow = !used[x + i, y + h] && match(grid[x + i, y + h]);

                        if (!wholeRow)
                            break;

                        h++;
                    }

                    for (var j = 0; j < h; j++)
                        for (var i = 0; i < w; i++)
                            used[x + i, y + j] = true;

                    result.Add(new RectInt(x, y, w, h));
                }
            }

            return result;
        }

        /// <summary>
        /// Whether the layout has any cell that is neither wall nor floor. Those are the gaps a
        /// single floor piece cannot cover: everywhere else the walls sit on top of it.
        /// </summary>
        static bool HasHoles(char[,] grid, int cols, int rows)
        {
            for (var y = 0; y < rows; y++)
                for (var x = 0; x < cols; x++)
                    if (grid[x, y] == ' ')
                        return true;

            return false;
        }

        static GameObject Place(GameObject template, Transform parent, GreyboxSettings s, RectInt rect,
                                int cols, int rows, bool scaled)
        {
            if (template == null)
                return null;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(template, parent);
            instance.transform.position = WorldOf(rect, cols, rows, s.cellSize);

            var width = rect.width * s.cellSize;
            var height = rect.height * s.cellSize;

            if (scaled)
            {
                // Colliders and shadow shapes are authored at one unit and ride the transform, so
                // stretching the transform stretches all three together.
                instance.transform.localScale = new Vector3(width, height, 1f);
            }
            else
            {
                var renderer = instance.GetComponent<SpriteRenderer>();
                if (renderer != null && renderer.drawMode != SpriteDrawMode.Simple)
                    renderer.size = new Vector2(width, height);
            }

            instance.name = $"{template.name}_{rect.x}_{rect.y}";
            return instance;
        }

        /// <summary>
        /// Centre of a block of cells, in world space. Row 0 is the top of the text, so y is
        /// flipped; the map is centred on the origin because the existing sandbox is.
        /// </summary>
        static Vector3 WorldOf(RectInt rect, int cols, int rows, float cellSize)
        {
            var x = (rect.x + (rect.width - 1) * 0.5f - (cols - 1) * 0.5f) * cellSize;
            var y = ((rows - 1) * 0.5f - (rect.y + (rect.height - 1) * 0.5f)) * cellSize;
            return new Vector3(x, y, 0f);
        }

        /// <summary>
        /// Replaces the captured breaker's UnityEvent-driven interactable with <see cref="Breaker"/>.
        /// Done on the prefab contents rather than on an instance, which is the only way to edit a
        /// prefab asset's components that Unity actually supports.
        /// </summary>
        static void SwapForBreaker(string path)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);

            var simple = contents.GetComponent<SimpleInteractable>();
            if (simple != null)
                DestroyImmediate(simple, true);

            if (contents.GetComponent<Breaker>() == null)
                contents.AddComponent<Breaker>();

            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        /// <summary>
        /// Finds each room as a connected region of floor, and returns the box around it.
        ///
        /// Doorways count as walls here. Without that the three rooms and the corridor flood into
        /// one region, because a doorway is exactly what joins them — which is right for walking
        /// through and wrong for deciding what the camera should be looking at.
        /// </summary>
        static List<RectInt> FindRooms(char[,] grid, int cols, int rows, int minCells)
        {
            const string inside = ".OBNLDE12";

            var seen = new bool[cols, rows];
            var result = new List<RectInt>();
            var stack = new Stack<Vector2Int>();

            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < cols; x++)
                {
                    if (seen[x, y] || inside.IndexOf(grid[x, y]) < 0)
                        continue;

                    int minX = x, maxX = x, minY = y, maxY = y, count = 0;

                    stack.Push(new Vector2Int(x, y));
                    seen[x, y] = true;

                    while (stack.Count > 0)
                    {
                        var cell = stack.Pop();
                        count++;

                        if (cell.x < minX) minX = cell.x;
                        if (cell.x > maxX) maxX = cell.x;
                        if (cell.y < minY) minY = cell.y;
                        if (cell.y > maxY) maxY = cell.y;

                        Push(stack, seen, grid, cols, rows, inside, cell.x + 1, cell.y);
                        Push(stack, seen, grid, cols, rows, inside, cell.x - 1, cell.y);
                        Push(stack, seen, grid, cols, rows, inside, cell.x, cell.y + 1);
                        Push(stack, seen, grid, cols, rows, inside, cell.x, cell.y - 1);
                    }

                    if (count >= minCells)
                        result.Add(new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1));
                }
            }

            return result;
        }

        static void Push(Stack<Vector2Int> stack, bool[,] seen, char[,] grid, int cols, int rows,
                         string inside, int x, int y)
        {
            if (x < 0 || y < 0 || x >= cols || y >= rows || seen[x, y] || inside.IndexOf(grid[x, y]) < 0)
                return;

            seen[x, y] = true;
            stack.Push(new Vector2Int(x, y));
        }

        /// <summary>
        /// Builds a lamp from scratch, for when there is no template to copy. The scene it was
        /// captured from does not necessarily still exist — the first thing this tool does to a
        /// level is replace it — and a horror scene with no global light cannot afford for L to
        /// quietly do nothing.
        /// </summary>
        static GameObject MakeLamp(RectInt cell, Transform parent, GreyboxSettings s, int cols, int rows)
        {
            var lamp = new GameObject("Lamp");
            lamp.transform.SetParent(parent, false);
            lamp.transform.position = WorldOf(cell, cols, rows, s.cellSize);

            var light = lamp.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.pointLightOuterRadius = s.lampRadius;
            light.pointLightInnerRadius = s.lampInnerRadius;
            light.intensity = s.lampIntensity;
            light.color = s.lampColour;
            light.falloffIntensity = 0.5f;
            LightEveryLayer(light, includeCharacter: true);

            return lamp;
        }

        /// <summary>
        /// A way out to another map. Which map is left blank on purpose — the layout is a shape,
        /// and only the person placing the house knows what is inside it.
        /// </summary>
        static void MakeMapDoor(RectInt cell, Transform parent, GreyboxSettings s,
                                GreyboxSettings.Map map, int cols, int rows)
        {
            var door = new GameObject("MapDoor");
            door.transform.SetParent(parent, false);
            door.transform.position = WorldOf(cell, cols, rows, s.cellSize);

            var box = door.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = Vector2.one * s.cellSize;

            var component = door.AddComponent<MapDoor>();

            var serialized = new SerializedObject(component);
            serialized.FindProperty("targetMap").stringValue = map.doorTarget ?? string.Empty;
            serialized.FindProperty("targetEntry").stringValue = map.doorEntry ?? string.Empty;
            serialized.ApplyModifiedProperties();

            if (string.IsNullOrEmpty(map.doorTarget))
                Debug.LogWarning($"'{map.name}' has a D but no Door Target, so that door goes " +
                                 "nowhere. Set it on the map in the builder.");
        }

        static void MakeMapEntry(RectInt cell, Transform parent, GreyboxSettings s, int cols, int rows)
        {
            var entry = new GameObject("MapEntry");
            entry.transform.SetParent(parent, false);
            entry.transform.position = WorldOf(cell, cols, rows, s.cellSize);
            entry.AddComponent<MapEntry>();
        }

        static void MakeRoom(RectInt rect, Transform parent, GreyboxSettings s, int cols, int rows, int index)
        {
            var room = new GameObject($"Room {(char)('A' + index)}");
            room.transform.SetParent(parent, false);
            room.transform.position = WorldOf(rect, cols, rows, s.cellSize);

            var box = room.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(rect.width * s.cellSize, rect.height * s.cellSize);

            room.AddComponent<RoomBounds>();
        }

        /// <summary>
        /// Makes sure a <see cref="PowerGrid"/> exists and knows which lights it owns. The lights
        /// live in the scene and are not regenerated, so this only ever has to find them.
        /// </summary>
        static void WirePower(Transform root, List<GameObject> lamps, GreyboxSettings s,
                              int cols, int rows)
        {
            // Under the map root, like everything else the map owns. Left as a root object of its
            // own it would stay at the origin while the map moved to its slot, and the map would
            // find no grid at all.
            var host = new GameObject("PowerGrid");
            host.transform.SetParent(root, false);
            var grid = host.AddComponent<PowerGrid>();

            // Two lights covering this map and nothing else. They used to be Global type, which in
            // URP 2D means the whole scene — and with the house and the street both loaded, one
            // map's breaker lit the other map as well. A point light with no falloff inside its
            // radius is a global light with a boundary, and the boundary is what makes it the
            // map's own.
            var lit = MakeMapLight(root, "Light_Map_PowerOn", s, cols, rows,
                                   s.poweredIntensity, s.poweredColour);
            var gloom = MakeMapLight(root, "Light_Map_Dark", s, cols, rows,
                                     s.darkIntensity, s.darkColour);

            var powered = new List<GameObject>(lamps) { lit };

            // The dark state keeps a light, dim, and it is not optional. The visibility mask only
            // ever hides: it cuts a hole the shape of the beam, and something still has to be
            // lighting what shows through the hole. Take it away and the flashlight turns into a
            // hole onto black — the cone technically working and the player unable to see a thing.
            var dark = new List<GameObject> { gloom };

            var serialized = new SerializedObject(grid);
            Fill(serialized.FindProperty("whenPowered"), powered);
            Fill(serialized.FindProperty("whenDark"), dark);

            var startPowered = serialized.FindProperty("startPowered").boolValue;
            serialized.ApplyModifiedProperties();

            // Leave the scene looking the way it will play. PowerGrid does this at Start anyway, so
            // the difference is only visible in the editor — which is exactly where a map lit by
            // lamps that will be off in play gets built wrong.
            foreach (var go in powered)
                if (go != null)
                    go.SetActive(startPowered);

            gloom.SetActive(!startPowered);

            // The hand-placed globals these replace, so a rebuild does not leave the map lit twice.
            foreach (var stale in new[] { "Light_Global_PowerOn", "Light_Global_Dark" })
            {
                var found = FindInScene(stale);
                if (found != null)
                    Undo.DestroyObjectImmediate(found);
            }
        }

        /// <summary>
        /// A light that covers this map and stops at its edge.
        ///
        /// Inner radius equal to outer means no falloff at all: flat inside, nothing outside. That
        /// is a global light with a boundary, which is what a map needs once several of them are
        /// loaded at the same time.
        /// </summary>
        static GameObject MakeMapLight(Transform root, string name, GreyboxSettings s,
                                       int cols, int rows, float intensity, Color colour)
        {
            var host = new GameObject(name);
            host.transform.SetParent(root, false);
            host.transform.localPosition = Vector3.zero;

            // Half the diagonal reaches the corners; the margin covers the camera seeing a little
            // past the outer wall.
            var half = new Vector2(cols, rows) * (s.cellSize * 0.5f);
            var radius = half.magnitude + s.cellSize * 4f;

            var light = host.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.pointLightOuterRadius = radius;
            light.pointLightInnerRadius = radius;
            light.pointLightInnerAngle = 360f;
            light.pointLightOuterAngle = 360f;
            light.intensity = intensity;
            light.color = colour;
            light.falloffIntensity = 0f;
            LightEveryLayer(light, includeCharacter: true);

            return host;
        }

        /// <summary>
        /// Points a light at every sorting layer except the viewer's own body.
        ///
        /// A Light2D lights the sorting layers it is told to and no others, and the default is
        /// Default alone. That was invisible while every sprite in the game sat on Default; the
        /// moment the tilemaps arrived on Background, Floor, Wall and Object, the lights stopped
        /// reaching any of them and the map simply rendered black.
        ///
        /// Character is left out on purpose: it is the layer the viewer's own sprite is moved to,
        /// so that their own torch does not light the body it comes from.
        /// </summary>
        static void LightEveryLayer(Light2D light, bool includeCharacter = false)
        {
            var layers = new List<int>();
            foreach (var layer in SortingLayer.layers)
                if (includeCharacter || layer.name != "Character")
                    layers.Add(layer.id);

            var serialized = new SerializedObject(light);
            var array = serialized.FindProperty("m_ApplyToSortingLayers");
            array.arraySize = layers.Count;
            for (var i = 0; i < layers.Count; i++)
                array.GetArrayElementAtIndex(i).intValue = layers[i];

            serialized.ApplyModifiedProperties();
        }

        static void Fill(SerializedProperty array, List<GameObject> values)
        {
            if (array == null)
                return;

            array.arraySize = values.Count;
            for (var i = 0; i < values.Count; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        /// <summary>
        /// Registers the scene so MapTravel can load it. Doing it here rather than expecting it to
        /// be remembered: a map that is not in this list looks exactly like a map with a broken
        /// door, and the failure only shows up once somebody walks into it.
        /// </summary>
        static void EnsureInBuildList(Scene scene)
        {
            if (string.IsNullOrEmpty(scene.path))
            {
                Debug.LogWarning("This scene has never been saved, so it cannot go in the build " +
                                 "list yet. Save it and build again.");
                return;
            }

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var entry in scenes)
                if (entry.path == scene.path)
                    return;

            scenes.Add(new EditorBuildSettingsScene(scene.path, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log($"Added '{scene.path}' to the build's scene list.");
        }

        /// <summary>
        /// Takes everything out of a map scene that is not the map.
        ///
        /// These all used to be built into every map, which was right while a map change swapped
        /// the whole scene. Now that maps are loaded alongside each other, walking into a house
        /// would bring a second camera, a second spawner and a second fade with it — and two
        /// spawners means the game quietly creating another pair of players.
        /// </summary>
        /// <summary>
        /// The map's own root, made once and kept.
        ///
        /// Named after the scene, because that is what a door asks for. An older map has the zone
        /// on the generated root instead; that one is adopted rather than replaced, so a rebuild
        /// upgrades the scene in place instead of orphaning whatever is already in it.
        /// </summary>
        static Transform MapRoot(Scene scene, GreyboxSettings s)
        {
            var zone = Object.FindFirstObjectByType<MapZone>(FindObjectsInactive.Include);

            if (zone != null && zone.name == s.rootName)
            {
                // The old shape: zone and generated geometry on the same object. Lift the zone onto
                // a root of its own so the geometry underneath becomes disposable again.
                var lifted = new GameObject(scene.name);
                Undo.RegisterCreatedObjectUndo(lifted, "Build Greybox");
                Undo.SetTransformParent(zone.transform, lifted.transform, "Build Greybox");
                Undo.DestroyObjectImmediate(zone);
                zone = lifted.AddComponent<MapZone>();
            }

            if (zone == null)
            {
                var host = new GameObject(scene.name);
                Undo.RegisterCreatedObjectUndo(host, "Build Greybox");
                zone = host.AddComponent<MapZone>();
            }

            return zone.transform;
        }

        /// <summary>
        /// Puts everything left at the scene's root under the map.
        ///
        /// A map scene has one root now, and things left beside it do not travel: the zone moves
        /// the map to its slot and they stay behind at the origin, on their own, in the middle of
        /// whichever map is in slot zero. Nothing about that looks like a parenting mistake — the
        /// monster simply stops being heard, because it is not in any map to be heard from.
        ///
        /// Parenting keeps world position, and in the editor the root is at the origin, so nothing
        /// visibly moves.
        /// </summary>
        static void AdoptStrays(GameObject root)
        {
            var moved = new List<string>();

            foreach (var go in root.scene.GetRootGameObjects())
            {
                if (go == root)
                    continue;

                // A grid out here is one the old builder left behind. The map builds its own,
                // under the root, and two of them fight over which one the breaker finds.
                if (go.GetComponent<PowerGrid>() != null)
                {
                    Debug.Log($"Removed the old '{go.name}'. The map builds its own PowerGrid " +
                              "under its root now.");
                    Undo.DestroyObjectImmediate(go);
                    continue;
                }

                Undo.SetTransformParent(go.transform, root.transform, "Build Greybox");
                moved.Add(go.name);
            }

            if (moved.Count > 0)
                Debug.Log($"Moved under '{root.name}': {string.Join(", ", moved)}. Anything left " +
                          "beside the map stays at the origin when the map moves to its slot.");
        }

        static void StripSystems()
        {
            var removed = new List<string>();

            Take<PlayerSpawner>(removed);
            Take<ScreenFade>(removed);
            Take<ControlsOverlay>(removed);
            Take<RoomCamera>(removed);
            Take<CinemachineCamera>(removed);
            Take<CinemachineBrain>(removed);
            Take<Camera>(removed);
            Take<AudioListener>(removed);

            if (removed.Count > 0)
                Debug.Log($"Taken out of the map scene: {string.Join(", ", removed)}. They live in " +
                          "the systems scene, which Ashburn > Greybox Map Builder builds.");
        }

        /// <summary>
        /// Takes a system out of a map scene, object and all.
        ///
        /// The whole object, and for a prefab the whole instance. Deleting only the component was
        /// the first attempt and it left the camera rig standing there minus its brain: the rig is
        /// a small hierarchy, so the camera and the brain live on different objects and neither
        /// looked like something worth deleting on its own. The leftover carried a second
        /// <see cref="RoomCamera"/> into play, which then won the race to be <c>Current</c> and
        /// framed rooms on a camera nobody was looking through.
        /// </summary>
        static void Take<T>(List<string> removed) where T : Component
        {
            foreach (var found in Object.FindObjectsByType<T>(FindObjectsInactive.Include,
                                                              FindObjectsSortMode.None))
            {
                if (found == null)
                    continue;

                var host = found.gameObject;

                // Anything inside the map belongs to the map. Taking the object would take level
                // geometry with it, so that one loses the component only — and is worth saying out
                // loud, because a camera built into a map is a mistake either way.
                if (MapZone.Of(found) != null)
                {
                    Debug.LogWarning($"'{host.name}' carries a {typeof(T).Name} inside the map " +
                                     "itself. Removing the component and leaving the object.", host);
                    Undo.DestroyObjectImmediate(found);
                    removed.Add(typeof(T).Name);
                    continue;
                }

                var target = PrefabUtility.IsPartOfPrefabInstance(host)
                    ? PrefabUtility.GetOutermostPrefabInstanceRoot(host)
                    : Root(host);

                removed.Add(target.name);
                Undo.DestroyObjectImmediate(target);
            }
        }

        static GameObject Root(GameObject go)
        {
            var transform = go.transform;
            while (transform.parent != null)
                transform = transform.parent;

            return transform.gameObject;
        }

        /// <summary>
        /// Finds a scene object by name including inactive ones. GameObject.Find skips those, and
        /// the light this has to reach is switched off at startup by design.
        /// </summary>
        static GameObject FindInScene(string name)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == name)
                    return root;

                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                    if (child.name == name)
                        return child.gameObject;
            }

            return null;
        }

    }
}

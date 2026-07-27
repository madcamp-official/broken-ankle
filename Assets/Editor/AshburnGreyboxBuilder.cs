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
    /// A greybox map, written as text.
    ///
    /// Kept as an asset so the layout is versioned with the scene it produces, and because the
    /// layout is the thing that gets edited twenty times in an evening — moving a wall should be
    /// retyping one character, not dragging a cube and then remembering which of its three
    /// components needed setting.
    /// </summary>
    class GreyboxSettings : ScriptableObject
    {
        /// <summary>
        /// One map's shape. Kept as a list rather than a single field because a house and the
        /// street it stands on are two scenes, and building one used to mean pasting the other
        /// one's text somewhere safe first.
        /// </summary>
        [System.Serializable]
        public class Map
        {
            [Tooltip("What this map is called here. Matches its scene name if you like.")]
            public string name = "Map";

            [Tooltip("Rows may be ragged; short ones are treated as empty on the right.\n\n" +
                     "#  wall        .  floor       (space) nothing\n" +
                     "+  doorway     O  pillar      B  breaker box\n" +
                     "N  nest        L  lamp        1 2  spawn points\n" +
                     "D  way to another map (set its target in the inspector)\n" +
                     "E  where arrivals from another map come out")]
            [TextArea(14, 40)]
            public string layout = "";

            [Tooltip("Scene name every D in this layout leads to. Set here rather than per door, " +
                     "because a map usually has one way out and filling it in by hand after every " +
                     "rebuild is how it ends up empty.")]
            public string doorTarget = "";

            [Tooltip("Id of the MapEntry to arrive at over there. 'Default' matches a generated E.")]
            public string doorEntry = "Default";
        }

        [Tooltip("Every map this project builds. Build acts on the one picked above the button.")]
        public Map[] maps =
        {
            new Map { name = "House", layout = HouseLayout, doorTarget = "Street" },
            new Map { name = "Street", layout = StreetLayout, doorTarget = "House" },
        };

        [Tooltip("Index of the map to build. Set from the dropdown, not by hand.")]
        public int selected;

        /// <summary>The map the build button acts on, or null when the list is empty.</summary>
        public Map Selected => maps != null && maps.Length > 0
            ? maps[Mathf.Clamp(selected, 0, maps.Length - 1)]
            : null;

        /// <summary>
        /// Inside the house: three rooms off one corridor, with the front door at the bottom left
        /// and the mark beside it where somebody coming in from the street arrives.
        /// </summary>
        const string HouseLayout =
            "###########################################\n" +
            "#.............#.............#.............#\n" +
            "#.............#.............#.............#\n" +
            "#....O........#......B......#......N......#\n" +
            "#.............#.............#.............#\n" +
            "#.............#.............#.............#\n" +
            "#.............#.............#.............#\n" +
            "#.............#.............#.............#\n" +
            "#######+#############+#############+#######\n" +
            "#.........................................#\n" +
            "#..1..2..............L....................#\n" +
            "#..E......................................#\n" +
            "###D#######################################\n";

        /// <summary>
        /// The estate outside. Two terraces facing each other across the road, the middle one on
        /// the south side being the house the interior belongs to — its door is the D.
        ///
        /// The neighbours are solid: they are shapes to walk around and to lose sight of each
        /// other behind, not places. Ashburn.MD opens on the players arriving from the south, so
        /// the spawn marks sit on the access road at the bottom.
        /// </summary>
        const string StreetLayout =
            "##############################################################################\n" +
            "#............................................................................#\n" +
            "#............................................................................#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#............................................................................#\n" +
            "#.......................O.........................O..........................#\n" +
            "#............................................................................#\n" +
            "#............................................................................#\n" +
            "#.........L...............L...............L...............L...........L......#\n" +
            "#...................................................................B........#\n" +
            "#............................................................................#\n" +
            "#.......................O.........................O..........................#\n" +
            "#.....................................E......................................#\n" +
            "#....################..........#######D########..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#............................................................................#\n" +
            "#............................................................................#\n" +
            "#.............O......................1..2.......................O............#\n" +
            "#............................................................................#\n" +
            "#............................................................................#\n" +
            "##############################################################################\n";

        [Tooltip("World units per character. The player is about one unit across, so 1 gives " +
                 "corridors you can read the width of by counting.")]
        public float cellSize = 1f;

        [Header("Templates")]
        [Tooltip("Instanced for every wall run. Scaled, so its sprite should be one unit at scale 1.")]
        public GameObject wall;

        [Tooltip("Instanced per floor rectangle. Needs a Tiled sprite renderer; its size is set, " +
                 "not its scale, so the grid does not stretch.")]
        public GameObject floor;

        public GameObject pillar;
        public GameObject breakerBox;
        public GameObject nest;
        public GameObject lamp;

        [Tooltip("Character to spawn. Used when a fresh scene has no PlayerSpawner yet, so a new " +
                 "map is playable the moment it is built.")]
        public GameObject playerPrefab;

        [Tooltip("Camera rig dropped into a scene that has none. Assets/Prefabs/CameraRig.")]
        public GameObject cameraRig;

        [Header("Lamp")]
        [Tooltip("Used when there is no lamp template. With no global light in a horror scene the " +
                 "lamps are the only fixed light there is, so L has to work without one.")]
        public float lampRadius = 4.5f;

        [Tooltip("Radius of the fully lit core. The gap out to lampRadius is the falloff.")]
        public float lampInnerRadius = 0.6f;

        public float lampIntensity = 0.85f;

        [Tooltip("Sodium street lighting is warm and slightly sick, which suits the place.")]
        public Color lampColour = new Color(1f, 0.86f, 0.66f);

        [Header("Output")]
        [Tooltip("Everything generated goes under one object with this name, and rebuilding " +
                 "replaces it. Nothing outside it is touched.")]
        public string rootName = "Level (generated)";

        [Tooltip("Point the scene's PlayerSpawner at the generated spawn points. Without this a " +
                 "rebuild leaves it holding references to objects that no longer exist.")]
        public bool rewireSpawner = true;

        [Tooltip("Create a PowerGrid if the scene has none and point it at the lights, so the " +
                 "breaker works. Rebuilding used to lose this wiring silently.")]
        public bool wirePower = true;

        [Tooltip("Put a RoomCamera on the scene's Cinemachine camera, so walking into a room " +
                 "frames it instead of trailing the player.")]
        public bool wireRoomCamera = true;

        [Tooltip("Floor regions smaller than this are cupboards, not rooms, and get no camera of " +
                 "their own.")]
        public int minRoomCells = 12;

        [Tooltip("Add the scene being built to the build's scene list. A map missing from it cannot " +
                 "be loaded at runtime, and the door leading to it fails with nothing on screen to " +
                 "say why.")]
        public bool addToBuildList = true;
    }

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

        void OnEnable()
        {
            _settings = AssetDatabase.LoadAssetAtPath<GreyboxSettings>(SettingsPath);
            if (_settings == null)
            {
                _settings = CreateInstance<GreyboxSettings>();
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                AssetDatabase.CreateAsset(_settings, SettingsPath);
                AssetDatabase.SaveAssets();
            }

            _serialized = new SerializedObject(_settings);
        }

        void OnGUI()
        {
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

            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, MessageType.None);

            EditorGUILayout.EndScrollView();
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

            var existing = scene.GetRootGameObjects();
            foreach (var go in existing)
                if (go.name == s.rootName)
                    Undo.DestroyObjectImmediate(go);

            var root = new GameObject(s.rootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Greybox");

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

            foreach (var rect in Merge(grid, cols, rows, c => System.Array.IndexOf(walkable, c) >= 0))
                Place(s.floor, floors, s, rect, cols, rows, scaled: false);

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

            if (s.rewireSpawner && spawnPoints.Count > 0)
                Rewire(spawnPoints, s.playerPrefab);

            if (s.wirePower)
                WirePower(root.transform, lamps);

            if (s.wireRoomCamera && roomRects.Count > 0)
                WireRoomCamera(s.cameraRig);

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
        static void WirePower(Transform root, List<GameObject> lamps)
        {
            var grid = Object.FindFirstObjectByType<PowerGrid>(FindObjectsInactive.Include);
            if (grid == null)
            {
                var host = new GameObject("PowerGrid");
                Undo.RegisterCreatedObjectUndo(host, "Build Greybox");
                grid = host.AddComponent<PowerGrid>();
            }

            var powered = new List<GameObject>(lamps);
            var lit = FindInScene("Light_Global_PowerOn");
            if (lit != null)
                powered.Add(lit);

            var dark = new List<GameObject>();
            var gloom = FindInScene("Light_Global_Dark");
            if (gloom != null)
                dark.Add(gloom);

            var serialized = new SerializedObject(grid);
            Fill(serialized.FindProperty("whenPowered"), powered);
            Fill(serialized.FindProperty("whenDark"), dark);
            serialized.ApplyModifiedProperties();

            if (lit == null && dark.Count == 0)
                Debug.LogWarning("No Light_Global_PowerOn or Light_Global_Dark in the scene, so " +
                                 "the breaker has nothing to switch.");
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

        /// <summary>Puts a <see cref="RoomCamera"/> on the scene's Cinemachine camera.</summary>
        static void WireRoomCamera(GameObject cameraRig)
        {
            var camera = Object.FindFirstObjectByType<CinemachineCamera>(FindObjectsInactive.Include);

            if (camera == null && cameraRig != null)
            {
                var rig = (GameObject)PrefabUtility.InstantiatePrefab(cameraRig);
                Undo.RegisterCreatedObjectUndo(rig, "Build Greybox");
                camera = rig.GetComponentInChildren<CinemachineCamera>(true);
            }

            if (camera == null)
            {
                Debug.LogWarning("No CinemachineCamera in the scene and no camera rig set on the " +
                                 "builder, so rooms will not frame themselves.");
                return;
            }

            if (camera.GetComponent<RoomCamera>() == null)
                Undo.AddComponent<RoomCamera>(camera.gameObject);

            // On its own object, never on the camera: the fade survives a map change, and anything
            // sharing its object would be dragged into the next scene with it.
            if (Object.FindFirstObjectByType<ScreenFade>(FindObjectsInactive.Include) == null)
            {
                var host = new GameObject(nameof(ScreenFade));
                Undo.RegisterCreatedObjectUndo(host, "Build Greybox");
                host.AddComponent<ScreenFade>();
            }
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

        /// <summary>
        /// Points the scene's spawner at the new points. The field is private, so it is reached
        /// the way the inspector reaches it rather than by widening the runtime API for a tool.
        /// </summary>
        static void Rewire(List<Transform> points, GameObject playerPrefab)
        {
            var spawner = Object.FindFirstObjectByType<PlayerSpawner>(FindObjectsInactive.Include);
            if (spawner == null)
            {
                // A scene made for a new map has none yet, and building the level but leaving it
                // unplayable until somebody remembers this component is a trap worth closing.
                var host = new GameObject(nameof(PlayerSpawner));
                Undo.RegisterCreatedObjectUndo(host, "Build Greybox");
                spawner = host.AddComponent<PlayerSpawner>();
            }

            var serialized = new SerializedObject(spawner);

            var array = serialized.FindProperty("spawnPoints");
            if (array != null)
            {
                array.arraySize = points.Count;
                for (var i = 0; i < points.Count; i++)
                    array.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
            }

            // Only filled when empty, so a scene that deliberately spawns something else keeps it.
            var prefab = serialized.FindProperty("playerPrefab");
            if (prefab != null && prefab.objectReferenceValue == null && playerPrefab != null)
                prefab.objectReferenceValue = playerPrefab;

            serialized.ApplyModifiedProperties();

            if (prefab != null && prefab.objectReferenceValue == null)
                Debug.LogWarning($"{nameof(PlayerSpawner)} has no player prefab. Set one on the " +
                                 "builder so new maps come out playable.");
        }
    }
}

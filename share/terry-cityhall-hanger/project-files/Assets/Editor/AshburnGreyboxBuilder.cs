using System.Collections.Generic;
using System.IO;
using Ashburn.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashburn.EditorTools
{
    /// <summary>
    /// Builds the level geometry from <see cref="GreyboxSettings.layout"/>.
    ///
    /// A wall here is three components that all have to agree - a collider for movement, the same
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
                EditorGUILayout.PropertyField(property, true);

            _serialized.ApplyModifiedProperties();

            EditorGUILayout.Space();

            if (_settings.wall == null || _settings.floor == null)
            {
                EditorGUILayout.HelpBox(
                    "Wall and floor templates are required. Open Sandbox_A and press the button " +
                    "below: it saves Wall_Top, Floor, Pillar_A, BreakerBox, Nest and Light_Lamp " +
                    "from the open scene as prefabs and wires them up here.",
                    MessageType.Info);

                if (GUILayout.Button("Create Template Prefabs From Scene", GUILayout.Height(24f)))
                    CaptureTemplates();
            }

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

            EditorUtility.SetDirty(_settings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _status = _settings.wall == null
                ? "Wall_Top was not found. Open Sandbox_A and press the button again."
                : $"Templates saved to {TemplateFolder}.";
        }

        static GameObject Capture(string sceneName, string prefabName)
        {
            var source = GameObject.Find(sceneName);
            if (source == null)
            {
                Debug.LogWarning($"'{sceneName}' was not found in the open scene. Skipping it.");
                return null;
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

            var prefab = PrefabUtility.SaveAsPrefabAsset(copy, $"{TemplateFolder}/{prefabName}.prefab");
            DestroyImmediate(copy);
            return prefab;
        }

        static void Build(GreyboxSettings s)
        {
            var grid = Parse(s.layout, out var cols, out var rows);
            if (cols == 0 || rows == 0)
            {
                Debug.LogWarning("The layout is empty.");
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
            walls.SetParent(root.transform, false);
            floors.SetParent(root.transform, false);
            props.SetParent(root.transform, false);
            spawns.SetParent(root.transform, false);

            // Anything a player can stand on. Doorways are openings in a wall, so they are floor
            // too - and they must be, or the beam would stop in an empty doorframe.
            var walkable = new[] { '.', '+', 'O', 'B', 'N', 'L', '1', '2' };

            foreach (var rect in Merge(grid, cols, rows, c => c == '#'))
                Place(s.wall, walls, s, rect, cols, rows, scaled: true);

            foreach (var rect in Merge(grid, cols, rows, c => System.Array.IndexOf(walkable, c) >= 0))
                Place(s.floor, floors, s, rect, cols, rows, scaled: false);

            var spawnPoints = new List<Transform>();

            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < cols; x++)
                {
                    var cell = new RectInt(x, y, 1, 1);
                    switch (grid[x, y])
                    {
                        case 'O': Place(s.pillar, props, s, cell, cols, rows, scaled: false); break;
                        case 'B': Place(s.breakerBox, props, s, cell, cols, rows, scaled: false); break;
                        case 'N': Place(s.nest, props, s, cell, cols, rows, scaled: false); break;
                        case 'L': Place(s.lamp, props, s, cell, cols, rows, scaled: false); break;
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

            root.transform.position = s.layoutOffset;

            if (s.rewireSpawner && spawnPoints.Count > 0)
                Rewire(spawnPoints);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;

            Debug.Log($"Greybox built: {cols}x{rows} cells, {walls.childCount} walls, " +
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

        static void Place(GameObject template, Transform parent, GreyboxSettings s, RectInt rect,
                          int cols, int rows, bool scaled)
        {
            if (template == null)
                return;

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
        /// Points the scene's spawner at the new points. The field is private, so it is reached
        /// the way the inspector reaches it rather than by widening the runtime API for a tool.
        /// </summary>
        static void Rewire(List<Transform> points)
        {
            var spawner = Object.FindFirstObjectByType<PlayerSpawner>();
            if (spawner == null)
                return;

            var serialized = new SerializedObject(spawner);
            var array = serialized.FindProperty("spawnPoints");
            if (array == null)
                return;

            array.arraySize = points.Count;
            for (var i = 0; i < points.Count; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = points[i];

            serialized.ApplyModifiedProperties();
        }
    }
}

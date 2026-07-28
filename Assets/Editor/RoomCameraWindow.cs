using System.Collections.Generic;
using Ashburn.World;
using UnityEditor;
using UnityEngine;

namespace Ashburn.EditorTools
{
    /// <summary>
    /// Shows what the room camera is going to do, and why.
    ///
    /// Built because "the room does not frame itself" has had three different causes so far and
    /// none of them were visible from the inspector: a second <see cref="RoomCamera"/> in another
    /// scene quietly winning the race to be <c>Current</c>, a room half a metre too wide for the
    /// screen falling out of framing altogether, and a clamp switched off on a wrong diagnosis.
    /// All three are one glance from here.
    ///
    /// The settings are the live component's, not a copy, so changing something here is changing
    /// the scene — including while the game is running, which is the only way to feel whether a
    /// number is right.
    /// </summary>
    class RoomCameraWindow : EditorWindow
    {
        [MenuItem("Ashburn/Room Camera")]
        static void Open() => GetWindow<RoomCameraWindow>("Room Camera").Show();

        SerializedObject _serialized;
        RoomCamera _camera;
        Vector2 _scroll;

        void OnEnable()
        {
            EditorApplication.hierarchyChanged += Refresh;
            Refresh();
        }

        void OnDisable() => EditorApplication.hierarchyChanged -= Refresh;

        // Play mode moves the camera every frame and loads maps as the players walk into them, so
        // a window that only redrew on interaction would be showing the previous map.
        void OnInspectorUpdate()
        {
            if (Application.isPlaying)
                Repaint();
        }

        void Refresh()
        {
            _camera = null;
            _serialized = null;
            Repaint();
        }

        void OnGUI()
        {
            var cameras = Object.FindObjectsByType<RoomCamera>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (cameras.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No RoomCamera in any open scene. Rooms will not frame themselves and the " +
                    "view stays on whatever CinemachinePlayerBinder is following.\n\n" +
                    "It belongs on the CinemachineCamera in the systems scene.", MessageType.Warning);
                return;
            }

            if (cameras.Length > 1)
            {
                // Exactly the bug that cost an afternoon: RoomCamera.Current is whichever woke up
                // last, and the camera being framed was not the camera being rendered.
                EditorGUILayout.HelpBox(
                    $"{cameras.Length} RoomCameras are loaded. Only one of them will be Current — " +
                    "the last one to wake — and if that is not the one you are looking through, " +
                    "rooms will appear not to frame at all.", MessageType.Error);

                foreach (var extra in cameras)
                    if (GUILayout.Button($"Select  {Path(extra)}"))
                        Selection.activeGameObject = extra.gameObject;

                EditorGUILayout.Space();
            }

            if (_camera != cameras[0] || _serialized == null)
            {
                _camera = cameras[0];
                _serialized = new SerializedObject(_camera);
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField(Path(_camera), EditorStyles.boldLabel);
            if (GUILayout.Button("Select"))
                Selection.activeGameObject = _camera.gameObject;

            EditorGUILayout.Space();

            _serialized.Update();
            foreach (var field in new[] { "padding", "transition", "panSeconds", "fadeOutSeconds",
                                          "fadeInSeconds", "maxSize", "clampToRoom" })
            {
                var property = _serialized.FindProperty(field);
                if (property != null)
                    EditorGUILayout.PropertyField(property, true);
            }

            _serialized.ApplyModifiedProperties();

            EditorGUILayout.Space();
            DrawRooms();
            EditorGUILayout.EndScrollView();
        }

        void DrawRooms()
        {
            var aspect = Aspect();
            EditorGUILayout.LabelField("Rooms", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Screen aspect {aspect:0.00}", EditorStyles.miniLabel);

            var rooms = Object.FindObjectsByType<RoomBounds>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (rooms.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No RoomBounds loaded. In edit mode that is normal — the maps are separate " +
                    "scenes and nothing has opened one. Open a map scene, or press Play.",
                    MessageType.Info);
                return;
            }

            var padding = _serialized.FindProperty("padding").floatValue;
            var maxSize = _serialized.FindProperty("maxSize").floatValue;
            var clamp = _serialized.FindProperty("clampToRoom").boolValue;
            var showing = _camera.Room;

            foreach (var room in Sorted(rooms))
            {
                var area = room.Area;

                // The same arithmetic RoomCamera does, so what is printed is what will happen
                // rather than what ought to.
                var wanted = Mathf.Max(area.size.y * 0.5f + padding,
                                       (area.size.x * 0.5f + padding) / aspect);
                var size = Mathf.Min(wanted, maxSize);
                var fitsX = area.size.x <= size * aspect * 2f;
                var fitsY = area.size.y <= size * 2f;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var here = room == showing ? "  ● " : "     ";
                        EditorGUILayout.LabelField($"{here}{room.name}",
                            EditorStyles.boldLabel, GUILayout.Width(140f));

                        EditorGUILayout.LabelField(
                            $"{area.size.x:0.#} × {area.size.y:0.#}   at size {size:0.##}" +
                            (wanted > maxSize ? $"  (wants {wanted:0.##}, capped by Max Size)" : ""),
                            EditorStyles.miniLabel);

                        if (GUILayout.Button("Select", GUILayout.Width(60f)))
                            Selection.activeGameObject = room.gameObject;
                    }

                    EditorGUILayout.LabelField("      " + Behaviour(fitsX, fitsY, clamp),
                        EditorStyles.miniLabel);
                }
            }

            if (!clamp)
                EditorGUILayout.HelpBox(
                    "Clamp To Room is off, so any room that does not fit the screen is not framed " +
                    "at all — the view simply follows the player inside it. A room only a little " +
                    "wider than the screen looks exactly like room framing being broken.",
                    MessageType.Warning);
        }

        static string Behaviour(bool fitsX, bool fitsY, bool clamp)
        {
            if (fitsX && fitsY)
                return "held on the room's centre";

            var axis = fitsX ? "vertically" : fitsY ? "horizontally" : "on both axes";
            return clamp
                ? $"too large {axis}: follows the player, stopped at the room's walls"
                : $"too large {axis}: follows the player, NOT held inside the room";
        }

        /// <summary>
        /// What the game view is actually shaped like. Asking Camera.main gives the answer for the
        /// last camera that rendered, which in edit mode is the scene view.
        /// </summary>
        static float Aspect()
        {
            if (Application.isPlaying && Camera.main != null)
                return Camera.main.aspect;

            var size = Handles.GetMainGameViewSize();
            return size.y > 0f ? size.x / size.y : 16f / 9f;
        }

        static IEnumerable<RoomBounds> Sorted(RoomBounds[] rooms)
        {
            var list = new List<RoomBounds>(rooms);
            list.Sort((a, b) => string.CompareOrdinal(Path(a), Path(b)));
            return list;
        }

        static string Path(Component component)
        {
            var path = component.name;
            for (var t = component.transform.parent; t != null; t = t.parent)
                path = t.name + "/" + path;

            return $"{component.gameObject.scene.name} · {path}";
        }
    }
}

using System.IO;
using UnityEditor;
using UnityEngine;

namespace Morrow.EditorTools
{
    /// <summary>
    /// Settings for the flashlight cookie, kept as an asset so the recipe is versioned alongside
    /// the texture it produces. Re-baking from these values reproduces the shipped cookie exactly.
    /// </summary>
    class FlashlightCookieSettings : ScriptableObject
    {
        [Tooltip("Where the baked texture is written. The _WxH suffix sets pixels-per-unit.")]
        public string outputPath = "Assets/Art/Debug/flashlight_cookie_32x32.png";

        [Tooltip("Texture size. Height is the beam's length, width is twice its reach sideways.")]
        public int width = 192;
        public int height = 224;

        [Tooltip("Half the cone's width at full length, in pixels. Sets how wide the beam opens.")]
        public int coneHalfWidth = 96;

        [Tooltip("Pixels per unit. 32 keeps one cookie texel equal to one art pixel.")]
        public int pixelsPerUnit = 32;

        [Header("Edges")]
        [Tooltip("Alpha per pixel stepping inward from the cone's straight sides. First value is " +
                 "the outermost pixel. Leave empty to draw no sides.")]
        public int[] sideRamp = { 30, 20, 15, 10, 10 };

        [Tooltip("Alpha per pixel stepping inward from the far arc. Empty means no arc, which is " +
                 "usually right: the beam already ends where it meets a wall.")]
        public int[] arcRamp = { };
    }

    /// <summary>
    /// Bakes the flashlight's light cookie.
    ///
    /// The cookie decides the beam's shape, and shape is a look-at-it-and-judge problem — this
    /// one was redrawn eight times in an afternoon. Editing numbers in a throwaway script and
    /// re-running it made every one of those a slow round trip, so the recipe lives in an asset
    /// and the bake is a button.
    /// </summary>
    class MorrowFlashlightCookieBaker : EditorWindow
    {
        const string SettingsPath = "Assets/Art/Debug/FlashlightCookie.asset";

        FlashlightCookieSettings _settings;
        SerializedObject _serialized;
        Vector2 _scroll;

        [MenuItem("Morrow/Flashlight Cookie Baker")]
        static void Open() => GetWindow<MorrowFlashlightCookieBaker>("Cookie Baker").Show();

        void OnEnable()
        {
            _settings = AssetDatabase.LoadAssetAtPath<FlashlightCookieSettings>(SettingsPath);
            if (_settings == null)
            {
                _settings = CreateInstance<FlashlightCookieSettings>();
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
            if (GUILayout.Button("Bake", GUILayout.Height(28f)))
                Bake(_settings);

            EditorGUILayout.Space();
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(_settings.outputPath);
            if (existing != null)
            {
                EditorGUILayout.LabelField("현재 굽힌 결과", EditorStyles.boldLabel);
                var rect = GUILayoutUtility.GetRect(_settings.width, _settings.height,
                    GUILayout.ExpandWidth(false));
                EditorGUI.DrawTextureTransparent(rect, existing, ScaleMode.ScaleToFit);
            }

            EditorGUILayout.EndScrollView();
        }

        static void Bake(FlashlightCookieSettings s)
        {
            var pixels = new Color32[s.width * s.height];
            var apexX = s.width / 2f;

            // The sides are tilted, so a horizontal step across them is shorter than the true
            // thickness. Without this the outline thickens as the cone widens.
            var slantScale = s.height / Mathf.Sqrt(
                (float)s.coneHalfWidth * s.coneHalfWidth + (float)s.height * s.height);

            for (var y = 0; y < s.height; y++)
            {
                for (var x = 0; x < s.width; x++)
                {
                    var dx = x + 0.5f - apexX;
                    var dy = y + 0.5f;
                    var halfWidthHere = dy * (s.coneHalfWidth / (float)s.height);
                    var across = Mathf.Abs(dx);

                    var alpha = 0;
                    if (across <= halfWidthHere)
                    {
                        alpha = Sample(s.sideRamp, (halfWidthHere - across) * slantScale);

                        if (s.arcRamp != null && s.arcRamp.Length > 0)
                        {
                            var distance = Mathf.Sqrt(dx * dx + dy * dy);
                            alpha = Mathf.Max(alpha, Sample(s.arcRamp, s.height - distance));
                        }
                    }

                    pixels[y * s.width + x] = new Color32(255, 255, 255, (byte)Mathf.Clamp(alpha, 0, 255));
                }
            }

            var texture = new Texture2D(s.width, s.height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();

            Directory.CreateDirectory(Path.GetDirectoryName(s.outputPath));
            File.WriteAllBytes(s.outputPath, texture.EncodeToPNG());
            DestroyImmediate(texture);

            AssetDatabase.ImportAsset(s.outputPath, ImportAssetOptions.ForceSynchronousImport);
            ApplyImportSettings(s);

            var lit = 0;
            foreach (var p in pixels)
                if (p.a > 0)
                    lit++;

            Debug.Log($"[Morrow] Baked '{s.outputPath}': {s.width}x{s.height}, {lit} lit pixels.");
        }

        /// <summary>Alpha for a pixel this far inside an edge, or zero once past the ramp.</summary>
        static int Sample(int[] ramp, float depth)
        {
            if (ramp == null || ramp.Length == 0 || depth < 0f)
                return 0;

            var index = (int)depth;
            return index < ramp.Length ? ramp[index] : 0;
        }

        /// <summary>
        /// Written out rather than left to the project's import rules, because a cookie needs the
        /// opposite of what a sprite sheet needs: one whole untrimmed rect, and a pivot at the
        /// apex so the light originates where the beam starts.
        /// </summary>
        static void ApplyImportSettings(FlashlightCookieSettings s)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(s.outputPath);

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
            settings.spritePivot = new Vector2(0.5f, 0f);
            settings.spriteExtrude = 0;
            importer.SetTextureSettings(settings);

            importer.spritePixelsPerUnit = s.pixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            // FlashlightOcclusion reads these pixels at runtime to carve the beam.
            importer.isReadable = true;

            importer.SaveAndReimport();
        }
    }
}

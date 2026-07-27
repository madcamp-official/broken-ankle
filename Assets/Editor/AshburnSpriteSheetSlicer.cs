using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Ashburn.EditorTools
{
    /// <summary>
    /// Re-slices a pixel-art sheet on an exact cell grid instead of relying on Unity's automatic
    /// detection, which produces a different bounding box per frame and splits any frame whose art
    /// is visually disconnected.
    ///
    /// Frames are indexed row-major from the top-left, matching how sheets are laid out by hand.
    /// The pivot is bottom-center so a character's feet mark the tile it is standing on, which is
    /// what Y-sorting needs in a 3/4 top-down view.
    ///
    /// Cell size is read from the "_WxH" suffix in the file name.
    /// </summary>
    static class AshburnSpriteSheetSlicer
    {
        const string MenuPath = "Assets/Ashburn/Slice Pixel Sheet on Cell Grid";
        static readonly Regex CellSuffix = new Regex(@"_(\d+)x(\d+)$");

        [MenuItem(MenuPath, false, 30)]
        static void SliceSelection()
        {
            var sliced = 0;
            foreach (var guid in Selection.assetGUIDs)
            {
                if (Slice(AssetDatabase.GUIDToAssetPath(guid)))
                    sliced++;
            }

            if (sliced > 0)
                Debug.Log($"[Ashburn] Sliced {sliced} sheet(s) on the cell grid.");
        }

        [MenuItem(MenuPath, true)]
        static bool ValidateSliceSelection()
        {
            foreach (var guid in Selection.assetGUIDs)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is TextureImporter
                    && CellSuffix.IsMatch(Path.GetFileNameWithoutExtension(path)))
                    return true;
            }

            return false;
        }

        static bool Slice(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return false;

            var sheetName = Path.GetFileNameWithoutExtension(path);
            var match = CellSuffix.Match(sheetName);
            if (!match.Success)
            {
                Debug.LogWarning($"[Ashburn] '{sheetName}' has no _WxH size suffix, so the cell size " +
                                 "is unknown. Rename it (for example foo_32x32.png) and try again.");
                return false;
            }

            var cellWidth = int.Parse(match.Groups[1].Value);
            var cellHeight = int.Parse(match.Groups[2].Value);

            if (importer.spriteImportMode != SpriteImportMode.Multiple)
            {
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.SaveAndReimport();

                // Reimporting invalidates the importer instance, so pick up the fresh one.
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                    return false;
            }

            var factories = new SpriteDataProviderFactories();
            factories.Init();
            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null)
            {
                Debug.LogError($"[Ashburn] No sprite data provider for '{sheetName}'.");
                return false;
            }

            provider.InitSpriteEditorDataProvider();

            var textureProvider = provider.GetDataProvider<ITextureDataProvider>();
            if (textureProvider == null)
            {
                Debug.LogError($"[Ashburn] No texture data provider for '{sheetName}'.");
                return false;
            }

            // Source dimensions, not the imported ones, so a clamped max size cannot skew the grid.
            textureProvider.GetTextureActualWidthAndHeight(out var width, out var height);

            if (width % cellWidth != 0 || height % cellHeight != 0)
            {
                Debug.LogError($"[Ashburn] '{sheetName}' is {width}x{height}, which is not a whole " +
                               $"number of {cellWidth}x{cellHeight} cells. Aborted rather than " +
                               "slice a grid that would clip the art.");
                return false;
            }

            var columns = width / cellWidth;
            var rows = height / cellHeight;
            var rects = new SpriteRect[columns * rows];

            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var index = row * columns + column;
                    rects[index] = new SpriteRect
                    {
                        name = $"{sheetName}_{index}",
                        // Rect coordinates start at the bottom-left, so row 0 sits at the top.
                        rect = new Rect(column * cellWidth,
                                        height - (row + 1) * cellHeight,
                                        cellWidth,
                                        cellHeight),
                        alignment = SpriteAlignment.BottomCenter,
                        pivot = new Vector2(0.5f, 0f),
                        border = Vector4.zero,
                        spriteID = GUID.Generate(),
                    };
                }
            }

            provider.SetSpriteRects(rects);

            // Keeps sprite names bound to stable file IDs so scene and animation references to a
            // given frame survive later re-slices.
            var nameFileIdProvider = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (nameFileIdProvider != null)
            {
                var pairs = new List<SpriteNameFileIdPair>(rects.Length);
                foreach (var rect in rects)
                    pairs.Add(new SpriteNameFileIdPair(rect.name, rect.spriteID));
                nameFileIdProvider.SetNameFileIdPairs(pairs);
            }

            provider.Apply();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            Debug.Log($"[Ashburn] '{sheetName}': {columns}x{rows} = {rects.Length} sprites " +
                      $"at {cellWidth}x{cellHeight}, pivot bottom-center.");
            return true;
        }
    }
}

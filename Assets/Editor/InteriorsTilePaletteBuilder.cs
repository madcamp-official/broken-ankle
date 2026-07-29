using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Ashburn.EditorTools
{
    static class InteriorsTilePaletteBuilder
    {
        const string InteriorsPath = "Assets/Art/Tiles/LimeZu/Interiors";

        [MenuItem("Ashburn/Tiles/Apply Interiors 32px Grid Slice")]
        public static void ApplyInteriorsGridSlice()
        {
            var textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { InteriorsPath });
            var count = 0;

            foreach (var guid in textureGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".png"))
                    continue;
                if (!path.StartsWith(InteriorsPath + "/"))
                    continue;
                if (path.Contains("/Palettes/") || path.Contains("/Tiles/"))
                    continue;

                if (ApplyGridSlice(path, 32, 32))
                    count++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Ashburn] Applied 32x32 Grid by Cell Size slicing to {count} Interiors texture(s).");
        }

        static bool ApplyGridSlice(string path, int cellWidth, int cellHeight)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return false;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 32f;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();

            importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return false;

            var factories = new SpriteDataProviderFactories();
            factories.Init();
            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null)
                return false;

            provider.InitSpriteEditorDataProvider();
            var textureProvider = provider.GetDataProvider<ITextureDataProvider>();
            if (textureProvider == null)
                return false;

            textureProvider.GetTextureActualWidthAndHeight(out var width, out var height);
            if (width % cellWidth != 0 || height % cellHeight != 0)
            {
                Debug.LogError($"[Ashburn] {path} is {width}x{height}, not divisible by {cellWidth}x{cellHeight}.");
                return false;
            }

            var sheetName = Path.GetFileNameWithoutExtension(path);
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
                        rect = new Rect(column * cellWidth, height - (row + 1) * cellHeight, cellWidth, cellHeight),
                        alignment = SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                        border = Vector4.zero,
                        spriteID = GUID.Generate(),
                    };
                }
            }

            provider.SetSpriteRects(rects);

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

            importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.spritePixelsPerUnit = 32f;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            Debug.Log($"[Ashburn] Grid sliced {sheetName}: {columns}x{rows} ({rects.Length}) at 32x32, PPU 32.");
            return true;
        }
    }
}

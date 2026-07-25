using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Morrow.EditorTools
{
    /// <summary>
    /// Applies the project's pixel-art import standard to any texture whose file name ends in a
    /// cell-size suffix such as "_32x32". Pixels-per-unit is set to the cell width so that one
    /// tile measures exactly one Unity world unit.
    ///
    /// Textures without the suffix are left completely alone, so this never touches UI art,
    /// photos or anything else that is not on the tile grid.
    /// </summary>
    class MorrowPixelArtPostprocessor : AssetPostprocessor
    {
        static readonly Regex CellSuffix = new Regex(@"_(\d+)x(\d+)$");

        public override uint GetVersion() => 1;

        void OnPreprocessTexture()
        {
            var match = CellSuffix.Match(Path.GetFileNameWithoutExtension(assetPath));
            if (!match.Success)
                return;

            var cellWidth = int.Parse(match.Groups[1].Value);
            var importer = (TextureImporter)assetImporter;

            // TextureImporterSettings and TextureImporter expose overlapping fields, so write the
            // settings block first and the individual properties after it. Last write wins.
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            // Tight meshes give every frame a different bounding box, which makes an animated
            // character jitter between frames. Full Rect keeps every frame the same size.
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
            settings.spritePivot = new Vector2(0.5f, 0f);
            importer.SetTextureSettings(settings);

            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = cellWidth;

            // Point filtering and no compression: anything else smears or dithers the pixels.
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;

            // Any rescale would shift the art off the cell grid, so forbid both ways it can happen.
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 8192;
        }
    }
}

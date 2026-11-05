#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
namespace Seabreeze.Editor
{
    public static class SpriteSheetImporter
    {
        public static void ImportAvailable()
        {
            Import("Assets/Seabreeze/Resources/Art/characters.png", new[] { "player", "mio", "aya", "chen" });
            ImportPlayerWalk();
        }
        public static void ImportPlayerWalk()
        {
            Import("Assets/Seabreeze/Resources/Art/player_walk.png", new[] { "walk_0", "walk_1", "walk_2", "walk_3" }, true);
        }

        /// <summary>
        /// Explicitly imports the production walk sheet; never called by ImportAvailable.
        /// Frames are read left to right, then top to bottom, using the supplied full-cell canvas.
        /// normalizedPivot is measured from the bottom-left of every cell (Unity convention).
        /// </summary>
        public static void ImportProductionWalk(int frameWidth, int frameHeight, float ppu, Vector2 normalizedPivot)
        {
            const string path = "Assets/Seabreeze/Resources/Art/player_walk_v1_sheet.png";
            const int columns = 4, rows = 2, maximumSize = 8192;
            if (frameWidth <= 0 || frameWidth > maximumSize / columns)
                throw new ArgumentOutOfRangeException(nameof(frameWidth), "Frame width must be between 1 and 2048 pixels.");
            if (frameHeight <= 0 || frameHeight > maximumSize / rows)
                throw new ArgumentOutOfRangeException(nameof(frameHeight), "Frame height must be between 1 and 4096 pixels.");
            if (float.IsNaN(ppu) || float.IsInfinity(ppu) || ppu <= 0f)
                throw new ArgumentOutOfRangeException(nameof(ppu), "Pixels per unit must be finite and positive.");
            if (float.IsNaN(normalizedPivot.x) || float.IsNaN(normalizedPivot.y) ||
                normalizedPivot.x < 0f || normalizedPivot.x > 1f ||
                normalizedPivot.y < 0f || normalizedPivot.y > 1f)
                throw new ArgumentOutOfRangeException(nameof(normalizedPivot), "Pivot coordinates must be finite and between 0 and 1.");
            if (!File.Exists(path))
                throw new FileNotFoundException("Production walk sheet has not been supplied.", path);

            // Validate the source canvas before changing any importer settings. No alpha trimming.
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!source.LoadImage(File.ReadAllBytes(path)))
                    throw new InvalidDataException("Could not decode production walk sheet: " + path);
                if (source.width != frameWidth * columns || source.height != frameHeight * rows)
                    throw new InvalidDataException(
                        $"Production walk sheet is {source.width}x{source.height}; expected {frameWidth * columns}x{frameHeight * rows} (4 columns, 2 rows).");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }

            // This manual entry point can also be called immediately after copying in a new sheet.
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("No TextureImporter is available for " + path);

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.spritePixelsPerUnit = ppu;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = maximumSize;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.SaveAndReimport();

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null)
                throw new InvalidOperationException("No sprite data provider is available for " + path);
            provider.InitSpriteEditorDataProvider();
            var previous = provider.GetSpriteRects();
            var rects = new SpriteRect[columns * rows];
            for (int i = 0; i < rects.Length; i++)
            {
                string name = $"player_walk_v1_{i:00}";
                var old = previous.FirstOrDefault(r => r.name == name);
                rects[i] = new SpriteRect
                {
                    name = name,
                    spriteID = old != null ? old.spriteID : GUID.Generate(),
                    rect = new Rect((i % columns) * frameWidth, (rows - 1 - i / columns) * frameHeight, frameWidth, frameHeight),
                    alignment = SpriteAlignment.Custom,
                    pivot = normalizedPivot
                };
            }
            provider.SetSpriteRects(rects);
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(
                rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
            provider.Apply();
            importer.SaveAndReimport();
            Debug.Log($"Imported production walk: 8 full-cell frames, {frameWidth}x{frameHeight}, {ppu} PPU, pivot {normalizedPivot}. Source: {path}");
        }

        static void Import(string path, string[] names, bool alignPlayerWalk = false)
        {
            if (!File.Exists(path)) return;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            source.LoadImage(File.ReadAllBytes(path));
            var pixels = source.GetPixels32();
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var previous = provider.GetSpriteRects();
            var rects = new SpriteRect[names.Length];
            int cell = source.width / names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                int minX = cell * (i + 1), minY = source.height, maxX = cell * i, maxY = 0;
                for (int y = 0; y < source.height; y++)
                for (int x = cell * i; x < cell * (i + 1); x++)
                {
                    if (pixels[y * source.width + x].a < 32) continue;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }
                if (minX >= maxX || minY >= maxY) throw new InvalidDataException("Empty sprite cell " + names[i]);
                var old = previous.FirstOrDefault(r => r.name == names[i]);
                rects[i] = new SpriteRect { name = names[i], spriteID = old != null ? old.spriteID : GUID.Generate(),
                    rect = new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1),
                    alignment = SpriteAlignment.Center, pivot = new Vector2(.5f, .5f) };
            }
            if (alignPlayerWalk)
            {
                // Preserve the authored vertical canvas while anchoring each pose to its head/body axis.
                float minY = rects.Min(r => r.rect.yMin);
                float maxY = rects.Max(r => r.rect.yMax);
                float[] headAxes = { .51105031f, .48943067f, .50601583f, .53181379f };
                for (int i = 0; i < rects.Length; i++)
                {
                    var rect = rects[i].rect;
                    rects[i].rect = new Rect(rect.x, minY, rect.width, maxY - minY);
                    rects[i].alignment = SpriteAlignment.Custom;
                    rects[i].pivot = new Vector2(headAxes[i], 0f);
                }
            }
            provider.SetSpriteRects(rects);
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
            provider.Apply();
            importer.SaveAndReimport();
            UnityEngine.Object.DestroyImmediate(source);
            Debug.Log("Sliced original transparent artwork without changing source pixels: " + path);
        }
    }
}
#endif

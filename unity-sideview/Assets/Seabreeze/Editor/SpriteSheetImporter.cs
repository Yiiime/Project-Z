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
            Import("Assets/Seabreeze/Resources/Art/player_walk.png", new[] { "walk_0", "walk_1", "walk_2", "walk_3" });
        }
        static void Import(string path, string[] names)
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

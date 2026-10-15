using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ToyGarden.Editor
{
    /// <summary>Repeatable atlas slicing, independent PNG export, and Unity UI nine-slice metadata.</summary>
    public static class GardenUISkinImporter
    {
        const string Root = "Assets/_Game/Resources/UI/EquipmentSkin";
        const int ReferenceSize = 1254;
        const int Padding = 4;
        static readonly string[] ChromeNames =
        {
            "panel", "slot-common", "slot-uncommon", "slot-rare",
            "slot-epic", "slot-legendary", "selection", "tab-normal",
            "tab-selected", "action-normal", "action-gold", "field",
            "tooltip", "progress-track", "progress-fill", "divider"
        };
        static readonly string[] IconNames =
        {
            "icon-blade", "icon-hammer", "icon-bow", "icon-helmet",
            "icon-chest", "icon-legs", "icon-gloves", "icon-boots",
            "icon-ring", "icon-amulet", "icon-gold", "icon-ore",
            "icon-essence", "icon-equipment", "icon-character", "icon-forge"
        };
        // Measured alpha > 128 component bounds of the generated 1254 × 1254 artwork.
        // These explicit top-left rectangles account for generation drift across nominal grid cells.
        static readonly RectInt[] ChromeReferenceBounds =
        {
            Bounds(39,63,320,326), Bounds(369,81,597,305), Bounds(673,83,898,304), Bounds(978,83,1201,303),
            Bounds(59,403,293,625), Bounds(367,403,598,625), Bounds(658,397,899,632), Bounds(933,465,1244,568),
            Bounds(18,745,325,843), Bounds(331,745,627,838), Bounds(633,746,930,840), Bounds(938,751,1237,834),
            Bounds(39,911,318,1192), Bounds(350,1046,623,1080), Bounds(649,1048,929,1082), Bounds(958,1047,1228,1083)
        };
        static readonly RectInt[] IconReferenceBounds =
        {
            Bounds(21,19,320,309), Bounds(369,20,623,313), Bounds(651,13,937,313), Bounds(985,19,1211,311),
            Bounds(21,334,301,624), Bounds(361,334,584,617), Bounds(648,353,937,615), Bounds(963,336,1225,622),
            Bounds(49,668,282,885), Bounds(357,635,590,914), Bounds(641,661,923,909), Bounds(969,660,1230,912),
            Bounds(54,932,299,1229), Bounds(354,939,625,1216), Bounds(664,943,883,1229), Bounds(962,938,1229,1219)
        };

        [MenuItem("Toy Garden/UI/Import and Slice Equipment Skin")]
        public static void ImportAndSlice()
        {
            Directory.CreateDirectory(Root + "/Slices");
            foreach (string filename in new[] { "background.png", "chrome-atlas.png", "icons-atlas.png" })
                if (!File.Exists(Root + "/" + filename))
                    throw new FileNotFoundException("Equipment artwork is missing; generate the source PNG before importing.", Root + "/" + filename);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureBackground();
            var manifest = new SliceManifest();
            ImportAtlas("chrome-atlas", ChromeNames, true, manifest);
            ImportAtlas("icons-atlas", IconNames, false, manifest);
            File.WriteAllText(Root + "/slice-manifest.json", JsonUtility.ToJson(manifest, true));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();
            Debug.Log($"Equipment UI skin imported: {manifest.sprites.Count} atlas sprites and standalone PNG slices; metadata: {Root}/slice-manifest.json");
        }

        static void ConfigureBackground()
        {
            var importer = AssetImporter.GetAtPath(Root + "/background.png") as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Background texture importer unavailable.");
            ConfigureTexture(importer);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.spriteBorder = Vector4.zero;
            SetFullRect(importer);
            importer.SaveAndReimport();
        }

        static void ImportAtlas(string atlasName, string[] names, bool chrome, SliceManifest manifest)
        {
            string path = Root + "/" + atlasName + ".png";
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
                throw new InvalidDataException("Failed to read atlas PNG: " + path);
            try
            {
                Color32[] pixels = texture.GetPixels32();
                var definitions = new List<SpriteMetaData>();
                bool hasTransparentPixel = Array.Exists(pixels, p => p.a < 8);
                if (!hasTransparentPixel)
                    throw new InvalidDataException("Atlas must have a genuine transparent background before slicing: " + path);

                for (int i = 0; i < names.Length; i++)
                {
                    RectInt reference = (chrome ? ChromeReferenceBounds : IconReferenceBounds)[i];
                    RectInt trimmed = ResolveRectangle(reference, texture.width, texture.height);
                    Vector4 border = chrome ? BorderFor(names[i], trimmed, texture.width / (float)ReferenceSize) : Vector4.zero;
                    var entry = new SpriteMetaData
                    {
                        name = names[i], rect = new Rect(trimmed.x, trimmed.y, trimmed.width, trimmed.height),
                        alignment = (int)SpriteAlignment.Center, pivot = new Vector2(.5f, .5f), border = border
                    };
                    definitions.Add(entry);
                    ExportSlice(texture, trimmed, names[i], border);
                    manifest.sprites.Add(new SliceEntry
                    {
                        name = names[i], atlas = atlasName + ".png", semanticIndex = i,
                        atlasWidth = texture.width, atlasHeight = texture.height,
                        x = trimmed.x, y = trimmed.y, width = trimmed.width, height = trimmed.height,
                        normalizedX = reference.x / (float)ReferenceSize, normalizedY = reference.y / (float)ReferenceSize,
                        normalizedWidth = reference.width / (float)ReferenceSize, normalizedHeight = reference.height / (float)ReferenceSize,
                        borderLeft = border.x, borderBottom = border.y, borderRight = border.z, borderTop = border.w,
                        png = "Slices/" + names[i] + ".png"
                    });
                }

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Atlas texture importer unavailable: " + path);
                ConfigureTexture(importer);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.spritePixelsPerUnit = 100;
                SetFullRect(importer);
                // This API avoids a dependency on the optional 2D Sprite editor package.
#pragma warning disable 0618
                importer.spritesheet = definitions.ToArray();
#pragma warning restore 0618
                importer.SaveAndReimport();
                int count = 0;
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is Sprite) count++;
                if (count != names.Length) throw new InvalidOperationException($"Expected {names.Length} sprites in {path}, imported {count}.");
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        static RectInt Bounds(int xMin, int yMin, int xMax, int yMax) => new(xMin, yMin, xMax - xMin, yMax - yMin);

        static RectInt ResolveRectangle(RectInt reference, int width, int height)
        {
            float xScale = width / (float)ReferenceSize, yScale = height / (float)ReferenceSize;
            int left = Mathf.Clamp(Mathf.RoundToInt((reference.xMin - Padding) * xScale), 0, width);
            int right = Mathf.Clamp(Mathf.RoundToInt((reference.xMax + Padding) * xScale), 0, width);
            int top = Mathf.Clamp(Mathf.RoundToInt((reference.yMin - Padding) * yScale), 0, height);
            int bottom = Mathf.Clamp(Mathf.RoundToInt((reference.yMax + Padding) * yScale), 0, height);
            if (right <= left || bottom <= top) throw new InvalidDataException("Invalid equipment sprite rectangle: " + reference);
            return new RectInt(left, height - bottom, right - left, bottom - top);
        }

        static Vector4 BorderFor(string name, RectInt rectangle, float scale)
        {
            float horizontal = 35, vertical = 35;
            if (name == "divider") return Vector4.zero;
            if (name.StartsWith("slot-", StringComparison.Ordinal) || name == "selection") horizontal = vertical = 30;
            else if (name.StartsWith("tab-", StringComparison.Ordinal)) { horizontal = 35; vertical = 25; }
            else if (name.StartsWith("action-", StringComparison.Ordinal)) { horizontal = 35; vertical = 25; }
            else if (name.StartsWith("progress-", StringComparison.Ordinal)) { horizontal = 20; vertical = 8; }
            else if (name == "field") { horizontal = 35; vertical = 25; }
            horizontal = Mathf.Min(horizontal * scale + Padding, rectangle.width * .45f);
            vertical = Mathf.Min(vertical * scale + Padding, rectangle.height * .45f);
            return new Vector4(horizontal, vertical, horizontal, vertical);
        }

        static void ExportSlice(Texture2D atlas, RectInt rectangle, string name, Vector4 border)
        {
            string path = Root + "/Slices/" + name + ".png";
            var slice = new Texture2D(rectangle.width, rectangle.height, TextureFormat.RGBA32, false);
            try
            {
                slice.SetPixels(atlas.GetPixels(rectangle.x, rectangle.y, rectangle.width, rectangle.height));
                slice.Apply();
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(slice));
            }
            finally { UnityEngine.Object.DestroyImmediate(slice); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Slice texture importer unavailable: " + path);
            ConfigureTexture(importer);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.spritePivot = new Vector2(.5f, .5f);
            importer.spriteBorder = border;
            SetFullRect(importer);
            importer.SaveAndReimport();
        }

        static void ConfigureTexture(TextureImporter importer)
        {
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
        }

        static void SetFullRect(TextureImporter importer)
        {
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteExtrude = 0;
            importer.SetTextureSettings(settings);
        }

        [Serializable]
        sealed class SliceManifest
        {
            public int version = 2;
            public string layoutMode = "explicit-rectangles-normalized";
            public string origin = "bottom-left";
            public string normalizedOrigin = "top-left";
            public string normalizedBounds = "Measured main-alpha silhouette; exported pixel rectangles include transparent padding";
            public int referenceWidth = ReferenceSize, referenceHeight = ReferenceSize, transparentPadding = Padding;
            public List<SliceEntry> sprites = new();
        }
        [Serializable]
        sealed class SliceEntry
        {
            public string name, atlas, png;
            public int semanticIndex, atlasWidth, atlasHeight, x, y, width, height;
            public float normalizedX, normalizedY, normalizedWidth, normalizedHeight;
            public float borderLeft, borderBottom, borderRight, borderTop;
        }
    }
}

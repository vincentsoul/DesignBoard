using System;
using System.Collections.Generic;
using UnityEngine;

namespace ToyGarden
{
    /// <summary>Generated equipment artwork, shared by the IMGUI prototype and reusable Unity UI sprites.</summary>
    public static class GardenUISkin
    {
        public const string ResourceRoot = "UI/EquipmentSkin/";
        static readonly Dictionary<string, Sprite> sprites = new(StringComparer.Ordinal);
        static readonly Dictionary<string, Texture2D> textures = new(StringComparer.Ordinal);
        static bool initialized;
        static Texture2D background;
        static Material drawingMaterial;
        static bool shaderUnavailable;

        public static bool Ready { get { EnsureLoaded(); return background != null && sprites.Count >= 32; } }
        public static Texture2D Background { get { EnsureLoaded(); return background; } }

        public static Sprite GetSprite(string name)
        {
            EnsureLoaded();
            return name != null && sprites.TryGetValue(name, out Sprite result) ? result : null;
        }

        public static Texture2D GetTexture(string name)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(name)) return null;
            if (!textures.TryGetValue(name, out Texture2D result))
            {
                result = Resources.Load<Texture2D>(ResourceRoot + "Slices/" + name);
                textures[name] = result;
            }
            return result;
        }

        public static Texture2D GetIcon(GearSlot slot, ActorKind kind)
        {
            string name = slot switch
            {
                GearSlot.Weapon => kind == ActorKind.Hammer ? "icon-hammer" : kind == ActorKind.Ranger ? "icon-bow" : "icon-blade",
                GearSlot.Helmet => "icon-helmet", GearSlot.Chest => "icon-chest",
                GearSlot.Legs => "icon-legs", GearSlot.Gloves => "icon-gloves",
                GearSlot.Boots => "icon-boots", GearSlot.Ring => "icon-ring", GearSlot.Amulet => "icon-amulet",
                _ => null
            };
            return GetTexture(name) ?? GardenItemIcon.Get(slot, kind);
        }

        public static void Panel(Rect rect) => Draw("panel", rect);
        public static void Button(Rect rect, bool selected) => Draw(selected ? "action-gold" : "action-normal", rect);
        public static void Slot(Rect rect, GearQuality? quality, bool selected)
        {
            string name = quality.HasValue ? "slot-" + quality.Value.ToString().ToLowerInvariant() : "slot-common";
            Draw(name, rect, quality.HasValue ? Color.white : new Color(.62f, .62f, .68f, .72f));
            if (selected) Draw("selection", rect);
        }

        /// <summary>
        /// Draws sRGB artwork or a camera render texture without legacy IMGUI's extra gamma conversion.
        /// The custom material keeps the current GUI transform, alpha blending and scroll clipping.
        /// GUI.color is an authored sRGB tint, just like the rest of the equipment palette.
        /// </summary>
        public static void DrawTexture(Rect rect, Texture texture, ScaleMode mode = ScaleMode.StretchToFill)
        {
            if (texture == null || rect.width <= 0 || rect.height <= 0) return;
            Rect uv = new(0, 0, 1, 1);
            float imageAspect = (float)texture.width / texture.height;
            float rectangleAspect = rect.width / rect.height;
            if (mode == ScaleMode.ScaleToFit)
            {
                Vector2 center = rect.center;
                if (imageAspect > rectangleAspect) rect.height = rect.width / imageAspect;
                else rect.width = rect.height * imageAspect;
                rect.center = center;
            }
            else if (mode == ScaleMode.ScaleAndCrop)
            {
                if (imageAspect > rectangleAspect) uv.width = rectangleAspect / imageAspect;
                else uv.height = imageAspect / rectangleAspect;
                uv.center = new Vector2(.5f, .5f);
            }
            DrawUV(texture, rect, uv);
        }

        /// <summary>Draws an atlas sprite. Borders authored by the importer are respected as nine-slices.</summary>
        public static void Draw(string spriteName, Rect rect, Color? tint = null)
        {
            if (rect.width <= 0 || rect.height <= 0) return;
            Sprite sprite = GetSprite(spriteName);
            if (sprite == null) { DrawFallback(spriteName, rect, tint); return; }
            Color previous = GUI.color;
            GUI.color = tint ?? Color.white;
            try
            {
                Vector4 border = sprite.border;
                if (spriteName.StartsWith("icon-", StringComparison.Ordinal))
                {
                    float fit = Mathf.Min(rect.width / sprite.rect.width, rect.height / sprite.rect.height);
                    float width = sprite.rect.width * fit, height = sprite.rect.height * fit;
                    DrawRegion(sprite.texture, new Rect(rect.center.x - width * .5f, rect.center.y - height * .5f, width, height), sprite.rect);
                }
                else if (border.sqrMagnitude < .01f) DrawRegion(sprite.texture, rect, sprite.rect);
                else DrawSliced(sprite, rect);
            }
            finally { GUI.color = previous; }
        }

        static void EnsureLoaded()
        {
            if (initialized) return;
            initialized = true;
            background = Resources.Load<Texture2D>(ResourceRoot + "background");
            LoadAtlas("chrome-atlas");
            LoadAtlas("icons-atlas");
        }

        static void LoadAtlas(string name)
        {
            foreach (Sprite sprite in Resources.LoadAll<Sprite>(ResourceRoot + name))
                if (sprite != null) sprites[sprite.name] = sprite;
        }

        static void DrawSliced(Sprite sprite, Rect destination)
        {
            Rect source = sprite.rect;
            Vector4 b = sprite.border; // Unity order: left, bottom, right, top.
            // Preserve full bevels when a large source frame becomes a small inventory slot.
            // Large panels retain the source corner size instead of stretching the ornament.
            float scale = Mathf.Min(1f, destination.width / source.width, destination.height / source.height);
            scale = Mathf.Min(scale, destination.width / Mathf.Max(1f, b.x + b.z), destination.height / Mathf.Max(1f, b.y + b.w));
            float left = b.x * scale, right = b.z * scale, bottom = b.y * scale, top = b.w * scale;
            // GUI coordinates run from the top; texture coordinates run from the bottom.
            float[] dx = { destination.x, destination.x + left, destination.xMax - right, destination.xMax };
            float[] dy = { destination.y, destination.y + top, destination.yMax - bottom, destination.yMax };
            float[] sx = { source.x, source.x + b.x, source.xMax - b.z, source.xMax };
            float[] sy = { source.yMax, source.yMax - b.w, source.y + b.y, source.y };
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 3; column++)
                {
                    Rect target = Rect.MinMaxRect(dx[column], dy[row], dx[column + 1], dy[row + 1]);
                    Rect sample = Rect.MinMaxRect(sx[column], sy[row + 1], sx[column + 1], sy[row]);
                    if (target.width > 0 && target.height > 0 && sample.width > 0 && sample.height > 0)
                        DrawRegion(sprite.texture, target, sample);
                }
        }

        static void DrawRegion(Texture texture, Rect destination, Rect source)
        {
            DrawUV(texture, destination,
                new Rect(source.x / texture.width, source.y / texture.height, source.width / texture.width, source.height / texture.height));
        }

        static void DrawUV(Texture texture, Rect destination, Rect uv)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (drawingMaterial == null && !shaderUnavailable)
            {
                // A Resources reference retains the shader in standalone player builds.
                Shader shader = Resources.Load<Shader>(ResourceRoot + "EquipmentIMGUI");
                if (shader != null && shader.isSupported)
                    drawingMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                else
                {
                    shaderUnavailable = true;
                    Debug.LogWarning("Equipment UI color shader unavailable; using the legacy GUI texture fallback.");
                }
            }
            if (drawingMaterial == null)
            {
                GUI.DrawTextureWithTexCoords(destination, texture, uv, true);
                return;
            }
            Color tint = QualitySettings.activeColorSpace == ColorSpace.Linear ? GUI.color.linear : GUI.color;
            // Explicit white is neutral for our shader. Do not use the overload whose default
            // vertex color is 0.5: that convention belongs to Unity's legacy multiply-by-two shader.
            Graphics.DrawTexture(destination, texture, uv, 0, 0, 0, 0, tint, drawingMaterial, 0);
        }

        static void DrawFallback(string name, Rect rect, Color? tint)
        {
            if (string.IsNullOrEmpty(name) || name.StartsWith("icon-", StringComparison.Ordinal)) return;
            bool selected = name == "selection" || name == "tab-selected" || name == "action-gold";
            Color fill = selected ? new Color(.39f, .27f, .12f, .98f) : new Color(.12f, .1f, .19f, .97f);
            Color edge = selected ? new Color(.98f, .76f, .36f) : new Color(.43f, .35f, .48f);
            if (tint.HasValue) { fill *= tint.Value; edge *= tint.Value; }
            Color previous = GUI.color;
            try
            {
                if (name != "selection")
                {
                    GUI.color = QualitySettings.activeColorSpace == ColorSpace.Linear ? fill.linear : fill;
                    GUI.DrawTexture(rect, Texture2D.whiteTexture);
                }
                GUI.color = QualitySettings.activeColorSpace == ColorSpace.Linear ? edge.linear : edge;
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1, rect.width, 1), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.x, rect.y, 1, rect.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.xMax - 1, rect.y, 1, rect.height), Texture2D.whiteTexture);
            }
            finally { GUI.color = previous; }
        }
    }
}

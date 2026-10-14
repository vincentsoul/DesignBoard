using UnityEngine;

namespace ToyGarden
{
    /// <summary>A spatial overview of the actual chapter regions and gate state.</summary>
    public static class GardenChapterMap
    {
        static readonly Color background = new(.04f, .11f, .16f, .92f);
        static readonly Color openRoute = new(.43f, .86f, .56f);
        static readonly Color lockedRoute = new(1f, .72f, .27f);
        static readonly Color party = new(.3f, .82f, 1f);

        public static void Draw(GardenGame game, Rect bounds, bool expanded)
        {
            var chapter = game.Chapter;
            if (chapter == null || chapter.regions == null || chapter.regions.Length == 0) return;
            Fill(bounds, background);
            var label = new GUIStyle(GUI.skin.label)
            {
                fontSize = expanded ? 15 : 11,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontStyle = FontStyle.Bold
            };
            label.normal.textColor = Color.white;
            Rect canvas = new(bounds.x + 13, bounds.y + 13, bounds.width - 26, bounds.height - 26);
            if (!expanded)
            {
                GUI.Label(new Rect(bounds.x, bounds.y + 2, bounds.width, 19), "M  CHAPTER MAP", label);
                canvas.y += 15; canvas.height -= 15;
            }
            Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var region in chapter.regions)
            {
                min = Vector2.Min(min, new Vector2(region.center.x, region.center.z) - region.size * .5f - Vector2.one * 5);
                max = Vector2.Max(max, new Vector2(region.center.x, region.center.z) + region.size * .5f + Vector2.one * 5);
            }
            float ratio = Mathf.Min(canvas.width / (max.x - min.x), canvas.height / (max.y - min.y));
            Vector2 origin = canvas.center - new Vector2(max.x - min.x, max.y - min.y) * ratio * .5f;
            Vector2 Project(Vector3 position) => origin + new Vector2(position.x - min.x, max.y - position.z) * ratio;

            for (int i = 0; i + 1 < chapter.regions.Length; i++)
                Line(Project(chapter.regions[i].center), Project(chapter.regions[i + 1].center),
                    chapter.IsGateOpen(i) ? openRoute : lockedRoute, expanded ? 5 : 3);
            if (chapter.regions.Length > 4)
                Line(Project(chapter.regions[4].center), Project(chapter.regions[1].center),
                    chapter.IsGateOpen(7) ? openRoute : lockedRoute, expanded ? 5 : 3);
            for (int i = 0; i < chapter.regions.Length; i++)
            {
                var region = chapter.regions[i];
                Vector2 p = Project(region.center);
                Vector2 size = region.size * ratio;
                bool current = region == chapter.CurrentRegion;
                Rect tile = new(p.x - size.x / 2, p.y - size.y / 2, size.x, size.y);
                Fill(tile, current ? new Color(.18f, .44f, .49f) : new Color(.2f, .31f, .33f));
                if (current) Fill(new Rect(tile.x, tile.y, tile.width, expanded ? 4 : 2), party);
                GUI.Label(tile, expanded ? $"{i + 1:00}\n{region.title}" : (i + 1).ToString("00"), label);
            }
            foreach (var gate in chapter.Gates)
            {
                Vector2 p = Project((gate.before + gate.after) * .5f);
                float size = expanded ? 11 : 5;
                Fill(new Rect(p.x - size / 2, p.y - size / 2, size, size), gate.IsOpen ? openRoute : lockedRoute);
            }
            if (game.ActiveHero != null)
            {
                Vector2 p = Project(game.ActiveHero.transform.position);
                float size = expanded ? 12 : 7;
                Fill(new Rect(p.x - size / 2 - 2, p.y - size / 2 - 2, size + 4, size + 4), Color.white);
                Fill(new Rect(p.x - size / 2, p.y - size / 2, size, size), party);
            }
        }

        static void Line(Vector2 start, Vector2 end, Color color, float width)
        {
            // Chapter routes are axis aligned. Draw in the existing scaled GUI space;
            // rotating GUI.matrix around a screen-space pivot displaces lines on Retina.
            if (Mathf.Abs(end.x - start.x) < .1f)
                Fill(new Rect(start.x - width * .5f, Mathf.Min(start.y, end.y), width, Mathf.Abs(end.y - start.y)), color);
            else
                Fill(new Rect(Mathf.Min(start.x, end.x), start.y - width * .5f, Mathf.Abs(end.x - start.x), width), color);
        }

        static void Fill(Rect rect, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}

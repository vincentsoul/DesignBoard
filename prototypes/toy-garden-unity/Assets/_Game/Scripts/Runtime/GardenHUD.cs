using UnityEngine;

namespace ToyGarden
{
    public sealed class GardenHUD : MonoBehaviour
    {
        GardenGame game;
        GUIStyle title, body, small, center, button;
        readonly Color ink = new(.055f, .105f, .15f, .93f);
        readonly Color gold = new(1f, .82f, .38f);
        readonly Color[] roles = { new(.32f, .78f, .96f), new(1f, .64f, .26f), new(.61f, .87f, .51f) };

        void Start() => game = GetComponent<GardenGame>();

        void InitStyles()
        {
            title = new GUIStyle(GUI.skin.label) { fontSize = 25, fontStyle = FontStyle.Bold };
            body = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
            small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            center = new GUIStyle(body) { alignment = TextAnchor.MiddleCenter };
            button = new GUIStyle(GUI.skin.button) { fontSize = 16, padding = new RectOffset(12, 12, 9, 9) };
            title.normal.textColor = body.normal.textColor = small.normal.textColor = center.normal.textColor = Color.white;
        }

        void OnGUI()
        {
            if (game == null) return;
            if (title == null) InitStyles();
            var oldMatrix = GUI.matrix;
            float scale = Mathf.Clamp(Screen.width / 1280f, .65f, 1.7f);
            float w = Screen.width / scale, h = Screen.height / scale;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            Panel(new Rect(24, 22, 345, 86), ink);
            GUI.Label(new Rect(42, 32, 320, 32), "WINDMILL GARDEN", title);
            GUI.Label(new Rect(43, 69, 310, 24), "TOY GARDEN  /  PARTY ACTION PROTOTYPE", small);
            Panel(new Rect(w - 370, 22, 346, 86), ink);
            GUI.Label(new Rect(w - 351, 34, 310, 42), game.Objective, body);
            GUI.Label(new Rect(w - 351, 78, 310, 22), $"ENCOUNTERS  {game.ClearedCount}/2     SUPPLIES  {(game.HasChest ? "FOUND" : "HIDDEN")}", small);
            if (game.Message.Length > 0)
            {
                Panel(new Rect(w / 2 - 250, 120, 500, 40), new Color(.05f, .12f, .18f, .8f));
                GUI.Label(new Rect(w / 2 - 240, 124, 480, 32), game.Message, center);
            }
            for (int i = 0; i < game.Heroes.Count; i++)
            {
                var hero = game.Heroes[i];
                float x = 24 + i * 209;
                var r = new Rect(x, h - 126, 199, 88);
                Panel(r, ink);
                if (hero == game.ActiveHero) Panel(new Rect(x, h - 126, 199, 4), roles[i]);
                GUI.color = Color.clear;
                if (GUI.Button(r, GUIContent.none)) game.SwitchTo(i);
                GUI.color = Color.white;
                GUI.Label(new Rect(x + 12, h - 117, 175, 23), GardenGame.RoleName(hero.kind) + (hero == game.ActiveHero ? "  *" : ""), body);
                var bar = new Rect(x + 12, h - 87, 175, 7);
                Panel(bar, new Color(.2f, .26f, .3f));
                Panel(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(hero.Health / Mathf.Max(1, hero.MaxHealth)), bar.height), roles[i]);
                string status = !hero.IsAlive ? "DOWN" : $"HP {Mathf.CeilToInt(hero.Health)} / {hero.MaxHealth:0}    Q {(hero.SkillCooldownRemaining > 0 ? hero.SkillCooldownRemaining.ToString("0.0") + "s" : "READY")}";
                GUI.Label(new Rect(x + 12, h - 70, 178, 24), status, small);
            }
            Panel(new Rect(0, h - 29, w, 29), ink);
            GUI.Label(new Rect(24, h - 27, w - 40, 26), "WASD / L-STICK  MOVE     LMB / J / A  ATTACK     SPACE / B  DODGE     Q / X  SKILL     TAB / LB  SWITCH     E / Y  INTERACT     ESC  PAUSE", small);
            string hint = game.InteractionHint;
            if (hint.Length > 0)
            {
                Panel(new Rect(w / 2 - 205, h - 183, 410, 40), ink);
                GUI.Label(new Rect(w / 2 - 200, h - 179, 400, 32), hint, center);
            }
            DrawEnemyBars(scale);
            if (game.Paused || game.GameOver || game.Won)
            {
                Panel(new Rect(0, 0, w, h), new Color(.025f, .07f, .11f, .77f));
                var heading = game.Won ? "GARDEN RESTORED" : game.GameOver ? "YOUR PARTY HAS FALLEN" : "PAUSED";
                var centeredTitle = new GUIStyle(title) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(w / 2 - 300, h / 2 - 95, 600, 45), heading, centeredTitle);
                GUI.Label(new Rect(w / 2 - 260, h / 2 - 45, 520, 50), game.Paused ? "Take a breath. Your party is waiting." :
                    $"{game.Elapsed / 60:0} min  {game.Elapsed % 60:00} sec     Supplies: {(game.HasChest ? "recovered" : "unexplored")}", center);
                if (GUI.Button(new Rect(w / 2 - 110, h / 2 + 20, 220, 48), game.Paused ? "RESUME" : "PLAY AGAIN  /  R", button))
                {
                    if (game.Paused) game.TogglePause(); else game.RestartGame();
                }
            }
            GUI.matrix = oldMatrix;
            GUI.color = Color.white;
        }

        void DrawEnemyBars(float scale)
        {
            var camera = Camera.main;
            if (camera == null || game.ActiveHero == null) return;
            foreach (var enemy in game.Enemies)
            {
                if (!enemy.IsAlive || !game.IsEncounterActive(enemy.encounterId) || GardenGame.FlatDistance(enemy.transform.position, game.ActiveHero.transform.position) > 23) continue;
                var p = camera.WorldToScreenPoint(enemy.transform.position + Vector3.up * (enemy.kind == ActorKind.Boss ? 3.5f : 2.2f));
                if (p.z <= 0) continue;
                float x = p.x / scale - 37, y = (Screen.height - p.y) / scale;
                Panel(new Rect(x, y, 74, 6), ink);
                Panel(new Rect(x + 1, y + 1, 72 * Mathf.Clamp01(enemy.Health / Mathf.Max(1, enemy.MaxHealth)), 4), gold);
            }
        }

        static void Panel(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}

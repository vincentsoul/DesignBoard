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
            title = new GUIStyle(GUI.skin.label) { font = GardenLanguage.Font, fontSize = 25, fontStyle = FontStyle.Bold };
            body = new GUIStyle(GUI.skin.label) { font = GardenLanguage.Font, fontSize = 16, wordWrap = true };
            small = new GUIStyle(GUI.skin.label) { font = GardenLanguage.Font, fontSize = 13, wordWrap = true };
            center = new GUIStyle(body) { alignment = TextAnchor.MiddleCenter };
            button = new GUIStyle(GUI.skin.button) { font = GardenLanguage.Font, fontSize = 16, padding = new RectOffset(12, 12, 9, 9) };
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
            GUI.Label(new Rect(42, 32, 320, 32), game.Chapter != null ? "风车谷" : "风车花园", title);
            GUI.Label(new Rect(43, 69, 310, 30), game.Chapter?.CurrentRegion?.title ?? "箱庭冒险 · 小队战斗", body);
            Panel(new Rect(w - 425, 22, 401, 108), ink);
            GUI.Label(new Rect(w - 407, 34, 365, 62), game.Objective, body);
            GUI.Label(new Rect(w - 407, 100, 365, 25), game.Chapter != null ?
                $"战斗 {game.ClearedCount}/{game.TotalEncounters}   谜题 {game.Chapter.SolvedCount}/7   宝箱 {game.Chapter.SupplyCount}" :
                $"战斗 {game.ClearedCount}/2   补给 {(game.HasChest ? "已找到" : "未发现")}", small);
            if (game.Message.Length > 0)
            {
                Panel(new Rect(w / 2 - 280, 145, 560, 48), new Color(.05f, .12f, .18f, .8f));
                GUI.Label(new Rect(w / 2 - 270, 149, 540, 40), game.Message, center);
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
                string status = !hero.IsAlive ? "已倒下" : $"生命 {Mathf.CeilToInt(hero.Health)} / {hero.MaxHealth:0}    Q {(hero.SkillCooldownRemaining > 0 ? hero.SkillCooldownRemaining.ToString("0.0") + "秒" : "就绪")}";
                GUI.Label(new Rect(x + 12, h - 70, 178, 24), status, small);
            }
            Panel(new Rect(0, h - 29, w, 29), ink);
            GUI.Label(new Rect(24, h - 27, w - 40, 26), "WASD 移动   J / 左键 攻击   空格 闪避   Q 技能   Tab 切人   E 交互 / 推箱   M 地图   Esc 暂停", small);
            string hint = game.InteractionHint;
            if (hint.Length > 0)
            {
                Panel(new Rect(w / 2 - 295, h - 204, 590, 65), ink);
                GUI.Label(new Rect(w / 2 - 285, h - 199, 570, 56), hint, center);
            }
            DrawEnemyBars(scale);
            if (game.Chapter != null && !game.MapOpen)
                GardenChapterMap.Draw(game, new Rect(w - 272, h - 251, 248, 210), false);
            if (game.MapOpen)
            {
                Panel(new Rect(0, 0, w, h), new Color(.025f, .07f, .11f, 1f));
                GUI.Label(new Rect(45, 25, w - 90, 45), "风车谷 · 章节地图", title);
                GUI.Label(new Rect(45, 77, w - 90, 50), game.Objective, body);
                GardenChapterMap.Draw(game, new Rect(45, 140, w - 400, h - 190), true);
                GUI.Label(new Rect(w - 320, 145, 290, 55), "八座花园，一段冒险", body);
                GUI.Label(new Rect(w - 320, 214, 285, 82), "蓝色：小队位置\n金色：尚未开放\n绿色：已经开放", body);
                float yy = 305;
                foreach (var puzzle in game.Chapter.Puzzles)
                {
                    GUI.Label(new Rect(w - 320, yy, 290, 46), (puzzle.Solved ? "[已完成] " : "[待完成] ") + puzzle.title, body);
                    yy += 45;
                }
                GUI.Label(new Rect(w - 320, h - 66, 290, 38), "M / Esc 返回冒险", body);
            }
            if (game.Paused || game.GameOver || game.Won)
            {
                Panel(new Rect(0, 0, w, h), new Color(.025f, .07f, .11f, .77f));
                var heading = game.Won ? "风车谷恢复生机" : game.GameOver ? "小队全员倒下" : "已暂停";
                var centeredTitle = new GUIStyle(title) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(w / 2 - 300, h / 2 - 95, 600, 45), heading, centeredTitle);
                GUI.Label(new Rect(w / 2 - 260, h / 2 - 45, 520, 50), game.Paused ? "歇一会儿，小队正在等你。" :
                    $"{Mathf.FloorToInt(game.Elapsed / 60)} 分 {game.Elapsed % 60:00} 秒     补给： {(game.Chapter != null ? game.Chapter.SupplyCount.ToString() : game.HasChest ? "已收集" : "未探索")}", center);
                if (GUI.Button(new Rect(w / 2 - 110, h / 2 + 20, 220, 48), game.Paused ? "继续冒险" : "重新开始 / R", button))
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

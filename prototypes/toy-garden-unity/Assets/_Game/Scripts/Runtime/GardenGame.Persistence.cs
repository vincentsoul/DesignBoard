using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ToyGarden
{
    [Serializable]
    public sealed class GardenSaveData
    {
        public int version = 1;
        public string progression;
        public float elapsed;
        public ActorKind activeHero;
        public bool won, gameOver;
        public List<int> active = new(), cleared = new(), chests = new();
        public List<string> claims = new();
        public List<GardenActorSave> actors = new();
        public List<GardenPuzzleSave> puzzles = new();
    }

    [Serializable]
    public sealed class GardenActorSave
    {
        public string key;
        public Vector3 position;
        public float health, skill, dodge;
    }

    [Serializable]
    public sealed class GardenPuzzleSave
    {
        public int id, sequence;
        public bool solved;
        public Vector2Int crate;
        public int[] values;
    }

    public sealed partial class GardenGame
    {
        // Tests capture this flag before scene creation; existing instances never change save ownership.
        public static bool PersistentSavesEnabled { get; set; } = true;
        public static string PersistenceDirectoryOverride { get; set; }
        public GardenProgression Progression { get; private set; }
        public bool EquipmentOpen { get; private set; }
        public string SaveStatus { get; private set; } = "尚未保存";
        public string SavePath => Path.Combine(saveDirectory ?? Application.persistentDataPath, "garden-adventure-v1.json");
        readonly HashSet<string> rewardClaims = new();
        bool usePersistentSaves, loadBlocked;
        string saveDirectory;
        float saveTimer;

        public bool RewardEncounter(int id)
        {
            if (!IsEncounterCleared(id) || !rewardClaims.Add("encounter:" + id)) return false;
            var result = Progression.GrantReward(id == TotalEncounters ? 1200 : 400, id == TotalEncounters ? 30 : 12,
                id == TotalEncounters ? 16 : 5, id == TotalEncounters ? 200 : 80,
                Progression.GetHero(ActiveHero.kind).level);
            FinishReward("战斗胜利！", result);
            return true;
        }

        public bool GrantPuzzleReward(int id)
        {
            if (Chapter == null || !Chapter.IsPuzzleSolved(id) || !rewardClaims.Add("puzzle:" + id)) return false;
            FinishReward("机关解开！", Progression.GrantReward(150, 5, 2, 35));
            return true;
        }

        public bool GrantChestReward(int index)
        {
            if (Chapter == null || !Chapter.Mechanisms.Any(m => m.kind == MechanismKind.Chest && m.index == index && m.Used)
                || !rewardClaims.Add("chest:" + index)) return false;
            FinishReward("宝箱已开启！", Progression.GrantReward(300, 10, 5, 45, Progression.GetHero(ActiveHero.kind).level));
            return true;
        }

        void FinishReward(string prefix, GearResult result)
        {
            RefreshHeroStats();
            ShowMessage(prefix + result.message, 6f);
            if (!SaveProgress()) ShowMessage(prefix + result.message + " 自动保存失败，请检查磁盘。", 8f);
        }

        public GardenSaveData CaptureSave()
        {
            return new GardenSaveData
            {
                progression = Progression.ToJson(false), elapsed = Elapsed,
                activeHero = ActiveHero != null ? ActiveHero.kind : ActorKind.Blade, won = Won, gameOver = GameOver,
                active = Enumerable.Range(1, TotalEncounters).Where(IsEncounterActive).ToList(),
                cleared = Enumerable.Range(1, TotalEncounters).Where(IsEncounterCleared).ToList(),
                claims = rewardClaims.OrderBy(x => x).ToList(),
                chests = Chapter == null ? new() : Chapter.Mechanisms.Where(m => m.kind == MechanismKind.Chest && m.Used).Select(m => m.index).ToList(),
                actors = Heroes.Concat(Enemies).Select(a => new GardenActorSave { key = ActorKey(a), position = SafePosition(a.transform.position, a.HomePosition),
                    health = a.Health, skill = a.SkillCooldownRemaining, dodge = a.DodgeCooldownRemaining }).ToList(),
                puzzles = Chapter == null ? new() : Chapter.Puzzles.Select(p => new GardenPuzzleSave { id = p.id, solved = p.Solved,
                    crate = p.CrateCell, sequence = p.SequenceProgress, values = p.Values.ToArray() }).ToList()
            };
        }

        public bool SaveProgress()
        {
            if (Progression == null) return false;
            if (!usePersistentSaves) return true;
            if (loadBlocked) return false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
                string temp = SavePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(CaptureSave(), true));
                if (File.Exists(SavePath)) File.Replace(temp, SavePath, SavePath + ".bak");
                else File.Move(temp, SavePath);
                SaveStatus = "已自动保存";
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException)
            {
                SaveStatus = "保存失败：请检查磁盘空间和目录权限";
                Debug.LogWarning("Garden save failed: " + error.Message);
                return false;
            }
        }

        void LoadProgress()
        {
            saveDirectory = PersistenceDirectoryOverride ?? Application.persistentDataPath;
            if (!usePersistentSaves || (!File.Exists(SavePath) && !File.Exists(SavePath + ".bak"))) return;
            if (TryReadSave(SavePath, out var snapshot, out var progression))
            {
                RestoreSave(snapshot, progression);
                SaveStatus = "已读取上次冒险";
                return;
            }
            if (TryReadSave(SavePath + ".bak", out snapshot, out progression))
            {
                try { if (File.Exists(SavePath)) File.Move(SavePath, SavePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")); }
                catch (IOException) { loadBlocked = true; }
                catch (UnauthorizedAccessException) { loadBlocked = true; }
                RestoreSave(snapshot, progression);
                SaveStatus = loadBlocked ? "主存档损坏：备份已读取，自动保存已暂停" : "已从备份恢复冒险";
            }
            else
            {
                loadBlocked = true;
                SaveStatus = "存档无法读取：已保留原文件，自动保存已暂停";
            }
            Debug.LogWarning(SaveStatus);
        }

        bool TryReadSave(string path, out GardenSaveData snapshot, out GardenProgression progression)
        {
            snapshot = null; progression = null;
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 8000000) return false;
                snapshot = JsonUtility.FromJson<GardenSaveData>(File.ReadAllText(path));
                return snapshot != null && snapshot.version == 1 && snapshot.actors != null && snapshot.actors.Count <= 256
                    && snapshot.puzzles != null && snapshot.puzzles.Count <= 64 && snapshot.chests != null
                    && snapshot.active != null && snapshot.cleared != null && snapshot.claims != null
                    && ValidWorldSnapshot(snapshot)
                    && GardenProgression.TryFromJson(snapshot.progression, out progression, out _);
            }
            catch (Exception error) when (error is ArgumentException || error is IOException || error is UnauthorizedAccessException)
            { return false; }
        }

        bool ValidWorldSnapshot(GardenSaveData snapshot)
        {
            var expected = Heroes.Concat(Enemies).ToArray();
            if (snapshot.actors.Count != expected.Length || snapshot.actors.Any(a => a == null)
                || snapshot.actors.Select(a => a.key).Distinct().Count() != expected.Length) return false;
            foreach (var actor in expected)
            {
                var state = snapshot.actors.FirstOrDefault(a => a.key == ActorKey(actor));
                if (state == null || !float.IsFinite(state.health) || state.health < 0) return false;
                if (actor.IsEnemy && snapshot.cleared.Contains(actor.encounterId) && state.health > 0) return false;
            }
            foreach (int id in Enumerable.Range(1, TotalEncounters))
            {
                var members = Enemies.Where(e => e.encounterId == id).ToArray();
                if (members.Length > 0 && !snapshot.cleared.Contains(id)
                    && members.All(e => snapshot.actors.First(a => a.key == ActorKey(e)).health <= 0)) return false;
            }
            if (Chapter != null && (snapshot.puzzles.Count != Chapter.Puzzles.Count || snapshot.puzzles.Any(p => p == null)
                || snapshot.puzzles.Select(p => p.id).Distinct().Count() != Chapter.Puzzles.Count
                || Chapter.Puzzles.Any(p => !snapshot.puzzles.Any(s => s.id == p.id)))) return false;
            return true;
        }

        void RestoreSave(GardenSaveData snapshot, GardenProgression progression)
        {
            Progression = progression;
            RefreshHeroStats();
            foreach (int id in snapshot.cleared) if (id > 0 && id <= TotalEncounters) clearedEncounters[id] = true;
            foreach (int id in snapshot.active) if (id > 0 && id <= TotalEncounters && !clearedEncounters[id]) activeEncounters[id] = true;
            foreach (string key in snapshot.claims) if (!string.IsNullOrEmpty(key)) rewardClaims.Add(key);
            if (Chapter != null)
            {
                foreach (var puzzle in Chapter.Puzzles)
                {
                    var state = snapshot.puzzles.FirstOrDefault(s => s != null && s.id == puzzle.id);
                    if (state != null) puzzle.RestoreSnapshot(state);
                }
                foreach (var chest in Chapter.Mechanisms.Where(m => m.kind == MechanismKind.Chest)) chest.SetUsed(snapshot.chests.Contains(chest.index));
                Chapter.RefreshGates();
            }
            foreach (var actor in Heroes.Concat(Enemies))
            {
                var state = snapshot.actors.FirstOrDefault(s => s != null && s.key == ActorKey(actor));
                if (state == null) continue;
                float health = float.IsFinite(state.health) ? state.health : actor.MaxHealth;
                actor.RestoreSnapshot(health, SafePosition(state.position, actor.HomePosition),
                    float.IsFinite(state.skill) ? state.skill : 0, float.IsFinite(state.dodge) ? state.dodge : 0);
                actor.IsPlayerControlled = false;
            }
            ActiveHero = Heroes.FirstOrDefault(h => h.kind == snapshot.activeHero && h.IsAlive)
                ?? Heroes.FirstOrDefault(h => h.IsAlive) ?? Heroes.FirstOrDefault();
            if (ActiveHero != null) ActiveHero.IsPlayerControlled = true;
            Elapsed = float.IsFinite(snapshot.elapsed) ? Mathf.Max(0, snapshot.elapsed) : 0;
            GameOver = !Heroes.Any(h => h.IsAlive);
            Won = snapshot.won && Chapter != null && Chapter.CanExit;
            Physics.SyncTransforms();
            Chapter?.TickChapter();
        }

        static string ActorKey(GardenActor actor) => FormattableString.Invariant($"{(int)actor.kind}|{actor.encounterId}|{actor.HomePosition.x:R}|{actor.HomePosition.y:R}|{actor.HomePosition.z:R}");
        static Vector3 SafePosition(Vector3 value, Vector3 fallback) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z) && value.y >= -4f && value.sqrMagnitude < 1000000f ? value : fallback;
        void OnApplicationQuit() => SaveProgress();
    }
}

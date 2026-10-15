using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ToyGarden
{
    [Serializable]
    public sealed class ChapterRegion
    {
        public string title, subtitle;
        public Vector3 center;
        public Vector2 size;
    }

    [Serializable]
    public sealed class ChapterEncounter
    {
        public int id;
        public Vector3 center;
        public float radius;
        public int requiredGate = -1;
    }

    public sealed class GardenChapter : MonoBehaviour
    {
        public ChapterRegion[] regions = Array.Empty<ChapterRegion>();
        public ChapterEncounter[] encounters = Array.Empty<ChapterEncounter>();
        public List<GardenPuzzle> Puzzles { get; private set; } = new();
        public List<GardenGate> Gates { get; private set; } = new();
        public List<GardenMechanism> Mechanisms { get; private set; } = new();
        public ChapterRegion CurrentRegion { get; private set; }
        public int SolvedCount => Puzzles.Count(puzzle => puzzle.Solved);
        public int SupplyCount => Mechanisms.Count(mechanism => mechanism.kind == MechanismKind.Chest && mechanism.Used);
        public bool CanExit => game != null && Puzzles.All(puzzle => puzzle.Solved)
            && encounters.All(encounter => game.IsEncounterCleared(encounter.id));
        public string ProgressText => $"机关 {SolvedCount}/{Puzzles.Count}    战斗 {(game != null ? game.ClearedCount : 0)}/{encounters.Length}    补给箱 {SupplyCount}";
        public string Objective
        {
            get
            {
                if (game == null) return "探索风车谷";
                if (game.Won) return "风车谷恢复了生机，本章已完成。";
                if (game.GameOver) return "队伍已全部倒下，请重新挑战本章。";
                if (CanExit) return "前往山顶拱门，按 E 完成本章";
                var route = Gates.FirstOrDefault(gate => gate.id != 7 && !gate.IsOpen);
                if (route != null)
                {
                    var puzzle = Puzzles.FirstOrDefault(item => item.id == route.puzzleId);
                    if (puzzle != null && !puzzle.Solved) return $"解开机关：{puzzle.title}";
                    if (route.encounterId > 0 && !game.IsEncounterCleared(route.encounterId))
                        return "击败本区域的敌人，打开已修复的通路";
                }
                var next = Puzzles.FirstOrDefault(puzzle => !puzzle.Solved);
                if (next != null) return $"解开机关：{next.title}";
                var battle = encounters.FirstOrDefault(encounter => !game.IsEncounterCleared(encounter.id));
                return battle != null ? "前往山顶，击败风车守卫" : "寻找山顶出口";
            }
        }

        GardenGame game;
        bool initialized;

        public void Initialize(GardenGame owner)
        {
            game = owner;
            Puzzles = FindObjectsByType<GardenPuzzle>().OrderBy(item => item.id).ToList();
            Gates = FindObjectsByType<GardenGate>().OrderBy(item => item.id).ToList();
            Mechanisms = FindObjectsByType<GardenMechanism>()
                .OrderBy(item => item.puzzleId).ThenBy(item => item.index).ToList();
            foreach (var mechanism in Mechanisms) mechanism.CaptureInitialState();
            foreach (var puzzle in Puzzles) puzzle.Initialize(this, Mechanisms);
            initialized = true;
            ResetChapter();
        }

        public void TickChapter()
        {
            if (!initialized || game == null || !game.IsPlaying || game.ActiveHero == null) return;
            UpdateRegion();
            RefreshGates();
            foreach (var encounter in encounters)
            {
                Vector3 position = game.ActiveHero.transform.position;
                if (CanActivateEncounter(encounter.id) && Mathf.Abs(position.y - encounter.center.y) < 3f
                    && GardenGame.FlatDistance(position, encounter.center) < encounter.radius)
                    game.ActivateEncounter(encounter.id);
            }
        }

        void UpdateRegion()
        {
            if (game?.ActiveHero == null || regions.Length == 0) return;
            Vector3 position = game.ActiveHero.transform.position;
            ChapterRegion nearest = regions.OrderBy(region => GardenGame.FlatDistance(position, region.center)).First();
            if (nearest == CurrentRegion) return;
            CurrentRegion = nearest;
            ShowMessage($"{nearest.title} · {nearest.subtitle}", 4f);
        }

        public void ResetChapter()
        {
            foreach (var mechanism in Mechanisms) mechanism.ResetMechanism();
            foreach (var puzzle in Puzzles) puzzle.ResetPuzzle();
            foreach (var gate in Gates) gate.SetOpen(false);
            CurrentRegion = null;
            Physics.SyncTransforms();
            UpdateRegion();
        }

        public bool IsPuzzleSolved(int id) => Puzzles.Any(puzzle => puzzle.id == id && puzzle.Solved);
        public bool IsGateOpen(int id) => id < 0 || Gates.Any(gate => gate.id == id && gate.IsOpen);

        public bool CanActivateEncounter(int id)
        {
            var encounter = encounters.FirstOrDefault(item => item.id == id);
            return game != null && game.IsPlaying && encounter != null && !game.IsEncounterCleared(id)
                && IsGateOpen(encounter.requiredGate);
        }

        internal void RefreshGates()
        {
            if (game == null) return;
            foreach (var gate in Gates)
            {
                bool open = (gate.puzzleId < 0 || IsPuzzleSolved(gate.puzzleId))
                    && (gate.encounterId <= 0 || game.IsEncounterCleared(gate.encounterId));
                if (gate.IsOpen == open) continue;
                gate.SetOpen(open);
                if (open) ShowMessage(gate.id == 7 ? "山谷捷径已经开放！" : "通往下一片花园的道路已经开放！", 4f);
            }
        }

        internal void PuzzleSolved(GardenPuzzle puzzle)
        {
            ShowMessage($"已解开机关：{puzzle.title}！", 4f);
            RefreshGates();
            if (Gates.Any(gate => gate.puzzleId == puzzle.id && !gate.IsOpen && gate.encounterId > 0))
                ShowMessage($"已解开{puzzle.title}。击败本区域敌人后即可开启通路。", 4f);
        }

        public string InteractionHint
        {
            get
            {
                var mechanism = NearestMechanism();
                if (mechanism == null) return "";
                if (mechanism.kind == MechanismKind.Chest) return mechanism.Used ? "已领取这份补给" : "E / Y：打开补给箱（存活队员各恢复 35 点生命）";
                if (mechanism.kind == MechanismKind.Exit) return CanExit ? "E / Y：完成风车谷章节" : "解开全部 7 处机关并完成 6 场战斗后即可离开";
                return FindPuzzle(mechanism)?.HintFor(mechanism) ?? "";
            }
        }

        public bool TryInteract()
        {
            var mechanism = NearestMechanism();
            if (mechanism == null || game == null) return false;
            switch (mechanism.kind)
            {
                case MechanismKind.Chest:
                    if (mechanism.Used) return false;
                    mechanism.SetUsed(true);
                    foreach (var hero in game.Heroes) if (hero.IsAlive) hero.Heal(35f);
                    ShowMessage("已取得补给，存活的队员各恢复 35 点生命。", 4f);
                    return true;
                case MechanismKind.Exit:
                    if (!CanExit) { ShowMessage("请先修复山谷中的机关，并击败风车守卫。"); return false; }
                    game.CompleteAdventure();
                    return true;
                default:
                    return FindPuzzle(mechanism)?.TryActivate(mechanism, game.ActiveHero.transform.position) ?? false;
            }
        }

        GardenPuzzle FindPuzzle(GardenMechanism mechanism) => Puzzles.FirstOrDefault(puzzle => puzzle.id == mechanism.puzzleId);

        GardenMechanism NearestMechanism()
        {
            if (game == null || !game.IsPlaying || game.ActiveHero == null) return null;
            Vector3 position = game.ActiveHero.transform.position;
            GardenMechanism nearest = null;
            float distance = float.PositiveInfinity;
            foreach (var mechanism in Mechanisms)
            {
                if (!CanUseMechanism(mechanism, position)) continue;
                float next = Vector3.SqrMagnitude(mechanism.transform.position - position);
                if (next < distance) { nearest = mechanism; distance = next; }
            }
            return nearest;
        }

        internal bool CanUseMechanism(GardenMechanism mechanism, Vector3 actorPosition)
        {
            if (!initialized || game == null || !game.IsPlaying || mechanism == null || !mechanism.gameObject.activeInHierarchy) return false;
            Vector3 delta = mechanism.transform.position - actorPosition;
            if (delta.magnitude > mechanism.interactionRange || Mathf.Abs(delta.y) > 2f) return false;
            Vector3 start = actorPosition + Vector3.up * .75f;
            Vector3 finish = mechanism.transform.position + Vector3.up * .75f;
            Vector3 ray = finish - start;
            if (ray.sqrMagnitude < .01f) return true;
            foreach (var hit in Physics.RaycastAll(start, ray.normalized, ray.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<GardenActor>() != null
                    || hit.collider.GetComponentInParent<GardenMechanism>() == mechanism) continue;
                return false;
            }
            return true;
        }

        internal void ShowMessage(string text, float seconds = 2f) => game?.ShowMessage(text, seconds);
    }
}

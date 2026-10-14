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
        public string ProgressText => $"ROUTES  {SolvedCount}/{Puzzles.Count}    BATTLES  {(game != null ? game.ClearedCount : 0)}/{encounters.Length}    CHESTS  {SupplyCount}";
        public string Objective
        {
            get
            {
                if (game == null) return "Explore Windmill Valley";
                if (game.Won) return "Windmill Valley restored. The chapter is complete.";
                if (game.GameOver) return "Your party has fallen. Begin the chapter again.";
                if (CanExit) return "Reach the summit arch and press E to finish";
                var route = Gates.FirstOrDefault(gate => gate.id != 7 && !gate.IsOpen);
                if (route != null)
                {
                    var puzzle = Puzzles.FirstOrDefault(item => item.id == route.puzzleId);
                    if (puzzle != null && !puzzle.Solved) return $"Restore {puzzle.title}";
                    if (route.encounterId > 0 && !game.IsEncounterCleared(route.encounterId))
                        return "Clear this garden's encounter to open the restored route";
                }
                var next = Puzzles.FirstOrDefault(puzzle => !puzzle.Solved);
                if (next != null) return $"Restore {next.title}";
                var battle = encounters.FirstOrDefault(encounter => !game.IsEncounterCleared(encounter.id));
                return battle != null ? "Reach the summit and defeat the windmill guardian" : "Find the summit exit";
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
            ShowMessage($"{nearest.title} - {nearest.subtitle}", 4f);
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
                if (open) ShowMessage(gate.id == 7 ? "The valley shortcut is now open!" : "The route to the next garden is open!", 4f);
            }
        }

        internal void PuzzleSolved(GardenPuzzle puzzle)
        {
            ShowMessage($"{puzzle.title} restored!", 4f);
            RefreshGates();
            if (Gates.Any(gate => gate.puzzleId == puzzle.id && !gate.IsOpen && gate.encounterId > 0))
                ShowMessage($"{puzzle.title} restored. Clear this encounter to open the route.", 4f);
        }

        public string InteractionHint
        {
            get
            {
                var mechanism = NearestMechanism();
                if (mechanism == null) return "";
                if (mechanism.kind == MechanismKind.Chest) return mechanism.Used ? "Supplies already collected" : "E / Y  -  Open supply chest (+35 party health)";
                if (mechanism.kind == MechanismKind.Exit) return CanExit ? "E / Y  -  Complete Windmill Valley" : "Restore all 7 routes and clear all 6 encounters to leave";
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
                    ShowMessage("Supplies recovered. Living party members regain 35 health.", 4f);
                    return true;
                case MechanismKind.Exit:
                    if (!CanExit) { ShowMessage("Restore the valley mechanisms and defeat its guardian first."); return false; }
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

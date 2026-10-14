using System.Collections.Generic;
using UnityEngine;

namespace ToyGarden
{
    public enum PuzzleKind { Lever, PushBlock, Dials, Sequence }

    public sealed class GardenPuzzle : MonoBehaviour
    {
        public int id;
        public string title;
        public PuzzleKind kind;
        public Vector3 gridOrigin;
        public float cellSize = 2f;
        public int gridWidth = 5, gridDepth = 5;
        public Vector2Int crateStart, crateGoal;
        public Vector2Int[] blockedCells = System.Array.Empty<Vector2Int>();
        public int[] solution = System.Array.Empty<int>();
        public bool Solved { get; private set; }
        public Vector2Int CrateCell { get; private set; }
        public IReadOnlyList<int> Values => values;
        public int SequenceProgress { get; private set; }

        GardenChapter chapter;
        List<GardenMechanism> mechanisms = new();
        int[] values = System.Array.Empty<int>();

        internal void Initialize(GardenChapter owner, List<GardenMechanism> allMechanisms)
        {
            chapter = owner;
            mechanisms.Clear();
            foreach (var mechanism in allMechanisms)
                if (mechanism.puzzleId == id && mechanism.kind != MechanismKind.Chest && mechanism.kind != MechanismKind.Exit)
                    mechanisms.Add(mechanism);
            ResetPuzzle();
        }

        public void ResetPuzzle()
        {
            Solved = false;
            SequenceProgress = 0;
            CrateCell = crateStart;
            values = new int[kind == PuzzleKind.Dials ? solution.Length : 0];
            foreach (var mechanism in mechanisms)
            {
                mechanism.ResetMechanism();
                if (mechanism.kind == MechanismKind.Crate) mechanism.transform.position = CellPosition(CrateCell);
            }
            Physics.SyncTransforms();
        }

        public Vector3 CellPosition(Vector2Int cell) => gridOrigin + new Vector3(cell.x * cellSize, 0f, cell.y * cellSize);

        public bool IsCellOpen(Vector2Int cell)
        {
            if (cell.x < 0 || cell.y < 0 || cell.x >= gridWidth || cell.y >= gridDepth) return false;
            foreach (var blocked in blockedCells) if (blocked == cell) return false;
            return true;
        }

        public bool TryActivate(GardenMechanism mechanism, Vector3 actorPosition)
        {
            if (mechanism == null || mechanism.puzzleId != id || Solved) return false;
            if (chapter != null && !chapter.CanUseMechanism(mechanism, actorPosition)) return false;
            if (Vector3.Distance(actorPosition, mechanism.transform.position) > mechanism.interactionRange) return false;
            bool accepted = false;
            switch (kind)
            {
                case PuzzleKind.Lever when mechanism.kind == MechanismKind.Lever:
                    mechanism.SetUsed(true);
                    Solved = accepted = true;
                    break;
                case PuzzleKind.PushBlock when mechanism.kind == MechanismKind.ResetCrate:
                    ResetPuzzle();
                    Tell("Block returned to its starting tile.");
                    return true;
                case PuzzleKind.PushBlock when mechanism.kind == MechanismKind.Crate:
                    accepted = PushCrate(mechanism, actorPosition);
                    break;
                case PuzzleKind.Dials when mechanism.kind == MechanismKind.Dial:
                    if (mechanism.index < 0 || mechanism.index >= values.Length) return false;
                    values[mechanism.index] = (values[mechanism.index] + 1) % 4;
                    mechanism.SetDialValue(values[mechanism.index]);
                    accepted = true;
                    Solved = values.Length > 0;
                    for (int i = 0; i < values.Length; i++) Solved &= values[i] == solution[i];
                    break;
                case PuzzleKind.Sequence when mechanism.kind == MechanismKind.SequenceSwitch:
                    if (solution.Length == 0) return false;
                    if (mechanism.index == solution[SequenceProgress])
                    {
                        SequenceProgress++;
                        mechanism.SetUsed(true);
                        Solved = SequenceProgress == solution.Length;
                        Tell(Solved ? "The chimes are in harmony." : $"Chime {SequenceProgress} / {solution.Length} correct.");
                    }
                    else
                    {
                        foreach (var item in mechanisms) item.SetUsed(false);
                        SequenceProgress = mechanism.index == solution[0] ? 1 : 0;
                        if (SequenceProgress == 1) mechanism.SetUsed(true);
                        Tell("Listen to the clue and begin the chimes again.");
                    }
                    accepted = true;
                    break;
            }
            if (accepted && Solved)
            {
                chapter?.PuzzleSolved(this);
            }
            return accepted;
        }

        bool PushCrate(GardenMechanism mechanism, Vector3 actorPosition)
        {
            Vector3 relative = mechanism.transform.position - actorPosition;
            relative.y = 0f;
            if (relative.sqrMagnitude < .25f)
            {
                Tell("Stand beside the block, facing the direction you want to push.");
                return false;
            }
            Vector2Int direction = Mathf.Abs(relative.x) > Mathf.Abs(relative.z)
                ? new Vector2Int(relative.x > 0f ? 1 : -1, 0)
                : new Vector2Int(0, relative.z > 0f ? 1 : -1);
            float crossOffset = direction.x != 0 ? Mathf.Abs(relative.z) : Mathf.Abs(relative.x);
            if (crossOffset > cellSize * .5f)
            {
                Tell("Stand straight behind the block to push one tile.");
                return false;
            }
            var destination = CrateCell + direction;
            if (!IsCellOpen(destination))
            {
                Tell("That tile is blocked. Use the reset crank if you need a fresh start.");
                return false;
            }
            Vector3 nextPosition = CellPosition(destination);
            var crateCollider = mechanism.GetComponentInChildren<BoxCollider>();
            if (crateCollider != null)
            {
                Bounds bounds = crateCollider.bounds;
                Vector3 center = nextPosition + bounds.center - mechanism.transform.position;
                Vector3 extents = Vector3.Max(bounds.extents - Vector3.one * .06f, Vector3.one * .02f);
                foreach (Collider obstruction in Physics.OverlapBox(center, extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (obstruction.transform.IsChildOf(mechanism.transform)
                        || obstruction.GetComponentInParent<GardenActor>() != null) continue;
                    Tell("There is no room for the block on that tile.");
                    return false;
                }
            }
            CrateCell = destination;
            mechanism.transform.position = nextPosition;
            Physics.SyncTransforms();
            Solved = CrateCell == crateGoal;
            if (!Solved) Tell("Block moved. Push it onto the glowing pressure tile.");
            return true;
        }

        internal string HintFor(GardenMechanism mechanism)
        {
            if (Solved) return $"{title} - restored";
            if (mechanism.kind == MechanismKind.ResetCrate) return "E / Y  -  Reset block to the starting tile";
            return kind switch
            {
                PuzzleKind.Lever => "E / Y  -  Pull lever and restore the route",
                PuzzleKind.PushBlock => "E / Y  -  Push one tile away from you; reach the glowing tile",
                PuzzleKind.Dials => $"E / Y  -  Turn dial {mechanism.index + 1}   [{string.Join(" / ", values)}]   Target: {string.Join(" / ", solution)}",
                PuzzleKind.Sequence => $"E / Y  -  Ring chime {mechanism.index + 1}   ({SequenceProgress}/{solution.Length})   Clue: {SequenceClue()}",
                _ => ""
            };
        }

        string SequenceClue()
        {
            var labels = new string[solution.Length];
            for (int i = 0; i < solution.Length; i++) labels[i] = (solution[i] + 1).ToString();
            return string.Join(" > ", labels);
        }

        void Tell(string text, float seconds = 2f) => chapter?.ShowMessage(text, seconds);
    }
}

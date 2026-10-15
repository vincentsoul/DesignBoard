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

        internal void RestoreSnapshot(GardenPuzzleSave state)
        {
            ResetPuzzle();
            Solved = state.solved;
            CrateCell = IsCellOpen(state.crate) ? state.crate : crateStart;
            if (Solved && kind == PuzzleKind.PushBlock) CrateCell = crateGoal;
            SequenceProgress = Mathf.Clamp(state.sequence, 0, Solved ? solution.Length : Mathf.Max(0, solution.Length - 1));
            if (Solved && kind == PuzzleKind.Sequence) SequenceProgress = solution.Length;
            for (int i = 0; i < values.Length; i++)
                values[i] = Solved ? solution[i] : state.values != null && i < state.values.Length ? Mathf.Clamp(state.values[i], 0, 3) : 0;
            foreach (var mechanism in mechanisms)
            {
                if (mechanism.kind == MechanismKind.Crate) mechanism.transform.position = CellPosition(CrateCell);
                else if (mechanism.kind == MechanismKind.Dial && mechanism.index >= 0 && mechanism.index < values.Length)
                    mechanism.SetDialValue(values[mechanism.index]);
                else if (mechanism.kind == MechanismKind.Lever) mechanism.SetUsed(Solved);
                else if (mechanism.kind == MechanismKind.SequenceSwitch)
                {
                    bool used = false;
                    for (int i = 0; i < SequenceProgress; i++) used |= solution[i] == mechanism.index;
                    mechanism.SetUsed(used);
                }
            }
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
                    Tell("木箱已返回起始格。");
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
                        Tell(Solved ? "风铃已经奏出和谐的旋律。" : $"顺序正确：{SequenceProgress}/{solution.Length}。");
                    }
                    else
                    {
                        foreach (var item in mechanisms) item.SetUsed(false);
                        SequenceProgress = mechanism.index == solution[0] ? 1 : 0;
                        if (SequenceProgress == 1) mechanism.SetUsed(true);
                        Tell("顺序有误，请根据线索重新敲响风铃。");
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
                Tell("站到木箱一侧，朝想推动的方向操作。");
                return false;
            }
            Vector2Int direction = Mathf.Abs(relative.x) > Mathf.Abs(relative.z)
                ? new Vector2Int(relative.x > 0f ? 1 : -1, 0)
                : new Vector2Int(0, relative.z > 0f ? 1 : -1);
            float crossOffset = direction.x != 0 ? Mathf.Abs(relative.z) : Mathf.Abs(relative.x);
            if (crossOffset > cellSize * .5f)
            {
                Tell("对正木箱一侧，即可向前推动一格。");
                return false;
            }
            var destination = CrateCell + direction;
            if (!IsCellOpen(destination))
            {
                Tell("前方格子无法通过。需要重来时，请使用重置摇柄。");
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
                    Tell("前方空间不足，无法推动木箱。");
                    return false;
                }
            }
            CrateCell = destination;
            mechanism.transform.position = nextPosition;
            Physics.SyncTransforms();
            Solved = CrateCell == crateGoal;
            if (!Solved) Tell("木箱已移动，请将它推到发光的压力板上。");
            return true;
        }

        internal string HintFor(GardenMechanism mechanism)
        {
            if (Solved) return $"{title} · 已完成";
            if (mechanism.kind == MechanismKind.ResetCrate) return "E / Y：将木箱重置到起始格";
            return kind switch
            {
                PuzzleKind.Lever => "E / Y：拉动拉杆，修复通路",
                PuzzleKind.PushBlock => "E / Y：向前推一格，将木箱推到发光格上",
                PuzzleKind.Dials => $"E / Y：旋转 {mechanism.index + 1} 号转盘　当前：{DialDirections(values)}　目标：{DialDirections(solution)}",
                PuzzleKind.Sequence => $"E / Y：敲响 {mechanism.index + 1} 号风铃（{SequenceProgress}/{solution.Length}）　顺序：{SequenceClue()}",
                _ => ""
            };
        }

        string SequenceClue()
        {
            var labels = new string[solution.Length];
            for (int i = 0; i < solution.Length; i++) labels[i] = (solution[i] + 1).ToString();
            return string.Join(" > ", labels);
        }

        static string DialDirections(IReadOnlyList<int> directions)
        {
            var labels = new string[directions.Count];
            for (int i = 0; i < directions.Count; i++)
                labels[i] = directions[i] switch { 0 => "北", 1 => "东", 2 => "南", 3 => "西", _ => directions[i].ToString() };
            return string.Join(" / ", labels);
        }

        void Tell(string text, float seconds = 2f) => chapter?.ShowMessage(text, seconds);
    }
}

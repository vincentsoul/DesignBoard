using UnityEngine;

namespace ToyGarden
{
    public enum MechanismKind { Lever, Crate, Dial, SequenceSwitch, ResetCrate, Chest, Exit }

    public sealed class GardenMechanism : MonoBehaviour
    {
        public MechanismKind kind;
        public int puzzleId = -1;
        public int index;
        public string label;
        public float interactionRange = 3f;
        public Transform visual;
        public bool Used { get; private set; }

        bool captured;
        Vector3 initialPosition;
        Quaternion initialRotation, initialVisualRotation;

        internal void CaptureInitialState()
        {
            if (captured) return;
            captured = true;
            initialPosition = transform.position;
            initialRotation = transform.rotation;
            initialVisualRotation = visual != null ? visual.localRotation : Quaternion.identity;
        }

        internal void ResetMechanism()
        {
            CaptureInitialState();
            Used = false;
            transform.SetPositionAndRotation(initialPosition, initialRotation);
            if (visual != null) visual.localRotation = initialVisualRotation;
        }

        internal void SetUsed(bool used)
        {
            CaptureInitialState();
            Used = used;
            if (visual == null) return;
            visual.localRotation = initialVisualRotation * (used
                ? Quaternion.Euler(kind == MechanismKind.Chest ? -75f : -40f, 0f, 0f)
                : Quaternion.identity);
        }

        internal void SetDialValue(int value)
        {
            CaptureInitialState();
            if (visual != null) visual.localRotation = initialVisualRotation * Quaternion.Euler(0f, value * 90f, 0f);
        }
    }
}

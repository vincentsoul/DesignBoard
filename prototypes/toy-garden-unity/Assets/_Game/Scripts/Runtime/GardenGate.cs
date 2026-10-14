using UnityEngine;

namespace ToyGarden
{
    public sealed class GardenGate : MonoBehaviour
    {
        public int id;
        public int puzzleId = -1;
        public int encounterId;
        public Transform barrier;
        public Vector3 before, after;
        public bool IsOpen { get; private set; }

        internal void SetOpen(bool open)
        {
            IsOpen = open;
            // The collision and carving obstacle share this root so both agree on route access.
            if (barrier != null) barrier.gameObject.SetActive(!open);
        }
    }
}

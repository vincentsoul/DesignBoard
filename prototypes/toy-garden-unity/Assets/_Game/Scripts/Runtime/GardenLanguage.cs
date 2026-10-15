using UnityEngine;

namespace ToyGarden
{
    /// <summary>One bundled Chinese font shared by the HUD, map and world signs.</summary>
    public static class GardenLanguage
    {
        static Font font;
        public static Font Font => font != null ? font : font = Resources.Load<Font>("Fonts/NotoSansCJKsc-Regular");
    }
}

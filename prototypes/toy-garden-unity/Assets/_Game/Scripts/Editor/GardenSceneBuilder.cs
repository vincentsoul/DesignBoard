using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ToyGarden.Editor
{
    /// <summary>Rebuilds the editable, original-art prototype from deterministic geometry.</summary>
    public static partial class GardenSceneBuilder
    {
        public const string ScenePath = "Assets/_Game/Scenes/ToyGarden.unity";
        const string AssetRoot = "Assets/_Game";
        const float ArenaFloor = 3.04f;
        const float ExitFloor = 3.08f;
        const float GrassDepth = 0.26f;
        static Transform environment;
        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        static System.Random random;

        [MenuItem("Toy Garden/Rebuild Prototype Scene")]
        public static void Build()
        {
            BuildChapter();
        }

        static void Palette()
        {
            Mat("Grass", "79C944"); Mat("GrassLight", "9CDD57"); Mat("GrassDark", "42A562");
            Mat("Soil", "C38754"); Mat("SoilLight", "DDA66A"); Mat("Stone", "E1CFA6");
            Mat("StoneLight", "F4E4BF"); Mat("Water", "56C5DF", 0.72f); Mat("Foam", "C2F5EF");
            Mat("Trunk", "966044"); Mat("Leaf", "48AD72"); Mat("LeafLight", "80CB63");
            Mat("Apple", "EF7653"); Mat("Gold", "FFD15B", 0.5f); Mat("Cream", "FFF0D0");
            Mat("Wood", "AD794B"); Mat("Pink", "F393BD"); Mat("Blue", "459ED5");
            Mat("Navy", "33496D"); Mat("Coral", "EE7666"); Mat("Lilac", "A798DC");
            Mat("Eye", "253856"); Mat("Metal", "CDDFE4", 0.65f); Mat("Enemy", "9E78B9");
            Mat("Boss", "D57E73"); Mat("DarkGrass", "548B42");
        }

        static void BuildGround()
        {
            Platform("Lower garden", new Vector3(-14, 0, -5), new Vector3(40, 1.8f, 42));
            Platform("Upper orchard", new Vector3(24, 3, -5), new Vector3(14, 4.8f, 24));
            // Four-centimetre turf terraces separate overlapping platform tops. These small,
            // deliberate steps are below both the controller step offset and NavMesh climb.
            // The earth remains below the grass, and its smaller radius removes coplanar sides.
            const float arenaBottom = -1.8f;
            float arenaEarthTop = ArenaFloor - GrassDepth + 0.02f;
            Cylinder("Arena earth", new Vector3(20, (arenaEarthTop + arenaBottom) * 0.5f, 19), 13.35f, arenaEarthTop - arenaBottom, "Soil", true);
            Cylinder("Arena grass", new Vector3(20, ArenaFloor - GrassDepth * 0.5f, 19), 13.5f, GrassDepth, "Grass", true);
            Platform("Exit terrace", new Vector3(5, ExitFloor, 24), new Vector3(20, ExitFloor + 1.8f, 10));

            Mesh slope = RampMesh();
            GameObject ramp = MeshObject("Wide garden ramp · 3m rise", slope, "Stone", environment);
            ramp.AddComponent<MeshCollider>().sharedMesh = slope;
            // Paving strips lie flush on the incline and never add physical steps.
            for (int i = 0; i < 9; i++)
            {
                float x = 3.7f + i * 1.55f;
                float y = (x - 3) * 3 / 14;
                GameObject slab = Box("Ramp paving", new Vector3(x, y + 0.035f, -5), new Vector3(1.25f, 0.045f, 4.5f), "StoneLight", false);
                slab.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(3, 14) * Mathf.Rad2Deg);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 7; i++)
                {
                    float x = 3 + i * 14f / 6;
                    FencePost(new Vector3(x, (x - 3) * 3 / 14, -5 + side * 3.15f));
                }
                Rail(new Vector3(3, 0.9f, -5 + side * 3.15f), new Vector3(17, 3.9f, -5 + side * 3.15f));
            }
            Cylinder("Arena sand ring", new Vector3(20, ArenaFloor + 0.012f, 19), 11.8f, 0.018f, "Stone", false);
            Cylinder("Arena grass inlay", new Vector3(20, ArenaFloor + 0.026f, 19), 10.9f, 0.018f, "GrassLight", false);
            // A large blue base gives the miniature an airy, island-like setting.
            Box("Distant blue backdrop", new Vector3(0, -4.6f, 0), new Vector3(420, 0.2f, 420), "Water", false, 0.1f);
        }

        static void Platform(string name, Vector3 surface, Vector3 size)
        {
            // Preserve the original bottom while burying the earth top 24cm below the walk
            // surface. The 26cm grass rim overlaps it by 2cm without sharing a visible plane.
            float earthInset = GrassDepth - 0.02f;
            Vector3 earthSize = new Vector3(size.x, size.y - earthInset, size.z);
            Box(name + " · earth", surface - Vector3.up * ((size.y + earthInset) * 0.5f), earthSize, "Soil", true, 0.8f);
            Box(name + " · grass rim", surface - Vector3.up * (GrassDepth * 0.5f), new Vector3(size.x, GrassDepth, size.z), "Grass", true, 0.12f);
            // Individual clay bands and pebbles keep the sides toy-like at oblique angles.
            Box(name + " · clay band", surface - Vector3.up * (size.y * 0.72f), new Vector3(size.x + 0.04f, 0.35f, size.z + 0.04f), "SoilLight", false, 0.17f);
        }

        static void BuildPaths()
        {
            Path(new Vector3(-19, 0, -20), new Vector3(-14, 0, -11), 7, 2.8f);
            Path(new Vector3(-5, 0, -5), new Vector3(3, 0, -5), 6, 3.4f);
            Path(new Vector3(-17, 0, 1), new Vector3(-24, 0, 9), 7, 2.3f);
            Path(new Vector3(18, 3, -5), new Vector3(23, 3, -5), 4, 3.5f);
            Path(new Vector3(22, 3, -2), new Vector3(20, 3, 8), 7, 3.6f);
            Path(new Vector3(10, ExitFloor, 24), new Vector3(-1, ExitFloor, 24), 8, 3.2f);
            Box("First battle clearing", new Vector3(-10, 0.01f, -5), new Vector3(18, 0.018f, 22), "GrassLight", false, 0.008f);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2 / 12;
                Box("Clearing border stone", new Vector3(-10 + Mathf.Cos(a) * 9.5f, 0.025f, -5 + Mathf.Sin(a) * 10.8f), new Vector3(0.8f, 0.05f, 0.55f), "Stone", false, 0.02f);
            }
            Sign(new Vector3(-21.5f, 0, -16), "ORCHARD", "Blue");
            Sign(new Vector3(1.1f, 0, -9.7f), "WINDMILL", "Coral");
            Sign(new Vector3(-21, 0, 6.8f), "SECRET", "Gold");
            Sign(new Vector3(24.3f, 3, 8.2f), "GROVE", "Lilac");
        }

        static void Path(Vector3 from, Vector3 to, int count, float width)
        {
            Vector3 direction = (to - from).normalized;
            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, count == 1 ? 0 : (float)i / (count - 1));
                GameObject paving = Box("Warm stepping stone", p + Vector3.up * 0.035f, new Vector3(width, 0.07f, 1.16f), i % 3 == 0 ? "StoneLight" : "Stone", false, 0.03f);
                paving.transform.rotation = Quaternion.Euler(0, yaw + (float)(random.NextDouble() - 0.5) * 7, 0);
            }
        }

        static void BuildBorders()
        {
            Fence(new Vector3(-33.2f, 0, -25.2f), new Vector3(5.2f, 0, -25.2f));
            Fence(new Vector3(-33.2f, 0, -25.2f), new Vector3(-33.2f, 0, 15.2f));
            Fence(new Vector3(-33.2f, 0, 15.2f), new Vector3(5.2f, 0, 15.2f));
            Fence(new Vector3(5.2f, 0, -25.2f), new Vector3(5.2f, 0, -8.2f));
            Fence(new Vector3(5.2f, 0, -1.8f), new Vector3(5.2f, 0, 15.2f));
            Fence(new Vector3(17.4f, 3, -16.2f), new Vector3(30.4f, 3, -16.2f));
            Fence(new Vector3(30.4f, 3, -16.2f), new Vector3(30.4f, 3, 6));
            Fence(new Vector3(17.4f, 3, -16.2f), new Vector3(17.4f, 3, -8.2f));
            Fence(new Vector3(17.4f, 3, -1.8f), new Vector3(17.4f, 3, 5.5f));
            Fence(new Vector3(-4.3f, 3, 19.7f), new Vector3(6.5f, 3, 19.7f));
            Fence(new Vector3(-4.3f, 3, 28.3f), new Vector3(11, 3, 28.3f));
            Fence(new Vector3(-4.3f, 3, 19.7f), new Vector3(-4.3f, 3, 28.3f));
            for (int i = 0; i < 40; i++)
            {
                float a0 = i * Mathf.PI * 2 / 40;
                float a1 = (i + 1) * Mathf.PI * 2 / 40;
                Vector3 p0 = new Vector3(20 + Mathf.Cos(a0) * 12.9f, 3, 19 + Mathf.Sin(a0) * 12.9f);
                Vector3 p1 = new Vector3(20 + Mathf.Cos(a1) * 12.9f, 3, 19 + Mathf.Sin(a1) * 12.9f);
                // Broad breaks connect the southern orchard and western exit terrace.
                Vector3 middle = (p0 + p1) * 0.5f;
                bool orchardOpening = middle.z < 8.5f && middle.x > 17.5f && middle.x < 26.5f;
                bool exitOpening = middle.x < 10.5f && middle.z > 20 && middle.z < 28.5f;
                if (orchardOpening || exitOpening) continue;
                FencePost(p0);
                Rail(p0 + Vector3.up * 0.85f, p1 + Vector3.up * 0.85f);
            }
        }

        static void Fence(Vector3 a, Vector3 b)
        {
            int count = Mathf.CeilToInt(Vector3.Distance(a, b) / 2.5f);
            for (int i = 0; i <= count; i++) FencePost(Vector3.Lerp(a, b, (float)i / count));
            Rail(a + Vector3.up * 0.82f, b + Vector3.up * 0.82f);
            Rail(a + Vector3.up * 0.38f, b + Vector3.up * 0.38f);
        }

        static void FencePost(Vector3 p)
        {
            Box("Cream fence post", p + Vector3.up * 0.65f, new Vector3(0.26f, 1.3f, 0.26f), "Cream", true, 0.08f);
            Sphere("Fence cap", p + Vector3.up * 1.31f, Vector3.one * 0.35f, "Gold");
        }

        static void Rail(Vector3 a, Vector3 b)
        {
            GameObject rail = Box("Cream safety rail", (a + b) * 0.5f, new Vector3(0.16f, 0.23f, Vector3.Distance(a, b)), "Cream", true, 0.06f);
            rail.transform.rotation = Quaternion.LookRotation(b - a);
        }

        static void BuildScenery()
        {
            Vector3[] trees = {
                new Vector3(-28,0,-20), new Vector3(-28,0,-15), new Vector3(-30,0,1),
                new Vector3(-28,0,12), new Vector3(-16,0,12), new Vector3(-6,0,12),
                new Vector3(1,0,10), new Vector3(1,0,-20), new Vector3(-9,0,-21),
                new Vector3(20,3,-12), new Vector3(27,3,-12), new Vector3(28.5f,3,-2),
                new Vector3(30,3,14), new Vector3(29,3,25), new Vector3(24,3,29),
                new Vector3(16,3,29), new Vector3(-1.5f,3,27)
            };
            for (int i = 0; i < trees.Length; i++) Tree(trees[i], 0.9f + (i % 3) * 0.13f, i % 2 == 0);
            Vector3[] bushes = {
                new Vector3(-31,0,-12),new Vector3(-31,0,-7),new Vector3(-29,0,6),
                new Vector3(-19,0,12.5f),new Vector3(-12,0,12.7f),new Vector3(-2,0,12),
                new Vector3(2,0,-14),new Vector3(-13,0,-22),new Vector3(-25,0,-22),
                new Vector3(19,3,-14.7f),new Vector3(24,3,-14.7f),new Vector3(29,3,-9),
                new Vector3(29,3,2),new Vector3(10,3,17),new Vector3(11,3,12),
                new Vector3(31,3,18),new Vector3(26,3,30),new Vector3(4,3,27)
            };
            for (int i = 0; i < bushes.Length; i++)
            {
                Bush(bushes[i], 1.1f + i % 3 * 0.2f);
                FlowerPatch(bushes[i] + new Vector3(-0.9f, 0, -1.1f), 7);
            }
            Cylinder("Pond water", new Vector3(-26, 0.045f, -7), 3.4f, 0.04f, "Water", false);
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2 / 16;
                Vector3 p = new Vector3(-26 + Mathf.Cos(a) * 3.5f, 0.22f, -7 + Mathf.Sin(a) * 3.5f);
                Sphere("Pond bank pebble", p, new Vector3(1.2f, 0.55f, 0.85f), i % 2 == 0 ? "Stone" : "StoneLight", true);
            }
            Cylinder("Lily pad", new Vector3(-27, 0.08f, -7), 0.55f, 0.025f, "GrassDark", false);
            Flower(new Vector3(-27, 0.09f, -7), "Pink", 1.25f);
            Box("Bench seat", new Vector3(-28, 0.65f, 2.8f), new Vector3(2.3f, 0.2f, 0.75f), "Wood", true);
            Box("Bench back", new Vector3(-28, 1.3f, 3.1f), new Vector3(2.3f, 0.6f, 0.14f), "Cream", true);
            for (int i = -1; i <= 1; i += 2) Box("Bench leg", new Vector3(-28 + i * 0.8f, 0.3f, 2.8f), new Vector3(0.2f, 0.6f, 0.55f), "Wood", true);
            for (int i = 0; i < 6; i++)
            {
                Vector3 p = new Vector3(-22 + i * 10, 18 + i % 3 * 3, 42 + i % 2 * 6);
                Sphere("Distant cloud", p, new Vector3(8, 2.5f, 3.5f), "Cream");
                Sphere("Cloud puff", p + new Vector3(-1, 1.1f, 0), new Vector3(4.2f, 3.1f, 3), "Cream");
            }
            Flag(new Vector3(-22.5f, 0, -20), "Blue");
            Flag(new Vector3(-15.5f, 0, -20), "Coral");
            Flag(new Vector3(16, 3, 9), "Gold");
            Flag(new Vector3(25, 3, 9), "Gold");
        }

        static void Tree(Vector3 p, float size, bool fruit)
        {
            Cylinder("Rounded fruit tree trunk", p + Vector3.up * 1.25f * size, 0.28f * size, 2.5f * size, "Trunk", true);
            Sphere("Tree crown", p + Vector3.up * 3.1f * size, new Vector3(3.9f, 3.6f, 3.6f) * size, "Leaf");
            Sphere("Sunlit crown", p + new Vector3(-0.7f, 3.8f, -0.3f) * size, new Vector3(2.6f, 2.3f, 2.7f) * size, "LeafLight");
            if (fruit)
                for (int i = 0; i < 5; i++)
                {
                    float a = i * Mathf.PI * 2 / 5;
                    Sphere("Orchard apple", p + new Vector3(Mathf.Cos(a) * 1.55f, 2.8f + i % 2 * 0.7f, Mathf.Sin(a) * 1.55f) * size, Vector3.one * 0.45f * size, "Apple");
                }
        }

        static void Bush(Vector3 p, float size)
        {
            Sphere("Rounded shrub", p + Vector3.up * 0.62f * size, new Vector3(1.7f, 1.3f, 1.4f) * size, "Leaf", true);
            Sphere("Shrub highlight", p + new Vector3(0.55f, 0.75f, -0.12f) * size, new Vector3(1.15f, 1.15f, 1.2f) * size, "LeafLight");
        }

        static void FlowerPatch(Vector3 p, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 q = p + new Vector3((float)random.NextDouble() * 2.3f, 0, (float)random.NextDouble() * 1.5f);
                Flower(q, i % 3 == 0 ? "Pink" : (i % 3 == 1 ? "Cream" : "Gold"), 0.7f + (float)random.NextDouble() * 0.35f);
            }
        }

        static void Flower(Vector3 p, string color, float size)
        {
            Cylinder("Flower stem", p + Vector3.up * 0.19f * size, 0.035f * size, 0.38f * size, "GrassDark", false);
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2 / 5;
                Sphere("Flower petal", p + new Vector3(Mathf.Cos(a) * 0.13f, 0.4f, Mathf.Sin(a) * 0.13f) * size, new Vector3(0.2f, 0.09f, 0.2f) * size, color);
            }
            Sphere("Flower heart", p + Vector3.up * 0.44f * size, Vector3.one * 0.13f * size, "Gold");
        }

        static void BuildWindmill(Vector3 p)
        {
            Cylinder("Windmill foundation", p + Vector3.up * 0.3f, 2.25f, 0.6f, "Stone", true);
            Cylinder("Cream windmill tower", p + Vector3.up * 3.8f, 1.55f, 7, "Cream", true);
            Sphere("Windmill coral roof", p + Vector3.up * 7.3f, new Vector3(3.6f, 2.3f, 3.6f), "Coral");
            Box("Windmill door", p + new Vector3(0, 1.2f, -1.55f), new Vector3(1, 2, 0.15f), "Blue", false, 0.07f);
            Box("Windmill window", p + new Vector3(0, 4.5f, -1.55f), new Vector3(0.8f, 0.9f, 0.15f), "Navy", false, 0.07f);
            Vector3 hub = p + new Vector3(0, 6.2f, -1.8f);
            for (int i = 0; i < 4; i++)
            {
                Quaternion spin = Quaternion.Euler(0, 0, 23 + 90 * i);
                Vector3 center = hub + spin * new Vector3(0, 2.1f, 0);
                GameObject blade = Box("Windmill sail", center, new Vector3(0.9f, 3.8f, 0.16f), "Cream", false, 0.07f);
                blade.transform.rotation = spin;
                GameObject strip = Box("Sail gold spine", center + Vector3.back * 0.12f, new Vector3(0.12f, 3.9f, 0.12f), "Gold", false, 0.04f);
                strip.transform.rotation = spin;
            }
            Sphere("Windmill hub", hub + Vector3.back * 0.15f, Vector3.one * 0.65f, "Gold");
        }

        static Transform BuildChest(Vector3 p)
        {
            Cylinder("Secret reward plinth", p + Vector3.up * 0.08f, 1.65f, 0.16f, "Stone", false);
            Box("Treasure chest body", p + Vector3.up * 0.52f, new Vector3(1.65f, 0.85f, 1.05f), "Wood", true);
            Transform hinge = new GameObject("Chest Lid Pivot").transform;
            hinge.SetParent(environment); hinge.position = p + new Vector3(0, 0.93f, 0.5f);
            Box("Golden chest lid", p + new Vector3(0, 1.05f, 0), new Vector3(1.7f, 0.3f, 1.12f), "Gold", false, 0.14f, hinge);
            Box("Chest clasp", p + new Vector3(0, 0.72f, -0.56f), new Vector3(0.28f, 0.38f, 0.12f), "Gold", false);
            for (int i = -1; i <= 1; i += 2) Box("Chest brass strap", p + new Vector3(i * 0.58f, 0.55f, -0.55f), new Vector3(0.12f, 0.7f, 0.07f), "Gold", false);
            return hinge;
        }

        static Transform BuildExit(Vector3 p)
        {
            for (int i = -1; i <= 1; i += 2)
            {
                Box("Exit gate post", p + new Vector3(0, 1.9f, i * 2.2f), new Vector3(0.7f, 3.8f, 0.7f), "Cream", true);
                Sphere("Exit post finial", p + new Vector3(0, 3.9f, i * 2.2f), Vector3.one * 0.85f, "Gold");
            }
            Box("Exit arch beam", p + Vector3.up * 3.5f, new Vector3(0.65f, 0.7f, 5), "Blue", true);
            Transform gate = new GameObject("Exit Gate · Opens After Guardian").transform;
            gate.SetParent(environment); gate.position = p;
            Box("Gate crossbar", p + Vector3.up * 1.2f, new Vector3(0.22f, 0.32f, 4.1f), "Gold", false, 0.08f, gate);
            for (int i = -2; i <= 2; i++) Box("Gate bar", p + new Vector3(0, 1.2f, i * 0.75f), new Vector3(0.2f, 2.2f, 0.2f), "Gold", false, 0.07f, gate);
            // Gate is a visual objective marker; a moving collider must not invalidate the baked NavMesh.
            return gate;
        }

        static void Flag(Vector3 p, string color)
        {
            Cylinder("Flagpole", p + Vector3.up * 1.65f, 0.065f, 3.3f, "Wood", true);
            Box("Colorful garden pennant", p + new Vector3(0.55f, 2.8f, 0), new Vector3(1.05f, 0.65f, 0.06f), color, false, 0.025f);
            Sphere("Flag finial", p + Vector3.up * 3.4f, Vector3.one * 0.22f, "Gold");
        }

        static void Sign(Vector3 p, string text, string color)
        {
            Box("Signpost", p + Vector3.up * 0.75f, new Vector3(0.18f, 1.5f, 0.18f), "Wood", true);
            Box("Wayfinding " + text, p + Vector3.up * 1.4f, new Vector3(2.1f, 0.65f, 0.16f), color, false, 0.08f);
            GameObject label = new GameObject(text + " label");
            label.transform.SetParent(environment);
            label.transform.position = p + new Vector3(0, 1.4f, -0.1f);
            label.transform.rotation = Quaternion.identity;
            TextMesh tm = label.AddComponent<TextMesh>();
            tm.text = text; tm.fontSize = 42; tm.characterSize = 0.072f; tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center; tm.color = Color.white;
        }

        static void CreateActor(ActorKind kind, Vector3 position, int encounterId, Transform parent)
        {
            GameObject actor = new GameObject(kind.ToString());
            actor.layer = 8;
            CharacterController controller = actor.AddComponent<CharacterController>();
            bool boss = kind == ActorKind.Boss;
            bool hero = kind == ActorKind.Blade || kind == ActorKind.Hammer || kind == ActorKind.Ranger;
            float scale = boss ? 1.75f : (hero ? 1 : 0.9f);
            controller.radius = 0.38f * scale; controller.height = 1.8f * scale;
            controller.center = Vector3.up * controller.height * 0.5f;
            controller.stepOffset = 0.35f; controller.slopeLimit = 48; controller.skinWidth = 0.045f;
            GardenActor component = actor.AddComponent<GardenActor>();
            component.kind = kind; component.encounterId = encounterId;
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(actor.transform, false);
            component.visualRoot = visual;
            string color = kind == ActorKind.Blade ? "Blue" : kind == ActorKind.Hammer ? "Coral" : kind == ActorKind.Ranger ? "Leaf" : boss ? "Boss" : "Enemy";
            GameObject body = Capsule("Toy tunic", new Vector3(0, 0.8f, 0), new Vector3(0.7f, 0.45f, 0.6f), color, visual);
            component.bodyRenderer = body.GetComponent<Renderer>();
            Sphere("Round face", new Vector3(0, 1.5f, 0), new Vector3(0.65f, 0.65f, 0.62f), hero ? "Cream" : color, false, visual);
            for (int i = -1; i <= 1; i += 2)
            {
                Sphere("Boot", new Vector3(i * 0.21f, 0.16f, 0.1f), new Vector3(0.31f, 0.32f, 0.45f), "Navy", false, visual);
                Sphere("Hand", new Vector3(i * 0.43f, 0.9f, 0.02f), Vector3.one * 0.25f, hero ? "Cream" : color, false, visual);
                Sphere("Bright eye", new Vector3(i * 0.13f, 1.55f, 0.28f), new Vector3(0.11f, 0.13f, 0.08f), "Eye", false, visual);
            }
            Transform weapon = new GameObject("Weapon").transform;
            weapon.SetParent(visual, false);
            weapon.localPosition = new Vector3(0.43f, 0.9f, 0.15f);
            component.weaponRoot = weapon;
            if (kind == ActorKind.Blade)
            {
                Box("Blue knight crest", new Vector3(0, 1.86f, 0), new Vector3(0.15f, 0.42f, 0.52f), "Gold", false, 0.07f, visual);
                Box("Silver blade", weapon.position + new Vector3(0, 0.43f, 0), new Vector3(0.18f, 0.93f, 0.12f), "Metal", false, 0.05f, weapon);
                Box("Gold sword guard", weapon.position, new Vector3(0.48f, 0.1f, 0.2f), "Gold", false, 0.04f, weapon);
            }
            else if (kind == ActorKind.Hammer || boss)
            {
                Sphere("Round helmet", new Vector3(0, 1.8f, -0.03f), new Vector3(0.8f, 0.44f, 0.73f), boss ? "Gold" : "Coral", false, visual);
                Box("Hammer handle", weapon.position + Vector3.up * 0.35f, new Vector3(0.13f, 0.85f, 0.13f), "Wood", false, 0.04f, weapon);
                Box("Great mallet", weapon.position + Vector3.up * 0.85f, new Vector3(0.85f, 0.48f, 0.46f), boss ? "Gold" : "Metal", false, 0.12f, weapon);
                if (boss)
                    for (int i = -1; i <= 1; i += 2) Box("Guardian shoulder", new Vector3(i * 0.46f, 1.2f, 0), new Vector3(0.5f, 0.4f, 0.7f), "Gold", false, 0.12f, visual);
            }
            else if (kind == ActorKind.Ranger)
            {
                Sphere("Ranger hood", new Vector3(0, 1.68f, -0.14f), new Vector3(0.79f, 0.68f, 0.62f), "Leaf", false, visual);
                Box("Ranger bow", weapon.position + Vector3.up * 0.2f, new Vector3(0.12f, 0.95f, 0.17f), "Gold", false, 0.05f, weapon);
                Box("Bow string", weapon.position + new Vector3(0, 0.2f, 0.18f), new Vector3(0.025f, 0.95f, 0.025f), "Cream", false, 0.01f, weapon);
            }
            else
            {
                for (int i = -1; i <= 1; i += 2) Sphere("Leaf sprite ear", new Vector3(i * 0.34f, 1.86f, 0), new Vector3(0.24f, 0.6f, 0.25f), kind == ActorKind.Spitter ? "Pink" : "Lilac", false, visual);
                Sphere("Sprite seed", weapon.position + Vector3.forward * 0.13f, Vector3.one * 0.35f, kind == ActorKind.Spitter ? "Pink" : "Gold", false, weapon);
            }
            visual.localScale = Vector3.one * scale;
            foreach (Transform child in actor.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 8;
            string prefabPath = AssetRoot + "/Prefabs/" + kind + ".prefab";
            // Encounter membership belongs to the scene instance. Rebuilding the reusable
            // Grunt prefab for a later district must not migrate earlier enemies to it.
            component.encounterId = 0;
            PrefabUtility.SaveAsPrefabAssetAndConnect(actor, prefabPath, InteractionMode.AutomatedAction);
            component.encounterId = encounterId;
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            actor.transform.SetParent(parent, false);
            actor.transform.position = position;
            PrefabUtility.RecordPrefabInstancePropertyModifications(actor.transform);
        }

        static void LightingAndCamera()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.72f, 0.84f, 1);
            RenderSettings.ambientEquatorColor = new Color(0.66f, 0.72f, 0.7f);
            RenderSettings.ambientGroundColor = new Color(0.38f, 0.43f, 0.48f);
            RenderSettings.fog = true; RenderSettings.fogColor = new Color(0.66f, 0.86f, 0.93f);
            RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 80; RenderSettings.fogEndDistance = 180;
            RenderSettings.skybox = null;
            GameObject sun = new GameObject("Warm afternoon sunlight");
            Light light = sun.AddComponent<Light>();
            light.type = LightType.Directional; light.color = new Color(1, 0.94f, 0.82f); light.intensity = 1.65f;
            light.shadows = LightShadows.Soft; light.shadowStrength = 0.62f; light.shadowBias = 0.025f; light.shadowNormalBias = 0.2f;
            sun.transform.rotation = Quaternion.Euler(48, -32, 0); RenderSettings.sun = light;
            QualitySettings.shadowDistance = 85; QualitySettings.shadows = ShadowQuality.All;
            GameObject cam = new GameObject("Main Camera"); cam.tag = "MainCamera";
            Camera camera = cam.AddComponent<Camera>(); cam.AddComponent<AudioListener>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.66f, 0.86f, 0.93f);
            camera.fieldOfView = 43; camera.nearClipPlane = 0.1f; camera.farClipPlane = 250;
            cam.transform.position = new Vector3(-19, 19, -36);
            cam.transform.LookAt(new Vector3(-19, 0.8f, -18));
        }

        static void ValidateNavigation()
        {
            Vector3[] route = {
                new Vector3(-19, 0, -18), new Vector3(-10, 0, -5),
                new Vector3(-24, 0, 7.6f), new Vector3(3, 0, -5),
                new Vector3(17.7f, 3, -5), new Vector3(22, 3, 4),
                new Vector3(20, 3, 19), new Vector3(1.4f, 3, 24)
            };
            if (!NavMesh.SamplePosition(route[0], out NavMeshHit start, 1.2f, NavMesh.AllAreas))
                throw new InvalidOperationException("The party starting area is not on the baked NavMesh.");
            NavMeshPath path = new NavMeshPath();
            for (int i = 1; i < route.Length; i++)
            {
                if (!NavMesh.SamplePosition(route[i], out NavMeshHit target, 1.2f, NavMesh.AllAreas) ||
                    !NavMesh.CalculatePath(start.position, target.position, NavMesh.AllAreas, path) ||
                    path.status != NavMeshPathStatus.PathComplete)
                    throw new InvalidOperationException("The garden route is disconnected at checkpoint " + i + " (" + route[i] + ").");
            }
            Debug.Log("Garden navigation verified: entrance, plaza, reward branch, ramp, orchard, guardian arena and exit are connected.");
        }

        static GameObject Box(string name, Vector3 position, Vector3 size, string material, bool collider, float radius = 0.08f, Transform parent = null)
        {
            GameObject go = MeshObject(name, RoundedBoxMesh(size, radius), material, parent == null ? environment : parent);
            go.transform.position = position;
            if (collider) go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        static GameObject Sphere(string name, Vector3 position, Vector3 size, string material, bool collider = false, Transform parent = null)
        {
            return Primitive(name, PrimitiveType.Sphere, position, size, material, collider, parent == null ? environment : parent);
        }

        static GameObject Capsule(string name, Vector3 position, Vector3 size, string material, Transform parent)
        {
            return Primitive(name, PrimitiveType.Capsule, position, size, material, false, parent);
        }

        static GameObject Cylinder(string name, Vector3 position, float radius, float height, string material, bool collider)
        {
            GameObject go = Primitive(name, PrimitiveType.Cylinder, position, new Vector3(radius * 2, height / 2, radius * 2), material, false, environment);
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            return go;
        }

        static GameObject Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale, string material, bool collider, Transform parent)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name; go.transform.SetParent(parent, false); go.transform.position = position; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = materials[material];
            if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static GameObject MeshObject(string name, Mesh mesh, string material, Transform parent)
        {
            GameObject go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = materials[material];
            return go;
        }

        static Mesh RoundedBoxMesh(Vector3 size, float rounding)
        {
            float r = Mathf.Clamp(rounding, 0.005f, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.49f);
            string key = "RoundBox_" + Number(size.x) + "_" + Number(size.y) + "_" + Number(size.z) + "_" + Number(r);
            if (meshes.TryGetValue(key, out Mesh cached)) return cached;
            string path = AssetRoot + "/Meshes/" + key + ".asset";
            cached = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (cached != null) { meshes.Add(key, cached); return cached; }
            Vector3 h = size * 0.5f;
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var triangles = new List<int>();
            for (int axis = 0; axis < 3; axis++)
            {
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                float[] us = FaceSamples(h[u], r), vs = FaceSamples(h[v], r);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    int start = vertices.Count;
                    for (int y = 0; y < vs.Length; y++)
                        for (int x = 0; x < us.Length; x++)
                        {
                            Vector3 p = Vector3.zero; p[axis] = h[axis] * sign; p[u] = us[x]; p[v] = vs[y];
                            Vector3 core = new Vector3(Mathf.Clamp(p.x, -h.x + r, h.x - r), Mathf.Clamp(p.y, -h.y + r, h.y - r), Mathf.Clamp(p.z, -h.z + r, h.z - r));
                            Vector3 normal = (p - core).normalized;
                            vertices.Add(core + normal * r); normals.Add(normal);
                        }
                    for (int y = 0; y < vs.Length - 1; y++)
                        for (int x = 0; x < us.Length - 1; x++)
                        {
                            int a = start + y * us.Length + x, b = a + 1, c = a + us.Length, d = c + 1;
                            if (sign > 0) { triangles.Add(a); triangles.Add(b); triangles.Add(c); triangles.Add(b); triangles.Add(d); triangles.Add(c); }
                            else { triangles.Add(a); triangles.Add(c); triangles.Add(b); triangles.Add(b); triangles.Add(c); triangles.Add(d); }
                        }
                }
            }
            Mesh mesh = new Mesh { name = key };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path); meshes.Add(key, mesh); return mesh;
        }

        static float[] FaceSamples(float half, float radius)
        {
            return new[] { -half, -half + radius * 0.29f, -half + radius, half - radius, half - radius * 0.29f, half };
        }

        static Mesh RampMesh()
        {
            string path = AssetRoot + "/Meshes/GardenRamp.asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            // World-space mesh: 14m run, 3m rise, 6m width; top meets both platforms exactly.
            Vector3[] p = {
                new Vector3(3,0,-8), new Vector3(3,0,-2), new Vector3(17,3,-8), new Vector3(17,3,-2),
                new Vector3(3,-1.3f,-8), new Vector3(3,-1.3f,-2), new Vector3(17,1.7f,-8), new Vector3(17,1.7f,-2)
            };
            int[] faces = {0,1,2,1,3,2, 4,6,5,5,6,7, 0,4,1,1,4,5, 2,3,6,3,7,6, 0,2,4,2,6,4, 1,5,3,3,5,7};
            var vertices = new Vector3[faces.Length]; var triangles = new int[faces.Length];
            for (int i = 0; i < faces.Length; i++) { vertices[i] = p[faces[i]]; triangles[i] = i; }
            Mesh mesh = new Mesh { name = "GardenRamp", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, path); return mesh;
        }

        static Material Mat(string name, string hex, float smoothness = 0.22f)
        {
            string path = AssetRoot + "/Materials/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("No supported material shader is available.");
            bool created = material == null;
            if (created) material = new Material(shader) { name = name };
            ColorUtility.TryParseHtmlString("#" + hex, out Color color);
            // URP _BaseColor is an ordinary [MainColor] property, not [Gamma] or [HDR].
            // SetColor therefore stores it verbatim; convert the sRGB palette explicitly.
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) color = color.linear;
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (created) AssetDatabase.CreateAsset(material, path); else EditorUtility.SetDirty(material);
            materials.Add(name, material); return material;
        }

        static Material EffectMaterial(string name, string shaderName)
        {
            string path = AssetRoot + "/Materials/" + name + ".mat";
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("Missing runtime effect shader: " + shaderName);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created) material = new Material(shader) { name = name };
            else material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.One);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
            if (material.HasProperty("_ColorMode")) material.SetFloat("_ColorMode", 0);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = (int)RenderQueue.Geometry;
            if (created) AssetDatabase.CreateAsset(material, path); else EditorUtility.SetDirty(material);
            return material;
        }

        static string Number(float number) => number.ToString("0.###", CultureInfo.InvariantCulture).Replace('.', '_');

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/'); string parent = path.Substring(0, slash);
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}

using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace ToyGarden.Editor
{
    /// <summary>The complete, editable Windmill Isles chapter: eight districts and a gated return loop.</summary>
    public static partial class GardenSceneBuilder
    {
        static Transform worldRoot;
        static Transform puzzleRoot;
        static readonly Vector3[] districtCenters = {
            new(0, 0, 0), new(0, 0, 60), new(64, 2, 60), new(64, 5, 124),
            new(0, 5, 124), new(-64, 5, 124), new(-64, 8, 60), new(-64, 8, 0)
        };
        static readonly Vector2[] districtSizes = {
            new(48,40), new(48,44), new(44,44), new(44,44),
            new(48,44), new(44,44), new(44,44), new(44,40)
        };
        static readonly string[] districtTitles = {
            "Sunbell Village", "Appleway Orchard", "Bluewater Works", "Clockwork Court",
            "Windmill Heights", "Ancient Grove", "Sunstone Ruins", "Guardian Summit"
        };
        static readonly string[] districtSubtitles = {
            "Wake the road with the village lever", "A crate, a plate and an orchard ambush",
            "Turn the water wheels to match the arrows", "Find a route around the workshop blocks",
            "Raise the wind bridges and open the return path", "Remember the three ancient chimes",
            "Read the sunstone compass", "Defeat the guardian and reach the chapter gate"
        };

        static void BuildChapter()
        {
            foreach (string directory in new[] { "Scenes", "Materials", "Meshes", "Prefabs" }) EnsureFolder(AssetRoot + "/" + directory);
            random = new System.Random(1847);
            materials.Clear(); meshes.Clear();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            worldRoot = new GameObject("WORLD · Windmill Isles Chapter").transform;
            environment = worldRoot;
            puzzleRoot = new GameObject("PUZZLES · Runtime Gates and Mechanisms").transform;
            Palette();
            Mat("Terracotta", "DF8862"); Mat("Slate", "607CA3"); Mat("Mint", "8DDEB9");
            Mat("Copper", "C99558", 0.42f); Mat("Lavender", "BBA5DC"); Mat("Marble", "E9E5D6");
            Mat("DeepWater", "389DB8", 0.6f); Mat("Sand", "F3CF87"); Mat("Rune", "6DDBE9", 0.6f);
            BuildChapterLand();
            BuildChapterBridges();
            BuildDistrictArt();
            BuildChapterSky();

            GardenGame game = new GameObject("GAME · Windmill Isles Adventure").AddComponent<GardenGame>();
            game.encounterOneCenter = new Vector3(3, 0, 56);
            game.encounterTwoCenter = new Vector3(65, 2, 54);
            game.encounterOneRadius = game.encounterTwoRadius = 10;
            game.exitPosition = new Vector3(-64, 8, -12);
            game.chestPosition = new Vector3(19, 0, 76);
            game.projectileMaterial = EffectMaterial("Projectile", "Universal Render Pipeline/Unlit");
            game.effectMaterial = EffectMaterial("Effect", "Universal Render Pipeline/Particles/Unlit");
            GardenChapter chapter = game.gameObject.AddComponent<GardenChapter>();
            chapter.regions = new ChapterRegion[districtCenters.Length];
            for (int i = 0; i < districtCenters.Length; i++)
                chapter.regions[i] = new ChapterRegion { title = districtTitles[i], subtitle = districtSubtitles[i], center = districtCenters[i], size = districtSizes[i] };
            chapter.encounters = new[] {
                Encounter(1, new Vector3(3,0,56), 10, 0), Encounter(2, new Vector3(65,2,54), 10, 1),
                Encounter(3, new Vector3(59,5,118), 10, 2), Encounter(4, new Vector3(-58,5,120), 10, 4),
                Encounter(5, new Vector3(-61,8,55), 10, 5), Encounter(6, new Vector3(-64,8,1), 13, 6)
            };

            // Bake the complete chapter before adding the movable gate and crate colliders.
            environment = worldRoot;
            NavMeshSurface surface = worldRoot.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.layerMask = 1 << 0;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.overrideVoxelSize = true; surface.voxelSize = .15f;
            surface.BuildNavMesh();
            if (surface.navMeshData == null) throw new InvalidOperationException("The chapter NavMesh failed to bake.");
            string navPath = AssetRoot + "/Scenes/ToyGardenNavMesh.asset";
            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(navPath) != null) AssetDatabase.DeleteAsset(navPath);
            AssetDatabase.CreateAsset(surface.navMeshData, navPath);
            ValidateChapterNavigation();

            BuildChapterPuzzles();
            BuildChapterGates();
            BuildChapterRewards(game);
            BuildChapterActors();
            environment = worldRoot;
            LightingAndCamera();
            Camera.main.transform.position = new Vector3(-12, 20, -29);
            Camera.main.transform.LookAt(new Vector3(-12, .8f, -12));
            Camera.main.farClipPlane = 350;
            RenderSettings.fogStartDistance = 120; RenderSettings.fogEndDistance = 260;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("Windmill Isles chapter saved: 8 districts, 8 gates, 7 puzzles, 6 encounters and 5 treasure branches.");
        }

        static ChapterEncounter Encounter(int id, Vector3 center, float radius, int requiredGate)
            => new ChapterEncounter { id = id, center = center, radius = radius, requiredGate = requiredGate };

        static void BuildChapterLand()
        {
            for (int i = 0; i < districtCenters.Length; i++)
            {
                environment = DistrictRoot(i, "Land and edges");
                Vector3 p = districtCenters[i]; Vector2 size = districtSizes[i];
                Platform(districtTitles[i], p, new Vector3(size.x, p.y + 5.5f, size.y));
                Box("Exposed lower rock stratum", p - Vector3.up * (p.y + 4.3f), new Vector3(size.x - 1.2f, 1.2f, size.y - 1.2f), "Slate", false, .5f);
                // The broad pasture shape is broken by terraces, cliff pebbles and garden edges.
                for (int side = -1; side <= 1; side += 2)
                    for (int k = 0; k < 9; k++)
                    {
                        float z = p.z - size.y * .4f + k * size.y * .1f;
                        Sphere("Cliff sandstone pebble", new Vector3(p.x + side * (size.x * .5f - .15f), p.y - 1.9f - k % 3 * .4f, z), new Vector3(1.2f, 1.3f, 2.1f), k % 2 == 0 ? "Stone" : "SoilLight");
                    }
                BorderDistrict(i);
            }
            environment = worldRoot;
            Box("Ocean beneath the chapter", new Vector3(0, -6.4f, 55), new Vector3(600, .25f, 500), "Water", false, .1f);
            for (int i = 0; i < 32; i++)
            {
                Vector3 p = new Vector3(-112 + i % 8 * 31, -6.18f, -33 + i / 8 * 62);
                Box("Ocean reflected ripple", p, new Vector3(8 + i % 3 * 3, .018f, .18f), "Foam", false, .008f);
            }
        }

        static Transform DistrictRoot(int index, string suffix)
        {
            Transform group = new GameObject($"{index + 1:00} · {districtTitles[index]} · {suffix}").transform;
            group.SetParent(worldRoot, false); return group;
        }

        static void BorderDistrict(int index)
        {
            Vector3 c = districtCenters[index]; Vector2 s = districtSizes[index];
            float left = c.x - s.x * .5f + .65f, right = c.x + s.x * .5f - .65f;
            float south = c.z - s.y * .5f + .65f, north = c.z + s.y * .5f - .65f;
            bool northOpen = index == 0 || index == 1 || index == 2 || index == 6 || index == 7;
            bool southOpen = index == 1 || index == 3 || index == 4 || index == 5 || index == 6;
            bool eastOpen = index == 1 || index == 4 || index == 5;
            bool westOpen = index == 2 || index == 3 || index == 4;
            EdgeFence(new(left,c.y,south), new(right,c.y,south), southOpen);
            EdgeFence(new(left,c.y,north), new(right,c.y,north), northOpen);
            EdgeFence(new(left,c.y,south), new(left,c.y,north), westOpen);
            EdgeFence(new(right,c.y,south), new(right,c.y,north), eastOpen);
        }

        static void EdgeFence(Vector3 a, Vector3 b, bool opening)
        {
            if (!opening) { Fence(a, b); return; }
            Vector3 middle = (a + b) * .5f, direction = (b - a).normalized;
            Fence(a, middle - direction * 4.25f); Fence(middle + direction * 4.25f, b);
        }

        static void BuildChapterBridges()
        {
            environment = new GameObject("09 · Connecting Bridges, Ramps and Return Loop").transform;
            environment.SetParent(worldRoot, false);
            Bridge("Village blossom bridge", new(0,0,18), new(0,0,40), 8, "Stone");
            Bridge("Orchard canal bridge", new(22,0,60), new(44,2,60), 8, "Stone");
            Bridge("Waterworks rising aqueduct", new(64,2,80), new(64,5,104), 8, "Marble");
            Bridge("Clockwork promenade", new(44,5,124), new(22,5,124), 8, "Wood");
            Bridge("Wind bridge", new(-22,5,124), new(-44,5,124), 8, "StoneLight");
            Bridge("Ancient ascent", new(-64,5,104), new(-64,8,80), 8, "Marble");
            Bridge("Guardian causeway", new(-64,8,40), new(-64,8,18), 8, "Stone");
            Bridge("Orchard return shortcut", new(0,5,104), new(0,0,80), 8, "Wood");
            environment = worldRoot;
        }

        static void Bridge(string title, Vector3 from, Vector3 to, float width, string material)
        {
            Vector3 flat = to - from; flat.y = 0; Vector3 forward = flat.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            var vertices = new[] { from-right*width*.5f, from+right*width*.5f, to-right*width*.5f, to+right*width*.5f,
                from-right*width*.5f-Vector3.up*.8f, from+right*width*.5f-Vector3.up*.8f,
                to-right*width*.5f-Vector3.up*.8f, to+right*width*.5f-Vector3.up*.8f };
            int[] indices = {0,2,1,1,2,3, 4,5,6,5,7,6, 0,1,4,1,5,4, 2,6,3,3,6,7, 0,4,2,2,4,6, 1,3,5,3,7,5};
            Vector3[] unique = new Vector3[indices.Length]; int[] triangles = new int[indices.Length];
            for (int i=0;i<indices.Length;i++) { unique[i]=vertices[indices[i]]; triangles[i]=i; }
            string path = AssetRoot + "/Meshes/ChapterBridge_" + title.Replace(' ', '_') + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear(); mesh.name = title; mesh.vertices = unique; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            GameObject bridge = MeshObject(title + " · walkable incline", mesh, material, environment);
            bridge.AddComponent<MeshCollider>().sharedMesh = mesh;
            int segments = Mathf.CeilToInt(flat.magnitude / 2.1f);
            float pitch = Mathf.Atan2(to.y-from.y,flat.magnitude)*Mathf.Rad2Deg;
            Quaternion slopeRotation = Quaternion.LookRotation(forward) * Quaternion.Euler(-pitch,0,0);
            for (int i=0;i<=segments;i++)
            {
                Vector3 p=Vector3.Lerp(from,to,(float)i/segments);
                GameObject slab=Box(title+" · warm paving",p+Vector3.up*.024f,new Vector3(width-.65f,.04f,.95f),i%3==0?"StoneLight":material,false,.015f);
                slab.transform.rotation=slopeRotation;
                if(i%2==0)
                {
                    FencePost(p+right*(width*.5f-.1f)); FencePost(p-right*(width*.5f-.1f));
                    float bottom=-5.7f, h=p.y-bottom;
                    Cylinder("Bridge support pier",p-Vector3.up*h*.5f, .7f,h,"Stone",false);
                    Box("Pier capital",p-Vector3.up*.85f,new Vector3(width-1,.6f,1.8f),"StoneLight",false,.2f);
                }
            }
            foreach(int side in new[]{-1,1})
            {
                Rail(from+right*side*(width*.5f-.1f)+Vector3.up*.82f,to+right*side*(width*.5f-.1f)+Vector3.up*.82f);
                Rail(from+right*side*(width*.5f-.1f)+Vector3.up*.38f,to+right*side*(width*.5f-.1f)+Vector3.up*.38f);
            }
        }

        static void BuildDistrictArt()
        {
            environment=DistrictRoot(0,"Village houses, fountain and flower beds"); BuildVillage();
            environment=DistrictRoot(1,"Orchard rows and farm courtyard"); BuildOrchard();
            environment=DistrictRoot(2,"Aqueduct, waterwheel and turquoise pools"); BuildWaterworks();
            environment=DistrictRoot(3,"Clock tower and workshop terraces"); BuildClockwork();
            environment=DistrictRoot(4,"Windmill flower terraces"); BuildWindmillHeights();
            environment=DistrictRoot(5,"Ancient tree and musical stones"); BuildAncientGrove();
            environment=DistrictRoot(6,"Ruined colonnades and compass garden"); BuildRuins();
            environment=DistrictRoot(7,"Guardian amphitheatre"); BuildSummit();
            environment=worldRoot;
        }

        static void BuildVillage()
        {
            Box("Village paving square",new(0,.02f,1),new(24,.035f,24),"Stone",false,.012f);
            Path(new(-12,0,-12),new(0,0,-9),8,3.8f); Path(new(0,0,5),new(0,0,19),9,4.4f);
            House(new(-16,0,5),"Coral",1.05f); House(new(15,0,-8),"Blue",1); House(new(16,0,9),"Lilac",.85f);
            Fountain(new(-1,0,0),2.5f);
            Arch(new(0,0,-17),8,"Blue");
            Sign(new(-6,0,14),"ORCHARD", "Blue"); Sign(new(9,0,13),"ROAD LEVER", "Gold");
            GardenBed(new(-17,0,-11),new(6,4),"Pink"); GardenBed(new(16,0,17),new(10,3),"Gold");
            GardenBed(new(-14,0,15),new(11,3),"Cream");
            MarketStall(new(7,0,-8),"Coral"); Bench(new(-7,0,2)); Bench(new(5,0,5));
            Tree(new(-20,0,-17),1.15f,true); Tree(new(20,0,-16),1.1f,false); Tree(new(-20,0,15),1.2f,true);
            Flag(new(-4.6f,0,-17),"Coral"); Flag(new(4.6f,0,-17),"Blue");
            for(int i=0;i<4;i++) Lantern(new(-8+i*5,0,11));
        }

        static void BuildOrchard()
        {
            Path(new(0,0,39),new(1,0,53),9,4); Path(new(8,0,60),new(23,0,60),9,4);
            Path(new(0,0,70),new(0,0,81),7,3.8f); Path(new(-2,0,63),new(-12,0,65),7,3);
            Clearing(new(3,0,56),10,"GrassLight");
            House(new(-17,0,46),"Terracotta",.85f);
            for(int row=0;row<3;row++)
                for(int col=0;col<3;col++)
                    Tree(new(11+col*4.7f,0,42+row*5.5f),.84f+col*.04f,true);
            for(int i=0;i<4;i++) Tree(new(-19+i*5,0,78),.9f,true);
            CropRows(new(10,0,69),4,4); GardenBed(new(9,0,78),new(9,3),"Gold");
            Barrel(new(-20,0,56)); Barrel(new(-18.8f,0,57)); Cart(new(-16,0,57));
            Sign(new(-19,0,61),"PUSH TO THE PLATE", "Gold"); Sign(new(18,0,63),"WATERWORKS", "Blue");
            Sign(new(4.5f,0,78),"RETURN BRIDGE", "Coral");
            FlowerPatch(new(-6,0,41),8); FlowerPatch(new(17,0,73),8);
        }

        static void BuildWaterworks()
        {
            Path(new(43,2,60),new(61,2,60),10,4); Path(new(64,2,63),new(64,2,81),10,4);
            Clearing(new(65,2,54),9,"Sand");
            Pool(new(77,2,68),4.8f); Pool(new(48,2,47),3.2f);
            Waterwheel(new(79,2,52),4.2f);
            for(int i=0;i<3;i++) Arch(new(51+i*10,2,42),6,"Marble");
            Box("Aqueduct upper water channel",new(63,7.9f,42),new(34,.4f,2.8f),"Stone",false,.12f);
            Box("Aqueduct flowing water",new(63,8.12f,42),new(33,.025f,1.9f),"Water",false,.01f);
            Box("Waterfall descending ribbon",new(78,4.8f,43.4f),new(2.7f,5.4f,.12f),"Water",false,.05f);
            for(int i=0;i<3;i++) Box("Waterfall foam stream",new(77.2f+i*.8f,4.8f,43.31f),new(.13f,5.4f,.04f),"Foam",false,.015f);
            Sign(new(56,2,76),"MATCH:  E   S   W", "Blue");
            Sign(new(69,2,78),"CLOCKWORK COURT", "Copper");
            Tree(new(47,2,77),1.1f,false); Tree(new(82,2,79),.9f,false);
            GardenBed(new(73,2,79),new(6,3),"Cream");
            for(int i=0;i<4;i++) Barrel(new(48+i*1.5f,2,53));
            Bench(new(71,2,72)); Lantern(new(58,2,78));
        }

        static void BuildClockwork()
        {
            Box("Clockwork checkerboard courtyard",new(61,5.02f,119),new(31,.035f,27),"Stone",false,.01f);
            for(int z=0;z<5;z++) for(int x=0;x<6;x++)
                if((x+z)%2==0) Box("Workshop stone tile",new(47+x*5,5.045f,106+z*5),new(4.6f,.018f,4.6f),"StoneLight",false,.008f);
            Path(new(64,5,104),new(60,5,116),8,4); Path(new(55,5,124),new(43,5,124),8,4);
            Clocktower(new(79,5,114));
            House(new(49,5,136),"Copper",.8f);
            Sign(new(72,5,142),"PUSH TO THE PLATE", "Gold");
            Sign(new(46,5,128),"WINDMILL HEIGHTS", "Coral");
            Gear(new(82,5.17f,134),2.8f,"Copper",false); Gear(new(82,5.25f,140),1.8f,"Gold",false);
            for(int i=0;i<3;i++) Barrel(new(79+i*1.4f,5,128));
            MarketStall(new(49,5,111),"Lilac");
            Tree(new(46,5,143),.9f,false); Tree(new(82,5,143),.9f,false);
            Lantern(new(57,5,139)); Lantern(new(57,5,106));
        }

        static void BuildWindmillHeights()
        {
            Path(new(23,5,124),new(-23,5,124),22,4.5f); Path(new(0,5,122),new(0,5,104),10,4);
            BuildWindmill(new(-12,5,136));
            Cylinder("Windmill terraced garden",new(-12,5.12f,136),5.1f,.22f,"StoneLight",false);
            GardenBed(new(12,5,137),new(14,6),"Pink"); GardenBed(new(-14,5,110),new(13,5),"Gold");
            GardenBed(new(12,5,110),new(12,4),"Cream");
            for(int i=0;i<4;i++) Tree(new(-21+i*5,5,143),.8f,false);
            House(new(16,5,129),"Blue",.7f);
            Sign(new(9,5,123),"WIND BRIDGE LEVER", "Gold"); Sign(new(-18,5,127),"ANCIENT GROVE", "Lilac");
            Sign(new(5,5,107),"ORCHARD SHORTCUT", "Coral");
            Bench(new(-5,5,131)); Bench(new(6,5,133));
            for(int i=0;i<5;i++) Flag(new(-20+i*10,5,118),i%2==0?"Coral":"Blue");
        }

        static void BuildAncientGrove()
        {
            Path(new(-43,5,124),new(-58,5,124),9,4); Path(new(-64,5,116),new(-64,5,104),8,4);
            Clearing(new(-58,5,120),10,"Mint");
            // Oversized interlocking crowns distinguish this woodland from the orchard.
            Tree(new(-78,5,142),2.5f,false); Tree(new(-48,5,142),1.7f,false);
            Tree(new(-82,5,110),1.4f,false); Tree(new(-46,5,107),1.2f,false);
            for(int i=0;i<5;i++)
            {
                float x=-80+i*7;
                Mushroom(new(x,5,139-i%2*2),.6f+i%3*.15f);
                Mushroom(new(x,5,108+i%2*2),.75f);
            }
            Arch(new(-72,5,137),10,"Wood");
            Sign(new(-72,5,139),"CHIME ORDER:  1 > 3 > 2", "Lilac");
            Sign(new(-69,5,108),"SUNSTONE RUINS", "Gold");
            Pool(new(-79,5,121),3);
            GardenBed(new(-49,5,133),new(8,4),"Pink");
            FlowerPatch(new(-80,5,134),12); FlowerPatch(new(-48,5,114),10);
            for(int i=0;i<5;i++) RuneStone(new(-82+i*7,5,144),i%3);
        }

        static void BuildRuins()
        {
            Box("Sunstone ruin pavement",new(-64,8.02f,60),new(34,.035f,32),"Sand",false,.012f);
            Path(new(-64,8,81),new(-64,8,71),7,4); Path(new(-64,8,53),new(-64,8,39),8,4);
            Clearing(new(-61,8,55),10,"StoneLight");
            for(int i=0;i<3;i++)
            {
                Column(new(-82,8,45+i*12),3.5f+i%2*2);
                Column(new(-46,8,45+i*12),4.8f-i%2*2);
            }
            Arch(new(-62,8,77),10,"Marble");
            Box("Broken ruin lintel",new(-81,8.5f,74),new(1.1f,1.1f,6),"Marble",true,.2f);
            Gear(new(-51,8.1f,72),4,"Gold",false);
            Sign(new(-74,8,74),"MATCH:  S   E", "Gold");
            Sign(new(-70,8,42),"GUARDIAN SUMMIT", "Coral");
            Tree(new(-83,8,79),.9f,false); Bush(new(-48,8,78),1.3f);
            GardenBed(new(-80,8,42),new(6,3),"Cream");
            for(int i=0;i<6;i++) RuneStone(new(-83+i*6.5f,8,80),i%3);
            FlowerPatch(new(-49,8,43),10);
        }

        static void BuildSummit()
        {
            Cylinder("Guardian grand amphitheatre outer ring",new(-64,8.035f,0),17,.04f,"Marble",false);
            Cylinder("Guardian golden arena ring",new(-64,8.06f,0),14.8f,.035f,"Gold",false);
            Cylinder("Guardian arena grass",new(-64,8.085f,0),13.8f,.035f,"GrassLight",false);
            Path(new(-64,8,19),new(-64,8,11),6,4.5f);
            for(int side=-1;side<=1;side+=2)
            {
                for(int i=0;i<4;i++) Column(new(-64+side*18,8,-12+i*8),3.6f);
                for(int tier=0;tier<3;tier++) Box("Amphitheatre curved seating block",new(-64+side*(15+tier*1.1f),8.2f+tier*.4f,0),new(1,.4f,21-tier*3),tier%2==0?"Stone":"StoneLight",false,.15f);
                Flag(new(-64+side*7,8,-15),"Coral");
                Tree(new(-64+side*19,8,16),1.2f,false);
            }
            Arch(new(-64,8,-15),9,"Blue");
            Sign(new(-71,8,-10),"CHAPTER GATE", "Gold");
            GardenBed(new(-77,8,-16),new(6,3),"Pink"); GardenBed(new(-51,8,-16),new(6,3),"Gold");
        }

        static void BuildChapterSky()
        {
            environment=worldRoot;
            for(int i=0;i<20;i++)
            {
                int edge=i/5, slot=i%5;
                Vector3 p=edge==0 ? new Vector3(-125,20+slot%3*2,-15+slot*42)
                    : edge==1 ? new Vector3(125,20+slot%3*2,-15+slot*42)
                    : edge==2 ? new Vector3(-100+slot*50,22,184)
                    : new Vector3(-100+slot*50,18,-60);
                Sphere("Distant cloud",p,new Vector3(12,3,5),"Cream");
                Sphere("Distant cloud puff",p+new Vector3(-2,1.3f,0),new Vector3(6,4,4),"Cream");
            }
            for(int i=0;i<9;i++)
            {
                Vector3 p=new(-132+i%3*132,-1,-40+i/3*107);
                Cylinder("Distant unvisited isle",p-Vector3.up*2,8+i%3*2,4,"Soil",false);
                Cylinder("Distant island grass",p+Vector3.up*.05f,8+i%3*2,.3f,"Grass",false);
                Tree(p,.9f,false);
            }
        }

        static void BuildChapterPuzzles()
        {
            environment=puzzleRoot;
            GardenPuzzle p0=Puzzle(0,"Village road lever",PuzzleKind.Lever);
            Lever(new(8,0,10),p0.id,"Open the orchard road");
            GardenPuzzle p1=PushPuzzle(1,"Orchard pressure plate",new(-18,0,64),new[]{new Vector2Int(2,1),new Vector2Int(2,2)});
            GardenPuzzle p2=Puzzle(2,"Waterworks compass wheels",PuzzleKind.Dials); p2.solution=new[]{1,2,3};
            for(int i=0;i<3;i++) Dial(new(52+i*4,2,69),2,i,"Water wheel "+(i+1));
            GardenPuzzle p3=PushPuzzle(3,"Clockwork pressure plate",new(67,5,131),new[]{new Vector2Int(1,2),new Vector2Int(2,2)});
            GardenPuzzle p4=Puzzle(4,"Windmill bridge lever",PuzzleKind.Lever);
            Lever(new(7,5,126),p4.id,"Raise wind bridge and shortcut");
            GardenPuzzle p5=Puzzle(5,"Ancient chime melody",PuzzleKind.Sequence); p5.solution=new[]{0,2,1};
            for(int i=0;i<3;i++) SequenceBell(new(-77+i*5,5,132),5,i);
            GardenPuzzle p6=Puzzle(6,"Sunstone compass",PuzzleKind.Dials); p6.solution=new[]{2,1};
            for(int i=0;i<2;i++) Dial(new(-77+i*7,8,68),6,i,"Sunstone dial "+(i+1));
            // Continuous colored routes make the connection between mechanism and gate readable.
            Conduit(new(8,0,10),new(0,0,17),"Gold"); Conduit(new(0,0,17),new(0,0,26),"Gold");
            Conduit(new(-14,0,74),new(18,0,74),"Gold"); Conduit(new(18,0,74),new(22,0,60),"Gold");
            Conduit(new(56,2,72),new(64,2,79),"Blue");
            Conduit(new(71,5,141),new(48,5,141),"Copper"); Conduit(new(48,5,141),new(44,5,124),"Copper");
            Conduit(new(7,5,126),new(-22,5,128),"Gold"); Conduit(new(7,5,126),new(2,5,104),"Gold");
            Conduit(new(-72,5,134),new(-66,5,104),"Lilac"); Conduit(new(-74,8,71),new(-69,8,40),"Rune");
            environment=worldRoot;
        }

        static GardenPuzzle Puzzle(int id,string title,PuzzleKind kind)
        {
            GameObject root=new GameObject($"Puzzle {id} · {title}"); root.transform.SetParent(puzzleRoot,false);
            GardenPuzzle puzzle=root.AddComponent<GardenPuzzle>(); puzzle.id=id; puzzle.title=title; puzzle.kind=kind; return puzzle;
        }

        static GardenPuzzle PushPuzzle(int id,string title,Vector3 origin,Vector2Int[] blocked)
        {
            GardenPuzzle p=Puzzle(id,title,PuzzleKind.PushBlock);
            p.gridOrigin=origin; p.cellSize=2; p.gridWidth=p.gridDepth=5;
            p.crateStart=new(1,1); p.crateGoal=new(3,3); p.blockedCells=blocked;
            for(int z=0;z<5;z++) for(int x=0;x<5;x++)
            {
                Vector3 tile=origin+new Vector3(x*2,.025f,z*2);
                Box("Push puzzle grid tile",tile,new Vector3(1.87f,.035f,1.87f),(x+z)%2==0?"StoneLight":"Stone",false,.015f);
            }
            // These static board obstacles belong to the moving puzzle root but are carved at runtime.
            foreach(Vector2Int cell in blocked)
            {
                GameObject block=Box("Fixed puzzle stop",origin+new Vector3(cell.x*2,.65f,cell.y*2),new Vector3(1.8f,1.3f,1.8f),"Slate",true,.2f);
                Carve(block,new Vector3(1.8f,1.3f,1.8f),Vector3.zero);
                Box("Stop gold cap",block.transform.position+Vector3.up*.69f,new Vector3(1.6f,.1f,1.6f),"Copper",false,.035f);
            }
            Vector3 goal=origin+new Vector3(6,.08f,6);
            Box("Goal pressure plate",goal,new Vector3(1.7f,.1f,1.7f),"Gold",false,.04f);
            Box("Plate centre rune",goal+Vector3.up*.065f,new Vector3(1.15f,.025f,1.15f),"Rune",false,.01f);
            GardenMechanism crate=Mechanism("Pushable crate",origin+new Vector3(2,0,2),MechanismKind.Crate,id,0,"Push crate");
            Box("Wooden crate body",crate.transform.position+Vector3.up*.79f,new Vector3(1.45f,1.48f,1.45f),"Wood",false,.14f,crate.transform);
            for(int sign=-1;sign<=1;sign+=2)
            {
                Box("Crate reinforcing band",crate.transform.position+new Vector3(sign*.48f,.74f,0),new Vector3(.14f,1.52f,1.5f),"Gold",false,.03f,crate.transform);
                Box("Crate reinforcing crossband",crate.transform.position+new Vector3(0,.74f,sign*.48f),new Vector3(1.5f,1.52f,.14f),"Gold",false,.03f,crate.transform);
            }
            BoxCollider collider=crate.gameObject.AddComponent<BoxCollider>(); collider.center=Vector3.up*.79f; collider.size=new(1.45f,1.48f,1.45f);
            Carve(crate.gameObject,collider.size,collider.center); crate.visual=crate.transform.GetChild(0);
            GardenMechanism reset=Mechanism("Reset crate lever",origin+new Vector3(-2.5f,0,-1),MechanismKind.ResetCrate,id,0,"Reset crate to start");
            LeverArt(reset,"Coral");
            Sign(origin+new Vector3(-2.5f,0,-3),"RESET CRATE", "Coral");
            Label(origin+new Vector3(4,.15f,-2.5f),"STAND BEHIND + E",.11f,"Eye");
            return p;
        }

        static void Lever(Vector3 position,int puzzleId,string label)
        {
            GardenMechanism mechanism=Mechanism(label,position,MechanismKind.Lever,puzzleId,0,label);
            LeverArt(mechanism,"Gold");
        }

        static void LeverArt(GardenMechanism mechanism,string color)
        {
            Vector3 p=mechanism.transform.position;
            Box("Mechanism pedestal",p+Vector3.up*.35f,new Vector3(1.3f,.7f,1.3f),"Stone",false,.18f,mechanism.transform);
            Transform handle=new GameObject("Lever handle pivot").transform; handle.SetParent(mechanism.transform,false); handle.localPosition=Vector3.up*.7f;
            Box("Lever stem",p+Vector3.up*1.2f,new Vector3(.18f,1,.18f),"Wood",false,.05f,handle);
            Sphere("Lever gold grip",p+Vector3.up*1.75f,Vector3.one*.5f,color,false,handle);
            mechanism.visual=handle;
        }

        static void Dial(Vector3 position,int puzzleId,int index,string label)
        {
            GardenMechanism mechanism=Mechanism(label,position,MechanismKind.Dial,puzzleId,index,label);
            Cylinder("Compass dial plinth",position+Vector3.up*.35f,1.3f,.7f,"Stone",false).transform.SetParent(mechanism.transform,true);
            Cylinder("Compass dial rim",position+Vector3.up*.73f,1.18f,.12f,"Gold",false).transform.SetParent(mechanism.transform,true);
            Transform arrow=new GameObject("Rotating compass arrow · N E S W").transform; arrow.SetParent(mechanism.transform,false); arrow.localPosition=Vector3.up*.84f;
            Box("Compass arrow shaft",position+new Vector3(0,.86f,.1f),new Vector3(.2f,.1f,1.25f),"Blue",false,.04f,arrow);
            foreach(int side in new[]{-1,1})
            {
                GameObject wing=Box("Compass arrow head",position+new Vector3(side*.2f,.87f,.55f),new Vector3(.16f,.12f,.6f),"Blue",false,.04f,arrow);
                wing.transform.rotation=Quaternion.Euler(0,side*45,0);
            }
            mechanism.visual=arrow;
            Label(position+new Vector3(0,1.15f,-1.6f),(index+1).ToString(),.16f,"Eye");
            Label(position+new Vector3(0,.14f,1.7f),"N",.13f,"Eye");
            Label(position+new Vector3(1.65f,.14f,0),"E",.13f,"Eye");
            Label(position+new Vector3(0,.14f,-1.7f),"S",.13f,"Eye");
            Label(position+new Vector3(-1.65f,.14f,0),"W",.13f,"Eye");
        }

        static void SequenceBell(Vector3 position,int puzzleId,int index)
        {
            GardenMechanism mechanism=Mechanism("Ancient chime "+(index+1),position,MechanismKind.SequenceSwitch,puzzleId,index,"Ring chime "+(index+1));
            string[] colors={"Blue","Gold","Pink"};
            Cylinder("Chime plinth",position+Vector3.up*.4f,1,.8f,"Stone",false).transform.SetParent(mechanism.transform,true);
            Transform bell=new GameObject("Chime visual").transform; bell.SetParent(mechanism.transform,false);
            Sphere("Ancient colored chime",position+Vector3.up*1.4f,new Vector3(1.25f,1.2f,1.25f),colors[index],false,bell);
            Box("Chime suspension",position+Vector3.up*2.1f,new Vector3(.18f,.55f,.18f),"Wood",false,.05f,bell);
            mechanism.visual=bell;
            Label(position+new Vector3(0,1,-1.1f),(index+1).ToString(),.2f,"Eye");
        }

        static GardenMechanism Mechanism(string title,Vector3 position,MechanismKind kind,int puzzleId,int index,string label)
        {
            GameObject go=new GameObject(title); go.transform.SetParent(puzzleRoot,false); go.transform.position=position;
            GardenMechanism mechanism=go.AddComponent<GardenMechanism>(); mechanism.kind=kind; mechanism.puzzleId=puzzleId; mechanism.index=index; mechanism.label=label; mechanism.interactionRange=2.7f;
            return mechanism;
        }

        static void BuildChapterGates()
        {
            environment=puzzleRoot;
            Gate(0,0,0,new(0,0,29),Vector3.forward,new(0,0,26),new(0,0,32),"Village Road");
            Gate(1,1,1,new(33,1,60),Vector3.right,new(30,.73f,60),new(36,1.27f,60),"Orchard Canal");
            Gate(2,2,2,new(64,3.5f,92),Vector3.forward,new(64,3.125f,89),new(64,3.875f,95),"Waterworks Ascent");
            Gate(3,3,3,new(33,5,124),Vector3.left,new(36,5,124),new(30,5,124),"Clockwork Walk");
            Gate(4,4,0,new(-33,5,124),Vector3.left,new(-30,5,124),new(-36,5,124),"Wind Bridge");
            Gate(5,5,4,new(-64,6.5f,92),Vector3.back,new(-64,6.125f,95),new(-64,6.875f,89),"Ancient Ascent");
            Gate(6,6,5,new(-64,8,29),Vector3.back,new(-64,8,32),new(-64,8,26),"Guardian Causeway");
            Gate(7,4,0,new(0,2.5f,92),Vector3.back,new(0,3.125f,95),new(0,1.875f,89),"Orchard Return");
            environment=worldRoot;
        }

        static void Gate(int id,int puzzleId,int encounterId,Vector3 position,Vector3 forward,Vector3 before,Vector3 after,string title)
        {
            GameObject root=new GameObject($"Gate {id} · {title}"); root.transform.SetParent(puzzleRoot,false); root.transform.position=position;
            GardenGate gate=root.AddComponent<GardenGate>(); gate.id=id; gate.puzzleId=puzzleId; gate.encounterId=encounterId; gate.before=before; gate.after=after;
            Transform barrier=new GameObject("Blocking gate · physical and navigation barrier").transform; barrier.SetParent(root.transform,false);
            barrier.rotation=Quaternion.LookRotation(forward);
            BoxCollider collider=barrier.gameObject.AddComponent<BoxCollider>(); collider.center=Vector3.up*1.65f; collider.size=new Vector3(8.3f,4,.75f);
            Carve(barrier.gameObject,collider.size,collider.center);
            Vector3 right=Vector3.Cross(Vector3.up,forward);
            for(int i=-4;i<=4;i++)
            {
                GameObject bar=Box("Gold gate bar",position+right*i*.9f+Vector3.up*1.7f,new Vector3(.18f,3.5f,.2f),"Gold",false,.065f,barrier);
                bar.transform.rotation=barrier.rotation;
            }
            GameObject beam=Box("Gate coral crossbeam",position+Vector3.up*2.45f,new Vector3(8.2f,.42f,.35f),"Coral",false,.1f,barrier); beam.transform.rotation=barrier.rotation;
            GameObject low=Box("Gate lower crossbeam",position+Vector3.up*.65f,new Vector3(8.2f,.28f,.35f),"Coral",false,.08f,barrier); low.transform.rotation=barrier.rotation;
            foreach(int side in new[]{-1,1})
            {
                Box("Gate masonry pillar",position+right*side*4.35f+Vector3.up*2,new Vector3(.8f,4.5f,.8f),"StoneLight",true,.2f,root.transform);
                Sphere("Gate lantern cap",position+right*side*4.35f+Vector3.up*4.4f,Vector3.one,"Gold",false,root.transform);
            }
            gate.barrier=barrier;
        }

        static void Carve(GameObject go,Vector3 size,Vector3 center)
        {
            NavMeshObstacle obstacle=go.AddComponent<NavMeshObstacle>(); obstacle.shape=NavMeshObstacleShape.Box; obstacle.size=size; obstacle.center=center;
            obstacle.carving=true; obstacle.carveOnlyStationary=false;
        }

        static void BuildChapterRewards(GardenGame game)
        {
            environment=puzzleRoot;
            Vector3[] positions={new(19,0,76),new(80,2,76),new(48,5,140),new(-79,5,113),new(-48,8,76)};
            for(int i=0;i<positions.Length;i++)
            {
                GardenMechanism chest=Mechanism("Chapter treasure "+(i+1),positions[i],MechanismKind.Chest,-1,i,"Open garden treasure");
                environment=chest.transform;
                Transform lid=BuildChest(positions[i]);
                chest.visual=lid;
                environment=puzzleRoot;
                if(i==0) game.chestLid=lid;
                Path(positions[i]+new Vector3(0,0,-5),positions[i]+new Vector3(0,0,-1.5f),4,1.8f);
                Flag(positions[i]+Vector3.right*2,"Gold");
            }
            GardenMechanism exit=Mechanism("Chapter exit",game.exitPosition,MechanismKind.Exit,-1,0,"Complete Windmill Isles");
            Cylinder("Chapter completion pad",game.exitPosition+Vector3.up*.04f,2.7f,.075f,"Gold",false);
            Cylinder("Chapter completion pad centre",game.exitPosition+Vector3.up*.085f,2.2f,.035f,"Blue",false);
            Label(game.exitPosition+new Vector3(0,2.2f,-1),"CHAPTER EXIT",.15f,"Cream");
            environment=worldRoot;
        }

        static void BuildChapterActors()
        {
            Transform party=new GameObject("PARTY · Three Switchable Heroes").transform;
            CreateActor(ActorKind.Blade,new(-12,.05f,-12),0,party);
            CreateActor(ActorKind.Hammer,new(-14,.05f,-14),0,party);
            CreateActor(ActorKind.Ranger,new(-10,.05f,-14),0,party);
            Transform enemies=new GameObject("ENEMIES · Six Chapter Encounters").transform;
            Vector3[] centres={new(3,0,56),new(65,2,54),new(59,5,118),new(-58,5,120),new(-61,8,55)};
            for(int i=0;i<centres.Length;i++)
            {
                Vector3 p=centres[i]; int id=i+1;
                CreateActor(ActorKind.Grunt,p+new Vector3(-3,.05f,-2),id,enemies);
                CreateActor(ActorKind.Grunt,p+new Vector3(3,.05f,-1),id,enemies);
                CreateActor(ActorKind.Grunt,p+new Vector3(-2,.05f,3),id,enemies);
                CreateActor(ActorKind.Spitter,p+new Vector3(3,.05f,4),id,enemies);
            }
            CreateActor(ActorKind.Boss,new(-64,8.05f,-1),6,enemies);
            CreateActor(ActorKind.Grunt,new(-70,8.05f,5),6,enemies);
            CreateActor(ActorKind.Spitter,new(-58,8.05f,5),6,enemies);
        }

        static void ValidateChapterNavigation()
        {
            Vector3 startPosition=new(-12,0,-12);
            if(!NavMesh.SamplePosition(startPosition,out NavMeshHit start,1.5f,NavMesh.AllAreas)) throw new InvalidOperationException("Chapter entrance has no NavMesh.");
            Vector3[] destinations={new(3,0,56),new(-17,0,63),new(65,2,54),new(56,2,67),new(59,5,118),new(69,5,130),new(7,5,124),new(-58,5,120),new(-72,5,130),new(-61,8,55),new(-74,8,66),new(-64,8,1),new(-64,8,-12)};
            foreach(Vector3 destination in destinations)
            {
                var path=new NavMeshPath();
                if(!NavMesh.SamplePosition(destination,out NavMeshHit target,1.5f,NavMesh.AllAreas)||!NavMesh.CalculatePath(start.position,target.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)
                    throw new InvalidOperationException("Chapter base navigation is disconnected at "+destination);
            }
            Debug.Log("Chapter base navigation verified across all 8 districts, 7 puzzle approaches and the final exit.");
        }

        static void Clearing(Vector3 p,float radius,string color)
        {
            Cylinder("Encounter clearing",p+Vector3.up*.018f,radius,.025f,color,false);
            for(int i=0;i<18;i++)
            {
                float a=i*Mathf.PI*2/18;
                Box("Clearing edge paver",p+new Vector3(Mathf.Cos(a)*radius,.055f,Mathf.Sin(a)*radius),new Vector3(.8f,.08f,.5f),"Stone",false,.03f);
            }
        }

        static void House(Vector3 p,string roofColor,float scale)
        {
            Transform original=environment;
            Transform group=new GameObject("Toy cottage · "+roofColor).transform; group.SetParent(environment,false); group.position=p;
            environment=group;
            Box("Cottage foundation",p+Vector3.up*.3f,new(6,.6f,5),"Stone",true,.2f);
            Box("Cream plaster walls",p+Vector3.up*2.3f,new(5.5f,4.3f,4.7f),"Cream",true,.35f);
            Box("Rounded colorful roof",p+Vector3.up*4.8f,new(6.6f,1.2f,5.8f),roofColor,false,.48f);
            Box("Roof ridge",p+Vector3.up*5.5f,new(5.3f,.65f,2.8f),roofColor,false,.28f);
            Box("Cottage door",p+new Vector3(0,1.35f,-2.4f),new(1.35f,2.5f,.16f),"Wood",false,.08f);
            Sphere("Door knob",p+new Vector3(.4f,1.3f,-2.55f),Vector3.one*.18f,"Gold");
            foreach(int side in new[]{-1,1})
            {
                Box("Blue window",p+new Vector3(side*1.8f,2.6f,-2.4f),new(1.1f,1.3f,.15f),"Blue",false,.08f);
                Box("Window sill",p+new Vector3(side*1.8f,1.9f,-2.65f),new(1.5f,.25f,.65f),"Wood",false,.08f);
                for(int i=0;i<3;i++) Flower(p+new Vector3(side*1.8f-.4f+i*.4f,2.03f,-2.7f),"Pink",.8f);
            }
            Box("Chimney",p+new Vector3(1.5f,5.9f,1.1f),new(.9f,2,.9f),"Stone",false,.18f);
            group.localScale=Vector3.one*scale;
            environment=original;
        }

        static void Fountain(Vector3 p,float radius)
        {
            Cylinder("Fountain stone basin",p+Vector3.up*.3f,radius,.6f,"Stone",true);
            Cylinder("Fountain blue pool",p+Vector3.up*.65f,radius-.3f,.1f,"Water",false);
            Cylinder("Fountain centre stem",p+Vector3.up*1.3f,.45f,1.5f,"StoneLight",true);
            Sphere("Fountain golden bud",p+Vector3.up*2.2f,Vector3.one*1.2f,"Gold");
            for(int i=0;i<6;i++)
            {
                float a=i*Mathf.PI/3;
                Sphere("Fountain water droplets",p+new Vector3(Mathf.Cos(a)*.85f,1.5f,Mathf.Sin(a)*.85f),new(.18f,.5f,.18f),"Foam");
            }
        }

        static void Arch(Vector3 p,float width,string color)
        {
            foreach(int side in new[]{-1,1})
            {
                Box("Arch foundation",p+new Vector3(side*width*.5f,.25f,0),new(1.5f,.5f,1.5f),"Stone",true,.15f);
                Box("Rounded arch column",p+new Vector3(side*width*.5f,2.7f,0),new(.9f,5.4f,.9f),color,true,.25f);
                Sphere("Arch capital",p+new Vector3(side*width*.5f,5.6f,0),Vector3.one*1.2f,"Gold");
            }
            Box("Rounded arch lintel",p+Vector3.up*5.35f,new(width+.9f,.9f,1.1f),color,false,.35f);
        }

        static void GardenBed(Vector3 p,Vector2 size,string flowerColor)
        {
            Box("Flower bed soil",p+Vector3.up*.035f,new Vector3(size.x,.055f,size.y),"SoilLight",false,.02f);
            foreach(int side in new[]{-1,1})
                Box("Flower bed stone edging",p+Vector3.forward*side*(size.y*.5f+.12f)+Vector3.up*.13f,new Vector3(size.x+.5f,.26f,.25f),"Stone",false,.08f);
            int columns=Mathf.FloorToInt(size.x/1.1f), rows=Mathf.Max(1,Mathf.FloorToInt(size.y/1.3f));
            for(int z=0;z<rows;z++) for(int x=0;x<columns;x++)
                Flower(p+new Vector3(-size.x*.45f+x*1.1f,0,-size.y*.3f+z*1.3f),flowerColor,.75f+(x+z)%3*.1f);
        }

        static void CropRows(Vector3 p,int columns,int rows)
        {
            for(int z=0;z<rows;z++)
            {
                Box("Orchard cultivated soil",p+new Vector3(0,.03f,z*1.5f),new Vector3(columns*1.5f,.04f,1.05f),"SoilLight",false,.015f);
                for(int x=0;x<columns;x++)
                {
                    Vector3 q=p+new Vector3(-columns*.6f+x*1.5f,.3f,z*1.5f);
                    Sphere("Round garden cabbage",q,new Vector3(.8f,.65f,.8f),(x+z)%2==0?"Leaf":"Mint");
                    Sphere("Cabbage leaf",q+Vector3.up*.2f,new Vector3(.5f,.45f,.55f),"LeafLight");
                }
            }
        }

        static void MarketStall(Vector3 p,string color)
        {
            Box("Market table",p+Vector3.up*.9f,new(4,.3f,2),"Wood",true,.1f);
            foreach(int side in new[]{-1,1}) Box("Stall post",p+new Vector3(side*1.8f,1.5f,.65f),new(.18f,3,.18f),"Wood",true,.05f);
            Box("Toy market canopy",p+new Vector3(0,3,0),new(4.5f,.35f,2.7f),color,false,.15f);
            for(int i=0;i<6;i++) Sphere("Market fruit",p+new Vector3(-1.5f+i*.6f,1.2f,-.3f),Vector3.one*.45f,i%2==0?"Apple":"Gold");
        }

        static void Bench(Vector3 p)
        {
            Box("Garden bench seat",p+Vector3.up*.6f,new(2.5f,.2f,.8f),"Wood",true,.08f);
            Box("Garden bench back",p+new Vector3(0,1.3f,.35f),new(2.5f,.65f,.16f),"Cream",true,.06f);
            foreach(int side in new[]{-1,1}) Box("Bench leg",p+new Vector3(side*.9f,.3f,0),new(.2f,.6f,.65f),"Wood",true,.05f);
        }

        static void Barrel(Vector3 p)
        {
            Cylinder("Garden barrel",p+Vector3.up*.65f,.55f,1.3f,"Wood",true);
            Cylinder("Barrel brass hoop",p+Vector3.up*1.05f,.59f,.1f,"Copper",false);
            Cylinder("Barrel lower hoop",p+Vector3.up*.25f,.59f,.1f,"Copper",false);
        }

        static void Cart(Vector3 p)
        {
            Box("Harvest cart",p+Vector3.up*.85f,new(2.8f,.75f,1.6f),"Wood",true,.14f);
            foreach(int side in new[]{-1,1})
            {
                GameObject wheel=Cylinder("Cart wheel",p+new Vector3(side*1.1f,.5f,side*.8f),.55f,.18f,"Copper",false);
                wheel.transform.rotation=Quaternion.Euler(90,0,0);
            }
            for(int i=0;i<6;i++) Sphere("Harvest apples",p+new Vector3(-.9f+i%3*.8f,1.3f,-.35f+i/3*.65f),Vector3.one*.6f,"Apple");
        }

        static void Lantern(Vector3 p)
        {
            Cylinder("Garden lamp post",p+Vector3.up*1.6f,.09f,3.2f,"Wood",true);
            Box("Garden lamp body",p+Vector3.up*3.25f,new(.65f,.75f,.65f),"Gold",false,.12f);
            Box("Garden lamp roof",p+Vector3.up*3.7f,new(.85f,.18f,.85f),"Blue",false,.07f);
        }

        static void Pool(Vector3 p,float radius)
        {
            Cylinder("Turquoise pool",p+Vector3.up*.045f,radius,.06f,"Water",false);
            for(int i=0;i<20;i++)
            {
                float a=i*Mathf.PI*2/20;
                Sphere("Pool rim stone",p+new Vector3(Mathf.Cos(a)*radius,.25f,Mathf.Sin(a)*radius),new Vector3(1,.55f,.85f),i%2==0?"Stone":"StoneLight",true);
            }
            Cylinder("Water lily",p+new Vector3(-radius*.25f,.09f,.5f),.7f,.035f,"GrassDark",false);
            Flower(p+new Vector3(-radius*.25f,.12f,.5f),"Pink",1.5f);
        }

        static void Waterwheel(Vector3 p,float radius)
        {
            Box("Waterwheel mill house",p+new Vector3(0,2.5f,2),new Vector3(5,5,4),"Cream",true,.3f);
            Box("Water mill roof",p+new Vector3(0,5.2f,2),new Vector3(6,.8f,5),"Blue",false,.3f);
            Vector3 hub=p+new Vector3(0,radius,-.5f);
            GameObject disc=Cylinder("Waterwheel wooden disc",hub,radius,.45f,"Wood",false); disc.transform.rotation=Quaternion.Euler(90,0,0);
            for(int i=0;i<12;i++)
            {
                Quaternion rotation=Quaternion.Euler(0,0,i*30);
                Vector3 q=hub+rotation*new Vector3(0,radius*.8f,0);
                GameObject paddle=Box("Waterwheel paddle",q,new Vector3(1.3f,.8f,1.4f),"Copper",false,.14f); paddle.transform.rotation=rotation;
                GameObject spoke=Box("Waterwheel spoke",hub+rotation*new Vector3(0,radius*.45f,-.32f),new Vector3(.18f,radius*.9f,.15f),"Gold",false,.05f); spoke.transform.rotation=rotation;
            }
            Sphere("Waterwheel hub",hub+Vector3.back*.35f,Vector3.one*.9f,"Gold");
        }

        static void Clocktower(Vector3 p)
        {
            Box("Clocktower footing",p+Vector3.up*.4f,new(5,.8f,5),"Stone",true,.25f);
            Box("Clock tower",p+Vector3.up*4.7f,new(4,8.6f,4),"Terracotta",true,.4f);
            Box("Clock tower cornice",p+Vector3.up*8.9f,new(5.1f,.65f,5.1f),"Cream",false,.25f);
            Sphere("Clock tower dome",p+Vector3.up*9.7f,new(4.8f,2.5f,4.8f),"Copper");
            GameObject dial=Cylinder("Clock ivory face",p+new Vector3(0,6.9f,-2.12f),1.45f,.15f,"Cream",false); dial.transform.rotation=Quaternion.Euler(90,0,0);
            Box("Clock hour hand",p+new Vector3(.25f,7.15f,-2.24f),new(.12f,.85f,.08f),"Eye",false,.03f).transform.rotation=Quaternion.Euler(0,0,-45);
            Box("Clock minute hand",p+new Vector3(0,7.45f,-2.25f),new(.1f,1.1f,.08f),"Eye",false,.03f);
            Box("Clock tower blue door",p+new Vector3(0,1.5f,-2.06f),new(1.4f,2.8f,.12f),"Blue",false,.08f);
        }

        static void Gear(Vector3 p,float radius,string color,bool collider)
        {
            Cylinder("Decorative clockwork gear",p,radius,.18f,color,collider);
            for(int i=0;i<12;i++)
            {
                float a=i*Mathf.PI/6;
                GameObject tooth=Box("Gear tooth",p+new Vector3(Mathf.Sin(a)*radius,0,Mathf.Cos(a)*radius),new Vector3(.7f,.24f,.7f),color,false,.1f);
                tooth.transform.rotation=Quaternion.Euler(0,i*30,0);
            }
            Cylinder("Gear inset",p+Vector3.up*.12f,radius*.6f,.05f,"StoneLight",false);
        }

        static void Mushroom(Vector3 p,float scale)
        {
            Cylinder("Grove mushroom stem",p+Vector3.up*.55f*scale,.3f*scale,1.1f*scale,"Cream",false);
            Sphere("Grove mushroom cap",p+Vector3.up*1.2f*scale,new Vector3(1.9f,.75f,1.9f)*scale,"Coral");
            for(int i=0;i<4;i++) Sphere("Mushroom ivory dot",p+new Vector3(Mathf.Sin(i*1.57f)*.5f,1.55f,Mathf.Cos(i*1.57f)*.5f)*scale,Vector3.one*.23f*scale,"Cream");
        }

        static void RuneStone(Vector3 p,int colorIndex)
        {
            string[] colors={"Rune","Gold","Pink"};
            Box("Ancient rounded rune stone",p+Vector3.up*.85f,new(1.25f,1.7f,.8f),"Slate",true,.32f);
            Box("Luminous rune inset",p+new Vector3(0,1,-.43f),new(.35f,.55f,.05f),colors[colorIndex],false,.02f);
        }

        static void Column(Vector3 p,float height)
        {
            Box("Ancient column base",p+Vector3.up*.2f,new(1.8f,.4f,1.8f),"Stone",true,.12f);
            Cylinder("Ancient marble column",p+Vector3.up*(height*.5f+.3f),.55f,height,"Marble",true);
            Box("Ancient column capital",p+Vector3.up*(height+.5f),new(1.6f,.5f,1.6f),"StoneLight",false,.15f);
        }

        static void Conduit(Vector3 a,Vector3 b,string color)
        {
            Vector3 flat=b-a; flat.y=0;
            GameObject line=Box("Mechanism to gate color conduit",(a+b)*.5f+Vector3.up*.09f,new Vector3(.14f,.07f,flat.magnitude),color,false,.025f);
            line.transform.rotation=Quaternion.LookRotation(b-a);
            for(int i=0;i<3;i++) Sphere("Conduit light bead",Vector3.Lerp(a,b,(i+1)/4f)+Vector3.up*.15f,Vector3.one*.24f,color);
        }

        static void Label(Vector3 position,string title,float scale,string color)
        {
            GameObject go=new GameObject(title+" label"); go.transform.SetParent(environment,false); go.transform.position=position;
            TextMesh text=go.AddComponent<TextMesh>(); text.text=title; text.fontSize=42; text.characterSize=scale; text.anchor=TextAnchor.MiddleCenter; text.alignment=TextAlignment.Center;
            text.color=materials[color].color;
        }
    }
}

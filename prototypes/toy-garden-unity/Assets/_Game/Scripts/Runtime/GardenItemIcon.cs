using System.Collections.Generic;
using UnityEngine;

namespace ToyGarden
{
    /// <summary>Original, readable equipment silhouettes generated at runtime without external art.</summary>
    public static class GardenItemIcon
    {
        const int Size = 96;
        static readonly Dictionary<int, Texture2D> cache = new();
        static Color[] pixels;
        static readonly Color metal = new(.88f, .9f, .97f);
        static readonly Color gold = new(.91f, .72f, .38f);
        static readonly Color shadow = new(.2f, .18f, .3f);

        public static Texture2D Get(GearSlot slot, ActorKind hero)
        {
            int key = (int)slot * 10 + (slot == GearSlot.Weapon ? (int)hero : 0);
            if (cache.TryGetValue(key, out Texture2D result) && result != null) return result;
            pixels = new Color[Size * Size];
            switch (slot)
            {
                case GearSlot.Weapon:
                    if (hero == ActorKind.Hammer)
                    {
                        Line(new(43,17), new(51,65), 5, gold);
                        Poly(metal, new(24,61),new(68,68),new(72,84),new(24,80));
                        Line(new(30,65),new(31,77),2,shadow); Line(new(63,71),new(64,80),2,shadow);
                    }
                    else if (hero == ActorKind.Ranger)
                    {
                        Arc(new(32,48),32,-78,78,3.3f,gold);
                        Line(new(39,17),new(39,79),1.2f,metal);
                        Line(new(17,48),new(78,48),2,metal);
                        Poly(gold,new(78,48),new(66,54),new(66,42));
                    }
                    else
                    {
                        Poly(metal,new(32,29),new(70,77),new(81,82),new(78,69),new(42,25));
                        Line(new(39,34),new(72,74),1.2f,Color.white);
                        Line(new(24,20),new(38,35),4,gold); Line(new(24,40),new(48,21),3,gold);
                        Circle(new(23,18),4,gold);
                    }
                    break;
                case GearSlot.Helmet:
                    Poly(metal,new(20,30),new(19,52),new(27,70),new(40,80),new(57,80),new(71,68),new(78,49),new(75,30),new(61,27),new(56,47),new(39,47),new(35,27));
                    Line(new(27,51),new(68,51),4,shadow); Line(new(48,79),new(48,55),3,gold);
                    break;
                case GearSlot.Chest:
                    Poly(metal,new(18,77),new(35,81),new(43,69),new(54,69),new(62,81),new(79,76),new(88,56),new(73,48),new(69,21),new(27,21),new(23,48),new(8,56));
                    Line(new(48,65),new(48,25),3,gold); Line(new(28,35),new(68,35),3,shadow);
                    Poly(gold,new(48,61),new(57,53),new(48,44),new(39,53));
                    break;
                case GearSlot.Legs:
                    Poly(metal,new(25,80),new(72,80),new(69,19),new(52,19),new(48,49),new(44,19),new(27,19));
                    Line(new(27,72),new(70,72),4,gold); Line(new(33,31),new(41,31),2,shadow); Line(new(55,31),new(65,31),2,shadow);
                    break;
                case GearSlot.Gloves:
                    Poly(metal,new(31,21),new(65,21),new(65,39),new(78,55),new(74,63),new(63,53),new(63,76),new(56,81),new(50,75),new(45,83),new(38,79),new(37,67),new(29,73),new(22,67),new(24,43),new(31,36));
                    Line(new(31,29),new(65,29),4,gold); Line(new(39,52),new(58,52),2,shadow);
                    break;
                case GearSlot.Boots:
                    Poly(metal,new(32,79),new(65,79),new(62,43),new(76,30),new(80,20),new(78,15),new(23,15),new(23,29),new(32,43));
                    Line(new(34,71),new(63,71),4,gold); Line(new(25,22),new(76,22),3,shadow);
                    Line(new(38,54),new(57,54),2,gold); Line(new(36,45),new(56,45),2,gold);
                    break;
                case GearSlot.Ring:
                    Circle(new(48,41),26,gold); Circle(new(48,41),16,Color.clear);
                    Poly(metal,new(48,83),new(63,68),new(48,55),new(33,68));
                    Poly(new(.5f,.86f,1),new(48,78),new(57,68),new(48,61),new(39,68));
                    break;
                case GearSlot.Amulet:
                    Line(new(22,80),new(29,55),2.5f,gold); Line(new(29,55),new(48,35),2.5f,gold);
                    Line(new(74,80),new(67,55),2.5f,gold); Line(new(67,55),new(48,35),2.5f,gold);
                    Poly(gold,new(48,48),new(64,30),new(48,12),new(32,30));
                    Poly(metal,new(48,41),new(57,30),new(48,19),new(39,30));
                    Circle(new(48,30),4,new(.64f,.5f,.94f));
                    break;
            }
            result = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "Garden equipment icon " + key, hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            result.SetPixels(pixels); result.Apply(false, true); cache[key] = result; pixels = null; return result;
        }

        static void Circle(Vector2 center,float radius,Color color)
        {
            for(int y=0;y<Size;y++) for(int x=0;x<Size;x++)
                if(Vector2.Distance(new(x+.5f,y+.5f),center)<=radius) pixels[y*Size+x]=color;
        }
        static void Line(Vector2 a,Vector2 b,float width,Color color)
        {
            Vector2 delta=b-a; float length=delta.sqrMagnitude;
            for(int y=0;y<Size;y++) for(int x=0;x<Size;x++)
            {
                Vector2 p=new(x+.5f,y+.5f); float t=Mathf.Clamp01(Vector2.Dot(p-a,delta)/Mathf.Max(.001f,length));
                if(Vector2.Distance(p,a+delta*t)<=width) pixels[y*Size+x]=color;
            }
        }
        static void Arc(Vector2 center,float radius,float from,float to,float width,Color color)
        {
            for(int i=0;i<24;i++)
            {
                float a=Mathf.Lerp(from,to,i/24f)*Mathf.Deg2Rad,b=Mathf.Lerp(from,to,(i+1)/24f)*Mathf.Deg2Rad;
                Line(center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,width,color);
            }
        }
        static void Poly(Color color,params Vector2[] points)
        {
            for(int y=0;y<Size;y++) for(int x=0;x<Size;x++)
            {
                bool inside=false; float px=x+.5f,py=y+.5f;
                for(int i=0,j=points.Length-1;i<points.Length;j=i++)
                    if((points[i].y>py)!=(points[j].y>py) && px<(points[j].x-points[i].x)*(py-points[i].y)/(points[j].y-points[i].y)+points[i].x) inside=!inside;
                if(inside) pixels[y*Size+x]=color;
            }
        }
    }
}

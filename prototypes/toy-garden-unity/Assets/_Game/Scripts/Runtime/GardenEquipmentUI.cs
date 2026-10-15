using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ToyGarden
{
    /// <summary>Full-screen equipment, character and crafting interface. All changes use the progression model.</summary>
    public sealed class GardenEquipmentUI : MonoBehaviour
    {
        const float Width=1440, Height=900;
        const int PageSize=16;
        static readonly Color Background=new(.075f,.065f,.105f), PanelColor=new(.115f,.105f,.16f), Raised=new(.16f,.145f,.205f);
        static readonly Color Gold=new(.91f,.75f,.43f), TextColor=new(.91f,.9f,.94f), Muted=new(.61f,.59f,.68f), Green=new(.48f,.86f,.65f), Red=new(.96f,.5f,.52f);
        static readonly GearSlot[] Slots=(GearSlot[])Enum.GetValues(typeof(GearSlot));
        static readonly ActorKind[] Heroes={ActorKind.Blade,ActorKind.Hammer,ActorKind.Ranger};
        static readonly GearStat[] Stats=(GearStat[])Enum.GetValues(typeof(GearStat));
        GardenGame game;
        GardenProgression Model => game.Progression;
        GardenCharacterPreview preview;
        ActorKind hero=ActorKind.Blade;
        int page,tab,qualityFilter=-1,slotFilter=-1;
        bool showEquipped,wasOpen,drawingConfirmation;
        string selectedId,confirmId,toast="";
        float toastUntil,uiScale;
        Vector2 detailsScroll,mouse;
        GearSort sort=GearSort.Quality;
        GearSlot craftSlot=GearSlot.Weapon;
        GearQuality craftQuality=GearQuality.Rare;
        GearSet craftSet=GearSet.None;
        GUIStyle title,heading,body,small,tiny,center,smallCenter;

        void Awake() => game=GetComponent<GardenGame>();
        void Update()
        {
            if(game==null) return;
            if(game.EquipmentOpen && !wasOpen)
            {
                hero=game.ActiveHero!=null?game.ActiveHero.kind:ActorKind.Blade;
                // Batchmode tests have no graphics device; progression and UI state still work.
                if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)
                    preview=new GardenCharacterPreview();
                RebuildPreview();
                if(selectedId==null) selectedId=Model?.inventory.FirstOrDefault(item=>!Model.IsEquipped(item.id))?.id;
            }
            if(!game.EquipmentOpen && wasOpen) { ReleasePreview(); confirmId=null; }
            wasOpen=game.EquipmentOpen;
        }
        void OnDisable() { ReleasePreview(); wasOpen=false; }
        void OnDestroy() => ReleasePreview();
        void ReleasePreview() { preview?.Dispose(); preview=null; }

        void Styles()
        {
            title=Style(30,FontStyle.Bold); heading=Style(21,FontStyle.Bold); body=Style(16); small=Style(13); tiny=Style(11);
            center=new GUIStyle(body) { alignment=TextAnchor.MiddleCenter,wordWrap=true };
            smallCenter=new GUIStyle(small) { alignment=TextAnchor.MiddleCenter,wordWrap=true };
        }
        static GUIStyle Style(int size,FontStyle weight=FontStyle.Normal)
        {
            var style=new GUIStyle(GUI.skin.label) { font=GardenLanguage.Font,fontSize=size,fontStyle=weight,wordWrap=true,padding=new RectOffset(0,0,0,0) };
            style.normal.textColor=TextColor; return style;
        }

        void OnGUI()
        {
            if(game==null || !game.EquipmentOpen || Model==null) return;
            if(title==null) Styles();
            int oldDepth=GUI.depth; GUI.depth=-100;
            Matrix4x4 matrix=GUI.matrix; Color color=GUI.color;
            Fill(new Rect(0,0,Screen.width,Screen.height),Background);
            uiScale=Mathf.Min(Screen.width/Width,Screen.height/Height);
            Vector2 offset=new((Screen.width-Width*uiScale)*.5f,(Screen.height-Height*uiScale)*.5f);
            mouse=(Event.current.mousePosition-offset)/uiScale;
            GUI.matrix=Matrix4x4.TRS(new Vector3(offset.x,offset.y,0),Quaternion.identity,Vector3.one*uiScale);
            bool previousEnabled=GUI.enabled;
            GUI.enabled=previousEnabled && confirmId==null;
            DrawHeader(); DrawNavigation(); DrawHeroPanel();
            if(tab==0) DrawInventory(); else if(tab==1) DrawCharacter(); else DrawForge();
            Fill(new Rect(24,839,1392,1),new Color(.32f,.29f,.38f));
            Text(new Rect(26,854,950,25),"换装与成长自动保存 · I / Esc 返回　·　点击装备查看详情　·　拖动角色可旋转",small,Muted);
            string saveStatus=game.SaveStatus ?? "";
            Text(new Rect(1120,854,292,25),saveStatus,small,saveStatus.Contains("失败") || saveStatus.Contains("暂停")?Red:Gold,TextAnchor.MiddleRight);
            if(Time.unscaledTime<toastUntil)
            {
                Fill(new Rect(400,794,640,40),new Color(.22f,.19f,.28f,.98f));
                Text(new Rect(414,801,612,28),toast,small,Gold,TextAnchor.MiddleCenter);
            }
            GUI.enabled=previousEnabled;
            if(confirmId!=null) DrawDismantleConfirmation();
            GUI.matrix=matrix; GUI.color=color; GUI.depth=oldDepth;
        }

        void DrawHeader()
        {
            Text(new Rect(30,25,400,42),"装备与成长",title,TextColor);
            Text(new Rect(32,71,500,25),"精炼装备，构筑属于小队的战斗风格",small,Muted);
            Currency(new Rect(801,29,170,45),"金币",Model.gold,Gold);
            Currency(new Rect(981,29,170,45),"矿石",Model.ore,new Color(.65f,.78f,.91f));
            Currency(new Rect(1161,29,170,45),"精粹",Model.essence,new Color(.75f,.57f,.95f));
            if(Button(new Rect(1351,29,63,45),"关闭",false)) game.CloseEquipment();
        }
        void Currency(Rect r,string name,int count,Color accent)
        {
            Fill(r,Raised); Fill(new Rect(r.x,r.y,3,r.height),accent);
            Text(new Rect(r.x+13,r.y+4,r.width-20,17),name,tiny,Muted);
            Text(new Rect(r.x+13,r.y+21,r.width-20,22),count.ToString("N0"),body,accent);
        }
        void DrawNavigation()
        {
            Fill(new Rect(24,112,146,706),PanelColor);
            string[] labels={"装 备","角 色","锻 造"};
            for(int i=0;i<3;i++)
            {
                Rect r=new(24,140+i*68,146,56);
                if(Button(r,labels[i],tab==i)) { tab=i; confirmId=null; }
                if(tab==i) Fill(new Rect(r.x,r.y,4,r.height),Gold);
            }
            Text(new Rect(40,378,114,24),"小队成员",small,Muted);
            for(int i=0;i<Heroes.Length;i++)
            {
                ActorKind kind=Heroes[i];
                if(Button(new Rect(35,416+i*69,124,57),GardenGame.RoleName(kind)+"\n等级 "+Model.GetHero(kind).level,hero==kind,smallCenter)) SelectHero(kind);
            }
            Text(new Rect(40,672,112,104),"战利品可强化、洗炼。\n\n套装效果在装备后生效。",small,Muted);
        }
        void SelectHero(ActorKind value)
        {
            if(hero==value) return;
            hero=value; detailsScroll=Vector2.zero; RebuildPreview();
        }
        void RebuildPreview()
        {
            if(preview==null || Model==null) return;
            GearItem chest=Model.GetEquipped(hero,GearSlot.Chest),weapon=Model.GetEquipped(hero,GearSlot.Weapon);
            Color tint=QualityColor((chest??weapon)?.quality??GearQuality.Common);
            preview.Show(game.Heroes.FirstOrDefault(actor=>actor.kind==hero),tint);
        }
        void DrawHeroPanel()
        {
            Fill(new Rect(190,112,500,706),PanelColor);
            HeroProgress progress=Model.GetHero(hero);
            Text(new Rect(211,131,300,35),GardenGame.RoleName(hero),heading,TextColor);
            Text(new Rect(529,135,138,27),"等级 "+progress.level,body,Gold,TextAnchor.MiddleRight);
            for(int i=0;i<Heroes.Length;i++)
                if(Button(new Rect(204+i*157,178,150,34),GardenGame.RoleName(Heroes[i]),hero==Heroes[i],smallCenter)) SelectHero(Heroes[i]);
            Rect portrait=new(278,226,324,405);
            if(preview!=null) GUI.DrawTexture(portrait,preview.Texture,ScaleMode.ScaleToFit,false);
            if(Event.current.type==EventType.MouseDrag && Event.current.button==0 && portrait.Contains(mouse) && confirmId==null)
            { preview?.Tick(-Event.current.delta.x/uiScale*.65f); Event.current.Use(); }
            for(int i=0;i<Slots.Length;i++)
            {
                bool left=i<4; int row=i%4;
                Rect rect=new(left?204:604,228+row*102,72,89);
                GearItem item=Model.GetEquipped(hero,Slots[i]);
                DrawSlot(rect,Slots[i],item);
            }
            Text(new Rect(282,637,316,25),"拖动旋转角色",small,Muted,TextAnchor.MiddleCenter);
            GearStats stats=Model.AggregateStats(hero);
            Fill(new Rect(210,684,460,1),new Color(.3f,.27f,.35f));
            MiniStat(new Rect(210,703,144,66),"攻击加成",Signed(stats.attack,false));
            MiniStat(new Rect(366,703,144,66),"防御加成",Signed(stats.defense,false));
            MiniStat(new Rect(522,703,144,66),"生命加成",Signed(stats.health,false));
            int pieces=Enum.GetValues(typeof(GearSet)).Cast<GearSet>().Where(set=>set!=GearSet.None).Sum(set=>Model.SetCount(hero,set));
            Text(new Rect(211,783,454,22),$"已装备 {progress.equipped.Count(entry=>!string.IsNullOrEmpty(entry.itemId))} / 8　·　套装部件 {pieces}",small,Muted,TextAnchor.MiddleCenter);
        }
        void DrawSlot(Rect r,GearSlot slot,GearItem item)
        {
            Color accent=item!=null?QualityColor(item.quality):new Color(.29f,.27f,.36f);
            Fill(r,item!=null?new Color(.18f,.16f,.23f):new Color(.095f,.085f,.135f));
            Border(r,item!=null && selectedId==item.id?Gold:accent,item!=null && selectedId==item.id?2:1);
            Color previous=GUI.color; GUI.color=item==null?new Color(.5f,.48f,.56f,.55f):Color.white;
            GUI.DrawTexture(new Rect(r.x+8,r.y+2,r.width-16,56),GardenItemIcon.Get(slot,item?.weaponClass??hero)); GUI.color=previous;
            Text(new Rect(r.x+2,r.y+60,r.width-4,22),GardenProgression.SlotName(slot),small,Muted,TextAnchor.MiddleCenter);
            if(item!=null && item.enhancement>0) Text(new Rect(r.x+39,r.y+2,31,18),"+"+item.enhancement,tiny,Gold,TextAnchor.MiddleRight);
            if(GUI.Button(r,GUIContent.none,GUIStyle.none))
            {
                tab=0; detailsScroll=Vector2.zero;
                if(item!=null) selectedId=item.id; else { slotFilter=(int)slot; page=0; selectedId=null; }
            }
        }
        void MiniStat(Rect r,string label,string value)
        {
            Text(new Rect(r.x,r.y,r.width,24),label,small,Muted,TextAnchor.MiddleCenter);
            Text(new Rect(r.x,r.y+26,r.width,33),value,heading,TextColor,TextAnchor.MiddleCenter);
        }

        List<GearItem> FilteredItems()
        {
            return Model.inventory.Where(item=>(showEquipped || !Model.IsEquipped(item.id))
                && (qualityFilter<0 || (int)item.quality==qualityFilter) && (slotFilter<0 || (int)item.slot==slotFilter)).ToList();
        }
        void DrawInventory()
        {
            Fill(new Rect(710,112,706,706),PanelColor);
            Text(new Rect(730,132,300,30),"背包装备",heading,TextColor);
            Text(new Rect(1098,136,294,24),$"容量 {Model.BackpackCount} / {GardenProgression.Capacity}",small,Muted,TextAnchor.MiddleRight);
            if(Button(new Rect(726,177,156,33),qualityFilter<0?"品质：全部":"品质："+GardenProgression.QualityName((GearQuality)qualityFilter),false,smallCenter))
            { qualityFilter++; if(qualityFilter>=5) qualityFilter=-1; page=0; }
            if(Button(new Rect(890,177,156,33),slotFilter<0?"部位：全部":"部位："+GardenProgression.SlotName((GearSlot)slotFilter),false,smallCenter))
            { slotFilter++; if(slotFilter>=8) slotFilter=-1; page=0; }
            if(Button(new Rect(1054,177,164,33),showEquipped?"已装备：显示":"已装备：隐藏",showEquipped,smallCenter)) { showEquipped=!showEquipped; page=0; }
            if(Button(new Rect(1226,177,170,33),"重置筛选",false,smallCenter)) { qualityFilter=slotFilter=-1; showEquipped=false; page=0; }
            List<GearItem> items=FilteredItems(); int pages=Mathf.Max(1,Mathf.CeilToInt(items.Count/(float)PageSize)); page=Mathf.Clamp(page,0,pages-1);
            for(int i=0;i<PageSize;i++)
            {
                Rect r=new(726+i%4*94,228+i/4*94,86,86); int index=page*PageSize+i;
                if(index<items.Count) DrawItemCell(r,items[index]);
                else { Fill(r,new Color(.09f,.08f,.125f)); Border(r,new Color(.2f,.18f,.25f)); }
            }
            if(items.Count==0) Text(new Rect(746,359,330,86),"没有符合条件的装备\n试试切换筛选，或前往锻造。",body,Muted,TextAnchor.MiddleCenter);
            if(Button(new Rect(726,618,72,32),"上一页",false,smallCenter,page>0)) page--;
            Text(new Rect(806,621,202,26),$"{page+1} / {pages}　共 {items.Count} 件",small,Muted,TextAnchor.MiddleCenter);
            if(Button(new Rect(1016,618,78,32),"下一页",false,smallCenter,page<pages-1)) page++;
            Fill(new Rect(1104,228,1,423),new Color(.28f,.25f,.33f));
            GearItem selected=Model.FindItem(selectedId);
            if(selected!=null) DrawItemDetails(new Rect(1118,228,282,423),selected);
            else Text(new Rect(1140,334,238,150),"选择一件装备\n\n查看属性、随机词条、\n套装效果与替换收益。",body,Muted,TextAnchor.MiddleCenter);
            DrawItemActions(selected);
        }
        void DrawItemCell(Rect r,GearItem item)
        {
            Color quality=QualityColor(item.quality);
            Fill(r,Color.Lerp(PanelColor,quality,.13f)); Border(r,selectedId==item.id?Gold:Color.Lerp(PanelColor,quality,.7f),selectedId==item.id?2:1);
            Fill(new Rect(r.x+1,r.y+r.height-3,r.width-2,2),quality);
            GUI.DrawTexture(new Rect(r.x+13,r.y+6,60,60),GardenItemIcon.Get(item.slot,item.weaponClass));
            Text(new Rect(r.x+6,r.y+63,r.width-12,19),"等级 "+item.itemLevel,tiny,Muted);
            if(item.enhancement>0) Text(new Rect(r.x+50,r.y+4,30,19),"+"+item.enhancement,tiny,Gold,TextAnchor.MiddleRight);
            if(item.locked) Text(new Rect(r.x+5,r.y+3,25,18),"锁",tiny,Gold);
            if(Model.IsEquipped(item.id)) Text(new Rect(r.x+49,r.y+63,31,18),"装备",tiny,Green,TextAnchor.MiddleRight);
            if(GUI.Button(r,GUIContent.none,GUIStyle.none)) { selectedId=item.id; detailsScroll=Vector2.zero; }
        }
        void DrawItemDetails(Rect area,GearItem item)
        {
            float contentHeight=300 + (item.slot==GearSlot.Weapon?28:0)
                + Stats.Count(stat=>Mathf.Abs(StatValue(item.baseStats,stat))>.00001f)*25
                + Mathf.Max(1,item.affixes.Count)*25 + (item.set!=GearSet.None?172:0) + Stats.Length*25;
            detailsScroll=GUI.BeginScrollView(area,detailsScroll,new Rect(0,0,258,Mathf.Max(area.height,contentHeight)));
            float y=0;
            Text(new Rect(0,y,250,57),item.name+(item.enhancement>0?"  +"+item.enhancement:""),heading,QualityColor(item.quality)); y+=63;
            Text(new Rect(0,y,250,26),GardenProgression.QualityName(item.quality)+" · "+GardenProgression.SlotName(item.slot)+" · 等级 "+item.itemLevel,small,Muted); y+=30;
            if(item.slot==GearSlot.Weapon) { Text(new Rect(0,y,250,24),"适用："+GardenGame.RoleName(item.weaponClass),small,Muted); y+=28; }
            HeroProgress owner=Model.EquippedBy(item.id);
            if(owner!=null) { Text(new Rect(0,y,250,25),"已由"+GardenGame.RoleName(owner.hero)+"装备",small,Green); y+=29; }
            y=DetailsHeading(y,"主属性（含强化）");
            foreach(GearStat stat in Stats)
            {
                float value=StatValue(item.baseStats,stat)*(1f+.08f*item.enhancement);
                if(Mathf.Abs(value)<.00001f) continue;
                StatRow(new Rect(0,y,248,24),GardenProgression.StatName(stat),Signed(value,IsPercent(stat)),TextColor); y+=25;
            }
            y=DetailsHeading(y,"随机词条");
            if(item.affixes.Count==0) { Text(new Rect(0,y,250,24),"此装备没有随机词条",small,Muted); y+=26; }
            foreach(GearAffix affix in item.affixes)
            { StatRow(new Rect(0,y,248,24),GardenProgression.StatName(affix.stat),Signed(affix.value,IsPercent(affix.stat)),new Color(.76f,.7f,.93f)); y+=25; }
            if(item.set!=GearSet.None)
            {
                int count=Model.SetCount(hero,item.set); y=DetailsHeading(y,GardenProgression.SetName(item.set)+$"  {count} / 4");
                foreach(int pieces in new[]{2,4})
                { Text(new Rect(0,y,248,62),$"{pieces} 件："+GardenProgression.SetBonusDescription(item.set,pieces),small,count>=pieces?Green:Muted); y+=64; }
            }
            y=DetailsHeading(y,"替换后的属性变化");
            if(item.slot==GearSlot.Weapon && item.weaponClass!=hero)
                Text(new Rect(0,y,248,48),"这件武器属于其他角色。",small,Muted);
            else
            {
                GearStats difference=Model.CompareStats(hero,item); bool any=false;
                foreach(GearStat stat in Stats)
                {
                    float value=StatValue(difference,stat); if(Mathf.Abs(value)<.00001f) continue; any=true;
                    StatRow(new Rect(0,y,248,24),GardenProgression.StatName(stat),Signed(value,IsPercent(stat)),value>0?Green:Red); y+=25;
                }
                if(!any) Text(new Rect(0,y,248,25),"与当前配置相同",small,Muted);
            }
            GUI.EndScrollView();
        }
        float DetailsHeading(float y,string text)
        {
            Fill(new Rect(0,y+5,248,1),new Color(.29f,.26f,.34f));
            Text(new Rect(0,y+15,250,26),text,small,Gold); return y+44;
        }
        void DrawItemActions(GearItem item)
        {
            Fill(new Rect(726,671,670,1),new Color(.29f,.26f,.34f));
            if(item==null)
            {
                Text(new Rect(731,691,440,46),"战利品、强化与套装共同影响角色的实战属性。",small,Muted);
                if(Button(new Rect(1226,748,170,45),"整理背包",false)) SortBackpack();
                return;
            }
            bool mine=Model.GetEquipped(hero,item.slot)?.id==item.id;
            GearCost enhance=GardenProgression.EnhanceCost(item),reroll=GardenProgression.RerollCost(item);
            if(Button(new Rect(726,691,159,54),mine?"卸下装备":"装备到当前角色",true)) Apply(mine?Model.Unequip(hero,item.slot):Model.Equip(hero,item.id));
            if(Button(new Rect(896,691,159,54),item.locked?"解除锁定":"锁定装备",false)) Apply(Model.ToggleLock(item.id));
            if(Button(new Rect(1066,691,159,54),"强化\n"+Cost(enhance),false,smallCenter)) Apply(Model.Enhance(item.id));
            if(Button(new Rect(1236,691,159,54),"洗炼词条\n"+Cost(reroll),false,smallCenter)) Apply(Model.Reroll(item.id));
            if(Button(new Rect(726,758,159,40),"分解装备",false,smallCenter)) confirmId=item.id;
            if(Button(new Rect(896,758,159,40),"排序："+SortName(sort),false,smallCenter)) sort=(GearSort)(((int)sort+1)%5);
            if(Button(new Rect(1066,758,159,40),"整理背包",false,smallCenter)) SortBackpack();
            Text(new Rect(1240,760,154,42),"分解前请确认锁定状态",tiny,Muted,TextAnchor.MiddleCenter);
        }

        void DrawCharacter()
        {
            Fill(new Rect(710,112,706,706),PanelColor);
            HeroProgress progress=Model.GetHero(hero); GardenActor actor=game.Heroes.FirstOrDefault(member=>member.kind==hero);
            Text(new Rect(735,137,500,35),GardenGame.RoleName(hero)+" · 角色成长",heading,TextColor);
            int required=GardenProgression.ExperienceRequired(progress.level);
            Text(new Rect(735,193,641,27),$"等级 {progress.level} / {GardenProgression.MaxLevel}　　经验 {progress.experience} / {required}",body,Muted);
            Fill(new Rect(735,235,650,8),Raised); Fill(new Rect(735,235,650*Mathf.Clamp01(progress.experience/(float)required),8),Gold);
            Text(new Rect(735,277,650,30),actor!=null?$"当前生命 {actor.Health:0} / {actor.MaxHealth:0}":"角色尚未加入场景",body,TextColor);
            Text(new Rect(735,335,650,30),"实战属性",heading,Gold);
            Text(new Rect(735,370,650,38),"包含等级、装备和套装效果；更换装备后即时更新。",small,Muted);
            string[] labels={"攻击力","防御力","生命上限","移动速度","暴击概率","暴击伤害","冷却缩减","生命汲取"};
            string[] values=actor!=null ? new[] {
                actor.CombatAttack.ToString("0.#"),actor.CombatDefense.ToString("0.#"),actor.MaxHealth.ToString("0.#"),actor.CombatMoveSpeed.ToString("0.00")+" 米/秒",
                (actor.CritChance*100).ToString("0.#")+"%",(actor.CritMultiplier*100).ToString("0.#")+"%",(actor.CooldownReduction*100).ToString("0.#")+"%",(actor.LifeSteal*100).ToString("0.#")+"%"
            } : new[]{"—","—","—","—","—","—","—","—"};
            for(int i=0;i<labels.Length;i++)
            {
                Rect r=new(735+i%2*335,424+i/2*48,315,36);
                Fill(r,Raised); StatRow(new Rect(r.x+12,r.y+7,r.width-24,25),labels[i],values[i],TextColor);
            }
            Text(new Rect(735,638,650,28),"当前套装",heading,Gold);
            int y=681;
            foreach(GearSet set in new[]{GearSet.Gale,GearSet.Bastion,GearSet.Ember})
            {
                int count=Model.SetCount(hero,set);
                Text(new Rect(735,y,187,28),GardenProgression.SetName(set)+$"　{count} 件",small,count>=2?Green:Muted);
                Text(new Rect(929,y,454,36),count>=4?"四件套效果已激活":count>=2?"两件套效果已激活":"集齐两件开始生效",small,count>=2?Green:Muted); y+=38;
            }
        }

        void DrawForge()
        {
            Fill(new Rect(710,112,706,706),PanelColor);
            Text(new Rect(735,136,640,34),"锻造工坊",heading,Gold);
            Text(new Rect(735,176,640,38),"选择部位、品质与套装；打造后获得随机词条。武器适用于当前角色。",small,Muted);
            Text(new Rect(735,228,640,25),"打造部位",body,TextColor);
            for(int i=0;i<Slots.Length;i++)
                if(Button(new Rect(735+i%4*164,267+i/4*45,154,36),GardenProgression.SlotName(Slots[i]),craftSlot==Slots[i],smallCenter)) craftSlot=Slots[i];
            Text(new Rect(735,370,640,25),"装备品质",body,TextColor);
            for(int i=0;i<5;i++)
                if(Button(new Rect(735+i*132,409,122,37),GardenProgression.QualityName((GearQuality)i),craftQuality==(GearQuality)i,smallCenter)) craftQuality=(GearQuality)i;
            Text(new Rect(735,474,640,25),"套装归属",body,TextColor);
            for(int i=0;i<4;i++)
                if(Button(new Rect(735+i*164,513,154,37),GardenProgression.SetName((GearSet)i),craftSet==(GearSet)i,smallCenter)) craftSet=(GearSet)i;
            Fill(new Rect(735,579,650,141),Raised);
            GUI.DrawTexture(new Rect(747,598,100,100),GardenItemIcon.Get(craftSlot,hero));
            Text(new Rect(865,598,495,30),GardenProgression.QualityName(craftQuality)+" · "+GardenProgression.SlotName(craftSlot),heading,QualityColor(craftQuality));
            Text(new Rect(865,636,495,26),GardenProgression.SetName(craftSet)+"　·　"+GardenGame.RoleName(hero)+"可用",small,Muted);
            GearCost cost=GardenProgression.CraftCost(craftSlot,craftQuality,craftSet);
            Text(new Rect(865,674,495,28),"材料需求："+Cost(cost),body,Gold);
            if(Button(new Rect(735,748,256,47),"打造装备",true))
            {
                GearResult result=Model.Craft(craftSlot,craftQuality,craftSet,hero);
                Apply(result);
                if(result.success && result.item!=null)
                {
                    selectedId=result.item.id; tab=0; qualityFilter=slotFilter=-1; showEquipped=false;
                    page=Mathf.Max(0,FilteredItems().FindIndex(item=>item.id==selectedId)/PageSize); detailsScroll=Vector2.zero;
                }
            }
            Text(new Rect(1008,748,379,49),"打造与洗炼均会消耗材料。分解未锁定装备可回收材料。",small,Muted);
        }

        void DrawDismantleConfirmation()
        {
            GearItem item=Model.FindItem(confirmId);
            if(item==null) { confirmId=null; return; }
            drawingConfirmation=true;
            Fill(new Rect(0,0,Width,Height),new Color(.02f,.015f,.035f,.86f));
            Rect dialog=new(435,285,570,310); Fill(dialog,Raised); Border(dialog,Gold);
            Text(new Rect(466,313,508,38),"确认分解装备？",heading,TextColor);
            Text(new Rect(466,364,508,55),item.name,body,QualityColor(item.quality));
            Text(new Rect(466,426,508,56),"回收："+Cost(GardenProgression.DismantleYield(item))+"\n分解后无法恢复；已装备或锁定的物品不能分解。",small,Muted);
            if(Button(new Rect(466,519,238,45),"取消",false)) confirmId=null;
            if(Button(new Rect(721,519,253,45),"确认分解",true))
            {
                string id=confirmId; confirmId=null; GearResult result=Model.Dismantle(id); Apply(result);
                if(result.success && selectedId==id) selectedId=null;
            }
            // Consume empty-space clicks so the dialog cannot activate equipment controls behind it.
            if(Event.current.type==EventType.MouseDown || Event.current.type==EventType.MouseUp) Event.current.Use();
            drawingConfirmation=false;
        }
        void SortBackpack()
        {
            Model.SortInventory(sort); page=0; Apply(GearResult.Ok("已按"+SortName(sort)+"整理背包。"));
        }
        void Apply(GearResult result)
        {
            toast=result.message; toastUntil=Time.unscaledTime+4;
            if(!result.success) return;
            game.RefreshHeroStats();
            if(!game.SaveProgress()) toast="修改已生效，但保存失败；请检查存档目录权限。";
            RebuildPreview();
        }

        static string Cost(GearCost value)
        {
            var parts=new List<string>();
            if(value.gold>0) parts.Add(value.gold+"金"); if(value.ore>0) parts.Add(value.ore+"矿"); if(value.essence>0) parts.Add(value.essence+"粹");
            return parts.Count>0?string.Join(" / ",parts):"无需材料";
        }
        static string SortName(GearSort value) => value switch { GearSort.Quality=>"品质",GearSort.Slot=>"部位",GearSort.Level=>"等级",GearSort.Set=>"套装",_=>"获得时间" };
        static bool IsPercent(GearStat stat) => stat!=GearStat.Attack && stat!=GearStat.Defense && stat!=GearStat.Health;
        static string Signed(float value,bool percent) => (value>=0?"+":"")+(percent?(value*100).ToString("0.#")+"%":value.ToString("0.#"));
        static float StatValue(GearStats stats,GearStat stat) => stat switch {
            GearStat.Attack=>stats.attack,GearStat.Defense=>stats.defense,GearStat.Health=>stats.health,GearStat.CritChance=>stats.critChance,
            GearStat.CritDamage=>stats.critDamage,GearStat.CooldownReduction=>stats.cooldownReduction,GearStat.LifeSteal=>stats.lifeSteal,GearStat.MoveSpeed=>stats.moveSpeed,_=>0
        };
        public static Color QualityColor(GearQuality quality) => quality switch {
            GearQuality.Uncommon=>new Color(.46f,.77f,.59f),GearQuality.Rare=>new Color(.42f,.67f,.96f),GearQuality.Epic=>new Color(.72f,.48f,.95f),GearQuality.Legendary=>new Color(.96f,.67f,.31f),_=>new Color(.68f,.67f,.73f)
        };
        void StatRow(Rect r,string label,string value,Color accent)
        {
            Text(new Rect(r.x,r.y,r.width*.66f,r.height),label,small,Muted);
            Text(new Rect(r.x+r.width*.63f,r.y,r.width*.37f,r.height),value,small,accent,TextAnchor.MiddleRight);
        }
        bool Button(Rect rect,string text,bool selected,GUIStyle style=null,bool enabled=true)
        {
            bool canUse=enabled && (confirmId==null || drawingConfirmation);
            Color fill=selected?new Color(.3f,.25f,.17f):Raised;
            if(rect.Contains(mouse)&&canUse) fill=Color.Lerp(fill,Gold,.12f);
            Fill(rect,canUse?fill:Color.Lerp(fill,Background,.35f));
            Border(rect,selected?Gold:new Color(.32f,.29f,.38f));
            Text(rect,text,style??center,canUse?(selected?Gold:TextColor):Muted,TextAnchor.MiddleCenter);
            bool before=GUI.enabled; GUI.enabled=canUse;
            bool clicked=GUI.Button(rect,GUIContent.none,GUIStyle.none); GUI.enabled=before; return clicked;
        }
        static void Text(Rect rect,string value,GUIStyle style,Color color,TextAnchor? alignment=null)
        {
            Color previous=style.normal.textColor; TextAnchor previousAlignment=style.alignment;
            style.normal.textColor=color; if(alignment.HasValue) style.alignment=alignment.Value;
            GUI.Label(rect,value,style); style.normal.textColor=previous; style.alignment=previousAlignment;
        }
        static void Fill(Rect rect,Color color)
        {
            // DrawTexture tints are linear in a Linear project; the palette is authored in sRGB.
            Color previous=GUI.color; GUI.color=QualitySettings.activeColorSpace==ColorSpace.Linear?color.linear:color;
            GUI.DrawTexture(rect,Texture2D.whiteTexture); GUI.color=previous;
        }
        static void Border(Rect r,Color color,float width=1)
        {
            Fill(new Rect(r.x,r.y,r.width,width),color); Fill(new Rect(r.x,r.y+r.height-width,r.width,width),color);
            Fill(new Rect(r.x,r.y,width,r.height),color); Fill(new Rect(r.x+r.width-width,r.y,width,r.height),color);
        }
    }
}

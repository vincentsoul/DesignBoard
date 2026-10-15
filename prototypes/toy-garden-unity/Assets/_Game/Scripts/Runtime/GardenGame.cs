using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ToyGarden
{
    [DefaultExecutionOrder(-100)]
    public sealed partial class GardenGame : MonoBehaviour
    {
        public static GardenGame Instance { get; private set; }
        public Vector3 encounterOneCenter = new(-10, 0, -5);
        public Vector3 encounterTwoCenter = new(20, 3, 19);
        public Vector3 exitPosition = new(0, 3, 24);
        public Vector3 chestPosition = new(-24, 0, 9);
        public float encounterOneRadius = 11;
        public float encounterTwoRadius = 12;
        public Transform exitGate;
        public Transform chestLid;
        public Material projectileMaterial;
        public Material effectMaterial;
        public List<GardenActor> Heroes { get; private set; } = new();
        public List<GardenActor> Enemies { get; private set; } = new();
        public GardenActor ActiveHero { get; private set; }
        public GardenChapter Chapter { get; private set; }
        public bool MapOpen { get; private set; }
        public bool IsPlaying => !Paused && !MapOpen && !EquipmentOpen && !GameOver && !Won;
        public bool IsCombatActive => activeEncounters.Any(active => active);
        public bool Paused { get; private set; }
        public bool GameOver { get; private set; }
        public bool Won { get; private set; }
        public bool HasChest { get; private set; }
        public bool AcceptPlayerInput { get; set; } = true;
        public float Elapsed { get; private set; }
        public string Message => Time.unscaledTime < messageUntil ? message : "";
        public int TotalEncounters => Chapter != null ? Chapter.encounters.Length : 2;
        public int ClearedCount => clearedEncounters.Count(cleared => cleared);
        public string Objective => Won ? "风车谷的道路已经畅通。" : GameOver ? "队伍已全部倒下。" :
            Chapter != null ? Chapter.Objective :
            !clearedEncounters[1] ? "击败花园广场的敌人" : !clearedEncounters[2] ?
            "沿坡道向上，击败风车守卫" : "前往出口拱门，按 E 离开";

        readonly bool[] activeEncounters = new bool[32];
        readonly bool[] clearedEncounters = new bool[32];
        InputActionMap inputs;
        InputAction move, attack, dodge, skill, switchNext, interact, pause, restart, map, equipment;
        string message;
        float messageUntil;
        Quaternion closedLid;

        void Awake()
        {
            Instance = this;
            usePersistentSaves = PersistentSavesEnabled;
            Progression = GardenProgression.CreateDefault();
            Application.targetFrameRate = 60;
            Physics.IgnoreLayerCollision(8, 8, true);
            var actors = FindObjectsByType<GardenActor>(FindObjectsSortMode.None);
            Heroes = actors.Where(a => a.kind <= ActorKind.Ranger).OrderBy(a => a.kind).ToList();
            Enemies = actors.Where(a => a.kind > ActorKind.Ranger).ToList();
            foreach (var actor in actors) actor.Initialize(this);
            if (Heroes.Count > 0)
            {
                ActiveHero = Heroes[0];
                ActiveHero.IsPlayerControlled = true;
            }
            if (chestLid != null) closedLid = chestLid.localRotation;
            Chapter = GetComponent<GardenChapter>();
            Chapter?.Initialize(this);
            LoadProgress();
            RefreshHeroStats();
            ConfigureInput();
            if (!TryGetComponent<GardenCameraRig>(out _)) gameObject.AddComponent<GardenCameraRig>();
            if (!TryGetComponent<GardenHUD>(out _)) gameObject.AddComponent<GardenHUD>();
            if (!TryGetComponent<GardenEquipmentUI>(out _)) gameObject.AddComponent<GardenEquipmentUI>();
            ShowMessage(loadBlocked ? SaveStatus : "欢迎来到风车谷。按 E 交互，按 M 查看地图，按 I 打开角色与装备。", loadBlocked ? 12 : 5);
        }

        void ConfigureInput()
        {
            inputs = new InputActionMap("Gameplay");
            move = inputs.AddAction("Move", InputActionType.Value);
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddBinding("<Gamepad>/leftStick");
            attack = Button("Attack", "<Mouse>/leftButton", "<Gamepad>/buttonSouth");
            attack.AddBinding("<Keyboard>/j");
            dodge = Button("Dodge", "<Keyboard>/space", "<Gamepad>/buttonEast");
            skill = Button("Skill", "<Keyboard>/q", "<Gamepad>/buttonWest");
            switchNext = Button("Switch", "<Keyboard>/tab", "<Gamepad>/leftShoulder");
            interact = Button("Interact", "<Keyboard>/e", "<Gamepad>/buttonNorth");
            pause = Button("Pause", "<Keyboard>/escape", "<Gamepad>/start");
            restart = Button("Restart", "<Keyboard>/r", "<Gamepad>/select");
            map = Button("Map", "<Keyboard>/m", "<Gamepad>/rightShoulder");
            equipment = Button("Equipment", "<Keyboard>/i", "<Gamepad>/dpad/up");
            inputs.Enable();
        }

        InputAction Button(string name, string keyboard, string gamepad)
        {
            var action = inputs.AddAction(name, InputActionType.Button);
            action.AddBinding(keyboard);
            action.AddBinding(gamepad);
            return action;
        }

        void Update()
        {
            if (AcceptPlayerInput && equipment.WasPressedThisFrame()) ToggleEquipment();
            if (AcceptPlayerInput && !EquipmentOpen && map.WasPressedThisFrame() && !Paused && !GameOver && !Won)
            {
                MapOpen = !MapOpen;
                foreach (var hero in Heroes) hero.SetMoveInput(Vector3.zero);
            }
            if (AcceptPlayerInput && pause.WasPressedThisFrame())
            {
                if (EquipmentOpen) CloseEquipment();
                else if (MapOpen) MapOpen = false;
                else TogglePause();
            }
            if (AcceptPlayerInput && (GameOver || Won) && restart.WasPressedThisFrame()) RestartGame();
            if (!IsPlaying || ActiveHero == null) return;
            Elapsed += Time.deltaTime;
            saveTimer += Time.unscaledDeltaTime;
            if (saveTimer >= 12f) { saveTimer = 0f; SaveProgress(); }
            if (AcceptPlayerInput)
            {
                if (switchNext.WasPressedThisFrame()) SwitchHero();
                var input = move.ReadValue<Vector2>();
                var camera = Camera.main;
                var forward = camera != null ? camera.transform.forward : Vector3.forward;
                var right = camera != null ? camera.transform.right : Vector3.right;
                forward.y = right.y = 0;
                var direction = Vector3.ClampMagnitude(forward.normalized * input.y + right.normalized * input.x, 1);
                ActiveHero.SetMoveInput(direction);
                if (dodge.WasPressedThisFrame()) ActiveHero.TryDodge(direction.sqrMagnitude > .01f ? direction : ActiveHero.transform.forward);
                if (skill.WasPressedThisFrame()) ActiveHero.TryAttack(true);
                if (attack.WasPressedThisFrame() && !PointerOverPartyCards()) ActiveHero.TryAttack();
                if (interact.WasPressedThisFrame()) TryInteract();
            }
            if (Chapter != null) Chapter.TickChapter();
            else
            {
                if (!clearedEncounters[1] && FlatDistance(ActiveHero.transform.position, encounterOneCenter) < encounterOneRadius - 1)
                    ActivateEncounter(1);
                if (clearedEncounters[1] && !clearedEncounters[2] && FlatDistance(ActiveHero.transform.position, encounterTwoCenter) < encounterTwoRadius - 1)
                    ActivateEncounter(2);
            }
            if (ActiveHero.transform.position.y < -5)
            {
                ActiveHero.Teleport(ActiveHero.HomePosition);
                ShowMessage("已返回安全道路。");
            }
        }

        public bool IsEncounterActive(int id) => id > 0 && id < activeEncounters.Length && activeEncounters[id];
        public bool IsEncounterCleared(int id) => id > 0 && id < clearedEncounters.Length && clearedEncounters[id];

        public void ActivateEncounter(int id)
        {
            if (!IsPlaying || id < 1 || id > TotalEncounters || activeEncounters[id] || clearedEncounters[id]) return;
            if (Chapter != null && !Chapter.CanActivateEncounter(id)) return;
            if (Chapter == null && id == 2 && !clearedEncounters[1]) return;
            activeEncounters[id] = true;
            ShowMessage(Chapter != null ? $"战斗 {id}/{TotalEncounters}：注意躲避攻击预警圈！" :
                id == 1 ? "花园遭遇战：注意躲避攻击预警圈！" : "风车守卫：留意蓄力攻击！", 4);
        }

        public bool SwitchHero(int direction = 1)
        {
            if (!IsPlaying || Heroes.Count == 0) return false;
            int start = Heroes.IndexOf(ActiveHero);
            int step = direction < 0 ? -1 : 1;
            for (int offset = 1; offset <= Heroes.Count; offset++)
            {
                int index = (start + step * offset + Heroes.Count * 2) % Heroes.Count;
                if (Heroes[index].IsAlive && Heroes[index] != ActiveHero) return SwitchTo(index);
            }
            return false;
        }

        public bool SwitchTo(int index)
        {
            if (!IsPlaying || index < 0 || index >= Heroes.Count || !Heroes[index].IsAlive || Heroes[index] == ActiveHero) return false;
            if (ActiveHero != null)
            {
                ActiveHero.SetMoveInput(Vector3.zero);
                ActiveHero.IsPlayerControlled = false;
            }
            ActiveHero = Heroes[index];
            ActiveHero.IsPlayerControlled = true;
            ActiveHero.SetMoveInput(Vector3.zero);
            ShowMessage($"已切换为{RoleName(ActiveHero.kind)}", 1.4f);
            return true;
        }

        public void NotifyDeath(GardenActor actor)
        {
            if (actor.IsEnemy)
            {
                int id = actor.encounterId;
                if (id > 0 && id < activeEncounters.Length && !clearedEncounters[id] && !Enemies.Any(e => e.encounterId == id && e.IsAlive))
                {
                    activeEncounters[id] = false;
                    clearedEncounters[id] = true;
                    ShowMessage(Chapter != null ? $"区域已清理！已完成 {ClearedCount}/{TotalEncounters} 场战斗。" :
                        id == 1 ? "广场已清理！沿坡道前往果园。" : "已击败风车守卫！出口已经开放。", 5);
                    if (Chapter != null) Chapter.TickChapter();
                    else if (ClearedCount == 2 && exitGate != null) exitGate.gameObject.SetActive(false);
                    RewardEncounter(id);
                }
            }
            else
            {
                if (!Heroes.Any(h => h.IsAlive))
                {
                    GameOver = true;
                    foreach (var hero in Heroes) hero.SetMoveInput(Vector3.zero);
                }
                else if (actor == ActiveHero) SwitchHero();
                SaveProgress();
            }
        }

        public string InteractionHint
        {
            get
            {
                if (ActiveHero == null || !IsPlaying) return "";
                if (Chapter != null) return Chapter.InteractionHint;
                if (!HasChest && Vector3.Distance(ActiveHero.transform.position, chestPosition) < 3.5f) return "E / Y：打开补给箱";
                if (Vector3.Distance(ActiveHero.transform.position, exitPosition) < 4)
                    return ClearedCount == 2 ? "E / Y：离开花园" : "完成两场战斗后开启出口";
                return "";
            }
        }

        public bool TryInteract()
        {
            if (!IsPlaying || ActiveHero == null) return false;
            if (Chapter != null) return Chapter.TryInteract();
            if (!HasChest && Vector3.Distance(ActiveHero.transform.position, chestPosition) < 3.5f)
            {
                HasChest = true;
                if (chestLid != null) chestLid.localRotation = closedLid * Quaternion.Euler(-75, 0, 0);
                foreach (var hero in Heroes.Where(h => h.IsAlive)) hero.Heal(35);
                ShowMessage("找到补给！存活的队员各恢复 35 点生命。", 4);
                return true;
            }
            if (ClearedCount == 2 && Vector3.Distance(ActiveHero.transform.position, exitPosition) < 4)
            {
                Won = true;
                foreach (var hero in Heroes) hero.SetMoveInput(Vector3.zero);
                return true;
            }
            return false;
        }

        public void TogglePause()
        {
            if (GameOver || Won) return;
            Paused = !Paused;
            foreach (var hero in Heroes) hero.SetMoveInput(Vector3.zero);
        }

        public void ToggleEquipment()
        {
            if (EquipmentOpen) { CloseEquipment(); return; }
            EquipmentOpen = true;
            MapOpen = false;
            foreach (var hero in Heroes) hero.SetMoveInput(Vector3.zero);
        }

        public void CloseEquipment()
        {
            EquipmentOpen = false;
            SaveProgress();
        }

        public void RefreshHeroStats()
        {
            if (Progression == null) return;
            foreach (var hero in Heroes) hero.ApplyGrowth(Progression.AggregateStats(hero.kind));
        }

        public void CompleteAdventure()
        {
            if (!IsPlaying || (Chapter != null && !Chapter.CanExit)) return;
            Won = true;
            foreach (var hero in Heroes) hero.SetMoveInput(Vector3.zero);
            SaveProgress();
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused && AcceptPlayerInput && !GameOver && !Won && !EquipmentOpen) Paused = true;
            if (!focused) SaveProgress();
        }

        public void RestartGame()
        {
            Paused = GameOver = Won = HasChest = MapOpen = EquipmentOpen = false;
            rewardClaims.Clear();
            Elapsed = 0;
            System.Array.Clear(activeEncounters, 0, activeEncounters.Length);
            System.Array.Clear(clearedEncounters, 0, clearedEncounters.Length);
            foreach (var projectile in FindObjectsByType<GardenProjectile>(FindObjectsSortMode.None)) Destroy(projectile.gameObject);
            foreach (var actor in Heroes.Concat(Enemies))
            {
                actor.ResetActor();
                actor.IsPlayerControlled = false;
            }
            ActiveHero = Heroes.FirstOrDefault();
            if (ActiveHero != null) ActiveHero.IsPlayerControlled = true;
            if (exitGate != null) exitGate.gameObject.SetActive(true);
            if (chestLid != null) chestLid.localRotation = closedLid;
            Chapter?.ResetChapter();
            SaveProgress();
            ShowMessage("章节已重置，角色等级与全部装备已保留。", 3);
        }

        public void ShowMessage(string text, float seconds = 2f)
        {
            message = text;
            messageUntil = Time.unscaledTime + seconds;
        }

        bool PointerOverPartyCards()
        {
            if (attack.activeControl?.device is not Mouse || Mouse.current == null) return false;
            float scale = Mathf.Clamp(Screen.width / 1280f, .65f, 1.7f);
            Vector2 pointer = Mouse.current.position.ReadValue() / scale;
            if (new Rect(390, 22, 150, 42).Contains(new Vector2(pointer.x, Screen.height / scale - pointer.y))) return true;
            return pointer.y >= 38 && pointer.y <= 126 && pointer.x >= 24 && pointer.x <= 24 + Heroes.Count * 209;
        }

        public static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
        public static string RoleName(ActorKind kind) => kind switch
        {
            ActorKind.Blade => "剑士", ActorKind.Hammer => "重锤手", ActorKind.Ranger => "游侠",
            ActorKind.Boss => "风车守卫", ActorKind.Spitter => "吐籽怪", _ => "花园小怪"
        };

        void OnDestroy()
        {
            inputs?.Disable();
            inputs?.Dispose();
            if (Instance == this) Instance = null;
        }
    }
}

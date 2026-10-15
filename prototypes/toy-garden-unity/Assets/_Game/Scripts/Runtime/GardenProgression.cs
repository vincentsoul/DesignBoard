using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ToyGarden
{
    // This model owns gear and currencies only. Scene state and file writes belong to the caller.
    [Serializable]
    public sealed class GardenProgression
    {
        public const int CurrentVersion = 1;
        public const int Capacity = 200;
        public const int MaxLevel = 50;
        public const int MaxEnhancement = 10;
        public const int MaxCurrency = 2000000000;
        public int version = CurrentVersion;
        public int gold, ore, essence;
        public int nextItemId = 1;
        public int nextRollSeed = 73021;
        public List<HeroProgress> heroes = new();
        // Equipped objects remain here so every unique ID has one owner and one canonical record.
        public List<GearItem> inventory = new();
        public int BackpackCount => inventory.Count(item => item != null && !IsEquipped(item.id));

        public static GardenProgression CreateDefault()
        {
            var model = new GardenProgression { gold = 5000, ore = 120, essence = 60 };
            for (int i = 0; i < 3; i++)
            {
                var hero = new HeroProgress { hero = (ActorKind)i };
                model.heroes.Add(hero);
                var weapon = model.CreateItem(GearSlot.Weapon, GearQuality.Common, GearSet.None, (ActorKind)i, 1, new System.Random(100 + i));
                weapon.isTraining = true;
                weapon.name = i == 0 ? "训练木剑" : i == 1 ? "训练木锤" : "训练短弓";
                weapon.baseStats = new GearStats();
                model.inventory.Add(weapon);
                hero.equipped.Add(new GearEquipSlot { slot = GearSlot.Weapon, itemId = weapon.id });
            }
            var starter = new[]
            {
                (GearSlot.Weapon, GearQuality.Rare, GearSet.Gale, ActorKind.Blade),
                (GearSlot.Weapon, GearQuality.Uncommon, GearSet.Bastion, ActorKind.Hammer),
                (GearSlot.Weapon, GearQuality.Rare, GearSet.Ember, ActorKind.Ranger),
                (GearSlot.Gloves, GearQuality.Rare, GearSet.Gale, ActorKind.Blade),
                (GearSlot.Boots, GearQuality.Uncommon, GearSet.Gale, ActorKind.Blade),
                (GearSlot.Chest, GearQuality.Rare, GearSet.Bastion, ActorKind.Blade),
                (GearSlot.Ring, GearQuality.Epic, GearSet.Ember, ActorKind.Blade),
                (GearSlot.Amulet, GearQuality.Rare, GearSet.Ember, ActorKind.Blade)
            };
            for (int i = 0; i < starter.Length; i++)
            {
                var (slot, quality, set, hero) = starter[i];
                model.inventory.Add(model.CreateItem(slot, quality, set, hero, 1, new System.Random(1337 + i)));
            }
            return model;
        }

        public HeroProgress GetHero(ActorKind hero) => heroes.FirstOrDefault(item => item != null && item.hero == hero);
        public GearItem FindItem(string id) => string.IsNullOrEmpty(id) ? null : inventory.FirstOrDefault(item => item != null && item.id == id);
        public GearItem GetEquipped(ActorKind hero, GearSlot slot)
        {
            var equipped = GetHero(hero)?.equipped.FirstOrDefault(item => item.slot == slot);
            return equipped == null ? null : FindItem(equipped.itemId);
        }
        public HeroProgress EquippedBy(string id) => string.IsNullOrEmpty(id) ? null :
            heroes.FirstOrDefault(hero => hero != null && hero.equipped.Any(slot => slot.itemId == id));
        public bool IsEquipped(string id) => EquippedBy(id) != null;
        public int SetCount(ActorKind hero, GearSet set) => set == GearSet.None ? 0 :
            GetHero(hero)?.equipped.Count(slot => FindItem(slot.itemId)?.set == set) ?? 0;

        public GearResult Equip(ActorKind hero, string id)
        {
            var owner = GetHero(hero);
            var item = FindItem(id);
            if (owner == null || item == null) return GearResult.Fail("未找到角色或装备。");
            if (item.slot == GearSlot.Weapon && item.weaponClass != hero) return GearResult.Fail("这件武器不适合当前职业。");
            if (owner.level < item.itemLevel) return GearResult.Fail($"角色达到 {item.itemLevel} 级后才能装备。");
            var wearing = EquippedBy(id);
            if (wearing != null) return GearResult.Fail(wearing == owner ? "该角色已经装备了这件物品。" : "其他队员正在使用这件装备，请先卸下。");
            owner.equipped.RemoveAll(slot => slot.slot == item.slot);
            owner.equipped.Add(new GearEquipSlot { slot = item.slot, itemId = id });
            return GearResult.Ok($"{HeroName(hero)}已装备{item.name}。", item);
        }

        public GearResult Unequip(ActorKind hero, GearSlot slot)
        {
            var item = GetEquipped(hero, slot);
            if (item == null) return GearResult.Fail("这个栏位没有装备。");
            if (BackpackCount >= Capacity) return GearResult.Fail("背包已满，请先整理出空位再卸下装备。");
            GetHero(hero).equipped.RemoveAll(entry => entry.slot == slot);
            return GearResult.Ok($"已卸下{item.name}。", item);
        }

        public GearResult ToggleLock(string id)
        {
            var item = FindItem(id);
            if (item == null) return GearResult.Fail("未找到这件装备。");
            item.locked = !item.locked;
            return GearResult.Ok(item.locked ? "装备已锁定，无法分解或洗炼。" : "装备已解锁。", item);
        }

        public void SortInventory(GearSort sort)
        {
            IEnumerable<GearItem> ordered = sort switch
            {
                GearSort.Slot => inventory.OrderBy(item => item.slot).ThenByDescending(item => item.quality).ThenByDescending(item => item.itemLevel),
                GearSort.Level => inventory.OrderByDescending(item => item.itemLevel).ThenByDescending(item => item.quality),
                GearSort.Set => inventory.OrderBy(item => item.set).ThenBy(item => item.slot).ThenByDescending(item => item.quality),
                GearSort.Newest => inventory.OrderByDescending(item => item.createdOrder),
                _ => inventory.OrderByDescending(item => item.quality).ThenByDescending(item => item.itemLevel).ThenBy(item => item.slot)
            };
            inventory = ordered.ToList();
        }

        public GearResult Dismantle(string id)
        {
            var item = FindItem(id);
            if (item == null) return GearResult.Fail("未找到这件装备。");
            if (item.locked) return GearResult.Fail("装备已锁定，请先解锁。");
            if (IsEquipped(id)) return GearResult.Fail("正在使用的装备不能分解，请先卸下。");
            var reward = DismantleYield(item);
            inventory.Remove(item);
            AddCurrencies(reward.gold, reward.ore, reward.essence);
            return GearResult.Ok($"已分解{item.name}，获得{reward}。");
        }

        public GearResult Enhance(string id)
        {
            var item = FindItem(id);
            if (item == null) return GearResult.Fail("未找到这件装备。");
            if (item.isTraining) return GearResult.Fail("训练武器无法强化，请先更换战利品装备。");
            if (item.enhancement >= MaxEnhancement) return GearResult.Fail("这件装备已达到强化上限 +10。");
            var cost = EnhanceCost(item);
            if (!CanAfford(cost)) return GearResult.Fail($"强化材料不足，需要{cost}。");
            Spend(cost);
            item.enhancement++;
            return GearResult.Ok($"强化成功：{item.name} +{item.enhancement}。", item);
        }

        public GearResult Reroll(string id, int seed = -1)
        {
            var item = FindItem(id);
            if (item == null) return GearResult.Fail("未找到这件装备。");
            if (item.locked) return GearResult.Fail("装备已锁定，请先解锁再洗炼。");
            if (item.isTraining || item.quality == GearQuality.Common) return GearResult.Fail("这件装备没有可洗炼的随机词条。");
            var cost = RerollCost(item);
            if (!CanAfford(cost)) return GearResult.Fail($"洗炼材料不足，需要{cost}。");
            var affixes = RollAffixes(item.quality, item.itemLevel, RandomFor(seed));
            Spend(cost);
            item.affixes = affixes;
            return GearResult.Ok($"已重新生成{item.name}的全部随机词条。", item);
        }

        public GearResult Craft(GearSlot slot, GearQuality quality, GearSet set, ActorKind hero, int seed = -1)
        {
            var owner = GetHero(hero);
            if (owner == null || !Enum.IsDefined(typeof(GearSlot), slot) || !Enum.IsDefined(typeof(GearQuality), quality)
                || !Enum.IsDefined(typeof(GearSet), set)) return GearResult.Fail("打造选项无效。");
            if (BackpackCount >= Capacity) return GearResult.Fail("背包已满，无法收下新打造的装备。");
            var cost = CraftCost(slot, quality, set);
            if (!CanAfford(cost)) return GearResult.Fail($"打造材料不足，需要{cost}。");
            var item = CreateItem(slot, quality, set, hero, owner.level, RandomFor(seed));
            Spend(cost);
            inventory.Add(item);
            return GearResult.Ok($"打造成功：{QualityName(quality)}{item.name}。", item);
        }

        public GearResult AddLoot(int itemLevel, int seed = -1)
        {
            if (BackpackCount >= Capacity) return GearResult.Fail("背包已满，这件战利品未能收取，请先整理背包。");
            var random = RandomFor(seed);
            double roll = random.NextDouble();
            var quality = roll < .40 ? GearQuality.Common : roll < .68 ? GearQuality.Uncommon :
                roll < .88 ? GearQuality.Rare : roll < .98 ? GearQuality.Epic : GearQuality.Legendary;
            var slot = (GearSlot)random.Next(8);
            var set = quality == GearQuality.Common || random.NextDouble() < .35 ? GearSet.None : (GearSet)random.Next(1, 4);
            var item = CreateItem(slot, quality, set, (ActorKind)random.Next(3), Clamp(itemLevel, 1, MaxLevel), random);
            inventory.Add(item);
            return GearResult.Ok($"获得{QualityName(quality)}装备：{item.name}。", item);
        }

        public GearResult AddExperience(ActorKind hero, int amount)
        {
            var owner = GetHero(hero);
            if (owner == null || amount <= 0) return GearResult.Fail("经验奖励无效。");
            if (owner.level >= MaxLevel) return GearResult.Ok($"{HeroName(hero)}已达到 {MaxLevel} 级。");
            int before = owner.level;
            GiveExperience(owner, amount);
            return GearResult.Ok(owner.level > before ? $"{HeroName(hero)}升至 {owner.level} 级！" : $"{HeroName(hero)}获得 {amount} 点经验。");
        }

        public GearResult GrantReward(int goldAmount, int oreAmount, int essenceAmount, int experienceAmount, int lootLevel = 0, int seed = -1)
        {
            goldAmount = Math.Max(0, goldAmount); oreAmount = Math.Max(0, oreAmount); essenceAmount = Math.Max(0, essenceAmount);
            experienceAmount = Math.Max(0, experienceAmount);
            AddCurrencies(goldAmount, oreAmount, essenceAmount);
            var levels = new List<string>();
            foreach (var hero in heroes)
            {
                int previous = hero.level;
                GiveExperience(hero, experienceAmount);
                if (hero.level > previous) levels.Add($"{HeroName(hero.hero)} {hero.level} 级");
            }
            string text = $"队伍获得 {experienceAmount} 经验、{goldAmount} 金币、{oreAmount} 矿石、{essenceAmount} 精粹。";
            if (levels.Count > 0) text += " 升级：" + string.Join("、", levels) + "。";
            GearResult loot = lootLevel > 0 ? AddLoot(lootLevel, seed) : null;
            if (loot != null) text += " " + loot.message;
            return GearResult.Ok(text, loot?.item);
        }

        public GearStats ItemStats(GearItem item)
        {
            var result = new GearStats();
            if (item == null) return result;
            result.Add(item.baseStats, 1f + .08f * item.enhancement);
            foreach (var affix in item.affixes) result.Add(affix.stat, affix.value);
            return result;
        }

        public GearStats AggregateStats(ActorKind hero) => AggregateWithReplacement(hero, null);

        // Compare whole loadouts so gaining or breaking a two/four-piece bonus is visible before equipping.
        public GearStats CompareStats(ActorKind hero, GearItem item)
        {
            var result = AggregateWithReplacement(hero, item);
            result.Add(AggregateStats(hero), -1f);
            return result;
        }

        GearStats AggregateWithReplacement(ActorKind hero, GearItem replacement)
        {
            var result = new GearStats();
            var owner = GetHero(hero);
            if (owner == null) return result;
            result.attack = Math.Max(0, owner.level - 1) * 2f;
            result.health = Math.Max(0, owner.level - 1) * 8f;
            var counts = new int[4];
            foreach (GearSlot slot in Enum.GetValues(typeof(GearSlot)))
            {
                var item = replacement != null && replacement.slot == slot ? replacement : GetEquipped(hero, slot);
                if (item == null) continue;
                result.Add(ItemStats(item));
                if (item.set > GearSet.None && item.set <= GearSet.Ember) counts[(int)item.set]++;
            }
            if (counts[(int)GearSet.Gale] >= 2) result.moveSpeed += .06f;
            if (counts[(int)GearSet.Gale] >= 4) { result.cooldownReduction += .10f; result.critChance += .04f; }
            if (counts[(int)GearSet.Bastion] >= 2) result.defense += 12f;
            if (counts[(int)GearSet.Bastion] >= 4) { result.health += 60f; result.defense += 8f; }
            if (counts[(int)GearSet.Ember] >= 2) result.attack += 8f;
            if (counts[(int)GearSet.Ember] >= 4) { result.critChance += .06f; result.lifeSteal += .04f; }
            result.critChance = Math.Min(result.critChance, .65f);
            result.critDamage = Math.Min(result.critDamage, 2f);
            result.cooldownReduction = Math.Min(result.cooldownReduction, .45f);
            result.lifeSteal = Math.Min(result.lifeSteal, .20f);
            result.moveSpeed = Math.Min(result.moveSpeed, .35f);
            return result;
        }

        public bool CanAfford(GearCost cost) => cost.gold >= 0 && cost.ore >= 0 && cost.essence >= 0
            && gold >= cost.gold && ore >= cost.ore && essence >= cost.essence;
        void Spend(GearCost cost) { gold -= cost.gold; ore -= cost.ore; essence -= cost.essence; }
        void AddCurrencies(int money, int mineral, int dust)
        {
            gold = SaturatingAdd(gold, money); ore = SaturatingAdd(ore, mineral); essence = SaturatingAdd(essence, dust);
        }

        public static GearCost EnhanceCost(GearItem item)
        {
            int n = Clamp(item?.enhancement ?? 0, 0, MaxEnhancement);
            return new GearCost(80 * (n + 1) * (n + 1), 2 + 2 * n, Math.Max(0, n - 3));
        }
        public static GearCost RerollCost(GearItem item)
        {
            int q = Clamp((int)(item?.quality ?? GearQuality.Common), 0, 4);
            return new GearCost(100 * (q + 1), 0, 2 + q * 2);
        }
        public static GearCost CraftCost(GearSlot slot, GearQuality quality, GearSet set)
        {
            int q = Clamp((int)quality, 0, 4);
            int[] money = { 80, 160, 320, 800, 1800 }, minerals = { 4, 8, 12, 20, 36 }, dust = { 0, 2, 4, 10, 24 };
            int cost = money[q];
            if (slot == GearSlot.Weapon) cost = cost * 6 / 5;
            if (set != GearSet.None) cost = cost * 13 / 10;
            return new GearCost(cost, minerals[q] + (set != GearSet.None ? 4 : 0), dust[q] + (set != GearSet.None ? 4 : 0));
        }
        public static GearCost DismantleYield(GearItem item)
        {
            if (item == null) return new GearCost();
            int q = Clamp((int)item.quality, 0, 4), n = Clamp(item.enhancement, 0, MaxEnhancement);
            return new GearCost(20 * (q + 1) * (n + 1), 1 + q + n / 2, Math.Max(0, q - 1));
        }
        public static int ExperienceRequired(int level) => 100 + 60 * Clamp(level, 1, MaxLevel);

        static void GiveExperience(HeroProgress hero, int amount)
        {
            long total = (long)hero.experience + Math.Max(0, amount);
            while (hero.level < MaxLevel && total >= ExperienceRequired(hero.level))
            {
                total -= ExperienceRequired(hero.level);
                hero.level++;
            }
            hero.experience = hero.level >= MaxLevel ? 0 : (int)total;
        }

        System.Random RandomFor(int seed)
        {
            if (seed != -1) return new System.Random(seed);
            int selected = nextRollSeed;
            nextRollSeed = nextRollSeed == int.MaxValue ? 73021 : nextRollSeed + 1;
            return new System.Random(selected);
        }

        GearItem CreateItem(GearSlot slot, GearQuality quality, GearSet set, ActorKind hero, int level, System.Random random)
        {
            nextItemId = Clamp(nextItemId, 1, 1500000000);
            while (FindItem("gear-" + nextItemId.ToString("D8")) != null) nextItemId++;
            int sequence = nextItemId++;
            var item = new GearItem
            {
                id = "gear-" + sequence.ToString("D8"), createdOrder = sequence, slot = slot, quality = quality,
                set = set, itemLevel = Clamp(level, 1, MaxLevel), weaponClass = hero,
                baseStats = BaseFor(slot, quality, level), affixes = RollAffixes(quality, level, random)
            };
            item.name = ItemName(item);
            return item;
        }

        static GearStats BaseFor(GearSlot slot, GearQuality quality, int level)
        {
            float factor = (1f + (int)quality * .25f) * (1f + .12f * (Clamp(level, 1, MaxLevel) - 1));
            var stats = new GearStats();
            switch (slot)
            {
                case GearSlot.Weapon: stats.attack = 5f; break;
                case GearSlot.Helmet: stats.defense = 2f; stats.health = 8f; break;
                case GearSlot.Chest: stats.defense = 3f; stats.health = 18f; break;
                case GearSlot.Legs: stats.defense = 3f; stats.health = 10f; break;
                case GearSlot.Gloves: stats.attack = 2f; stats.defense = 1f; break;
                case GearSlot.Boots: stats.defense = 2f; stats.health = 6f; break;
                case GearSlot.Ring: stats.attack = 2f; stats.health = 6f; break;
                case GearSlot.Amulet: stats.defense = 1f; stats.health = 14f; break;
            }
            var result = new GearStats(); result.Add(stats, factor); return result;
        }

        static List<GearAffix> RollAffixes(GearQuality quality, int level, System.Random random)
        {
            var choices = Enumerable.Range(0, 8).ToList();
            var result = new List<GearAffix>();
            for (int i = 0; i < (int)quality; i++)
            {
                int index = random.Next(choices.Count);
                GearStat stat = (GearStat)choices[index]; choices.RemoveAt(index);
                AffixRange(stat, level, out float min, out float max);
                float value = min + (max - min) * (float)random.NextDouble();
                value = (float)Math.Round(value, stat <= GearStat.Health ? 1 : 4);
                value = Math.Max(min, Math.Min(max, value));
                result.Add(new GearAffix { stat = stat, value = value });
            }
            return result;
        }

        static void AffixRange(GearStat stat, int level, out float min, out float max)
        {
            (min, max) = stat switch
            {
                GearStat.Attack => (4f, 8f), GearStat.Defense => (3f, 7f), GearStat.Health => (18f, 35f),
                GearStat.CritChance => (.02f, .05f), GearStat.CritDamage => (.05f, .12f),
                GearStat.CooldownReduction => (.02f, .05f), GearStat.LifeSteal => (.01f, .025f),
                _ => (.02f, .045f)
            };
            if (stat <= GearStat.Health)
            {
                float factor = 1f + .12f * (Clamp(level, 1, MaxLevel) - 1);
                min *= factor; max *= factor;
            }
        }

        public string ToJson(bool pretty = true) => JsonUtility.ToJson(this, pretty);

        public static GardenProgression FromJson(string json, out string message)
        {
            return TryFromJson(json, out var result, out message) ? result : null;
        }

        public static bool TryFromJson(string json, out GardenProgression result, out string message)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(json) || json.Length > 4000000 || !json.TrimStart().StartsWith("{"))
            { message = "存档内容为空、过大或格式不正确。"; return false; }
            try
            {
                var parsed = JsonUtility.FromJson<GardenProgression>(json);
                if (parsed == null || !json.Contains("\"heroes\"") || !json.Contains("\"inventory\""))
                { message = "存档缺少角色与背包数据。"; return false; }
                if (parsed.version > CurrentVersion || parsed.version < 0)
                { message = "此存档版本不受当前游戏支持。"; return false; }
                message = parsed.ValidateAndRepair();
                result = parsed;
                return true;
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException || error is OverflowException)
            { message = "无法读取存档，原文件不会被覆盖。"; return false; }
        }

        public string ValidateAndRepair()
        {
            bool repaired = version != CurrentVersion;
            version = CurrentVersion;
            int oldGold = gold, oldOre = ore, oldEssence = essence;
            gold = Clamp(gold, 0, MaxCurrency); ore = Clamp(ore, 0, MaxCurrency); essence = Clamp(essence, 0, MaxCurrency);
            repaired |= gold != oldGold || ore != oldOre || essence != oldEssence;
            if (heroes == null) { heroes = new List<HeroProgress>(); repaired = true; }
            if (inventory == null) { inventory = new List<GearItem>(); repaired = true; }
            var cleanHeroes = new List<HeroProgress>();
            for (int i = 0; i < 3; i++)
            {
                var hero = heroes.FirstOrDefault(candidate => candidate != null && candidate.hero == (ActorKind)i);
                if (hero == null) { hero = new HeroProgress { hero = (ActorKind)i }; repaired = true; }
                int oldLevel = hero.level, oldExperience = hero.experience;
                hero.level = Clamp(hero.level, 1, MaxLevel);
                hero.experience = Clamp(hero.experience, 0, hero.level == MaxLevel ? 0 : ExperienceRequired(hero.level) - 1);
                if (hero.equipped == null) { hero.equipped = new List<GearEquipSlot>(); repaired = true; }
                repaired |= hero.level != oldLevel || hero.experience != oldExperience;
                cleanHeroes.Add(hero);
            }
            repaired |= heroes.Count != 3;
            heroes = cleanHeroes;
            var ids = new HashSet<string>();
            var reservedIds = new HashSet<string>(inventory.Where(item => item != null && !string.IsNullOrWhiteSpace(item.id))
                .Select(item => item.id));
            var cleanItems = new List<GearItem>();
            nextItemId = Clamp(nextItemId, 1, 1000000000);
            int greatest = inventory.Where(item => item != null).Select(item => Clamp(item.createdOrder, 0, 1000000000)).DefaultIfEmpty().Max();
            nextItemId = Math.Max(nextItemId, greatest + 1);
            foreach (var item in inventory)
            {
                if (item == null || !Enum.IsDefined(typeof(GearSlot), item.slot) || !Enum.IsDefined(typeof(GearQuality), item.quality)
                    || !Enum.IsDefined(typeof(GearSet), item.set) || (item.slot == GearSlot.Weapon && !IsHero(item.weaponClass)))
                { repaired = true; continue; }
                if (string.IsNullOrWhiteSpace(item.id) || item.id.Length > 80 || !ids.Add(item.id))
                {
                    do { item.id = "gear-" + nextItemId++.ToString("D8"); }
                    while (reservedIds.Contains(item.id) || !ids.Add(item.id));
                    repaired = true;
                }
                int level = Clamp(item.itemLevel, 1, MaxLevel), enhancement = Clamp(item.enhancement, 0, MaxEnhancement);
                repaired |= level != item.itemLevel || enhancement != item.enhancement;
                item.itemLevel = level; item.enhancement = enhancement;
                item.createdOrder = Clamp(item.createdOrder, 0, 1000000000);
                if (item.isTraining)
                {
                    repaired |= item.slot != GearSlot.Weapon || item.quality != GearQuality.Common || item.set != GearSet.None
                        || item.itemLevel != 1 || item.enhancement != 0 || (item.affixes != null && item.affixes.Count > 0);
                    item.slot = GearSlot.Weapon; item.quality = GearQuality.Common; item.set = GearSet.None;
                    item.itemLevel = 1; item.enhancement = 0; item.affixes = new List<GearAffix>();
                    if (!IsHero(item.weaponClass)) { item.weaponClass = ActorKind.Blade; repaired = true; }
                }
                var expected = item.isTraining ? new GearStats() : BaseFor(item.slot, item.quality, item.itemLevel);
                repaired |= item.baseStats == null || Enum.GetValues(typeof(GearStat)).Cast<GearStat>()
                    .Any(stat => Math.Abs(expected.Get(stat) - item.baseStats.Get(stat)) > .001f || !Finite(item.baseStats.Get(stat)));
                item.baseStats = expected;
                if (item.affixes == null) repaired = true;
                var affixes = new List<GearAffix>(); var stats = new HashSet<GearStat>();
                foreach (var affix in item.affixes ?? new List<GearAffix>())
                {
                    if (affix == null || !Enum.IsDefined(typeof(GearStat), affix.stat) || !Finite(affix.value)
                        || affix.value <= 0 || !stats.Add(affix.stat) || affixes.Count >= (int)item.quality)
                    { repaired = true; continue; }
                    AffixRange(affix.stat, item.itemLevel, out float min, out float max);
                    float value = Math.Max(min, Math.Min(max, affix.value));
                    repaired |= Math.Abs(value - affix.value) > .0001f;
                    affix.value = value;
                    affixes.Add(affix);
                }
                item.affixes = affixes;
                if (string.IsNullOrWhiteSpace(item.name) || item.name.Length > 60 || item.name.Contains("<") || item.name.Contains(">"))
                { item.name = ItemName(item); repaired = true; }
                cleanItems.Add(item);
            }
            inventory = cleanItems;
            var wornIds = new HashSet<string>();
            foreach (var hero in heroes)
            {
                var occupied = new HashSet<GearSlot>(); var equipped = new List<GearEquipSlot>();
                foreach (var entry in hero.equipped)
                {
                    var item = entry == null ? null : FindItem(entry.itemId);
                    if (item == null || item.slot != entry.slot || item.itemLevel > hero.level
                        || (item.slot == GearSlot.Weapon && item.weaponClass != hero.hero)
                        || occupied.Contains(entry.slot) || wornIds.Contains(entry.itemId)) { repaired = true; continue; }
                    occupied.Add(entry.slot); wornIds.Add(entry.itemId); equipped.Add(entry);
                }
                hero.equipped = equipped;
            }
            int backpack = 0;
            inventory = inventory.Where(item => wornIds.Contains(item.id) || ++backpack <= Capacity).ToList();
            repaired |= backpack > Capacity;
            return repaired ? "存档已读取，并修复了无效或越界的数据。" : "存档已读取。";
        }

        static bool IsHero(ActorKind hero) => hero >= ActorKind.Blade && hero <= ActorKind.Ranger;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
        static int SaturatingAdd(int current, int amount) => (int)Math.Max(0, Math.Min(MaxCurrency, (long)current + Math.Max(0, amount)));
        public static string HeroName(ActorKind hero) => hero switch { ActorKind.Blade => "剑士", ActorKind.Hammer => "重锤手", ActorKind.Ranger => "游侠", _ => "队员" };
        public static string SlotName(GearSlot slot) => slot switch
        {
            GearSlot.Weapon => "武器", GearSlot.Helmet => "头盔", GearSlot.Chest => "胸甲", GearSlot.Legs => "腿甲",
            GearSlot.Gloves => "手套", GearSlot.Boots => "靴子", GearSlot.Ring => "戒指", GearSlot.Amulet => "项链", _ => "装备"
        };
        public static string QualityName(GearQuality quality) => quality switch
        {
            GearQuality.Common => "普通", GearQuality.Uncommon => "精良", GearQuality.Rare => "稀有",
            GearQuality.Epic => "史诗", GearQuality.Legendary => "传说", _ => "未知"
        };
        public static string SetName(GearSet set) => set switch { GearSet.Gale => "疾风", GearSet.Bastion => "磐石", GearSet.Ember => "余烬", _ => "无套装" };
        public static string StatName(GearStat stat) => stat switch
        {
            GearStat.Attack => "攻击", GearStat.Defense => "防御", GearStat.Health => "生命", GearStat.CritChance => "暴击率",
            GearStat.CritDamage => "暴击伤害", GearStat.CooldownReduction => "冷却缩减", GearStat.LifeSteal => "吸血", GearStat.MoveSpeed => "移速", _ => "属性"
        };
        public static string ItemName(GearItem item)
        {
            string kind = item.slot == GearSlot.Weapon ? item.weaponClass switch
            { ActorKind.Blade => "长剑", ActorKind.Hammer => "重锤", _ => "长弓" } : SlotName(item.slot);
            return (item.set == GearSet.None ? "花园" : SetName(item.set)) + kind;
        }
        public static string SetBonusDescription(GearSet set, int pieces) => (set, pieces) switch
        {
            (GearSet.Gale, 2) => "移速 +6%",
            (GearSet.Gale, 4) => "冷却缩减 +10%、暴击率 +4%",
            (GearSet.Bastion, 2) => "防御 +12",
            (GearSet.Bastion, 4) => "生命 +60、防御 +8",
            (GearSet.Ember, 2) => "攻击 +8",
            (GearSet.Ember, 4) => "暴击率 +6%、吸血 +4%",
            _ => "这件装备不属于套装。"
        };
    }
}

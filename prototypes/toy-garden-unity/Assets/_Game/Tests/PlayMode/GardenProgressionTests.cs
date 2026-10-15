using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ToyGarden.Tests
{
    public class GardenProgressionModelTests
    {
        [Test]
        public void EquipmentEnforcesWeaponClassReplacementAndUniqueOwnership()
        {
            var progression = FundedProgression();
            var bow = Craft(progression, GearSlot.Weapon, GearQuality.Rare, GearSet.None, ActorKind.Ranger, 101);
            int count = progression.inventory.Count;
            Assert.IsFalse(progression.Equip(ActorKind.Blade, bow.id).success);
            Assert.IsFalse(progression.Equip(ActorKind.Hammer, bow.id).success);
            Assert.IsTrue(progression.Equip(ActorKind.Ranger, bow.id).success);
            Assert.AreEqual(bow.id, progression.GetEquipped(ActorKind.Ranger, GearSlot.Weapon).id);
            Assert.AreEqual(count, progression.inventory.Count, "Replacing equipment must retain the old item in the inventory.");

            var first = Craft(progression, GearSlot.Helmet, GearQuality.Rare, GearSet.Gale, ActorKind.Blade, 102);
            var second = Craft(progression, GearSlot.Helmet, GearQuality.Epic, GearSet.Gale, ActorKind.Blade, 103);
            Assert.IsTrue(progression.Equip(ActorKind.Blade, first.id).success);
            string occupiedState = progression.ToJson(false);
            Assert.IsFalse(progression.Equip(ActorKind.Hammer, first.id).success);
            Assert.AreEqual(occupiedState, progression.ToJson(false), "A failed cross-hero equip must not steal the item.");
            Assert.IsTrue(progression.Equip(ActorKind.Blade, second.id).success);
            Assert.IsFalse(progression.IsEquipped(first.id));
            Assert.IsNotNull(progression.FindItem(first.id));
            Assert.AreEqual(second.id, progression.GetEquipped(ActorKind.Blade, GearSlot.Helmet).id);
            Assert.IsTrue(progression.Equip(ActorKind.Hammer, first.id).success,
                "Non-weapon slots must remain usable by another profession once unequipped.");
            Assert.AreEqual(1, progression.GetHero(ActorKind.Blade).equipped.Count(slot => slot.slot == GearSlot.Helmet));
            Assert.AreEqual(1, progression.GetHero(ActorKind.Hammer).equipped.Count(slot => slot.slot == GearSlot.Helmet));
        }

        [Test]
        public void EnhancementChargesTheDisplayedCostPreservesAffixesAndStopsAtTen()
        {
            var progression = FundedProgression();
            var item = Craft(progression, GearSlot.Weapon, GearQuality.Rare, GearSet.Ember, ActorKind.Blade, 201);
            string affixes = JsonUtility.ToJson(new AffixSnapshot { affixes = item.affixes });
            float[] baseStats = Stats(item.baseStats);
            for (int level = 0; level < GardenProgression.MaxEnhancement; level++)
            {
                var cost = GardenProgression.EnhanceCost(item);
                Assert.AreEqual(80 * (level + 1) * (level + 1), cost.gold);
                Assert.AreEqual(2 + 2 * level, cost.ore);
                Assert.AreEqual(Math.Max(0, level - 3), cost.essence);
                var before = Wallet(progression);
                Assert.IsTrue(progression.Enhance(item.id).success);
                Assert.AreEqual(level + 1, item.enhancement);
                AssertWalletCost(progression, before, cost);
                Assert.AreEqual(affixes, JsonUtility.ToJson(new AffixSnapshot { affixes = item.affixes }));
            }
            Assert.AreEqual(10, item.enhancement);
            float[] expected = baseStats.Select(value => value * 1.8f).ToArray();
            foreach (var affix in item.affixes) expected[(int)affix.stat] += affix.value;
            AssertStats(expected, Stats(progression.ItemStats(item)), "Enhancement must scale only the base stats.");
            string capped = progression.ToJson(false);
            Assert.IsFalse(progression.Enhance(item.id).success);
            Assert.AreEqual(capped, progression.ToJson(false));
            var training = progression.inventory.First(gear => gear.isTraining);
            Assert.IsFalse(progression.Enhance(training.id).success);
            Assert.AreEqual(capped, progression.ToJson(false));
        }

        [Test]
        public void CraftingAndEnhancementFailAtomicallyWhenResourcesAreMissing()
        {
            var progression = FundedProgression();
            var cost = GardenProgression.CraftCost(GearSlot.Chest, GearQuality.Legendary, GearSet.Bastion);
            progression.gold = cost.gold;
            progression.ore = Math.Max(0, cost.ore - 1);
            progression.essence = cost.essence;
            Assert.Greater(cost.ore, 0, "The selected recipe should require ore.");
            string before = progression.ToJson(false);
            Assert.IsFalse(progression.Craft(GearSlot.Chest, GearQuality.Legendary, GearSet.Bastion, ActorKind.Blade, 301).success);
            Assert.AreEqual(before, progression.ToJson(false), "A missing ingredient must not partially spend currency or allocate an item.");
            progression.ore = cost.ore;
            int count = progression.inventory.Count;
            var crafted = progression.Craft(GearSlot.Chest, GearQuality.Legendary, GearSet.Bastion, ActorKind.Blade, 301);
            Assert.IsTrue(crafted.success);
            Assert.AreEqual(count + 1, progression.inventory.Count);
            Assert.AreEqual(0, progression.gold);
            Assert.AreEqual(0, progression.ore);
            Assert.AreEqual(0, progression.essence);
            Assert.AreEqual(GearSlot.Chest, crafted.item.slot);
            Assert.AreEqual(GearQuality.Legendary, crafted.item.quality);
            Assert.AreEqual(GearSet.Bastion, crafted.item.set);
            before = progression.ToJson(false);
            Assert.IsFalse(progression.Enhance(crafted.item.id).success);
            Assert.AreEqual(before, progression.ToJson(false), "Failed enhancement must preserve both the item and wallet.");
        }

        [Test]
        public void AFullBackpackRejectsCraftingWithoutSpendingCurrency()
        {
            var progression = FundedProgression();
            int guard = 0;
            while (progression.BackpackCount < GardenProgression.Capacity && guard++ <= GardenProgression.Capacity)
                Assert.IsTrue(progression.Craft(GearSlot.Ring, GearQuality.Common, GearSet.None, ActorKind.Blade, guard).success);
            Assert.AreEqual(GardenProgression.Capacity, progression.BackpackCount);
            string full = progression.ToJson(false);
            Assert.IsFalse(progression.Craft(GearSlot.Ring, GearQuality.Legendary, GearSet.Gale, ActorKind.Blade, 999).success);
            Assert.AreEqual(full, progression.ToJson(false));
        }

        [Test]
        public void LockedAndEquippedItemsCannotBeDismantled()
        {
            var progression = FundedProgression();
            var item = Craft(progression, GearSlot.Gloves, GearQuality.Epic, GearSet.Gale, ActorKind.Blade, 401);
            Assert.IsTrue(progression.ToggleLock(item.id).success);
            Assert.IsTrue(item.locked);
            string locked = progression.ToJson(false);
            Assert.IsFalse(progression.Dismantle(item.id).success);
            Assert.IsFalse(progression.Reroll(item.id, 402).success);
            Assert.AreEqual(locked, progression.ToJson(false));
            Assert.IsTrue(progression.ToggleLock(item.id).success);
            Assert.IsTrue(progression.Equip(ActorKind.Blade, item.id).success);
            string equipped = progression.ToJson(false);
            Assert.IsFalse(progression.Dismantle(item.id).success);
            Assert.AreEqual(equipped, progression.ToJson(false));
            Assert.IsTrue(progression.Unequip(ActorKind.Blade, GearSlot.Gloves).success);
            long resources = (long)progression.gold + progression.ore + progression.essence;
            Assert.IsTrue(progression.Dismantle(item.id).success);
            Assert.IsNull(progression.FindItem(item.id));
            Assert.Greater((long)progression.gold + progression.ore + progression.essence, resources);
            string removed = progression.ToJson(false);
            Assert.IsFalse(progression.Dismantle(item.id).success, "The same item cannot produce salvage twice.");
            Assert.AreEqual(removed, progression.ToJson(false));
        }

        [Test]
        public void QualityControlsUniqueAffixesAndSeededRerollsAreDeterministic()
        {
            var progression = FundedProgression();
            var duplicate = FundedProgression();
            foreach (GearQuality quality in Enum.GetValues(typeof(GearQuality)))
            {
                var first = Craft(progression, GearSlot.Boots, quality, GearSet.None, ActorKind.Blade, 500 + (int)quality);
                var second = Craft(duplicate, GearSlot.Boots, quality, GearSet.None, ActorKind.Blade, 500 + (int)quality);
                Assert.AreEqual((int)quality, first.affixes.Count);
                Assert.AreEqual(first.affixes.Count, first.affixes.Select(affix => affix.stat).Distinct().Count());
                Assert.IsTrue(first.affixes.All(affix => affix.value > 0));
                AssertAffixes(first, second);
            }
            var legendary = progression.inventory.Last(item => item.quality == GearQuality.Legendary);
            var matching = duplicate.inventory.Last(item => item.quality == GearQuality.Legendary);
            float[] originalBase = Stats(legendary.baseStats);
            var cost = GardenProgression.RerollCost(legendary);
            var before = Wallet(progression);
            Assert.IsTrue(progression.Reroll(legendary.id, 550).success);
            Assert.IsTrue(duplicate.Reroll(matching.id, 550).success);
            AssertWalletCost(progression, before, cost);
            Assert.AreEqual(4, legendary.affixes.Count);
            Assert.AreEqual(4, legendary.affixes.Select(affix => affix.stat).Distinct().Count());
            AssertStats(originalBase, Stats(legendary.baseStats), "Rerolling affixes must preserve the equipment base.");
            AssertAffixes(legendary, matching);
        }

        [Test]
        public void SetsApplyOnlyTheirTwoAndFourPieceThresholds()
        {
            var slots = new[] { GearSlot.Helmet, GearSlot.Chest, GearSlot.Legs, GearSlot.Gloves };
            foreach (GearSet set in new[] { GearSet.Gale, GearSet.Bastion, GearSet.Ember })
            {
                var progression = FundedProgression();
                for (int pieces = 1; pieces <= 4; pieces++)
                {
                    var item = Craft(progression, slots[pieces - 1], GearQuality.Rare, set, ActorKind.Blade, 600 + pieces);
                    Assert.IsTrue(progression.Equip(ActorKind.Blade, item.id).success);
                    Assert.AreEqual(pieces, progression.SetCount(ActorKind.Blade, set));
                    float[] equipment = new float[8];
                    foreach (var equipped in progression.GetHero(ActorKind.Blade).equipped)
                    {
                        float[] stats = Stats(progression.ItemStats(progression.FindItem(equipped.itemId)));
                        for (int i = 0; i < equipment.Length; i++) equipment[i] += stats[i];
                    }
                    float[] expected = new float[8];
                    if (pieces >= 2)
                    {
                        if (set == GearSet.Gale) expected[7] += .06f;
                        if (set == GearSet.Bastion) expected[1] += 12f;
                        if (set == GearSet.Ember) expected[0] += 8f;
                    }
                    if (pieces >= 4)
                    {
                        if (set == GearSet.Gale) { expected[5] += .10f; expected[3] += .04f; }
                        if (set == GearSet.Bastion) { expected[2] += 60f; expected[1] += 8f; }
                        if (set == GearSet.Ember) { expected[3] += .06f; expected[6] += .04f; }
                    }
                    float[] aggregate = Stats(progression.AggregateStats(ActorKind.Blade));
                    AssertStats(expected, aggregate.Select((value, index) => value - equipment[index]).ToArray(),
                        $"{set} with {pieces} pieces has incorrect set bonuses.");
                }
            }
        }

        [Test]
        public void ExperienceCarriesBetweenLevelsAndStopsAtLevelFifty()
        {
            var progression = FundedProgression();
            var hero = progression.GetHero(ActorKind.Blade);
            float[] initial = Stats(progression.AggregateStats(ActorKind.Blade));
            int experience = GardenProgression.ExperienceRequired(1) + GardenProgression.ExperienceRequired(2) + 40;
            Assert.IsTrue(progression.AddExperience(ActorKind.Blade, experience).success);
            Assert.AreEqual(3, hero.level);
            Assert.AreEqual(40, hero.experience);
            float[] current = Stats(progression.AggregateStats(ActorKind.Blade));
            Assert.AreEqual(initial[0] + 4f, current[0], .0001f);
            Assert.AreEqual(initial[2] + 16f, current[2], .0001f);
            string before = progression.ToJson(false);
            Assert.IsFalse(progression.AddExperience(ActorKind.Blade, -1).success);
            Assert.AreEqual(before, progression.ToJson(false));
            int toMaximum = 1000;
            for (int level = hero.level; level < GardenProgression.MaxLevel; level++)
                toMaximum += GardenProgression.ExperienceRequired(level);
            Assert.IsTrue(progression.AddExperience(ActorKind.Blade, toMaximum).success);
            Assert.AreEqual(50, hero.level);
            Assert.AreEqual(0, hero.experience);
            progression.AddExperience(ActorKind.Blade, 500);
            Assert.AreEqual(50, hero.level);
            Assert.AreEqual(0, hero.experience);
        }

        [Test]
        public void SavedEquipmentRoundTripsAndMalformedOrFutureJsonIsRejected()
        {
            var progression = FundedProgression();
            Assert.IsTrue(progression.AddExperience(ActorKind.Blade, 200).success);
            var item = Craft(progression, GearSlot.Amulet, GearQuality.Epic, GearSet.Ember, ActorKind.Blade, 701);
            Assert.IsTrue(progression.Enhance(item.id).success);
            Assert.IsTrue(progression.Equip(ActorKind.Blade, item.id).success);
            Assert.IsTrue(progression.ToggleLock(item.id).success);
            string json = progression.ToJson(false);
            Assert.IsTrue(GardenProgression.TryFromJson(json, out var restored, out string message), message);
            Assert.AreEqual(json, restored.ToJson(false));
            AssertStats(Stats(progression.AggregateStats(ActorKind.Blade)), Stats(restored.AggregateStats(ActorKind.Blade)),
                "Restoring a save must retain the same derived attributes.");
            foreach (string invalid in new[] { "", "null", "{broken json" })
            {
                Assert.IsFalse(GardenProgression.TryFromJson(invalid, out var rejected, out var error));
                Assert.IsNull(rejected);
                Assert.IsNotEmpty(error);
            }
            string future = Regex.Replace(json, "\\\"version\\\"\\s*:\\s*" + GardenProgression.CurrentVersion,
                "\"version\":" + (GardenProgression.CurrentVersion + 1));
            Assert.AreNotEqual(json, future, "The future-version fixture must modify the actual serialized schema version.");
            Assert.IsFalse(GardenProgression.TryFromJson(future, out var newer, out var unsupported));
            Assert.IsNull(newer);
            Assert.IsNotEmpty(unsupported);
        }

        [Test]
        public void LoadingRepairsInvalidValuesAndDuplicateEquipmentOwnership()
        {
            var progression = FundedProgression();
            var item = Craft(progression, GearSlot.Ring, GearQuality.Legendary, GearSet.Gale, ActorKind.Blade, 801);
            Assert.IsTrue(progression.Equip(ActorKind.Blade, item.id).success);
            progression.GetHero(ActorKind.Hammer).equipped.Add(new GearEquipSlot { slot = GearSlot.Ring, itemId = item.id });
            progression.gold = -10;
            progression.GetHero(ActorKind.Blade).level = 100;
            item.enhancement = 999;
            item.baseStats.attack = 10000000f;
            item.affixes.Add(new GearAffix { stat = item.affixes[0].stat, value = item.affixes[0].value });
            var duplicate = JsonUtility.FromJson<GearItem>(JsonUtility.ToJson(item));
            progression.inventory.Add(duplicate);
            var invalid = JsonUtility.FromJson<GearItem>(JsonUtility.ToJson(item));
            invalid.id = "invalid-slot-item";
            invalid.slot = (GearSlot)999;
            progression.inventory.Add(invalid);
            Assert.IsTrue(GardenProgression.TryFromJson(progression.ToJson(false), out var repaired, out string message), message);
            Assert.AreEqual(0, repaired.gold);
            Assert.AreEqual(50, repaired.GetHero(ActorKind.Blade).level);
            Assert.IsNull(repaired.FindItem("invalid-slot-item"));
            Assert.AreEqual(repaired.inventory.Count, repaired.inventory.Select(gear => gear.id).Distinct().Count());
            Assert.IsTrue(repaired.inventory.All(gear => gear.enhancement >= 0 && gear.enhancement <= 10));
            Assert.Less(repaired.FindItem(item.id).baseStats.attack, 10000000f);
            Assert.IsTrue(repaired.inventory.All(gear => gear.affixes.Select(affix => affix.stat).Distinct().Count() == gear.affixes.Count));
            Assert.IsTrue(repaired.inventory.All(gear => gear.affixes.Count <= (int)gear.quality));
            var equippedIds = repaired.heroes.SelectMany(hero => hero.equipped).Select(slot => slot.itemId).ToArray();
            Assert.AreEqual(equippedIds.Length, equippedIds.Distinct().Count(), "Repair must remove duplicated ownership across heroes.");
        }

        static GardenProgression FundedProgression()
        {
            var progression = GardenProgression.CreateDefault();
            progression.gold = progression.ore = progression.essence = 1000000;
            return progression;
        }

        static GearItem Craft(GardenProgression progression, GearSlot slot, GearQuality quality, GearSet set, ActorKind hero, int seed)
        {
            var result = progression.Craft(slot, quality, set, hero, seed);
            Assert.IsTrue(result.success, result.message);
            Assert.IsNotNull(result.item);
            return result.item;
        }

        static int[] Wallet(GardenProgression progression) => new[] { progression.gold, progression.ore, progression.essence };

        static void AssertWalletCost(GardenProgression progression, int[] before, GearCost cost)
        {
            Assert.AreEqual(before[0] - cost.gold, progression.gold);
            Assert.AreEqual(before[1] - cost.ore, progression.ore);
            Assert.AreEqual(before[2] - cost.essence, progression.essence);
        }

        static float[] Stats(GearStats stats) => new[]
        {
            stats.attack, stats.defense, stats.health, stats.critChance,
            stats.critDamage, stats.cooldownReduction, stats.lifeSteal, stats.moveSpeed
        };

        static void AssertStats(float[] expected, float[] actual, string message)
        {
            Assert.AreEqual(expected.Length, actual.Length, message);
            for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], actual[i], .0001f, message + $" Stat {i}");
        }

        static void AssertAffixes(GearItem expected, GearItem actual)
        {
            Assert.AreEqual(expected.affixes.Count, actual.affixes.Count);
            for (int i = 0; i < expected.affixes.Count; i++)
            {
                Assert.AreEqual(expected.affixes[i].stat, actual.affixes[i].stat);
                Assert.AreEqual(expected.affixes[i].value, actual.affixes[i].value, .0001f);
            }
        }

        [Serializable]
        sealed class AffixSnapshot { public List<GearAffix> affixes; }
    }

    public class GardenProgressionIntegrationTests
    {
        GardenGame game;
        bool previousPersistentSaves;
        string previousPersistenceDirectory, temporarySaveDirectory;
        Keyboard keyboard;
        InputSettings originalInputSettings, isolatedInputSettings;
        bool originalRunInBackground;

        [UnitySetUp]
        public IEnumerator LoadAnIsolatedProgressionScene()
        {
            // Disable persistence before Awake and before unloading any previous test scene.
            previousPersistentSaves = GardenGame.PersistentSavesEnabled;
            previousPersistenceDirectory = GardenGame.PersistenceDirectoryOverride;
            temporarySaveDirectory = Path.Combine(Path.GetTempPath(), "ToyGarden-ProgressionTest-" + Guid.NewGuid().ToString("N"));
            GardenGame.PersistentSavesEnabled = false;
            GardenGame.PersistenceDirectoryOverride = temporarySaveDirectory;
            yield return SceneManager.LoadSceneAsync("ToyGarden", LoadSceneMode.Single);
            yield return null;
            game = UnityEngine.Object.FindAnyObjectByType<GardenGame>();
            Assert.IsNotNull(game);
            game.AcceptPlayerInput = false;
            game.RestartGame();
            Assert.IsNotNull(game.Progression);
            Assert.IsFalse(GardenGame.PersistentSavesEnabled);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator ReleaseTheGameBeforeRestoringPersistence()
        {
            if (game != null) game.AcceptPlayerInput = false;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            keyboard = null;
            if (isolatedInputSettings != null)
            {
                InputSystem.settings = originalInputSettings;
                Application.runInBackground = originalRunInBackground;
                UnityEngine.Object.DestroyImmediate(isolatedInputSettings);
                isolatedInputSettings = originalInputSettings = null;
            }
            // Destroy the test instance before removing its temporary save directory and restoring globals.
            if (game != null) UnityEngine.Object.Destroy(game.gameObject);
            yield return null;
            try
            {
                if (Directory.Exists(temporarySaveDirectory)) Directory.Delete(temporarySaveDirectory, true);
            }
            finally
            {
                GardenGame.PersistentSavesEnabled = previousPersistentSaves;
                GardenGame.PersistenceDirectoryOverride = previousPersistenceDirectory;
            }
        }

        [UnityTest]
        public IEnumerator EquippingGearChangesRealMeleeDamageDefenseAndHealth()
        {
            game.enabled = false;
            OpenTheOrchardRoute();
            game.ActivateEncounter(1);
            var hero = game.ActiveHero;
            foreach (var actor in game.Enemies.Concat(game.Heroes.Where(actor => actor != hero))) actor.enabled = false;
            var enemy = game.Enemies.First(actor => actor.kind == ActorKind.Grunt && actor.encounterId == 1);
            float originalAttack = hero.CombatAttack;
            float originalDefense = hero.CombatDefense;
            float originalHealth = hero.MaxHealth;
            var weaponResult = game.Progression.Craft(GearSlot.Weapon, GearQuality.Common, GearSet.None, hero.kind, 901);
            var armorResult = game.Progression.Craft(GearSlot.Chest, GearQuality.Common, GearSet.None, hero.kind, 902);
            Assert.IsTrue(weaponResult.success, weaponResult.message);
            Assert.IsTrue(armorResult.success, armorResult.message);
            Assert.IsTrue(game.Progression.Equip(hero.kind, weaponResult.item.id).success);
            Assert.IsTrue(game.Progression.Equip(hero.kind, armorResult.item.id).success);
            game.RefreshHeroStats();
            Assert.AreEqual(originalAttack + game.Progression.ItemStats(weaponResult.item).attack, hero.CombatAttack, .0001f);
            Assert.AreEqual(originalDefense + game.Progression.ItemStats(armorResult.item).defense, hero.CombatDefense, .0001f);
            Assert.AreEqual(originalHealth + game.Progression.ItemStats(armorResult.item).health, hero.MaxHealth, .0001f);
            Assert.AreEqual(0f, hero.CritChance, "Common equipment provides a deterministic damage sample without random affixes.");
            Assert.Greater(hero.CombatAttack, originalAttack);

            hero.Teleport(enemy.transform.position - Vector3.forward * 1.5f);
            hero.transform.forward = Vector3.forward;
            float enemyHealth = enemy.Health;
            Assert.IsTrue(hero.TryAttack());
            yield return new WaitForSeconds(.2f);
            Assert.AreEqual(enemyHealth - hero.CombatAttack, enemy.Health, .001f,
                "The ordinary melee impact must consume the equipped weapon's real attack value.");
            float beforeDamage = hero.Health;
            hero.TakeDamage(20f, enemy.transform.position);
            Assert.AreEqual(beforeDamage - 20f * 100f / (100f + hero.CombatDefense), hero.Health, .001f,
                "Equipped defense must reduce actual incoming damage.");
            float remainingFraction = hero.Health / hero.MaxHealth;
            Assert.IsTrue(game.Progression.Unequip(hero.kind, GearSlot.Chest).success);
            game.RefreshHeroStats();
            Assert.AreEqual(originalHealth, hero.MaxHealth, .001f);
            Assert.AreEqual(remainingFraction, hero.Health / hero.MaxHealth, .0001f,
                "Changing maximum health must preserve the living hero's health fraction.");
            AssertPersistenceIsSuppressed();
        }

        [UnityTest]
        public IEnumerator RealWorldRewardsAreGrantedOnceForEachCompletedSource()
        {
            game.enabled = false;
            foreach (var actor in game.Heroes.Concat(game.Enemies)) actor.enabled = false;
            string untouched = game.Progression.ToJson(false);
            Assert.IsFalse(game.RewardEncounter(1));
            Assert.IsFalse(game.GrantPuzzleReward(0));
            Assert.IsFalse(game.GrantChestReward(0));
            Assert.AreEqual(untouched, game.Progression.ToJson(false), "Uncompleted world sources cannot grant rewards.");

            OpenTheOrchardRoute();
            string afterPuzzle = game.Progression.ToJson(false);
            Assert.AreNotEqual(untouched, afterPuzzle, "Solving the lever must grant its progression reward.");
            Assert.IsFalse(game.GrantPuzzleReward(0));
            Assert.AreEqual(afterPuzzle, game.Progression.ToJson(false));

            game.ActivateEncounter(1);
            foreach (var enemy in game.Enemies.Where(actor => actor.encounterId == 1)) enemy.TakeDamage(10000f, Vector3.zero);
            Assert.IsTrue(game.IsEncounterCleared(1));
            string afterBattle = game.Progression.ToJson(false);
            Assert.AreNotEqual(afterPuzzle, afterBattle, "Clearing an actual encounter must grant its reward.");
            Assert.IsFalse(game.RewardEncounter(1));
            Assert.AreEqual(afterBattle, game.Progression.ToJson(false));

            var chest = game.Chapter.Mechanisms.First(mechanism => mechanism.kind == MechanismKind.Chest);
            game.ActiveHero.Teleport(chest.transform.position + Vector3.back * 1.5f);
            Assert.IsTrue(game.TryInteract());
            Assert.IsTrue(chest.Used);
            string afterChest = game.Progression.ToJson(false);
            Assert.AreNotEqual(afterBattle, afterChest, "Opening an actual supply chest must grant its reward.");
            Assert.IsFalse(game.GrantChestReward(chest.index));
            Assert.IsFalse(game.TryInteract());
            Assert.AreEqual(afterChest, game.Progression.ToJson(false));
            AssertPersistenceIsSuppressed();
            yield return null;
        }

        [UnityTest]
        public IEnumerator EquipmentKeyboardInputSuspendsGameplayAndEscapeResumesIt()
        {
            originalInputSettings = InputSystem.settings;
            originalRunInBackground = Application.runInBackground;
            isolatedInputSettings = UnityEngine.Object.Instantiate(originalInputSettings);
            isolatedInputSettings.hideFlags = HideFlags.HideAndDontSave;
            isolatedInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            isolatedInputSettings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = isolatedInputSettings;
            Application.runInBackground = true;
            yield return null;
            game.RestartGame();
            keyboard = InputSystem.AddDevice<Keyboard>("Progression integration keyboard");
            InputSystem.EnableDevice(keyboard);
            game.AcceptPlayerInput = true;
            yield return TapKey(Key.I);
            Assert.IsTrue(game.EquipmentOpen, "I must open the equipment interface through Input System.");
            Assert.IsFalse(game.IsPlaying);
            var selected = game.ActiveHero;
            var positions = game.Heroes.Concat(game.Enemies).ToDictionary(actor => actor, actor => actor.transform.position);
            float elapsed = game.Elapsed;
            float cooldown = selected.SkillCooldownRemaining;
            float dodgeCooldown = selected.DodgeCooldownRemaining;
            string progression = game.Progression.ToJson(false);
            var enemy = game.Enemies[0];
            float enemyHealth = enemy.Health;
            QueueKeys(Key.W, Key.J, Key.Q, Key.E, Key.Tab, Key.Space);
            yield return new WaitForSecondsRealtime(.25f);
            Assert.AreSame(selected, game.ActiveHero, "Equipment navigation must not also switch the controlled party member.");
            foreach (var sample in positions)
                Assert.Less(Vector3.Distance(sample.Key.transform.position, sample.Value), .001f,
                    $"{sample.Key.kind} moved while equipment was open.");
            Assert.AreEqual(elapsed, game.Elapsed, .001f);
            Assert.AreEqual(cooldown, selected.SkillCooldownRemaining, .001f);
            Assert.AreEqual(dodgeCooldown, selected.DodgeCooldownRemaining, .001f);
            selected.DealDamageTo(enemy, 50f);
            Assert.AreEqual(enemyHealth, enemy.Health, "Combat must not apply damage behind the equipment interface.");
            Assert.AreEqual(progression, game.Progression.ToJson(false), "Blocked gameplay keys must not change equipment or rewards.");
            QueueKeys();
            yield return null;
            yield return TapKey(Key.Escape);
            Assert.IsFalse(game.EquipmentOpen);
            Assert.IsFalse(game.Paused);
            Assert.IsTrue(game.IsPlaying);
            Vector3 start = selected.transform.position;
            QueueKeys(Key.W);
            float deadline = Time.realtimeSinceStartup + 3f;
            while (GardenGame.FlatDistance(start, selected.transform.position) < .6f && Time.realtimeSinceStartup < deadline)
                yield return null;
            QueueKeys();
            Assert.Greater(GardenGame.FlatDistance(start, selected.transform.position), .6f,
                "Ordinary movement must resume after closing equipment.");
            AssertPersistenceIsSuppressed();
        }

        [UnityTest]
        public IEnumerator IsolatedSaveReloadPreservesEquipmentWorldStateAndRewardClaims()
        {
            // This test alone enables real IO, after an override points at a fresh GUID directory.
            UnityEngine.Object.Destroy(game.gameObject);
            yield return null;
            GardenGame.PersistentSavesEnabled = true;
            Assert.AreEqual(temporarySaveDirectory, GardenGame.PersistenceDirectoryOverride);
            yield return SceneManager.LoadSceneAsync("ToyGarden", LoadSceneMode.Single);
            game = UnityEngine.Object.FindAnyObjectByType<GardenGame>();
            Assert.IsNotNull(game);
            game.AcceptPlayerInput = false;
            game.enabled = false;
            foreach (var actor in game.Heroes.Concat(game.Enemies)) actor.enabled = false;
            if (game.Paused) game.TogglePause();
            Assert.AreEqual(temporarySaveDirectory, Path.GetDirectoryName(game.SavePath),
                "Real persistence may only target the temporary test directory.");

            OpenTheOrchardRoute();
            game.ActivateEncounter(1);
            foreach (var enemy in game.Enemies.Where(actor => actor.encounterId == 1)) enemy.TakeDamage(10000f, Vector3.zero);
            var chest = game.Chapter.Mechanisms.First(mechanism => mechanism.kind == MechanismKind.Chest);
            int chestIndex = chest.index;
            game.ActiveHero.Teleport(chest.transform.position + Vector3.back * 1.5f);
            Assert.IsTrue(game.TryInteract());
            var hero = game.ActiveHero;
            var result = game.Progression.Craft(GearSlot.Weapon, GearQuality.Common, GearSet.None, hero.kind, 1001);
            Assert.IsTrue(result.success, result.message);
            Assert.IsTrue(game.Progression.Equip(hero.kind, result.item.id).success);
            Assert.IsTrue(game.Progression.Enhance(result.item.id).success);
            Assert.IsTrue(game.Progression.ToggleLock(result.item.id).success);
            game.RefreshHeroStats();
            hero.TakeDamage(17f, hero.transform.position + Vector3.back);
            hero.Teleport(new Vector3(6f, .05f, 14f));
            Physics.SyncTransforms();
            Vector3 savedPosition = hero.transform.position;
            float savedHealth = hero.Health;
            float savedMaximumHealth = hero.MaxHealth;
            float savedAttack = hero.CombatAttack;
            string equippedId = result.item.id;
            string savedProgression = game.Progression.ToJson(false);
            string[] savedClaims = game.CaptureSave().claims.ToArray();
            Assert.AreEqual(3, savedClaims.Length, "The fixture must claim its lever, encounter and chest exactly once.");
            Assert.IsTrue(game.SaveProgress());
            Assert.IsTrue(File.Exists(game.SavePath));
            Assert.IsTrue(File.Exists(game.SavePath + ".bak"), "Repeated atomic saves should retain a previous snapshot.");
            var onDisk = JsonUtility.FromJson<GardenSaveData>(File.ReadAllText(game.SavePath));
            Assert.AreEqual(savedProgression, onDisk.progression);

            // Loading the ordinary scene exercises Awake, JSON validation and world restoration together.
            yield return SceneManager.LoadSceneAsync("ToyGarden", LoadSceneMode.Single);
            game = UnityEngine.Object.FindAnyObjectByType<GardenGame>();
            Assert.IsNotNull(game);
            game.AcceptPlayerInput = false;
            game.enabled = false;
            foreach (var actor in game.Heroes.Concat(game.Enemies)) actor.enabled = false;
            if (game.Paused) game.TogglePause();
            Assert.AreEqual(temporarySaveDirectory, Path.GetDirectoryName(game.SavePath));
            Assert.AreEqual(savedProgression, game.Progression.ToJson(false));
            Assert.AreEqual(equippedId, game.Progression.GetEquipped(ActorKind.Blade, GearSlot.Weapon).id);
            Assert.IsTrue(game.Progression.FindItem(equippedId).locked);
            Assert.AreEqual(1, game.Progression.FindItem(equippedId).enhancement);
            Assert.IsTrue(game.Chapter.IsPuzzleSolved(0));
            Assert.IsTrue(game.Chapter.IsGateOpen(0));
            Assert.IsTrue(game.IsEncounterCleared(1));
            Assert.IsTrue(game.Enemies.Where(actor => actor.encounterId == 1).All(actor => !actor.IsAlive));
            Assert.IsTrue(game.Chapter.Mechanisms.Single(mechanism => mechanism.kind == MechanismKind.Chest && mechanism.index == chestIndex).Used);
            Assert.Less(Vector3.Distance(savedPosition, game.ActiveHero.transform.position), .2f);
            Assert.AreEqual(savedHealth, game.ActiveHero.Health, .001f);
            Assert.AreEqual(savedMaximumHealth, game.ActiveHero.MaxHealth, .001f);
            Assert.AreEqual(savedAttack, game.ActiveHero.CombatAttack, .001f);
            CollectionAssert.AreEqual(savedClaims, game.CaptureSave().claims);
            Assert.IsFalse(game.GrantPuzzleReward(0));
            Assert.IsFalse(game.RewardEncounter(1));
            Assert.IsFalse(game.GrantChestReward(chestIndex));
            Assert.AreEqual(savedProgression, game.Progression.ToJson(false),
                "Reloading a save must not make collected world rewards available again.");
        }

        void AssertPersistenceIsSuppressed()
        {
            Assert.IsFalse(GardenGame.PersistentSavesEnabled);
            Assert.AreEqual(temporarySaveDirectory, Path.GetDirectoryName(game.SavePath));
            Assert.IsTrue(game.SaveProgress(), "Suppressed persistence is a successful no-op.");
            Assert.IsFalse(File.Exists(game.SavePath), "A non-persistence test must not create a save file.");
        }

        void OpenTheOrchardRoute()
        {
            var lever = game.Chapter.Mechanisms.Single(mechanism => mechanism.kind == MechanismKind.Lever && mechanism.puzzleId == 0);
            game.ActiveHero.Teleport(lever.transform.position + Vector3.back * 1.5f);
            Physics.SyncTransforms();
            Assert.IsTrue(game.TryInteract());
            Assert.IsTrue(game.Chapter.IsPuzzleSolved(0));
            Assert.IsTrue(game.Chapter.IsGateOpen(0));
        }

        void QueueKeys(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));

        IEnumerator TapKey(Key key)
        {
            QueueKeys(key);
            yield return new WaitForSecondsRealtime(.08f);
            QueueKeys();
            yield return null;
        }
    }

}

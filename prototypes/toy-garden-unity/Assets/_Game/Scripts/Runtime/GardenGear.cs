using System;
using System.Collections.Generic;

namespace ToyGarden
{
    public enum GearSlot { Weapon, Helmet, Chest, Legs, Gloves, Boots, Ring, Amulet }
    public enum GearQuality { Common, Uncommon, Rare, Epic, Legendary }
    public enum GearStat { Attack, Defense, Health, CritChance, CritDamage, CooldownReduction, LifeSteal, MoveSpeed }
    public enum GearSet { None, Gale, Bastion, Ember }
    public enum GearSort { Quality, Slot, Level, Set, Newest }

    [Serializable]
    public sealed class GearStats
    {
        public float attack, defense, health, critChance, critDamage, cooldownReduction, lifeSteal, moveSpeed;

        public GearStats Copy() => new()
        {
            attack = attack, defense = defense, health = health, critChance = critChance,
            critDamage = critDamage, cooldownReduction = cooldownReduction, lifeSteal = lifeSteal, moveSpeed = moveSpeed
        };

        public void Add(GearStats other, float multiplier = 1f)
        {
            if (other == null) return;
            attack += other.attack * multiplier;
            defense += other.defense * multiplier;
            health += other.health * multiplier;
            critChance += other.critChance * multiplier;
            critDamage += other.critDamage * multiplier;
            cooldownReduction += other.cooldownReduction * multiplier;
            lifeSteal += other.lifeSteal * multiplier;
            moveSpeed += other.moveSpeed * multiplier;
        }

        public void Add(GearStat stat, float value)
        {
            switch (stat)
            {
                case GearStat.Attack: attack += value; break;
                case GearStat.Defense: defense += value; break;
                case GearStat.Health: health += value; break;
                case GearStat.CritChance: critChance += value; break;
                case GearStat.CritDamage: critDamage += value; break;
                case GearStat.CooldownReduction: cooldownReduction += value; break;
                case GearStat.LifeSteal: lifeSteal += value; break;
                case GearStat.MoveSpeed: moveSpeed += value; break;
            }
        }

        public float Get(GearStat stat) => stat switch
        {
            GearStat.Attack => attack, GearStat.Defense => defense, GearStat.Health => health,
            GearStat.CritChance => critChance, GearStat.CritDamage => critDamage,
            GearStat.CooldownReduction => cooldownReduction, GearStat.LifeSteal => lifeSteal,
            GearStat.MoveSpeed => moveSpeed, _ => 0f
        };
    }

    [Serializable]
    public sealed class GearAffix
    {
        public GearStat stat;
        public float value;
    }

    [Serializable]
    public sealed class GearItem
    {
        public string id, name;
        public GearSlot slot;
        public GearQuality quality;
        public GearSet set;
        public int itemLevel = 1;
        public int enhancement;
        public ActorKind weaponClass;
        public bool locked, isTraining;
        public int createdOrder;
        public GearStats baseStats = new();
        public List<GearAffix> affixes = new();
    }

    [Serializable]
    public sealed class GearEquipSlot
    {
        public GearSlot slot;
        public string itemId;
    }

    [Serializable]
    public sealed class HeroProgress
    {
        public ActorKind hero;
        public int level = 1;
        public int experience;
        public List<GearEquipSlot> equipped = new();
    }

    [Serializable]
    public struct GearCost
    {
        public int gold, ore, essence;
        public GearCost(int gold, int ore, int essence) { this.gold = gold; this.ore = ore; this.essence = essence; }
        public override string ToString() => $"金币 {gold} / 矿石 {ore} / 精粹 {essence}";
    }

    public sealed class GearResult
    {
        public bool success;
        public string message;
        public GearItem item;
        public static GearResult Ok(string text, GearItem item = null) => new() { success = true, message = text, item = item };
        public static GearResult Fail(string text) => new() { success = false, message = text };
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Decentralized.Domain
{
    /// <summary>显卡或矿机的型号。算力内部单位 H（1 H = 10 MH/s），耗电单位 kJ/天。</summary>
    public sealed class HardwareSpec
    {
        public string Model;
        public string Brand;
        public double Hash;
        public double Kj;
        public int Price;
        /// <summary>暴毙概率的品牌系数。</summary>
        /// <summary>档次：0 低端、1 中端、2 高端、3 旗舰（显卡）。实物特征（供电口、风扇数）只看档次。</summary>
        public int Tier;
        /// <summary>废片：只会作为假卡背后的真实芯片出现，不上架、不单独出现。低档次的卡也能被冒充靠它。</summary>
        public bool Scrap;
        public double DeathFactor;
        /// <summary>每天损耗的系数。</summary>
        public double WearFactor;
        /// <summary>矿机（ASIC）：独立机器；false 为插在矿架上的显卡。</summary>
        public bool Asic;

        /// <summary>显示用算力：显卡 MH/s，矿机 GH/s。</summary>
        public double DisplayHash => Asic ? Hash * 10 / 1000 : Hash * 10;
    }

    public static class Hardware
    {
        /// <summary>各档次的实物特征：供电口针数（旗舰是两个 8 针）与风扇数量。</summary>
        public static readonly string[] PowerPins = { "6", "8", "8", "8+8" };
        public static readonly int[] FanCount = { 1, 2, 3, 3 };
        /// <summary>各档次的供电口：0 = 6 针，1 = 8 针，2 = 双 8 针。</summary>
        public static readonly int[] PinClass = { 0, 1, 1, 2 };

        public const string Probidia = "Probidia", Pmd = "PMD", RatDigg = "RatDigg";

        public static readonly IReadOnlyDictionary<string, HardwareSpec> Specs = new[]
        {
            // 四个档次（低端、中端、高端、旗舰），两个牌子通用：型号只是显示名，性能和价格由「牌子 + 档次」决定
            Gpu("RTX60", Probidia, 0, 1.8, 4, 220), Gpu("RTX70", Probidia, 1, 5.0, 12, 800),
            Gpu("RTX80", Probidia, 2, 8.0, 18, 1300), Gpu("RTX90", Probidia, 3, 20.0, 30, 5000),
            Gpu("RX70", Pmd, 0, 1.4, 2.5, 170), Gpu("RX80", Pmd, 1, 4.0, 8, 680),
            Gpu("RX800", Pmd, 2, 8.0, 14, 1500), Gpu("RX900", Pmd, 3, 15.0, 20, 4000),
            Scrap("RX10", Pmd, 0, 0.3, 2, 60),
            Asic("M1", 50, 60, 11000), Asic("M2", 120, 110, 27000), Asic("M3", 300, 220, 72000),
        }.ToDictionary(s => s.Model);

        private static HardwareSpec Gpu(string model, string brand, int tier, double h, double kj, int price) => new HardwareSpec
        {
            Model = model, Brand = brand, Tier = tier, Hash = h, Kj = kj, Price = price,
            DeathFactor = brand == Probidia ? 0.7 : 1.0, WearFactor = brand == Probidia ? 1.0 : 1.2,
        };

        private static HardwareSpec Scrap(string model, string brand, int tier, double h, double kj, int price)
        {
            var s = Gpu(model, brand, tier, h, kj, price);
            s.Scrap = true;
            return s;
        }

        private static HardwareSpec Asic(string model, double h, double kj, int price) => new HardwareSpec
        {
            Model = model, Brand = RatDigg, Hash = h, Kj = kj, Price = price, DeathFactor = 0.5, WearFactor = 0.8, Asic = true,
        };
    }

    /// <summary>
    /// 矿架上游戏模块的效果。机器汇总的三项百分比（性能、效率、品质）各除以 <see cref="Tuning.ModuleRatio"/> 后生效，不设上限（强力模块是玩家自己拿到的）。
    /// 性能加成同时按比例提高损耗与暴毙率。三个乘数各有防出错的下限：算力不低于 <see cref="Tuning.ModuleHashFloor"/>，耗电不低于 <see cref="Tuning.ModuleKjFloor"/>，损耗不低于 <see cref="Tuning.ModuleWearFloor"/>。
    /// </summary>
    public struct ModuleBonus
    {
        public double Hash;   // 算力乘数
        public double Kj;     // 耗电乘数
        public double Wear;   // 每日损耗乘数
        public double Death;  // 暴毙率乘数

        public static ModuleBonus None => new ModuleBonus { Hash = 1, Kj = 1, Wear = 1, Death = 1 };

        public static ModuleBonus From(int performance, int efficiency, int quality)
        {
            double r = Tuning.ModuleRatio * 100.0;
            double hash = performance / r, save = efficiency / r, wearCut = quality / r;
            double heat = 1 + Math.Max(0, hash);
            return new ModuleBonus
            {
                Hash = Math.Max(Tuning.ModuleHashFloor, 1 + hash),
                Kj = Math.Max(Tuning.ModuleKjFloor, 1 - save),
                Wear = Math.Max(Tuning.ModuleWearFloor, (1 - wearCut) * heat),
                Death = heat,
            };
        }

    }

    /// <summary>一张显卡或一台矿机的实例状态（存进物品标签）。</summary>
    public sealed class Unit
    {
        public string Model;
        /// <summary>假卡：标着 Model，芯片实为 FakeOf。</summary>
        public string FakeOf;
        public double Health = 100;
        public bool Dead;
        public bool Refurbished;
        /// <summary>外观；新卡（正规渠道）没有记录，按全新画。</summary>
        public CardLook Look;
        /// <summary>按当前健康度算出的外观。</summary>
        public CardLook Appearance => Look.Appearance(Health);

        public HardwareSpec Label => Hardware.Specs[Model];
        public HardwareSpec Chip => Hardware.Specs[FakeOf ?? Model];

        /// <summary>实物上的风扇数：对得上标称型号时是标称档次的，对不上（假卡的破绽）时是真实芯片的。</summary>
        public int ObservedCount => Hardware.FanCount[(Look.CountBad != 0 ? Chip : Label).Tier];
        /// <summary>实物上的供电口：0 = 6 针，1 = 8 针，2 = 双 8 针；规则同风扇数。</summary>
        public int ObservedPin => Hardware.PinClass[(Look.PinBad != 0 ? Chip : Label).Tier];

        /// <summary>当前算力：健康 ≥ 50 时满算力，低于 50 线性降到健康 0 时的 60%。</summary>
        public double Hash(ModuleBonus b)
        {
            if (Dead) return 0;
            return Chip.Hash * (Health >= 50 ? 1.0 : 0.6 + 0.4 * Health / 50) * b.Hash * Appearance.HashFactor;
        }

        public double Kj(ModuleBonus b) => Dead ? 0 : Chip.Kj * b.Kj;

        /// <summary>今晚的暴毙概率。</summary>
        public double DeathChance(ModuleBonus b)
        {
            double p = Tuning.DeathBase + Tuning.DeathWorn * Math.Pow(1 - Health / 100, 2);
            return p * Label.DeathFactor * (FakeOf != null ? Tuning.FakeDeathFactor : 1) * b.Death * Appearance.DeathFactor;
        }

        /// <summary>一夜的损耗与暴毙判定；返回是否在今晚暴毙。</summary>
        public bool Night(ModuleBonus b, Rng rng)
        {
            if (Dead) return false;
            Health = Math.Max(0, Health - Label.WearFactor * b.Wear * Appearance.WearFactor);
            if (rng.Chance(DeathChance(b)))
            {
                Dead = true;
                return true;
            }
            return false;
        }

        /// <summary>翻新：每张卡一次，健康 +30。</summary>
        public bool Refurbish()
        {
            if (Dead || Refurbished) return false;
            Refurbished = true;
            Health = Math.Min(100, Health + Tuning.RefurbishHealth);
            return true;
        }
    }

    /// <summary>全网：NPC 背景算力、出块奖励与减半。</summary>
    public sealed class Network
    {
        public double NpcHash = Tuning.NpcHash0;
        /// <summary>NPC 矿工每 H 的耗电（kJ）：矿机时代到来后减半，他们能挖得更久，全网算力均衡点升高。</summary>
        public double NpcKj = Tuning.NpcKjPerH;
        public int EraDay = -1, EraWarnDay = -1;
        public bool EraWarned, EraDone;
        public double Reward = Tuning.BlockReward;
        /// <summary>停电、动荡等事件对 NPC 算力的可用比例。</summary>
        public double Availability = 1.0;

        /// <summary>按算力分得今晚的出块（已扣矿池抽成）。</summary>
        public double Mine(double playerHash)
        {
            if (playerHash <= 0) return 0;
            double total = NpcHash * Availability + playerHash;
            return Reward * playerHash / total * (1 - Tuning.PoolFee) * Scales.Mining;
        }

        public enum EraStep { None, Warn, Start }

        /// <summary>
        /// "矿机时代"：第一次调用时在 EraMin–EraMax 天之间随机定一天（至少留出预兆的天数），提前 4–7 天预兆，到点 NPC 耗电效率翻倍，永久。
        /// 返回今晚发生的一步（预兆 / 开始）。
        /// </summary>
        public EraStep EraNight(int day, Rng rng)
        {
            if (EraDone) return EraStep.None;
            if (EraDay < 0)
            {
                EraDay = Math.Max(day + Tuning.EraWarnMax + 1, rng.Range(Tuning.EraMin, Tuning.EraMax));
                EraWarnDay = EraDay - rng.Range(Tuning.EraWarnMin, Tuning.EraWarnMax);
            }
            if (day >= EraDay) { NpcKj *= Tuning.EraKjMult; EraDone = true; return EraStep.Start; }
            if (!EraWarned && day >= EraWarnDay) { EraWarned = true; return EraStep.Warn; }
            return EraStep.None;
        }

        /// <summary>
        /// 夜间：第 HalvingDays 天的倍数时减半；NPC 矿工按"每 H 收益 / 电费"与目标比的偏离扩张或关机，每晚最多 ±5%。
        /// </summary>
        public void Night(int day, double price, double playerHash)
        {
            if (day > 0 && day % Tuning.HalvingDays == 0) Reward /= 2;
            double total = NpcHash * Availability + playerHash;
            double perH = Reward * price / total;
            double ratio = perH / (NpcKj * Tuning.ElecPrice);
            double adj = Math.Max(-Tuning.NpcAdj, Math.Min(Tuning.NpcAdj, Tuning.NpcAdj * (ratio / Tuning.NpcTargetRatio - 1)));
            NpcHash *= 1 + adj;
        }
    }
}

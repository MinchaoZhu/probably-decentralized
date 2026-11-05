using System.Collections.Generic;

namespace Decentralized.Domain
{
    /// <summary>
    /// 正规渠道（杰克逊博士）每次进货时上架的挖矿设备：全新、真货。返回物品种类：矿架用 <see cref="Rig"/>，显卡用型号。
    /// </summary>
    public static class Dealer
    {
        public const string Rig = "RIG";

        public static readonly string[] Basic = { "RTX60", "RX70" };
        public static readonly string[] Advanced = { "RTX70", "RX80", "RTX80", "RX800" };
        public static readonly string[] Flagship = { "RTX90", "RX900" };

        public const int AdvancedFromDay = Tuning.DealerAdvancedFromDay, FlagshipFromDay = Tuning.DealerFlagshipFromDay, SpecialFlagshipFromDay = Tuning.DealerSpecialFlagshipFromDay;
        public const double RigRestockChance = Tuning.DealerRigRestock, AdvancedChance = Tuning.DealerAdvanced, AdvancedSecondChance = Tuning.DealerAdvancedSecond, FlagshipChance = Tuning.DealerFlagship;

        public static List<string> Stock(int day, bool ownsRig, bool specialStock, Rng rng)
        {
            var list = new List<string>();
            if (!ownsRig || rng.Chance(RigRestockChance)) list.Add(Rig);
            for (int i = 0; i < 2; i++) list.Add(Pick(Basic, rng));
            if (day >= AdvancedFromDay && rng.Chance(AdvancedChance))
            {
                list.Add(Pick(Advanced, rng));
                if (rng.Chance(AdvancedSecondChance)) list.Add(Pick(Advanced, rng));
            }
            if (specialStock && day >= SpecialFlagshipFromDay) list.Add(Pick(Flagship, rng));
            else if (day >= FlagshipFromDay && rng.Chance(FlagshipChance)) list.Add(Pick(Flagship, rng));
            return list;
        }

        private static string Pick(string[] from, Rng rng) => from[rng.Range(0, from.Length - 1)];
    }
}

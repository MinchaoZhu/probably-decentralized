using System;

namespace Decentralized.Domain
{
    /// <summary>mod 设置里的倍率（全局，不随存档）。接入层读设置后写进来；领域层在对应概率和产出处乘上。</summary>
    public static class Scales
    {
        /// <summary>风险倍率：骗局、跑路、钱包投毒、地址劫持、钓鱼的发生概率。</summary>
        public static double Risk = 1;
        /// <summary>挖矿产出倍率：玩家矿机拿到的区块奖励。</summary>
        public static double Mining = 1;

        /// <summary>概率乘风险倍率，封顶 1。</summary>
        public static double R(double chance) => Math.Min(1, chance * Risk);

        /// <summary>设置里的倍率档位（“×1.5”）。</summary>
        public static readonly string[] Choices = { "×0.5", "×1", "×1.5", "×2" };
        public const string Default = "×1";

        public static double Parse(string s)
        {
            if (s != null && s.StartsWith("×") && double.TryParse(s.Substring(1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0) return v;
            return 1;
        }
    }
}

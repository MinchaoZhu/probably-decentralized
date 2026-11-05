using System.Collections.Generic;

namespace Decentralized.Domain
{
    /// <summary>钱包里的一条转账记录：对方地址、币种、数量、哪天、转入还是转出。地址投毒的 0 币转入也记在这里。</summary>
    public sealed class TxRecord
    {
        public string Addr, Asset;
        public double Amount;
        public int Day;
        public bool In;
    }

    /// <summary>手续费档位：慢最便宜，网络拥堵时可能被挤出内存池；快最贵，拥堵时也照常确认。</summary>
    public enum FeeTier { Slow, Normal, Fast }

    /// <summary>链上转账的规则：手续费、需要的确认数、拥堵时慢档被丢弃的概率。</summary>
    public static class TxRules
    {
        /// <summary>手续费（信用点等值，按转出的币扣）：档位基础价 × 拥堵倍数。</summary>
        public static double FeeCredits(FeeTier t, bool congested, double gas = 1) =>
            (t == FeeTier.Slow ? Tuning.TxFeeSlow : t == FeeTier.Normal ? Tuning.TxFeeNormal : Tuning.TxFeeFast) * gas * (congested ? Tuning.TxCongestedFeeMult : 1);

        /// <summary>Gas 周期部分：phase 是这一局的起始相位（0..周期-1）。</summary>
        public static double GasCycle(int day, int phase) => 1 + Tuning.TxGasAmp * System.Math.Sin(2 * System.Math.PI * (day + phase) / Tuning.TxGasPeriod);

        /// <summary>某天的 gas 指数：周期 × (1 ± 随机波动)。</summary>
        public static double Gas(int day, int phase, Rng rng) => GasCycle(day, phase) * (1 + Tuning.TxGasNoise * (rng.NextDouble() * 2 - 1));

        /// <summary>需要的确认数：金额越大越多。</summary>
        public static int Confirmations(double valueCredits) =>
            valueCredits >= Tuning.TxConfirmBigValue ? Tuning.TxConfirmBig : valueCredits >= Tuning.TxConfirmMidValue ? Tuning.TxConfirmMid : Tuning.TxConfirmBase;

        /// <summary>Gas 档位本身的速度倍数：越高越快。</summary>
        public static double TierSpeed(FeeTier t) => t == FeeTier.Fast ? Tuning.TxSpeedFast : t == FeeTier.Normal ? Tuning.TxSpeedNormal : Tuning.TxSpeedSlow;

        /// <summary>整条确认时间线的时间倍数：档位速度 × 拥堵时慢档、普通档变慢（快档不受拥堵影响）。</summary>
        public static double Slowdown(FeeTier t, bool congested) => TierSpeed(t) * (!congested ? 1 : t == FeeTier.Fast ? 1 : t == FeeTier.Normal ? Tuning.TxCongestedNormalSlow : Tuning.TxCongestedSlowSlow);

        /// <summary>拥堵时慢档交易被挤出内存池的概率（交易作废，手续费照扣）。</summary>
        public static double DropChance(FeeTier t, bool congested) => congested && t == FeeTier.Slow ? Tuning.TxSlowDrop : 0;

        /// <summary>夜间区块高度的自然增长。</summary>
        public static int BlocksPerDay(Rng rng) => rng.Range(Tuning.BlocksPerDayMin, Tuning.BlocksPerDayMax);

        /// <summary>转账记录最多保留几条。</summary>
        public const int HistoryMax = 20;

        public static void Record(List<TxRecord> history, TxRecord r)
        {
            history.Insert(0, r);
            if (history.Count > HistoryMax) history.RemoveRange(HistoryMax, history.Count - HistoryMax);
        }
    }
}

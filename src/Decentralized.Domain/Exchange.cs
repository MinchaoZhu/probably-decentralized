using System;
using System.Collections.Generic;
using System.Linq;

namespace Decentralized.Domain
{
    public enum AmlOutcome { Clear, Light, Heavy }

    /// <summary>一次合规出金的结果。罪证由接入层记进游戏的罪证账本（销赃）。</summary>
    public sealed class CashoutResult
    {
        public double Requested;
        public double Received;
        public double Confiscated;
        public double Crime;
        public double Tax;
        public double HitChance;
        public AmlOutcome Outcome;
    }

    /// <summary>
    /// 合规交易所出金：CRC 兑回信用点，过 AML。审查按单笔金额、7 天累计金额、污点比例、是否来自混币器或被标记地址判定。
    /// 轻度命中（污点 &lt; 30%）没收污点部分、不记罪证；重度命中没收污点部分，并把污点额记为销赃罪证。
    /// </summary>
    public sealed class Exchange
    {
        /// <summary>最近的出金记录：(天, 金额)。</summary>
        public List<KeyValuePair<int, double>> History = new List<KeyValuePair<int, double>>();

        public static double HitChance(double amount, double taint, double recentTotal, double rent, bool fromMixerOrFlagged)
        {
            if (amount <= 0) return 0;
            double ratio = taint / amount;
            double big = Tuning.AmlBigRents * rent, cum = Tuning.AmlCumRents * rent;
            double p = 0;
            if (ratio >= Tuning.AmlTaintRatio) p += ratio * Tuning.AmlTaintMult;
            p += Math.Max(0, (amount - big) / big) * Tuning.AmlExcessWeight;
            p += Math.Max(0, (recentTotal + amount - cum) / cum) * Tuning.AmlExcessWeight;
            if (fromMixerOrFlagged) p += Tuning.AmlMixerBonus;
            return Math.Min(Tuning.AmlMaxChance, p);
        }

        /// <summary>
        /// 从地址里取出 amount 个 CRC 兑成信用点（按 CRC 当前价格，平时为 1；脱锚期间按市价），扣手续费。污点与成本按比例随取出部分计算；
        /// 只对超出成本的部分计税。
        /// </summary>
        public CashoutResult Cashout(Wallet wallet, double amount, Func<string, double> price, int day, double rent, bool fromMixer, Rng rng)
        {
            amount = Math.Min(amount, wallet.Get(Assets.CRC));
            var r = new CashoutResult { Requested = amount };
            if (amount <= 0) return r;
            double total = wallet.Value(price);
            double share = total > 0 ? Math.Min(1, amount * price(Assets.CRC) / total) : 0;
            double taint = wallet.Taint * share, basis = wallet.Basis * share;
            History.RemoveAll(h => h.Key <= day - Tuning.AmlWindowDays);
            double recent = History.Sum(h => h.Value);
            double value = amount * price(Assets.CRC);
            r.HitChance = Math.Min(1, HitChance(value, taint, recent, rent, fromMixer || wallet.Flagged) * StoryMods.Current.Aml(day));
            wallet.Add(Assets.CRC, -amount);
            wallet.Taint -= taint;
            wallet.Basis -= basis;
            History.Add(new KeyValuePair<int, double>(day, value));
            double got = value * (1 - Tuning.ExchangeFee);
            if (rng.Chance(r.HitChance))
            {
                r.Outcome = taint / value >= Tuning.AmlHeavyRatio ? AmlOutcome.Heavy : AmlOutcome.Light;
                r.Confiscated = Math.Min(got, taint);
                got -= r.Confiscated;
                if (r.Outcome == AmlOutcome.Heavy) r.Crime = taint;
            }
            r.Tax = Math.Max(0, got - basis) * Tuning.TaxRate;
            r.Received = got;
            return r;
        }
    }
}

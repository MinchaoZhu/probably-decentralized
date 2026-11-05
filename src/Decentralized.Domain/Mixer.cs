using System;
using System.Collections.Generic;
using System.Linq;

namespace Decentralized.Domain
{
    /// <summary>混币器里一笔在途的存入。</summary>
    public sealed class MixDeposit
    {
        public string Asset;
        public double Amount;
        /// <summary>存入时一并带走的污点与成本（信用点）。</summary>
        public double Taint, Basis;
        public int Rounds;
        public int ReadyDay;
        /// <summary>存入的钱包地址：到期提回这里。</summary>
        public string Wallet;
    }

    /// <summary>一笔混币结束时的结果，供夜间日志使用。</summary>
    public sealed class MixResult
    {
        public bool Rugged;
        public string Asset;
        public double Amount, TaintBefore, TaintAfter;
        public bool Flagged;
    }

    /// <summary>
    /// 混币器：把币存进去，每轮扣手续费、降低污点，轮数 = 等待天数，到期提回同一个地址。
    /// 匿名集 = NPC 背景量 + 玩家存入量，玩家占比越大清洗效果越差。镇压事件开始时可能被制裁：
    /// 制裁期间提出的币视为被标记（出金审查按"来自混币器或被标记地址"加重，且地址被标记）。每晚有极小概率跑路。
    /// </summary>
    public sealed class Mixer
    {
        public List<MixDeposit> Pending = new List<MixDeposit>();
        public int SanctionedUntil = -1;
        /// <summary>提回的币最近一次从混币器出来的天数，用来给出金审查加重。</summary>
        public int LastReturnDay = -999;

        public bool Sanctioned(int day) => day <= SanctionedUntil;

        /// <summary>资金来自混币器：最近 AML 窗口内提回过币，或者被标记。</summary>
        public bool RecentlyMixed(int day) => day - LastReturnDay <= Tuning.AmlWindowDays;

        /// <summary>一轮的清洗率：基准 × 匿名集系数。</summary>
        /// <summary>每轮手续费（剧情可能改动）。</summary>
        public static double Fee => Math.Min(0.5, Tuning.MixerFee * StoryMods.Current.MixerFee);

        public static double RoundRate(double depositValue) => Tuning.MixerCleanRate * Tuning.MixerNpcVolume * StoryMods.Current.MixerVolume / (Tuning.MixerNpcVolume * StoryMods.Current.MixerVolume + Math.Max(0, depositValue));

        /// <summary>存入：从钱包按价值比例带走污点与成本。轮数超出范围或余额不足返回 null。</summary>
        public MixDeposit Deposit(Wallet w, string asset, double amount, int rounds, int day, Func<string, double> price)
        {
            if (rounds < 1 || rounds > Tuning.MixerMaxRounds || amount <= 0 || w.Avail(asset) + 1e-9 < amount) return null;
            double value = amount * price(asset);
            if (value < Tuning.MixerMin) return null;
            double total = w.Value(price);
            double share = total > 0 ? Math.Min(1, value / total) : 0;
            double taint = w.Taint * share, basis = w.Basis * share;
            w.Add(asset, -amount);
            w.Taint -= taint;
            w.Basis -= basis;
            var d = new MixDeposit { Asset = asset, Amount = amount, Taint = taint, Basis = basis, Rounds = rounds, ReadyDay = day + rounds, Wallet = w.Address };
            Pending.Add(d);
            return d;
        }

        /// <summary>镇压事件开始：有一定概率被制裁，持续若干天。</summary>
        public void OnEvent(MarketEvent ev, int day, Rng rng)
        {
            if (ev == MarketEvent.Crackdown && rng.Chance(Math.Min(1, Tuning.MixerSanctionChance * StoryMods.Current.Sanction(day)))) SanctionedUntil = day + Tuning.MixerSanctionDays;
        }

        /// <summary>每晚：到期的存入提回钱包（污点按轮数降低），在途的各有小概率跑路。</summary>
        public List<MixResult> Night(Wallet w, int day, Func<string, double> price, Rng rng) => Night(_ => w, day, price, rng);

        /// <summary>每晚（多钱包）：到期的存入按存入时的地址提回原钱包。</summary>
        public List<MixResult> Night(Func<string, Wallet> wallet, int day, Func<string, double> price, Rng rng)
        {
            var results = new List<MixResult>();
            foreach (var d in Pending.ToList())
            {
                if (rng.Chance(Scales.R(Tuning.MixerRugChance * StoryMods.Current.MixerRug)))
                {
                    Pending.Remove(d);
                    results.Add(new MixResult { Rugged = true, Asset = d.Asset, Amount = d.Amount });
                    continue;
                }
                if (day < d.ReadyDay) continue;
                Pending.Remove(d);
                double rate = RoundRate(d.Amount * price(d.Asset));
                double taint = d.Taint * Math.Pow(1 - rate, d.Rounds);
                double amount = d.Amount * Math.Pow(1 - Fee, d.Rounds);
                bool flagged = Sanctioned(day);
                var w = wallet(d.Wallet);
                w.Add(d.Asset, amount);
                w.Taint += taint;
                w.Basis += d.Basis;
                if (flagged) w.Flagged = true;
                LastReturnDay = day;
                results.Add(new MixResult { Asset = d.Asset, Amount = amount, TaintBefore = d.Taint, TaintAfter = taint, Flagged = flagged });
            }
            return results;
        }
    }
}

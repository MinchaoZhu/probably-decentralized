using System;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace Decentralized.Domain
{
    /// <summary>混币器里一笔在途的存入。</summary>
    public sealed class MixDeposit
    {
        public string Asset;
        public double Amount;
        /// <summary>存入的币里脏的枚数。</summary>
        public double Dirty;
        /// <summary>旧存档（数据版本 5 之前）的污点额（信用点），读档迁移时换成 <see cref="Dirty"/>。</summary>
        [JsonProperty("Taint")] public double LegacyTaint;
        public int Rounds;
        public int ReadyDay;
        /// <summary>存入的钱包地址：到期提回这里。</summary>
        public string Wallet;
    }

    /// <summary>一笔到期提回的出款记录（泄露时按它记罪证）：Cleaned 是这笔洗掉的污点，按提回时的价格折成信用点。</summary>
    public sealed class MixReturn
    {
        public int Day;
        public string Wallet, Asset;
        public double Amount, Cleaned;
    }

    /// <summary>泄露的结果：最近几笔出款，一共洗掉了多少污点（信用点），折成的诈骗罪证。</summary>
    public sealed class MixLeak
    {
        public int Count;
        public double Cleaned, Fraud;
    }

    /// <summary>一笔混币结束时的结果，供夜间日志使用。</summary>
    public sealed class MixResult
    {
        public bool Rugged;
        public string Asset;
        public double Amount, TaintBefore, TaintAfter;
        /// <summary>被标记（制裁期间提出、从钓鱼混币器提出）：不洗，整笔都是脏的。</summary>
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
        /// <summary>最近的出款记录（Tumbler 的密钥落到治安部时用来还原）。</summary>
        public List<MixReturn> Returns = new List<MixReturn>();

        /// <summary>
        /// 泄露：最近 <see cref="Tuning.TumblerLeakDays"/> 天的出款被还原，按这些出款洗掉的污点记诈骗罪证（<see cref="Wallet.FraudFor"/>）。记录清空。
        /// </summary>
        public MixLeak Leak(int day)
        {
            var recent = Returns.Where(x => day - x.Day <= Tuning.TumblerLeakDays).ToList();
            double cleaned = recent.Sum(x => x.Cleaned);
            Returns.Clear();
            return new MixLeak { Count = recent.Count, Cleaned = cleaned, Fraud = Wallet.FraudFor(cleaned) };
        }

        private void Remember(MixReturn ret)
        {
            Returns.Add(ret);
            Returns.RemoveAll(x => ret.Day - x.Day > Tuning.TumblerLeakDays);
            while (Returns.Count > Tuning.TumblerReturnsMax) Returns.RemoveAt(0);
        }

        /// <summary>黑市卷款跑路：在途的存入全部丢失。</summary>
        public List<MixResult> RugAll()
        {
            var list = Pending.Select(p => new MixResult { Rugged = true, Asset = p.Asset, Amount = p.Amount }).ToList();
            Pending.Clear();
            return list;
        }

        public bool Sanctioned(int day) => day <= SanctionedUntil;

        /// <summary>一轮的清洗率：基准 × 匿名集系数。</summary>
        /// <summary>每轮手续费（剧情可能改动）。</summary>
        public static double Fee => Math.Min(0.5, Tuning.MixerFee * StoryMods.Current.MixerFee);

        public static double RoundRate(double depositValue, int day = -1) => Tuning.MixerCleanRate * (day >= 0 ? StoryMods.Current.MixClean(day) : 1) * Tuning.MixerNpcVolume * StoryMods.Current.MixerVolume / (Tuning.MixerNpcVolume * StoryMods.Current.MixerVolume + Math.Max(0, depositValue));

        /// <summary>存入：按这种币的脏比例带走脏枚数。轮数超出范围或余额不足返回 null。</summary>
        public MixDeposit Deposit(Wallet w, string asset, double amount, int rounds, int day, Func<string, double> price)
        {
            if (rounds < 1 || rounds > Tuning.MixerMaxRounds || amount <= 0 || w.Avail(asset) + 1e-9 < amount) return null;
            double value = amount * price(asset);
            if (value < Tuning.MixerMin) return null;
            double dirty = amount * w.DirtyRatio(asset);
            w.Add(asset, -amount);
            var d = new MixDeposit { Asset = asset, Amount = amount, Dirty = dirty, Rounds = rounds, ReadyDay = day + rounds, Wallet = w.Address };
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
                // 制裁期间提出的、从治安部的钓鱼混币器提出的，都被标记：不洗，这笔币按全额污点带回
                bool flagged = Sanctioned(day) || StoryMods.Current.MixerHoneypot;
                double rate = flagged ? 0 : RoundRate(d.Amount * price(d.Asset), day);
                double amount = d.Amount * Math.Pow(1 - Fee, d.Rounds);
                double dirty = flagged ? amount : Math.Min(amount, d.Dirty * Math.Pow(1 - rate, d.Rounds));
                var w = wallet(d.Wallet);
                w.AddDirty(d.Asset, amount, dirty);
                double p = price(d.Asset);
                Remember(new MixReturn { Day = day, Wallet = d.Wallet, Asset = d.Asset, Amount = amount, Cleaned = Math.Max(0, d.Dirty - dirty) * p });
                results.Add(new MixResult { Asset = d.Asset, Amount = amount, TaintBefore = d.Dirty * p, TaintAfter = dirty * p, Flagged = flagged });
            }
            return results;
        }
    }
}

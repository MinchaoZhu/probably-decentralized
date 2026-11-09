using System;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace Decentralized.Domain
{
    /// <summary>自营混币器里一种币：玩家的本金（数量、其中脏的枚数）和还没取走的手续费收入（取走时整笔是脏的）。</summary>
    public sealed class MixPool
    {
        public double Capital, CapDirty, Earned;
        /// <summary>旧存档（数据版本 5 之前）本金的污点额（信用点），读档迁移时换成 <see cref="CapDirty"/>。</summary>
        [JsonProperty("CapTaint")] public double LegacyCapTaint;
    }

    /// <summary>NPC 客户的一笔存入：扣掉手续费后的数量、存入时的污点额（信用点）、轮数、到期日。</summary>
    public sealed class OwnMixDeposit
    {
        public string Asset;
        public double Amount, Taint;
        public int Rounds, ReadyDay;
    }

    /// <summary>一晚发生的事。Kind：deposit（今晚存入合计）/ payout（今晚出款合计，Ratio 是出款剩下的污点比例）/ sting（便衣存入）/ seized（被查封）。</summary>
    public sealed class OwnMixEvent
    {
        public string Kind;
        public double Value, Ratio, Crime;
        public int Count;
    }

    /// <summary>
    /// 自营混币器：玩家注资开张，NPC 客户把带污点的币存进来，几天后出款，玩家收手续费。和普通混币器一样，每一轮按匿名集洗掉一部分污点：
    /// 匿名集主要是玩家的本金和没取走的收入（客户在途的币按一个比例折算），注资越多洗得越干净，客户越愿意来。
    /// 手续费是玩家的收入，留在池子里，取走时全额带污点；本金取回时带着它自己的污点（注资用脏币，取回的也脏）。
    /// 客流 = 基础需求(天) × 黑市声望 × 和 Tumbler 比的吸引力（费率加上出款剩下的污点，近期出款的滑动平均就是口碑）。
    /// 风声随客流累积（池子越大越难追查，累积得越慢）、每晚衰减，按风声有概率被查封：池子和在途的存入全部没收，记销赃罪证，停业若干天。营业的晚上可能有便衣存入，记销赃罪证。
    /// 玩家可以随时卷款跑路：池子连同客户的币全部进钱包、全额带污点，记诈骗罪证，混币器永久关闭。
    /// </summary>
    public sealed class OwnMixer
    {
        public static readonly string[] AssetsList = { Assets.PSC, Assets.CRC, Assets.CRT, Assets.CRA };

        public bool Open;
        public bool Closed;
        public int SeizedUntil = -1;
        public double Fee = Tuning.OwnMixFeeDefault;
        /// <summary>口碑：近期出款剩下的污点比例（滑动平均），越低越好；开张时按当时的匿名集估。</summary>
        public double Rating = 1;
        public double Heat;
        public Dictionary<string, MixPool> Pools = new Dictionary<string, MixPool>();
        public List<OwnMixDeposit> Pending = new List<OwnMixDeposit>();
        /// <summary>累计手续费收入（信用点，按入账时的价格），统计用。</summary>
        public double FeesTotal;

        public bool Seized(int day) => day <= SeizedUntil;
        public bool Running(int day) => Open && !Closed && !Seized(day);

        public MixPool Pool(string asset)
        {
            if (!Pools.TryGetValue(asset, out var p)) Pools[asset] = p = new MixPool();
            return p;
        }

        /// <summary>一种币在池子里的总数：本金、收入、客户在途的币。</summary>
        public double Total(string asset) => Pool(asset).Capital + Pool(asset).Earned + Pending.Where(x => x.Asset == asset).Sum(x => x.Amount);

        /// <summary>匿名集（信用点）：玩家的本金和收入，加上客户在途的币按 OwnMixPendingWeight 折算（主要靠注资撑起来）。</summary>
        public double Anonymity(Func<string, double> price) => AssetsList.Sum(a => (Free(a) + Tuning.OwnMixPendingWeight * Pending.Where(x => x.Asset == a).Sum(x => x.Amount)) * price(a));

        /// <summary>每一轮洗掉的污点比例：基准清洗率 × 匿名集 / (匿名集 + OwnMixAnon0)。</summary>
        public static double RoundRate(double anonymity) => Tuning.MixerCleanRate * anonymity / (anonymity + Tuning.OwnMixAnon0);

        /// <summary>按平均轮数出款时剩下的污点比例。</summary>
        public static double Remain(double rate) => Math.Pow(1 - rate, Tuning.OwnMixAvgRounds);

        /// <summary>玩家能取走的：本金加收入（客户在途的币不能动）。</summary>
        public double Free(string asset) => Pool(asset).Capital + Pool(asset).Earned;

        /// <summary>开张（或停业后重新开张）：口碑按现在的匿名集估。</summary>
        public void Start(Func<string, double> price)
        {
            Open = true;
            Rating = Remain(RoundRate(Anonymity(price)));
        }

        /// <summary>注资：币从钱包进本金，按这种币的脏比例带上脏枚数。</summary>
        public bool Inject(Wallet w, string asset, double amount)
        {
            if (amount <= 0 || w.Avail(asset) + 1e-9 < amount || !AssetsList.Contains(asset)) return false;
            double dirty = amount * w.DirtyRatio(asset);
            w.Add(asset, -amount);
            var p = Pool(asset);
            p.Capital += amount; p.CapDirty += dirty;
            return true;
        }

        /// <summary>取回：不超过 <see cref="Free"/>。先取手续费收入（整笔是脏的），再取本金（按本金的脏比例）。</summary>
        public bool Withdraw(Wallet w, string asset, double amount)
        {
            var p = Pool(asset);
            if (amount <= 0 || amount > Free(asset) + 1e-9) return false;
            double fromFees = Math.Min(amount, p.Earned), fromCap = Math.Min(p.Capital, amount - fromFees);
            double capDirty = p.Capital > 1e-12 ? p.CapDirty * fromCap / p.Capital : 0;
            p.Earned -= fromFees; p.Capital -= fromCap; p.CapDirty -= capDirty;
            w.AddDirty(asset, fromFees + fromCap, fromFees + capDirty);
            return true;
        }

        /// <summary>卷款跑路：本金、收入和客户在途的币全部进钱包，整笔是脏的，永久关闭。返回 (币种, 数量)。</summary>
        public List<KeyValuePair<string, double>> Rug(Wallet w)
        {
            var got = new List<KeyValuePair<string, double>>();
            foreach (var a in AssetsList)
            {
                double amt = Total(a);
                if (amt <= 1e-9) continue;
                w.AddDirty(a, amt, amt);
                got.Add(new KeyValuePair<string, double>(a, amt));
            }
            Pools.Clear(); Pending.Clear();
            Open = false; Closed = true;
            return got;
        }

        /// <summary>今晚的基础需求（信用点）：随天数增长。</summary>
        public static double Demand(int day) => Tuning.OwnMixDemand0 + Tuning.OwnMixDemandPerDay * day;

        /// <summary>
        /// 和 Tumbler 的混币器比，分到的客流（0–1）：客户的成本 = 费率 + 出款剩下的污点比例 × OwnMixTaintWeight，比对手便宜就多分。
        /// rivalRemain 是对手出款剩下的污点比例；对手关停时客户的另一条路是不混（成本再加 OwnMixRivalGone）。
        /// </summary>
        public double Share(double rivalFee, double rivalRemain, bool rivalOpen)
        {
            double mine = Fee + Rating * Tuning.OwnMixTaintWeight;
            double rival = rivalFee + rivalRemain * Tuning.OwnMixTaintWeight + (rivalOpen ? 0 : Tuning.OwnMixRivalGone);
            return 1 / (1 + Math.Exp((mine - rival) / Tuning.OwnMixFeeScale));
        }

        /// <summary>
        /// 夜间：到期的存入出款（按匿名集洗掉污点），口碑跟着出款走；营业中接新的存入；风声累积、按风声查封。
        /// bmRep：黑市声望（0–100）；rivalFee、rivalRemain、rivalOpen：Tumbler 混币器的费率、出款剩下的污点比例、是否在营业；enforce：剧情带来的执法倍数；crackdown：镇压期间。
        /// </summary>
        public List<OwnMixEvent> Night(int day, Func<string, double> price, int bmRep, double rivalFee, double rivalRemain, bool rivalOpen, double enforce, bool crackdown, Rng rng)
        {
            var evs = new List<OwnMixEvent>();
            if (Closed || Seized(day)) return evs;

            // 出款（客户的币回到客户手里，污点按匿名集洗掉一部分）
            double rate = RoundRate(Anonymity(price));
            double paid = 0; int paidN = 0;
            foreach (var dep in Pending.Where(x => x.ReadyDay <= day).ToList())
            {
                Pending.Remove(dep);
                paid += dep.Amount * price(dep.Asset);
                paidN++;
            }
            if (paidN > 0)
            {
                double remain = Remain(rate);
                Rating += Tuning.OwnMixRatingSpeed * (remain - Rating);
                evs.Add(new OwnMixEvent { Kind = "payout", Value = paid, Ratio = remain, Count = paidN });
            }

            // 存入
            double inValue = 0; int n = 0;
            if (Open)
            {
                double repF = Tuning.OwnMixRepMin + (1 - Tuning.OwnMixRepMin) * Math.Max(0, Math.Min(100, bmRep)) / 100.0;
                double demand = Demand(day) * repF * Share(rivalFee, rivalRemain, rivalOpen);
                double left = demand * rng.Range(1 - Tuning.OwnMixNoise, 1 + Tuning.OwnMixNoise);
                while (left > Tuning.MixerMin)
                {
                    double v = Math.Min(left, Tuning.OwnMixTicket * rng.Range(0.5, 1.5));
                    left -= v;
                    string asset = Pick(rng);
                    double px = price(asset);
                    if (px <= 0) continue;
                    double amt = v / px;
                    Pool(asset).Earned += amt * Fee;
                    FeesTotal += v * Fee;
                    int rounds = rng.Range(1, Tuning.MixerMaxRounds);
                    Pending.Add(new OwnMixDeposit { Asset = asset, Amount = amt * (1 - Fee), Taint = v * rng.Range(Tuning.OwnMixInTaintMin, Tuning.OwnMixInTaintMax), Rounds = rounds, ReadyDay = day + rounds });
                    inValue += v; n++;
                }
                if (n > 0)
                {
                    evs.Add(new OwnMixEvent { Kind = "deposit", Value = inValue, Count = n });
                    // 便衣：营业的晚上按概率有一笔存入来自治安部
                    if (rng.Chance(Tuning.OwnMixSting))
                    {
                        double v = inValue / n;
                        evs.Add(new OwnMixEvent { Kind = "sting", Value = v, Crime = Math.Min(Tuning.CrimeEventCap, v * Tuning.OwnMixStingCrime) });
                    }
                }
            }

            // 风声与查封
            // 池子越大，客户的币越好藏，同样的客流引起的风声越小
            Heat = Heat * Tuning.OwnMixHeatKeep + inValue / Tuning.OwnMixHeatScale / (1 + Anonymity(price) / Tuning.OwnMixHeatAnon0);
            if (rng.Chance(SeizeChance(enforce, crackdown)))
            {
                double lost = AssetsList.Sum(a => Total(a) * price(a));
                evs.Add(new OwnMixEvent { Kind = "seized", Value = lost, Crime = Math.Min(Tuning.CrimeEventCap, lost * Tuning.OwnMixSeizeCrime) });
                Pools.Clear(); Pending.Clear();
                SeizedUntil = day + Tuning.OwnMixSeizeDays;
                Heat = 0; Open = false;
            }
            return evs;
        }

        /// <summary>今晚被查封的概率：风声 × 系数 × 执法倍数（镇压期间再乘），封顶。</summary>
        public double SeizeChance(double enforce, bool crackdown) => Math.Min(Tuning.OwnMixSeizeMax, Tuning.OwnMixSeizeRate * Heat * enforce * (crackdown ? Tuning.OwnMixSeizeCrackdown : 1));

        /// <summary>风声的三档提示：0 低、1 中、2 高（按今晚的查封概率）。</summary>
        public int HeatLevel(double enforce) { double s = SeizeChance(enforce, false); return s < Tuning.OwnMixHeatMid ? 0 : s < Tuning.OwnMixHeatHigh ? 1 : 2; }

        private static string Pick(Rng rng)
        {
            double r = rng.NextDouble() * Tuning.OwnMixAssetWeights.Sum();
            for (int i = 0; i < AssetsList.Length; i++) { r -= Tuning.OwnMixAssetWeights[i]; if (r <= 0) return AssetsList[i]; }
            return AssetsList[AssetsList.Length - 1];
        }
    }
}

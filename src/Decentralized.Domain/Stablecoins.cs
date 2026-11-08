using System;
using System.Collections.Generic;
using System.Linq;

namespace Decentralized.Domain
{
    /// <summary>
    /// 三种稳定币的价格与状态：CRC（上层，托管银行出事时脱锚、可能减记），CRT（黑市，镇压或火并时脱锚，
    /// 镇压期间按黑市声望出卖客户），CRA（革命军，部分抵押：流通量 S、储备 R（CRC）、抵押率 c；质押利息以无抵押的新 CRA 付出，
    /// 稀释储备覆盖率 R/S；革命军挪用储备时 R/S 低于崩盘线进入三天死亡螺旋，劫掠上层补回储备）。
    /// </summary>
    public enum CraOrderKind { Minted, Burned, Lost }

    /// <summary>官方一级市场的排队单：铸造（存入 CRC 和 SPARK）或销毁（存入 CRA），到期结算，官方可能把它黑掉。</summary>
    public sealed class CraOrder
    {
        public int Id, Due;
        public string Wallet;
        public bool Mint;
        public double Cra, Crc, Spark, C;
        /// <summary>付出去的币的脏比例（按价值加权）：到期换回的币按这个比例是脏的。</summary>
        public double DirtyRatio;
    }

    public sealed class CraOrderEvent
    {
        public CraOrder Order;
        public CraOrderKind Kind;
        public double Cra, Crc, Spark;
    }

    public sealed class Stablecoins
    {
        public double Crc = 1, Crt = 1, Cra = 1;
        public int CrcDepegEnd = -1, CrtDepegEnd = -1;
        public bool CrcHaircutPending;
        /// <summary>CRA 流通量与储备（CRC）；c 是现在铸造要求的 CRC 份额（0～1）。</summary>
        public double Supply = Tuning.CraS0, Reserve = Tuning.CraS0 * Tuning.CraC0, C = Tuning.CraC0;
        /// <summary>预兆日（新闻发出的那天）、预定的挪用日、崩盘开始日（-1 = 没有）。</summary>
        public int CraWarnDay = -1, CraEventDay = -1, CraCrashDay = -1;
        public List<CraOrder> Orders = new List<CraOrder>();
        public int OrderSeq;
        public double RevPower = 50;
        /// <summary>平衡代币 SPARK 的价格（信用点）；SparkPre 是螺旋开始前一刻的价格，螺旋三天按它的倍数走。</summary>
        public double Spark = Tuning.SparkStart, SparkPre = -1;
        public List<double> RevHistory = new List<double>();

        public double Price(string asset)
        {
            switch (asset)
            {
                case Assets.CRC: return Crc;
                case Assets.CRT: return Crt;
                case Assets.CRA: return Cra;
                case Assets.SPARK: return Spark;
                default: return 1;
            }
        }

        /// <summary>减记发生时返回 CRC 持有量要乘的系数（其余情况为 1）。</summary>
        public double OnEventStart(MarketEvent ev, int day, Rng rng)
        {
            if (ev == MarketEvent.RevRaidUpper && CrcDepegEnd < 0 && rng.Chance(Tuning.CrcDepegChance * StoryMods.Current.CrcDepeg))
            {
                Crc = rng.Range(Tuning.CrcDepegMin, Tuning.CrcDepegMax);
                CrcDepegEnd = day + rng.Range(Tuning.CrcDepegDaysMin, Tuning.CrcDepegDaysMax);
                CrcHaircutPending = rng.Chance(Math.Min(1, Tuning.CrcHaircutChance * StoryMods.Current.CrcHaircut));
            }
            if ((ev == MarketEvent.GangWar || ev == MarketEvent.Crackdown) && CrtDepegEnd < 0 && rng.Chance(Tuning.CrtDepegChance * StoryMods.Current.CrtDepeg))
            {
                Crt = rng.Range(Tuning.CrtDepegMin, Tuning.CrtDepegMax);
                CrtDepegEnd = day + rng.Range(Tuning.CrtDepegDaysMin, Tuning.CrtDepegDaysMax);
            }
            if (ev == MarketEvent.RevRaidUpper) { Spark *= Tuning.SparkRaidShock; if (CraCrashDay < 0) Reserve += Tuning.CraLoot * Supply; }
            if (ev == MarketEvent.LowerUnrest) Spark *= Tuning.SparkUnrestShock;
            return 1;
        }

        /// <summary>镇压开始时，玩家持有的 CRT 被冻结一半的概率：黑市声望 0 时 40%，50 以上为 0。</summary>
        public static double CrtFreezeChance(double blackMarketRep) =>
            Math.Max(0, Tuning.CrtFreezeBase - blackMarketRep * Tuning.CrtFreezePerRep) * StoryMods.Current.CrtFreeze;

        /// <summary>镇压开始时是否冻结玩家的 CRT：按黑市声望，声望越低越容易被黑市"出卖"。</summary>
        public static bool CrtFrozenByCrackdown(double blackMarketRep, Rng rng) => rng.Chance(CrtFreezeChance(blackMarketRep));

        /// <summary>储备覆盖率 R/S。</summary>
        public double Ratio => Supply > 0 ? Reserve / Supply : 1;

        /// <summary>崩盘线之上的储备（CRC）：吃空即必然崩盘。预测盘和线索用它估预兆。</summary>
        public double Cushion => Reserve - Tuning.CraCrashRatio * Supply;
        public static double Cushion0 => Tuning.CraS0 * (Tuning.CraC0 - Tuning.CraCrashRatio);

        /// <summary>每晚的垫子消耗：利息按无抵押的新 CRA 付出，每份把崩盘线抬高 CraCrashRatio。</summary>
        public static double CushionDrain(double stakedTotal, double rate) => stakedTotal * rate * Tuning.CraCrashRatio;

        /// <summary>储备覆盖率对应的价格：高于安全线为 1，线性降到崩盘线的 1 − CraDiscountMax。</summary>
        public static double PriceOfRatio(double r)
        {
            if (r >= Tuning.CraSafeRatio) return 1;
            double t = Math.Max(0, (r - Tuning.CraCrashRatio) / (Tuning.CraSafeRatio - Tuning.CraCrashRatio));
            return 1 - Tuning.CraDiscountMax * (1 - t);
        }

        /// <summary>铸造 cra 个 CRA 要的 CRC 和 SPARK 数量（SPARK 部分按现价折算）。</summary>
        public void Collateral(double cra, out double crc, out double spark)
        {
            crc = cra * C;
            spark = Spark > 0 ? cra * (1 - C) / Spark : 0;
        }

        /// <summary>
        /// 铸造申请：从钱包扣 CRC 和 SPARK，排队 CraMintDaysMin..Max 天，到期铸出 CRA。余额不足返回 null。
        /// </summary>
        public CraOrder RequestMint(Wallet w, double cra, int day, Rng rng)
        {
            if (cra <= 0) return null;
            Collateral(cra, out double crc, out double spark);
            if (w.Avail(Assets.CRC) + 1e-9 < crc || w.Avail(Assets.SPARK) + 1e-9 < spark) return null;
            double sv = spark * Spark, total = crc + sv;
            double ratio = total > 0 ? (crc * w.DirtyRatio(Assets.CRC) + sv * w.DirtyRatio(Assets.SPARK)) / total : 0;
            w.Add(Assets.CRC, -crc); w.Add(Assets.SPARK, -spark);
            var o = new CraOrder { Id = ++OrderSeq, Wallet = w.Address, Mint = true, Cra = cra, Crc = crc, Spark = spark, C = C, DirtyRatio = ratio, Due = day + rng.Range(Tuning.CraMintDaysMin, Tuning.CraMintDaysMax) };
            Orders.Add(o);
            return o;
        }

        /// <summary>销毁申请：从钱包扣 CRA，排队 CraUnlockDays 天，到期换回 CRC 和 SPARK。余额不足返回 null。</summary>
        public CraOrder RequestBurn(Wallet w, double cra, int day)
        {
            if (cra <= 0 || w.Avail(Assets.CRA) + 1e-9 < cra) return null;
            double ratio = w.DirtyRatio(Assets.CRA);
            w.Add(Assets.CRA, -cra);
            var o = new CraOrder { Id = ++OrderSeq, Wallet = w.Address, Mint = false, Cra = cra, C = C, DirtyRatio = ratio, Due = day + Tuning.CraUnlockDays };
            Orders.Add(o);
            return o;
        }

        /// <summary>官方每晚黑掉一笔排队单的概率：系数 × (1 − CRA 价格)，价格不低于 1 时为 0。</summary>
        public double RugChance => Math.Max(0, 1 - Cra) * Tuning.CraRugPerDepeg;

        public bool CraSubsidyLow => Cushion <= Cushion0 * Tuning.CraSubsidyWarn;

        /// <summary>CRA 的预兆：外生螺旋的新闻已出，或补贴池告急（利率下调）。螺旋开始后不再算预兆。</summary>
        public bool CraWarning(int day) => (CraWarnDay >= 0 && CraWarnDay <= day || CraSubsidyLow) && CraCrashDay < 0 && Cra >= 0.97;

        public double CraRate => CraSubsidyLow ? Tuning.CraRateLow : Tuning.CraRate;

        /// <summary>
        /// 夜间结算。返回 CRC 持有量要乘的系数（减记兑现时为 0.95，否则为 1）。
        /// playerStaked：玩家的 CRA 质押额，用来算利息带来的稀释（玩家的利息由调用方按 <see cref="CraRate"/> 发放）。
        /// </summary>
        public double Night(int day, Rng rng, double playerStaked)
        {
            double crcFactor = 1;
            if (CrcDepegEnd == day)
            {
                Crc = 1;
                if (CrcHaircutPending) crcFactor = Tuning.CrcHaircut;
                CrcDepegEnd = -1;
                CrcHaircutPending = false;
            }
            if (CrtDepegEnd == day)
            {
                Crt = 1;
                CrtDepegEnd = -1;
            }
            RevHistory.Add(RevPower);
            if (RevHistory.Count > 7) RevHistory.RemoveAt(0);
            RevPower = Math.Max(0, Math.Min(100, RevPower + rng.Gauss() * 3));

            if (CraCrashDay < 0)
            {
                double interest = (Tuning.CraNpcStake + playerStaked) * CraRate;
                Supply += interest;
                // 抵押率调整：价格低于 1 上调，回到 1 下调；兑付的 CRC 份额不超过储备均值（见 <see cref="Redeem"/>）
                if (Cra < 0.995) C = Math.Min(1, C + Tuning.CraCrStep);
                else if (Cra >= 0.999) C = Math.Max(Tuning.CraCrMin, C - Tuning.CraCrStep);
                if (CraEventDay < 0)
                {
                    bool revFalling = RevHistory.Count == 7 && RevPower < RevHistory[0] - 5;
                    double weekly = Tuning.CraSpiralWeekly * (revFalling ? 3 : 1) * StoryMods.Current.CraSpiral(day);
                    double daily = 1 - Math.Pow(1 - weekly, 1.0 / 7);
                    if (rng.Chance(daily) || Cushion <= 0)
                    {
                        CraWarnDay = day;
                        CraEventDay = day + rng.Range(Tuning.CraWarnLeadMin, Tuning.CraWarnLeadMax);
                    }
                }
                else if (day >= CraEventDay)
                {
                    // 挪用：储备被革命军拿走一部分，R/S 掉到崩盘线以下就是挤兑
                    Reserve *= 1 - rng.Range(Tuning.CraEmbezzleMin, Tuning.CraEmbezzleMax);
                    CraEventDay = -1; CraWarnDay = -1;
                    if (Ratio < Tuning.CraCrashRatio) CraCrashDay = day;
                }
            }
            if (CraCrashDay >= 0)
            {
                int k = Math.Min(day - CraCrashDay, Tuning.CraSpiralPath.Length - 1);
                Cra = Tuning.CraSpiralPath[k];
                if (SparkPre < 0) SparkPre = Spark;
                Spark = SparkPre * Tuning.SparkSpiralPath[Math.Min(day - CraCrashDay, Tuning.SparkSpiralPath.Length - 1)];
            }
            else
            {
                Cra = PriceOfRatio(Ratio);
                Spark *= Math.Exp(rng.Gauss() * Tuning.SparkVol) * StoryMods.Current.TakeSparkPush();
                Spark += (Tuning.SparkStart - Spark) * Tuning.SparkRevert;
                Spark = Math.Max(Tuning.SparkStart * Tuning.SparkMinMult, Math.Min(Tuning.SparkStart * Tuning.SparkMaxMult, Spark));
            }
            return crcFactor;
        }

        /// <summary>
        /// 销毁一份 CRA 的兑付：CRC 份额 = min(申请时的 c, 储备均值 R/S)，其余按现价用新增发的 SPARK 补。
        /// 返回 (CRC 数量, SPARK 数量)，并扣减储备和流通量。
        /// </summary>
        public void Redeem(double cra, double c, out double crc, out double spark)
        {
            double share = Math.Min(c, Ratio);
            crc = Math.Min(Reserve, cra * share);
            spark = Spark > 0 ? cra * (1 - crc / Math.Max(1e-9, cra)) / Spark : 0;
            Reserve -= crc; Supply = Math.Max(0, Supply - cra);
        }

        /// <summary>
        /// 夜间处理排队单：先对每一笔掷官方跑路（整笔丢失），再结算到期的。
        /// 铸造到期：CRC 进储备、SPARK 销毁、流通量增加；销毁到期：按 <see cref="Redeem"/> 兑付。
        /// </summary>
        public List<CraOrderEvent> NightOrders(int day, Rng rng)
        {
            var evs = new List<CraOrderEvent>();
            foreach (var o in Orders.ToList())
            {
                if (rng.Chance(RugChance)) { Orders.Remove(o); evs.Add(new CraOrderEvent { Order = o, Kind = CraOrderKind.Lost }); continue; }
                if (day < o.Due) continue;
                Orders.Remove(o);
                var ev = new CraOrderEvent { Order = o };
                if (o.Mint)
                {
                    Supply += o.Cra; Reserve += o.Crc;
                    ev.Kind = CraOrderKind.Minted; ev.Cra = o.Cra;
                }
                else
                {
                    Redeem(o.Cra, o.C, out ev.Crc, out ev.Spark);
                    ev.Kind = CraOrderKind.Burned;
                }
                evs.Add(ev);
            }
            return evs;
        }
    }
}

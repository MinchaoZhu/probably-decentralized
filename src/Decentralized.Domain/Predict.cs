using System;
using System.Collections.Generic;
using System.Linq;

namespace Decentralized.Domain
{
    public enum PredictKind { Event, Faction, Purity, Price, Gas, Cra, Era }

    public enum PredictResult { Ok, Closed, Amount, Funds, Cap, Opposite, NoPosition }

    /// <summary>
    /// 一个盘口：问一个是非题，开盘当天可以买卖，窗口（FromDay..ToDay）里的结果由游戏或本 mod 的数值决定，ToDay 夜间结算。
    /// 定价用 LMSR：初始价等于先验概率 P0，流动性参数 B 按开盘日的单盘额度（<see cref="Budget"/>）定，与先验无关；
    /// 额度决定份额上限（限制内幕单能赚多少）和内幕消息的价格。
    /// </summary>
    public sealed class PredictMarket
    {
        public int Id;
        public PredictKind Kind;
        /// <summary>事件 / 派系标识；纯度盘是 "gutter" 或 "rust"；行情盘是 "PSC"。</summary>
        public string Key, Title;
        /// <summary>阈值（纯度、价格盘）。</summary>
        public double Param;
        public int OpenDay, FromDay, ToDay;
        public double P0, B, QYes, QNo;
        /// <summary>单盘额度（开盘日的 <see cref="LossFor"/>）。旧存档里的盘口没有这个字段，按当时"额度 = B × ln(1/先验)"回推。</summary>
        public double Budget;
        /// <summary>昨晚消息反应之前的价格（-1 = 还没反应过）：列表里显示涨跌，让玩家看到消息怎样推动价格。</summary>
        public double PrevP = -1;
        /// <summary>-1 未结算，0 否，1 是，2 作废（缺观测数据，退还本金）。</summary>
        public int Outcome = -1;

        public double Loss => Budget > 0 ? Budget : B * Math.Log(1 / Math.Min(P0, 1 - P0));
        public double ShareCap => Tuning.PredictCap * Loss;
        public bool IsOpen(int day) => Outcome < 0 && day < FromDay;
        public double PYes => 1 / (1 + Math.Exp((QNo - QYes) / B));

        public static double LossFor(int day) => Tuning.PredictLoss0 + Tuning.PredictLossPerDay * day;

        public static PredictMarket Create(int id, PredictKind kind, string key, string title, double param, int openDay, int fromDay, int toDay, double p0)
        {
            p0 = Math.Max(Tuning.PredictPMin, Math.Min(Tuning.PredictPMax, p0));
            double budget = LossFor(openDay), b = Tuning.PredictDepth * budget / Math.Log(2);
            return new PredictMarket { Id = id, Kind = kind, Key = key, Title = title, Param = param, OpenDay = openDay, FromDay = fromDay, ToDay = toDay, P0 = p0, B = b, Budget = budget, QYes = b * Math.Log(p0 / (1 - p0)) };
        }

        /// <summary>LMSR 成本函数（数值稳定写法）。</summary>
        public double C(double qy, double qn)
        {
            double m = Math.Max(qy, qn);
            return m + B * Math.Log(Math.Exp((qy - m) / B) + Math.Exp((qn - m) / B));
        }

        /// <summary>某一边买入 shares 份要付的成本（不含手续费）。</summary>
        public double BuyCost(bool yes, double shares) => yes ? C(QYes + shares, QNo) - C(QYes, QNo) : C(QYes, QNo + shares) - C(QYes, QNo);

        /// <summary>某一边卖出 shares 份能收回的金额（不含手续费）。</summary>
        public double SellValue(bool yes, double shares) => yes ? C(QYes, QNo) - C(QYes - shares, QNo) : C(QYes, QNo) - C(QYes, QNo - shares);

        /// <summary>花 cost（不含手续费）能买到的份数：LMSR 成本函数的闭式反解。</summary>
        public double SharesFor(bool yes, double cost)
        {
            double y = yes ? QYes : QNo, o = yes ? QNo : QYes;
            double m = Math.Max(y, o);
            double a = Math.Exp((y - m) / B), oo = Math.Exp((o - m) / B);
            double x = Math.Exp(Math.Min(cost / B, 700)) * (a + oo) - oo;
            return m + B * Math.Log(x) - y;
        }

        public double PriceOf(bool yes) => yes ? PYes : 1 - PYes;
    }

    /// <summary>玩家在某个盘口的持仓（一个盘口只能持有一边）。Insider 是内幕单买入的份数。</summary>
    public sealed class PredictPos
    {
        public int Market;
        /// <summary>持仓所在的钱包地址（下注签名的钱包，结算和卖出都回到它）。</summary>
        public string Wallet;
        public bool Yes;
        public double Shares, Cost, Taint, Basis, Insider;
    }

    /// <summary>情报贩子卖的内幕消息：对某个盘口给出"是 / 否"的说法，三档准确率，可能是假的（与真相相反）。</summary>
    public sealed class PredictTip
    {
        public int Market, Tier;
        public bool Claim;
        public double Paid;
    }

    public sealed class PredictStat
    {
        public int Count, Hits;
        public double SumP0;
        public double AvgP0 => Count > 0 ? SumP0 / Count : 0;
        public double HitRate => Count > 0 ? Hits / (double)Count : 0;
    }

    /// <summary>一次夜间价格反应。</summary>
    public sealed class PredictMove
    {
        public PredictMarket Market;
        public double From, To;
    }

    /// <summary>结算记录（盘口列表下面的"最近结果"）。</summary>
    public sealed class PredictRecord
    {
        public int Day;
        public string Title;
        public int Outcome;
        public double Pnl;
        public bool Held;
    }

    /// <summary>夜间结算发生的事。Kind：win / lose / void / caught。</summary>
    public sealed class PredictEvent
    {
        public string Kind, Title;
        public PredictMarket Market;
        public double Amount, Fraud;
    }

    /// <summary>候选盘口（接入层按游戏当前状态给出）：先验概率按窗口算。</summary>
    public sealed class PredictCandidate
    {
        public PredictKind Kind;
        public string Key, Title;
        public double Param;
        /// <summary>true：只问明天一天（纯度盘）。</summary>
        public bool Tomorrow;
        public Func<int, int, double> Prob;
    }

    public sealed class PredictBook
    {
        public List<PredictMarket> Markets = new List<PredictMarket>();
        public List<PredictPos> Positions = new List<PredictPos>();
        public List<PredictRecord> Recent = new List<PredictRecord>();
        public List<PredictTip> Tips = new List<PredictTip>();
        /// <summary>按盘口类型的历史：开过几个、开盘价之和、实际发生几次。玩家据此看出哪类盘口系统性地定价偏高或偏低。</summary>
        public Dictionary<string, PredictStat> Stats = new Dictionary<string, PredictStat>();
        public int Seq;
        /// <summary>每晚观测：当天新出现的事件标识（和 "F:派系"）。窗口里缺任何一天的观测，盘口作废退款。</summary>
        public Dictionary<int, List<string>> Started = new Dictionary<int, List<string>>();
        public List<string> Prev;

        public PredictMarket Find(int id) => Markets.Find(m => m.Id == id);
        public PredictPos PosOf(int id, string wallet) => Positions.Find(p => p.Market == id && p.Wallet == wallet);
        public List<PredictPos> PosAll(int id) => Positions.Where(p => p.Market == id).ToList();

        private static double Share(Wallet acct, double amount, Func<string, double> price)
        {
            double total = acct.Value(price);
            return total > 0 ? Math.Min(1, amount * price(Assets.CRC) / total) : 0;
        }

        /// <summary>
        /// 买入：花 spend（含手续费）的 CRC。污点与成本按花掉的价值比例随资金进持仓。insider：这笔单下单时玩家已经知道答案（传闻纸条、派系特权）。
        /// </summary>
        public PredictResult Buy(Wallet acct, PredictMarket m, bool yes, double spend, int day, Func<string, double> pr, bool insider, out double shares)
        {
            shares = 0;
            if (!m.IsOpen(day)) return PredictResult.Closed;
            if (spend <= 0) return PredictResult.Amount;
            if (acct.Avail(Assets.CRC) + 1e-9 < spend) return PredictResult.Funds;
            var all = PosAll(m.Id);
            if (all.Any(x => x.Yes != yes)) return PredictResult.Opposite;
            var pos = PosOf(m.Id, acct.Address);
            double cost = spend / (1 + Tuning.PredictFee);
            shares = m.SharesFor(yes, cost);
            if (all.Sum(x => x.Shares) + shares > m.ShareCap + 1e-9) { shares = 0; return PredictResult.Cap; }
            double share = Share(acct, spend, pr);
            double taint = acct.Taint * share, basis = acct.Basis * share;
            acct.Add(Assets.CRC, -spend); acct.Taint -= taint; acct.Basis -= basis;
            if (yes) m.QYes += shares; else m.QNo += shares;
            if (pos == null) Positions.Add(pos = new PredictPos { Market = m.Id, Yes = yes, Wallet = acct.Address });
            pos.Shares += shares; pos.Cost += cost; pos.Taint += taint; pos.Basis += basis;
            if (insider) pos.Insider += shares;
            return PredictResult.Ok;
        }

        /// <summary>全部卖出这个钱包在盘口里的持仓（盘口关闭前）：收回 LMSR 的卖价减手续费，污点按比例退回，赚的部分是干净的。返回收回的 CRC。</summary>
        public PredictResult SellAll(Wallet acct, PredictMarket m, int day, out double got) => SellAll(acct, m, day, null, out got, out _);

        /// <summary>
        /// 全部卖出；内幕持仓（这个盘口合计）达到查处线、而且卖出有赚，按查处概率被查：本金和收益全部没收，收益的一半记罪证（fraud &gt; 0）。
        /// 先买后卖、赶在结算前离场，同样会被查。
        /// </summary>
        public PredictResult SellAll(Wallet acct, PredictMarket m, int day, Rng rng, out double got, out double fraud)
        {
            got = 0; fraud = 0;
            var pos = PosOf(m.Id, acct.Address);
            if (pos == null) return PredictResult.NoPosition;
            if (!m.IsOpen(day)) return PredictResult.Closed;
            double value = m.SellValue(pos.Yes, pos.Shares);
            got = value * (1 - Tuning.PredictFee);
            bool insider = PosAll(m.Id).Sum(x => x.Insider) >= Tuning.PredictInsiderShare * m.ShareCap;
            if (pos.Yes) m.QYes -= pos.Shares; else m.QNo -= pos.Shares;
            Positions.Remove(pos);
            if (rng != null && insider && pos.Insider > 0 && got > pos.Cost && rng.Chance(Math.Min(1, Tuning.PredictInsiderCatch * StoryMods.Current.Enforce(day))))
            {
                fraud = Math.Min(Tuning.PredictFraudCap, (got - pos.Cost) * Tuning.PredictFraudShare);
                got = 0;
            }
            Pay(acct, pos, got);
            return PredictResult.Ok;
        }

        private static void Pay(Wallet acct, PredictPos pos, double payout)
        {
            double keep = pos.Cost > 0 ? Math.Min(1, payout / pos.Cost) : 0;
            acct.Add(Assets.CRC, payout); acct.Taint += pos.Taint * keep; acct.Basis += pos.Basis * keep;
        }

        /// <summary>
        /// 夜间：先登记今晚的观测，再结算 ToDay 到了的盘口（赢的钱回到下注的钱包，wallet 按地址找）。outcome 由接入层给出（事件 / 派系读观测，纯度和行情读游戏数值），null 表示拿不到数据。
        /// 赢家每份兑付 1 信用点；内幕持仓达到份额上限的 <see cref="Tuning.PredictInsiderShare"/> 后，有概率被查：本金和收益全部没收，收益的一半记罪证。
        /// </summary>
        public List<PredictEvent> Night(int day, IEnumerable<string> activeNow, Func<PredictMarket, bool?> outcome, Func<string, Wallet> wallet, Rng rng, double push = 0)
        {
            var now = activeNow.Distinct().ToList();
            if (Prev != null) Started[day] = now.Where(x => !Prev.Contains(x)).ToList();
            Prev = now;
            foreach (var k in Started.Keys.Where(k => k < day - 12).ToList()) Started.Remove(k);
            var evs = new List<PredictEvent>();
            foreach (var m in Markets.Where(x => x.Outcome < 0 && x.ToDay <= day).ToList())
            {
                bool? res = outcome(m);
                var all = PosAll(m.Id);
                foreach (var pos in all) Positions.Remove(pos);
                double cost = all.Sum(x => x.Cost);
                if (res == null)
                {
                    m.Outcome = 2;
                    foreach (var pos in all) Pay(wallet(pos.Wallet), pos, pos.Cost);
                    if (all.Count > 0) evs.Add(new PredictEvent { Kind = "void", Title = m.Title, Market = m, Amount = cost });
                    Record(day, m, 2, 0, all.Count > 0);
                    continue;
                }
                m.Outcome = res.Value ? 1 : 0;
                string sk = m.Kind.ToString();
                if (!Stats.TryGetValue(sk, out var st)) Stats[sk] = st = new PredictStat();
                st.Count++; st.SumP0 += m.P0; if (res.Value) st.Hits++;
                var winners = all.Where(x => x.Yes == res.Value).ToList();
                // 操纵：行情盘上，赢家的方向和玩家推价的方向一致时按推价幅度被查——本金和收益都没收，收益的一半记罪证
                if (m.Kind == PredictKind.Price && winners.Count > 0 && winners.Any(x => x.Yes ? push > 0 : push < 0) && rng.Chance(Math.Min(1, Market.ManipChance(push) * StoryMods.Current.Enforce(day))))
                {
                    double profitM = winners.Sum(x => x.Shares) - cost;
                    evs.Add(new PredictEvent { Kind = "manip", Title = m.Title, Market = m, Amount = cost, Fraud = Math.Min(Tuning.PredictFraudCap, Math.Max(0, profitM) * Tuning.PredictFraudShare) });
                    Record(day, m, m.Outcome, -cost, true);
                    continue;
                }
                if (all.Count > 0 && winners.Count == 0)
                {
                    evs.Add(new PredictEvent { Kind = "lose", Title = m.Title, Market = m, Amount = cost });
                    Record(day, m, m.Outcome, -cost, true);
                }
                else if (winners.Count > 0)
                {
                    double payout = winners.Sum(x => x.Shares), profit = payout - cost;
                    // 内幕持仓按盘口合计（拆到几个钱包里也一样）
                    if (all.Sum(x => x.Insider) >= Tuning.PredictInsiderShare * m.ShareCap && rng.Chance(Math.Min(1, Tuning.PredictInsiderCatch * StoryMods.Current.Enforce(day))))
                    {
                        // 查实：本金和收益全部没收
                        evs.Add(new PredictEvent { Kind = "caught", Title = m.Title, Market = m, Amount = payout, Fraud = Math.Min(Tuning.PredictFraudCap, Math.Max(0, profit) * Tuning.PredictFraudShare) });
                        Record(day, m, m.Outcome, -cost, true);
                    }
                    else
                    {
                        foreach (var pos in winners) Pay(wallet(pos.Wallet), pos, pos.Shares);
                        evs.Add(new PredictEvent { Kind = "win", Title = m.Title, Market = m, Amount = payout });
                        Record(day, m, m.Outcome, profit, true);
                    }
                }
                else Record(day, m, m.Outcome, 0, false);
            }
            Markets.RemoveAll(m => m.Outcome >= 0);
            Tips.RemoveAll(t => Find(t.Market) == null);
            return evs;
        }

        private void Record(int day, PredictMarket m, int outcome, double pnl, bool held)
        {
            Recent.Add(new PredictRecord { Day = day, Title = m.Title, Outcome = outcome, Pnl = pnl, Held = held });
            while (Recent.Count > Tuning.PredictHistory) Recent.RemoveAt(0);
        }

        public PredictTip TipFor(int market) => Tips.Find(t => t.Market == market);

        /// <summary>造一条内幕消息：假情报（概率 IntelFake）说的与真相相反，否则按这一档的准确率说对或说错。</summary>
        public static PredictTip MakeTip(PredictMarket m, bool truth, int tier, Rng rng)
        {
            bool claim;
            if (rng.Chance(Tuning.IntelFake)) claim = !truth;
            else claim = rng.Chance(Tuning.IntelTipAcc[tier]) ? truth : !truth;
            return new PredictTip { Market = m.Id, Tier = tier, Claim = claim, Paid = Tuning.IntelTipPrice[tier] * m.Loss };
        }

        /// <summary>
        /// 夜间的消息反应：还能交易的盘口（明天仍开着）向"公开信息下的合理概率"靠拢 PredictDrift，再加 logit 噪声（NPC 流量）。
        /// 价格在夜里才反应，所以白天先看到报纸、传闻的人可以抢在前面买、等涨价再卖。target 返回 null 表示这个盘口没有新信息。
        /// </summary>
        public List<PredictMove> Drift(int day, Func<PredictMarket, double?> target, Rng rng)
        {
            var moves = new List<PredictMove>();
            foreach (var m in Markets.Where(x => x.Outcome < 0 && x.FromDay > day + 1))
            {
                m.PrevP = m.PYes;
                double p = m.PYes, t = target(m) ?? p;
                t = Math.Max(Tuning.PredictPMin, Math.Min(Tuning.PredictPMax, t));
                double pt = p + Tuning.PredictDrift * (t - p);
                double lp = Math.Log(pt / (1 - pt)) + Tuning.PredictNoise * rng.Gauss();
                pt = Math.Max(Tuning.PredictPMin, Math.Min(Tuning.PredictPMax, 1 / (1 + Math.Exp(-lp))));
                m.QYes = m.QNo + m.B * Math.Log(pt / (1 - pt));
                moves.Add(new PredictMove { Market = m, From = m.PrevP, To = m.PYes });
            }
            return moves;
        }

        /// <summary>读观测：窗口里每一天都有记录才能判断；有任何一天缺就返回 null（作废）。</summary>
        public bool? Happened(PredictMarket m)
        {
            string tag = m.Kind == PredictKind.Faction ? "F:" + m.Key : m.Key;
            bool any = false;
            for (int d = m.FromDay; d <= m.ToDay; d++)
            {
                if (!Started.TryGetValue(d, out var list)) return null;
                if (list.Contains(tag)) any = true;
            }
            return any;
        }

        /// <summary>
        /// 每天开门后补盘：开放的盘口少于下限时补到下限，之后每天再随机新开一两个，总数不超过上限。同类同键的盘口不重复开。
        /// 返回新开的盘口。
        /// </summary>
        public List<PredictMarket> Refill(int day, Rng rng, IList<PredictCandidate> candidates)
        {
            var made = new List<PredictMarket>();
            int open = Markets.Count(m => m.Outcome < 0);
            int want = open < Tuning.PredictOpenMin ? Tuning.PredictOpenMin - open : 0;
            want = Math.Max(want, rng.Range(1, Tuning.PredictNewPerDay));
            want = Math.Min(want, Tuning.PredictOpenMax - open);
            var pool = candidates.Where(c => !Markets.Any(m => m.Outcome < 0 && m.Kind == c.Kind && m.Key == c.Key)).ToList();
            while (want-- > 0 && pool.Count > 0)
            {
                var c = pool[rng.Range(0, pool.Count - 1)];
                pool.RemoveAll(x => x.Kind == c.Kind && x.Key == c.Key);
                int from = c.Tomorrow ? day + 1 : day + 1 + rng.Range(0, 2);
                int to = c.Tomorrow ? from : from + rng.Range(0, 2);
                var m = PredictMarket.Create(++Seq, c.Kind, c.Key, c.Title, c.Param, day, from, to, c.Prob(from, to));
                Markets.Add(m); made.Add(m);
            }
            return made;
        }
    }

    /// <summary>盘口先验概率：事件按游戏的掷骰规则，纯度按均匀分布，行情按本 mod 的价格过程。</summary>
    public static class PredictMath
    {
        /// <summary>某个常规事件在窗口里出现的概率：每天先过"有事件"这一关（chance），再按权重在可选事件里抽中。</summary>
        public static double EventProb(double dayChance, double odd, double totalOdd, int days)
        {
            double p1 = totalOdd > 0 ? dayChance * odd / totalOdd : 0;
            return 1 - Math.Pow(1 - p1, days);
        }

        /// <summary>某天 gas 不低于阈值的概率（已知相位）：周期值 × (1 ± 噪声) 均匀分布。</summary>
        public static double GasAtLeast(int day, int phase, double threshold)
        {
            double c = TxRules.GasCycle(day, phase), hi = c * (1 + Tuning.TxGasNoise), lo = c * (1 - Tuning.TxGasNoise);
            return Math.Max(0, Math.Min(1, (hi - threshold) / (hi - lo)));
        }

        /// <summary>不知道相位时的先验：七个相位等可能。</summary>
        public static double GasFlat(int day, double threshold)
        {
            double s = 0;
            for (int ph = 0; ph < Tuning.TxGasPeriod; ph++) s += GasAtLeast(day, ph, threshold);
            return s / Tuning.TxGasPeriod;
        }

        /// <summary>
        /// CRA 预警在 [from, to] 夜里发出的概率：每晚补贴池先被消耗，归零那晚必发，否则按日概率；
        /// 窗口之前已经发出的不算（那就是"否"）。subsidy / drain 是今天的补贴池和每晚消耗，today 是今晚。
        /// </summary>
        public static double CraWarnProb(double subsidy, double drain, int today, int from, int to, double dailyHazard)
        {
            double noWarn = 1, inWindow = 0;
            for (int d = today; d <= to; d++)
            {
                subsidy -= drain;
                double h = subsidy <= 0 ? 1 : dailyHazard;
                if (d >= from) inWindow += noWarn * h;
                noWarn *= 1 - h;
            }
            return inWindow;
        }

        /// <summary>
        /// 矿机时代在 [from, to] 开始的概率：没出预警时在 [eraMin, eraMax] 里均匀；预警已出（warnDay >= 0）时落在预警后 EraWarnMin..EraWarnMax 天。
        /// 只算还没过去的日子（today 之后）。
        /// </summary>
        public static double EraProb(int today, int from, int to, int warnDay)
        {
            int lo = warnDay >= 0 ? warnDay + Tuning.EraWarnMin : Tuning.EraMin, hi = warnDay >= 0 ? warnDay + Tuning.EraWarnMax : Tuning.EraMax;
            lo = Math.Max(lo, today + 1);
            if (hi < lo) return 0;
            int a = Math.Max(lo, from), b = Math.Min(hi, to);
            return b < a ? 0 : (b - a + 1) / (double)(hi - lo + 1);
        }

        public static double FactionProb(int days) => 1 - Math.Pow(1 - Tuning.PredictFactionDaily, days);

        /// <summary>均匀整数 lo..hi 里不低于 threshold 的概率。</summary>
        public static double PurityAtLeast(double threshold, int lo, int hi)
        {
            double n = hi - lo + 1, k = hi - Math.Ceiling(threshold) + 1;
            return Math.Max(0, Math.Min(1, k / n));
        }

        public static double Phi(double x)
        {
            double t = 1 / (1 + 0.2316419 * Math.Abs(x));
            double d = 0.3989423 * Math.Exp(-x * x / 2);
            double p = d * t * (0.3193815 + t * (-0.3565638 + t * (1.781478 + t * (-1.821256 + t * 1.330274))));
            return x > 0 ? 1 - p : p;
        }

        /// <summary>
        /// 价格在 nights 个夜晚之后（每晚向锚回归 revert、加 vol 的对数噪声，每个早上再加 vol/2 的开门波动）不低于 threshold 的概率。
        /// 对数价格近似正态；锚按今天的值外推（事件和减半的变化不计）。
        /// </summary>
        public static double PriceAtLeast(double price, double anchor, double vol, double revert, int nights, double threshold)
        {
            double m = Math.Log(price), la = Math.Log(anchor), v = 0;
            for (int i = 0; i < nights; i++)
            {
                m += revert * (la - m);
                v = (1 - revert) * (1 - revert) * v + vol * vol + vol * vol / 4;
            }
            double sd = Math.Sqrt(Math.Max(v, 1e-12));
            return 1 - Phi((Math.Log(threshold) - m) / sd);
        }
    }
}

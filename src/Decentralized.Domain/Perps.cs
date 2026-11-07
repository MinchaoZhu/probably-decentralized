using System;
using System.Collections.Generic;
using System.Linq;

namespace Decentralized.Domain
{
    /// <summary>永续合约的一个净仓位。Size 是标的数量，Margin 是保证金（CRC，信用点等值），Taint / Basis 是存入时带来的污点与成本。</summary>
    public sealed class PerpPosition
    {
        public string Product;
        public bool Long;
        public double Size, Entry, Margin, Taint, Basis;

        public double Notional(double price) => Size * price;
        public double Pnl(double price) => (Long ? 1 : -1) * Size * (price - Entry);
        public double Equity(double price) => Margin + Pnl(price);
        public double Leverage => Margin > 0 ? Size * Entry / Margin : 0;

        /// <summary>强平价：权益跌到名义价值的维持比例时。</summary>
        public double LiqPrice => Long
            ? Math.Max(0, (Size * Entry - Margin) / (Size * (1 - Tuning.PerpMaint)))
            : (Margin + Size * Entry) / (Size * (1 + Tuning.PerpMaint));
    }

    public enum PerpResult { Ok, Banned, Debt, Leverage, Size, Funds, Opposite, NoPosition, Margin, Thin, Single }

    /// <summary>夜间发生的事：强平、自动减仓、催收没收、核销。</summary>
    public sealed class PerpEvent
    {
        public string Kind;           // liq / adl / seize / writeoff / interest / wipe
        public string Product;
        public bool Long;
        public double Amount;         // 损失、减记、或欠款（信用点）；wipe 是全网爆仓的名义价值（含玩家）
        public int Count;             // wipe：爆仓人数（含玩家）
        public double Move;           // wipe：标记价的涨跌幅
    }

    /// <summary>
    /// Poinbase 永续合约（PSC-PERP、SPARK-PERP）：保证金用 Poinbase 账户里的 CRC，一个品种一个净仓位。
    /// 标记价是当日均价，强平只在夜间检查（白天插针不触发）。强平后的剩余权益进保险基金，权益为负先由基金垫付，
    /// 基金不够的缺口记成玩家的欠款。NPC 仓位随行情爆仓：大部分正常爆仓，剩余保证金进基金；行情越快穿仓越多，缺口由基金垫付。
    /// 基金见底时盈利的仓位被自动减仓（盈利打折）。
    /// 欠款每天计息，每晚先用账户里的 CRC 还；逾期没还清由接入层没收物品抵债，剩下的核销并拉黑永续账户一段时间。
    /// </summary>
    public sealed class PerpBook
    {
        public List<PerpPosition> Positions = new List<PerpPosition>();
        public double InsuranceFund = Tuning.PerpFund0;
        public double Debt;
        public int DebtSince = -1, BannedUntil = -1;
        public Dictionary<string, List<double>> Marks = new Dictionary<string, List<double>>();
        public Dictionary<string, double> Funding = new Dictionary<string, double>();
        /// <summary>资金费系数（费率 = 系数 × 标记价偏离 7 日均价的比例）。默认取配置，模拟里用来校准。</summary>
        public double FundCoef = Tuning.PerpFundCoef;
        /// <summary>成交深度倍数（滑点 = 名义价值 / (倍数 × 永续深度)），模拟里用来校准。</summary>
        public double DepthMult = Tuning.PerpDepthMult;

        /// <summary>成交价：买入（开多、平空）往上滑，卖出往下滑，滑点随名义价值占永续深度的比例增大，封顶 50%。</summary>
        public double Exec(double price, double notional, bool buy, int day)
        {
            double slip = Math.Min(0.5, notional / (DepthMult * Tuning.PerpDepth(day)));
            return price * (1 + (buy ? slip : -slip));
        }

        public static readonly string[] Products = { "PSC", "SPARK" };
        public static int MaxLeverage(string product) => product == "SPARK" ? Tuning.PerpSparkLev : Tuning.PerpPscLev;
        public static double MaxNotional(string product) => product == "SPARK" ? Tuning.PerpSparkCap : Tuning.PerpPscCap;

        public PerpPosition Of(string product) => Positions.Find(p => p.Product == product);
        public bool Banned(int day) => day <= BannedUntil;
        public double Rate(string product) => Funding.TryGetValue(product, out var r) ? r : 0;

        private static double Share(Wallet acct, double amount, Func<string, double> price)
        {
            double total = acct.Value(price);
            return total > 0 ? Math.Min(1, amount * price(Assets.CRC) / total) : 0;
        }

        /// <summary>开仓（同一时间只有一个仓位，已有仓位时只能 <see cref="AddMargin"/>）：保证金 + 手续费从账户的 CRC 里扣，污点与成本按保证金的价值比例随保证金进仓位。</summary>
        public PerpResult Open(Wallet acct, string product, bool isLong, double margin, int leverage, double price, int day, Func<string, double> pr)
        {
            if (Banned(day)) return PerpResult.Banned;
            if (Debt > 1e-9) return PerpResult.Debt;
            if (leverage < 1 || leverage > MaxLeverage(product)) return PerpResult.Leverage;
            if (margin <= 0 || price <= 0) return PerpResult.Margin;
            var pos = Of(product);
            if (pos != null && pos.Long != isLong) return PerpResult.Opposite;
            if (Positions.Count > 0) return PerpResult.Single;   // 只支持逐仓、一个仓位：有仓位后只能追加保证金
            double notional = margin * leverage, fee = notional * Tuning.PerpFee;
            double spot = price;
            price = Exec(price, notional, isLong, day);
            if ((pos?.Notional(price) ?? 0) + notional > MaxNotional(product) + 1e-9) return PerpResult.Size;
            if (acct.Avail(Assets.CRC) + 1e-9 < margin + fee) return PerpResult.Funds;
            // 滑点太大：按现价算权益已经低于强平线（再留一点余地），开了就是被强平
            double sizeNew = notional / price, sizeAll = (pos?.Size ?? 0) + sizeNew;
            double entryAll = ((pos?.Size ?? 0) * (pos?.Entry ?? 0) + sizeNew * price) / sizeAll;
            double eq0 = (pos?.Margin ?? 0) + margin + (isLong ? 1 : -1) * sizeAll * (spot - entryAll);
            if (eq0 < (Tuning.PerpMaint + Tuning.PerpOpenBuffer) * sizeAll * spot) return PerpResult.Thin;
            double share = Share(acct, margin, pr);
            double taint = acct.Taint * share, basis = acct.Basis * share;
            acct.Add(Assets.CRC, -(margin + fee)); acct.Taint -= taint; acct.Basis -= basis;
            InsuranceFund += fee * Tuning.PerpFeeToFund;
            if (pos == null) Positions.Add(pos = new PerpPosition { Product = product, Long = isLong, Entry = price });
            double size = notional / price;
            pos.Entry = (pos.Size * pos.Entry + size * price) / (pos.Size + size);
            pos.Size += size; pos.Margin += margin; pos.Taint += taint; pos.Basis += basis;
            return PerpResult.Ok;
        }

        /// <summary>追加保证金（不收手续费），把强平价推远。</summary>
        public PerpResult AddMargin(Wallet acct, string product, double amount, Func<string, double> pr)
        {
            var pos = Of(product);
            if (pos == null) return PerpResult.NoPosition;
            if (amount <= 0) return PerpResult.Margin;
            if (acct.Avail(Assets.CRC) + 1e-9 < amount) return PerpResult.Funds;
            double share = Share(acct, amount, pr);
            double taint = acct.Taint * share, basis = acct.Basis * share;
            acct.Add(Assets.CRC, -amount); acct.Taint -= taint; acct.Basis -= basis;
            pos.Margin += amount; pos.Taint += taint; pos.Basis += basis;
            return PerpResult.Ok;
        }

        /// <summary>平仓（按白天的现价）：权益减手续费回到账户，污点按剩下的比例退回，盈利是干净的新币。权益不够付时缺口按穿仓处理。返回回到账户的 CRC。</summary>
        public double Close(Wallet acct, string product, double price, int day, out double deficit)
        {
            deficit = 0;
            var pos = Of(product);
            if (pos == null) return 0;
            Positions.Remove(pos);
            price = Exec(price, pos.Notional(price), !pos.Long, day);
            double fee = pos.Notional(price) * Tuning.PerpFee;
            double payout = pos.Equity(price) - fee;
            InsuranceFund += Math.Min(fee, Math.Max(0, pos.Equity(price))) * Tuning.PerpFeeToFund;
            if (payout < 0) { deficit = -payout; Cover(deficit, day); return 0; }
            Return(acct, pos, payout);
            return payout;
        }

        private void Return(Wallet acct, PerpPosition pos, double payout)
        {
            double keep = pos.Margin > 0 ? Math.Min(1, payout / pos.Margin) : 0;
            acct.Add(Assets.CRC, payout); acct.Taint += pos.Taint * keep; acct.Basis += pos.Basis * keep;
        }

        /// <summary>穿仓缺口：保险基金先垫，不够的记成欠款。</summary>
        private double Cover(double deficit, int day)
        {
            double take = Math.Min(InsuranceFund, deficit);
            InsuranceFund -= take;
            double rest = deficit - take;
            if (rest > 1e-9) { Debt += rest; if (DebtSince < 0) DebtSince = day; }
            return rest;
        }

        /// <summary>
        /// NPC 仓位在一晚涨跌幅 move 下对保险基金的净流入（可为负），以及爆仓的名义价值。oi 是亏损一侧的持仓。
        /// 爆仓量 = oi × min(1, move / PerpNpcLiqScale)；其中穿仓的比例随涨跌幅升高，正常爆仓交出剩余保证金，穿仓的缺口由基金垫付。
        /// </summary>
        public static double NpcFlow(double move, double oi, out double liquidated)
        {
            move = Math.Abs(move);
            liquidated = oi * Math.Min(1, move / Tuning.PerpNpcLiqScale);
            double bank = Math.Min(Tuning.PerpNpcBankMax, Tuning.PerpNpcBankBase + Tuning.PerpNpcBankSlope * Math.Max(0, move - Tuning.PerpNpcBankFree));
            return liquidated * ((1 - bank) * Tuning.PerpNpcLeft - bank * move * Tuning.PerpNpcGap);
        }

        /// <summary>NPC 在某个品种上单边的持仓（信用点名义价值）。</summary>
        public static double NpcOi(string product, int day) => Tuning.PerpNpcOi * Tuning.PoolDepth(day) * (product == "SPARK" ? Tuning.SparkDepthShare : 1);

        /// <summary>
        /// 夜间结算。marks：各品种今天的标记价（日均价）。顺序：资金费 → NPC 爆仓与穿仓进出基金 → 强平 → 基金见底时自动减仓盈利仓位 →
        /// 欠款计息并用账户 CRC 偿还 → 逾期的触发没收。标记价涨跌达到 <see cref="Tuning.PerpNewsMove"/> 的品种另记一条 wipe（全网爆仓，含玩家）。
        /// </summary>
        public List<PerpEvent> Night(int day, IDictionary<string, double> marks, Wallet acct, Func<string, double> pr)
        {
            var evs = new List<PerpEvent>();
            var wipes = new Dictionary<string, PerpEvent>();
            bool adl = false;
            foreach (var kv in marks)
            {
                string product = kv.Key; double mark = kv.Value;
                if (!Marks.TryGetValue(product, out var hist)) Marks[product] = hist = new List<double>();
                double prev = hist.Count > 0 ? hist[hist.Count - 1] : mark;
                double rate = 0;
                if (hist.Count >= 3) rate = Math.Max(-Tuning.PerpFundCap, Math.Min(Tuning.PerpFundCap, FundCoef * (mark / hist.Average() - 1)));
                Funding[product] = rate;
                hist.Add(mark); if (hist.Count > 7) hist.RemoveAt(0);
                var pos = Of(product);
                if (pos != null) pos.Margin -= (pos.Long ? 1 : -1) * rate * pos.Notional(mark);
                double ret = prev > 0 ? mark / prev - 1 : 0;
                InsuranceFund += NpcFlow(ret, NpcOi(product, day), out double liq);
                if (Math.Abs(ret) >= Tuning.PerpNewsMove)
                    wipes[product] = new PerpEvent { Kind = "wipe", Product = product, Long = ret < 0, Amount = liq, Count = (int)Math.Round(liq / Tuning.PerpNpcAvgPos), Move = ret };
            }
            if (InsuranceFund < 0) { InsuranceFund = 0; adl = true; }
            foreach (var pos in Positions.ToList())
            {
                if (!marks.TryGetValue(pos.Product, out var mark)) continue;
                double eq = pos.Equity(mark), notional = pos.Notional(mark);
                if (eq >= Tuning.PerpMaint * notional) continue;
                Positions.Remove(pos);
                double rem = eq - Tuning.PerpLiqFee * notional;
                if (rem >= 0) InsuranceFund += rem; else Cover(-rem, day);
                evs.Add(new PerpEvent { Kind = "liq", Product = pos.Product, Long = pos.Long, Amount = pos.Margin });
                if (wipes.TryGetValue(pos.Product, out var wp) && wp.Long == pos.Long) { wp.Amount += notional; wp.Count++; }
            }
            evs.AddRange(wipes.Values);
            if (adl)
                foreach (var pos in Positions.ToList())
                {
                    if (!marks.TryGetValue(pos.Product, out var mark) || pos.Pnl(mark) <= 0) continue;
                    double pnl = pos.Pnl(mark), cut = pnl * Tuning.PerpAdlHaircut;
                    Positions.Remove(pos);
                    Return(acct, pos, pos.Margin + pnl - cut);
                    evs.Add(new PerpEvent { Kind = "adl", Product = pos.Product, Long = pos.Long, Amount = cut });
                }
            if (Debt > 1e-9)
            {
                double interest = Debt * Tuning.PerpDebtInterest;
                Debt += interest;
                double repay = Math.Min(Debt, acct.Avail(Assets.CRC));
                if (repay > 1e-9)
                {
                    double share = Share(acct, repay, pr);
                    double taint = acct.Taint * share, basis = acct.Basis * share;
                    acct.Add(Assets.CRC, -repay); acct.Taint -= taint; acct.Basis -= basis;
                    Debt -= repay;
                    evs.Add(new PerpEvent { Kind = "repaid", Amount = repay });
                }
                if (Debt < 1e-6) { Debt = 0; DebtSince = -1; }
                else
                {
                    evs.Add(new PerpEvent { Kind = "interest", Amount = Debt });
                    if (day - DebtSince >= Tuning.PerpDebtDays) evs.Add(new PerpEvent { Kind = "seize", Amount = Debt });
                }
            }
            return evs;
        }

        /// <summary>没收物品抵债之后：抵掉的价值从欠款里扣，还剩的核销并拉黑永续账户。返回核销的数额。</summary>
        public double ApplySeizure(double value, int day)
        {
            Debt -= value;
            double writtenOff = Math.Max(0, Debt);
            if (writtenOff > 0.5) BannedUntil = day + Tuning.PerpBanDays;
            Debt = 0; DebtSince = -1;
            return writtenOff;
        }
    }
}

using System;
using System.Collections.Generic;

namespace Decentralized.Domain
{
    /// <summary>换币客户的来意：卖币给玩家（玩家付现金收币），或者向玩家买币（玩家转币收现金）。</summary>
    public enum OtcSide { Sell, Buy }

    /// <summary>
    /// 一位换币客户的报价。Sell：客户手里的币（Qty、按现价的市值 Value、污点比例 TaintRatio）和他要的现金 Cash；Sting 是便衣（收下就记罪证，币不会到）。
    /// Buy：客户要买的币和数量，Value 是按现价的市值，Cash 是他出的现金（市价加一个随机的溢价，可能低于市价）。
    /// 玩家可以只成交一部分（<see cref="Otc.Part"/>），现金、市值按数量比例折算。
    /// </summary>
    public sealed class OtcOffer
    {
        public OtcSide Side;
        public string Asset;
        public double Qty, Value, TaintRatio;
        public int Cash;
        public bool Sting;
    }

    /// <summary>换币台的累计统计（存档）：成交额（信用点）、笔数、客户地址编号；混币器提议是否已经发过（第三阶段用）。</summary>
    public sealed class OtcDesk
    {
        public double Volume;
        public int Deals, Seq;
        public bool MixerOffered;

        public void Record(double cash) { Volume += Math.Max(0, cash); Deals++; }
    }

    /// <summary>
    /// 换币客户（场外换币台）：小偷灯切到"换币客户"后招来，一半来卖币、一半来买币。
    /// 卖币沿用原来的收黑币：帮派火并、镇压期间多是 CRT，革命军袭击上层、下层动荡期间多是 PSC，这类币全额带污点；
    /// 其他时候在 PSC、CRT、CRA 里随机，污点比例随机，现金价按污点比例打折；Ponero 兑换处开放后偶尔有人出手 Ponero（不带污点）。
    /// 买币：什么币都可能要，出价是市价加一个随机溢价，玩家自己决定成不成交；卖出的币不追查污点。两边都可以只成交一部分。
    /// </summary>
    public static class Otc
    {
        /// <summary>客户卖币时可能出手的币（没有相关事件时随机）。</summary>
        public static readonly string[] SellAssets = { Assets.PSC, Assets.CRT, Assets.CRA };
        /// <summary>客户买币时可能要的币。</summary>
        public static readonly string[] BuyAssets = { Assets.PSC, Assets.CRC, Assets.CRT, Assets.CRA, Assets.SPARK };

        public static OtcSide PickSide(Rng rng) => rng.Chance(Tuning.OtcSellShare) ? OtcSide.Sell : OtcSide.Buy;

        /// <summary>和当前事件有关的币（帮派火并、镇压 → CRT；袭击上层、下层动荡 → PSC），没有返回 null。</summary>
        public static string EventAsset(ICollection<MarketEvent> active, Rng rng)
        {
            var c = new List<string>();
            if (active.Contains(MarketEvent.GangWar) || active.Contains(MarketEvent.Crackdown)) c.Add(Assets.CRT);
            if (active.Contains(MarketEvent.RevRaidUpper) || active.Contains(MarketEvent.LowerUnrest)) c.Add(Assets.PSC);
            return c.Count == 0 ? null : c[rng.Range(0, c.Count - 1)];
        }

        /// <summary>
        /// 卖币客户的报价。有相关事件时按 OtcEventChance 出手事件里来的币（全额污点）；Ponero 兑换处开放时按 OtcPoneroChance 出手 Ponero；
        /// 否则在 PSC、CRT、CRA 里随机，污点比例在 OtcTaintMin–OtcTaintMax 之间。市值 = 房租 × 区间；现金价 = 市值 × 折扣，折扣按污点比例从 OtcCutClean 线性降到 OtcCutDirty。
        /// crackdown：镇压期间便衣更多。
        /// </summary>
        public static OtcOffer MakeSell(ICollection<MarketEvent> active, bool poneroOpen, Func<string, double> price, double rent, Rng rng, double stingMult = 1)
        {
            if (rent <= 0) return null;
            string asset = null; double ratio;
            string ev = EventAsset(active, rng);
            if (ev != null && rng.Chance(Tuning.OtcEventChance)) { asset = ev; ratio = 1; }
            else if (poneroOpen && rng.Chance(Tuning.OtcPoneroChance)) { asset = Assets.Ponero; ratio = 0; }
            else { asset = SellAssets[rng.Range(0, SellAssets.Length - 1)]; ratio = rng.Range(Tuning.OtcTaintMin, Tuning.OtcTaintMax); }
            double p = price(asset);
            if (p <= 0) return null;
            double value = rent * rng.Range(Tuning.OtcValueMinRents, Tuning.OtcValueMaxRents);
            double cut = asset == Assets.Ponero ? rng.Range(Tuning.OtcPoneroCutMin, Tuning.OtcPoneroCutMax) : CutFor(ratio);
            bool crackdown = active.Contains(MarketEvent.Crackdown);
            return new OtcOffer
            {
                Side = OtcSide.Sell, Asset = asset, Qty = value / p, Value = value, TaintRatio = ratio, Cash = (int)Math.Round(value * cut),
                Sting = rng.Chance(Math.Min(1, (Tuning.OtcSting + (crackdown ? Tuning.OtcStingCrackdown : 0)) * stingMult)),
            };
        }

        /// <summary>现金价占市值的比例：污点比例 0 时 OtcCutClean，1 时 OtcCutDirty，之间线性。</summary>
        public static double CutFor(double taintRatio) => Tuning.OtcCutClean + (Tuning.OtcCutDirty - Tuning.OtcCutClean) * Math.Max(0, Math.Min(1, taintRatio));

        /// <summary>买币客户的来意：随机一种币，市值 = 房租 × OtcBuyMinRents–OtcBuyMaxRents，出价 = 市值 × (1 + OtcBidMin–OtcBidMax 之间的随机溢价)。</summary>
        public static OtcOffer MakeBuy(Func<string, double> price, double rent, Rng rng)
        {
            if (rent <= 0) return null;
            string asset = BuyAssets[rng.Range(0, BuyAssets.Length - 1)];
            double p = price(asset);
            if (p <= 0) return null;
            double value = rent * rng.Range(Tuning.OtcBuyMinRents, Tuning.OtcBuyMaxRents);
            return new OtcOffer { Side = OtcSide.Buy, Asset = asset, Qty = value / p, Value = value, Cash = (int)Math.Round(value * (1 + rng.Range(Tuning.OtcBidMin, Tuning.OtcBidMax))) };
        }

        /// <summary>只成交 qty（不超过报价的数量）：现金、市值按数量比例折算，污点比例和便衣不变。qty 不合法返回 null。</summary>
        public static OtcOffer Part(OtcOffer o, double qty)
        {
            if (o == null || qty <= 0 || o.Qty <= 0) return null;
            double f = Math.Min(1, qty / o.Qty);
            return new OtcOffer { Side = o.Side, Asset = o.Asset, Qty = o.Qty * f, Value = o.Value * f, TaintRatio = o.TaintRatio, Cash = (int)Math.Round(o.Cash * f), Sting = o.Sting };
        }

        /// <summary>收下卖币客户的币：币进钱包，其中 数量 × 污点比例 枚是脏的。</summary>
        public static void Receive(Wallet w, OtcOffer o)
        {
            w.AddDirty(o.Asset, o.Qty, o.Qty * o.TaintRatio);
        }

        /// <summary>
        /// 客户地址的来历（写进链上账本，玩家在浏览器里能查到）：1–OtcHistoryMax 笔转入，每一笔按污点比例的概率来自"脏"的来源（混币器、黑市平台、骗局地址），否则来自 Poinbase 或 DEX。
        /// 返回 (来源地址, 数量占比) 列表；dirty 和 clean 由接入层给出地址。
        /// </summary>
        public static List<KeyValuePair<string, double>> History(OtcOffer o, IList<string> dirty, IList<string> clean, Rng rng)
        {
            var list = new List<KeyValuePair<string, double>>();
            int n = rng.Range(1, Tuning.OtcHistoryMax);
            double left = 1;
            for (int i = 0; i < n; i++)
            {
                bool isDirty = dirty.Count > 0 && rng.Chance(o.TaintRatio);
                var src = isDirty ? dirty[rng.Range(0, dirty.Count - 1)] : clean[rng.Range(0, clean.Count - 1)];
                double share = i == n - 1 ? left : left * rng.Range(0.3, 0.7);
                left -= share;
                list.Add(new KeyValuePair<string, double>(src, share));
            }
            return list;
        }
    }
}

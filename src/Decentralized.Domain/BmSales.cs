using System;
using System.Collections.Generic;
using System.Linq;

namespace Decentralized.Domain
{
    public enum SaleState { Listed, Sold, Paid, Lost, Delisted }

    /// <summary>玩家在黑市上架的一件货。货交给平台（从储藏区拿走），成交后货款托管几天再到钱包，可能带污点。</summary>
    public sealed class BmSale
    {
        public string Item;
        public string Model, FakeOf;
        public double Health;
        /// <summary>标价倍率与标价（信用点）。</summary>
        public double Mult;
        /// <summary>参考价：改价时按它重算标价。</summary>
        public long Ref;
        public int ListPrice;
        /// <summary>上架时付费的钱包地址：回款进这里。</summary>
        public string Wallet;
        public int ListedDay, PayDay;
        public SaleState State = SaleState.Listed;
        /// <summary>成交后扣掉抽成的货款（信用点）与其中带的污点。</summary>
        public int Proceeds;
        public double Taint;
        /// <summary>成交那一轮的收款币（空 = CRT，旧存档）：货款到期后以这个币付给钱包。</summary>
        public string PayAsset;
        /// <summary>终态原因：fake（买家看穿假卡退货）/ rug / delist。</summary>
        public string Why;

        /// <summary>聚合键：同一种商品（标识、型号、真伪、成色取整）算一组，同组倍率相同。</summary>
        public static string KeyOf(string item, string model, string fakeOf, double health) => item + "|" + model + "|" + fakeOf + "|" + Math.Round(health);
        public string Key => KeyOf(Item, Model, FakeOf, Health);
    }

    public sealed partial class BlackMarket
    {
        public List<BmSale> Sales = new List<BmSale>();
        /// <summary>玩家作为卖家的好评（%）：影响成交速度；卖假卡被看穿会下降，每笔成交回升一点。</summary>
        public int Rating = Tuning.BmRatingStart;

        /// <summary>期望成交天数：倍率不高于 BmSellFastMult 取 BmSellDaysFast，BmSellDay1Mult 取 BmSellDays1，BmSellMax 取 BmSellDaysMax，之间插值（快档到一档线性，一档以上指数）。</summary>
        public static double ExpectedDays(double m)
        {
            if (m <= Tuning.BmSellFastMult) return Tuning.BmSellDaysFast;
            if (m < Tuning.BmSellDay1Mult) return Tuning.BmSellDaysFast + (m - Tuning.BmSellFastMult) / (Tuning.BmSellDay1Mult - Tuning.BmSellFastMult) * (Tuning.BmSellDays1 - Tuning.BmSellDaysFast);
            return Tuning.BmSellDays1 * Math.Pow(Tuning.BmSellDaysMax / Tuning.BmSellDays1, (m - Tuning.BmSellDay1Mult) / (Tuning.BmSellMax - Tuning.BmSellDay1Mult));
        }

        private static double T(double m) => Math.Max(0, Math.Min(1, (m - Tuning.BmSellMin) / (Tuning.BmSellMax - Tuning.BmSellMin)));
        /// <summary>货款带污点的概率与（带的时候）污点占货款的比例：都随标价倍率上升。</summary>
        public static double TaintChance(double m) => Tuning.BmSaleTaintChanceMin + T(m) * (Tuning.BmSaleTaintChanceMax - Tuning.BmSaleTaintChanceMin);
        public static double TaintRatio(double m) => Tuning.BmSaleTaintRatioMin + T(m) * (Tuning.BmSaleTaintRatioMax - Tuning.BmSaleTaintRatioMin);

        /// <summary>每晚成交的概率：1 / 期望天数，乘好评系数（好评 90 为 1，封顶 1.1），最多 1。</summary>
        public double SaleChance(double m) => Math.Min(1, 1 / ExpectedDays(m) * Math.Min(1.1, Rating / (double)Tuning.BmRatingStart));

        /// <summary>近 7 天成交额推高的污点加成：出售量越大，货款越容易带污点、比例越高。</summary>
        public double VolumeTaint(int day)
        {
            double v = Sales.Where(x => (x.State == SaleState.Sold || x.State == SaleState.Paid) && day - (x.PayDay - Tuning.BmSaleHold) <= 7).Sum(x => (double)x.Proceeds);
            return Math.Min(Tuning.BmVolumeTaintMax, v / Tuning.BmVolumeRef * Tuning.BmVolumeTaintMax);
        }

        /// <summary>收款币：愿意接受的币（存档），至少一种；默认只有 CRT。</summary>
        public List<string> SellAccepts = new List<string> { Assets.CRT };
        public static readonly string[] SellCoins = { Assets.PSC, Assets.CRT, Assets.CRA, Assets.Ponero };

        public static double CoinWeight(string asset) =>
            asset == Assets.PSC ? Tuning.BmWeightPsc : asset == Assets.CRT ? Tuning.BmWeightCrt : asset == Assets.CRA ? Tuning.BmWeightCra : asset == Assets.Ponero ? Tuning.BmWeightPonero : 0;

        public IEnumerable<string> AcceptedCoins => SellAccepts.Count == 0 ? new[] { Assets.CRT } : SellAccepts.Where(a => CoinWeight(a) > 0).Distinct().ToArray();

        /// <summary>开关一种收款币；最后一种不能关掉。</summary>
        public bool ToggleCoin(string asset)
        {
            if (SellAccepts.Count == 0) SellAccepts.Add(Assets.CRT);
            if (SellAccepts.Contains(asset)) { if (SellAccepts.Count <= 1) return false; SellAccepts.Remove(asset); return true; }
            if (CoinWeight(asset) <= 0) return false;
            SellAccepts.Add(asset);
            return true;
        }

        /// <summary>在架的件数（全部）、种数、某一组的件数与倍率（没有在架返回 null）。</summary>
        public int ActiveListings => Sales.Count(x => x.State == SaleState.Listed);
        public int ActiveKinds => Sales.Where(x => x.State == SaleState.Listed).Select(x => x.Key).Distinct().Count();
        public int ActiveOf(string key) => Sales.Count(x => x.State == SaleState.Listed && x.Key == key);
        public double? MultOf(string key) { var s = Sales.FirstOrDefault(x => x.State == SaleState.Listed && x.Key == key); return s == null ? (double?)null : s.Mult; }

        /// <summary>某一组再上架 add 件、倍率 m 时的限制：null = 可以；stack = 超过每种上限；kinds = 超过种数上限；mult = 与已在架的同种货倍率不同。</summary>
        public string ListBlock(string key, double m, int add = 1)
        {
            int have = ActiveOf(key);
            if (have + add > Tuning.BmMaxStack) return "stack";
            if (have == 0 && ActiveKinds >= Tuning.BmMaxKinds) return "kinds";
            var cur = MultOf(key);
            if (cur != null && Math.Abs(cur.Value - m) > 1e-9) return "mult";
            return null;
        }

        public static int ListPriceOf(long refValue, double m) => (int)Math.Max(1, Math.Round(refValue * m));
        public static int ListFee(int listPrice) => (int)Math.Ceiling(listPrice * Tuning.BmListFee);

        /// <summary>上架：交付一件货，收上架费（付给 feeAsset 币，污点与成本按比例带走）。倍率超出范围、平台关闭或余额不足返回 null。</summary>
        public BmSale List(string item, string model, string fakeOf, double health, long refValue, double m, Wallet w, string feeAsset, int day, Func<string, double> price)
        {
            if (m < Tuning.BmSellMin - 1e-9 || m > Tuning.BmSellMax + 1e-9 || refValue <= 0 || Closed(day) || feeAsset == Assets.CRC || ListBlock(BmSale.KeyOf(item, model, fakeOf, health), m) != null) return null;
            int listPrice = ListPriceOf(refValue, m);
            double p = price(feeAsset);
            if (p <= 0) return null;
            double qty = ListFee(listPrice) / p;
            if (w.Avail(feeAsset) + 1e-9 < qty) return null;
            double total = w.Value(price);
            double share = total > 0 ? Math.Min(1, ListFee(listPrice) / total) : 0;
            w.Taint -= w.Taint * share; w.Basis -= w.Basis * share;
            w.Add(feeAsset, -qty);
            var s = new BmSale { Item = item, Model = model, FakeOf = fakeOf, Health = health, Ref = refValue, Mult = m, ListPrice = listPrice, ListedDay = day, Wallet = w.Address };
            Sales.Add(s);
            return s;
        }

        public void Delist(BmSale s) { s.State = SaleState.Delisted; s.Why = "delist"; }

        /// <summary>未到账的货款（信用点）：跑路时一并归零，也推高跑路概率。</summary>
        public double HeldSales => Sales.Where(x => x.State == SaleState.Sold).Sum(x => (double)x.Proceeds);

        /// <summary>每晚推进出售：上架的货按概率成交（假卡可能被看穿退货），成交的货款托管到期后进钱包。镇压期间平台下线，不推进。</summary>
        private void SalesNight(int day, bool crackdown, Func<string, Wallet> wallet, Func<string, double> price, Rng rng, List<BmEvent> ev)
        {
            if (crackdown) return;
            foreach (var s in Sales.Where(x => x.State == SaleState.Listed && x.ListedDay < day).ToList())
            {
                // 每接受一种币算一轮成交：轮次顺序随机，任一轮命中即成交，货款用命中的币付
                string hit = null;
                double chance = SaleChance(s.Mult);
                foreach (var c in AcceptedCoins.OrderBy(_ => rng.NextDouble()).ToList())
                    if (rng.Chance(Math.Min(1, chance * CoinWeight(c)))) { hit = c; break; }
                if (hit == null) continue;
                s.PayAsset = hit;
                if (s.FakeOf != null && rng.Chance(Tuning.BmFakeDetect))
                {
                    s.State = SaleState.Delisted; s.Why = "fake";
                    Rating = Math.Max(0, Rating - Tuning.BmRatingFakeLoss);
                    ev.Add(new BmEvent { Kind = "sale_fake", Sale = s });
                    continue;
                }
                s.State = SaleState.Sold;
                s.Proceeds = (int)Math.Round(s.ListPrice * (1 - Tuning.BmCommission));
                double vb = VolumeTaint(day);
                s.Taint = rng.Chance(Math.Min(1, TaintChance(s.Mult) + vb)) ? s.Proceeds * Math.Min(1, TaintRatio(s.Mult) + vb) : 0;
                s.PayDay = day + Tuning.BmSaleHold;
                Rating = Math.Min(100, Rating + 1);
                if (Honeypot(day)) Traps.Add(new BmTrap { Day = day + Tuning.BmTrapDelay, Amount = s.Proceeds, Why = "honeypot", Item = s.Item, Group = HoneypotStart });
                ev.Add(new BmEvent { Kind = "sale_sold", Sale = s, Amount = s.Proceeds });
            }
            foreach (var s in Sales.Where(x => x.State == SaleState.Sold && day >= x.PayDay).ToList())
            {
                string pa = s.PayAsset ?? Assets.CRT;
                double p = price(pa);
                if (p <= 0) continue;
                var w = wallet(s.Wallet);
                w.Add(pa, s.Proceeds / p);
                w.Taint += s.Taint;
                w.Basis += s.Proceeds;
                s.State = SaleState.Paid;
                ev.Add(new BmEvent { Kind = "sale_paid", Sale = s, Amount = s.Proceeds });
            }
            // 买家落网：近期到账的出售记录会连累卖家（玩家）
            var recent = Sales.Where(x => x.State == SaleState.Paid && day - x.PayDay <= 14).ToList();
            if (recent.Count > 0 && rng.Chance(Tuning.BmSellerBust))
            {
                foreach (var s in recent) Traps.Add(new BmTrap { Day = day + Tuning.BmTrapDelay, Amount = s.Proceeds, Why = "seller", Item = s.Item, Group = -day });
                Forum.Add("busted_buyer");
            }
            Sales.RemoveAll(x => (x.State == SaleState.Paid || x.State == SaleState.Delisted || x.State == SaleState.Lost) && day - Math.Max(x.ListedDay, x.PayDay) > 14);
        }

    }
}

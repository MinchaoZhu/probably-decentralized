using System;
using System.Collections.Generic;
using System.Linq;

namespace Decentralized.Domain
{
    /// <summary>货的种类：违禁品、高科技物品、显卡。</summary>
    public enum GoodKind { Contraband, Tech, Gpu, Goods }

    /// <summary>黑市货架上的一种货。显卡的 Item 是 "gpu:型号"，Model 是标称型号，FakeOf 非空表示背后是别的芯片（买家看不到）。</summary>
    public class BmGood
    {
        public string Item;
        public GoodKind Kind;
        public int Level;
        public int Price;
        public string Model, FakeOf;
        public double Health;
    }

    /// <summary>现货柜台上的一件货：剩余件数、含溢价的单价（信用点）。</summary>
    public sealed class BmOffer : BmGood
    {
        public int Qty;
    }

    /// <summary>
    /// Web3 黑市：黑市派系在链上的实体。货架上主要是违禁品（档次随黑市声望放开），偶尔有高科技物品和显卡（加价），
    /// 现货只收币，脏币也收，付出去的币连同按比例的污点一起留在卖家那边；Ponero 兑换处把任意币兑成无法追踪的 Ponero（这部分价值的污点被清除），有每日额度和手续费。
    /// 镇压期间整个平台下线。
    /// </summary>
    public sealed partial class BlackMarket
    {
        public List<BmOffer> Offers = new List<BmOffer>();
        public int RefreshedDay = -999;
        /// <summary>Ponero 兑换处当天已兑换的价值（信用点）及其日期。</summary>
        public double DeskToday;
        public int DeskDay = -1;

        /// <summary>违禁品池：游戏物品 id 与违禁等级（1 低、2 中、3 高）。都是能转手的货（枪械、钥匙卡、密码解序器芯片、爆炸物）；弹药、毒品、药品玩家用不上，不上架。</summary>
        public static readonly KeyValuePair<string, int>[] Pool =
        {
            P("handmade_pistol", 1), P("printed_gun", 1), P("stun_baton", 1),
            P("eng_keycard", 1), P("med_keycard", 1), P("ser_keycard", 1), P("sup_keycard", 1), P("crypto_module_ser", 1), P("crypto_module_sup", 1),
            P("revolver", 2), P("heavy_handmade_pistol", 2), P("flashbang_grenade", 2), P("smoke_grenade", 2),
            P("sci_keycard", 2), P("sec_keycard", 2), P("crypto_module_eng", 2), P("crypto_module_med", 2),
            P("shotgun", 3), P("smg", 3), P("c4", 3), P("cmd_keycard", 3), P("crypto_module_sec", 3),
        };

        /// <summary>高科技物品池（不是违禁品，黑市上要加价）：矿机能用上的系统模组、能量电池、能量阵列，和矿架本身。</summary>
        public static readonly string[] TechPool =
        {
            "system_module_overclock", "system_module_performance", "system_module_quality", "system_module_eco", "system_module_efficiancy",
            "system_module_fineness", "system_module_streamlining", "energy_credit_ext", "energy_credit_hyp", "energy_credit_breeder",
            "powerblock", "module_extractor_advanced", "pd_rig",
        };

        private static KeyValuePair<string, int> P(string id, int level) => new KeyValuePair<string, int>(id, level);

        public static bool Open(int rep) => rep >= Tuning.BmOpenRep;
        public static int MaxLevel(int rep) => rep >= Tuning.BmHighRep ? 3 : rep >= Tuning.BmMidRep ? 2 : rep >= Tuning.BmOpenRep ? 1 : 0;
        public static bool DeskOpen(int rep) => rep >= Tuning.PoneroOpenRep;
        public static int StockCount(int rep) => Tuning.BmStockBase + (int)Math.Max(0, rep / Tuning.BmStockPerRep);

        /// <summary>货的加价：违禁品 0，高科技、显卡各有额外加价（在现货溢价 / 订单折扣之外）。</summary>
        public static double Extra(GoodKind k) => k == GoodKind.Tech ? Tuning.BmTechMarkup : k == GoodKind.Gpu ? Tuning.BmGpuMarkup : 0;

        /// <summary>现货单价（信用点）：基础价 ×（1 + 溢价 + 货的加价）。</summary>
        public static int PriceOf(long baseValue, GoodKind k = GoodKind.Contraband) => (int)Math.Max(1, Math.Round(baseValue * (1 + Tuning.BmPremium + Extra(k))));

        /// <summary>抽 count 种货：高科技（中档声望起）和显卡按概率换掉违禁品。value 给出游戏物品的基础价；显卡的基础价按标称型号与成色算。</summary>
        private List<BmGood> Roll(int count, int day, int rep, Rng rng, Func<string, long> value, Func<long, GoodKind, int> price)
        {
            var goods = new List<BmGood>();
            var pool = Pool.Where(x => x.Value <= MaxLevel(rep)).ToList();
            var tech = TechPool.ToList();
            var gpus = UsedDealers.Pool(day);
            for (int i = 0; i < count; i++)
            {
                double r = rng.NextDouble();
                if (r < Tuning.BmGpuChance)
                {
                    bool fresh = rng.Chance(Tuning.BmGpuNew);
                    var c = UsedDealers.Card(gpus, fresh ? 0 : Tuning.BmGpuFake, 0, rng);
                    if (fresh) c.Health = 100;
                    goods.Add(new BmGood { Item = "gpu:" + c.Model, Kind = GoodKind.Gpu, Level = 2, Model = c.Model, FakeOf = c.FakeOf, Health = c.Health, Price = price(CardValue.Of(c.Model, c.Health), GoodKind.Gpu) });
                }
                else if (r < Tuning.BmGpuChance + Tuning.BmTechChance && tech.Count > 0)
                {
                    int k = rng.Range(0, tech.Count - 1);
                    string id = tech[k];
                    tech.RemoveAt(k);
                    long v = value(id);
                    if (v > 0) goods.Add(new BmGood { Item = id, Kind = GoodKind.Tech, Level = 3, Price = price(v, GoodKind.Tech) });
                }
                else if (pool.Count > 0)
                {
                    int k = rng.Range(0, pool.Count - 1);
                    var pick = pool[k];
                    pool.RemoveAt(k);
                    long v = value(pick.Key);
                    if (v > 0) goods.Add(new BmGood { Item = pick.Key, Kind = GoodKind.Contraband, Level = pick.Value, Price = price(v, GoodKind.Contraband) });
                }
            }
            return goods;
        }

        /// <summary>每周刷新一次（或首次开放时）：按声望开放的档次抽 StockCount 种货，每种 1–3 件（高档与显卡、高科技 1 件）。</summary>
        public bool Refresh(int day, int rep, Rng rng, Func<string, long> value)
        {
            if (!Open(rep) || day - RefreshedDay < Tuning.BmRefreshDays) return false;
            RefreshedDay = day;
            Offers.Clear();
            foreach (var g in Roll(StockCount(rep), day, rep, rng, value, PriceOf))
                Offers.Add(new BmOffer { Item = g.Item, Kind = g.Kind, Level = g.Level, Price = g.Price, Model = g.Model, FakeOf = g.FakeOf, Health = g.Health, Qty = g.Kind != GoodKind.Contraband || g.Level == 3 ? 1 : rng.Range(1, Tuning.BmSpotQtyMax) });
            return true;
        }

        /// <summary>买一件：付 asset 币。钱包里按价值比例带走的污点和成本留在卖家那边。成功返回 true，库存减一。</summary>
        public bool Buy(BmOffer o, Wallet w, string asset, Func<string, double> price)
        {
            if (o == null || o.Qty <= 0 || asset == Assets.CRC) return false;
            double p = price(asset);
            if (p <= 0) return false;
            double qty = o.Price / p;
            if (w.Avail(asset) + 1e-9 < qty) return false;
            double total = w.Value(price);
            double share = total > 0 ? Math.Min(1, o.Price / total) : 0;
            w.Taint -= w.Taint * share;
            w.Basis -= w.Basis * share;
            w.Add(asset, -qty);
            o.Qty--;
            if (o.Qty <= 0) Offers.Remove(o);
            return true;
        }

        /// <summary>Ponero 对信用点的价格：跟随 PSC。</summary>
        public static double PoneroPrice(double pscPrice) => pscPrice * Tuning.PoneroPscRatio;

        public double DeskLeft(int day) => Math.Max(0, Tuning.PoneroDailyCap - (DeskDay == day ? DeskToday : 0));

        /// <summary>兑入 Ponero：from 币 amount 个，扣手续费后按 Ponero 价到账；这部分价值带的污点被清除（不跟着 Ponero）。返回到账数量，额度或余额不足返回 -1。</summary>
        public double ToPonero(Wallet w, string from, double amount, int day, Func<string, double> price)
        {
            double value = amount * price(from);
            if (from == Assets.Ponero || amount <= 0 || value <= 0 || w.Avail(from) + 1e-9 < amount || value > DeskLeft(day) + 1e-9) return -1;
            double total = w.Value(price);
            double share = total > 0 ? Math.Min(1, value / total) : 0;
            w.Taint -= w.Taint * share;
            w.Add(from, -amount);
            double got = value * (1 - Tuning.PoneroFee) / price(Assets.Ponero);
            w.Add(Assets.Ponero, got);
            w.RecordBuy(Assets.Ponero, got, value);
            Spend(day, value);
            return got;
        }

        /// <summary>Ponero 兑出成 to 币：手续费同上，计入当天额度。返回到账数量，不足返回 -1。</summary>
        public double FromPonero(Wallet w, string to, double amount, int day, Func<string, double> price)
        {
            double value = amount * price(Assets.Ponero);
            if (to == Assets.Ponero || amount <= 0 || value <= 0 || w.Avail(Assets.Ponero) + 1e-9 < amount || value > DeskLeft(day) + 1e-9 || price(to) <= 0) return -1;
            w.Add(Assets.Ponero, -amount);
            double got = value * (1 - Tuning.PoneroFee) / price(to);
            w.Add(to, got);
            w.RecordBuy(to, got, value);
            Spend(day, value);
            return got;
        }

        private void Spend(int day, double value)
        {
            if (DeskDay != day) { DeskDay = day; DeskToday = 0; }
            DeskToday += value;
        }
    }
}

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
    /// 现货只收币，脏币也收，付出去的币连同按比例的污点一起留在卖家那边；Ponero 兑换处把任意币兑成无法追踪的 Ponero（这部分价值的污点被清除），收手续费，兑换量越大价格越差。
    /// 镇压期间整个平台下线。
    /// </summary>
    public sealed partial class BlackMarket
    {
        public List<BmOffer> Offers = new List<BmOffer>();
        public int RefreshedDay = -999;
        /// <summary>Ponero 兑换处累计兑换量（信用点，每晚按 <see cref="Tuning.PoneroUsedKeep"/> 衰减）及其记录日期。</summary>
        public double DeskUsed;
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

        /// <summary>最近一个该上新的夜晚（不晚于 day）：夜间结算时 day 还是当天，第二天早上才看到货，所以周一的货在周日夜里上。</summary>
        public static int LastRefreshNight(int day)
        {
            for (int d = day; d > day - 7; d--)
                if (Tuning.BmRefreshWeekdays.Contains((d + 1) % 7)) return d;
            return day;
        }

        /// <summary>到了上新日（或首次开放时，错过的上新日在下一晚补上）：按声望开放的档次抽 StockCount 种货，每种 1–3 件（高档与显卡、高科技 1 件）。</summary>
        public bool Refresh(int day, int rep, Rng rng, Func<string, long> value)
        {
            if (!Open(rep) || RefreshedDay >= LastRefreshNight(day)) return false;
            RefreshedDay = day;
            Offers.Clear();
            foreach (var g in Roll(StockCount(rep), day, rep, rng, value, PriceOf))
                Offers.Add(new BmOffer { Item = g.Item, Kind = g.Kind, Level = g.Level, Price = g.Price, Model = g.Model, FakeOf = g.FakeOf, Health = g.Health, Qty = g.Kind != GoodKind.Contraband || g.Level == 3 ? 1 : rng.Range(1, Tuning.BmSpotQtyMax) });
            return true;
        }

        /// <summary>买一件：付 asset 币。付出的币按它的脏比例带走脏枚数，成本按价值比例留在卖家那边。成功返回 true，库存减一。</summary>
        public bool Buy(BmOffer o, Wallet w, string asset, Func<string, double> price)
        {
            if (o == null || o.Qty <= 0 || asset == Assets.CRC) return false;
            double p = price(asset);
            if (p <= 0) return false;
            double qty = o.Price / p;
            if (w.Avail(asset) + 1e-9 < qty) return false;
            w.Add(asset, -qty);
            o.Qty--;
            if (o.Qty <= 0) Offers.Remove(o);
            return true;
        }

        /// <summary>夜间 NPC 买家：现货和商家货架上每件货各按 <see cref="Tuning.BmNpcBuyChance"/> 被买走，卖完下架。返回卖出的件数。</summary>
        public int NpcBuys(Rng rng)
        {
            int total = 0;
            foreach (var o in Offers) { int n = NpcSold(o.Qty, rng); o.Qty -= n; total += n; }
            foreach (var l in Catalog) { int n = NpcSold(l.Qty, rng); l.Qty -= n; total += n; }
            Offers.RemoveAll(o => o.Qty <= 0);
            Catalog.RemoveAll(l => l.Qty <= 0);
            return total;
        }

        private static int NpcSold(int qty, Rng rng)
        {
            int n = 0;
            for (int i = 0; i < qty; i++) if (rng.Chance(Tuning.BmNpcBuyChance)) n++;
            return n;
        }

        /// <summary>Ponero 对信用点的价格：跟随 PSC。</summary>
        public static double PoneroPrice(double pscPrice) => pscPrice * Tuning.PoneroPscRatio;

        /// <summary>
        /// 兑换处的报价：Ponero 按当天开盘价折算（玩家盘中拉盘影响不到）。PSC 一边取开盘价和现价里对玩家不利的那个，插针时不能拿开盘价套利。其他币按现价。
        /// into：true 是兑入 Ponero（PSC 是付出的币），false 是兑出（PSC 是得到的币）。
        /// </summary>
        public static double DeskQuote(string asset, double spot, double open, bool into)
        {
            if (asset == Assets.Ponero) return PoneroPrice(open);
            if (asset == Assets.PSC) return into ? Math.Min(spot, open) : Math.Max(spot, open);
            return spot;
        }

        /// <summary>累计兑换量（信用点）：按记录之后过去的晚数衰减。</summary>
        public double DeskUsedNow(int day) => DeskDay < 0 ? 0 : DeskUsed * Math.Pow(Tuning.PoneroUsedKeep, Math.Max(0, day - DeskDay));

        /// <summary>手续费率：基础费率，付出的币带脏币时加上脏币附加费。</summary>
        public static double DeskFeeRate(double dirtyRatio) => Tuning.PoneroFee + (dirtyRatio >= Tuning.PoneroDirtyMin ? Tuning.PoneroDirtyFee : 0);

        /// <summary>一笔兑换的总损耗比例（手续费和流动性）：累计兑换量相对深度越大，价格越差，到账因子 1 / (1 + (累计 + 本笔 / 2) / 深度)。</summary>
        public double DeskLoss(double value, double dirtyRatio, int day) => 1 - (1 - DeskFeeRate(dirtyRatio)) / (1 + (DeskUsedNow(day) + value / 2) / Tuning.PoneroDepth);

        /// <summary>兑换处的到账数量。</summary>
        public double DeskOut(double amount, double fromPrice, double toPrice, double dirtyRatio, int day) => amount * fromPrice * (1 - DeskLoss(amount * fromPrice, dirtyRatio, day)) / toPrice;

        /// <summary>兑入 Ponero：from 币 amount 个，扣损耗后按 Ponero 价到账；兑掉的币连同其中的脏枚数一起消失，换来的 Ponero 是干净的。返回到账数量，余额不足或数量无效返回 -1。</summary>
        public double ToPonero(Wallet w, string from, double amount, int day, Func<string, double> price)
        {
            double value = amount * price(from);
            if (from == Assets.Ponero || amount <= 0 || value <= 0 || w.Avail(from) + 1e-9 < amount) return -1;
            double got = DeskOut(amount, price(from), price(Assets.Ponero), w.DirtyRatio(from), day);
            w.Add(from, -amount);
            w.Add(Assets.Ponero, got);
            w.RecordBuy(Assets.Ponero, got, value);
            Spend(day, value);
            return got;
        }

        /// <summary>Ponero 兑出成 to 币：Ponero 是干净的，只收基础手续费和流动性损耗。返回到账数量，不足返回 -1。</summary>
        public double FromPonero(Wallet w, string to, double amount, int day, Func<string, double> price)
        {
            double value = amount * price(Assets.Ponero);
            if (to == Assets.Ponero || amount <= 0 || value <= 0 || w.Avail(Assets.Ponero) + 1e-9 < amount || price(to) <= 0) return -1;
            double got = DeskOut(amount, price(Assets.Ponero), price(to), 0, day);
            w.Add(Assets.Ponero, -amount);
            w.Add(to, got);
            w.RecordBuy(to, got, value);
            Spend(day, value);
            return got;
        }

        private void Spend(int day, double value)
        {
            DeskUsed = DeskUsedNow(day) + value;
            DeskDay = day;
        }
    }
}

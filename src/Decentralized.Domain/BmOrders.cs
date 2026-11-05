using System;
using System.Collections.Generic;
using System.Linq;

namespace Decentralized.Domain
{
    public enum PayMode { Escrow, Advance }
    /// <summary>商家类型：矿卡（全新 / 二手）、矿架能源、模组、军火、枪匠配件、证件芯片、药物、化学、酿酒、水培、养殖、矿工、水务、仓储、工具、奢侈品；大宗商家、尖端店铺（只卖贵货，概率低）、个人卖家（什么都卖，价格随心所欲）随机出现。</summary>
    public enum MerchantType { GpuNew, GpuUsed, Rigs, Modules, Arms, GunMods, Docs, Pharma, Chem, Brewer, Farm, Husbandry, Miner, Water, Storage, Tools, Luxury, Bulk, Elite, Personal, Asic }
    public enum OrderState { Pending, Ready, Done, Refunded, Lost }

    /// <summary>黑市上的卖家：成交数与好评率公开；是不是骗子是隐藏的。</summary>
    public sealed class BmSeller
    {
        public string Name;
        public int Trades;
        public int Rating;   // 好评率 %
        public bool Scam;
        public MerchantType Type;
        /// <summary>商家的价格系数（订单价 = 基础价 × 系数，各家不同）。</summary>
        public double Factor;
        public int Days => Type == MerchantType.Bulk ? Tuning.BmBulkDays : Type == MerchantType.Elite ? Tuning.BmEliteDays : Tuning.BmDoorDays;
        /// <summary>只有成交数够多的卖家才接受提前放款。</summary>
        public bool Advance => Trades >= Tuning.BmAdvanceMinTrades;
    }

    /// <summary>商家货架上的一种货：卖家、货、单价（订单价）、库存。</summary>
    public sealed class BmListing : BmGood
    {
        public int Seller;
        public int Qty;
    }

    public sealed class BmOrder
    {
        public int Id;
        public string Item;
        public string Seller;
        public bool SellerScam;
        public PayMode Mode;
        public int Count = 1;
        public string Model, FakeOf;
        public double Health;
        public int Price;
        public string Asset;
        public double Qty;
        /// <summary>付款的钱包地址：退款回这里。</summary>
        public string Wallet;
        /// <summary>付款时留在卖家那边的污点与成本：退款时一并还回，免得"付脏币再退款"洗钱。</summary>
        public double Taint, Basis;
        public int PlacedDay, DueDay;
        /// <summary>被截获后执法选择了上门：到货时快递员是执法人员，签收即记罪证。</summary>
        public bool Controlled;
        public OrderState State = OrderState.Pending;
        /// <summary>终态的原因：done / scam / rug / intercept / theft / refund。</summary>
        public string Why;
    }

    /// <summary>一笔会被追查的交易：到期日、金额（信用点）、来源（honeypot / seller / controlled）。</summary>
    public sealed class BmTrap
    {
        public int Day;
        public double Amount;
        public string Why;
        public string Item;
        /// <summary>对应的订单（没收到货的订单不会被追查）；0 表示不挂订单（出售记录、落网）。</summary>
        public int OrderId;
        /// <summary>同一次蜜罐 / 同一次落网的追查归为一组，累计记罪证有上限。</summary>
        public int Group;
    }

    /// <summary>夜间发生的订单事件，供日志与新闻使用。</summary>
    public sealed class BmEvent
    {
        public string Kind;   // ready / scam_refund / scam_loss / rug / intercept / reopen
        public BmOrder Order;
        public BmSale Sale;
        public double Amount;
    }

    public sealed partial class BlackMarket
    {
        public List<BmSeller> Sellers = new List<BmSeller>();
        public List<BmListing> Catalog = new List<BmListing>();
        public List<BmOrder> Orders = new List<BmOrder>();
        public List<string> Forum = new List<string>();
        /// <summary>平台跑路后关闭到这一天（含）。</summary>
        public int ClosedUntil = -1;
        /// <summary>执法接管平台的最后一天（含）；期间的交易之后会被追查。</summary>
        public int HoneypotUntil = -1;
        public int HoneypotStart = -1;
        /// <summary>每组追查已经记过的罪证。</summary>
        public Dictionary<int, double> Charged = new Dictionary<int, double>();
        public List<BmTrap> Traps = new List<BmTrap>();
        public int NextOrderId = 1;

        /// <summary>订单没有交付（退款、被截获、骗子、平台跑路、拒签、被查封）：对应的追查一并取消。</summary>
        private void DropTraps(BmOrder o) => Traps.RemoveAll(t => t.OrderId == o.Id && o.Id != 0);

        public bool Honeypot(int day) => day <= HoneypotUntil;

        public static double HoneypotChance(int secRep) => Math.Min(Tuning.BmHoneypotMax, Tuning.BmHoneypotBase + Math.Max(0, secRep) * Tuning.BmHoneypotPerSecRep);


        public bool Closed(int day) => day <= ClosedUntil;

        /// <summary>托管中和已提前放款但尚未交付的资金（信用点），平台跑路的概率随它增加。</summary>
        public double Held => Orders.Where(o => o.State == OrderState.Pending).Sum(o => (double)o.Price) + HeldSales;

        /// <summary>批量折扣：一次订 5 件以上 95 折，10 件以上 9 折。</summary>
        public static double BulkDiscount(int count) => count >= Tuning.BmBulk2Count ? Tuning.BmBulk2Discount : count >= Tuning.BmBulk1Count ? Tuning.BmBulk1Discount : 0;
        public static int OrderTotal(int unit, int count) => (int)Math.Max(1, Math.Round(unit * count * (1 - BulkDiscount(count))));
        public static int EscrowFee(int price) => (int)Math.Ceiling(price * Tuning.BmEscrowFee);
        /// <summary>下单实付（信用点）：托管 = 订单价 + 托管费；提前放款 = 订单价打折。</summary>
        public static int Pay(int price, PayMode m) => m == PayMode.Escrow ? price + EscrowFee(price) : (int)Math.Round(price * (1 - Tuning.BmAdvanceDiscount));

        public double RugChance => Scales.R(Math.Min(Tuning.BmRugMax, Tuning.BmRugBase + Held / 10000.0 * Tuning.BmRugPerTenK));

        /// <summary>每周必有的"矿工路线"商家类型（从中抽 2 家），其余按全部类型抽。</summary>
        private static readonly MerchantType[] Core = { MerchantType.GpuNew, MerchantType.GpuUsed, MerchantType.Rigs, MerchantType.Modules };
        private static readonly MerchantType[] Others = { MerchantType.Arms, MerchantType.GunMods, MerchantType.Docs, MerchantType.Pharma, MerchantType.Chem, MerchantType.Brewer,
            MerchantType.Farm, MerchantType.Husbandry, MerchantType.Miner, MerchantType.Water, MerchantType.Storage, MerchantType.Tools, MerchantType.Luxury };

        private static double F(Rng rng, double lo, double hi) => lo + rng.NextDouble() * (hi - lo);

        private static List<BmEntry> AllEntries() => Merchants.Table.Values.SelectMany(x => x).GroupBy(x => x.Id).Select(g => g.First()).ToList();

        private static string[] HighGpus => Hardware.Specs.Values.Where(x => !x.Asic && !x.Scrap && x.Tier >= Tuning.BmEliteGpuTier).Select(x => x.Model).ToArray();

        /// <summary>商家类型对应的货（含货种、库存），违禁品按声望放开等级。个人卖家什么都卖、每件各自定价；尖端店铺只卖贵货。</summary>
        private List<BmListing> MerchantGoods(int idx, BmSeller s, int day, int rep, Rng rng, Func<string, long> value)
        {
            var list = new List<BmListing>();
            if (s.Type == MerchantType.Asic) return AsicGoods(idx, s, rep, rng);
            bool gpuKind = s.Type == MerchantType.GpuNew || s.Type == MerchantType.GpuUsed;
            int n = s.Type == MerchantType.Bulk ? 2 : s.Type == MerchantType.Elite ? Tuning.BmEliteListings : s.Type == MerchantType.Personal ? Tuning.BmPersonalListings : Tuning.BmMerchantListings;
            List<BmEntry> entries = gpuKind ? new List<BmEntry>() : s.Type == MerchantType.Bulk || s.Type == MerchantType.Personal || s.Type == MerchantType.Elite ? AllEntries() : Merchants.Table[s.Type].ToList();
            entries = entries.Where(e => e.Kind != GoodKind.Contraband || e.Level <= MaxLevel(rep)).ToList();
            var gpus = UsedDealers.Pool(day);
            for (int i = 0; i < n; i++)
            {
                int qty = s.Type == MerchantType.Bulk ? rng.Range(Tuning.BmBulkQtyMin, Tuning.BmBulkQtyMax)
                    : s.Type == MerchantType.Elite ? rng.Range(1, Tuning.BmEliteQtyMax)
                    : s.Type == MerchantType.Personal ? rng.Range(1, Tuning.BmPersonalQtyMax)
                    : s.Type == MerchantType.GpuNew ? rng.Range(Math.Max(1, Tuning.BmQtyMin - Tuning.BmNewGpuQtyMinCut), Tuning.BmQtyMax - Tuning.BmNewGpuQtyMaxCut) : rng.Range(Tuning.BmQtyMin, Tuning.BmQtyMax);
                double factor = s.Type == MerchantType.Personal ? F(rng, Tuning.BmPersonalFactorMin, Tuning.BmPersonalFactorMax) : s.Factor * F(rng, 1 - Tuning.BmPriceJitter, 1 + Tuning.BmPriceJitter);
                BmListing l = null;
                bool eliteGpu = s.Type == MerchantType.Elite && rng.Chance(Tuning.BmEliteGpuChance);
                bool personalGpu = s.Type == MerchantType.Personal && rng.Chance(Tuning.BmPersonalGpuChance);
                if (gpuKind || eliteGpu || personalGpu)
                {
                    bool fresh = s.Type == MerchantType.GpuNew || eliteGpu || (personalGpu && rng.Chance(Tuning.BmPersonalGpuNew));
                    var pool = eliteGpu ? HighGpus : gpus;
                    var c = UsedDealers.Card(pool, fresh ? 0 : Tuning.BmGpuFake, 0, rng);
                    if (fresh) c.Health = 100;
                    l = new BmListing { Item = "gpu:" + c.Model, Kind = GoodKind.Gpu, Level = 2, Model = c.Model, FakeOf = c.FakeOf, Health = c.Health, Price = (int)Math.Max(1, Math.Round(CardValue.Of(c.Model, c.Health) * factor)) };
                }
                else
                {
                    bool wantElite = s.Type == MerchantType.Elite || (s.Type == MerchantType.Personal && rng.Chance(Tuning.BmPersonalEliteShare));
                    BmEntry pick = default; long v = 0; bool found = false;
                    for (int tries = 0; tries < 40 && entries.Count > 0 && !found; tries++)
                    {
                        int k = rng.Range(0, entries.Count - 1);
                        var e = entries[k];
                        long val = value(e.Id);
                        if (val <= 0) { entries.RemoveAt(k); continue; }
                        bool ok = s.Type == MerchantType.Elite ? val >= Tuning.BmEliteMinValue : wantElite ? val > Tuning.BmMaxUnitValue : val <= Tuning.BmMaxUnitValue;
                        if (!ok) continue;
                        pick = e; v = val; found = true; entries.RemoveAt(k);
                    }
                    if (!found) continue;
                    l = new BmListing { Item = pick.Id, Kind = pick.Kind, Level = pick.Level, Price = (int)Math.Max(1, Math.Round(v * factor)) };
                }
                l.Seller = idx; l.Qty = qty;
                list.Add(l);
            }
            return list;
        }

        public static int AsicRep(string model) => model == "M3" ? Tuning.AsicRepM3 : model == "M2" ? Tuning.AsicRepM2 : Tuning.AsicRepM1;
        private static double AsicWeight(string model) => model == "M3" ? Tuning.AsicWeightM3 : model == "M2" ? Tuning.AsicWeightM2 : Tuning.AsicWeightM1;

        /// <summary>矿机商：一台 RatDigg 矿机，型号按黑市声望放开，新机（70%）或二手；价格 = 机价（二手按健康度折算）× 商家系数。</summary>
        private List<BmListing> AsicGoods(int idx, BmSeller s, int rep, Rng rng)
        {
            var models = new[] { "M1", "M2", "M3" }.Where(m => rep >= AsicRep(m)).ToList();
            if (models.Count == 0) return new List<BmListing>();
            double roll = rng.NextDouble() * models.Sum(AsicWeight);
            string model = models.Last();
            foreach (var m in models) { roll -= AsicWeight(m); if (roll <= 0) { model = m; break; } }
            bool fresh = rng.Chance(Tuning.AsicNewShare);
            double health = fresh ? 100 : rng.Range(Tuning.AsicUsedHealthMin, Tuning.AsicUsedHealthMax);
            double factor = s.Factor * F(rng, 1 - Tuning.BmPriceJitter, 1 + Tuning.BmPriceJitter);
            return new List<BmListing>
            {
                new BmListing { Seller = idx, Item = "gpu:" + model, Kind = GoodKind.Gpu, Level = 2, Model = model, Health = health, Qty = model == "M1" ? rng.Range(1, 2) : 1,
                    Price = (int)Math.Max(1, Math.Round(CardValue.Of(model, health) * factor)) },
            };
        }

        /// <summary>每周重新生成商家、货架和论坛情报（与现货同一天刷新）：从 6 种类型里抽 BmMerchantCount 家，另有概率出现一家大宗商家。</summary>
        public void RefreshOrders(int day, int rep, Rng rng, Func<string, long> value, bool allTypes = false)
        {
            Sellers.Clear(); Catalog.Clear(); Forum.Clear();
            var types = Core.OrderBy(_ => rng.NextDouble()).Take(Tuning.BmCoreMerchants).ToList();
            types.AddRange(Others.OrderBy(_ => rng.NextDouble()).Take(Tuning.BmMerchantCount - Tuning.BmCoreMerchants));
            if (allTypes) { types = ((MerchantType[])Enum.GetValues(typeof(MerchantType))).ToList(); types.Add(MerchantType.Personal); }
            else
            {
            if (rng.Chance(Tuning.BmBulkChance)) types.Add(MerchantType.Bulk);
            if (rng.Chance(Tuning.BmEliteChance)) types.Add(MerchantType.Elite);
            if (rep >= Tuning.AsicRepM1 && rng.Chance(Tuning.AsicChance)) types.Add(MerchantType.Asic);
            for (int i = 0; i < Tuning.BmPersonalCount; i++) types.Add(MerchantType.Personal);
            }
            foreach (var t in types)
            {
                bool personal = t == MerchantType.Personal;
                bool scam = rng.Chance(personal ? Tuning.BmPersonalScam : Tuning.BmScamSellerShare);
                // 骗子多半是新号，也有刷过好评的老号；个人卖家都是新号
                bool fresh = personal || (scam ? rng.Chance(Tuning.BmScamFresh) : rng.Chance(Tuning.BmHonestFresh));
                double lo = t == MerchantType.Elite || t == MerchantType.Asic ? Tuning.BmFactorEliteMin : t == MerchantType.GpuNew ? Tuning.BmFactorNewMin : t == MerchantType.GpuUsed ? Tuning.BmFactorUsedMin : t == MerchantType.Bulk ? Tuning.BmFactorBulkMin : Tuning.BmFactorMin;
                double hi = t == MerchantType.Elite || t == MerchantType.Asic ? Tuning.BmFactorEliteMax : t == MerchantType.GpuNew ? Tuning.BmFactorNewMax : t == MerchantType.GpuUsed ? Tuning.BmFactorUsedMax : t == MerchantType.Bulk ? Tuning.BmFactorBulkMax : Tuning.BmFactorMax;
                var s = new BmSeller
                {
                    Name = ChainAddress.Make(rng.NextUInt()),
                    Scam = scam, Type = t, Factor = personal ? 1.0 : F(rng, lo, hi),
                    Trades = fresh ? rng.Range(0, Tuning.BmFreshTradesMax) : rng.Range(Tuning.BmOldTradesMin, Tuning.BmOldTradesMax),
                    Rating = scam && !fresh ? rng.Range(Tuning.BmScamOldRatingMin, Tuning.BmScamOldRatingMax) : fresh ? rng.Range(Tuning.BmFreshRatingMin, Tuning.BmFreshRatingMax) : rng.Range(Tuning.BmOldRatingMin, Tuning.BmOldRatingMax),
                };
                Sellers.Add(s);
                if (scam && rng.Chance(Tuning.BmForumScamHint)) Forum.Add("scam:" + s.Name);
            }
            for (int i = 0; i < Sellers.Count; i++) Catalog.AddRange(MerchantGoods(i, Sellers[i], day, rep, rng, value));
            if (RugChance > Scales.R(Tuning.BmRugBase * Tuning.BmForumRugFactor)) Forum.Add("rug");
        }

        /// <summary>目录里全部货按订单价算的总值（信用点），用来核对每周周转量。</summary>
        public double CatalogValue => Catalog.Sum(l => (double)l.Price * l.Qty);

        /// <summary>下单：钱包里的币按实付折算扣除，污点与成本按比例留在卖家那边（记在订单上）。成功返回订单。</summary>
        public BmOrder Place(BmListing l, int count, PayMode mode, Wallet w, string asset, int day, Func<string, double> price)
        {
            if (l == null || count < 1 || count > l.Qty || asset == Assets.CRC || Closed(day)) return null;
            var seller = Sellers[l.Seller];
            if (mode == PayMode.Advance && !seller.Advance) return null;
            double p = price(asset);
            if (p <= 0) return null;
            int credits = Pay(OrderTotal(l.Price, count), mode);
            double qty = credits / p;
            if (w.Avail(asset) + 1e-9 < qty) return null;
            double total = w.Value(price);
            double share = total > 0 ? Math.Min(1, credits / total) : 0;
            double taint = w.Taint * share, basis = w.Basis * share;
            w.Taint -= taint; w.Basis -= basis;
            w.Add(asset, -qty);
            var o = new BmOrder
            {
                Id = NextOrderId++, Item = l.Item, Seller = seller.Name, SellerScam = seller.Scam, Mode = mode, Count = count, Model = l.Model, FakeOf = l.FakeOf, Health = l.Health, Price = credits, Asset = asset, Qty = qty,
                Taint = taint, Basis = basis, PlacedDay = day, DueDay = day + seller.Days, Wallet = w.Address,
            };
            l.Qty -= count;
            if (l.Qty <= 0) Catalog.Remove(l);
            Orders.Add(o);
            if (Honeypot(day)) Traps.Add(new BmTrap { Day = day + Tuning.BmTrapDelay, Amount = credits, Why = "honeypot", Item = l.Item, OrderId = o.Id, Group = HoneypotStart });
            return o;
        }

        private static void Refund(Wallet w, BmOrder o)
        {
            w.Add(o.Asset, o.Qty);
            w.Taint += o.Taint; w.Basis += o.Basis;
        }

        /// <summary>每晚推进订单：平台跑路、到期的订单（卖家跑路、被截获、死投被挖走）、转入可取状态。镇压期间到期顺延一天。</summary>
        public List<BmEvent> Night(int day, bool crackdown, Wallet w, Rng rng, int secRep = 0, Func<string, double> price = null) => Night(day, crackdown, _ => w, rng, secRep, price);

        /// <summary>每晚（多钱包）：退款回付款的钱包，出售回款进上架时付费的钱包。</summary>
        public List<BmEvent> Night(int day, bool crackdown, Func<string, Wallet> w, Rng rng, int secRep = 0, Func<string, double> price = null)
        {
            var ev = new List<BmEvent>();
            if (!Honeypot(day) && rng.Chance(HoneypotChance(secRep))) { HoneypotUntil = day + Tuning.BmHoneypotDays; HoneypotStart = day; if (!Forum.Contains("easy")) Forum.Add("easy"); }
            if (ClosedUntil == day) ev.Add(new BmEvent { Kind = "reopen" });
            if (!Closed(day) && (Orders.Any(o => o.State == OrderState.Pending) || Sales.Any(x => x.State == SaleState.Listed || x.State == SaleState.Sold)) && rng.Chance(RugChance))
            {
                foreach (var o in Orders.Where(o => o.State == OrderState.Pending))
                {
                    o.State = OrderState.Lost; o.Why = "rug"; DropTraps(o);
                    ev.Add(new BmEvent { Kind = "rug", Order = o, Amount = o.Price });
                }
                foreach (var sl in Sales.Where(x => x.State == SaleState.Listed || x.State == SaleState.Sold))
                {
                    ev.Add(new BmEvent { Kind = "sale_rug", Sale = sl, Amount = sl.State == SaleState.Sold ? sl.Proceeds : sl.ListPrice });
                    sl.State = SaleState.Lost; sl.Why = "rug";
                }
                ClosedUntil = day + Tuning.BmRugClosedDays;
                Offers.Clear(); Catalog.Clear(); Sellers.Clear();
                return ev;
            }
            foreach (var o in Orders.Where(x => x.State == OrderState.Pending).ToList())
            {
                if (day < o.DueDay) continue;
                if (crackdown) { o.DueDay = day + 1; continue; }
                if (o.SellerScam)
                {
                    if (o.Mode == PayMode.Escrow) { Refund(w(o.Wallet), o); o.State = OrderState.Refunded; o.Why = "scam"; DropTraps(o); ev.Add(new BmEvent { Kind = "scam_refund", Order = o, Amount = o.Price }); }
                    else { o.State = OrderState.Lost; o.Why = "scam"; DropTraps(o); ev.Add(new BmEvent { Kind = "scam_loss", Order = o, Amount = o.Price }); }
                    continue;
                }
                double ic = Tuning.BmInterceptDoor + (crackdown ? Tuning.BmInterceptCrackdown : 0);
                if (rng.Chance(ic))
                {
                    if (rng.Chance(Tuning.BmControlled)) { o.Controlled = true; o.State = OrderState.Ready; ev.Add(new BmEvent { Kind = "ready", Order = o }); continue; }
                    o.State = OrderState.Lost; o.Why = "intercept"; DropTraps(o); ev.Add(new BmEvent { Kind = "intercept", Order = o, Amount = o.Price }); continue;
                }
                o.State = OrderState.Ready;
                ev.Add(new BmEvent { Kind = "ready", Order = o });
            }
            if (price != null) SalesNight(day, crackdown, w, price, rng, ev);
            // 卖家落网：近期成交过的卖家各有小概率被抓，他们留存的订单记录之后会连累买家
            foreach (var name in Orders.Where(x => x.State == OrderState.Done && day - x.DueDay <= 14).Select(x => x.Seller).Distinct().ToList())
            {
                if (!rng.Chance(Tuning.BmSellerBust)) continue;
                foreach (var o in Orders.Where(x => x.State == OrderState.Done && x.Seller == name && day - x.DueDay <= 14))
                    Traps.Add(new BmTrap { Day = day + Tuning.BmTrapDelay, Amount = o.Price, Why = "seller", Item = o.Item, Group = -day });
                Forum.Add("busted:" + name);
            }
            // 同一晚到期的追查按来源（同一次蜜罐 / 落网）合成一次事件；记入货款的一部分，同一次累计有上限
            var due = Traps.Where(x => day >= x.Day).ToList();
            foreach (var g in due.GroupBy(x => new { x.Why, x.Group }))
            {
                Charged.TryGetValue(g.Key.Group, out double done);
                double amount = Math.Min(g.Sum(x => x.Amount) * Tuning.BmTrapShare, Tuning.BmTrapWindowCap - done);
                if (amount < 1) continue;
                Charged[g.Key.Group] = done + amount;
                ev.Add(new BmEvent { Kind = "trap", Amount = amount, Order = new BmOrder { Item = g.First().Item, Seller = g.Key.Why } });
            }
            Traps.RemoveAll(x => day >= x.Day);
            Orders.RemoveAll(o => o.State != OrderState.Pending && o.State != OrderState.Ready && day - o.DueDay > 14);
            return ev;
        }

        /// <summary>受控交付：签收则当场记罪证（返回金额）并查封，不交货；拒签则损失货款，不留罪证。</summary>
        public double Sign(BmOrder o, bool accept)
        {
            o.State = OrderState.Lost;
            o.Why = accept ? "seized" : "refused";
            DropTraps(o);
            return accept ? o.Price : 0;
        }

        public void Deliver(BmOrder o) { o.State = OrderState.Done; o.Why = "done"; }

        public IEnumerable<BmOrder> Ready => Orders.Where(o => o.State == OrderState.Ready);
    }
}

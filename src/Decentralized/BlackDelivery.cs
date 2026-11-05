using System;
using System.Linq;
using Decentralized.Domain;

namespace Decentralized
{
    /// <summary>
    /// Web3 黑市的货物进出：每周刷新现货与订单目录，夜间推进订单并写夜间日志，
    /// 上门（快递员当顾客上门）把到货交给玩家。
    /// </summary>
    public static class BlackDelivery
    {
        public static void Subscribe()
        {
            ModHook.OnGenerateCustomerNormal += OnGenerate;
        }

        public static void Unsubscribe()
        {
            ModHook.OnGenerateCustomerNormal -= OnGenerate;
        }

        private static readonly System.Collections.Generic.Dictionary<string, long> ValueCache = new System.Collections.Generic.Dictionary<string, long>();

        public static long Value(string id)
        {
            if (ValueCache.TryGetValue(id, out var cached)) return cached;
            var it = ModHelper.SpawnItem(id);
            long v = 0;
            if (it != null) { v = it.GetBaseValue(); it.Destroy(); }
            ValueCache[id] = v;
            return v;
        }

        /// <summary>货架上的货对应的游戏物品 id：显卡是 "gpu:型号"，对应本 mod 的显卡物品。</summary>
        public static string GameId(string id) => id.StartsWith("gpu:") ? (Hardware.Specs.TryGetValue(id.Substring(4), out var sp) && sp.Asic ? AsicItems.Id(sp.Model) : Items.GpuId(id.Substring(4))) : id;

        /// <summary>造出这件货：显卡按成色与（可能的）假卡真身造，不带卖家的说法；其余直接造。</summary>
        public static GameItem Make(string id, string model, string fakeOf, double health)
        {
            if (id.StartsWith("gpu:")) return Hardware.Specs[model].Asic ? AsicItems.New(model, health) : Items.NewCard(model, health, fakeOf, Session.Data.Rng);
            if (id == Items.HwWallet) return Items.NewHwWallet(Session.Data.Rng.Chance(Scales.R(Tuning.WalletPoisonShare)));
            return ModHelper.SpawnItem(id);
        }

        /// <summary>商家名：随机的匿名哈希，类型另外标注。</summary>
        public static string DisplayName(string name) => name;

        public static string ItemName(string id)
        {
            var it = ModHelper.SpawnItem(GameId(id));
            if (it == null) return id;
            string n = it.name;
            it.Destroy();
            return n;
        }

        // ---- 玩家出售：从储藏区挑能上架的货 ----

        /// <summary>这件物品在黑市上的货 id（显卡是 "gpu:型号"，其余是物品 id）；不能上架返回 null。</summary>
        public static string SellId(GameItem it)
        {
            if (it == null || it.identifier == null) return null;
            if (Items.IsGpu(it) || AsicItems.Is(it)) return "gpu:" + Items.ToUnit(it).Model;
            string id = it.identifier;
            return Merchants.IsGood(id) ? id : null;
        }

        /// <summary>自己的储藏区、背包不能上架：卖掉会带走里面的东西，也会丢掉容量。</summary>
        private static readonly System.Collections.Generic.HashSet<string> StorageIds = new System.Collections.Generic.HashSet<string>(Merchants.Table[MerchantType.Storage].Select(e => e.Id));

        public static System.Collections.Generic.List<GameItem> Sellable() =>
            GraphUtils.FindAllChildrenType<GameItem>(EmporiumEntry.Instance.invElement, i => GeneralHelper.IsItemOwned(i) && SellId(i) != null && !i.IsTag(Items.DeadTag) && !StorageIds.Contains(i.identifier) && i.FindAllChildItems().Count == 0, i => i.identifier != Items.Rig);

        /// <summary>参考价：显卡按标称型号与成色，其余按游戏基础价。</summary>
        public static long RefValue(GameItem it)
        {
            string id = SellId(it);
            if (id == null) return 0;
            if (Items.IsGpu(it) || AsicItems.Is(it)) { var u = Items.ToUnit(it); return CardValue.Of(u.Model, u.Health); }
            return Value(id);
        }

        public static GoodKind KindOf(string id) => id.StartsWith("gpu:") ? GoodKind.Gpu : Merchants.Info(id).Kind;

        /// <summary>每周刷新：现货与订单目录一起。返回是否刷新了。</summary>
        public static bool Restock(SaveData d, int day, bool force = false)
        {
            int rep = StoreReputation.GetBMReputation();
            if (d.Black.Closed(day)) return false;
            if (force) d.Black.RefreshedDay = -999;
            if (!d.Black.Refresh(day, rep, d.Rng, Value)) return false;
            d.Black.RefreshOrders(day, rep, d.Rng, Value);
            // 平台现货偶尔有硬件钱包：比杰克逊博士便宜，外观一样，其中一部分被动过手脚
            if (d.Rng.Chance(Tuning.WalletMallChance))
                d.Black.Offers.Add(new BmOffer { Item = Items.HwWallet, Kind = GoodKind.Tech, Price = d.Rng.Range(Tuning.WalletMallMin, Tuning.WalletMallMax), Qty = d.Rng.Range(1, Tuning.WalletMallQtyMax) });
            Log.Info($"黑市刷新（第 {day} 天）：现货 {d.Black.Offers.Count} 种，商家 {d.Black.Sellers.Count} 家（{string.Join("/", d.Black.Sellers.Select(x => x.Type))}），货架 {d.Black.Catalog.Count} 种共 {d.Black.CatalogValue:0} 信用点，高科技 {d.Black.Offers.Count(o => o.Kind == GoodKind.Tech)}，显卡 {d.Black.Offers.Count(o => o.Kind == GoodKind.Gpu)}");
            return true;
        }

        public static void Night(SaveData d, int day, bool crackdown)
        {
            var ps = PlayerStore.Instance;
            foreach (var e in d.Black.Night(day, crackdown, d.WalletAt, d.Rng, StoreReputation.GetSecReputation(), Session.Price))
            {
                if (e.Sale != null)
                {
                    string sname = ItemName(e.Sale.Item);
                    Log.Info($"黑市出售事件：{e.Kind} {e.Sale.Item} ×{e.Sale.Mult} 货款 {e.Amount:0} 污点 {e.Sale.Taint:0}");
                    switch (e.Kind)
                    {
                        case "sale_sold": ps.AddNightLog(L.T("nightlog.bm.sale_sold", sname, Fmt.Num(e.Amount), e.Sale.PayDay), "99bb66"); break;
                        case "sale_paid": ps.AddNightLog(L.T("nightlog.bm.sale_paid", sname, Fmt.Num(e.Amount), Fmt.Num(e.Sale.Taint)), "99bb66"); break;
                        case "sale_fake":
                            Items.Give(Make(e.Sale.Item, e.Sale.Model, e.Sale.FakeOf, e.Sale.Health));
                            ps.AddNightLog(L.T("nightlog.bm.sale_fake", sname), "cc7755");
                            break;
                        case "sale_rug": ps.AddNightLog(L.T("nightlog.bm.sale_rug", sname, Fmt.Num(e.Amount)), "cc7755"); break;
                    }
                    if (e.Kind == "sale_rug") News.Show("pd_news_bmrug_" + day, L.T("news.bmrug.title"), L.T("news.bmrug.desc"), 4);
                    continue;
                }
                string name = e.Order != null && e.Kind != "trap" ? ItemName(e.Order.Item) : null;
                Log.Info($"黑市订单事件：{e.Kind} {e.Order?.Item} {e.Order?.Seller} {e.Order?.Mode} 受控 {e.Order?.Controlled} 金额 {e.Amount:0}");
                switch (e.Kind)
                {
                    case "ready": ps.AddNightLog(L.T("nightlog.bm.ready", name), "99bb66"); break;
                    case "scam_refund": ps.AddNightLog(L.T("nightlog.bm.scam_refund", DisplayName(e.Order.Seller), name), "cc7755"); break;
                    case "scam_loss": ps.AddNightLog(L.T("nightlog.bm.scam_loss", DisplayName(e.Order.Seller), name, Fmt.Num(e.Amount)), "cc7755"); break;
                    case "intercept": ps.AddNightLog(L.T("nightlog.bm.intercept", name, Fmt.Num(e.Amount)), "cc7755"); break;
                    case "rug": ps.AddNightLog(L.T("nightlog.bm.rug", name, Fmt.Num(e.Amount)), "cc7755"); break;
                    case "trap":
                        {
                            int amount = (int)Math.Min(Tuning.CrimeEventCap, e.Amount);
                            ps.secData.CommitCrime(e.Order.Item != null && e.Order.Item.StartsWith("gpu:") ? "FENCING" : "CONTRABAND_TRAFFICKING", amount);
                            Log.Info($"黑市追查：记罪证 {amount}（{e.Order.Seller}）");
                            ps.AddNightLog(L.T("nightlog.bm.trap." + e.Order.Seller, ItemName(e.Order.Item), amount), "cc7755");
                            break;
                        }
                    case "reopen": ps.AddNightLog(L.T("nightlog.bm.reopen"), "99bb66"); break;
                }
                if (e.Kind == "rug") Night_RugNews(day);
            }
            if (Unlock.BlackUnlocked && !crackdown) Restock(d, day);
        }

        private static void Night_RugNews(int day) => News.Show("pd_news_bmrug_" + day, L.T("news.bmrug.title"), L.T("news.bmrug.desc"), 4);

        // ---- 上门：开门后有到货的当天，快递员排进顾客队伍 ----
        private static int courierDay = -1;

        private static void OnGenerate(StoreClientManager manager)
        {
            if (Session.Data == null || !Unlock.BlackUnlocked) return;
            Log.Guard("黑市快递员", () =>
            {
                int day = StoreStation.GetDayCounter();
                if (day == courierDay || !Session.Data.Black.Ready.Any()) return;
                courierDay = day;
                manager.AddClient(CreateCourier());
            });
        }

        public static StoreClient CreateCourier()
        {
            var rng = Session.Data.Rng;
            var c = new StoreClient
            {
                identifier = "pd_bm_courier",
                displayName = L.T("courier.name"),
                spriteName = SpriteDict.GetRandomScavSpriteName(),
                clientIntent = StoreClient.ClientIntent.SELL,
            };
            StoreClientFactionSetup.InitScav(c);
            var trap = Session.Data.Black.Ready.FirstOrDefault(o => o.Controlled);
            Action finish = () => { PlayerStore.Instance.DismissCurrentClient(); };
            if (trap == null)
            {
                c.mainDialogue.SetText(c.displayName, L.T("courier.line1." + rng.Range(1, 2))).NextDialogue().SetText(c.displayName, L.T("courier.line2." + rng.Range(1, 2)))
                    .SetEndAction(() => Log.Guard("黑市快递交货", () => { DeliverDoor(); finish(); }));
            }
            else
            {
                // 受控交付：快递员神色异常，签收就是当场记罪证并查封，拒签只损失货款
                var sign = new Dialogue();
                sign.SetText("PLAYER", L.T("courier.sign.say"));
                sign.NextDialogue().SetText(c.displayName, L.T("courier.signed")).SetEndAction(() => Log.Guard("受控交付签收", () =>
                {
                    int amount = (int)Math.Min(Tuning.CrimeEventCap, Session.Data.Black.Sign(trap, true));
                    PlayerStore.Instance.secData.CommitCrime(trap.Item.StartsWith("gpu:") ? "FENCING" : "CONTRABAND_TRAFFICKING", amount);
                    Log.Info($"受控交付：签收，记罪证 {amount}，货被查封");
                    DeliverDoor(); finish();
                }));
                var refuse = new Dialogue();
                refuse.SetText("PLAYER", L.T("courier.refuse.say"));
                refuse.NextDialogue().SetText(c.displayName, L.T("courier.refused")).SetEndAction(() => Log.Guard("受控交付拒签", () => { Session.Data.Black.Sign(trap, false); Log.Info("受控交付：拒签"); DeliverDoor(); finish(); }));
                c.mainDialogue.SetText(c.displayName, L.T("courier.odd1")).NextDialogue().SetText(c.displayName, L.T("courier.odd2"))
                    .AddChoice(DirectoryMaster.Item("yes"), L.T("courier.sign"), "", sign, null)
                    .AddChoice(DirectoryMaster.Item("cancel"), L.T("courier.refuse"), "", refuse, null);
            }
            c.AddBasicDialog();
            c.CompleteClientCreation();
            return c;
        }

        private static void DeliverDoor()
        {
            var d = Session.Data;
            foreach (var o in d.Black.Ready.Where(x => !x.Controlled).ToList())
            {
                for (int i = 0; i < o.Count; i++)
                {
                    var made = Make(o.Item, o.Model, o.FakeOf, o.Health);
                    Items.Give(made);
                    if (o.Item.StartsWith("gpu:")) { var u = Items.ToUnit(made); Log.Info($"黑市到货显卡：标称 {u.Model} 真身 {(u.FakeOf ?? "（真卡）")} 成色 {u.Health:0} 外观 {u.Look.Encode()}"); }
                }
                d.Black.Deliver(o);
                Log.Info($"黑市快递：{o.Item} ×{o.Count}{(o.FakeOf != null ? "（假卡，真身 " + o.FakeOf + "）" : "")} 已放进储藏区");
            }
            Session.Save();
        }
    }
}

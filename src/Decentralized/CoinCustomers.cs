using System.Collections.Generic;
using System.Linq;
using Decentralized.Domain;

namespace Decentralized
{
    /// <summary>
    /// 用币付款的顾客：每天排队的买家里，按派系（上层 CRC、黑市 CRT、革命派 CRA、下层 PSC）有一小部分改用币结算。
    /// 成交时在店里扣回刚入账的信用点，换成币进热钱包；赃物的款项不记销赃，改成币带污点（货值 × 赃物热度%）。
    /// </summary>
    public static class CoinCustomers
    {
        private static readonly HashSet<StoreClient> Marked = new HashSet<StoreClient>();
        private static int markDay = -1;

        public static void Subscribe()
        {
            ModHook.OnGenerateCustomerLate += OnGenerate;
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Run night (test)", () => Log.Guard("过夜（测试）", () =>
            {
                // 走游戏自己的夜间流程（EndNight 会触发本 mod 的夜间结算），再进下一天并开门前的日程
                // 游戏过夜会清空前柜台，先把自己的东西收进物品栏
                foreach (var it in EmporiumEntry.Instance.frontInvinvElement.childItems.ToList())
                    if (GeneralHelper.IsItemOwned(it)) GraphUtils.TryAcceptAll(EmporiumEntry.Instance.invElement, it);
                PlayerStore.Instance.EndNight();
                StoreStation.Instance.storeEventManager.OnDayEnd();   // 让事件按天数过期
                StoreStation.Instance.StartDay();
                Log.Info($"过夜完成，现在是第 {StoreStation.GetDayCounter()} 天");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Morning report (test)", () => Log.Guard("晨间日志（测试）", () => StartOfDayUIManager.Instance.ShowMorningReport()));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Newspaper (test)", () => Log.Guard("报纸（测试）", () => NewsUIManager.Instance.ToggleUI()));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Start crackdown (test)", () => Log.Guard("镇压（测试）", () =>
            {
                StoreStation.Instance.storeEventManager.ActivateEvent(OperationListSec.Crackdown());
                Log.Info("镇压事件已激活");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Frozen CRT (test)", () => Log.Guard("冻结 CRT（测试）", () =>
            {
                var w = Session.Data.Hot;
                w.Add(Assets.CRT, 1000);
                double f = w.Freeze(Assets.CRT, Tuning.CrtFreezeShare, StoreStation.GetDayCounter() + Tuning.CrtUnfreezeAfter);
                Log.Info($"CRT {w.Get(Assets.CRT):0}，冻结 {f:0}，可动用 {w.Avail(Assets.CRT):0}");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Tainted wallet (test)", () => Log.Guard("带污点的钱包（测试）", () =>
            {
                var w = Session.Data.Hot;
                w.AddDirty(Assets.PSC, 100, 30); w.AddDirty(Assets.CRT, 1000, 1000); w.Basis += 2000;
                Log.Info($"钱包：PSC {w.Get(Assets.PSC):0.##}（脏 {w.DirtyOf(Assets.PSC):0.##}），CRT {w.Get(Assets.CRT):0}（脏 {w.DirtyOf(Assets.CRT):0}）");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Terminal (test)", () => Log.Guard("终端（测试）", TerminalWindow.Open));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Black market +50 rep, restock (test)", () => Log.Guard("黑市（测试）", () =>
            {
                StoreReputation.ModReputation("FACTION_BLACK_MARKET", 50);
                if (!Unlock.IsUnlocked) ModHelper.GetNetworkUpgrade(Unlock.Id)?.Unlock();
                if (!Unlock.BlackUnlocked) ModHelper.GetNetworkUpgrade(Unlock.BlackId)?.Unlock();
                if (!Unlock.MixerUnlocked) ModHelper.GetNetworkUpgrade(Unlock.MixerId)?.Unlock();
                var d = Session.Data;
                d.Hot.Add(Assets.PSC, 20); d.Hot.AddDirty(Assets.CRT, 3000, 2000); d.Hot.Basis += 1000;
                BlackDelivery.Restock(d, StoreStation.GetDayCounter(), true);
                Log.Info($"黑市：声望 {StoreReputation.GetBMReputation()}，上架 {d.Black.Offers.Count} 种：{string.Join(", ", d.Black.Offers.Select(o => o.Item + "×" + o.Qty + "@" + o.Price))}，解锁 {Unlock.BlackUnlocked}");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Black market sample (test)", () => Log.Guard("黑市抽样（测试）", () =>
            {
                var d = Session.Data;
                int day = StoreStation.GetDayCounter();
                var vals = new System.Collections.Generic.List<double>();
                var tech = new System.Collections.Generic.List<double>();
                for (int i = 0; i < 60; i++)
                {
                    BlackDelivery.Restock(d, day, true);
                    vals.Add(d.Black.CatalogValue);
                }
                var missing = Merchants.Table.SelectMany(kv => kv.Value.Select(e => (kv.Key, e.Id))).Where(x => BlackDelivery.Value(x.Item2) <= 0).Select(x => x.Item1 + ":" + x.Item2).ToList();
                var costs = Merchants.Table.SelectMany(kv => kv.Value.Select(e => (kv.Key, e.Id))).Select(x => BlackDelivery.Value(x.Item2)).Where(v => v > 0).OrderBy(v => v).ToList();
                Log.Info("黑市抽样：基础价 >2500 的货：" + string.Join(", ", Merchants.Table.SelectMany(kv => kv.Value.Select(e => (kv.Key, e.Id))).Select(x => (id: x.Item2, v: BlackDelivery.Value(x.Item2))).Where(x => x.v > 2500).Select(x => x.id + "=" + x.v)));
                Log.Info($"黑市抽样：无法造出或价格为 0 的货 {missing.Count} 个：{string.Join(", ", missing)}；基础价中位 {costs[costs.Count / 2]}，最高 {costs.Last()}");
                Log.Info($"黑市抽样：60 周货架总值 平均 {vals.Average():0}，最小 {vals.Min():0}，最大 {vals.Max():0}");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Black market all merchants (test)", () => Log.Guard("黑市全部商家（测试）", () =>
            {
                var d = Session.Data;
                d.Black.RefreshOrders(StoreStation.GetDayCounter(), StoreReputation.GetBMReputation(), d.Rng, BlackDelivery.Value, true);
                Log.Info($"黑市全部商家（测试）：{d.Black.Sellers.Count} 家，货架 {d.Black.Catalog.Count} 种，{string.Join("/", d.Black.Sellers.Select(x => x.Type))}");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Black market negative cases (test)", () => Log.Guard("黑市负面场景（测试）", () =>
            {
                var d = Session.Data;
                int day = StoreStation.GetDayCounter();
                d.Black.RefreshOrders(day, StoreReputation.GetBMReputation(), d.Rng, BlackDelivery.Value, true);
                var w = d.Hot;
                w.Add(Assets.CRT, 20000);
                BmListing First(MerchantType t) => d.Black.Catalog.First(l => d.Black.Sellers[l.Seller].Type == t);
                // 骗子（老号，接受提前放款）：二手矿卡；诚实：全新矿卡；诚实二手卡里夹假卡
                var scamUsed = First(MerchantType.GpuUsed);
                var sUsed = d.Black.Sellers[scamUsed.Seller]; sUsed.Scam = true; sUsed.Trades = 120; sUsed.Rating = 97;
                var honest = First(MerchantType.GpuNew);
                d.Black.Sellers[honest.Seller].Scam = false; d.Black.Sellers[honest.Seller].Trades = 150;
                foreach (var s in d.Black.Sellers.Where(x => x.Type == MerchantType.Personal)) s.Scam = true;
                var fakeL = First(MerchantType.Modules);
                var fakeCard = new BmListing { Seller = d.Black.Sellers.FindIndex(x => x.Type == MerchantType.Modules), Item = "gpu:RTX70", Kind = GoodKind.Gpu, Level = 2, Model = "RTX70", FakeOf = "RTX60", Health = 55, Price = 500, Qty = 5 };
                d.Black.Sellers[fakeCard.Seller].Scam = false; d.Black.Sellers[fakeCard.Seller].Trades = 150;
                d.Black.Catalog.Add(fakeCard);
                var persL = d.Black.Catalog.First(l => d.Black.Sellers[l.Seller].Type == MerchantType.Personal);
                d.Black.HoneypotUntil = day + 6;
                int before = d.Black.Orders.Count;
                var o1 = d.Black.Place(scamUsed, 1, PayMode.Escrow, w, Assets.CRT, day, Session.Price);   // 骗子 + 托管 → 退款
                var o2 = d.Black.Place(scamUsed, 1, PayMode.Advance, w, Assets.CRT, day, Session.Price);  // 骗子 + 提前放款 → 钱没了
                var o3 = d.Black.Place(fakeCard, 2, PayMode.Escrow, w, Assets.CRT, day, Session.Price);   // 假卡
                var o4 = d.Black.Place(persL, 1, PayMode.Advance, w, Assets.CRT, day, Session.Price);     // 个人卖家不接受提前放款 → null
                var o5 = d.Black.Place(persL, 1, PayMode.Escrow, w, Assets.CRT, day, Session.Price);      // 个人卖家（骗子）+ 托管
                Log.Info($"负面场景：下单 骗子托管={o1 != null} 骗子提前={o2 != null} 假卡={o3 != null} 个人提前={o4 != null}（应为 False）个人托管={o5 != null}，蜜罐至第 {d.Black.HoneypotUntil} 天，追查 {d.Black.Traps.Count} 笔，钱包 CRT {w.Get(Assets.CRT):0} 污点 {w.Taint:0}");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Black market events (test)", () => Log.Guard("黑市事件（测试）", () =>
            {
                var d = Session.Data;
                int day = StoreStation.GetDayCounter();
                d.Black.Traps.Add(new BmTrap { Day = day, Amount = 300, Why = "honeypot", Item = "c4" });
                var w = d.Hot;
                for (int i = 0; i < 8; i++) d.Black.List("gpu:RTX70", "RTX70", "RTX60", 60, 500, 0.6, w, Assets.CRT, day - 1, Session.Price);
                var l = d.Black.Catalog.FirstOrDefault();
                if (l != null)
                {
                    var o = d.Black.Place(l, 1, PayMode.Escrow, w, Assets.CRT, day, Session.Price);
                    if (o != null) { o.State = OrderState.Ready; o.Controlled = true; }
                }
                Log.Info($"黑市事件（测试）：追查 300 今晚到期，假卡上架 8 件，受控订单 {d.Black.Orders.Count(x => x.Controlled)} 个");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Courier (test)", () => Log.Guard("黑市快递（测试）", () =>
            {
                foreach (var o in Session.Data.Black.Orders.Where(x => x.State == OrderState.Pending)) { o.State = OrderState.Ready; }
                PlayerStore.Instance.currentClientInstance = null;
                PlayerStore.Instance.storeClientManager.clientStack.Insert(0, BlackDelivery.CreateCourier());
                PlayerStore.Instance.TryCallNextClient();
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Courier trap (test)", () => Log.Guard("受控交付（测试）", () =>
            {
                foreach (var o in Session.Data.Black.Orders.Where(x => x.State == OrderState.Pending)) { o.State = OrderState.Ready; o.Controlled = true; }
                PlayerStore.Instance.currentClientInstance = null;
                PlayerStore.Instance.storeClientManager.clientStack.Insert(0, BlackDelivery.CreateCourier());
                PlayerStore.Instance.TryCallNextClient();
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Coin buyer forged (test)", () => { ForceFake = true; CoinBuyerTest(); });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Coin buyer (test)", () => { ForceFake = false; CoinBuyerTest(); });
        }

        private static bool ForceFake;

        private static void CoinBuyerTest() => Log.Guard("用币付款顾客（测试）", () =>
            {
                var c = new StoreClient
                {
                    identifier = "pd_coin_test", displayName = "Coin buyer", spriteName = SpriteDict.GetRandomScavSpriteName(),
                    clientFaction = "FACTION_BLACK_MARKET", useClientBudget = true, clientIntent = StoreClient.ClientIntent.BUY,
                    canClientExposeFeature = ClientCanExposeFunc.BasicClientExposeCapacity,
                };
                c.SetBudget(5000);
                c.clientBuyingTagList.Add("SEED");
                c.mainDialogue.SetText(c.displayName, "CRT.").SetEndAction(() => { });
                c.AddBasicDialog();
                c.CompleteClientCreation();
                Mark(c, Assets.CRT, ForceFake);
                Log.Info($"用币付款顾客（测试）已设置，钱包污点 {Session.Data.Hot.Taint:0.##}，当前顾客 {PlayerStore.Instance.currentClientInstance?.GetClientBlueprint()?.identifier}，队列 {PlayerStore.Instance.storeClientManager.clientStack.Count} 人");
                PlayerStore.Instance.currentClientInstance = null;   // 测试：丢掉在场的顾客（开局的高利贷者不可遣散）
                PlayerStore.Instance.storeClientManager.clientStack.Insert(0, c);
                PlayerStore.Instance.TryCallNextClient();
                // 把后柜里的种子都标成赃物（热度 50%），买走后钱包的污点应为成交额的一半
                foreach (var it in EmporiumEntry.Instance.backInvinvElement.childItems)
                    if (it.IsTag("SEED")) it.ModifyTag("STOLEN_VALUE_INT", t => t.Enable().SetInt(50));
            });

        public static void Unsubscribe() => ModHook.OnGenerateCustomerLate -= OnGenerate;

        private static void OnGenerate(StoreClientManager manager)
        {
            if (Session.Data == null || !Unlock.IsUnlocked || !Session.Data.Connected) return;   // 没有连接钱包就没有用币付款的顾客
            Log.Guard("用币付款", () =>
            {
                int day = StoreStation.GetDayCounter();
                if (day != markDay) { Marked.Clear(); markDay = day; }
                foreach (var c in manager.clientStack)
                {
                    if (c == null || c.isSecurity || Marked.Contains(c) || c.clientIntent != StoreClient.ClientIntent.BUY || c.isNoContributeToEvidence) continue;
                    Marked.Add(c);
                    string asset = CoinPayment.AssetFor(c.clientFaction);
                    if (asset == null || !Session.Data.Rng.Chance(CoinPayment.Chance(asset))) continue;
                    Mark(c, asset, day >= Tuning.ScamMinDay && Session.Data.Rng.Chance(Tuning.FakeTransferShare));
                }
            });
        }

        /// <summary>
        /// 标成用币付款的顾客。开口先说用什么币付：正常的说"链上确认要一会儿"；骗子（伪造转账）说"已经转了，给你看截图，快给货"——
        /// 成交后信用点照样被扣回，币却不会到账。玩家能做的就是听清这句话，不卖给他。
        /// </summary>
        private static void Mark(StoreClient c, string asset, bool fake)
        {
            var before = c.onItemBought;
            double total = 0;   // 这位顾客这一单一共付了多少币
            c.isNoContributeToEvidence = true;
            var intro = new Dialogue().SetText(c.displayName, L.T(fake ? "coinpay.intro.fake" : "coinpay.intro", asset));
            intro.SetNextDialogue(c.mainDialogue);
            c.mainDialogue = intro;
            c.onItemBought = (item, cost) =>
            {
                before?.Invoke(item, cost);
                Log.Guard("用币付款成交", () =>
                {
                    if (fake) { FakePay(c, asset, item, cost); total += cost / Session.Price(asset); }
                    else total += Pay(c, asset, item, cost);
                    Say(c, asset, total);
                });
            };
        }

        /// <summary>伪造转账：信用点扣回（顾客没真付），币不会到账，晨间日志说明。</summary>
        private static void FakePay(StoreClient client, string asset, GameItem item, int cost)
        {
            if (cost <= 0) return;
            ModHelper.AddCredit(-cost, false);
            Session.Data.FakePaidToday.Add(L.T("coinpay.fake.entry", client.displayName, item.name, cost));
            Log.Info($"伪造转账：{client.displayName} 拿走 {item.name}（{cost} 信用点），{asset} 不会到账");
        }

        /// <summary>成交后顾客说的话：告诉玩家这单是币到账，不是信用点。</summary>
        private static void Say(StoreClient client, string asset, double total)
        {
            client.acceptDealDialogue = new Dialogue().SetText(client.displayName, L.T("coinpay.line", Fmt.Num(total), asset));
            client.acceptDealDialogue.priority = 4;
            client.acceptDealDialoguePlayed = false;
        }

        private static double Pay(StoreClient client, string asset, GameItem item, int cost)
        {
            var d = Session.Data;
            double price = Session.Price(asset);
            if (cost <= 0 || price <= 0) return 0;
            int heat = item.IsTag("STOLEN_VALUE_INT") ? item.GetTagReadonly("STOLEN_VALUE_INT").GetInt() : 0;
            ModHelper.AddCredit(-cost, false);
            double qty = CoinPayment.Receive(d.Hot, asset, cost, price, heat);
            d.Chain(ChainAddress.Make(d.Rng.NextUInt()), d.Hot.Address, asset, qty);
            d.CoinSalesToday += cost;
            Log.Info($"用币付款：{item.name} {cost} 信用点 → {qty:0.####} {asset}，污点 +{cost * heat / 100.0:0}");
            return qty;
        }
    }
}

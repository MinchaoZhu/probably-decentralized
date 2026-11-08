using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Decentralized.Domain;

namespace Decentralized
{
    /// <summary>
    /// 杰克逊博士进货时（夜间去他店里，或他每周来本店摆摊）加上挖矿设备。用游戏给 mod 的进货钩子和他自己的上架函数，
    /// 价格是物品价值加上他的加价（难度规则与夜间事件决定）。加入 Web3 治理社区之后才上货。
    /// </summary>
    public static class DealerStock
    {
        public static void Subscribe()
        {
            ModHook.OnPlaceInventorInventoryItemLate += OnPlace;
            // 开发者模式（F9）里的测试入口：像到了杰克逊店里一样，清空并扩大柜台后上货
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Give rig x3 (test)", () => { for (int i = 0; i < 3; i++) Items.Give(ModHelper.SpawnItem(Items.Rig)); });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Used cards x4 (test)", () =>
            {
                var rng = Session.Data.Rng;
                Items.Give(Items.NewCard("RTX80", 40, null, rng));
                Items.Give(Items.NewCard("RTX80", 70, "RTX60", rng));
                Items.Give(Items.NewCard("RX800", 30, "RX70", rng));
                Items.Give(Items.NewCard("RX900", 90, "RX70", rng));
            });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Dump inspection UI (test)", () => Log.Guard("检视界面结构", () =>
            {
                var sb = new System.Text.StringBuilder("\n");
                void Walk(Transform t, int depth)
                {
                    var rt = t as RectTransform;
                    var img = t.GetComponent<UnityEngine.UI.Image>();
                    sb.AppendLine($"{new string(' ', depth * 2)}{t.name} active={t.gameObject.activeSelf} pos={t.localPosition} size={(rt != null ? rt.sizeDelta.ToString() : "-")} scale={t.localScale}{(img != null ? " img=" + (img.sprite != null ? img.sprite.name : "null") : "")}");
                    if (depth < 5) foreach (Transform c in t) Walk(c, depth + 1);
                }
                var m = InspectionUIManager.Instance;
                var ci = PlayerStore.Instance.currentClientInstance;
                sb.AppendLine("当前顾客：" + (ci == null ? "无" : ci.GetClientBlueprint().identifier + " 意图 " + ci.GetClientBlueprint().clientIntent));
                sb.AppendLine("检视物品：" + (m.currentInspectingItem?.identifier ?? "无") + " APPRAISAL=" + (m.currentInspectingItem?.IsTag("APPRAISAL_ITEM") ?? false));
                Walk(m.panel.transform, 0);
                Log.Info(sb.ToString());
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Edit claim + offer to buyer (test)", () => Log.Guard("卖出侧测试", () =>
            {
                var rng = Session.Data.Rng;
                // 自己的一张真卡：标签机改成旗舰型号并把成色说成没问题，再摆给一位买家
                var item = Items.NewCard("RTX70", 55, null, rng);
                Items.Give(item);
                var u = Items.ToUnit(item);
                var c = Claim.Truth(u); c.Model = "RTX90"; c.Fan = 0; c.Fingers = 0; c.Bracket = 0; c.Sticker = 0;
                item.EnableTag(Items.EditedTag);
                Items.SetClaim(item, c, 0);
                var client = new StoreClient { identifier = "pd_test_buyer", displayName = "买家", spriteName = SpriteDict.GetRandomScavSpriteName(), clientIntent = StoreClient.ClientIntent.BUY };
                client.CompleteClientCreation();
                PlayerStore.Instance.currentClientInstance = StoreClientInstance.CreateClientInstance(client);
                Log.Info($"测试：卡 #{item.uniqueId} 说法 {c.Encode()}，真实 {Claim.Truth(u).Encode()}，价格 {item.unitValue}，信誉前");
                Items.OfferForTest(item);
                Log.Info($"测试：验货后 Exposed={Items.Exposed(item)} 价格 {item.unitValue}");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Buy scam card (test)", () => Log.Guard("买入侧测试", () =>
            {
                var rng = Session.Data.Rng;
                var item = Items.NewCard("RTX90", 60, "RTX60", rng, DealerKind.Scammer, 0);
                long before = item.unitValue;
                GeneralHelper.SetItemOwned(item, true);
                Items.Refresh(item);
                Items.Give(item);
                Log.Info($"买入测试：卡 #{item.uniqueId} 买前报价 {before}，买下后价值 {item.unitValue}，真实 {CardValue.True(Items.ToUnit(item))}，说法 {(Items.GetClaim(item)?.Encode() ?? "无")}");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Poinbase +5000 CRC, unlock (test)", () => Log.Guard("永续测试", () =>
            {
                var d = Session.Data;
                d.Poinbase.Add(Assets.CRC, 5000); d.Poinbase.Basis += 5000;
                if (!Unlock.IsUnlocked) ModHelper.GetNetworkUpgrade(Unlock.Id)?.Unlock();
                Log.Info($"测试：Poinbase CRC {d.Poinbase.Get(Assets.CRC):0}，解锁 {Unlock.IsUnlocked}");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Give ASIC M1/M2/M3 (test)", () => { foreach (var m in new[] { "M1", "M2", "M3" }) Items.Give(AsicItems.New(m, 100)); for (int i = 0; i < 4; i++) Items.Give(ModHelper.SpawnItem("rare_electronic")); });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Era in 2 days (test)", () => { var n = Session.Data.Network; int day = StoreStation.GetDayCounter(); n.EraDay = day + 2; n.EraWarnDay = day; n.EraWarned = false; n.EraDone = false; Log.Info($"测试：矿机时代第 {n.EraDay} 天，NPC 耗电 {n.NpcKj}"); });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Predict: refill all kinds (test)", () => Log.Guard("预测测试", () =>
            {
                var d = Session.Data; int day = StoreStation.GetDayCounter();
                d.Predict.Markets.Clear();
                PredictGame.Morning(d, day); var made = new List<PredictMarket>(); for (int i = 0; i < 4; i++) made.AddRange(d.Predict.Refill(day, d.Rng, PredictGame.Candidates(day)));
                Log.Info($"测试：新开 {made.Count} 个预测盘口（不看解锁日）");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Predict: add era and CRA (test)", () =>
            {
                var d = Session.Data; int day = StoreStation.GetDayCounter(); var bk = d.Predict;
                bk.Markets.Add(PredictMarket.Create(++bk.Seq, PredictKind.Era, "era", L.T("predict.era"), 0, day, day + 1, day + 3, PredictMath.EraProb(day, day + 1, day + 3, -1) + 0.05));
                double h = 1 - System.Math.Pow(1 - Tuning.CraSpiralWeekly, 1.0 / 7);
                bk.Markets.Add(PredictMarket.Create(++bk.Seq, PredictKind.Cra, "cra", "CRA", 0, day, day + 1, day + 3, PredictMath.CraWarnProb(d.Stable.Cushion, Stablecoins.CushionDrain(Tuning.CraNpcStake + d.Staking.Earning, d.Stable.CraRate), day, day + 1, day + 3, h)));
                Log.Info($"测试：矿机时代 {d.Network.EraDay}，CRA 储备 {d.Stable.Reserve:0}（R/S {d.Stable.Ratio:0.00}）");
            });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Informant: queue power shortage in 3 days (test)", () => Log.Guard("线人测试", () =>
            {
                StoreStation.Instance.storeEventManager.QueueFuturEvent(EventListNormal.CreatePowerShortage(), 3, true);
                Log.Info("测试：停电 3 天后（已知）");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Scams: airdrops + pools + wick (test)", () => Log.Guard("骗局测试", () =>
            {
                var d = Session.Data; int day = StoreStation.GetDayCounter();
                var assets = new[] { Assets.CRT, Assets.PSC, Assets.CRC, Assets.CRA };
                for (int i = 0; i < 4; i++) d.Scams.NewAirdrop(day, d.Rng, assets);
                while (d.Scams.Pools.Count(p => !p.Closed) < Tuning.YieldPoolMax) d.Scams.NewPool(day, d.Rng);
                d.Market.Wick = 0; double w = 0; for (int i = 0; i < 200 && w == 0; i++) w = d.Market.TryWick(d.Rng, day);
                Log.Info($"测试：空投 {string.Join(",", d.Scams.Airdrops.Select(a => a.Name + (a.Scam ? "*" : "")))}；池子 {string.Join(",", d.Scams.Pools.Select(p => p.Name + (p.Scam ? "*" : "")))}；插针 {w:0.000} 回去 {d.Market.WickBack:0.00}");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Scams: fake support now (test)", () => Log.Guard("假客服测试", () =>
            {
                var m = PlayerStore.Instance.storeClientManager;
                var c = CoinCustomers.FakeSupportForTest();
                PlayerStore.Instance.currentClientInstance = null;
                m.clientStack.Insert(0, c);
                PlayerStore.Instance.TryCallNextClient();
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Chart: run market 30 days (test)", () => Log.Guard("K 线测试", () =>
            {
                // 用真实的行情过程往前跑 30 天，记录蜡烛（会改动这一局的 PSC 价格，只在测试存档用）
                var d = Session.Data; int day0 = StoreStation.GetDayCounter() - 30; var none = new List<MarketEvent>();
                d.Candles.Series.Clear();
                for (int k = day0; k < day0 + 30; k++)
                {
                    d.Market.Open(none, d.Rng, k); CandleChart.Morning(d, k);
                    double wh = d.Market.Whale(d.Rng); d.Candles.Tick(Assets.PSC, k, d.Market.Price, wh);
                    if (d.Market.TryWick(d.Rng, k) != 0) CandleChart.Touch(d, k, Assets.PSC, Assets.Ponero);
                    if (d.Rng.Chance(0.3)) { double v = d.Rng.Range(500.0, 4000.0); bool buy = d.Rng.Chance(0.5); if (buy) d.Market.Main.BuyBase(v); else d.Market.Main.SellBase(v / d.Market.Price); d.Candles.Tick(Assets.PSC, k, d.Market.Price, v, true); }
                    d.Market.UndoWick(k); CandleChart.Close(d, k);
                    d.Stable.Night(k, d.Rng, d.Staking.Earning);
                    d.Market.Night(none, d.Rng, k, Session.Price); d.Market.UpdateSupply(k);
                }
                CandleChart.Morning(d, day0 + 30);
                Log.Info($"测试：K 线 {d.Candles.Of(Assets.PSC).Count} 根，PSC {d.Market.Price:0.00}");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Story: Serpico to Revolution, Eleanor leaks, Lun arrested (test)", () =>
            {
                var cd = PlayerStore.GetStoreClientData();
                cd.serpicoDocumentChoice = SerpicoStoryline.CHOICE_REVOLUTION; cd.eleanorDriveChoice = 2; cd.officerLunArrested = true;
                Log.Info("测试：剧情选择已设置（革命军 / 泄露 / 伦警官被捕），今晚生效");
            });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Tumbler: next visit now (test)", () => Log.Guard("Tumbler 测试", () =>
            {
                // 把 Tumbler 的下一次上门挪到今天，并立刻叫进来
                var t = Session.Data.Story.Tumbler; int day = StoreStation.GetDayCounter();
                if (t.StartDay < 0) t.StartDay = day;
                t.StartDay = day - Tumbler.Offsets[System.Math.Min(t.Stage, Tumbler.Offsets.Length - 1)]; t.VisitedDay = -1;
                var af = Session.Data.Story.After;
                if (af.AgentDay >= day) af.AgentDay = day - 1;
                if (af.SearchDay >= day) af.SearchDay = day - 1;
                PlayerStore.Instance.currentClientInstance = null;
                var m = PlayerStore.Instance.storeClientManager;
                TumblerStory.ForceVisit(m);
                PlayerStore.Instance.TryCallNextClient();
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Tumbler: restart story (test)", () =>
            {
                var st = Session.Data.Story;
                Log.Info($"测试：Tumbler 剧情重来（原进度 {st.Tumbler.Stage}，选择 {st.Tumbler.Choice}，结束 {st.Tumbler.Done}）");
                st.Tumbler = new Tumbler(); st.After = new TumblerAfter();
                st.Applied.RemoveAll(k => k.StartsWith("tumbler_"));
                st.MixerClosed = st.MixerRebranded = st.MixerHoneypot = false; st.MixerFee = st.MixerVolume = st.MixerRug = 1;
            });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Hot wallet +2000 CRC (test)", () =>
            {
                var w = Session.Data.Hot; w.Add(Assets.CRC, 2000); w.Basis += 2000;
                Log.Info($"测试：钱包 CRC {w.Get(Assets.CRC):0}，污点 {w.Taint:0}");
            });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "My mixer: max heat (test)", () =>
            {
                var m = Session.Data.OwnMix; m.Heat = 1e4;
                Log.Info($"测试：自营混币器风声拉满，今晚查封概率 {m.SeizeChance(1, false):P0}");
            });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Mixer: honeypot now (test)", () =>
            {
                var d = Session.Data;
                d.Story.Rebrand(true); d.Story.After.Rebranded = true;
                d.Addresses["mixer_old"] = d.Address("mixer"); d.Addresses["mixer"] = ChainAddress.Make(d.Rng.NextUInt());
                Log.Info($"测试：混币器换成钓鱼混币器，费率 {Mixer.Fee:P1}");
            });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Tumbler: contact, crackdown tomorrow (test)", () => Log.Guard("联系人测试", () =>
            {
                Session.Data.Story.After.Contact = true;
                StoreStation.Instance.storeEventManager.QueueFuturEvent(OperationListSec.Crackdown(), 1, false);
                Log.Info("测试：Tumbler 成为联系人，镇压排到明天（玩家不知道）");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Tumbler: leak tonight (test)", () =>
            {
                var a = Session.Data.Story.After; a.Leaked = false; a.LeakDay = StoreStation.GetDayCounter();
                Log.Info($"测试：今晚泄露，出款记录 {Session.Data.Mixer.Returns.Count} 条");
            });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Tumbler: trial market now (test)", () => Log.Guard("审判盘测试", () =>
            {
                var d = Session.Data; var a = d.Story.After; int day = StoreStation.GetDayCounter();
                a.Verdict = -1; a.TrialMarket = a.TrialDone = false; a.Hints.Clear(); a.HintDay = -1;
                a.StartTrial(day - Tuning.TumblerTrialOpen, d.Rng);
                PredictGame.OpenTrial(d, day);
                Log.Info($"测试：审判盘开盘，第 {a.TrialDay} 天宣判，判决 {a.Verdict}");
                Session.Save();
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Tumbler: revenge tonight (test)", () =>
            {
                var a = Session.Data.Story.After; a.Revenged = false; a.RevengeDay = StoreStation.GetDayCounter();
                Log.Info("测试：今晚报复");
            });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Tumbler: BM rug tonight (test)", () =>
            {
                var a = Session.Data.Story.After; a.BmRugged = false; a.BmRugDay = StoreStation.GetDayCounter();
                Log.Info($"测试：今晚黑市跑路，在途 {Session.Data.Mixer.Pending.Count} 笔");
            });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Mixer: rebrand tonight (test)", () =>
            {
                var a = Session.Data.Story.After; a.Rebranded = false; a.ClosedDay = StoreStation.GetDayCounter() - Tuning.TumblerRebrandDays;
                Log.Info("测试：今晚换皮重开");
            });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Lower-level rep +30 (test)", () => StoreReputation.ModReputation(StoreReputation.FACTION_LOWER_LEVEL, 30));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Dump crimes and reputation (test)", () => Log.Guard("罪证与声望", () =>
            {
                var sd = PlayerStore.Instance.secData;
                var crimes = string.Join("，", sd.crimeTypes.Select((c, i) => c + " " + sd.crimeAmount[i]));
                string Rep(string f) => f + " " + (StoreReputation.GetStoreReputation(f)?.GetReputation().ToString() ?? "-");
                Log.Info($"测试：罪证 [{crimes}]，证据 {sd.evidenceLevel}；声望 {Rep(StoreReputation.FACTION_LOWER_LEVEL)}，{Rep(StoreReputation.FACTION_SECURITY)}，{Rep(StoreReputation.FACTION_BLACK_MARKET)}；现金 {PlayerStore.Instance.playerCash}");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Scams: phishing approval + scam pool (test)", () => Log.Guard("骗局补测", () =>
            {
                // 当前钱包：一个钓鱼授权（明晚转走 CRT）；一个必然是骗局的池子（存 500）和一个正规的时间锁池子（存 500 并申请取出）
                var d = Session.Data; int day = StoreStation.GetDayCounter(); var w = d.Hot;
                d.Scams.Pools.Clear(); d.Scams.Approvals.Clear();
                d.Scams.Approvals.Add(new Approval { Id = ++d.Scams.Seq, Wallet = w.Address, Asset = Assets.CRT, Spender = "P0inbase", Limit = double.PositiveInfinity, Scam = true, DrainDay = day + 1 });
                var bad = new YieldPool { Id = ++d.Scams.Seq, Name = "Test Scam", OpenDay = day - 1, Rate = 0.03, Reserve = 9999, Cap = 1e6, Scam = true };
                var ok = new YieldPool { Id = ++d.Scams.Seq, Name = "Test Lock", OpenDay = day - 1, Rate = 0.01, Reserve = 9999, Cap = 1e6, Timelock = true };
                d.Scams.Pools.Add(bad); d.Scams.Pools.Add(ok);
                d.Scams.Deposit(bad, w, 500, Session.Price); d.Scams.Deposit(ok, w, 500, Session.Price); d.Scams.Withdraw(ok, w, day);
                Log.Info($"测试：钓鱼授权 #{d.Scams.Approvals[0].Id}（明晚转走 CRT {w.Get(Assets.CRT):0}），骗局池存 500，时间锁池取款第 {day + Tuning.YieldLockDays} 天到账；CRC {w.Get(Assets.CRC):0}");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Perp: PSC long 1000 x2 (test)", () => Log.Guard("永续测试", () =>
            {
                var d = Session.Data; d.Poinbase.Add(Assets.CRC, 2000);
                // 先让价格偏离 7 日均线，资金费才不为零：补几个历史标记价
                d.Perps.Marks["PSC"] = new System.Collections.Generic.List<double> { d.Market.Price * 0.8, d.Market.Price * 0.8, d.Market.Price * 0.8 };
                var res = d.Perps.Open(d.Poinbase, "PSC", true, 1000, 2, d.Market.Price, StoreStation.GetDayCounter(), Session.Price);
                Log.Info($"测试：永续开多 {res}，现价 {d.Market.Price:0.00}");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Give key drive (test)", () => Items.Give(ModHelper.SpawnItem(Items.KeyDrive)));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Give GPU guide (test)", () => Items.Give(ModHelper.SpawnItem(Items.Guide)));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Predict: window = tomorrow (test)", () => { foreach (var m in Session.Data.Predict.Markets) { m.FromDay = m.ToDay = StoreStation.GetDayCounter() + 1; } Log.Info("测试：所有盘口窗口改成明天"); });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Perp fund 0 (test)", () => { Session.Data.Perps.InsuranceFund = 0; Log.Info("测试：保险基金清零"); });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "SPARK x0.3 (test)", () => { var st = Session.Data.Stable; st.Spark *= 0.3; Log.Info($"测试：SPARK 价格 {st.Spark:0.00}"); });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Wallet gift: CRC, CRA, PSC (test)", () => { var w = Session.Data.Hot; w.Add(Assets.CRA, 1000); w.Add(Assets.CRC, 1000); w.Add(Assets.CRT, 3000); w.Add(Assets.PSC, 20); w.Add(Assets.SPARK, 300); w.Basis += 5000; Log.Info($"测试：{WalletWindow.NameOf(w)} CRA {w.Get(Assets.CRA):0}"); });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Poinbase gift: CRC, PSC (test)", () => { var pb = Session.Data.Poinbase; pb.Add(Assets.CRC, 5000); pb.Add(Assets.PSC, 30); pb.Basis += 8000; Log.Info($"测试：Poinbase CRC {pb.Get(Assets.CRC):0}"); });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "CRA spiral warning (test)", () =>
            {
                var st = Session.Data.Stable; int day = StoreStation.GetDayCounter();
                st.CraWarnDay = day; st.CraEventDay = day + 3;
                Log.Info($"测试：CRA 预兆今天，挪用日第 {st.CraEventDay} 天");
            });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Give terminal (test)", () => { Items.Give(ModHelper.SpawnItem(Items.Terminal)); });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Hardware wallets (test)", () => { Items.Give(Items.NewHwWallet(false)); Items.Give(Items.NewHwWallet(true)); Log.Info("测试：给了一个正品、一个被投毒的硬件钱包"); });
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Chain drama (test)", () => Log.Guard("链上事件测试", () =>
            {
                // 今天拥堵；当前钱包最近转出过的地址立刻收到相似地址的 0 币转入；报纸和论坛各来一条钓鱼
                var d = Session.Data;
                int day = StoreStation.GetDayCounter();
                d.CongestedFrom = day; d.CongestedUntil = day + 1;
                var last = d.Hot.History.FirstOrDefault(r => !r.In);
                if (last != null) d.Chain(ChainAddress.Lookalike(last.Addr, d.Rng), d.Hot.Address, Assets.PSC, 0, zero: true);
                string fake = ChainAddress.Make(d.Rng.NextUInt());
                News.Show("pd_news_phish_test_" + day, L.T("news.phish.title"), L.T("news.phish.desc", fake), 2);
                d.Black.Forum.Add("phish:" + ChainAddress.Make(d.Rng.NextUInt()));
                Log.Info($"测试：拥堵到第 {d.CongestedUntil} 天；投毒 {(last != null ? last.Addr : "无转出记录")}；钓鱼 {fake}");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Dr. Jackson stock (test)", () =>
            {
                EmporiumEntry.Instance.ClearFrontInv();
                EmporiumEntry.Instance.ExpandFrontCounterGrid();
                StoreClientList.PlaceInventorInventory();
            });
        }

        public static void Unsubscribe()
        {
            ModHook.OnPlaceInventorInventoryItemLate -= OnPlace;
            ModHelper.RemoveDebugFunctions(ModInfo.Id);
        }

        private static void OnPlace(List<GameItem> owned)
        {
            if (Session.Data == null || !Unlock.IsUnlocked) return;
            Log.Guard("杰克逊博士上货", () =>
            {
                bool ownsRig = owned != null && owned.Any(i => i.identifier == Items.Rig);
                bool special = AfterhourEvents.IsActive("special_stock");
                var placed = new List<string>();
                foreach (var kind in Dealer.Stock(StoreStation.GetDayCounter(), ownsRig, special, Session.Data.Rng))
                {
                    var item = ModHelper.SpawnItem(kind == Dealer.Rig ? Items.Rig : Items.GpuId(kind));
                    bool ok = PlayerStore.Instance.TryAddDirectSellingItemToTable(item);
                    placed.Add(ok ? $"{kind}#{item.uniqueId}" : kind + "（柜台已满）");
                }
                // 硬件钱包（正品）：钱包和手上的硬件钱包加起来还没到上限时，有一定概率上一个
                int wallets = Session.Data.Wallets.Count + (owned?.Count(i => i.identifier == Items.HwWallet) ?? 0);
                if (wallets < Tuning.WalletMax && Session.Data.Rng.Chance(Tuning.WalletJacksonChance))
                {
                    var hw = Items.NewHwWallet(false);
                    placed.Add(PlayerStore.Instance.TryAddDirectSellingItemToTable(hw) ? $"硬件钱包#{hw.uniqueId}" : "硬件钱包（柜台已满）");
                }
                Log.Info("杰克逊博士上货：" + string.Join("，", placed));
            });
        }
    }
}

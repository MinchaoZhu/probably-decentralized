using System;
using System.Linq;
using Decentralized.Domain;

namespace Decentralized
{
    /// <summary>
    /// 本 mod 自己的剧情线：混币器运营者 Tumbler。解锁混币器、第 30 天起，四次上门（游戏的对话顾客）：
    /// 1 自我介绍；2 说治安部在查混币服务（当晚混币器被制裁几天）；3 求你藏密钥盘，三选一（藏 / 举报 / 卖给黑市）；4 结局。
    /// 进度存在 <see cref="StoryMods.Tumbler"/>，结果改动混币器（见 <see cref="StoryMods.Apply"/>）。
    /// 之后的剧情（<see cref="TumblerAfter"/>）：治安部探员、搜查、密钥泄露、审判盘、黑市跑路、报复、长期联系人、换皮重开。
    /// </summary>
    public static class TumblerStory
    {
        public static void Subscribe() => ModHook.OnGenerateCustomerLate += OnGenerate;
        public static void Unsubscribe() => ModHook.OnGenerateCustomerLate -= OnGenerate;

        public static void ForceVisit(StoreClientManager manager) => OnGenerate(manager);

        private static void OnGenerate(StoreClientManager manager)
        {
            var d = Session.Data;
            if (d == null || !Unlock.IsUnlocked || !Settings.TumblerStory) return;
            Log.Guard("Tumbler", () =>
            {
                int day = StoreStation.GetDayCounter();
                var t = d.Story.Tumbler;
                if (t.TryStart(day, Unlock.MixerUnlocked)) Log.Info($"Tumbler 剧情开始：第 {t.StartDay} 天第一次上门");
                var a = d.Story.After;
                if (a.AgentDue(t, day) || a.SearchDue(t, day))
                {
                    bool search = a.SearchDue(t, day);
                    manager.AddNextClient(search ? Search() : Agent());
                    Log.Info($"治安部探员上门（{(search ? "搜查" : "问话")}）");
                    Session.Save();
                    return;
                }
                int due = t.Due(day, a);
                if (due == 0) return;
                // 举报之后第 4 次不上门；其余的上门要等对话真正走完才算数（当天没聊完、被清场或读档重来，明天照样来）
                if (due == 4 && t.Choice == Tumbler.Report) { t.Visited(day); Log.Info("Tumbler 已被举报，不再上门"); Session.Save(); return; }
                var c = Visit(due, t.Choice);
                manager.AddNextClient(c);
                Log.Info($"Tumbler 第 {due} 次上门（选择 {t.Choice}）");
                Session.Save();
            });
        }

        /// <summary>夜间：第 2 次上门那天，治安部封了混币器节点。</summary>
        public static void Night(SaveData d, int day)
        {
            var t = d.Story.Tumbler;
            if (t.Stage == 2 && t.VisitedDay == day && !d.Story.Has("tumbler_sanction"))
            {
                d.Story.Applied.Add("tumbler_sanction");
                d.Mixer.SanctionedUntil = System.Math.Max(d.Mixer.SanctionedUntil, day + 4);
                News.Show("pd_news_tumbler_sanction", L.T("news.tumbler.sanction.title"), L.T("news.tumbler.sanction.desc"), 3, 8);
            }
            After(d, day);
        }

        /// <summary>四次上门之后的夜间：泄露、庭审新闻与宣判、黑市跑路、报复、联系人的镇压提示、换皮重开。</summary>
        private static void After(SaveData d, int day)
        {
            var a = d.Story.After;
            var ps = PlayerStore.Instance;
            if (a.LeakDay >= 0 && day >= a.LeakDay && !a.Leaked)
            {
                a.Leaked = true;
                var r = d.Mixer.Leak(day);
                int fraud = (int)Math.Round(r.Fraud);
                if (fraud > 0) ps.secData.CommitCrime("FRAUD", fraud);
                if (r.Count == 0) ps.AddNightLog(L.T("nightlog.tumbler.leak.none"));
                else ps.AddNightLog(L.T("nightlog.tumbler.leak", r.Count, Fmt.Credits(r.Cleaned), fraud), "cc7755");
                News.Show("pd_news_tumbler_leak", L.T("news.tumbler.leak.title"), L.T("news.tumbler.leak.desc"), 3, 8);
                Log.Info($"Tumbler 密钥泄露：还原 {r.Count} 笔出款，洗掉的污点 {r.Cleaned:0}，记诈骗罪证 {fraud}");
            }
            bool? hint = a.Hint(day, d.Rng);
            if (hint != null)
            {
                int n = a.Hints.Count, v = (n - 1) % 3 + 1;
                string side = hint.Value ? "pro" : "def";
                News.Show("pd_news_trial_" + day, L.T("news.trial." + side + ".title", n), L.T("news.trial." + side + ".desc" + v), 1, 7);
                Log.Info($"庭审第 {n} 天：{(hint.Value ? "控方" : "辩方")}占上风，公开定罪概率 {a.TrialPublic():0.00}（判决 {a.Verdict}）");
            }
            if (a.Verdict >= 0 && !a.TrialDone && day >= a.TrialDay)
            {
                a.TrialDone = true;
                if (a.Verdict == 1) News.Show("pd_news_trial_verdict", L.T("news.trial.convict.title"), L.T("news.trial.convict.desc"), 3, 8);
                else
                {
                    a.RevengeDay = day + Tuning.TumblerRevengeDelay;
                    News.Show("pd_news_trial_verdict", L.T("news.trial.acquit.title"), L.T("news.trial.acquit.desc"), 3, 8);
                }
                Log.Info($"Tumbler 宣判：{(a.Verdict == 1 ? "定罪" : "无罪")}");
            }
            if (a.BmRugDay >= 0 && day >= a.BmRugDay && !a.BmRugged)
            {
                a.BmRugged = true;
                foreach (var m in d.Mixer.RugAll()) ps.AddNightLog(L.T("nightlog.mixer_rug", Fmt.Num(m.Amount), m.Asset), "cc7755");
                d.Story.Apply("tumbler_bm_rug", day);
                a.ClosedDay = day;
                a.RevengeDay = day + Tuning.TumblerRevengeDelay;
                News.Show("pd_news_tumbler_bmrug", L.T("news.tumbler.bmrug.title"), L.T("news.tumbler.bmrug.desc"), 4, 8);
                Log.Info("黑市接手的混币器卷款跑路");
            }
            if (a.RevengeDay >= 0 && day >= a.RevengeDay && !a.Revenged)
            {
                a.Revenged = true;
                Revenge(d, day);
            }
            if (a.Contact) Tip(d, day, a);
            if (a.RebrandDue(day))
            {
                a.Rebranded = true;
                bool honeypot = d.Rng.Chance(Tuning.TumblerHoneypot);
                d.Story.Rebrand(honeypot);
                d.Addresses["mixer_old"] = d.Address("mixer");
                d.Addresses["mixer"] = ChainAddress.Make(d.Rng.NextUInt());
                News.Show("pd_news_mixer_rebrand", L.T("news.mixer.rebrand.title", L.T("mixer.brand")), L.T("news.mixer.rebrand.desc", L.T("mixer.brand")), 4);
                Log.Info($"混币器换皮重开：{L.T("mixer.brand")}，钓鱼 {honeypot}，费率 {Mixer.Fee:P1}");
            }
        }

        /// <summary>
        /// Tumbler 报复：先留一张纸条。按概率给当前钱包植入木马（剪贴板劫持、钱够多后被转空，和商城的投毒钱包一样）；
        /// 否则每个钱包对最近的转出对象各收到一笔来自相似地址的 0 币转入（地址投毒）。
        /// </summary>
        private static void Revenge(SaveData d, int day)
        {
            var ps = PlayerStore.Instance;
            ps.AddNightLog(L.T("nightlog.tumbler.note"), "cc7755");
            if (d.Connected && !d.Hot.Poisoned && d.Rng.Chance(Tuning.TumblerRevengeWalletShare))
            {
                d.Hot.Poisoned = true;
                Log.Info($"Tumbler 报复：钱包 {d.Hot.Address} 被植入木马");
                return;
            }
            int n = 0;
            foreach (var w in d.Wallets)
            {
                var targets = w.History.Where(h => !h.In && h.Amount > 0 && d.Own(h.Addr) == null).Select(h => h.Addr).Distinct().Take(Tuning.TumblerRevengePoison).ToList();
                if (targets.Count == 0) targets.Add(d.Address("poinbase"));
                foreach (var t in targets) { d.PoisonPlans.Add(new PoisonPlan { Wallet = w.Address, Target = t, Day = day + d.Rng.Range(1, Tuning.TxPoisonDelayMax) }); n++; }
            }
            Log.Info($"Tumbler 报复：安排 {n} 笔地址投毒");
        }

        /// <summary>长期联系人：游戏排期里明天开始的镇压（玩家知不知道都算），提前一晚提示，每个只提示一次。</summary>
        private static void Tip(SaveData d, int day, TumblerAfter a)
        {
            var mgr = StoreStation.Instance?.storeEventManager;
            if (mgr == null) return;
            for (int i = 0; i < mgr.futurEvents.Count && i < mgr.futurEventsDay.Count; i++)
            {
                var e = mgr.futurEvents[i];
                int start = day + mgr.futurEventsDay[i];
                if (start != day + 1 || (e.identifier != "crackdown" && e.identifier != "sec_martial_law") || a.Tipped.Contains(start)) continue;
                a.Tipped.Add(start);
                PlayerStore.Instance.AddNightLog(L.T("nightlog.tumbler.tip"));
                Log.Info($"Tumbler 提示：第 {start} 天开始 {e.identifier}");
            }
            a.Tipped.RemoveAll(x => x < day - 30);
        }

        private static StoreClient New()
        {
            // 每次上门是同一个人：外观第一次上门时定下来
            var t = Session.Data.Story.Tumbler;
            if (string.IsNullOrEmpty(t.Sprite)) t.Sprite = SpriteDict.GetRandomShadySpriteName();
            var c = new StoreClient
            {
                identifier = "pd_tumbler", displayName = L.T("tumbler.name"), spriteName = t.Sprite,
                clientIntent = StoreClient.ClientIntent.DIALOGUE, nonShootable = true, dismissable = false,
            };
            ModHelper.SetClientSource(c, ModInfo.Id);
            return c;
        }

        /// <summary>治安部探员（问话和搜查是同一个人）。</summary>
        private static StoreClient NewAgent()
        {
            var a = Session.Data.Story.After;
            if (string.IsNullOrEmpty(a.AgentSprite)) a.AgentSprite = SpriteDict.GetRandomSecSpriteName();
            var c = new StoreClient
            {
                identifier = "pd_sec_agent", displayName = L.T("agent.name"), spriteName = a.AgentSprite,
                clientIntent = StoreClient.ClientIntent.DIALOGUE, nonShootable = true, dismissable = false,
            };
            ModHelper.SetClientSource(c, ModInfo.Id);
            return c;
        }

        /// <summary>第 2、3 次上门之间：探员来问 Tumbler 有没有来过。配合：治安部声望 +；否认：没有影响。</summary>
        private static StoreClient Agent()
        {
            var c = NewAgent();
            var ask = new Dialogue(); var yes = new Dialogue(); var no = new Dialogue();
            yes.SetText("PLAYER", L.T("agent.choice.yes")).NextDialogue().SetText(c.displayName, L.T("agent.yes1")).SetEndAction(() => AgentDone(TumblerAfter.Cooperate));
            no.SetText("PLAYER", L.T("agent.choice.no")).NextDialogue().SetText(c.displayName, L.T("agent.no1")).SetEndAction(() => AgentDone(TumblerAfter.Deny));
            Lines(c, c.mainDialogue, "agent.v1.", 3).SetNextDialogue(ask);
            ask.SetText(c.displayName, L.T("agent.ask"))
                .AddChoice(DirectoryMaster.Item("$"), "-", "", yes, () => { })
                .AddChoice(DirectoryMaster.Item("$"), "-", "", no, () => { });
            c.AddBasicDialog();
            c.CompleteClientCreation();
            return c;
        }

        private static void AgentDone(int choice)
        {
            Log.Guard("探员问话结束", () =>
            {
                var a = Session.Data.Story.After;
                if (a.Agent == 0)
                {
                    a.Agent = choice; a.AgentDay = StoreStation.GetDayCounter();
                    if (choice == TumblerAfter.Cooperate) StoreReputation.ModSecReputation(Tuning.TumblerAgentSecRep);
                }
                Log.Info($"探员问话：{(choice == TumblerAfter.Cooperate ? "配合" : "否认")}");
                Session.Save();
            });
            Leave();
        }

        private static GameItem FindDrive() => EmporiumEntry.Instance?.GetAllOwnedItems().FirstOrDefault(i => i != null && i.identifier == Items.KeyDrive);

        /// <summary>配合过探员、又帮 Tumbler 藏了密钥盘：第 4 次上门前一天来搜查，搜到就没收（记违禁品罪证），当晚泄露。</summary>
        private static StoreClient Search()
        {
            var c = NewAgent();
            bool found = FindDrive() != null;
            Lines(c, c.mainDialogue, "agent.search.", 2).NextDialogue().SetText(c.displayName, L.T(found ? "agent.search.found" : "agent.search.none")).SetEndAction(() =>
            {
                Log.Guard("探员搜查", () =>
                {
                    var a = Session.Data.Story.After;
                    int day = StoreStation.GetDayCounter();
                    if (a.Searched) return;
                    a.Searched = true; a.SearchDay = day;
                    var drive = FindDrive();
                    if (drive != null)
                    {
                        drive.Destroy();
                        a.Seized = true;
                        PlayerStore.Instance.secData.CommitCrime("CONTRABAND_TRAFFICKING", Tuning.TumblerSearchCrime);
                        a.LeakTonight(day);
                    }
                    Log.Info($"探员搜查：{(drive != null ? "搜走了密钥盘" : "没搜到")}");
                    Session.Save();
                });
                Leave();
            });
            c.AddBasicDialog();
            c.CompleteClientCreation();
            return c;
        }

        private static Dialogue Lines(StoreClient c, Dialogue first, string prefix, int n)
        {
            var dl = first.SetText(c.displayName, L.T(prefix + "1"));
            for (int i = 2; i <= n; i++) dl = dl.NextDialogue().SetText(c.displayName, L.T(prefix + i));
            return dl;
        }

        private static StoreClient Visit(int n, int choice)
        {
            var c = New();
            var d = Session.Data;
            switch (n)
            {
                case 1: Lines(c, c.mainDialogue, "tumbler.v1.", 4).SetEndAction(() => Finish(1)); break;
                case 2: Lines(c, c.mainDialogue, "tumbler.v2.", 3).SetEndAction(() => Finish(2)); break;
                case 3:
                    {
                        var ask = new Dialogue();
                        var hide = new Dialogue(); var report = new Dialogue(); var sell = new Dialogue();
                        hide.SetText("PLAYER", L.T("tumbler.v3.choice.hide")).NextDialogue().SetText(c.displayName, L.T("tumbler.v3.thanks1")).NextDialogue().SetText(c.displayName, L.T("tumbler.v3.thanks2")).SetEndAction(Leave);
                        sell.SetText("PLAYER", L.T("tumbler.v3.choice.sell")).NextDialogue().SetText(c.displayName, L.T("tumbler.v3.thanks1")).NextDialogue().SetText(c.displayName, L.T("tumbler.v3.thanks2")).SetEndAction(Leave);
                        report.SetText("PLAYER", L.T("tumbler.v3.choice.report")).NextDialogue().SetText(c.displayName, L.T("tumbler.v3.report1")).SetEndAction(Leave);
                        Lines(c, c.mainDialogue, "tumbler.v3.", 3).SetNextDialogue(ask);
                        ask.SetText(c.displayName, L.T("tumbler.v3.ask"))
                            .AddChoice(DirectoryMaster.Item("$"), "-", "", hide, () => Choose(Tumbler.Hide))
                            .AddChoice(DirectoryMaster.Item("$"), "-", "", report, () => Choose(Tumbler.Report))
                            .AddChoice(DirectoryMaster.Item("$"), "-", "", sell, () => Choose(Tumbler.Sell));
                        break;
                    }
                case 4:
                    if (choice == Tumbler.Hide)
                    {
                        bool has = FindDrive() != null;
                        if (d.Story.After.Seized) Lines(c, c.mainDialogue, "tumbler.v4.seized.", 3).SetEndAction(() => Finish(4));
                        else if (has) Lines(c, c.mainDialogue, "tumbler.v4.back.", 3).SetEndAction(() => { Returned(); Finish(4); });
                        // 盘不在手上：按落到治安部算，当晚泄露
                        else Lines(c, c.mainDialogue, "tumbler.v4.lost.", 3).SetEndAction(() => { Session.Data.Story.After.LeakTonight(StoreStation.GetDayCounter()); Finish(4); });
                    }
                    else if (choice == Tumbler.Sell) Lines(c, c.mainDialogue, "tumbler.v4.sold.", 3).SetEndAction(() => Finish(4));
                    else Lines(c, c.mainDialogue, "tumbler.v4.none.", 1).SetEndAction(() => Finish(4));
                    break;
            }
            c.AddBasicDialog();
            c.CompleteClientCreation();
            return c;
        }

        private static void Leave() => DialogUIManager.Instance.CurrentClientLeave();

        /// <summary>第 n 次上门的对话走完：进度推进一步（只在这一次还没记过时），存档，顾客离开。</summary>
        private static void Finish(int n)
        {
            Log.Guard("Tumbler 上门结束", () =>
            {
                var t = Session.Data.Story.Tumbler;
                if (t.Stage == n - 1) t.Visited(StoreStation.GetDayCounter());
                Log.Info($"Tumbler 第 {n} 次上门结束，进度 {t.Stage}");
                Session.Save();
            });
            Leave();
        }

        /// <summary>第 3 次上门做出的选择。</summary>
        private static void Choose(int choice)
        {
            Log.Guard("Tumbler 选择", () =>
            {
                var d = Session.Data;
                d.Story.Tumbler.Choice = choice;
                if (d.Story.Tumbler.Stage == 2) d.Story.Tumbler.Visited(StoreStation.GetDayCounter());
                int day = StoreStation.GetDayCounter();
                switch (choice)
                {
                    case Tumbler.Hide:
                        Items.Give(ModHelper.SpawnItem(Items.KeyDrive));
                        break;
                    case Tumbler.Report:
                        d.Story.Apply("tumbler_reported", day);
                        d.Story.After.LeakTonight(day);
                        d.Story.After.StartTrial(day, d.Rng);
                        ModHelper.AddCredit(Tuning.TumblerBounty, true);
                        StoreReputation.ModSecReputation(Tuning.TumblerSecRep);
                        News.Show("pd_news_tumbler_caught", L.T("news.tumbler.caught.title"), L.T("news.tumbler.caught.desc"), 4, 8);
                        break;
                    case Tumbler.Sell:
                        d.Story.Apply("tumbler_sold", day);
                        d.Story.After.StartSold(day, d.Rng);
                        d.Hot.AddDirty(Assets.CRT, Tuning.TumblerSellCrt, Tuning.TumblerSellCrt * Tuning.TumblerSellTaint); d.Hot.Basis += Tuning.TumblerSellCrt;
                        d.Chain(d.Address("platform"), d.Hot.Address, Assets.CRT, Tuning.TumblerSellCrt);
                        StoreReputation.ModReputation(StoreReputation.FACTION_BLACK_MARKET, Tuning.TumblerBmRep);
                        News.Show("pd_news_tumbler_sold", L.T("news.tumbler.sold.title"), L.T("news.tumbler.sold.desc"), 4, 8);
                        break;
                }
                Log.Info($"Tumbler：选择 {choice}");
                Session.Save();
            });
        }

        /// <summary>第 4 次上门取回密钥盘：收走物品，谢礼进当前钱包，混币器降费、匿名集变大。</summary>
        private static void Returned()
        {
            Log.Guard("Tumbler 取回密钥盘", () =>
            {
                var d = Session.Data;
                var drive = FindDrive();
                if (drive == null) return;
                drive.Destroy();
                d.Story.Apply("tumbler_kept", StoreStation.GetDayCounter());
                d.Story.After.Contact = true;
                d.Hot.Add(Assets.CRT, Tuning.TumblerGift); d.Hot.Basis += Tuning.TumblerGift;
                d.Chain(d.Address("tumbler"), d.Hot.Address, Assets.CRT, Tuning.TumblerGift);
                PlayerStore.Instance.AddNightLog(L.T("nightlog.tumbler.gift", Fmt.Num(Tuning.TumblerGift), WalletWindow.NameOf(d.Hot)));
                Session.Save();
            });
        }
    }
}

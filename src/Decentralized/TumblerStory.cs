using System.Linq;
using Decentralized.Domain;

namespace Decentralized
{
    /// <summary>
    /// 本 mod 自己的剧情线：混币器运营者 Tumbler。解锁混币器、第 30 天起，四次上门（游戏的对话顾客）：
    /// 1 自我介绍；2 说治安部在查混币服务（当晚混币器被制裁几天）；3 求你藏密钥盘，三选一（藏 / 举报 / 卖给黑市）；4 结局。
    /// 进度存在 <see cref="StoryMods.Tumbler"/>，结果改动混币器（见 <see cref="StoryMods.Apply"/>）。
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
                int due = t.Due(day);
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
        }

        private static StoreClient New()
        {
            var c = new StoreClient
            {
                identifier = "pd_tumbler", displayName = L.T("tumbler.name"), spriteName = SpriteDict.GetRandomShadySpriteName(),
                clientIntent = StoreClient.ClientIntent.DIALOGUE, nonShootable = true, dismissable = false,
            };
            ModHelper.SetClientSource(c, ModInfo.Id);
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
                        bool has = EmporiumEntry.Instance != null && EmporiumEntry.Instance.GetAllOwnedItems().Any(i => i != null && i.identifier == Items.KeyDrive);
                        if (has) Lines(c, c.mainDialogue, "tumbler.v4.back.", 3).SetEndAction(() => { Returned(); Finish(4); });
                        else Lines(c, c.mainDialogue, "tumbler.v4.lost.", 3).SetEndAction(() => Finish(4));
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
                        ModHelper.AddCredit(Tuning.TumblerBounty, true);
                        StoreReputation.ModSecReputation(Tuning.TumblerSecRep);
                        News.Show("pd_news_tumbler_caught", L.T("news.tumbler.caught.title"), L.T("news.tumbler.caught.desc"), 4, 8);
                        break;
                    case Tumbler.Sell:
                        d.Story.Apply("tumbler_sold", day);
                        d.Hot.Add(Assets.CRT, Tuning.TumblerSellCrt); d.Hot.Taint += Tuning.TumblerSellCrt * 0.5; d.Hot.Basis += Tuning.TumblerSellCrt;
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
                var drive = EmporiumEntry.Instance?.GetAllOwnedItems().FirstOrDefault(i => i != null && i.identifier == Items.KeyDrive);
                if (drive == null) return;
                drive.Destroy();
                d.Story.Apply("tumbler_kept", StoreStation.GetDayCounter());
                d.Hot.Add(Assets.CRT, Tuning.TumblerGift); d.Hot.Basis += Tuning.TumblerGift;
                d.Chain(d.Address("tumbler"), d.Hot.Address, Assets.CRT, Tuning.TumblerGift);
                PlayerStore.Instance.AddNightLog(L.T("nightlog.tumbler.gift", Fmt.Num(Tuning.TumblerGift), WalletWindow.NameOf(d.Hot)));
                Session.Save();
            });
        }
    }
}

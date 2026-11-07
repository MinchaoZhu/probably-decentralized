using System;
using Decentralized.Domain;

namespace Decentralized
{
    /// <summary>
    /// 收黑币的上门顾客（游戏的对话顾客）：游戏里出了事，有人拿着来路不明的币折价换现金，见 <see cref="HotCoins"/>。
    /// 一天最多一位。收下：付现金，币进已连接的钱包（PSC、CRT 全额带污点）；钓鱼执法：现金被没收并记销赃罪证，币不会到。
    /// </summary>
    public static class HotCoinSeller
    {
        private static int seenDay = -1;

        public static void Subscribe()
        {
            ModHook.OnGenerateCustomerLate += OnGenerate;
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Hot coin seller (test)", () => Log.Guard("收黑币（测试）", () =>
            {
                var d = Session.Data;
                string[] assets = { Assets.CRT, Assets.PSC, Assets.Ponero };
                var c = Create(assets[d.Rng.Range(0, assets.Length - 1)]);
                if (c == null) return;
                PlayerStore.Instance.currentClientInstance = null;
                PlayerStore.Instance.storeClientManager.clientStack.Insert(0, c);
                PlayerStore.Instance.TryCallNextClient();
            }));
        }

        public static void Unsubscribe() => ModHook.OnGenerateCustomerLate -= OnGenerate;

        private static void OnGenerate(StoreClientManager manager)
        {
            var d = Session.Data;
            if (d == null || !Unlock.IsUnlocked || !d.Connected) return;
            Log.Guard("收黑币", () =>
            {
                int day = StoreStation.GetDayCounter();
                if (day == seenDay) return;
                seenDay = day;
                bool ponero = Unlock.BlackUnlocked && BlackMarket.DeskOpen(StoreReputation.GetBMReputation());
                string asset = HotCoins.Pick(Night.ActiveEvents(), ponero, d.Rng);
                if (asset == null) return;
                var c = Create(asset);
                if (c != null) manager.AddClient(c);
            });
        }

        private static StoreClient Create(string asset)
        {
            var d = Session.Data;
            bool crackdown = Night.ActiveEvents().Contains(MarketEvent.Crackdown);
            var o = HotCoins.Make(asset, Session.Price(asset), Session.Rent(), crackdown, d.Rng);
            if (o == null) return null;
            var c = new StoreClient
            {
                identifier = "pd_hot_coins", displayName = L.T("hot.name"),
                spriteName = asset == Assets.CRT ? SpriteDict.GetRandomBMMaleSpriteName() : asset == Assets.PSC ? SpriteDict.GetRandomRevSpriteName() : SpriteDict.GetRandomShadySpriteName(),
                clientIntent = StoreClient.ClientIntent.DIALOGUE,
            };
            // 选项的动作先于下一句执行：在动作里结算，再把卖家的回话换成结果
            var yes = new Dialogue();
            var reply = yes.SetText("PLAYER", L.T("hot.say.yes")).NextDialogue();
            reply.SetText(c.displayName, L.T("hot.refused")).SetEndAction(Leave);
            var no = new Dialogue();
            no.SetText("PLAYER", L.T("hot.say.no")).NextDialogue().SetText(c.displayName, L.T("hot.refused")).SetEndAction(Leave);
            c.mainDialogue.SetText(c.displayName, L.T("hot.intro." + asset)).NextDialogue()
                .SetText(c.displayName, L.T("hot.offer", Fmt.Num(o.Qty), asset, Fmt.Credits(o.Value), Fmt.Credits(o.Cash)))
                .AddChoice(DirectoryMaster.Item("yes"), L.T("hot.choice.yes"), "", yes, () => reply.SetText(c.displayName, L.T(Accept(o))))
                .AddChoice(DirectoryMaster.Item("cancel"), L.T("hot.choice.no"), "", no, null);
            ModHelper.SetClientSource(c, ModInfo.Id);
            c.AddBasicDialog();
            c.CompleteClientCreation();
            Log.Info($"收黑币上门：{o.Qty:0.##} {asset}，市值 {o.Value:0}，要价 {o.Cash}{(o.Sting ? "（钓鱼执法）" : "")}");
            return c;
        }

        /// <summary>选了收下：现金不够或没连钱包就作罢；钓鱼执法没收现金、记罪证；否则付现金、币进钱包。返回卖家回话的键。</summary>
        private static string Accept(HotOffer o)
        {
            string line = "hot.refused";
            Log.Guard("收黑币成交", () =>
            {
                var d = Session.Data;
                if (!d.Connected) { line = "hot.nowallet"; return; }
                if (PlayerStore.Instance.playerCash < o.Cash) { line = "hot.nocash"; return; }
                ModHelper.AddCredit(-o.Cash, false);
                if (o.Sting)
                {
                    PlayerStore.Instance.secData.CommitCrime("FENCING", (int)Math.Min(Tuning.CrimeEventCap, o.Cash));
                    line = "hot.sting";
                    Log.Info($"收黑币：钓鱼执法，现金 {o.Cash} 没收，记销赃罪证");
                }
                else
                {
                    HotCoins.Receive(d.Hot, o);
                    d.Chain(ChainAddress.Make(d.Rng.NextUInt()), d.Hot.Address, o.Asset, o.Qty);
                    line = "hot.done";
                    Log.Info($"收黑币：{o.Qty:0.##} {o.Asset} 进 {WalletWindow.NameOf(d.Hot)}，付现金 {o.Cash}，钱包污点 {d.Hot.Taint:0}");
                }
                Session.Save();
            });
            return line;
        }

        private static void Leave() => DialogUIManager.Instance.CurrentClientLeave();
    }
}

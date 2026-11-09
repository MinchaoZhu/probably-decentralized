using System;
using System.Collections.Generic;
using Decentralized.Domain;

namespace Decentralized
{
    /// <summary>
    /// 换币客户（游戏的对话顾客），由切到"换币客户"的暗灯招来，见 <see cref="OtcLamp"/> 与 <see cref="Otc"/>。
    /// 客户说明来意后选"去终端交易"：打开交易终端的柜台交易页（<see cref="TerminalWindow"/>），客户留在柜台前等着。
    /// 在终端里定成交数量（可以只成交一部分）：卖币的客户收现金、币直接进已连接的钱包（便衣会没收现金并记销赃罪证）；
    /// 买币的客户弹出 PLEDGER，收款地址和数量已填好，签名确认后客户付现金。客户走了（被打发、没成交）就作废。
    /// 原版取顾客名字时也会调用工厂：只有在生成顾客的那段时间里（普通顾客回调之间，原版在这里按展示柜生成顾客）才生成报价、写链上账本。
    /// </summary>
    public static class OtcClient
    {
        public const string Id = OtcLamp.Target;
        private static bool spawning;

        /// <summary>正在柜台前、已经去终端交易的客户：报价、收款地址和顾客对象（用来判断他还在不在）。</summary>
        public static OtcOffer Active;
        public static string ActiveAddress;
        private static StoreClient activeClient;

        /// <summary>买币客户的账单：确认到账或作废之前一直记着。</summary>
        private static WalletWindow.Bill pendingBill;
        /// <summary>还没签名付款的换币账单：钱包没有终端账单时用它（见 <see cref="WalletWindow.Pending"/>）；签名后清掉，没成交（挤出内存池）时放回来。</summary>
        public static WalletWindow.Bill Standing;

        public static void Subscribe()
        {
            StoreClientListDict.storeClientDict[Id] = Create;
            ModHook.OnGenerateCustomerNormal += Open;
            ModHook.OnGenerateCustomerLate += Close;
            ModHook.OnGoingSleepEarly += Drop;
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "OTC client (test)", () => Log.Guard("换币客户（测试）", () =>
            {
                spawning = true;
                StoreClient c;
                try { c = Create(); } finally { spawning = false; }
                PlayerStore.Instance.currentClientInstance = null;
                PlayerStore.Instance.storeClientManager.clientStack.Insert(0, c);
                PlayerStore.Instance.TryCallNextClient();
            }));
        }

        public static void Unsubscribe()
        {
            StoreClientListDict.storeClientDict.Remove(Id);
            ModHook.OnGenerateCustomerNormal -= Open;
            ModHook.OnGenerateCustomerLate -= Close;
            ModHook.OnGoingSleepEarly -= Drop;
        }

        /// <summary>这张账单是不是换币客户开的（终端不让它独占 Web3、灰网页）。</summary>
        public static bool IsBill(WalletWindow.Bill bill) => bill != null && bill == pendingBill;

        private static void Open(StoreClientManager _) => spawning = true;
        private static void Close(StoreClientManager _) => spawning = false;

        /// <summary>顾客表里的工厂。</summary>
        private static StoreClient Create()
        {
            StoreClient c = null;
            if (spawning && Session.Data != null && Unlock.OtcUnlocked) Log.Guard("换币客户上门", () => c = Real());
            return c ?? Shell();
        }

        /// <summary>只有名字的顾客（原版取名字时用；生成失败时上门说一句就走）。</summary>
        private static StoreClient Shell()
        {
            var c = Base();
            c.mainDialogue.SetText(c.displayName, L.T("otc.refused")).SetEndAction(Leave);
            Finish(c);
            return c;
        }

        private static StoreClient Base() => new StoreClient
        {
            identifier = Id, displayName = L.T("otc.name"), spriteName = SpriteDict.GetRandomShadySpriteName(),
            clientIntent = StoreClient.ClientIntent.DIALOGUE,
        };

        private static void Finish(StoreClient c)
        {
            ModHelper.SetClientSource(c, ModInfo.Id);
            c.AddBasicDialog();
            c.CompleteClientCreation();
        }

        private static StoreClient Real()
        {
            var d = Session.Data;
            var side = Otc.PickSide(d.Rng);
            bool ponero = Unlock.BlackUnlocked && BlackMarket.DeskOpen(StoreReputation.GetBMReputation());
            var o = side == OtcSide.Sell ? Otc.MakeSell(Night.ActiveEvents(), ponero, Session.Price, Session.Rent(), d.Rng, StoryMods.Current.OtcSting(StoreStation.GetDayCounter())) : Otc.MakeBuy(Session.Price, Session.Rent(), d.Rng);
            if (o == null) return null;
            string addr = d.Address("otc:" + (++d.Otc.Seq));
            var c = Base();
            if (o.Side == OtcSide.Sell) WriteHistory(d, o, addr);
            Build(c, o, addr);
            Finish(c);
            Log.Info(o.Side == OtcSide.Sell
                ? $"换币客户（卖币）：{o.Qty:0.##} {o.Asset}，市值 {o.Value:0}，污点比例 {o.TaintRatio:0.00}，要价 {o.Cash}{(o.Sting ? "（便衣）" : "")}"
                : $"换币客户（买币）：{o.Qty:0.##} {o.Asset}，市值 {o.Value:0}，出价 {o.Cash}");
            Session.Save();
            return c;
        }

        /// <summary>客户地址的来历写进链上账本（Ponero 不上账本）：脏的来源是混币器和黑市平台，干净的是 Poinbase 和 DEX。</summary>
        private static void WriteHistory(SaveData d, OtcOffer o, string addr)
        {
            if (o.Asset == Assets.Ponero) return;
            var dirty = new List<string> { d.Address("mixer"), d.Address("platform") };
            var clean = new List<string> { d.Address("poinbase"), d.Address("dex") };
            foreach (var kv in Otc.History(o, dirty, clean, d.Rng)) d.Chain(kv.Key, addr, o.Asset, o.Qty * kv.Value);
        }

        /// <summary>对话：来意和报价，选"去终端交易"打开柜台交易页、客户留下等着；选"算了"客户走人。</summary>
        private static void Build(StoreClient c, OtcOffer o, string addr)
        {
            var trade = new Dialogue();
            trade.SetText("PLAYER", L.T("otc.say.trade")).NextDialogue().SetText(c.displayName, L.T("otc.wait"));
            var no = new Dialogue();
            no.SetText("PLAYER", L.T("otc.say.no")).NextDialogue().SetText(c.displayName, L.T("otc.refused")).SetEndAction(Leave);
            string intro = o.Side == OtcSide.Buy ? "otc.intro.buy" : o.TaintRatio >= 1 || o.Asset == Assets.Ponero ? "otc.intro." + o.Asset : "otc.intro.sell";
            string offer = o.Side == OtcSide.Buy
                ? L.T("otc.buy.ask", Fmt.Num(o.Qty), o.Asset, Fmt.Credits(o.Value), Fmt.Credits(o.Cash))
                : L.T("otc.offer", Fmt.Num(o.Qty), o.Asset, Fmt.Credits(o.Value), Fmt.Credits(o.Cash), ChainAddress.Short(addr));
            c.mainDialogue.SetText(c.displayName, L.T(intro, o.Asset)).NextDialogue()
                .SetText(c.displayName, offer)
                .AddChoice(DirectoryMaster.Item("yes"), L.T("otc.say.trade"), "", trade, () => Begin(c, o, addr))
                .AddChoice(DirectoryMaster.Item("cancel"), L.T("otc.say.no"), "", no, null);
        }

        /// <summary>去终端交易：记下在场的客户，打开交易终端的柜台交易页。</summary>
        private static void Begin(StoreClient c, OtcOffer o, string addr)
        {
            Log.Guard("换币客户去终端交易", () =>
            {
                Active = o; ActiveAddress = addr; activeClient = c;
                TerminalWindow.OpenOtc();
            });
        }

        /// <summary>客户还在柜台前吗（没被打发、没走）。不在了就作废这笔交易。</summary>
        public static bool Present()
        {
            if (Active == null) return false;
            var cur = PlayerStore.Instance?.currentClientInstance?.GetClientBlueprint();
            if (cur != null && cur == activeClient) return true;
            Log.Info("换币客户已经离开，交易作废");
            Drop();
            return false;
        }

        /// <summary>
        /// 终端里点"成交"：qty 是成交的数量（不超过报价）。卖币：付现金、币进已连接的钱包；买币：开账单、弹出钱包（地址已填好）。
        /// 返回终端显示的消息（null 表示弹出了钱包，等签名）。
        /// </summary>
        public static string Deal(double qty)
        {
            if (!Present()) return L.T("otc.msg.gone");
            var part = Otc.Part(Active, qty);
            if (part == null) return L.T("msg.amount");
            return part.Side == OtcSide.Sell ? Take(part) : Give(part);
        }

        /// <summary>收下客户卖的币：没连钱包、现金不够就作罢；便衣没收现金、记销赃罪证；否则付现金、币进钱包。客户随后离开。</summary>
        private static string Take(OtcOffer o)
        {
            var d = Session.Data;
            var ps = PlayerStore.Instance;
            if (!d.Connected) return L.T("web3.err.noconn");
            if (ps.playerCash < o.Cash) return L.T("otc.msg.cash");
            string msg = null;
            Log.Guard("换币客户成交（卖币）", () =>
            {
                ModHelper.AddCredit(-o.Cash, false);
                if (o.Sting)
                {
                    ps.secData.CommitCrime("FENCING", (int)Math.Min(Tuning.CrimeEventCap, o.Cash));
                    Say(L.T("otc.sting"));
                    msg = L.T("otc.msg.sting", Fmt.Credits(o.Cash));
                    Log.Info($"换币客户：便衣，现金 {o.Cash} 没收，记销赃罪证");
                }
                else
                {
                    Otc.Receive(d.Hot, o);
                    d.Chain(ActiveAddress, d.Hot.Address, o.Asset, o.Qty);
                    d.Otc.Record(o.Cash);
                    msg = L.T("otc.msg.bought", Fmt.Num(o.Qty), o.Asset, Fmt.Credits(o.Cash), Fmt.Num(o.Qty * o.TaintRatio));
                    Say(L.T("otc.done"));
                    Log.Info($"换币客户：{o.Qty:0.##} {o.Asset} 进 {WalletWindow.NameOf(d.Hot)}，付现金 {o.Cash}，其中脏的 {o.Qty * o.TaintRatio:0.##}，钱包里 {o.Asset} 脏 {d.Hot.DirtyOf(o.Asset):0.##}");
                }
                Drop();
                Session.Save();
            });
            return msg;
        }

        /// <summary>卖币给客户：开账单（客户地址已填好），弹出钱包签名；确认后客户付现金、离开。</summary>
        private static string Give(OtcOffer o)
        {
            double qty = WalletWindow.RoundUp(o.Qty);
            string asset = o.Asset;
            int cash = o.Cash;
            WalletWindow.Bill bill = null;
            bill = new WalletWindow.Bill
            {
                Address = ActiveAddress, Asset = asset, Amount = qty, Prefill = true, Summary = L.T("pay.otc", WalletWindow.Amt(qty), asset, Fmt.Credits(cash)),
                Execute = pw => Log.Guard("换币客户到账", () =>
                {
                    pw.Add(asset, -qty);
                    ModHelper.AddCredit(cash, true);
                    Session.Data.Otc.Record(cash);
                    if (pendingBill == bill) pendingBill = null;
                    if (Active != null && Present()) { Say(L.T("otc.paid")); Active = null; ActiveAddress = null; activeClient = null; }
                    TerminalWindow.Refresh(L.T("otc.msg.sold", WalletWindow.Amt(qty), asset, Fmt.Credits(cash)));
                    Log.Info($"换币客户：{qty} {asset} 到账，收现金 {cash}");
                    Session.Save();
                }),
            };
            pendingBill = Standing = bill;
            WalletWindow.OpenForBill(bill);
            Log.Info($"换币客户：卖出 {qty} {asset}，现金 {cash}，等签名");
            return null;
        }

        /// <summary>客户说最后一句，然后离开。</summary>
        private static void Say(string line)
        {
            var c = activeClient;
            if (c == null || DialogUIManager.Instance == null) return;
            var dl = new Dialogue();
            dl.SetText(c.displayName, line).SetEndAction(Leave);
            DialogUIManager.Instance.StartDialogue(dl);
        }

        /// <summary>放弃（终端的"放弃"）：客户说一句走人，交易作废。</summary>
        public static void Cancel()
        {
            if (Present()) Say(L.T("otc.refused"));
            Drop();
        }

        /// <summary>这位客户的交易结束（成交、作废、离开，或者睡觉前）：还没签名的账单一起作废；已经签名、还在确认中的照常到账。</summary>
        private static void Drop()
        {
            Active = null; ActiveAddress = null; activeClient = null;
            Void();
        }

        /// <summary>作废换币账单（终端的"作废"、客户离开、读档）。</summary>
        public static void Void()
        {
            if (Standing != null && WalletWindow.Pending == Standing) WalletWindow.Pending = null;
            Standing = null;
            if (pendingBill != null && Standing == null) pendingBill = null;
        }

        private static void Leave() => DialogUIManager.Instance.CurrentClientLeave();
    }
}

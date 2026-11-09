using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Decentralized.Domain;

namespace Decentralized
{
    /// <summary>
    /// 交易终端窗口：首页三个入口（银行、Poinbase、Web3）加报告。银行是真实的信用点（存取、房租）；
    /// Poinbase 是合规交易所；Web3 是去中心化交易和链上钱包。每次操作后按当前页重建窗口。
    /// </summary>
    public static partial class TerminalWindow
    {
        public const string Id = "pd_terminal";
        private const float Bezel = 6f;
        // 窗口尺寸取自 mod 设置（828×680 是基准，各页布局按多出来的宽高等比放宽）
        private static float WinW => Settings.TerminalW;
        private static float WinH => Settings.TerminalH;
        private static float ScreenW => WinW - 60f;
        private static float ScreenH => WinH - 102f;
        private static float Ext => ScreenW - 768f;
        private static float LeftW => 420f + Ext * 0.6f;
        private static float RightW => 310f + Ext * 0.4f;
        /// <summary>大窗口里文字放大（行高、卡片同步放大）：基准窗口 1，1200 宽约 1.24，上限 1.3。</summary>
        private static float F => Mathf.Clamp(1f + Ext * 0.00065f, 1f, 1.3f);
        public static float ChartScale => LeftW >= 634f ? 3f : 2f;
        private static float[] Sc(float[] b) { float k = LeftW / 420f; return b.Select(x => x * k).ToArray(); }
        private enum Page { Home, Bank, Poinbase, Web3, Mining, Gray, Wallets, Otc }

        private static Page _page = Page.Home;
        private static string _amount = "";
        private static string _message;

        private static readonly List<GameObject> _picked = new List<GameObject>();
        private static readonly List<Image> _clear = new List<Image>();
        private static readonly List<PledgerSkin.RowDef> _rows = new List<PledgerSkin.RowDef>();

        // 窗口建好之后保留：之后的刷新只重建页签栏、滚动内容和消息行，不重建窗口框架，滚动位置也不丢。
        private static CustomUIWindow _win;
        private static RectTransform _screen, _topRoot, _bodyRoot, _noteRoot;
        private static ScrollRect _scroll;
        private static float _builtW, _builtH;
        private static string _scrollKey;
        private static bool _dirty;

        private static bool Alive() =>
            CustomUIManager.Instance != null && CustomUIManager.Instance.IsOpen(Id) && _win != null && _win.root != null
            && _screen != null && _topRoot != null && _bodyRoot != null && _noteRoot != null
            && Math.Abs(_builtW - WinW) < 0.5f && Math.Abs(_builtH - WinH) < 0.5f;

        /// <summary>打开或刷新终端。窗口还没开（或窗口大小变了）时马上完整构建；已经开着就只标记要刷新，同一帧里的多次刷新合并成一次（见 <see cref="Flush"/>）。</summary>
        public static void Open()
        {
            if (Session.Data == null) return;
            if (!Alive()) { OpenFull(); return; }
            _dirty = true;
            WalletDriver.Ensure();
        }

        /// <summary>每帧由常驻驱动调用：有待刷新的就重建一次内容区。</summary>
        public static void Flush()
        {
            if (!_dirty) return;
            _dirty = false;
            if (Session.Data == null || CustomUIManager.Instance == null || !CustomUIManager.Instance.IsOpen(Id)) return;
            if (!Alive()) { OpenFull(); return; }
            Rebuild();
        }

        private static string ScrollKey() => string.Join("|", _page, _web3Tab, _grayTab, _blackTab, _sellView, _walletTab, _poinTab);

        private static void BuildTop(CustomUIBuilder b)
        {
            if (_page == Page.Gray && !GrayAvailable) _page = Page.Web3;
            if (_page == Page.Otc && OtcClient.Active == null) _page = Page.Home;
            TabRows(b, Enum.GetValues(typeof(Page)).Cast<Page>().Where(p => (p != Page.Gray || GrayAvailable) && (p != Page.Otc || OtcClient.Active != null)).Select(p =>
            {
                var page = p;
                return new TabDef { Name = L.T("tab." + p.ToString().ToLowerInvariant()), On = page == _page, Click = () => { _page = page; _message = null; Open(); } };
            }).ToList(), true);
            ConnectPicker(b);
        }

        private static void BuildBody(CustomUIBuilder b)
        {
            bool pay = (_page == Page.Web3 || _page == Page.Gray) && WalletWindow.Pending != null && !WalletWindow.Pending.Contract;
            // 换币客户的账单不独占页面：手里往往没有客户要的币，要先在这里换出来再付
            if (pay) PayPage(b);
            if (pay && !OtcClient.IsBill(WalletWindow.Pending)) return;
            switch (_page)
            {
                case Page.Home: HomePage(b); break;
                case Page.Bank: BankPage(b); break;
                case Page.Poinbase: ExchangePage(b, Venue.Exchange); break;
                case Page.Web3: Web3Page(b); break;
                case Page.Mining: ReportPage(b); break;
                case Page.Gray: GrayPage(b); break;
                case Page.Wallets: WalletsPage(b); break;
                case Page.Otc: OtcPage(b); break;
                default: break;
            }
        }

        private static void BuildNote(CustomUIBuilder b)
        {
            if (!string.IsNullOrEmpty(_message)) b.AddLabel(_message, "term_note");
        }

        /// <summary>完整构建：窗口、黑边、屏幕、页签栏、滚动区、消息行。</summary>
        private static void OpenFull()
        {
            var b = CustomUIManager.Window(Id, L.T("term.title"));
            if (b == null) return;
            _picked.Clear(); _clear.Clear(); _rows.Clear(); _live.Clear();
            b.SetSize(WinW, WinH).SetDraggable(true).SetCloseOnEscape(true).UseMachineryStyle();
            // 黑色外边框包裹墨水屏
            b.BeginPanel(ScreenW + Bezel * 2, ScreenH + Bezel * 2, TextAnchor.MiddleCenter, Bezel + 3f, 0f);
            var bezel = b.Last.image;
            b.BeginPanel(ScreenW, ScreenH, TextAnchor.UpperLeft, 12f, 4f);
            var screen = b.CurrentContainer;
            var paper = b.Last.image;
            b.BeginPanel(ScreenW - 24f, -1f, TextAnchor.UpperLeft, 0f, 4f);
            _clear.Add(b.Last.image);
            var top = b.CurrentContainer;
            BuildTop(b);
            b.End();
            b.AddSeparator();
            b.BeginScroll(ScreenH - 52f);
            _clear.Add(b.Last.image);
            var body = b.CurrentContainer;
            BuildBody(b);
            b.End();
            b.BeginPanel(ScreenW - 24f, -1f, TextAnchor.UpperLeft, 0f, 0f);
            _clear.Add(b.Last.image);
            var note = b.CurrentContainer;
            BuildNote(b);
            b.End();
            b.End();
            b.End();
            var win = b.Show();
            _win = win; _screen = screen; _topRoot = top; _bodyRoot = body; _noteRoot = note;
            _scroll = body != null ? body.GetComponentInParent<ScrollRect>() : null;
            _builtW = WinW; _builtH = WinH; _scrollKey = ScrollKey(); _dirty = false;
            Log.Guard("交易终端外观", () => PledgerSkin.Apply(win, screen, bezel, paper, _picked, _clear, _rows, F));
            Log.Guard("购物车按钮", () => CartFab(win));
        }

        /// <summary>只重建页签栏、滚动内容和消息行；窗口框架和滚动视图保留（滚动位置自然不变，换页或换子页签时回到顶部）。</summary>
        private static void Rebuild()
        {
            var m = CustomUIManager.Instance;
            _picked.Clear(); _clear.Clear(); _rows.Clear(); _live.Clear();
            CustomUIBuilder.ClearContainer(_topRoot);
            CustomUIBuilder.ClearContainer(_bodyRoot);
            CustomUIBuilder.ClearContainer(_noteRoot);
            BuildTop(new CustomUIBuilder(m, _win, _topRoot));
            BuildBody(new CustomUIBuilder(m, _win, _bodyRoot));
            BuildNote(new CustomUIBuilder(m, _win, _noteRoot));
            string key = ScrollKey();
            if (key != _scrollKey)
            {
                _scrollKey = key;
                if (_scroll != null && _scroll.content != null) { var cp = _scroll.content.anchoredPosition; _scroll.content.anchoredPosition = new Vector2(cp.x, 0f); }
            }
            CustomUIBuilder.RebuildLayout(_topRoot); CustomUIBuilder.RebuildLayout(_bodyRoot); CustomUIBuilder.RebuildLayout(_noteRoot);
            Log.Guard("交易终端外观", () => PledgerSkin.Apply(_win, _screen, null, null, _picked, _clear, _rows, F, false));
            Log.Guard("购物车按钮", () => CartFab(_win));
        }

        private static void Go(Page p) { _page = p; _message = null; Open(); }

        /// <summary>换币客户选了"去终端交易"：打开终端的柜台交易页，数量默认填客户的全部。</summary>
        public static void OpenOtc()
        {
            var o = OtcClient.Active;
            if (o == null) return;
            _page = Page.Otc; _message = null;
            _amount = WalletWindow.Amt(o.Qty);
            Open();
        }

        private static readonly double[] OtcRatios = { 0.25, 0.5, 0.75, 1 };

        /// <summary>柜台交易：客户的来意和报价，按比例或手输定成交数量，预览现金和币，成交或放弃。</summary>
        private static void OtcPage(CustomUIBuilder b)
        {
            var o = OtcClient.Active;
            b.AddHeader(L.T("otc.h"));
            if (o == null || !OtcClient.Present()) { b.AddLabel(L.T("otc.msg.gone")); return; }
            var w = new[] { 200f, 300f };
            bool take = o.Side == OtcSide.Sell;
            TableRow(b, w, L.T("otc.k.side"), L.T(take ? "otc.side.sell" : "otc.side.buy", o.Asset));
            TableRow(b, w, L.T("otc.k.qty"), Fmt.Num(o.Qty) + " " + o.Asset);
            TableRow(b, w, L.T("otc.k.value"), Fmt.Credits(o.Value));
            TableRow(b, w, L.T(take ? "otc.k.ask" : "otc.k.bid"), Fmt.Credits(o.Cash));
            TableRow(b, w, L.T("otc.k.unit"), Fmt.Num(o.Cash / o.Qty) + " / " + o.Asset);
            b.BeginRow(6f, TextAnchor.MiddleLeft);
            b.AddLabel(L.T("otc.k.addr", ChainAddress.Short(OtcClient.ActiveAddress)));
            if (take) b.AddButton(L.T("otc.lookup"), () => { _explorerQuery = OtcClient.ActiveAddress; _page = Page.Wallets; _walletTab = WalletTab.Explorer; _message = null; Open(); });
            b.End();
            b.AddSeparator();
            b.BeginRow(4f);
            foreach (var r in OtcRatios)
            {
                double ratio = r;
                string v = WalletWindow.Amt(o.Qty * ratio);
                PledgerSkin.Pick(b, (ratio * 100).ToString("0") + "%", (_amount ?? "").Trim() == v, () => { _amount = v; Open(); }, _picked);
            }
            b.End();
            AmountRow(b, true, o.Asset);
            if (double.TryParse((_amount ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double q) && q > 0)
            {
                var part = Otc.Part(o, q);
                b.AddLabel(take ? L.T("otc.preview.take", Fmt.Credits(part.Cash), Fmt.Num(part.Qty), o.Asset) : L.T("otc.preview.give", Fmt.Num(part.Qty), o.Asset, Fmt.Credits(part.Cash)));
                if (!take) b.AddLabel(L.T("mixer.balance", o.Asset, Fmt.Num(Session.Data.Hot.Avail(o.Asset))));
            }
            b.BeginRow(6f);
            b.AddButton(L.T("otc.deal"), () =>
            {
                if (!ReadAmount(out double v)) return;
                string msg = OtcClient.Deal(v);
                if (msg != null) _message = msg;
                Open();
            });
            b.AddButton(L.T("otc.cancel"), () => { OtcClient.Cancel(); _message = null; _page = Page.Home; Open(); });
            b.End();
        }

        /// <summary>钱包那边交易落地后刷新终端（开着的话），带上结果消息。</summary>
        public static void Refresh(string message)
        {
            if (message != null) _message = message;
            if (CustomUIManager.Instance != null && CustomUIManager.Instance.IsOpen(Id)) Open();
        }

        private static void Entry(CustomUIBuilder b, string icon, string name, string line1, string line2, Page target, bool compact, int perRow) =>
            Entry(b, icon, name, line1, line2, L.T("home.enter"), () => Go(target), compact, perRow);

        private static void Entry(CustomUIBuilder b, string icon, string name, string line1, string line2, string button, Action onEnter, bool compact, int perRow)
        {
            float h = compact ? Mathf.Clamp((ScreenH - 190f) / 2f, 180f, 270f) : 250f + (ScreenH - 578f) * 0.25f, pic = compact ? 64f : 96f;
            b.BeginPanel((ScreenW - 80f) / perRow, h, TextAnchor.UpperCenter, 10f, compact ? 4f : 6f);
            var cardImg = b.Last.image;
            cardImg.sprite = MachineryStyle.White;
            cardImg.color = PledgerSkin.RowNormal;
            b.AddImage(ModInfo.Id + ":" + icon, pic, pic);
            b.AddHeader(name);
            b.AddLabel(line1);
            if (line2 != null) b.AddLabel(line2, "entry_l2_dim");
            b.AddFlexibleSpace();
            b.AddButton(button, onEnter);
            b.End();
        }

        private static void HomePage(CustomUIBuilder b)
        {
            var d = Session.Data;
            double chain = d.ChainValue(Session.Price);
            b.AddLabel(L.T("home.summary", PlayerStore.Instance.playerCash, Fmt.Credits(chain)));
            b.AddSpace(6f);
            var cards = new List<Action<bool, int>>
            {
                (two, n) => Entry(b, "ui_bank", L.T("entry.bank"), L.T("entry.bank.l1"), L.T("entry.bank.l2", PlayerStore.Instance.playerCash), Page.Bank, two, n),
                (two, n) => Entry(b, "ui_poinbase", L.T("entry.poinbase"), L.T("entry.poinbase.l1"), null, Page.Poinbase, two, n),
                (two, n) => Entry(b, "ui_web3", L.T("entry.web3"), L.T("entry.web3.l1"), L.T("entry.web3.l2", Fmt.Credits(chain)), Page.Web3, two, n),
                (two, n) => Entry(b, "ui_mining", L.T("tab.mining"), L.T("entry.mining.l1"), null, Page.Mining, two, n),
            };
            if (GrayAvailable) cards.Add((two, n) => Entry(b, "ui_gray", L.T("tab.gray"), L.T("entry.gray.l1"), null, Page.Gray, two, n));
            cards.Add((two, n) => Entry(b, "ui_wallets", L.T("tab.wallets"), L.T("entry.wallets.l1"), L.T("entry.wallets.l2", d.Wallets.Count, Tuning.WalletMax), Page.Wallets, two, n));
            cards.Add((two, n) => Entry(b, "ui_help", L.T("entry.help"), L.T("entry.help.l1"), null, L.T("home.open"), HandbookWindow.Open, two, n));
            // 一行四张；多于四张时分两行（每行尽量均分），卡片和图标相应缩小
            bool twoRows = cards.Count > 4;
            int first = twoRows ? (cards.Count + 1) / 2 : cards.Count;
            for (int i = 0; i < cards.Count; i += first)
            {
                b.BeginRow(8f, TextAnchor.UpperCenter);
                for (int j = i; j < Math.Min(cards.Count, i + first); j++) cards[j](twoRows, first);
                b.End();
                if (twoRows && i == 0) b.AddSpace(8f);
            }
            b.AddSpace(8f);
            b.AddSeparator();
            var r = d.LastNight;
            b.AddLabel(r == null ? L.T("report.none") : L.T("home.lastnight", r.Day, Fmt.Num(r.Mined), r.PoweredRigs, r.Rigs), "report_dim");
        }

        private static void Cell(CustomUIBuilder b, float w, string text, bool header = false, TextAnchor align = TextAnchor.MiddleLeft, string tip = null)
        {
            b.BeginPanel(w, 26f * F, align, 0f, 0f);
            _clear.Add(b.Last.image);
            if (!string.IsNullOrEmpty(tip)) b.WithTooltip(tip);
            b.AddLabel(header ? "<b>" + text + "</b>" : text);
            PledgerSkin.FitCurrent(b);
            b.End();
        }

        private static void TableRow(CustomUIBuilder b, float totalW, float[] widths, TextAnchor[] aligns, string[] cells, bool selected = false, Action onClick = null, string[] tips = null)
        {
            b.BeginPanel(totalW, 28f * F, TextAnchor.MiddleLeft, 4f, 0f);
            _rows.Add(new PledgerSkin.RowDef { Root = b.Last.root, Image = b.Last.image, Selected = selected, OnClick = onClick });
            b.BeginRow(0f, TextAnchor.MiddleLeft);
            for (int i = 0; i < widths.Length; i++)
            {
                var align = (aligns != null && i < aligns.Length) ? aligns[i] : (i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight);
                Cell(b, widths[i], i < cells.Length ? cells[i] : "", false, align, tips != null && i < tips.Length ? tips[i] : null);
            }
            b.End();
            b.End();
        }

        private static void TableRow(CustomUIBuilder b, float[] widths, params string[] cells) =>
            TableRow(b, widths.Sum() + 8f, widths, null, cells, false, null);

        private static void TableHeader(CustomUIBuilder b, float totalW, float[] widths, TextAnchor[] aligns, params string[] cells)
        {
            b.BeginPanel(totalW, 26f * F, TextAnchor.MiddleLeft, 4f, 0f);
            _clear.Add(b.Last.image);
            b.BeginRow(0f, TextAnchor.MiddleLeft);
            for (int i = 0; i < widths.Length; i++)
            {
                var align = (aligns != null && i < aligns.Length) ? aligns[i] : (i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight);
                Cell(b, widths[i], i < cells.Length ? cells[i] : "", true, align);
            }
            b.End();
            b.End();
        }

        private static void TableHeader(CustomUIBuilder b, float[] widths, params string[] cells) =>
            TableHeader(b, widths.Sum() + 8f, widths, null, cells);

        /// <summary>左右分栏：左边是账户与持仓表格，右边是操作区。</summary>
        private static void Columns(CustomUIBuilder b, Action<CustomUIBuilder> left, Action<CustomUIBuilder> right)
        {
            b.BeginRow(10f);
            b.BeginPanel(LeftW, -1f, TextAnchor.UpperLeft, 4f, 4f);
            _clear.Add(b.Last.image);
            left(b);
            b.End();
            b.BeginPanel(RightW, -1f, TextAnchor.UpperLeft, 4f, 6f);
            _clear.Add(b.Last.image);
            right(b);
            b.End();
            b.End();
        }

        // 输入时实时更新的预览文字：输入框每次输入只改这些文字，不重建页面（重建会让输入框失焦）。
        private static readonly List<KeyValuePair<CustomUIElement, Func<string>>> _live = new List<KeyValuePair<CustomUIElement, Func<string>>>();

        /// <summary>加一行实时预览：文字由 text() 算，输入框内容变化时刷新；没有内容时是一个空行占位。</summary>
        private static void LiveLabel(CustomUIBuilder b, Func<string> text, string style)
        {
            string t = text();
            b.AddLabel(string.IsNullOrEmpty(t) ? " " : t, style);
            if (b.Last != null && b.Last.text != null) _live.Add(new KeyValuePair<CustomUIElement, Func<string>>(b.Last, text));
        }

        private static void RefreshLive()
        {
            foreach (var kv in _live)
            {
                if (kv.Key == null || kv.Key.text == null) continue;
                string t = kv.Value();
                kv.Key.text.text = string.IsNullOrEmpty(t) ? " " : t;
            }
        }

        private static bool ParseAmount(out double v) =>
            double.TryParse((_amount ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0;

        private static void AmountRow(CustomUIBuilder b, bool label = true, string unit = null)
        {
            if (label) b.AddLabel(unit == null ? L.T("trade.amount") : L.T("trade.amount.unit", unit));
            b.BeginPanel(RightW - 30f, -1f, TextAnchor.MiddleCenter, 0f, 0f);
            _clear.Add(b.Last.image);
            b.AddInput("0", _amount, s => { _amount = s; RefreshLive(); }, "amount", s => { _amount = s; RefreshLive(); });
            b.End();
        }

        /// <summary>银行：真实的信用点。现金、房租，以及信用点与 Poinbase 账户（CRC）之间的存取。</summary>
        private static void BankPage(CustomUIBuilder b)
        {
            var ps = PlayerStore.Instance;
            var w = new[] { 180f, 220f };
            b.AddHeader(L.T("entry.bank"));
            TableRow(b, w, L.T("bank.cash.k"), ps.playerCash.ToString());
            TableRow(b, w, L.T("bank.rent.k"), L.T("bank.rent.v", Session.Rent(), ps.GetRentDayCounter()));
            TableRow(b, w, L.T("bank.crc.k"), Fmt.Num(Session.Data.Poinbase.Get(Assets.CRC)));
        }

        private static string StableStatus(Stablecoins st, string asset, int day)
        {
            double p = st.Price(asset);
            if (asset == Assets.CRA && st.CraWarning(day)) return L.T("stable.warn");
            if (asset == Assets.CRA && p < 0.97) return L.T("stable.spiral");
            return p < 0.99 ? L.T("stable.depeg") : L.T("stable.ok");
        }

        private static readonly float[] PriceWBase = new[] { 100f, 160f };
        private static float[] PriceW => Sc(PriceWBase);
        private static readonly string[] SwapCoins = { Assets.PSC, Assets.CRC, Assets.CRT, Assets.CRA, Assets.SPARK };
        /// <summary>Ponero 只能和这些币互换（走兑换处，不经 DEX）。</summary>
        private static readonly string[] DeskCoins = { Assets.PSC, Assets.CRT, Assets.CRA };
        private static string _swapFrom = Assets.CRC, _swapTo = Assets.PSC;
        private static bool DeskOpen => Unlock.BlackUnlocked && BlackMarket.DeskOpen(StoreReputation.GetBMReputation());
        private static bool DeskPair => _swapFrom == Assets.Ponero || _swapTo == Assets.Ponero;

        /// <summary>选币：另一边选了同一种币、或选的组合兑换处不支持时，另一边换成可用的币。</summary>
        private static void PickSwap(string coin, bool from)
        {
            string other = from ? _swapTo : _swapFrom;
            bool clash = other == coin || (coin == Assets.Ponero && !DeskCoins.Contains(other)) || (other == Assets.Ponero && !DeskCoins.Contains(coin));
            if (clash) other = coin == Assets.Ponero ? DeskCoins[0] : SwapCoins.First(x => x != coin);
            _swapFrom = from ? coin : other;
            _swapTo = from ? other : coin;
            Open();
        }

        /// <summary>币价表：各币在 DEX 池里以 CRC 计的价格。</summary>
        private static void PriceTable(CustomUIBuilder b)
        {
            var m = Session.Data.Market;
            b.AddSeparator();
            TableHeader(b, PriceW, L.T("col.asset"), L.T("col.crcprice"));
            foreach (var a in SwapCoins.Where(x => x != Assets.CRC))
                TableRow(b, PriceW, a, Fmt.Num(m.DexPool(a).Price));
        }

        /// <summary>互换：任意币对任意币，实际经 CRC 路由；预览显示得到数量、经过几个池、损耗。兑换处开放后可选 Ponero，选中时显示价格和当日额度。</summary>
        private static void SwapBlock(CustomUIBuilder b)
        {
            var m = Session.Data.Market;
            bool deskOpen = DeskOpen;
            if (!deskOpen && DeskPair) { _swapFrom = Assets.CRC; _swapTo = Assets.PSC; }
            var coins = deskOpen ? SwapCoins.Concat(new[] { Assets.Ponero }).ToArray() : SwapCoins;
            Action<string, string, bool> picker = (label, cur, from) =>
            {
                b.AddLabel(label);
                b.BeginRow(4f);
                foreach (var c in coins)
                {
                    var coin = c;
                    PledgerSkin.Pick(b, coin, coin == cur, () => PickSwap(coin, from), _picked);
                }
                b.End();
            };
            AmountRow(b);
            picker(L.T("swap.pay"), _swapFrom, true);
            picker(L.T("swap.get"), _swapTo, false);
            if (DeskPair) b.AddLabel(L.T("black.desk.pair", Fmt.Num(Session.DeskPrice(Assets.Ponero, true))));
            LiveLabel(b, () =>
            {
                if (DeskPair)
                {
                    if (!ParseAmount(out double dv)) return "";
                    var desk = Session.Data.Black;
                    int today = StoreStation.GetDayCounter();
                    bool into = _swapTo == Assets.Ponero;
                    double px = Session.DeskPrice(_swapFrom, into), dirty = into ? Session.Data.Hot.DirtyRatio(_swapFrom) : 0;
                    return L.T("swap.preview.desk", Fmt.Num(desk.DeskOut(dv, px, Session.DeskPrice(_swapTo, into), dirty, today)), _swapTo, (desk.DeskLoss(dv * px, dirty, today) * 100).ToString("0.##", CultureInfo.InvariantCulture));
                }
                var q = ParseAmount(out double v) ? m.Quote(_swapFrom, _swapTo, v) : null;
                return q == null ? "" : L.T("swap.preview", Fmt.Num(q.Out), _swapTo, q.Hops, (q.Impact * 100).ToString("0.00", CultureInfo.InvariantCulture));
            }, "swap_preview_dim");
            b.AddButton(L.T("swap.go"), () => Swap());
        }

        /// <summary>互换：DEX 是合约调用，要在硬件钱包上签名。</summary>
        private static void Swap()
        {
            if (!ReadAmount(out double v)) return;
            string from = _swapFrom, to = _swapTo;
            if (DeskPair) { DeskBill(v, from, to); return; }
            var q = Session.Data.Market.Quote(from, to, v);
            if (q == null) { _message = L.T("swap.same"); Open(); return; }
            var w = Session.Data.Hot;
            if (w.Avail(from) + 1e-9 < v) { _message = L.T(frozenPay(w, from) ? "msg.frozen" : "msg.balance"); Open(); return; }
            _message = L.T("hw.wait");
            Open();
            WalletWindow.OpenForBill(new WalletWindow.Bill { Contract = true, Party = Session.Data.Address("dex"), Asset = from, Amount = v, Summary = L.T("hw.swap", Fmt.Num(v), from, Fmt.Num(q.Out), to), Execute = pw => DoSwap(from, to, v, pw) });
        }

        private static void DoSwap(string from, string to, double v, Wallet w)
        {
            if (w.Avail(from) + 1e-9 < v) { _message = L.T(frozenPay(w, from) ? "msg.frozen" : "msg.balance"); Open(); return; }
            double got = Session.Data.Market.Swap(from, to, v);
            if (got <= 0) { _message = L.T("swap.same"); Open(); return; }
            double spent = v * Session.Price(from);
            CandleChart.Trade(from, spent); CandleChart.Trade(to, spent);
            // 换来的币继承付出那种币的脏比例
            w.Convert(from, v, to, got);
            w.RecordBuy(to, got, spent);
            _message = L.T("msg.trade", Fmt.Num(v), from, Fmt.Num(got), to);
            Session.Save();
            Open();
        }

        private static void TradeRows(CustomUIBuilder b, Pool pool)
        {
            {
                var p = pool;
                // 在 Poinbase 页里，交易所名不用再写一遍（行太长会被截断）
                b.AddLabel(p.Base + "/" + p.Quote + "  " + L.T("trade.price", Fmt.Num(p.Price)) + "  " + L.T("trade.fee", (p.Fee * 100).ToString("0.0#", CultureInfo.InvariantCulture)));
                LiveLabel(b, () => SpotPreview(p, true), "trade_preview_dim");
                LiveLabel(b, () => SpotPreview(p, false), "trade_preview_dim");
                b.BeginRow(4f);
                b.AddButton(L.T("trade.buy"), () => Trade(p, buy: true));
                b.AddButton(L.T("trade.sell"), () => Trade(p, buy: false));
                b.End();
            }
        }

        /// <summary>现货模拟（不改池子）：输入的数量是标的币；买入算要付多少计价币，卖出算能得到多少。均价含手续费，滑点不含手续费。</summary>
        private static string SpotPreview(Pool p, bool buy)
        {
            if (!ParseAmount(out double v)) return "";
            double quote = buy ? p.QuoteIn(v) : p.QuoteOut(v);
            if (quote <= 0 || double.IsInfinity(quote)) return buy ? L.T("trade.sim.thin") : "";
            double avg = quote / v, net = buy ? avg * (1 - p.Fee) : avg / (1 - p.Fee);
            double slip = buy ? net / p.Price - 1 : 1 - net / p.Price;
            return L.T(buy ? "trade.sim.buy" : "trade.sim.sell", Fmt.Num(quote), p.Quote, Fmt.Num(avg), (Math.Max(0, slip) * 100).ToString("0.00", CultureInfo.InvariantCulture));
        }

        private static readonly float[] HoldWBase = new[] { 80f, 100f, 110f, 110f };
        private static float[] HoldW => Sc(HoldWBase);
        private static readonly TextAnchor[] HoldAligns = { TextAnchor.MiddleLeft, TextAnchor.MiddleRight, TextAnchor.MiddleRight, TextAnchor.MiddleRight };
        /// <summary>钱包的持仓表多一列“其中脏的”（Poinbase 没有污点，不显示）。</summary>
        private static readonly float[] HoldDWBase = new[] { 60f, 85f, 75f, 90f, 90f };
        private static float[] HoldDW => Sc(HoldDWBase);
        private static readonly TextAnchor[] HoldDAligns = { TextAnchor.MiddleLeft, TextAnchor.MiddleRight, TextAnchor.MiddleRight, TextAnchor.MiddleRight, TextAnchor.MiddleRight };

        private static void Holdings(CustomUIBuilder b, Wallet w, Func<string, bool> show)
        {
            bool dirtyCol = w != Session.Data.Poinbase;
            var hw = dirtyCol ? HoldDW : HoldW; var ha = dirtyCol ? HoldDAligns : HoldAligns;
            if (dirtyCol) TableHeader(b, hw.Sum() + 8f, hw, ha, L.T("col.asset"), L.T("col.amount"), L.T("col.dirty"), L.T("col.value"), L.T("col.avg"));
            else TableHeader(b, hw.Sum() + 8f, hw, ha, L.T("col.asset"), L.T("col.amount"), L.T("col.value"), L.T("col.avg"));
            bool any = false;
            foreach (var kv in w.Balances.OrderBy(k => k.Key).Where(k => show(k.Key)))
            {
                any = true;
                string asset = kv.Key;
                bool selected = (_page == Page.Poinbase && _poinTab == PoinTab.Funds && asset == _fundsAsset);
                double dirty = w.DirtyOf(asset);
                TableRow(b, hw.Sum() + 8f, hw, ha,
                    dirtyCol ? new[] { kv.Key, Fmt.Num(kv.Value), dirty > 1e-9 ? Fmt.Num(dirty) : "—", Fmt.Credits(kv.Value * Session.Price(kv.Key)), AvgText(w, kv.Key) }
                             : new[] { kv.Key, Fmt.Num(kv.Value), Fmt.Credits(kv.Value * Session.Price(kv.Key)), AvgText(w, kv.Key) },
                    selected,
                    () => { if (_page == Page.Poinbase && _poinTab == PoinTab.Funds) { _fundsAsset = asset; if (asset != Assets.CRC) _fundsBank = false; Open(); } });
            }
            if (!any) b.AddLabel(L.T(w == Session.Data.Poinbase ? "poin.empty" : "wallet.empty"), "hold_empty_dim");
            foreach (var kv in w.Frozen.OrderBy(k => k.Key).Where(k => show(k.Key)))
                b.AddLabel(L.T("wallet.frozen", kv.Key, Fmt.Num(kv.Value), w.FrozenUntil.TryGetValue(kv.Key, out var u) ? u : 0), "hold_frozen_note");
        }

        /// <summary>Poinbase：合规交易所。左边账户持仓，右边交易；信用点的存取在银行。</summary>
        private enum PoinTab { Funds, Spot, Perp }
        private static PoinTab _poinTab = PoinTab.Funds;

        private static void ExchangePage(CustomUIBuilder b, Venue venue)
        {
            var d = Session.Data;
            var w = d.Poinbase;
            b.BeginRow(4f);
            foreach (PoinTab t in Enum.GetValues(typeof(PoinTab)))
            {
                var tab = t;
                string name = L.T("poin.sub." + t.ToString().ToLowerInvariant());
                PledgerSkin.Pick(b, name, tab == _poinTab, () => { _poinTab = tab; _message = null; Open(); }, _picked);
            }
            b.End();
            if (_poinTab == PoinTab.Perp) { PerpPage(b); return; }
            if (_poinTab == PoinTab.Funds) { FundsPage(b); return; }
            Columns(b, l =>
            {
                CandleChart.AddPicker(l, "poin", Assets.PSC, Open, _picked, Exchange.Listed);
                l.AddSeparator();
                l.AddHeader(L.T("poin.account.h"));
                Holdings(l, w, a => true);
            }, r =>
            {
                // 交易对跟着行情图选中的币（CRC 本身没有交易对，显示 PSC/CRC）
                string sel = CandleChart.Selected("poin", Assets.PSC, Exchange.Listed);
                var pair = d.Market.Find(Venue.Exchange, sel, Assets.CRC) ?? d.Market.Main;
                r.AddHeader(L.T("trade.h"));
                AmountRow(r, unit: pair.Base);
                TradeRows(r, pair);
            });
        }

        private enum FundsMode { Receive, Send }
        private static FundsMode _fundsMode = FundsMode.Receive;
        private static string _fundsAsset = Assets.CRC, _fundsTo = "";
        private static bool _fundsBank = true;

        /// <summary>资金页：入金和出金，先选币种。CRC 可以走银行账户或钱包，其他币只能走钱包。</summary>
        private static void FundsPage(CustomUIBuilder b)
        {
            var d = Session.Data;
            var w = d.Poinbase;
            if (_fundsAsset != Assets.CRC) _fundsBank = false;
            Columns(b, l =>
            {
                l.AddHeader(L.T("poin.account.h"));
                Holdings(l, w, a => true);
            }, r =>
            {
                r.BeginRow(4f);
                foreach (FundsMode m in Enum.GetValues(typeof(FundsMode)))
                {
                    var mode = m;
                    PledgerSkin.Pick(r, L.T("funds." + m.ToString().ToLowerInvariant()), mode == _fundsMode, () => { _fundsMode = mode; _message = null; Open(); }, _picked);
                }
                r.End();
                // 入金只收 Poinbase 上架的币；出金另外列出账户里还有余额的其他币（旧存档里充进来的 CRT、CRA）
                var coins = Exchange.Listed.Concat(_fundsMode == FundsMode.Send ? SwapCoins.Where(a => !Exchange.Listed.Contains(a) && w.Get(a) > 1e-9) : Enumerable.Empty<string>()).ToList();
                if (!coins.Contains(_fundsAsset)) { _fundsAsset = Assets.CRC; _fundsBank = true; }
                r.BeginRow(4f);
                foreach (var c in coins)
                {
                    var coin = c;
                    PledgerSkin.Pick(r, coin, coin == _fundsAsset, () => { _fundsAsset = coin; if (coin != Assets.CRC) _fundsBank = false; Open(); }, _picked);
                }
                r.End();
                if (_fundsAsset == Assets.CRC)
                {
                    r.BeginRow(4f);
                    PledgerSkin.Pick(r, L.T("funds.bank"), _fundsBank, () => { _fundsBank = true; Open(); }, _picked);
                    PledgerSkin.Pick(r, L.T("funds.wallet"), !_fundsBank, () => { _fundsBank = false; Open(); }, _picked);
                    r.End();
                }
                r.AddSeparator();
                if (_fundsMode == FundsMode.Receive)
                {
                    if (_fundsBank)
                    {
                        AmountRow(r);
                        r.AddButton(L.T("trade.deposit"), Deposit);
                    }
                    else
                    {
                        r.AddLabel(L.T("poin.deposit", d.Address("poinbase")), "deposit_dim");
                        // 只收干净的币：连着的钱包里有脏币时，写明每种币能充多少
                        if (d.Connected && Exchange.Listed.Any(a => d.Hot.DirtyOf(a) > 1e-9))
                            r.AddLabel(L.T("poin.deposit.clean", WalletWindow.NameOf(d.Hot), string.Join("　", Exchange.Listed.Where(a => d.Hot.Get(a) > 1e-9).Select(a => Fmt.Num(d.Hot.CleanAvail(a)) + " " + a))), "taint_note");
                        r.AddButton(L.T("hw.copy"), () => { Clipboard.Copy(d.Address("poinbase")); _message = L.T("hw.copied.addr", d.Address("poinbase")); Open(); });
                    }
                    return;
                }
                if (_fundsBank)
                {
                    AmountRow(r);
                    r.AddButton(L.T("trade.withdraw"), Withdraw);
                    return;
                }
                r.AddLabel(L.T("funds.to"));
                r.BeginPanel(RightW - 30f, -1f, TextAnchor.MiddleCenter, 0f, 0f);
                _clear.Add(r.Last.image);
                r.AddInput("0x", _fundsTo, s => _fundsTo = s, "funds_to", s => _fundsTo = s);
                r.End();
                if (d.Wallets.Count > 0)
                {
                    r.BeginRow(4f);
                    foreach (var ow in d.Wallets)
                    {
                        var own = ow;
                        PledgerSkin.Pick(r, WalletWindow.NameOf(own), ChainAddress.Same(_fundsTo, own.Address), () => { _fundsTo = own.Address; Open(); }, _picked);
                    }
                    r.End();
                }
                AmountRow(r);
                r.AddButton(L.T("funds.go"), FundsSend);
                foreach (var t in Funds.Active)
                {
                    r.AddLabel(L.T("funds.sending", Fmt.Num(t.Amount), t.Asset, ChainAddress.Short(t.To)), "funds_sending_dim");
                    r.AddLabel(Funds.StatusText(t), "funds_status_" + t.Id);
                    r.AddProgressBar(Funds.Progress(t), "funds_prog_" + t.Id);
                }
            });
        }

        private static void FundsSend()
        {
            if (!ReadAmount(out double v)) return;
            string err = Funds.Start(_fundsAsset, v, _fundsTo);
            _message = err != null ? L.T(err) : null;
            Open();
        }

        private static string _perpProduct = "PSC";
        private static bool _perpLong = true;
        private static int _perpLev = 3;
        private static readonly float[] PerpWBase = new[] { 80f, 70f, 110f, 60f, 70f };
        private static float[] PerpW => Sc(PerpWBase);
        private static readonly TextAnchor[] PerpAligns = { TextAnchor.MiddleLeft, TextAnchor.MiddleRight, TextAnchor.MiddleRight, TextAnchor.MiddleRight, TextAnchor.MiddleRight };

        private static double PerpSpot(string p) => p == "SPARK" ? Session.Data.Stable.Spark : Session.Data.Market.Price;

        /// <summary>永续页：保证金账户、仓位、资金费与保险基金；开仓、追加保证金、平仓。Poinbase 是托管账户，不用签名。</summary>
        private static void PerpPage(CustomUIBuilder b)
        {
            var d = Session.Data;
            var bk = d.Perps;
            var acct = d.Poinbase;
            int day = StoreStation.GetDayCounter();
            if (_perpLev > PerpBook.MaxLeverage(_perpProduct)) _perpLev = PerpBook.MaxLeverage(_perpProduct);
            Columns(b, l =>
            {
                l.AddHeader(L.T("perp.h"));
                l.AddLabel(L.T("perp.acct", Fmt.Num(acct.Avail(Assets.CRC))));
                if (bk.Debt > 0) l.AddLabel(L.T("perp.debt", Fmt.Credits(bk.Debt), Math.Max(0, Tuning.PerpDebtDays - (day - bk.DebtSince))), "perp_debt_note");
                if (bk.Banned(day)) l.AddLabel(L.T("perp.banned", bk.BannedUntil - day + 1), "perp_banned_note");
                l.AddSeparator();
                if (bk.Positions.Count == 0) l.AddLabel(L.T("perp.none"), "perp_none_dim");
                else
                {
                    TableHeader(l, PerpW.Sum() + 8f, PerpW, PerpAligns, L.T("perp.col.pos"), L.T("perp.col.notional"), L.T("perp.col.price"), L.T("perp.col.pnl"), L.T("perp.col.liq"));
                    foreach (var p in bk.Positions)
                    {
                        var pos = p;
                        double spot = PerpSpot(pos.Product);
                        bool selPos = pos.Product == _perpProduct;
                        TableRow(l, PerpW.Sum() + 8f, PerpW, PerpAligns,
                            new[] {
                                pos.Product + " " + L.T(pos.Long ? "perp.long" : "perp.short") + " " + Math.Round(pos.Leverage, 1) + "×",
                                Fmt.Credits(pos.Notional(spot)),
                                Fmt.Num(pos.Entry) + " → " + Fmt.Num(spot),
                                Fmt.Credits(pos.Pnl(spot)),
                                Fmt.Num(pos.LiqPrice)
                            },
                            selPos,
                            () => { _perpProduct = pos.Product; Open(); });
                    }
                }
                l.AddSeparator();
                foreach (var p in PerpBook.Products)
                    l.AddLabel(L.T("perp.rate", p, Fmt.Num(PerpSpot(p)), (bk.Rate(p) * 100).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)), "perp_rate_dim");
                l.AddLabel(L.T("perp.fund", Fmt.Credits(bk.InsuranceFund)), "perp_fund_dim");
                l.AddSeparator();
                CandleChart.Add(l, "perp", _perpProduct);
            }, r =>
            {
                var held = bk.Positions.Count > 0 ? bk.Positions[0] : null;
                if (held != null) { _perpProduct = held.Product; _perpLong = held.Long; _perpLev = Math.Max(1, (int)Math.Round(held.Leverage)); }
                r.AddHeader(L.T(held == null ? "perp.open" : "perp.col.pos"));
                if (held == null)
                {
                    r.BeginRow(4f);
                    foreach (var p in PerpBook.Products)
                    {
                        var product = p;
                        PledgerSkin.Pick(r, product, product == _perpProduct, () => { _perpProduct = product; Open(); }, _picked);
                    }
                    r.End();
                    r.BeginRow(4f);
                    PledgerSkin.Pick(r, L.T("perp.long"), _perpLong, () => { _perpLong = true; Open(); }, _picked);
                    PledgerSkin.Pick(r, L.T("perp.short"), !_perpLong, () => { _perpLong = false; Open(); }, _picked);
                    r.End();
                    r.BeginRow(4f);
                    foreach (int lev in new[] { 1, 2, 3, 5 })
                    {
                        if (lev > PerpBook.MaxLeverage(_perpProduct)) continue;
                        int lv = lev;
                        PledgerSkin.Pick(r, lv + "×", lv == _perpLev, () => { _perpLev = lv; Open(); }, _picked);
                    }
                    r.End();
                }
                r.AddLabel(L.T("perp.margin"));
                AmountRow(r, false);
                if (held == null)
                {
                    LiveLabel(r, () => PerpOpenPreview(1), "perp_preview_dim");
                    LiveLabel(r, () => PerpOpenPreview(2), "perp_preview_dim");
                }
                else
                {
                    LiveLabel(r, () => PerpClosePreview(held, 1), "perp_preview_dim");
                    LiveLabel(r, () => PerpClosePreview(held, 2), "perp_preview_dim");
                }
                r.BeginRow(6f);
                if (held == null) r.AddButton(L.T("perp.do.open"), PerpOpen);
                else r.AddButton(L.T("perp.do.add"), PerpAdd);
                if (held != null) r.AddButton(L.T("perp.do.close"), PerpClose);
                r.End();
            });
        }

        /// <summary>开仓模拟：名义价值、预估平均成交价（含滑点）、手续费、强平价。line 1 是价格行，2 是费用和强平价行。</summary>
        private static string PerpOpenPreview(int line)
        {
            if (!ParseAmount(out double m)) return "";
            var bk = Session.Data.Perps;
            int day = StoreStation.GetDayCounter();
            double notional = m * _perpLev, spot = PerpSpot(_perpProduct), exec = bk.Exec(spot, notional, _perpLong, day);
            if (line == 1) return L.T("perp.preview", Fmt.Credits(notional), Fmt.Num(exec), ((exec / spot - 1) * 100).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture));
            var ghost = new PerpPosition { Long = _perpLong, Entry = exec, Size = notional / exec, Margin = m };
            return L.T("perp.preview2", Fmt.Num(notional * Tuning.PerpFee), Fmt.Num(ghost.LiqPrice));
        }

        /// <summary>平仓模拟：按现价平掉整个仓位的预估成交价（含滑点）、手续费、回到账户的 CRC 和盈亏。</summary>
        private static string PerpClosePreview(PerpPosition pos, int line)
        {
            var bk = Session.Data.Perps;
            double spot = PerpSpot(pos.Product);
            double exec = bk.Exec(spot, pos.Notional(spot), !pos.Long, StoreStation.GetDayCounter());
            double fee = pos.Notional(exec) * Tuning.PerpFee;
            double payout = Math.Max(0, pos.Equity(exec) - fee);
            if (line == 1) return L.T("perp.sim.close", Fmt.Credits(pos.Notional(spot)), Fmt.Num(exec), ((exec / spot - 1) * 100).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture));
            return L.T("perp.sim.close2", Fmt.Num(fee), Fmt.Num(payout), (payout - pos.Margin >= 0 ? "+" : "") + Fmt.Num(payout - pos.Margin));
        }

        private static readonly float[] PoolWBase = new[] { 110f, 50f, 64f, 64f, 50f, 78f };
        private static float[] PoolW => Sc(PoolWBase);
        private static readonly TextAnchor[] PoolAligns = { TextAnchor.MiddleLeft, TextAnchor.MiddleRight, TextAnchor.MiddleRight, TextAnchor.MiddleRight, TextAnchor.MiddleRight, TextAnchor.MiddleRight };

        /// <summary>第三方收益池：日息、储备、时间锁、审计、我的存款，存入 / 取出（合约调用，签名）。是不是骗局看不见。</summary>
        private static void YieldPools(CustomUIBuilder l, Wallet w, int day)
        {
            var sc = Session.Data.Scams;
            var pools = sc.Pools.Where(p => !p.Closed || p.Pending.Any(q => ChainAddress.Same(q.Wallet, w.Address))).ToList();
            l.AddHeader(L.T("pool.h"));
            if (pools.Count == 0) { l.AddLabel(L.T("pool.none"), "pool_none_dim"); return; }
            TableHeader(l, PoolW.Sum() + 8f, PoolW, PoolAligns, L.T("pool.col.name"), L.T("pool.col.rate"), L.T("pool.col.reserve"), L.T("pool.col.lock"), L.T("pool.col.audit"), L.T("pool.col.mine"));
            foreach (var p in pools)
            {
                var pp = p;
                string mine = Fmt.Num(p.Of(w.Address)) + (p.Pending.Any(q => ChainAddress.Same(q.Wallet, w.Address)) ? "+" + L.T("pool.pending") : "");
                TableRow(l, PoolW.Sum() + 8f, PoolW, PoolAligns,
                    new[] {
                        p.Name + (p.Closed ? " " + L.T("pool.closed") : ""),
                        (p.Rate * 100).ToString("0", CultureInfo.InvariantCulture) + "%",
                        Fmt.Num(p.Reserve),
                        L.T(p.Timelock ? "pool.yes" : "pool.no"),
                        L.T(p.Audited ? "pool.yes" : "pool.no"),
                        mine
                    });
                if (p.Closed) continue;
                l.BeginRow(4f);
                l.AddButton(L.T("pool.deposit"), () => PoolCall(pp, true));
                if (p.Of(w.Address) > 0) l.AddButton(L.T(p.Timelock ? "pool.withdraw.lock" : "pool.withdraw", Tuning.YieldLockDays), () => PoolCall(pp, false));
                l.End();
                l.AddSpace(2f);
            }
        }

        private static void PoolCall(YieldPool p, bool deposit)
        {
            var d = Session.Data;
            var w = d.Hot;
            double v = 0;
            if (deposit)
            {
                if (!ReadAmount(out v)) return;
                if (w.Avail(Assets.CRC) + 1e-9 < v) { _message = L.T("msg.balance"); Open(); return; }
                if (p.Total + v > p.Cap + 1e-9) { _message = L.T("pool.err.cap", Fmt.Num(Math.Max(0, p.Cap - p.Total))); Open(); return; }
            }
            _message = L.T("hw.wait");
            Open();
            WalletWindow.OpenForBill(new WalletWindow.Bill
            {
                Contract = true, Party = Session.Data.Address("yield:" + p.Name), Asset = Assets.CRC, Amount = v, Spend = deposit ? v : 0, FeeAsset = deposit ? null : BestFeeAsset(w),
                Summary = L.T(deposit ? "pool.sum.in" : "pool.sum.out", p.Name, Fmt.Num(deposit ? v : p.Of(w.Address))),
                Execute = pw =>
                {
                    int day = StoreStation.GetDayCounter();
                    if (deposit) _message = d.Scams.Deposit(p, pw, v) ? L.T("pool.done.in", Fmt.Num(v), p.Name) : L.T("msg.balance");
                    else
                    {
                        double amt = p.Of(pw.Address), got = d.Scams.Withdraw(p, pw, day);
                        _message = amt <= 0 ? L.T("pool.err.none") : got > 0 ? L.T("pool.done.out", Fmt.Num(got)) : L.T("pool.done.lock", Fmt.Num(amt), day + Tuning.YieldLockDays);
                    }
                    Session.Save();
                    Open();
                },
            });
        }

        /// <summary>空投页：可领取的空投（名字、奖励、要求的授权）和当前钱包的授权（可撤销）。领取和撤销都是合约调用，签名时 PLEDGER 上会写明授权范围。</summary>
        private static void AirdropPage(CustomUIBuilder b)
        {
            var d = Session.Data;
            var w = d.Hot;
            int day = StoreStation.GetDayCounter();
            Columns(b, l =>
            {
                l.AddHeader(L.T("air.h"));
                var list = d.Scams.Airdrops.Where(a => !a.Claimed && a.Expire >= day).ToList();
                if (list.Count == 0) l.AddLabel(L.T("air.none"), "air_none_dim");
                foreach (var a in list)
                {
                    var ad = a;
                    l.AddLabel(L.T("air.row", a.Name, Fmt.Num(a.Reward), ApproveText(a.Approve, a.Asset), a.Expire));
                    bool authed = d.Connected && Scams.Authorized(a, w);
                    l.AddLabel(authed ? L.T("air.authed", WalletWindow.NameOf(w)) : L.T("air.unauth"), authed ? "air_authed_dim" : "air_unauth_dim");
                    if (authed) l.AddButton(L.T("air.claim"), () => ClaimAirdrop(ad));
                    else l.AddButton(L.T("air.auth"), () => AuthorizeAirdrop(ad));
                    l.AddSpace(4f);
                }
            }, r =>
            {
                r.AddHeader(L.T("air.approvals", WalletWindow.NameOf(w)));
                var aps = d.Scams.Approvals.Where(x => ChainAddress.Same(x.Wallet, w.Address)).ToList();
                if (aps.Count == 0) r.AddLabel(L.T("air.approvals.none"), "air_app_none_dim");
                foreach (var ap in aps)
                {
                    int id = ap.Id;
                    r.AddLabel(L.T("air.approval", ap.Spender, ApproveText(ap.Limit, ap.Asset)));
                    r.AddButton(L.T("air.revoke"), () => RevokeApproval(id));
                    r.AddSpace(4f);
                }
            });
        }

        private static string ApproveText(double limit, string asset) =>
            limit <= 0 ? L.T("air.noapprove") : double.IsPositiveInfinity(limit) ? L.T("air.unlimited", asset) : L.T("air.limited", asset, Fmt.Num(limit));

        /// <summary>为这个空投授权当前连接的钱包：在钱包上签授权合约，摘要里写明授权范围（钓鱼空投的范围会写得很大）。</summary>
        private static void AuthorizeAirdrop(Airdrop a)
        {
            var d = Session.Data;
            if (!d.Connected) { _message = L.T("web3.err.noconn"); Open(); return; }
            _message = L.T("hw.wait");
            Open();
            var w = d.Hot;
            WalletWindow.OpenForBill(new WalletWindow.Bill
            {
                Contract = true, Party = Session.Data.Address("airdrop:" + a.Name), Asset = Assets.CRC, Amount = 0, Spend = 0, FeeAsset = BestFeeAsset(w),
                Summary = L.T("air.auth.sum", a.Name, ApproveText(a.Approve, a.Asset)),
                Execute = pw =>
                {
                    Scams.Authorize(a, pw, StoreStation.GetDayCounter());
                    _message = L.T("air.authed.msg", a.Name);
                    Log.Info($"授权空投 {a.Name}：钱包 {pw.Address}");
                    Session.Save();
                    Open();
                },
            });
        }

        private static void ClaimAirdrop(Airdrop a)
        {
            var d = Session.Data;
            if (!d.Connected || !Scams.Authorized(a, d.Hot)) { _message = L.T("air.err.unauth"); Open(); return; }
            _message = L.T("hw.wait");
            Open();
            var w = d.Hot;
            WalletWindow.OpenForBill(new WalletWindow.Bill
            {
                Contract = true, Party = Session.Data.Address("airdrop:" + a.Name), Asset = Assets.CRC, Amount = 0, Spend = 0, FeeAsset = BestFeeAsset(w),
                Summary = L.T("air.sum", a.Name, ApproveText(a.Approve, a.Asset)),
                Execute = pw =>
                {
                    var ap = d.Scams.Claim(a, pw, StoreStation.GetDayCounter(), d.Rng, out double got);
                    _message = L.T("air.claimed", Fmt.Num(got));
                    Log.Info($"领取空投 {a.Name}：得 {got}，授权 {(ap == null ? "无" : ap.Asset + " " + ap.Limit)}，骗局 {a.Scam}");
                    Session.Save();
                    Open();
                },
            });
        }

        private static void RevokeApproval(int id)
        {
            var d = Session.Data;
            var ap = d.Scams.Approvals.Find(x => x.Id == id);
            if (ap == null) return;
            var w = d.Own(ap.Wallet) ?? d.Hot;
            _message = L.T("hw.wait");
            Open();
            WalletWindow.OpenForBill(new WalletWindow.Bill
            {
                Contract = true, Party = Session.Data.Address("airdrop:" + ap.Spender), Asset = Assets.CRC, Amount = 0, Spend = 0, FeeAsset = BestFeeAsset(w),
                Summary = L.T("air.revoke.sum", ap.Spender, ap.Asset),
                Execute = pw =>
                {
                    _message = ChainAddress.Same(pw.Address, ap.Wallet) && d.Scams.Revoke(id) ? L.T("air.revoked") : L.T("air.err.wallet");
                    Session.Save();
                    Open();
                },
            });
        }

        private enum YieldTab { Pools, Mine }
        private static YieldTab _yieldTab = YieldTab.Pools;

        /// <summary>理财页：两个子页。"存钱"是 NPC 开的第三方收益池，你存进去吃利息（可能是骗局）；"自己发"是你当庄开高息理财（庞氏），下层的人往你这儿存。</summary>
        private static void YieldPage(CustomUIBuilder b)
        {
            b.BeginRow(4f);
            foreach (YieldTab t in Enum.GetValues(typeof(YieldTab)))
            {
                var tab = t;
                string name = L.T("yield.sub." + t.ToString().ToLowerInvariant());
                PledgerSkin.Pick(b, name, tab == _yieldTab, () => { _yieldTab = tab; _message = null; Open(); }, _picked);
            }
            b.End();
            if (_yieldTab == YieldTab.Mine) { PonziPage(b); return; }
            var d = Session.Data;
            var w = d.Hot;
            int day = StoreStation.GetDayCounter();
            Columns(b, l => YieldPools(l, w, day), r =>
            {
                r.AddHeader(L.T("pool.side.h"));
                r.AddLabel(L.T("predict.acct", WalletWindow.NameOf(w), Fmt.Num(w.Avail(Assets.CRC))));
                AmountRow(r);
            });
        }

        private static double _ponziRate = 0.04;
        private static bool _ponziConfirm;

        private static int LowerRep() => StoreReputation.GetStoreReputation(StoreReputation.FACTION_LOWER_LEVEL)?.GetReputation() ?? 0;

        /// <summary>理财页：开一个高息理财产品，下层的人往里存钱；池子随时可以卷走，但每晚都有被举报的风险。开张和卷走是链上合约调用，用当前钱包，要签名；之后每晚垫付的利息从发行人钱包出。</summary>
        private static void PonziPage(CustomUIBuilder b)
        {
            var d = Session.Data;
            var pz = d.Ponzi;
            int day = StoreStation.GetDayCounter();
            Columns(b, l =>
            {
                l.AddHeader(L.T("ponzi.h"));
                var issuer = pz.Open ? d.Own(pz.Wallet) ?? d.Hot : d.Hot;
                l.AddLabel(L.T("ponzi.acct", WalletWindow.NameOf(issuer), Fmt.Num(issuer.Avail(Assets.CRC))));
                l.AddSeparator();
                if (pz.Open)
                {
                    l.AddLabel(L.T("ponzi.rate", (pz.Rate * 100).ToString("0", CultureInfo.InvariantCulture)));
                    l.AddLabel(L.T("ponzi.pool", Fmt.Num(pz.Pool), Fmt.Num(Ponzi.Cap(day))));
                    l.AddLabel(L.T("ponzi.inflow", Fmt.Num(pz.Inflow)));
                    l.AddLabel(L.T("ponzi.interest", Fmt.Num(pz.Pool * pz.Rate)));
                    l.AddLabel(L.T("ponzi.own", Fmt.Num(pz.OwnPaid)));
                    double risk = Ponzi.ReportChance(pz.Rate, pz.Pool, day);
                    l.AddLabel(L.T("ponzi.wind", L.T(risk < 0.05 ? "ponzi.wind.low" : risk < 0.10 ? "ponzi.wind.mid" : "ponzi.wind.high")), risk >= 0.10 ? "ponzi_risk_note" : risk >= 0.05 ? "ponzi_risk_dim" : "ponzi_risk_ok");
                }
                else l.AddLabel(pz.Done ? L.T("ponzi.done") : day < Tuning.PonziMinDay ? L.T("ponzi.early", Tuning.PonziMinDay) : L.T("ponzi.idle"), "ponzi_status_dim");
            }, r =>
            {
                if (pz.Open)
                {
                    r.AddHeader(L.T("ponzi.ops"));
                    r.AddButton(_ponziConfirm ? L.T("ponzi.rug.confirm", Fmt.Num(pz.Pool)) : L.T("ponzi.rug", Fmt.Num(pz.Pool)), () =>
                    {
                        if (!_ponziConfirm) { _ponziConfirm = true; Open(); return; }
                        _ponziConfirm = false;
                        var iw = d.Own(pz.Wallet) ?? d.Hot;
                        _message = L.T("hw.wait");
                        Open();
                        WalletWindow.OpenForBill(new WalletWindow.Bill
                        {
                            Contract = true, Party = Session.Data.Address("ponzi"), Asset = Assets.CRC, Amount = 0, Spend = 0, FeeAsset = BestFeeAsset(iw),
                            Summary = L.T("ponzi.rug.sum", Fmt.Num(pz.Pool)),
                            Execute = pw =>
                            {
                                if (!pz.Open || !ChainAddress.Same(pw.Address, pz.Wallet)) { _message = L.T("ponzi.err.wallet"); Open(); return; }
                                double take = pz.Rug(pw);
                                PlayerStore.Instance.secData.CommitCrime("FRAUD", (int)Tuning.PonziCrime);
                                StoreReputation.GetStoreReputation(StoreReputation.FACTION_LOWER_LEVEL)?.SetReputation(0);
                                News.Show("pd_news_ponzi_rug_" + day, L.T("news.ponzi.rug.title"), L.T("news.ponzi.rug.desc"), 4);
                                Log.Info($"卷走理财池 {take:0}，自己垫的利息 {pz.OwnPaid:0}，钱包 {pw.Address}");
                                _message = L.T("ponzi.rugged", Fmt.Num(take));
                                Session.Save();
                                Open();
                            },
                        });
                    });
                    return;
                }
                if (pz.Done || day < Tuning.PonziMinDay) return;
                r.AddHeader(L.T("ponzi.open"));
                r.BeginRow(4f);
                foreach (double rate in Tuning.PonziRates)
                {
                    double rt = rate;
                    string t = (rate * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
                    PledgerSkin.Pick(r, t, Math.Abs(rt - _ponziRate) < 1e-9, () => { _ponziRate = rt; Open(); }, _picked);
                }
                r.End();
                r.AddLabel(L.T("ponzi.ad", Fmt.Num(Tuning.PonziAdCost)), "ponzi_ad_dim");
                r.AddButton(L.T("ponzi.start"), () =>
                {
                    if (d.Hot.Avail(Assets.CRC) + 1e-9 < Tuning.PonziAdCost) { _message = L.T("ponzi.err"); Open(); return; }
                    double rate = _ponziRate;
                    _message = L.T("hw.wait");
                    Open();
                    WalletWindow.OpenForBill(new WalletWindow.Bill
                    {
                        Contract = true, Party = Session.Data.Address("ponzi"), Asset = Assets.CRC, Amount = Tuning.PonziAdCost,
                        Summary = L.T("ponzi.start.sum", (rate * 100).ToString("0", CultureInfo.InvariantCulture), Fmt.Num(Tuning.PonziAdCost)),
                        Execute = pw =>
                        {
                            _message = pz.Start(pw, rate, StoreStation.GetDayCounter()) ? L.T("ponzi.started") : L.T("ponzi.err");
                            Session.Save();
                            Open();
                        },
                    });
                });
            });
        }

        private static int _predPick;
        private enum PredTab { Markets, Intel }
        private static PredTab _predTab = PredTab.Markets;

        private static string P2(double x) => x.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>预测页：盘口（左边列表与最近结果，右边选中盘口的价格、线索、买卖）和情报（情报贩子）。下注和卖出是链上合约调用，用当前钱包的 CRC，要在钱包上签名；持仓记在签名的钱包名下。</summary>
        private static void PredictPage(CustomUIBuilder b)
        {
            if (_predTab == PredTab.Intel && !Unlock.BlackUnlocked) _predTab = PredTab.Markets;
            if (Unlock.BlackUnlocked)
            {
                b.BeginRow(4f);
                foreach (PredTab t in Enum.GetValues(typeof(PredTab)))
                {
                    var tab = t;
                    string name = L.T("predict.tab." + t.ToString().ToLowerInvariant());
                    PledgerSkin.Pick(b, name, tab == _predTab, () => { _predTab = tab; _message = null; Open(); }, _picked);
                }
                b.End();
            }
            if (_predTab == PredTab.Intel) { IntelPage(b); return; }
            var d = Session.Data;
            var bk = d.Predict;
            var w = d.Hot;
            int day = StoreStation.GetDayCounter();
            var open = bk.Markets.Where(m => m.Outcome < 0).OrderBy(m => m.IsOpen(day) ? 0 : 1).ThenBy(m => m.FromDay).ThenBy(m => m.Id).ToList();
            var sel = open.Find(m => m.Id == _predPick) ?? open.FirstOrDefault();
            _predPick = sel?.Id ?? 0;
            Columns(b, l =>
            {
                l.AddHeader(L.T("predict.h"));
                l.AddLabel(L.T("predict.acct", WalletWindow.NameOf(w), Fmt.Num(w.Avail(Assets.CRC))));
                l.AddSeparator();
                // 选中 PSC 价格盘时，在列表上方画日 K 和阈值线
                if (sel != null && sel.Kind == PredictKind.Price) { CandleChart.Add(l, "predict", Assets.PSC, sel.Param); l.AddSeparator(); }
                if (open.Count == 0) l.AddLabel(L.T("predict.none"), "pred_none_dim");
                foreach (var m in open)
                {
                    var mk = m;
                    var all = bk.PosAll(m.Id);
                    string q = PredictGame.Question(m);
                    l.BeginRow(4f);
                    PledgerSkin.Pick(l, q, m.Id == _predPick, () => { _predPick = mk.Id; _message = null; Open(); }, _picked);
                    l.End();
                    string state = m.IsOpen(day) ? "" : "　" + L.T("predict.closed");
                    string move = m.PrevP >= 0 && Math.Abs(m.PYes - m.PrevP) >= 0.02 ? L.T("predict.row.move", P2(m.PrevP), m.PYes > m.PrevP ? "↑" : "↓") : "";
                    l.AddLabel(L.T("predict.row", P2(m.PYes), P2(1 - m.PYes)) + move + (all.Count > 0 ? "　" + L.T("predict.held", L.T(all[0].Yes ? "predict.yes" : "predict.no"), Fmt.Num(all.Sum(x => x.Shares)), Fmt.Num(all.Sum(x => x.Cost))) : "") + state, "pred_row_dim");
                }
                if (bk.Stats.Count > 0)
                {
                    l.AddSeparator();
                    l.AddLabel(L.T("predict.stats.h"));
                    foreach (var kv in bk.Stats.OrderByDescending(k => k.Value.Count))
                        l.AddLabel(L.T("predict.stats.row", L.T("predict.kind." + kv.Key), kv.Value.Count, P2(kv.Value.AvgP0), (kv.Value.HitRate * 100).ToString("0", CultureInfo.InvariantCulture)), "pred_stat_dim");
                }
                if (bk.Recent.Count > 0)
                {
                    l.AddSeparator();
                    foreach (var r in Enumerable.Reverse(bk.Recent))
                        l.AddLabel(L.T("predict.result", r.Day, r.Title, r.Outcome == 2 ? L.T("predict.void") : L.T(r.Outcome == 1 ? "predict.yes" : "predict.no"), r.Held ? (r.Pnl >= 0 ? "+" : "") + Fmt.Num(r.Pnl) : "—"), "pred_res_dim");
                }
            }, r =>
            {
                if (sel == null) { r.AddHeader(L.T("predict.h")); return; }
                r.AddHeader(L.T("predict.pick"));
                r.AddLabel(PredictGame.Question(sel));
                r.AddLabel(L.T("predict.price", P2(sel.PYes), P2(1 - sel.PYes)));
                foreach (var clue in PredictGame.Clues(sel)) r.AddLabel("· " + clue, "pred_clue_dim");
                foreach (var pos in bk.PosAll(sel.Id))
                {
                    var ow = d.Own(pos.Wallet);
                    r.AddLabel(L.T("predict.pos", ow != null ? WalletWindow.NameOf(ow) : pos.Wallet, L.T(pos.Yes ? "predict.yes" : "predict.no"), Fmt.Num(pos.Shares)));
                    if (pos.Wallet == w.Address)
                    {
                        r.AddLabel(L.T("predict.pos3", P2(pos.Cost / pos.Shares), P2(sel.PriceOf(pos.Yes))), "pred_pos_dim");
                        r.AddLabel(L.T("predict.pos2", Fmt.Num(pos.Cost), Fmt.Num(pos.Shares)), "pred_pos_dim");
                        r.AddLabel(L.T("predict.pos4", Fmt.Num(sel.SellValue(pos.Yes, pos.Shares) * (1 - Tuning.PredictFee))), "pred_pos_dim");
                    }
                }
                if (!sel.IsOpen(day)) { r.AddLabel(L.T("predict.closed.note"), "pred_closed_note"); return; }
                AmountRow(r);
                if (double.TryParse((_amount ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0 && v < 1e7)
                {
                    foreach (bool yes in new[] { true, false })
                    {
                        double shares = sel.SharesFor(yes, v / (1 + Tuning.PredictFee));
                        r.AddLabel(L.T("predict.preview", L.T(yes ? "predict.yes" : "predict.no"), Fmt.Num(shares), P2(v / shares), Fmt.Num(shares - v)), "pred_prev_dim");
                    }
                }
                r.BeginRow(6f);
                r.AddButton(L.T("predict.buy.yes"), () => PredictBuy(sel, true));
                r.AddButton(L.T("predict.buy.no"), () => PredictBuy(sel, false));
                r.End();
                r.AddSpace(4f);
                r.AddButton(L.T("predict.sell"), () => PredictSell(sel));
            });
        }

        /// <summary>情报页：局势简报，以及对派系行动、矿机时代盘口的内幕消息。付款是 CRT 的合约调用（销毁），要签名。</summary>
        private static void IntelPage(CustomUIBuilder b)
        {
            var d = Session.Data;
            var w = d.Hot;
            int day = StoreStation.GetDayCounter();
            Columns(b, l =>
            {
                l.AddHeader(L.T("intel.h"));
                if (!PredictGame.BrokerOpen) { l.AddLabel(L.T("intel.locked", Tuning.IntelRep), "intel_locked_note"); return; }
                l.AddLabel(L.T("predict.acct.crt", WalletWindow.NameOf(w), Fmt.Num(w.Avail(Assets.CRT))));
                l.AddButton(L.T("intel.brief.buy", Fmt.Num(Tuning.IntelBriefPrice)), IntelBuyBrief);
                l.AddSeparator();
                if (d.Brief.Count == 0) l.AddLabel(L.T("intel.brief.none"), "intel_brief_dim");
                else
                {
                    l.AddLabel(L.T("intel.brief.day", d.BriefDay), "intel_day_dim");
                    foreach (var line in d.Brief) l.AddLabel(line, "intel_line_dim");
                }
            }, r =>
            {
                r.AddHeader(L.T("intel.tips.h"));
                if (!PredictGame.BrokerOpen) return;
                var list = d.Predict.Markets.Where(m => PredictGame.Tippable(m, day)).OrderBy(m => m.FromDay).ToList();
                if (list.Count == 0) r.AddLabel(L.T("intel.tips.none"), "intel_tips_none_dim");
                foreach (var m in list)
                {
                    var mk = m;
                    r.AddLabel(PredictGame.Question(m));
                    var tip = d.Predict.TipFor(m.Id);
                    if (tip != null) { r.AddLabel(L.T("predict.clue.tip", L.T("predict.tier." + tip.Tier), L.T(tip.Claim ? "predict.yes" : "predict.no")), "intel_tip_dim"); continue; }
                    for (int t = 0; t < 3; t++)
                    {
                        int tier = t;
                        r.AddButton(L.T("intel.tip.buy", (int)Math.Round(Tuning.IntelTipAcc[t] * 100), Fmt.Num(Tuning.IntelTipPrice[t] * m.Loss)), () => IntelBuyTip(mk, tier));
                        r.Last?.SetPreferredSize(RightW - 30f, 28f * F);
                        if (t < 2) r.AddSpace(2f);
                    }
                }
            });
        }

        private static void IntelBuyBrief()
        {
            var d = Session.Data;
            double price = Tuning.IntelBriefPrice;
            if (d.Hot.Avail(Assets.CRT) + 1e-9 < price) { _message = L.T("intel.err.funds"); Open(); return; }
            _message = L.T("hw.wait");
            Open();
            WalletWindow.OpenForBill(new WalletWindow.Bill
            {
                Contract = true, Party = Session.Data.Address("intel"), Asset = Assets.CRT, Amount = price, Summary = L.T("intel.brief.sum", Fmt.Num(price)),
                Execute = pw =>
                {
                    if (pw.Avail(Assets.CRT) + 1e-9 < price) { _message = L.T("intel.err.funds"); Open(); return; }
                    pw.Add(Assets.CRT, -price);
                    int day = StoreStation.GetDayCounter();
                    bool fake = d.Rng.Chance(Tuning.IntelFake);
                    d.Brief = PredictGame.MakeBrief(fake, day); d.BriefDay = day;
                    Log.Info($"局势简报：{(fake ? "假情报" : "真")}，{string.Join(" | ", d.Brief)}");
                    _message = L.T("intel.brief.done");
                    Session.Save();
                    Open();
                },
            });
        }

        private static void IntelBuyTip(PredictMarket m, int tier)
        {
            var d = Session.Data;
            double price = Tuning.IntelTipPrice[tier] * m.Loss;
            if (d.Hot.Avail(Assets.CRT) + 1e-9 < price) { _message = L.T("intel.err.funds"); Open(); return; }
            _message = L.T("hw.wait");
            Open();
            WalletWindow.OpenForBill(new WalletWindow.Bill
            {
                Contract = true, Party = Session.Data.Address("intel"), Asset = Assets.CRT, Amount = price, Summary = L.T("intel.tip.sum", Fmt.Num(price), PredictGame.Question(m)),
                Execute = pw =>
                {
                    int day = StoreStation.GetDayCounter();
                    var truth = PredictGame.Truth(m);
                    if (pw.Avail(Assets.CRT) + 1e-9 < price || truth == null || !m.IsOpen(day) || d.Predict.TipFor(m.Id) != null) { _message = L.T("intel.err.funds"); Open(); return; }
                    pw.Add(Assets.CRT, -price);
                    var tip = PredictBook.MakeTip(m, truth.Value, tier, d.Rng);
                    d.Predict.Tips.Add(tip);
                    Log.Info($"内幕消息 #{m.Id} 第 {tier + 1} 档：说{(tip.Claim ? "是" : "否")}，真相{(truth.Value ? "是" : "否")}");
                    _message = L.T("intel.tip.done", L.T(tip.Claim ? "predict.yes" : "predict.no"));
                    Session.Save();
                    Open();
                },
            });
        }

        private static void PredictBuy(PredictMarket m, bool yes)
        {
            if (!ReadAmount(out double v)) return;
            var d = Session.Data;
            var w = d.Hot;
            if (w.Avail(Assets.CRC) + 1e-9 < v) { _message = L.T("predict.err.Funds"); Open(); return; }
            if (d.Predict.PosAll(m.Id).Any(x => x.Yes != yes)) { _message = L.T("predict.err.Opposite"); Open(); return; }
            string side = L.T(yes ? "predict.yes" : "predict.no");
            _message = L.T("hw.wait");
            Open();
            WalletWindow.OpenForBill(new WalletWindow.Bill
            {
                Contract = true, Party = Session.Data.Address("predict"), Asset = Assets.CRC, Amount = v,
                Summary = L.T("predict.sum.buy", PredictGame.Question(m), side, Fmt.Num(v)),
                Execute = pw =>
                {
                    int day = StoreStation.GetDayCounter();
                    bool insider = PredictGame.Known(m, yes);
                    var res = d.Predict.Buy(pw, m, yes, v, day, insider, out double shares);
                    _message = res == PredictResult.Ok ? L.T("predict.bought", side, Fmt.Num(shares), Fmt.Num(v)) : L.T("predict.err." + res);
                    if (res == PredictResult.Ok) { Log.Info($"预测下注 #{m.Id} {(yes ? "是" : "否")} {shares:0.0} 份，花 {v:0.##}，钱包 {pw.Address}，内幕 {insider}"); Session.Save(); }
                    Open();
                },
            });
        }

        private static void PredictSell(PredictMarket m)
        {
            var d = Session.Data;
            var w = d.Hot;
            if (d.Predict.PosOf(m.Id, w.Address) == null) { _message = L.T("predict.err.NoPosition"); Open(); return; }
            _message = L.T("hw.wait");
            Open();
            WalletWindow.OpenForBill(new WalletWindow.Bill
            {
                Contract = true, Party = Session.Data.Address("predict"), Asset = Assets.CRC, Amount = 0, Spend = 0, FeeAsset = BestFeeAsset(w),
                Summary = L.T("predict.sum.sell", PredictGame.Question(m)),
                Execute = pw =>
                {
                    var res = d.Predict.SellAll(pw, m, StoreStation.GetDayCounter(), Session.Price, out double got, out double taint);
                    _message = res == PredictResult.Ok ? L.T("predict.sold", Fmt.Num(got)) : L.T("predict.err." + res);
                    if (res == PredictResult.Ok && taint > 1e-9)
                    {
                        _message = L.T("predict.sold.caught", Fmt.Num(got), Fmt.Num(taint));
                        Log.Info($"内幕卖出：收回 {got:0}，其中 {taint:0.##} CRC 记为脏的");
                    }
                    if (res == PredictResult.Ok) Session.Save();
                    Open();
                },
            });
        }

        private static string PerpErr(PerpResult res) => L.T("perp.err." + res);

        private static void PerpOpen()
        {
            if (!ReadAmount(out double v)) return;
            var d = Session.Data;
            var res = d.Perps.Open(d.Poinbase, _perpProduct, _perpLong, v, _perpLev, PerpSpot(_perpProduct), StoreStation.GetDayCounter());
            if (res == PerpResult.Ok) CandleChart.Trade(_perpProduct, v * _perpLev);
            _message = res == PerpResult.Ok ? L.T("perp.opened", _perpProduct, L.T(_perpLong ? "perp.long" : "perp.short"), Fmt.Num(v), _perpLev) : PerpErr(res);
            if (res == PerpResult.Ok) Session.Save();
            Open();
        }

        private static void PerpAdd()
        {
            if (!ReadAmount(out double v)) return;
            var d = Session.Data;
            var res = d.Perps.AddMargin(d.Poinbase, _perpProduct, v);
            _message = res == PerpResult.Ok ? L.T("perp.added", Fmt.Num(v)) : PerpErr(res);
            if (res == PerpResult.Ok) Session.Save();
            Open();
        }

        private static void PerpClose()
        {
            var d = Session.Data;
            var pos = d.Perps.Of(_perpProduct);
            if (pos == null) { _message = PerpErr(PerpResult.NoPosition); Open(); return; }
            double margin = pos.Margin;
            CandleChart.Trade(_perpProduct, pos.Notional(PerpSpot(_perpProduct)));
            double paid = d.Perps.Close(d.Poinbase, _perpProduct, PerpSpot(_perpProduct), StoreStation.GetDayCounter(), out double deficit);
            // 操纵：平仓盈利，而且盈利方向和玩家自己推价的方向一致，按推价幅度被查：盈利照常留在 Poinbase 账户，只记诈骗罪证
            double push = d.Market.PlayerPush, profit = paid - margin;
            bool aligned = _perpProduct == "PSC" && profit > 0 && (pos.Long ? push > 0 : push < 0);
            if (aligned && d.Rng.Chance(Math.Min(1, Market.ManipChance(push) * StoryMods.Current.Enforce(StoreStation.GetDayCounter()))))
            {
                int day = StoreStation.GetDayCounter();
                int fraud = (int)Math.Round(Wallet.FraudFor(profit));
                if (fraud > 0) PlayerStore.Instance.secData.CommitCrime("FRAUD", fraud);
                News.Show("pd_news_manip_" + day, L.T("news.manip.title"), L.T("news.manip.desc"), 3);
                _message = L.T("perp.manip", Fmt.Credits(profit), fraud);
                Log.Info($"永续操纵被查：盈利 {profit:0}，记诈骗罪证 {fraud}，推价 {push:0.000}");
                Session.Save();
                Open();
                return;
            }
            _message = L.T("perp.closed", Fmt.Num(paid), (paid - margin >= 0 ? "+" : "") + Fmt.Num(paid - margin)) + (deficit > 0 ? "\n" + L.T("perp.deficit", Fmt.Credits(deficit)) : "");
            Session.Save();
            Open();
        }

        /// <summary>Poinbase 提到当前钱包：托管账户直接转，不用签名。污点按比例随币走。</summary>
        /// <summary>钱包页：全部硬件钱包，连接（设为当前）、打开实体钱包。</summary>
        private enum WalletTab { List, Explorer }
        private static WalletTab _walletTab = WalletTab.List;

        private static void WalletsPage(CustomUIBuilder b)
        {
            var d = Session.Data;
            TabRows(b, Enum.GetValues(typeof(WalletTab)).Cast<WalletTab>().Select(t => { var tab = t; return new TabDef { Name = L.T("wallets.sub." + t.ToString().ToLowerInvariant()), On = tab == _walletTab, Click = () => { _walletTab = tab; _message = null; Open(); } }; }).ToList(), false);
            if (_walletTab == WalletTab.Explorer) { ExplorerPage(b); return; }
            b.AddLabel(L.T("wallets.count", d.Wallets.Count, Tuning.WalletMax), "wallets_count_dim");
            if (d.Wallets.Count == 0) { b.AddLabel(L.T("wallets.none"), "wallets_none_dim"); return; }
            bool wide = L.Wide && Ext < 100f;
            float k = Ext < 100f ? 1f : (ScreenW - 70f) / 740f;
            bool big = Ext >= 100f;
            float btnCut = wide ? 112f : big ? 78f : 150f, statW = wide ? 92f : big ? 70f : 84f;
            var cols = (wide ? new[] { 100f, 92f, 62f, 62f, 372f } : Ext < 100f ? new[] { 100f, 110f, 85f, 70f, 375f } : new[] { 165f, 100f, 70f, 115f, 290f }).Select(x => x * k).ToArray();
            var aligns = new[] { TextAnchor.MiddleLeft, TextAnchor.MiddleLeft, TextAnchor.MiddleRight, TextAnchor.MiddleRight, TextAnchor.MiddleLeft };
            TableHeader(b, 740f * k, cols, aligns, L.T("wallets.col.name"), L.T("wallets.col.addr"), L.T("col.value"), L.T("wallets.col.taint"), "");
            for (int i = 0; i < d.Wallets.Count; i++)
            {
                var w = d.Wallets[i];
                int idx = i;
                bool isCurrent = idx == d.Current;
                // 一行三块：数据格（整行反色的是这一块）、操作键（和顶栏页签一样的黑色触控键，放在纸面上）、连接状态靠右
                b.BeginRow(4f, TextAnchor.MiddleLeft);
                b.BeginPanel(cols[0] + cols[1] + cols[2] + cols[3] + 8f, 32f * F, TextAnchor.MiddleLeft, 4f, 0f);
                _rows.Add(new PledgerSkin.RowDef
                {
                    Root = b.Last.root,
                    Image = b.Last.image,
                    Selected = isCurrent,
                    OnClick = () => { d.Current = idx; Session.Save(); Open(); }
                });
                b.BeginRow(0f, TextAnchor.MiddleLeft);
                Cell(b, cols[0], (isCurrent ? "● " : "") + WalletWindow.NameOf(w), false, TextAnchor.MiddleLeft);
                Cell(b, cols[1], w.Address, false, TextAnchor.MiddleLeft);
                Cell(b, cols[2], Fmt.Credits(w.Value(Session.Price)), false, TextAnchor.MiddleRight);
                Cell(b, cols[3], Fmt.Credits(w.Taint(Session.Price)), false, TextAnchor.MiddleRight);
                b.End();
                b.End();
                b.BeginPanel(cols[4] - btnCut * k, 32f * F, TextAnchor.MiddleLeft, 0f, 0f);
                _clear.Add(b.Last.image);
                b.BeginRow(4f, TextAnchor.MiddleLeft);
                RowKey(b, L.T("wallets.open"), () => WalletWindow.Open(w));
                RowKey(b, L.T("hw.copy"), () => { Clipboard.Copy(w.Address); _message = L.T("hw.copied.addr", w.Address); Open(); });
                RowKey(b, L.T("wallets.rename"), () => { _renamePick = w.Address; _renameText = w.Label ?? ""; _message = null; Open(); });
                RowKey(b, _destroyPick == w.Address ? L.T("wallets.destroy.sure") : L.T("wallets.destroy"), () => DestroyWallet(w));
                b.End();
                b.End();
                b.BeginPanel(statW * k, 32f * F, TextAnchor.MiddleRight, 0f, 0f);
                _clear.Add(b.Last.image);
                b.BeginRow(0f, TextAnchor.MiddleRight);
                if (idx != d.Current) RowKey(b, L.T("wallets.use"), () => { d.Current = idx; Session.Save(); Open(); });
                else { b.AddLabel(L.T("wallets.current"), "wallets_cur_dim"); PledgerSkin.FitCurrent(b); }
                b.End();
                b.End();
                b.End();
            }
            if (d.Connected)
            {
                // 选中的钱包：下面列出它持有的币和数量
                b.AddSeparator();
                b.AddHeader(L.T("web3.wallet.h") + "　" + WalletWindow.NameOf(d.Hot));
                Holdings(b, d.Hot, a => true);
            }
            if (_renamePick != null) RenameRow(b);
        }

        /// <summary>表格行里的操作键：固定 24 高，垂直居中（行高 32）。</summary>
        private static void RowKey(CustomUIBuilder b, string text, Action onClick)
        {
            b.AddButton(text, onClick);
            var go = b.Last.root;
            if (go == null) return;
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.minHeight = 26f * F; le.preferredHeight = 26f * F; le.flexibleHeight = 0f;
        }

        private static string _destroyPick, _renamePick, _renameText = "";

        /// <summary>改名：表格下方一行输入框；留空恢复默认名。</summary>
        private static void RenameRow(CustomUIBuilder b)
        {
            var w = Session.Data.Own(_renamePick);
            if (w == null) { _renamePick = null; return; }
            b.AddSeparator();
            b.BeginRow(8f, TextAnchor.MiddleLeft);
            b.AddLabel(L.T("wallets.rename.h", w.Address));
            b.BeginPanel(220f, -1f, TextAnchor.MiddleLeft, 0f, 0f);
            _clear.Add(b.Last.image);
            b.AddInput(WalletWindow.NameOf(w), _renameText, s => _renameText = s, "rename", s => _renameText = s);
            b.End();
            b.AddButton(L.T("wallets.rename.ok"), () =>
            {
                string name = (_renameText ?? "").Trim();
                w.Label = name.Length == 0 ? null : name.Length > 16 ? name.Substring(0, 16) : name;
                _renamePick = null;
                Session.Save();
                WalletWindow.RefreshDevice(w, true);
                Open();
            });
            b.AddButton(L.T("black.edit.cancel"), () => { _renamePick = null; Open(); });
            b.End();
        }

        /// <summary>销毁钱包：里面的币永久丢失；点两次确认。之后回到这个地址的退款、回款、混币提回都丢了（发给不存在的地址）。销毁的是当前连接的钱包时，变成没有连接。</summary>
        private static void DestroyWallet(Wallet w)
        {
            var d = Session.Data;
            int idx = d.Wallets.IndexOf(w);
            if (idx < 0) return;
            if (_destroyPick != w.Address) { _destroyPick = w.Address; _message = L.T("wallets.destroy.warn", WalletWindow.NameOf(w)); Open(); return; }
            _destroyPick = null;
            string name = WalletWindow.NameOf(w);
            WalletWindow.Close(w);
            var current = d.Connected ? d.Hot : null;
            d.Wallets.RemoveAt(idx);
            d.Current = current == null || current == w ? -1 : d.Wallets.IndexOf(current);
            Log.Info($"销毁硬件钱包 {w.Address}{(w.Poisoned ? "（被投毒）" : "")}");
            Session.Save();
            _message = L.T("wallets.destroyed", name, w.Address);
            Open();
        }

        /// <summary>硬件钱包拖到终端上：添加一个钱包（被投毒的钱包看起来一样）。</summary>
        public static bool CanAddWallet() => Session.Data != null && Session.Data.Wallets.Count < Tuning.WalletMax;

        public static void AddWallet(bool poisoned)
        {
            var w = Session.Data.AddWallet(poisoned);
            Log.Info($"添加硬件钱包 {w.Address}{(poisoned ? "（被投毒）" : "")}");
            Session.Save();
            _page = Page.Wallets; _walletTab = WalletTab.List;
            _message = L.T("wallets.added", WalletWindow.NameOf(w), w.Address);
            Open();
        }

        private sealed class TabDef { public string Name; public bool On; public Action Click; }
        private static float TabPx(string txt) => txt.Sum(c => c > 0x2E80 ? 18f : 9f) + 28f;

        /// <summary>
        /// 一排页签：按字数估宽，超出一行就折到下一行。connect 为真时把连接钱包的控件放在最后一行右端，放不下就单独一行。
        /// </summary>
        private static void TabRows(CustomUIBuilder b, List<TabDef> tabs, bool connect)
        {
            float avail = ScreenW - 40f, used = 0f;
            bool open = false;
            foreach (var t in tabs)
            {
                float wpx = TabPx(t.Name);
                if (open && used + wpx > avail) { b.End(); open = false; used = 0f; }
                if (!open) { b.BeginRow(4f, TextAnchor.MiddleLeft); open = true; }
                used += wpx + 4f;
                PledgerSkin.Pick(b, t.Name, t.On, t.Click, _picked);
            }
            if (!connect) { if (open) b.End(); return; }
            float widget = 120f + (Session.Data.Connected ? TabPx(L.T("web3.conn", WalletWindow.NameOf(Session.Data.Hot), ChainAddress.Short(Session.Data.Hot.Address))) : TabPx(L.T("web3.connect")));
            if (open && used + widget > avail) { b.End(); open = false; }
            if (!open) b.BeginRow(4f, TextAnchor.MiddleLeft);
            b.AddFlexibleSpace();
            ConnectWidget(b);
            b.End();
        }

        private static bool GrayAvailable => Unlock.MixerUnlocked || Unlock.BlackUnlocked || Unlock.OtcUnlocked;

        private static bool GrayTabOn(GrayTab t) => t == GrayTab.Mixer ? Unlock.MixerUnlocked : t == GrayTab.Black ? Unlock.BlackUnlocked : Unlock.OtcUnlocked;
        private enum GrayTab { Mixer, Black, Own }
        private static GrayTab _grayTab = GrayTab.Mixer;

        /// <summary>灰网：混币器和商店，各自在人脉网络里解锁，解锁了哪个就有哪个。</summary>
        private static void GrayPage(CustomUIBuilder b)
        {
            var tabs = Enum.GetValues(typeof(GrayTab)).Cast<GrayTab>().Where(GrayTabOn).ToList();
            if (!GrayTabOn(_grayTab) && tabs.Count > 0) _grayTab = tabs[0];
            if (tabs.Count > 1)
                TabRows(b, tabs.Select(t => { var tab = t; return new TabDef { Name = L.T("gray.sub." + t.ToString().ToLowerInvariant()), On = tab == _grayTab, Click = () => { _grayTab = tab; _message = null; Open(); } }; }).ToList(), false);
            if (!Session.Data.Connected) b.AddLabel(L.T("web3.err.noconn"), "term_note");
            if (_grayTab == GrayTab.Mixer) MixerPage(b); else if (_grayTab == GrayTab.Own) OwnMixPage(b); else BlackPage(b);
        }

        /// <summary>Web3：链上钱包与去中心化交易。</summary>
        private static void Web3Page(CustomUIBuilder b)
        {
            var w = Session.Data.Hot;
            if (_web3Tab == Web3Tab.Predict && !PredictGame.Available(StoreStation.GetDayCounter())) _web3Tab = Web3Tab.Trade;
            TabRows(b, Enum.GetValues(typeof(Web3Tab)).Cast<Web3Tab>()
                .Where(t => t != Web3Tab.Predict || PredictGame.Available(StoreStation.GetDayCounter()))
                .Select(t => { var tab = t; return new TabDef { Name = L.T("web3.sub." + t.ToString().ToLowerInvariant()), On = tab == _web3Tab, Click = () => { _web3Tab = tab; _message = null; Open(); } }; }).ToList(), false);
            if (!Session.Data.Connected) b.AddLabel(L.T("web3.err.noconn"), "term_note");
            if (_web3Tab == Web3Tab.Stake) { StakePage(b); return; }
            if (_web3Tab == Web3Tab.Predict) { PredictPage(b); return; }
            if (_web3Tab == Web3Tab.Ponzi) { YieldPage(b); return; }
            if (_web3Tab == Web3Tab.Airdrop) { AirdropPage(b); return; }
            Columns(b, l =>
            {
                l.AddHeader(L.T("web3.wallet.h") + (Session.Data.Connected ? "　" + WalletWindow.NameOf(w) : ""));
                Holdings(l, w, a => true);
                l.AddSeparator();
                l.AddLabel(L.T("wallet.total", Fmt.Credits(w.Value(Session.Price))));
                if (w.Taint(Session.Price) > 0.5) l.AddLabel(L.T("wallet.taint", Fmt.Credits(w.Taint(Session.Price))), "taint_note");
                PriceTable(l);
                l.AddSeparator();
                CandleChart.AddPicker(l, "web3", Assets.PSC, Open, _picked);
            }, r =>
            {
                r.AddHeader(L.T("trade.h"));
                SwapBlock(r);
            });
        }

        private static bool _connectOpen;

        /// <summary>Web3 页标签栏最右：已连接显示钱包名、地址和"断开连接"，没有连接显示"连接钱包"。状态和钱包页的"已连接"是同一份。</summary>
        private static void ConnectWidget(CustomUIBuilder b)
        {
            var d = Session.Data;
            if (d.Connected)
            {
                var w = d.Hot;
                b.AddLabel(L.T("web3.conn", WalletWindow.NameOf(w), ChainAddress.Short(w.Address)));
                PledgerSkin.Pick(b, L.T("web3.disconnect"), false, () => { d.Current = -1; _connectOpen = false; WalletWindow.Reset(); Session.Save(); Open(); }, _picked);
            }
            else PledgerSkin.Pick(b, L.T("web3.connect"), _connectOpen, () =>
            {
                if (d.Wallets.Count == 0) { _message = L.T("wallets.none"); Open(); return; }
                if (d.Wallets.Count == 1) { d.Current = 0; Session.Save(); Open(); return; }
                _connectOpen = !_connectOpen; Open();
            }, _picked);
        }

        /// <summary>有多个钱包时，点"连接钱包"在标签栏下面列出钱包，选一个连接。</summary>
        private static void ConnectPicker(CustomUIBuilder b)
        {
            var d = Session.Data;
            if (!_connectOpen || d.Connected) { _connectOpen = false; return; }
            b.BeginRow(4f);
            for (int i = 0; i < d.Wallets.Count; i++)
            {
                int idx = i; var w = d.Wallets[i];
                PledgerSkin.Pick(b, WalletWindow.NameOf(w) + "  " + ChainAddress.Short(w.Address), false, () => { d.Current = idx; _connectOpen = false; Session.Save(); Open(); }, _picked);
            }
            b.End();
        }

        private static string _explorerQuery = "";
        private static readonly float[] ExplWBase = new[] { 62f, 56f, 212f, 212f, 118f };
        private static float[] ExplW => Sc(ExplWBase);
        private static readonly TextAnchor[] ExplAligns = { TextAnchor.MiddleLeft, TextAnchor.MiddleLeft, TextAnchor.MiddleLeft, TextAnchor.MiddleLeft, TextAnchor.MiddleRight };

        /// <summary>地址的显示名：自己的钱包、Poinbase、混币器、全网矿池、黑市卖家，其余是缩写。</summary>
        /// <summary>地址的名称：自己的钱包、矿池、Poinbase、混币器、灰网平台、兑换处、商家类型；没有名称返回 null。</summary>
        private static string AddressName(string a)
        {
            var d = Session.Data;
            if (string.IsNullOrEmpty(a)) return null;
            if (a == "network") a = d.Address("pool");
            var own = d.Own(a);
            if (own != null) return WalletWindow.NameOf(own);
            if (ChainAddress.Same(a, d.Address("pool"))) return L.T("explorer.pool");
            if (ChainAddress.Same(a, d.Address("poinbase"))) return "Poinbase";
            // 其他收款方、合约按登记的键取名：空投、收益池带自己的名字
            string key = d.Addresses.FirstOrDefault(kv => ChainAddress.Same(kv.Value, a)).Key;
            if (key != null)
            {
                if (key.StartsWith("airdrop:") || key.StartsWith("yield:")) return key.Substring(key.IndexOf(':') + 1);
                if (key.StartsWith("otc:")) return L.T("otc.name");
                switch (key)
                {
                    case "mixer": return d.Story.MixerRebranded ? L.T("mixer.brand") : L.T("explorer.mixer");
                    case "mixer_old": return L.T("explorer.mixer");
                    case "platform": return L.T("merchant.Platform");
                    case "desk": return L.T("explorer.desk");
                    case "dex": return "DEX";
                    case "staking": return L.T("stake.h");
                    case "cra": return L.T("mint.h");
                    case "predict": return L.T("predict.h");
                    case "intel": return L.T("intel.h");
                    case "ponzi": return L.T("ponzi.h");
                    case "tumbler": return "Tumbler";
                    case "ownmix": return L.T("gray.sub.own");
                }
            }
            var sl = d.Black.Sellers.FirstOrDefault(x => ChainAddress.Same(x.Name, a));
            return sl != null ? L.T("merchant." + sl.Type) : null;
        }

        private static string AddressFull(string a) => string.IsNullOrEmpty(a) ? "—" : a == "network" ? Session.Data.Address("pool") : ChainAddress.Normalize(a);

        private static float TextPx(string t) => t.Sum(c => c > 0x2E80 ? 16f : 8.2f) * F;

        /// <summary>按列宽选显示内容：完整地址加名称放得下就都显示；放不下先去掉名称，再用缩写；被省略的地方给悬停提示（完整地址和名称）。</summary>
        private static void AddressCell(string a, float width, out string text, out string tip)
        {
            string full = AddressFull(a), name = AddressName(a);
            string both = name != null ? full + " (" + name + ")" : full;
            float avail = (width - 8f) * 1.15f;
            tip = null;
            if (TextPx(both) <= avail) { text = both; return; }
            tip = both;
            text = TextPx(full) <= avail ? full : ChainAddress.Short(full);
        }

        /// <summary>浏览器：公共数据，不需要连接钱包。查任意地址的转账，只显示真实发生的。</summary>
        private static void ExplorerPage(CustomUIBuilder b)
        {
            var d = Session.Data;
            b.BeginPanel(RightW - 30f, -1f, TextAnchor.MiddleCenter, 0f, 0f);
            _clear.Add(b.Last.image);
            b.AddInput(L.T("explorer.search"), _explorerQuery, s => _explorerQuery = s, "explorer", s => { _explorerQuery = s; Open(); });
            b.End();
            if (d.Wallets.Count > 0)
            {
                b.BeginRow(4f);
                PledgerSkin.Pick(b, L.T("explorer.all"), string.IsNullOrWhiteSpace(_explorerQuery), () => { _explorerQuery = ""; Open(); }, _picked);
                foreach (var ow in d.Wallets)
                {
                    var own = ow;
                    PledgerSkin.Pick(b, WalletWindow.NameOf(own), ChainAddress.Same(_explorerQuery, own.Address), () => { _explorerQuery = own.Address; Open(); }, _picked);
                }
                b.End();
            }
            b.AddSeparator();
            var rows = d.Ledger.Of(_explorerQuery, Tuning.LedgerShow);
            if (rows.Count == 0) { b.AddLabel(L.T("explorer.none"), "explorer_none_dim"); return; }
            TableHeader(b, ExplW.Sum() + 8f, ExplW, ExplAligns, L.T("explorer.col.block"), L.T("explorer.col.day"), L.T("explorer.col.from"), L.T("explorer.col.to"), L.T("explorer.col.amount"));
            var ew = ExplW;
            foreach (var e in rows)
            {
                AddressCell(e.From, ew[2], out string fromText, out string fromTip);
                AddressCell(e.To, ew[3], out string toText, out string toTip);
                TableRow(b, ew.Sum() + 8f, ew, ExplAligns,
                    new[] { "#" + e.Block, L.T("explorer.day", e.Day), fromText, toText, Fmt.Num(e.Amount) + " " + e.Asset },
                    false, null, new[] { null, null, fromTip, toTip, null });
            }
        }

        private enum BlackTab { Buy, Sell, Orders }
        private static BlackTab _blackTab = BlackTab.Buy;
        private static PayMode _mode = PayMode.Escrow;
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> IconCache = new System.Collections.Generic.Dictionary<string, Sprite>();

        /// <summary>货架上的商品图：游戏物品自己的贴图（本 mod 的显卡用本 mod 的贴图）。</summary>
        private static Sprite Icon(string id)
        {
            if (IconCache.TryGetValue(id, out var cached)) return cached;
            Sprite sprite = null;
            var it = ModHelper.SpawnItem(BlackDelivery.GameId(id));
            if (it != null)
            {
                try { sprite = it.useSpriteFromMod ? Items.ModSprite(it.spritePath) : RenderHandler.LoadFromAtlas(it.spriteAtlasPath, it.spritePath); }
                catch (Exception ex) { Log.Info($"商品图读取失败 {id}：{ex.Message}"); }
                it.Destroy();
            }
            IconCache[id] = sprite;
            return sprite;
        }

        /// <summary>Web3 页里的黑市：黑市派系的链上实体。货架式商城（现货、订单）、我的订单与情报、Ponero 兑换处。</summary>
        private static void BlackPage(CustomUIBuilder b)
        {
            int rep = StoreReputation.GetBMReputation();
            int day = StoreStation.GetDayCounter();
            var d = Session.Data;
            if (Night.ActiveEvents().Contains(MarketEvent.Crackdown)) { b.AddLabel(L.T("black.offline"), "black_offline_note"); return; }
            if (!BlackMarket.Open(rep)) { b.AddLabel(L.T("black.closed", Tuning.BmOpenRep, rep), "black_closed_note"); return; }
            b.BeginRow(4f);
            foreach (BlackTab t in Enum.GetValues(typeof(BlackTab)))
            {
                var tab = t;
                string name = L.T("black.sub." + t.ToString().ToLowerInvariant());
                PledgerSkin.Pick(b, name, tab == _blackTab, () => { _blackTab = tab; _message = null; _sellView = SellView.Showcase; _stage.Clear(); _delist.Clear(); _coinsOpen = false; _checkout = false; Open(); }, _picked);
            }
            b.End();
            if (d.Black.Closed(day)) { b.AddLabel(L.T("black.rugged", d.Black.ClosedUntil - day + 1), "black_rugged_note"); return; }
            if (_checkout) { CheckoutPage(b, d); return; }
            if (_blackTab == BlackTab.Orders) { OrdersPage(b, day); return; }
            if (_blackTab == BlackTab.Sell) { SellPage(b, day); return; }
            BuyPage(b, d, rep);
        }

        /// <summary>
        /// 买货：左栏是卖家列表（第一个是平台现货，立即到货但贵，其余是商家，便宜但要等），右栏是这家的货架。
        /// 点"购买"后右栏换成付款面板：选数量（和付款方式）、付款币种；商家不收手里的币时可以用 Web3 内置的自动换币（多付滑点）。论坛情报在最下面。
        /// </summary>
        private static void BuyPage(CustomUIBuilder b, SaveData d, int rep)
        {
            var w = d.Hot;
            var sellers = d.Black.Sellers;
            if (_merchant >= sellers.Count) _merchant = -1;
            bool platform = _merchant < 0;
            b.AddLabel(L.T("black.rep", rep, BlackMarket.MaxLevel(rep)) + (w.Taint(Session.Price) > 0.5 ? "　" + L.T("wallet.taint", Fmt.Credits(w.Taint(Session.Price))) : ""), "black_rep_dim");
            b.BeginRow(8f, TextAnchor.UpperLeft);
            b.BeginPanel(SellerW + 8f, -1f, TextAnchor.UpperLeft, 0f, 2f);
            _clear.Add(b.Last.image);
            var names = new System.Collections.Generic.List<string> { L.T("merchant.Platform") };
            for (int k = 0; k < sellers.Count; k++) names.Add(MerchantLabel(sellers[k], true));
            for (int i = 0; i < names.Count; i++)
            {
                int idx = i - 1;
                TableRow(b, SellerW, new[] { SellerW - 8f }, new[] { TextAnchor.MiddleLeft }, new[] { names[i] }, idx == _merchant, () => { _merchant = idx; _message = null; Open(); });
            }
            b.End();
            b.BeginPanel(ShelfW, -1f, TextAnchor.UpperLeft, 0f, 4f);
            _clear.Add(b.Last.image);
            ShopInfo(b, d, platform ? null : sellers[_merchant]);
            if (platform) PlatformShelf(b, d);
            else MerchantShelf(b, d);
            b.End();
            b.End();
            b.AddSeparator();
            b.AddHeader(L.T("black.forum.h"));
            if (d.Black.Forum.Count == 0) b.AddLabel(L.T("black.forum.quiet"), "forum_quiet_dim");
            foreach (var f in d.Black.Forum)
                b.AddLabel(f.StartsWith("phish:") ? L.T("black.forum.phish", f.Substring(6)) : f.StartsWith("scam:") ? L.T("black.forum.scam", BlackDelivery.DisplayName(f.Substring(5))) : f.StartsWith("busted:") ? L.T("black.forum.busted", f.Substring(7)) : L.T("black.forum." + f), "forum_dim");
        }

        private static float SellerW => 176f * F;
        private static float ShelfW => 560f + Ext * 0.78f;

        private static void PlatformShelf(CustomUIBuilder b, SaveData d)
        {
            if (d.Black.Offers.Count == 0) { b.AddLabel(L.T("black.empty"), "platform_empty_dim"); return; }
            Shelf(b, d.Black.Offers.Cast<BmGood>().ToList(), (c, g) =>
            {
                var offer = (BmOffer)g;
                GoodHead(c, g, L.T("black.stock", offer.Qty));
                c.AddLabel(L.T("black.price", offer.Price));
                c.AddFlexibleSpace();
                c.AddButton(L.T("black.buy"), () => AddToCart(-1, offer));
            });
        }

        private static double _sellMult = 1.0;

        private enum SellView { Showcase, Add, Fee }
        private static SellView _sellView = SellView.Showcase;
        private static readonly Dictionary<string, int> _stage = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _delist = new Dictionary<string, int>();
        private static bool _coinsOpen;
        private static string _feeCoin;

        /// <summary>储藏区里同一种货（标识、型号、真伪、成色都相同）聚成一组。</summary>
        private sealed class SellGroup
        {
            public string Key, Id, Model, FakeOf;
            public double Health;
            public long Ref;
            public GoodKind Kind;
            public readonly List<GameItem> Items = new List<GameItem>();
        }

        private static List<SellGroup> SellGroups()
        {
            var map = new Dictionary<string, SellGroup>();
            var list = new List<SellGroup>();
            foreach (var it in BlackDelivery.Sellable())
            {
                string id = BlackDelivery.SellId(it);
                if (id == null) continue;
                var kind = BlackDelivery.KindOf(id);
                var u = kind == GoodKind.Gpu ? Items.ToUnit(it) : null;
                string key = BmSale.KeyOf(id, u?.Model, u?.FakeOf, u?.Health ?? 0);
                if (!map.TryGetValue(key, out var g))
                {
                    g = new SellGroup { Key = key, Id = id, Model = u?.Model, FakeOf = u?.FakeOf, Health = u?.Health ?? 0, Ref = BlackDelivery.RefValue(it), Kind = kind };
                    map[key] = g; list.Add(g);
                }
                g.Items.Add(it);
            }
            return list;
        }

        private static double MultFor(string key) => Session.Data.Black.MultOf(key) ?? _sellMult;
        private static int Staged(string key) => _stage.TryGetValue(key, out int n) ? n : 0;

        /// <summary>这一组预备数量再加一件会不会超限：返回提示文字，可以加返回 null。</summary>
        private static string StageBlock(SellGroup g)
        {
            var d = Session.Data;
            int have = d.Black.ActiveOf(g.Key), staged = Staged(g.Key);
            if (have + staged + 1 > Tuning.BmMaxStack) return L.T("black.sell.limit.stack", Tuning.BmMaxStack);
            if (have == 0 && staged == 0)
            {
                int kinds = d.Black.ActiveKinds + _stage.Count(kv => kv.Value > 0 && d.Black.ActiveOf(kv.Key) == 0);
                if (kinds >= Tuning.BmMaxKinds) return L.T("black.sell.limit.kinds", Tuning.BmMaxKinds);
            }
            return null;
        }

        private static BmGood GoodOf(string id, GoodKind kind, string model, double health, int price) =>
            new BmGood { Item = id, Kind = kind, Level = kind == GoodKind.Gpu ? 2 : Merchants.Info(id).Level, Model = model, Health = health, Price = price };

        /// <summary>卖货：默认是已上架的橱窗；"上架更多"进入备货页，"货架"付平台费后上架。</summary>
        private static void SellPage(CustomUIBuilder b, int day)
        {
            if (_sellView == SellView.Add) { SellAddPage(b); return; }
            if (_sellView == SellView.Fee) { SellFeePage(b); return; }
            ShowcasePage(b);
        }

        private static void ShowcasePage(CustomUIBuilder b)
        {
            var d = Session.Data;
            var listed = d.Black.Sales.Where(x => x.State == SaleState.Listed).GroupBy(x => x.Key).ToList();
            b.AddLabel(L.T("black.sell.rating", d.Black.Rating) + "　" + L.T("black.sell.kinds", d.Black.ActiveKinds, Tuning.BmMaxKinds), "sell_status_dim");
            b.BeginRow(6f);
            b.AddButton(L.T("black.sell.add"), () => { _sellView = SellView.Add; _delist.Clear(); _message = null; Open(); });
            PledgerSkin.Pick(b, L.T("black.sell.coins"), _coinsOpen, () => { _coinsOpen = !_coinsOpen; Open(); }, _picked);
            b.End();
            if (_coinsOpen)
            {
                b.BeginRow(4f);
                foreach (var c in BlackMarket.SellCoins)
                {
                    string coin = c;
                    PledgerSkin.Pick(b, coin, d.Black.AcceptedCoins.Contains(coin), () =>
                    {
                        if (!d.Black.ToggleCoin(coin)) _message = L.T("black.sell.nocoin");
                        else { _message = null; Session.Save(); }
                        Open();
                    }, _picked);
                }
                b.End();
            }
            if (listed.Count == 0) { b.AddLabel(L.T("black.sell.empty"), "sell_none_dim"); return; }
            var cards = new Dictionary<BmGood, IGrouping<string, BmSale>>();
            Shelf(b, listed.Select(g =>
            {
                var s = g.First();
                var kind = BlackDelivery.KindOf(s.Item);
                var good = GoodOf(s.Item, kind, s.Model, s.Health, s.ListPrice);
                cards[good] = g;
                return good;
            }).ToList(), (c, good) =>
            {
                var g = cards[good];
                string key = g.Key; int count = g.Count();
                GoodHead(c, good, L.T("black.sell.have", count));
                c.AddLabel(L.T("black.price", good.Price));
                c.AddFlexibleSpace();
                int n = _delist.TryGetValue(key, out int cur) ? cur : 0;
                c.AddButton(n > 0 ? L.T("black.sell.off.n", n) : L.T("black.sell.off"), () => { _delist[key] = (n + 1) % (count + 1); _message = null; Open(); });
            });
        }

        private static void ConfirmDelist()
        {
            var d = Session.Data;
            int n = 0;
            foreach (var kv in _delist.Where(kv => kv.Value > 0).ToList())
                foreach (var s in d.Black.Sales.Where(x => x.State == SaleState.Listed && x.Key == kv.Key).Take(kv.Value).ToList())
                {
                    d.Black.Delist(s);
                    Items.Give(BlackDelivery.Make(s.Item, s.Model, s.FakeOf, s.Health));
                    n++;
                }
            _delist.Clear();
            _message = L.T("black.sell.delisted", n);
            Session.Save();
            Open();
        }

        private static void SellAddPage(CustomUIBuilder b)
        {
            var d = Session.Data;
            var groups = SellGroups();
            _stage.Keys.Where(k => groups.All(g => g.Key != k)).ToList().ForEach(k => _stage.Remove(k));
            foreach (var g in groups) if (Staged(g.Key) > g.Items.Count) _stage[g.Key] = g.Items.Count;
            b.BeginRow(8f, TextAnchor.UpperLeft);
            b.BeginPanel(SellerW + 8f, -1f, TextAnchor.UpperLeft, 0f, 2f);
            _clear.Add(b.Last.image);
            b.AddLabel(L.T("black.sell.mult"), "sell_mult_dim");
            foreach (var m in Tuning.BmSellMults)
            {
                double mult = m;
                TableRow(b, SellerW, new[] { SellerW - 8f }, new[] { TextAnchor.MiddleLeft },
                    new[] { "×" + m.ToString("0.0", CultureInfo.InvariantCulture) },
                    Math.Abs(mult - _sellMult) < 1e-9, () => { _sellMult = mult; Open(); });
            }
            b.AddSpace(4f);
            b.AddLabel(L.T("black.sell.rating", d.Black.Rating), "sell_status_dim");
            b.AddLabel(L.T("black.sell.kinds", d.Black.ActiveKinds, Tuning.BmMaxKinds), "sell_status_dim");
            b.AddSpace(4f);
            b.AddButton(L.T("black.sell.back"), () => { _sellView = SellView.Showcase; _stage.Clear(); _message = null; Open(); });
            b.End();
            b.BeginPanel(ShelfW, -1f, TextAnchor.UpperLeft, 0f, 4f);
            _clear.Add(b.Last.image);
            if (groups.Count == 0) b.AddLabel(L.T("black.sell.none"), "sell_none_dim");
            else
            {
                var cards = new Dictionary<BmGood, SellGroup>();
                Shelf(b, groups.Select(g =>
                {
                    var good = GoodOf(g.Id, g.Kind, g.Model, g.Health, BlackMarket.ListPriceOf(g.Ref, MultFor(g.Key)));
                    cards[good] = g;
                    return good;
                }).ToList(), (c, good) =>
                {
                    var g = cards[good];
                    GoodHead(c, good, "");
                    c.AddLabel(L.T("black.price", good.Price));
                    c.AddLabel(L.T("black.stock", g.Items.Count), "good_line2_dim");
                    c.AddFlexibleSpace();
                    c.BeginRow(4f, TextAnchor.MiddleCenter);
                    RowKey(c, "-", () => { if (Staged(g.Key) > 0) _stage[g.Key] = Staged(g.Key) - 1; _message = null; Open(); });
                    Cell(c, 34f, Staged(g.Key).ToString(), false, TextAnchor.MiddleCenter);
                    RowKey(c, "+", () =>
                    {
                        string block = Staged(g.Key) >= g.Items.Count ? null : StageBlock(g);
                        if (block != null) { _message = block; Open(); return; }
                        if (Staged(g.Key) < g.Items.Count) _stage[g.Key] = Staged(g.Key) + 1;
                        _message = null; Open();
                    });
                    c.End();
                });
            }
            b.End();
            b.End();
        }

        private static double StageFee(List<SellGroup> groups) =>
            groups.Sum(g => Staged(g.Key) * (double)BlackMarket.ListFee(BlackMarket.ListPriceOf(g.Ref, MultFor(g.Key))));

        /// <summary>货架：左栏是准备上架的清单，右栏选收款币付平台费，付完才上架。</summary>
        private static void SellFeePage(CustomUIBuilder b)
        {
            var d = Session.Data;
            var w = d.Hot;
            var groups = SellGroups().Where(g => Staged(g.Key) > 0).ToList();
            if (groups.Count == 0) { _sellView = SellView.Add; Open(); return; }
            double fee = StageFee(groups);
            var acc = Merchants.PlatformAccepts;
            if (_feeCoin == null || !acc.Contains(_feeCoin)) _feeCoin = acc[0];
            Columns(b, l =>
            {
                l.AddHeader(L.T("black.fee.h"));
                foreach (var g in groups)
                    l.AddLabel(L.T("black.fee.line", ItemName(g.Id), Staged(g.Key), BlackMarket.ListPriceOf(g.Ref, MultFor(g.Key)), "×" + MultFor(g.Key).ToString("0.0", CultureInfo.InvariantCulture)), "cart_shop");
                l.AddSeparator();
                l.AddLabel(L.T("black.fee.total", Fmt.Credits(fee)), "cart_total");
            }, r =>
            {
                r.AddHeader(L.T("black.fee.pay"));
                r.BeginRow(4f);
                foreach (var a in acc) { string asset = a; PledgerSkin.Pick(r, asset, asset == _feeCoin, () => { _feeCoin = asset; Open(); }, _picked); }
                r.End();
                r.AddLabel(L.T("bpay.need", WalletWindow.Amt(WalletWindow.RoundUp(fee / Session.Price(_feeCoin))), _feeCoin, Fmt.Num(w.Avail(_feeCoin))), "bpay_need_dim");
                r.AddButton(L.T("bpay.invoice"), () =>
                {
                    string coin = _feeCoin;
                    string summary = L.T("black.fee.sum", groups.Sum(g => Staged(g.Key)));
                    BeginPay(d.Address("platform"), summary, coin, fee, pw => ListStaged(coin, pw));
                });
                r.AddSpace(4f);
                r.AddButton(L.T("black.sell.back"), () => { _sellView = SellView.Add; _message = null; Open(); });
            });
        }

        /// <summary>付完平台费后：按预备数量逐件上架，货从储藏区拿走。</summary>
        private static void ListStaged(string asset, Wallet pw)
        {
            var d = Session.Data;
            int day = StoreStation.GetDayCounter(), ok = 0, fail = 0;
            foreach (var g in SellGroups())
            {
                int n = Staged(g.Key);
                if (n <= 0) continue;
                double mult = MultFor(g.Key);
                foreach (var it in g.Items.Take(n))
                {
                    int heat = it.IsTag("STOLEN_VALUE_INT") ? it.GetTagReadonly("STOLEN_VALUE_INT").GetInt() : 0;
                    var s = d.Black.List(g.Id, g.Model, g.FakeOf, g.Health, g.Ref, mult, pw, asset, day, Session.Price, heat);
                    if (s == null) { fail++; continue; }
                    it.Destroy();
                    ok++;
                }
            }
            _stage.Clear();
            _sellView = SellView.Showcase;
            _message = fail == 0 ? L.T("black.sell.listedall", ok) : L.T("black.sell.partial", ok, fail);
            Session.Save();
            Open();
        }

        // ——— 购物车 ———

        private sealed class CartLine { public int Seller; public BmGood Good; public int Qty; }
        private static readonly List<CartLine> _cart = new List<CartLine>();
        private static bool _checkout;
        private static readonly Dictionary<int, string> _ckSwap = new Dictionary<int, string>();
        private static readonly Dictionary<int, PayMode> _ckMode = new Dictionary<int, PayMode>();
        private static readonly Dictionary<int, string> _ckGroupCoin = new Dictionary<int, string>();

        /// <summary>读档或换局时清空购物车（货架对象已经不是同一批了）。</summary>
        public static void ClearCart() { _stage.Clear(); _delist.Clear(); _sellView = SellView.Showcase; _cart.Clear(); _checkout = false; _ckMode.Clear(); _ckGroupCoin.Clear(); _ckSwap.Clear(); }

        private static int StockOf(BmGood g) => g is BmOffer o ? o.Qty : ((BmListing)g).Qty;

        /// <summary>丢掉已经下架的货，数量压到库存以内。</summary>
        private static void CartValidate(SaveData d)
        {
            _cart.RemoveAll(l => l.Good is BmOffer o ? !d.Black.Offers.Contains(o) : !d.Black.Catalog.Contains((BmListing)l.Good) || l.Seller >= d.Black.Sellers.Count);
            foreach (var l in _cart) l.Qty = Math.Max(1, Math.Min(l.Qty, StockOf(l.Good)));
        }

        private static int CartCount() => _cart.Sum(l => l.Qty);

        private static void AddToCart(int seller, BmGood g)
        {
            var line = _cart.Find(l => l.Good == g);
            if (line == null) _cart.Add(new CartLine { Seller = seller, Good = g, Qty = 1 });
            else line.Qty = Math.Min(StockOf(g), line.Qty + 1);
            _message = L.T("cart.added", ItemName(g.Item));
            Open();
        }

        private static string[] AcceptsOf(SaveData d, int seller) => seller < 0 ? Merchants.PlatformAccepts : Merchants.Accepts(d.Black.Sellers[seller].Type);
        private static string AddressOf(SaveData d, int seller) => seller < 0 ? d.Address("platform") : d.Black.Sellers[seller].Name;
        private static string ShopName(SaveData d, int seller) => seller < 0 ? L.T("merchant.Platform") : MerchantLabel(d.Black.Sellers[seller], true);
        private static PayMode ModeOf(SaveData d, int seller) => seller >= 0 && d.Black.Sellers[seller].Advance && _ckMode.TryGetValue(seller, out var m) ? m : PayMode.Escrow;

        private static double LineCredits(SaveData d, CartLine l) =>
            l.Good is BmOffer o ? o.Price * l.Qty : BlackMarket.Pay(BlackMarket.OrderTotal(((BmListing)l.Good).Price, l.Qty), ModeOf(d, l.Seller));

        private static double GroupCredits(SaveData d, int seller) => _cart.Where(l => l.Seller == seller).Sum(l => LineCredits(d, l));

        /// <summary>悬浮购物车：商店页右下角一个常驻的小按钮，显示件数，点开进结算页。</summary>
        private sealed class Fab { public string Text; public bool On; public Action Click; }

        /// <summary>右下角悬浮按钮：买货页是购物车，卖货的备货页是货架，橱窗里有预备下架时是确定下架；其余页面没有。</summary>
        private static Fab FabNow()
        {
            if (_page != Page.Gray || _grayTab != GrayTab.Black || _checkout || WalletWindow.Pending != null || !Unlock.BlackUnlocked) return null;
            if (_blackTab == BlackTab.Buy) return new Fab { Text = L.T("cart.fab", CartCount()), On = CartCount() > 0, Click = () => { _checkout = true; _message = null; Open(); } };
            if (_blackTab != BlackTab.Sell) return null;
            if (_sellView == SellView.Add)
            {
                int n = _stage.Values.Sum();
                return new Fab { Text = L.T("shelf.fab", n), On = n > 0, Click = () => { if (n > 0) { _sellView = SellView.Fee; _message = null; Open(); } } };
            }
            if (_sellView == SellView.Showcase)
            {
                int n = _delist.Values.Sum();
                return n > 0 ? new Fab { Text = L.T("delist.fab", n), On = true, Click = ConfirmDelist } : null;
            }
            return null;
        }

        private static void CartFab(CustomUIWindow win)
        {
            if (win?.root == null) return;
            var old = win.root.transform.Find("pd_cart_fab");
            var fab = FabNow();
            if (fab == null) { if (old != null) UnityEngine.Object.Destroy(old.gameObject); return; }
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("pd_cart_fab", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(win.root.transform, false);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(Mathf.Max(132f, fab.Text.Length * 9f + 40f), 36f);
            rt.anchoredPosition = new Vector2(-34f, 34f);
            var img = go.GetComponent<Image>();
            PledgerSkin.KeyShape(img, fab.On ? PledgerSkin.Ink : PledgerSkin.Key);
            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => fab.Click());
            var tgo = new GameObject("label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            tgo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)tgo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            var tmp = tgo.GetComponent<TextMeshProUGUI>();
            if (win.frame?.titleText != null) { tmp.font = win.frame.titleText.font; tmp.fontSharedMaterial = win.frame.titleText.fontSharedMaterial; }
            tmp.text = fab.Text;
            tmp.fontSize = 15f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = PledgerSkin.KeyText;
            tmp.raycastTarget = false;
            go.transform.SetAsLastSibling();
        }

        /// <summary>右栏最前面：商家信息（成交、好评、到货天数、收的币、是否接受提前放款）。</summary>
        private static void ShopInfo(CustomUIBuilder b, SaveData d, BmSeller seller)
        {
            if (seller == null)
            {
                b.AddHeader(L.T("merchant.Platform"));
                b.AddLabel(L.T("black.platform.line"), "shop_info_dim");
                b.AddLabel(L.T("black.info.accepts", string.Join(" / ", Merchants.PlatformAccepts)), "shop_accepts_dim");
            }
            else
            {
                b.AddHeader(MerchantLabel(seller));
                b.AddLabel(L.T("black.seller", "", seller.Trades, seller.Rating) + "　" + L.T("black.days", seller.Days), "shop_info_dim");
                b.AddLabel(L.T("black.info.accepts", string.Join(" / ", Merchants.Accepts(seller.Type))) + "　" + L.T(seller.Advance ? "black.info.advance.yes" : "black.info.advance.no"), "shop_accepts_dim");
            }
            b.AddSeparator();
        }

        /// <summary>结算页：左栏是购物车详情（按商家分组，可增减数量、删除），右栏是付款：每家商家收款地址不同，所以逐家付款。</summary>
        private static void CheckoutPage(CustomUIBuilder b, SaveData d)
        {
            CartValidate(d);
            var w = d.Hot;
            var groups = _cart.GroupBy(l => l.Seller).OrderBy(g => g.Key).ToList();
            Columns(b, l =>
            {
                l.AddHeader(L.T("cart.h"));
                if (_cart.Count == 0) { l.AddLabel(L.T("cart.empty"), "cart_empty_dim"); return; }
                foreach (var g in groups)
                {
                    int seller = g.Key;
                    l.AddLabel("<b>" + ShopName(d, seller) + "</b>", "cart_shop");
                    if (seller >= 0 && d.Black.Sellers[seller].Advance)
                    {
                        l.BeginRow(4f);
                        foreach (PayMode m in Enum.GetValues(typeof(PayMode)))
                        {
                            var mode = m;
                            PledgerSkin.Pick(l, L.T("black.mode." + m), ModeOf(d, seller) == mode, () => { _ckMode[seller] = mode; Open(); }, _picked);
                        }
                        l.End();
                    }
                    foreach (var line in g.ToList())
                    {
                        var ln = line;
                        l.BeginRow(4f, TextAnchor.MiddleLeft);
                        Cell(l, 150f, ItemName(ln.Good.Item), false, TextAnchor.MiddleLeft);
                        RowKey(l, "-", () => { ln.Qty--; if (ln.Qty < 1) _cart.Remove(ln); Open(); });
                        Cell(l, 26f, ln.Qty.ToString(), false, TextAnchor.MiddleCenter);
                        RowKey(l, "+", () => { ln.Qty = Math.Min(StockOf(ln.Good), ln.Qty + 1); Open(); });
                        Cell(l, 58f, Fmt.Credits(LineCredits(d, ln)), false, TextAnchor.MiddleRight);
                        RowKey(l, "×", () => { _cart.Remove(ln); Open(); });
                        l.End();
                    }
                }
                l.AddSeparator();
                l.AddLabel(L.T("bpay.total", Fmt.Credits(_cart.Sum(x => LineCredits(d, x)))), "cart_total");
            }, r =>
            {
                r.AddHeader(L.T("bpay.h"));
                if (_cart.Count == 0) { r.AddButton(L.T("cart.back"), () => { _checkout = false; Open(); }); return; }
                foreach (var g in groups) GroupPay(r, d, w, g.Key);
                r.AddSpace(4f);
                r.AddButton(L.T("cart.back"), () => { _checkout = false; _message = null; Open(); });
            });
        }

        /// <summary>逐家付款：这家商家收的币里选一种，生成账单在钱包里转账；也可以用钱包里别的币自动换币（多付滑点）一次签名付清。</summary>
        private static void GroupPay(CustomUIBuilder r, SaveData d, Wallet w, int seller)
        {
            var acc = AcceptsOf(d, seller);
            if (!_ckGroupCoin.TryGetValue(seller, out var coin) || !acc.Contains(coin)) coin = _ckGroupCoin[seller] = acc[0];
            double credits = GroupCredits(d, seller);
            r.AddSeparator();
            r.AddLabel(ShopName(d, seller) + "　" + Fmt.Credits(credits), "cart_shop");
            r.BeginRow(4f);
            foreach (var a in acc) { string asset = a; PledgerSkin.Pick(r, asset, asset == coin, () => { _ckGroupCoin[seller] = asset; Open(); }, _picked); }
            r.End();
            r.AddLabel(L.T("bpay.need", WalletWindow.Amt(WalletWindow.RoundUp(credits / Session.Price(coin))), coin, Fmt.Num(w.Avail(coin))), "bpay_need_dim");
            r.AddButton(L.T("bpay.invoice"), () =>
            {
                var lines = _cart.Where(l => l.Seller == seller).ToList();
                var mode = ModeOf(d, seller);
                string summary = ShopName(d, seller) + " ×" + lines.Sum(l => l.Qty);
                _checkout = false;
                BeginPay(AddressOf(d, seller), summary, coin, credits, pw => { var r2 = ExecGroup(seller, lines, mode, coin, pw); Finish(CartMessage(r2.items, r2.failed)); });
            });
            double slipped = credits * (1 + Tuning.BmAutoSwapSlip);
            var sources = w.Balances.Keys.Where(a => !acc.Contains(a) && a != Assets.Ponero && Session.Price(a) > 0 && w.Avail(a) * Session.Price(a) >= slipped).ToList();
            if (sources.Count == 0) return;
            if (!_ckSwap.TryGetValue(seller, out var src) || !sources.Contains(src)) src = _ckSwap[seller] = sources[0];
            r.AddLabel(L.T("bpay.swap.h", coin), "bpay_swap_dim");
            r.BeginRow(4f);
            foreach (var s in sources) { string sc = s; PledgerSkin.Pick(r, sc, sc == src, () => { _ckSwap[seller] = sc; Open(); }, _picked); }
            r.End();
            double needSrc = WalletWindow.RoundUp(slipped / Session.Price(src));
            r.AddLabel(L.T("bpay.swap.need", WalletWindow.Amt(needSrc), src, (Tuning.BmAutoSwapSlip * 100).ToString("0", CultureInfo.InvariantCulture)), "bpay_swap_need_dim");
            r.AddButton(L.T("bpay.swap.btn"), () => SwapGroup(seller, coin, src, credits, needSrc));
        }

        /// <summary>自动换币并结算一家商家：一次合约调用，先把别的币按滑点换成这家收的币，再下单。</summary>
        private static void SwapGroup(int seller, string coin, string src, double credits, double needSrc)
        {
            var d = Session.Data;
            var lines = _cart.Where(l => l.Seller == seller).ToList();
            var mode = ModeOf(d, seller);
            string summary = ShopName(d, seller) + " ×" + lines.Sum(l => l.Qty);
            _checkout = false;
            _message = L.T("hw.wait");
            Open();
            WalletWindow.OpenForBill(new WalletWindow.Bill
            {
                Contract = true, Asset = src, Amount = needSrc, Spend = needSrc,
                Summary = L.T("bpay.swap.sum", summary, src, WalletWindow.Amt(needSrc), coin),
                Execute = pw =>
                {
                    if (pw.Avail(src) + 1e-9 < needSrc) { Finish(L.T("msg.balance")); return; }
                    double got = credits / Session.Price(coin) * 1.002;
                    pw.Convert(src, needSrc, coin, got); pw.RecordBuy(coin, got, credits);
                    CandleChart.Trade(src, credits); CandleChart.Trade(coin, credits);
                    Log.Info($"自动换币：{needSrc:0.####} {src} → {got:0.####} {coin}（{credits:0} 信用点等值）");
                    // 一次签名两步：经 DEX 换币，再付给这家商家；分别记账
                    d.Chain(pw.Address, d.Address("dex"), src, needSrc);
                    d.Chain(d.Address("dex"), pw.Address, coin, got);
                    var paying = SaveData.Snapshot(pw);
                    var r2 = ExecGroup(seller, lines, mode, coin, pw);
                    d.ChainDiff(pw, paying, AddressOf(d, seller));
                    Finish(CartMessage(r2.items, r2.failed));
                },
            });
        }

        private static void Finish(string message)
        {
            _message = message;
            Session.Save();
            Open();
        }

        /// <summary>结算一家商家的购物车行：平台现货直接买、商家下订单，成功的行从购物车里拿掉。返回结果消息。</summary>
        private static (int items, int failed) ExecGroup(int seller, List<CartLine> lines, PayMode mode, string asset, Wallet pw)
        {
            var d = Session.Data;
            int items = 0, failed = 0;
            foreach (var line in lines)
            {
                if (line.Good is BmOffer o)
                {
                    int n = BuyOfferCore(o, line.Qty, asset, pw);
                    items += n;
                    if (n < line.Qty) failed += line.Qty - n;
                }
                else
                {
                    var ord = d.Black.Place((BmListing)line.Good, line.Qty, mode, pw, asset, StoreStation.GetDayCounter(), Session.Price);
                    if (ord == null) failed += line.Qty; else items += ord.Count;
                }
                _cart.Remove(line);
            }
            return (items, failed);
        }

        private static string CartMessage(int items, int failed) => failed == 0 ? L.T("cart.done", items) : L.T("cart.partial", items, failed);

        private static string MerchantLabel(BmSeller s, bool shortName = false) => L.T("merchant." + s.Type) + " " + (shortName ? "·" + s.Name.Substring(Math.Max(0, s.Name.Length - 4)) : s.Name);

        private static int _merchant = -1;
        private static readonly int[] Qtys = { 1, 5, 10 };

        /// <summary>商家货架：这家的成交数、好评和到货天数，下面是这周的货；点"购买"进付款面板。</summary>
        private static void MerchantShelf(CustomUIBuilder b, SaveData d)
        {
            var sellers = d.Black.Sellers;
            var seller = sellers[_merchant];
            int merchant = _merchant;
            var goods = d.Black.Catalog.Where(l => l.Seller == merchant).Cast<BmGood>().ToList();
            if (goods.Count == 0) { b.AddLabel(L.T("black.empty"), "shelf_empty_dim"); return; }
            Shelf(b, goods, (c, g) =>
            {
                var listing = (BmListing)g;
                GoodHead(c, g, L.T("black.stock", listing.Qty));
                c.AddLabel(L.T("black.price", BlackMarket.Pay(BlackMarket.OrderTotal(listing.Price, 1), PayMode.Escrow)));
                c.AddFlexibleSpace();
                c.AddButton(L.T("black.buy"), () => AddToCart(merchant, listing));
            }, 3);
        }

        private static int ShelfCols => Ext < 100f ? 3 : Math.Max(3, (int)Math.Round(ShelfW / (172f * F)));
        private static float CardW => Ext < 100f ? 172f : (ShelfW - 6f * (ShelfCols - 1)) / ShelfCols;
        private static float CardH => 268f * F;

        /// <summary>货架：一行 4 张商品卡。</summary>
        private static void Shelf(CustomUIBuilder b, System.Collections.Generic.List<BmGood> goods, Action<CustomUIBuilder, BmGood> card, int cols = -1)
        {
            if (cols <= 0) cols = ShelfCols;
            for (int i = 0; i < goods.Count; i += cols)
            {
                b.BeginRow(6f);
                for (int k = i; k < Math.Min(goods.Count, i + cols); k++)
                {
                    var g = goods[k];
                    b.BeginPanel(CardW, CardH, TextAnchor.UpperCenter, 6f, 3f);
                    if (b.Last != null && b.Last.image != null)
                    {
                        b.Last.image.sprite = MachineryStyle.White;
                        b.Last.image.color = PledgerSkin.RowNormal;
                    }
                    card(b, g);
                    b.End();
                }
                b.End();
            }
        }

        private static void GoodHead(CustomUIBuilder c, BmGood g, string line2)
        {
            var sprite = Icon(g.Item);
            if (sprite != null) c.AddImage(sprite, 64f, 64f); else c.AddSpace(64f);
            c.AddLabel("<b>" + ItemName(g.Item) + "</b>");
            PledgerSkin.FitBox(c, 52f * F);
            bool asic = g.Kind == GoodKind.Gpu && g.Model != null && Hardware.Specs.TryGetValue(g.Model, out var gs) && gs.Asic;
            c.AddLabel(asic ? (g.Health >= 100 ? L.T("black.kind.asicnew") : L.T("black.kind.asic", (int)Math.Round(g.Health))) : g.Kind == GoodKind.Gpu ? (g.Health >= 100 ? L.T("black.kind.gpunew") : L.T("black.kind.gpu", (int)Math.Round(g.Health))) : L.T("black.kind." + g.Kind + (g.Kind == GoodKind.Contraband ? "." + g.Level : "")), "good_kind_dim");
            if (!string.IsNullOrEmpty(line2)) c.AddLabel(line2, "good_line2_dim");
        }

        private static string ItemName(string id) => BlackDelivery.ItemName(id);

        /// <summary>开账单：收款地址、币种、金额（按信用点等值折成币），付款在硬件钱包里做。</summary>
        private static void BeginPay(string address, string summary, string asset, double credits, Action<Wallet> execute) =>
            BillFor(address, summary, asset, credits / Session.Price(asset), execute);

        private static void BillFor(string address, string summary, string asset, double amount, Action<Wallet> execute)
        {
            WalletWindow.Pending = new WalletWindow.Bill { Address = address, Summary = summary, Asset = asset, Amount = WalletWindow.RoundUp(amount), Execute = execute };
            _message = null;
            Open();
        }

        /// <summary>待付账单：收款地址和金额，去钱包里转账；也可以作废。</summary>
        private static void PayPage(CustomUIBuilder b)
        {
            var p = WalletWindow.Pending;
            b.AddHeader(L.T("black.pay.confirm"));
            b.AddLabel(p.Summary);
            b.BeginRow(8f, TextAnchor.MiddleLeft);
            b.AddLabel(L.T("black.pay.to", p.Address), "pay_to_dim");
            b.AddButton(L.T("hw.copy"), () => { Clipboard.Copy(p.Address); _message = L.T("hw.copied.addr", p.Address); Open(); });
            b.End();
            b.AddLabel(L.T("black.pay.amount", WalletWindow.Amt(p.Amount), p.Asset, Fmt.Credits(p.Amount * Session.Price(p.Asset))));
            b.BeginRow(6f);
            b.AddButton(L.T("black.pay.wallet"), () => WalletWindow.OpenForBill(p));
            b.AddButton(L.T("black.pay.cancel"), () => { if (OtcClient.IsBill(p)) OtcClient.Void(); else WalletWindow.Pending = null; _message = null; Open(); });
            b.End();
        }

        private static void BuyOffer(BmOffer o, int count, string asset, Wallet pw)
        {
            int bought = BuyOfferCore(o, count, asset, pw);
            if (bought == 0) { _message = L.T("msg.balance"); Open(); return; }
            _message = L.T("black.bought", ItemName(o.Item) + (bought > 1 ? " ×" + bought : ""));
            Session.Save();
            Open();
        }

        /// <summary>平台现货买 count 件：每件从钱包扣币，货进储藏区。返回实际买到的件数。</summary>
        private static int BuyOfferCore(BmOffer o, int count, string asset, Wallet pw)
        {
            var d = Session.Data;
            string item = o.Item, model = o.Model, fake = o.FakeOf;
            double health = o.Health;
            int bought = 0;
            for (int i = 0; i < count; i++)
            {
                if (!d.Black.Buy(o, pw, asset, Session.Price)) break;
                Items.Give(BlackDelivery.Make(item, model, fake, health));
                bought++;
            }
            return bought;
        }

        private static void PlaceOrder(BmListing l, int count, string asset, PayMode mode, Wallet pw)
        {
            var d = Session.Data;
            var o = d.Black.Place(l, count, mode, pw, asset, StoreStation.GetDayCounter(), Session.Price);
            if (o == null) { _message = L.T("msg.balance"); Open(); return; }
            _message = L.T("black.ordered", ItemName(o.Item) + (o.Count > 1 ? " ×" + o.Count : ""), o.DueDay);
            Session.Save();
            Open();
        }

        /// <summary>在途订单：买入还没取货的订单，和卖出已成交、货款在托管中的订单；每条显示状态与到期日。</summary>
        private static void OrdersPage(CustomUIBuilder b, int day)
        {
            var d = Session.Data;
            var buys = d.Black.Orders.Where(o => o.State == OrderState.Pending || o.State == OrderState.Ready).OrderByDescending(x => x.PlacedDay).ToList();
            var sells = d.Black.Sales.Where(x => x.State == SaleState.Sold).OrderByDescending(x => x.ListedDay).ToList();
            b.BeginRow(8f, TextAnchor.UpperLeft);
            b.BeginPanel((SellerW + 8f + ShelfW) * 0.5f, -1f, TextAnchor.UpperLeft, 4f, 4f);
            _clear.Add(b.Last.image);
            b.AddHeader(L.T("black.orders.in"));
            if (buys.Count == 0) b.AddLabel(L.T("black.mine.none"), "orders_in_none_dim");
            foreach (var o in buys)
                b.AddLabel(L.T("black.mine.line", ItemName(o.Item) + (o.Count > 1 ? " ×" + o.Count : ""), BlackDelivery.DisplayName(o.Seller), L.T("black.mode." + o.Mode), L.T("black.state." + o.State), o.DueDay));
            b.End();
            b.BeginPanel((SellerW + 8f + ShelfW) * 0.5f, -1f, TextAnchor.UpperLeft, 4f, 4f);
            _clear.Add(b.Last.image);
            b.AddHeader(L.T("black.orders.out"));
            if (sells.Count == 0) b.AddLabel(L.T("black.mine.none"), "orders_out_none_dim");
            foreach (var sale in sells)
                b.AddLabel(L.T("black.mine.sale", ItemName(sale.Item), sale.Ref, sale.ListPrice, sale.Mult.ToString("0.00", CultureInfo.InvariantCulture), L.T("black.sale.Sold", sale.PayDay, sale.PayAsset ?? Assets.CRT)));
            b.End();
            b.End();
        }

        /// <summary>Ponero 兑换处的账单：和 DEX 互换共用一个界面，但走收款地址 + 手输签名的流程。</summary>
        private static void DeskBill(double v, string from, string to)
        {
            var d = Session.Data;
            _web3Tab = Web3Tab.Trade;
            BillFor(d.Address("desk"), L.T("pay.swap", Fmt.Num(v), from), from, v, pw => DeskSwap(v, from, to, pw));
        }

        private static void DeskSwap(double v, string from, string to, Wallet pw)
        {
            var d = Session.Data;
            int day = StoreStation.GetDayCounter();
            bool into = to == Assets.Ponero;
            double got = into ? d.Black.ToPonero(pw, from, v, day, a => Session.DeskPrice(a, true)) : d.Black.FromPonero(pw, to, v, day, a => Session.DeskPrice(a, false));
            if (got < 0) { _message = L.T("black.desk.fail"); Open(); return; }
            double cr = v * Session.Price(from);
            CandleChart.Trade(Assets.Ponero, cr); CandleChart.Trade(into ? from : to, cr);
            _message = L.T("msg.trade", Fmt.Num(v), from, Fmt.Num(got), to);
            Session.Save();
            Open();
        }

        private enum Web3Tab { Trade, Stake, Airdrop, Predict, Ponzi }
        private static Web3Tab _web3Tab = Web3Tab.Trade;
        private static string _mixAsset = Assets.PSC;
        private static int _mixRounds = 2;
        private static readonly float[] MixWBase = new[] { 70f, 100f, 100f, 80f };
        private static float[] MixW => Sc(MixWBase);
        private static readonly TextAnchor[] MixAligns = { TextAnchor.MiddleLeft, TextAnchor.MiddleRight, TextAnchor.MiddleRight, TextAnchor.MiddleRight };

        /// <summary>混币器：把币存进去，几轮之后提回同一个地址，污点降低，每轮扣手续费。</summary>
        private static void MixerPage(CustomUIBuilder b)
        {
            var d = Session.Data;
            var w = d.Hot;
            int day = StoreStation.GetDayCounter();
            Columns(b, l =>
            {
                l.AddHeader(d.Story.MixerRebranded ? L.T("mixer.brand") : L.T("mixer.h"));
                l.AddLabel(d.Story.MixerClosed ? L.T("mixer.status.closed") : d.Mixer.Sanctioned(day) ? L.T("mixer.sanctioned", d.Mixer.SanctionedUntil - day + 1) : L.T("mixer.ok"), d.Story.MixerClosed || d.Mixer.Sanctioned(day) ? "mixer_status_note" : "mixer_status_dim");
                if (w.Taint(Session.Price) > 0.5) l.AddLabel(L.T("wallet.taint", Fmt.Credits(w.Taint(Session.Price))), "taint_note");
                l.AddSeparator();
                l.AddHeader(L.T("mixer.pending"));
                if (d.Mixer.Pending.Count == 0) l.AddLabel(L.T("mixer.none"), "mixer_none_dim");
                else
                {
                    TableHeader(l, MixW.Sum() + 8f, MixW, MixAligns, L.T("col.asset"), L.T("col.amount"), L.T("mixer.col.taint"), L.T("mixer.col.ready"));
                    foreach (var p in d.Mixer.Pending)
                        TableRow(l, MixW.Sum() + 8f, MixW, MixAligns, new[] { p.Asset, Fmt.Num(p.Amount), p.Dirty > 1e-9 ? Fmt.Num(p.Dirty) : "—", L.T("mixer.day", p.ReadyDay) });
                }
            }, r =>
            {
                r.AddHeader(L.T("mixer.deposit"));
                if (d.Story.MixerClosed) { r.AddLabel(L.T("mixer.closed"), "mixer_closed_note"); return; }
                r.BeginRow(4f);
                foreach (var a in new[] { Assets.PSC, Assets.CRC, Assets.CRT, Assets.CRA })
                {
                    var asset = a;
                    PledgerSkin.Pick(r, asset, asset == _mixAsset, () => { _mixAsset = asset; Open(); }, _picked);
                }
                r.End();
                r.AddLabel(L.T("mixer.balance", _mixAsset, Fmt.Num(w.Avail(_mixAsset))));
                AmountRow(r);
                r.AddLabel(L.T("mixer.rounds"));
                r.BeginRow(4f);
                for (int n = 1; n <= Tuning.MixerMaxRounds; n++)
                {
                    int rounds = n;
                    PledgerSkin.Pick(r, rounds.ToString(), rounds == _mixRounds, () => { _mixRounds = rounds; Open(); }, _picked);
                }
                r.End();
                if (double.TryParse((_amount ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0)
                {
                    double rate = Mixer.RoundRate(v * Session.Price(_mixAsset), day);
                    r.AddLabel(L.T("mixer.preview", Fmt.Num(v * Math.Pow(1 - Mixer.Fee, _mixRounds)), _mixAsset, (int)Math.Round((1 - Math.Pow(1 - rate, _mixRounds)) * 100), day + _mixRounds), "mixer_preview_dim");
                }
                r.AddButton(L.T("mixer.go"), () =>
                {
                    if (!ReadAmount(out double mv)) return;
                    string asset = _mixAsset; int rounds = _mixRounds;
                    BillFor(d.Address("mixer"), L.T("pay.mix", Fmt.Num(mv), asset), asset, mv, pw => MixDeposit(mv, asset, rounds, pw));
                });
            });
        }

        private static void MixDeposit(double v, string asset, int rounds, Wallet pw)
        {
            var d = Session.Data;
            int day = StoreStation.GetDayCounter();
            if (pw.Avail(asset) + 1e-9 < v) { _message = L.T("msg.balance"); Open(); return; }
            var dep = d.Mixer.Deposit(pw, asset, v, rounds, day, Session.Price);
            _message = dep == null ? L.T("mixer.min", Tuning.MixerMin) : L.T("mixer.done", Fmt.Num(v), asset, dep.ReadyDay);
            Session.Save();
            Open();
        }

        private static readonly float[] StakeWBase = new[] { 110f, 90f, 200f };
        private static float[] StakeW => Sc(StakeWBase);
        private static readonly TextAnchor[] StakeAligns = { TextAnchor.MiddleLeft, TextAnchor.MiddleRight, TextAnchor.MiddleRight };

        /// <summary>质押页：革命军 CRA 质押池（日息、解锁排队）和平衡代币 SPARK 的铸销。都是合约调用，要在钱包上签名。</summary>
        private static void StakePage(CustomUIBuilder b)
        {
            var d = Session.Data;
            var st = d.Stable;
            var w = d.Hot;
            int day = StoreStation.GetDayCounter();
            Columns(b, l =>
            {
                l.AddHeader(L.T("stake.h"));
                l.AddLabel(L.T("stake.rate", (st.CraRate * 100).ToString("0.0", CultureInfo.InvariantCulture), Tuning.CraUnlockDays) + (st.CraSubsidyLow ? "　" + L.T("stake.low") : ""), st.CraSubsidyLow ? "stake_low_note" : "stake_rate_dim");
                l.AddLabel(L.T("stake.cra", Fmt.Num(st.Cra), StableStatus(st, Assets.CRA, day)));
                l.AddSeparator();
                if (d.Staking.Lots.Count == 0) l.AddLabel(L.T("stake.none"), "stake_none_dim");
                else
                {
                    TableHeader(l, StakeW.Sum() + 8f, StakeW, StakeAligns, L.T("stake.col.wallet"), L.T("stake.col.amount"), L.T("stake.col.queue"));
                    foreach (var lot in d.Staking.Lots)
                    {
                        var ow = d.Own(lot.Wallet);
                        string queue = lot.Unlocking.Count == 0 ? "—" : string.Join(" ", lot.Unlocking.Select(u => Fmt.Num(u.Value) + L.T("stake.day", u.Key)));
                        TableRow(l, StakeW.Sum() + 8f, StakeW, StakeAligns,
                            new[] { ow != null ? WalletWindow.NameOf(ow) : lot.Wallet, Fmt.Num(lot.Amount), queue });
                    }
                }
                l.AddSeparator();
                l.AddLabel(L.T("mint.reserve", (st.Ratio * 100).ToString("0", CultureInfo.InvariantCulture), (st.C * 100).ToString("0", CultureInfo.InvariantCulture)), "mint_reserve_dim");
                l.AddLabel(L.T("spark.price", Fmt.Num(st.Spark)));
                var mine = st.Orders.Where(o => d.Own(o.Wallet) != null).ToList();
                if (mine.Count > 0)
                {
                    l.AddSeparator();
                    l.AddLabel(L.T("mint.queue"));
                    foreach (var o in mine) l.AddLabel(L.T("mint.order", L.T(o.Mint ? "cra.mint" : "cra.burn"), Fmt.Num(o.Cra), o.Due), "mint_order_dim");
                }
            }, r =>
            {
                r.AddHeader(L.T("stake.ops", WalletWindow.NameOf(w)));
                r.AddLabel(L.T("stake.avail", Fmt.Num(w.Avail(Assets.CRA)), Fmt.Num(d.Staking.Of(w.Address)?.Amount ?? 0)), "stake_avail_dim");
                r.AddLabel(L.T("stake.avail2", Fmt.Num(w.Avail(Assets.SPARK))), "stake_avail2_dim");
                AmountRow(r);
                r.AddSpace(4f);
                r.AddLabel(L.T("stake.coin"));
                r.BeginRow(4f);
                foreach (var c in StakeCoins)
                {
                    var coin = c;
                    PledgerSkin.Pick(r, coin, coin == _stakeCoin, () => { _stakeCoin = coin; Open(); }, _picked);
                }
                r.End();
                r.BeginRow(6f);
                r.AddButton(L.T("stake.do"), () => StakeCall(true));
                r.AddButton(L.T("stake.undo"), () => StakeCall(false));
                r.End();
                r.AddSeparator();
                r.AddHeader(L.T("mint.h"));
                if (double.TryParse((_amount ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double mv) && mv > 0)
                {
                    st.Collateral(mv, out double needCrc, out double needSpark);
                    r.AddLabel(L.T("mint.need", Fmt.Num(needCrc), Fmt.Num(needSpark)), "mint_need_dim");
                }
                r.BeginRow(6f);
                r.AddButton(L.T("cra.mint"), () => MintCall(true));
                r.AddButton(L.T("cra.burn"), () => MintCall(false));
                r.End();
            });
        }

        private static readonly string[] StakeCoins = { Assets.CRA, Assets.CRC, Assets.CRT, Assets.SPARK };
        private static string _stakeCoin = Assets.CRA;

        /// <summary>手续费用钱包里价值最高的那种币付。</summary>
        private static string BestFeeAsset(Wallet w) =>
            w.Balances.Where(k => k.Value > 1e-9 && Session.Price(k.Key) > 0).OrderByDescending(k => k.Value * Session.Price(k.Key)).Select(k => k.Key).FirstOrDefault() ?? Assets.PSC;

        private static void StakeCall(bool into)
        {
            if (!ReadAmount(out double v)) return;
            var d = Session.Data;
            var w = d.Hot;
            string coin = into ? _stakeCoin : Assets.CRA;
            if (into ? w.Avail(coin) + 1e-9 < v : (d.Staking.Of(w.Address)?.Amount ?? 0) + 1e-9 < v) { _message = L.T("msg.balance"); Open(); return; }
            var q = into && coin != Assets.CRA ? d.Market.Quote(coin, Assets.CRA, v) : null;
            _message = L.T("hw.wait");
            Open();
            WalletWindow.OpenForBill(new WalletWindow.Bill
            {
                Contract = true, Party = Session.Data.Address("staking"), Asset = coin, Amount = v, Spend = into ? v : 0, FeeAsset = into ? null : BestFeeAsset(w),
                Summary = into && q != null ? L.T("stake.sum.via", Fmt.Num(v), coin, Fmt.Num(q.Out)) : L.T(into ? "stake.sum.in" : "stake.sum.out", Fmt.Num(v), Tuning.CraUnlockDays),
                Execute = pw =>
                {
                    int day = StoreStation.GetDayCounter();
                    double cra = v;
                    if (into && coin != Assets.CRA)
                    {
                        if (pw.Avail(coin) + 1e-9 < v) { _message = L.T("msg.balance"); Open(); return; }
                        double spent = v * Session.Price(coin);
                        cra = d.Market.Swap(coin, Assets.CRA, v);
                        pw.Convert(coin, v, Assets.CRA, cra); pw.RecordBuy(Assets.CRA, cra, spent);
                        CandleChart.Trade(coin, spent); CandleChart.Trade(Assets.CRA, spent);
                    }
                    bool ok = into ? d.Staking.Stake(pw, cra) : d.Staking.RequestUnstake(pw.Address, v, day);
                    _message = ok ? L.T(into ? "stake.done.in" : "stake.done.out", Fmt.Num(cra), day + Tuning.CraUnlockDays) : L.T("msg.balance");
                    Session.Save();
                    Open();
                },
            });
        }

        /// <summary>官方一级市场：存入 CRC 和 SPARK 铸造 CRA（铸出后直接进质押），或销毁 CRA 换回两者。排队等待，官方可能不兑现。</summary>
        private static void MintCall(bool mint)
        {
            if (!ReadAmount(out double v)) return;
            var d = Session.Data;
            var w = d.Hot;
            var st = d.Stable;
            st.Collateral(v, out double needCrc, out double needSpark);
            bool enough = mint ? w.Avail(Assets.CRC) + 1e-9 >= needCrc && w.Avail(Assets.SPARK) + 1e-9 >= needSpark : w.Avail(Assets.CRA) + 1e-9 >= v;
            if (!enough) { _message = L.T("msg.balance"); Open(); return; }
            int minD = Tuning.CraMintDaysMin, maxD = Tuning.CraMintDaysMax;
            _message = L.T("hw.wait");
            Open();
            WalletWindow.OpenForBill(new WalletWindow.Bill
            {
                Contract = true, Party = Session.Data.Address("cra"), Asset = mint ? Assets.CRC : Assets.CRA, Amount = mint ? needCrc : v, Spend = mint ? needCrc : v,
                Summary = mint ? L.T("mint.sum.in", Fmt.Num(v), Fmt.Num(needCrc), Fmt.Num(needSpark), minD, maxD) : L.T("mint.sum.out", Fmt.Num(v), Tuning.CraUnlockDays),
                Execute = pw =>
                {
                    int day = StoreStation.GetDayCounter();
                    var o = mint ? st.RequestMint(pw, v, day, d.Rng) : st.RequestBurn(pw, v, day);
                    _message = o == null ? L.T("msg.balance") : L.T(mint ? "mint.done.in" : "mint.done.out", o.Due);
                    Session.Save();
                    Open();
                },
            });
        }

        private static bool frozenPay(Wallet w, string asset) => w.Frozen.ContainsKey(asset);

        private static string AvgText(Wallet w, string asset) => w.AvgBuy(asset) is double a ? Fmt.Num(a) : L.T("avg.none");

        private static void ReportPage(CustomUIBuilder b)
        {
            var r = Session.Data.LastNight;
            if (r == null) { b.AddLabel(L.T("report.none"), "report_none_dim"); return; }
            b.AddHeader(L.T("report.day", r.Day));
            b.AddLabel(L.T("report.rigs", r.PoweredRigs, r.Rigs), "report_dim");
            b.AddLabel(L.T("report.hash", Fmt.Num(r.Hash * 10), Fmt.Num(r.NetworkHash * 10)), "report_dim");
            b.AddLabel(L.T("report.mined", Fmt.Num(r.Mined), Fmt.Credits(r.Mined * r.Price)));
            b.AddLabel(L.T("report.energy", Fmt.Num(r.Kj)), "report_dim");
            b.AddLabel(L.T("report.burnt", r.Burnt.Count), r.Burnt.Count > 0 ? "report_burnt_note" : "report_burnt_dim");
            foreach (var n in r.Burnt) b.AddLabel("  " + n, "report_burnt_note");
            b.AddLabel(L.T("report.price", Fmt.Num(r.Price)), "report_dim");
        }

        private static bool ReadAmount(out double v)
        {
            if (!double.TryParse((_amount ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v <= 0)
            {
                _message = L.T("msg.amount");
                Open();
                return false;
            }
            return true;
        }

        private static void Deposit()
        {
            if (!ReadAmount(out double v)) return;
            int credits = (int)Math.Floor(v);
            if (credits <= 0 || PlayerStore.Instance.playerCash < credits) { _message = L.T("msg.balance"); Open(); return; }
            ModHelper.AddCredit(-credits, false);
            var w = Session.Data.Poinbase;
            w.Add(Assets.CRC, credits / Session.Data.Stable.Crc);
            w.RecordBuy(Assets.CRC, credits / Session.Data.Stable.Crc, credits);
            _message = L.T("msg.deposit", credits);
            Session.Save();
            Open();
        }

        private static void Withdraw()
        {
            if (!ReadAmount(out double v)) return;
            var d = Session.Data;
            if (d.Perps.Debt > 0) { _message = L.T("perp.debt.block"); Open(); return; }
            if (d.Poinbase.Avail(Assets.CRC) + 1e-9 < v) { _message = L.T("msg.balance"); Open(); return; }
            var r = d.Exchange.Cashout(d.Poinbase, v, Session.Price);
            int got = (int)Math.Floor(r.Received);
            ModHelper.AddCredit(got, true);
            _message = L.T("msg.cashout", got);
            Session.Save();
            Open();
        }

        /// <summary>交易：Poinbase 是托管账户，直接成交。输入的数量是标的币：买入按池子反算要付的计价币。</summary>
        private static void Trade(Pool p, bool buy)
        {
            if (!ReadAmount(out double v)) return;
            var w = Session.Data.Poinbase;
            if (buy) v = p.QuoteIn(v);
            string pay = buy ? p.Quote : p.Base;
            if (double.IsInfinity(v) || w.Avail(pay) + 1e-9 < v) { _message = L.T(frozenPay(w, pay) ? "msg.frozen" : "msg.balance"); Open(); return; }
            DoTrade(p, buy, v, w);
        }

        private static void DoTrade(Pool p, bool buy, double v, Wallet w)
        {
            string pay = buy ? p.Quote : p.Base, get = buy ? p.Base : p.Quote;
            if (w.Avail(pay) + 1e-9 < v) { _message = L.T(frozenPay(w, pay) ? "msg.frozen" : "msg.balance"); Open(); return; }
            double before = p.Price;
            double got = buy ? p.BuyBase(v) : p.SellBase(v);
            if (p == Session.Data.Market.Main) { Session.Data.Market.PlayerPush += Math.Log(p.Price / before); Log.Info($"现货推价：累计 {Session.Data.Market.PlayerPush:0.000}"); }
            double spent = v * Session.Price(pay);
            CandleChart.Trade(pay, spent); CandleChart.Trade(get, spent);
            w.Add(pay, -v);
            w.Add(get, got);
            w.RecordBuy(get, got, spent);
            _message = L.T("msg.trade", Fmt.Num(v), pay, Fmt.Num(got), get);
            Session.Save();
            Open();
        }
    }
}

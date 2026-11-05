using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Decentralized.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Decentralized
{
    /// <summary>
    /// 硬件钱包 PLEDGER：每个钱包一台，从终端的钱包页打开，各是一个独立窗口。石墨机身、墨水屏、下面两个物理按键
    /// （左 × 拒绝 / 返回，右 ✓ 下一步，签名要按住）。链上的转账都从这里发：终端只开账单（收款地址、币种、金额），
    /// 玩家在钱包里手输地址、核对、按住 ✓ 签名，然后看着交易进内存池、打包、确认。地址带校验位，输错一位几乎都会被拦下；
    /// 碰巧合法的错地址没人用，币就没了。DEX 兑换是合约调用，没有收款地址，只要签名。
    /// 收款方：有账单的地址（执行账单）、自己的另一个钱包、Poinbase 充值地址（只收 CRC / PSC）；其余地址币转进去就回不来。
    /// </summary>
    public static class WalletWindow
    {
        private const string IdPrefix = "pd_wallet_";
        private const float WinW = 452f, WinH = 694f, ScreenW = 384f, ScreenH = 540f, Bezel = 6f;
        private enum Screen { Home, Send, Review, Broadcast, Done }

        /// <summary>终端开出的账单：转账（有收款地址）或合约调用（DEX 兑换，没有地址）。付清后用付款的钱包执行。</summary>
        public sealed class Bill
        {
            public string Address, Asset, Summary;
            public double Amount;
            public bool Contract;
            /// <summary>合约调用时钱包里实际要花掉的数量（默认等于 Amount；取出质押这类不花币的调用为 0）和付手续费的币（默认用 Asset）。</summary>
            public double? Spend;
            public string FeeAsset;
            public Action<Wallet> Execute;
        }

        /// <summary>当前待付的账单（同一时间一张，新的覆盖旧的）。任何一台钱包都可以付。</summary>
        public static Bill Pending;

        private sealed class Tx
        {
            public string To, Asset;
            public double Amount;
            public Bill Bill;
            public long Block;
            public float Start;
            public bool Finished;
            /// <summary>手续费（币的数量）与扣费的币。</summary>
            public double Fee;
            public string FeeAsset;
            /// <summary>各阶段结束的时刻（秒）：签名、内存池、打包、每次确认；被挤出内存池时只有前两段再加一段等待。</summary>
            public float[] Times;
            public int Confs;
            public bool Dropped;
        }

        /// <summary>一台硬件钱包的屏幕状态。</summary>
        private sealed class Device
        {
            public string Address;
            public Screen Screen = Screen.Home;
            public string To = "", Amount = "", Asset = Assets.PSC, Note;
            public FeeTier Tier = FeeTier.Normal;
            public Tx Tx;
            public string Id => IdPrefix + Address;
            public Wallet Wallet => Session.Data.Own(Address);
        }

        private static readonly Dictionary<string, Device> Devices = new Dictionary<string, Device>();
        private static readonly string[] SendAssets = { Assets.PSC, Assets.CRC, Assets.CRT, Assets.CRA, Assets.SPARK, Assets.Ponero };

        /// <summary>网络拥堵：夜间结算随机开始，持续一两天，期间手续费变贵、慢档和普通档确认变慢。</summary>
        public static bool Congested
        {
            get { var d = Session.Data; int day = StoreStation.GetDayCounter(); return d != null && day >= d.CongestedFrom && day <= d.CongestedUntil; }
        }

        /// <summary>手续费折成币的数量。</summary>
        private static double FeeIn(string asset, FeeTier tier) { double p = Session.Price(asset); return p > 0 ? TxRules.FeeCredits(tier, Congested, Session.Data.Gas) / p : 0; }

        /// <summary>出块动画的时间表：签名 0.8 秒，内存池 1.2 秒，打包 1 秒，每次确认 0.6 秒；拥堵时按档位放慢。</summary>
        private static float[] Timeline(int confs, double slow, bool dropped)
        {
            var t = new List<float> { 0.8f * (float)slow, (0.8f + 1.2f) * (float)slow };
            if (dropped) { t.Add(t[1] + 1.5f * (float)slow); return t.ToArray(); }
            t.Add(t[1] + 1.0f * (float)slow);
            for (int i = 0; i < confs; i++) t.Add(t[t.Count - 1] + 0.6f * (float)slow);
            return t.ToArray();
        }


        public static string NameOf(Wallet w)
        {
            if (!string.IsNullOrWhiteSpace(w.Label)) return w.Label;
            int i = Session.Data.Wallets.IndexOf(w);
            return L.T("hw.wallet.n", w.Name ?? (i + 1).ToString());
        }

        /// <summary>按住 ✓ 签名要多久：基础秒数 × 该钱包当前 Gas 档位的速度倍数（快档短、慢档长）。</summary>
        internal static float HoldSecs(string address)
        {
            var w = Session.Data?.Own(address);
            return (float)(Tuning.TxHoldSecs * (w == null ? 1 : TxRules.TierSpeed(Dev(w).Tier)));
        }

        private static Device Dev(Wallet w)
        {
            if (!Devices.TryGetValue(w.Address, out var dev)) Devices[w.Address] = dev = new Device { Address = w.Address };
            return dev;
        }

        private static bool IsOpen(Device dev) => CustomUIManager.Instance != null && CustomUIManager.Instance.IsOpen(dev.Id);

        /// <summary>钱包被销毁：关掉它的窗口，丢掉设备状态。</summary>
        public static void Close(Wallet w)
        {
            if (w == null || !Devices.TryGetValue(w.Address, out var dev)) return;
            if (IsOpen(dev)) CustomUIManager.Instance.CloseWindow(dev.Id);
            Devices.Remove(w.Address);
        }

        /// <summary>读档 / 新游戏后清掉上一局的设备状态并关窗。</summary>
        public static void Reset()
        {
            foreach (var dev in Devices.Values)
                if (IsOpen(dev)) CustomUIManager.Instance.CloseWindow(dev.Id);
            Devices.Clear();
            Pending = null;
        }

        /// <summary>终端"打开钱包转账" / DEX 签名请求：在当前连接的钱包上打开。转账填好币种和金额，地址留给玩家手输。</summary>
        public static void OpenForBill(Bill bill)
        {
            if (!Session.Data.Connected) { TerminalWindow.Refresh(L.T("web3.err.noconn")); return; }
            Pending = bill;
            var dev = Dev(Session.Data.Hot);
            if (dev.Tx != null && !dev.Tx.Finished) { Open(dev); return; }
            dev.Note = null;
            if (bill.Contract) dev.Screen = Screen.Review;
            else
            {
                dev.Screen = Screen.Send;
                dev.To = "";
                dev.Asset = bill.Asset;
                dev.Amount = Amt(bill.Amount);
            }
            Open(dev);
        }

        public static void Open(Wallet w) { if (w != null) Open(Dev(w)); }

        private static void Open(Device dev)
        {
            if (Session.Data == null || dev.Wallet == null) return;
            WalletDriver.Ensure();
            var b = CustomUIManager.Window(dev.Id, "PLEDGER");
            if (b == null) return;
            _rows.Clear(); _clear.Clear(); _picked.Clear();
            int idx = Session.Data.Wallets.IndexOf(dev.Wallet);
            b.SetSize(WinW, WinH).SetDraggable(true).SetCloseOnEscape(true).UseMachineryStyle();
            b.SetPosition(new Vector2(600f + 28f * idx, -24f * idx));
            // 屏幕外面一圈黑色边框，里面是墨水屏
            b.BeginPanel(ScreenW + Bezel * 2, ScreenH + Bezel * 2, TextAnchor.MiddleCenter, Bezel + 3f, 0f);
            var bezel = b.Last.image;
            b.BeginPanel(ScreenW, ScreenH, TextAnchor.UpperLeft, 14f, 6f);
            var screen = b.CurrentContainer;
            var paper = b.Last.image;
            switch (dev.Screen)
            {
                case Screen.Send: SendScreen(b, dev); break;
                case Screen.Review: ReviewScreen(b, dev); break;
                case Screen.Broadcast: BroadcastScreen(b, dev); break;
                case Screen.Done: DoneScreen(b, dev); break;
                default: HomeScreen(b, dev); break;
            }
            b.AddFlexibleSpace();
            if (!string.IsNullOrEmpty(dev.Note)) b.AddLabel(dev.Note, "note");
            b.End();
            b.End();
            // 机身下沿：左 ×，右 ✓，左右对称
            b.BeginRow(0f, TextAnchor.MiddleCenter);
            b.AddIconButton(Items.ModSprite("ui_hw_no"), 60f, () => Reject(dev));
            b.BeginPanel(ScreenW + Bezel * 2 - 120f, 60f, TextAnchor.MiddleCenter, 0f, 0f);
            var plate = b.Last.image;
            b.End();
            b.AddIconButton(Items.ModSprite("ui_hw_ok"), 60f, () => Confirm(dev));
            var ok = b.Last.root;
            b.End();
            var win = b.Show();
            Log.Guard("硬件钱包外观", () => Skin(win, screen, bezel, paper, plate, ok, dev));
        }

        /// <summary>换上硬件的样子：机身、屏幕边框、墨水屏是自己画的像素九宫格，屏上按钮做成黑底白字的触控键。</summary>
        private static void Skin(CustomUIWindow win, RectTransform screen, Image bezel, Image paper, Image plate, GameObject ok, Device dev)
        {
            if (win?.root == null) return;
            var rows = _rows.Select(x => new PledgerSkin.RowDef
            {
                Root = x.root,
                Image = x.image,
                Selected = (x.asset == dev.Asset),
                OnClick = () => { dev.Asset = x.asset; dev.Note = null; Open(dev); }
            });
            PledgerSkin.Apply(win, screen, bezel, paper, _picked, _clear, rows);
            if (plate != null) PledgerSkin.SetClear(plate);
            // 签名要按住 ✓：在按键上挂一个按住计时
            if (ok != null && dev.Screen == Screen.Review && ok.GetComponent<HoldToSign>() == null) ok.AddComponent<HoldToSign>().Device = dev.Address;
        }

        private const float RowW = ScreenW - 28f;
        private static readonly List<(GameObject root, Image image, string asset)> _rows = new List<(GameObject, Image, string)>();
        private static readonly List<Image> _clear = new List<Image>();
        private static readonly List<GameObject> _picked = new List<GameObject>();

        /// <summary>屏上的选择按钮（币种、手续费档位）：选中的一个画成白底黑框黑字、四角带准星角标。</summary>
        private static void Pick(CustomUIBuilder b, string text, bool selected, Action onClick) =>
            PledgerSkin.Pick(b, text, selected, onClick, _picked);

        public static string Amt(double v) => v.ToString(v >= 100 ? "0.##" : "0.####", CultureInfo.InvariantCulture);

        /// <summary>账单金额按钱包显示的精度往上取整，照着填就够付。</summary>
        public static double RoundUp(double v) => v >= 100 ? Math.Ceiling(v * 100 - 1e-6) / 100 : Math.Ceiling(v * 10000 - 1e-6) / 10000;

        private static void Go(Device dev, Screen s) { dev.Screen = s; dev.Note = null; Open(dev); }

        private static void HomeScreen(CustomUIBuilder b, Device dev)
        {
            var w = dev.Wallet;
            b.AddLabel("<size=22><b>" + NameOf(w) + "</b></size>");
            b.BeginRow(8f, TextAnchor.MiddleLeft);
            b.AddLabel(w.Address, "addr_dim");
            b.AddButton(L.T("hw.copy"), () => { Clipboard.Copy(w.Address); dev.Note = L.T("hw.copied"); Open(dev); });
            b.End();
            b.AddSeparator();
            // 余额：每个币一行，左边币种、右边数量与估值，全部显示（含 0）；点一行选中，发送时默认用它
            foreach (var a in SendAssets)
            {
                double q = w.Get(a);
                b.BeginPanel(RowW, 32f, TextAnchor.MiddleLeft, 6f, 0f);
                _rows.Add((b.Last.root, b.Last.image, a));
                b.BeginRow(0f, TextAnchor.MiddleLeft);
                b.BeginPanel(90f, 26f, TextAnchor.MiddleLeft, 0f, 0f);
                _clear.Add(b.Last.image);
                b.AddLabel(a);
                b.End();
                b.BeginPanel(130f, 26f, TextAnchor.MiddleRight, 0f, 0f);
                _clear.Add(b.Last.image);
                b.AddLabel(Amt(q) + (w.Frozen.TryGetValue(a, out var fz) && fz > 0 ? L.T("hw.balance.frozen", Amt(fz)) : ""));
                b.End();
                b.BeginPanel(RowW - 12f - 90f - 130f, 26f, TextAnchor.MiddleRight, 0f, 0f);
                _clear.Add(b.Last.image);
                b.AddLabel(L.T("hw.balance.value", Fmt.Credits(q * Session.Price(a))));
                b.End();
                b.End();
                b.End();
            }
            b.AddSeparator();
            b.AddLabel(L.T("hw.total", Fmt.Credits(w.Value(Session.Price))));
            if (w.Taint > 0.5) b.AddLabel(L.T("wallet.taint", Fmt.Credits(w.Taint)));
            if (Pending != null) b.AddLabel(Pending.Contract ? L.T("hw.pending.sign", Pending.Summary) : L.T("hw.pending.bill", Pending.Summary));
            if (Congested) b.AddLabel(L.T("hw.congested"), "congest_note");
            b.AddSpace(6f);
            b.BeginRow();
            b.AddButton(L.T("hw.send"), () => Go(dev, Screen.Send));
            if (Pending != null && Pending.Contract && w == Session.Data.Hot) b.AddButton(L.T("hw.review"), () => Go(dev, Screen.Review));
            b.End();
        }

        private static void SendScreen(CustomUIBuilder b, Device dev)
        {
            var w = dev.Wallet;
            b.AddHeader(L.T("hw.send"));
            b.AddLabel(L.T("hw.to"));
            b.BeginRow(6f, TextAnchor.MiddleLeft);
            b.BeginPanel(RowW - (L.Wide ? 100f : 70f), -1f, TextAnchor.MiddleLeft, 0f, 0f);
            _clear.Add(b.Last.image);
            b.AddInput("0x", dev.To, s => dev.To = s, "to", s => dev.To = s);
            b.End();
            b.AddButton(L.T("hw.paste"), () =>
            {
                string clip = Clipboard.Paste();
                // 被投毒的钱包：固件里的木马有时把粘贴的地址换成长得很像的骗子地址
                if (w.Poisoned && ChainAddress.Valid(clip) && Session.Data.Rng.Chance(Scales.R(Tuning.TxHijackChance))) { clip = ChainAddress.Lookalike(clip, Session.Data.Rng); Log.Info($"剪贴板木马：{Clipboard.Paste()} → {clip}"); }
                dev.To = clip; dev.Note = null; Open(dev);
            });
            b.End();
            b.BeginRow();
            foreach (var a in SendAssets)
            {
                var asset = a;
                Pick(b, asset, asset == dev.Asset, () => { dev.Asset = asset; Open(dev); });
            }
            b.End();
            b.AddLabel(L.T("hw.avail", dev.Asset, Amt(w.Avail(dev.Asset))), "avail_dim");
            b.AddLabel(L.T("hw.amount"));
            b.AddInput("0", dev.Amount, s => dev.Amount = s, "amount", s => dev.Amount = s);
            if (Pending != null && !Pending.Contract)
            {
                b.AddLabel(L.T("hw.bill", Pending.Summary, Amt(Pending.Amount), Pending.Asset), "bill_dim");
                b.AddButton(L.T("hw.fill"), () => { dev.Asset = Pending.Asset; dev.Amount = Amt(Pending.Amount); Open(dev); });
            }
            // 最近转账：点一下填入地址（地址投毒的 0 币转入也混在这里，缩写看起来一样）
            var recent = w.History.GroupBy(r => r.Addr + (r.In ? "<" : ">")).Select(g => g.First()).Take(4).ToList();
            if (recent.Count > 0)
            {
                b.AddLabel(L.T("hw.recent"), "recent_dim");
                foreach (var r in recent)
                {
                    var rec = r;
                    b.AddButton(L.T(rec.In ? "hw.recent.in" : "hw.recent.out", ChainAddress.Short(rec.Addr), Amt(rec.Amount), rec.Asset), () => { dev.To = rec.Addr; dev.Note = null; Open(dev); });
                }
            }
            b.AddLabel(L.T("hw.next"), "hint_dim");
        }

        private static bool ReadAmount(Device dev, out double v) =>
            double.TryParse((dev.Amount ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0;

        private static bool IsPoinbase(string to) => ChainAddress.Same(to, Session.Data.Address("poinbase"));

        /// <summary>从发送页进核对页之前的检查：地址格式、校验位、余额、收款方收不收。</summary>
        private static string CheckSend(Device dev)
        {
            var w = dev.Wallet;
            string to = ChainAddress.Normalize(dev.To);
            if (!ChainAddress.WellFormed(to)) return L.T("hw.err.format");
            if (!ChainAddress.Valid(to)) return L.T("hw.err.checksum");
            if (ChainAddress.Same(to, w.Address)) return L.T("hw.err.self");
            if (!ReadAmount(dev, out double v)) return L.T("msg.amount");
            if (w.Avail(dev.Asset) + 1e-9 < v) return L.T(w.Frozen.ContainsKey(dev.Asset) ? "msg.frozen" : "msg.balance");
            if (w.Avail(dev.Asset) + 1e-9 < v + FeeIn(dev.Asset, dev.Tier)) return L.T("hw.err.fee");
            var bill = Match(to);
            if (bill != null)
            {
                if (bill.Asset != dev.Asset) return L.T("hw.err.asset", bill.Asset);
                if (v + 1e-9 < bill.Amount) return L.T("hw.err.short", Amt(bill.Amount), bill.Asset);
            }
            else if (IsPoinbase(to)) { }
            else if (Session.Data.Own(to) == null && Known(to)) return L.T("hw.err.nobill");
            return null;
        }

        private static Bill Match(string to) => Pending != null && !Pending.Contract && ChainAddress.Same(Pending.Address, to) ? Pending : null;

        /// <summary>有过往来的地址（平台、商家、混币器、兑换处）：没有账单时对方不收。</summary>
        internal static bool Known(string to)
        {
            var d = Session.Data;
            return d.Addresses.Values.Any(a => ChainAddress.Same(a, to)) || d.Black.Sellers.Any(s => ChainAddress.Same(s.Name, to));
        }

        private static void ReviewScreen(CustomUIBuilder b, Device dev)
        {
            b.AddHeader(L.T("hw.review.h"));
            if (Pending != null && Pending.Contract)
            {
                b.AddLabel(L.T("hw.contract"));
                b.AddLabel(Pending.Summary);
            }
            else
            {
                ReadAmount(dev, out double v);
                string to = ChainAddress.Normalize(dev.To);
                b.AddLabel(L.T("hw.review.to"));
                b.AddLabel("<size=26><b>" + to + "</b></size>");
                b.AddLabel(L.T("hw.review.amount", Amt(v), dev.Asset, Fmt.Credits(v * Session.Price(dev.Asset))));
                var bill = Match(to);
                var own = Session.Data.Own(to);
                if (bill != null) b.AddLabel(L.T("hw.review.bill", bill.Summary));
                else if (own != null) b.AddLabel(L.T("hw.review.own", NameOf(own)));
                else if (IsPoinbase(to)) b.AddLabel(L.T("hw.review.poinbase"));
                else if (dev.Wallet.History.Any(r => ChainAddress.Same(r.Addr, to))) b.AddLabel(L.T("hw.review.history"));
                else b.AddLabel(L.T("hw.review.unknown"), "warn_note");
                if (bill != null && v > bill.Amount + 1e-6) b.AddLabel(L.T("hw.review.over", Amt(v - bill.Amount), dev.Asset), "over_note");
            }
            // 手续费档位、确认数、拥堵
            string feeAsset = FeeAssetOf(dev);
            b.AddSeparator();
            b.BeginRow();
            foreach (FeeTier t in Enum.GetValues(typeof(FeeTier)))
            {
                var tier = t;
                string n = L.T("hw.fee." + t.ToString().ToLowerInvariant());
                Pick(b, n, tier == dev.Tier, () => { dev.Tier = tier; dev.Note = null; Open(dev); });
            }
            b.End();
            double fee = FeeIn(feeAsset, dev.Tier);
            b.AddLabel(L.T("hw.fee", Amt(fee), feeAsset, Fmt.Num(TxRules.FeeCredits(dev.Tier, Congested, Session.Data.Gas))));
            b.AddLabel(L.T("hw.gas", Session.Data.Gas.ToString("0.00", CultureInfo.InvariantCulture)), "gas_dim");
            b.AddLabel(L.T("hw.confs", TxRules.Confirmations(ValueOf(dev))));
            if (Congested) b.AddLabel(dev.Tier == FeeTier.Slow ? L.T("hw.congested.slow") : L.T("hw.congested"), "congest_note");
            b.AddSpace(8f);
            b.AddLabel(L.T("hw.hold"), "hold_dim");
        }

        private static void BroadcastScreen(CustomUIBuilder b, Device dev)
        {
            b.AddHeader(L.T("hw.tx.h"));
            b.AddLabel(StatusText(dev), "status");
            b.AddProgressBar(Progress(dev), "progress");
            b.AddSpace(10f);
            b.AddButton(L.T("hw.skip"), () => Finish(dev));
        }

        private static void DoneScreen(CustomUIBuilder b, Device dev)
        {
            var tx = dev.Tx;
            if (tx != null && tx.Dropped)
            {
                b.AddHeader(L.T("hw.dropped.h"));
                b.AddLabel(L.T("hw.tx.dropped"));
                b.AddLabel(L.T("hw.fee", Amt(tx.Fee), tx.FeeAsset, Fmt.Num(tx.Fee * Session.Price(tx.FeeAsset))));
                b.AddSpace(8f);
                b.AddButton(L.T("hw.back"), () => { dev.Tx = null; Go(dev, Screen.Home); });
                return;
            }
            b.AddHeader(L.T("hw.done.h"));
            if (tx != null)
            {
                b.AddLabel(L.T("hw.done.block", tx.Block));
                if (tx.To != null) b.AddLabel(L.T("hw.done.sent", Amt(tx.Amount), tx.Asset, tx.To));
                var own = tx.To != null ? Session.Data.Own(tx.To) : null;
                if (tx.Bill != null) b.AddLabel(tx.Bill.Summary);
                else if (own != null) b.AddLabel(L.T("hw.review.own", NameOf(own)));
                else if (tx.To != null && IsPoinbase(tx.To)) b.AddLabel(L.T("hw.review.poinbase"));
                else if (tx.To != null) b.AddLabel(L.T("hw.done.void"), "void_note");
            }
            b.AddSpace(8f);
            b.AddButton(L.T("hw.back"), () => { dev.Tx = null; Go(dev, Screen.Home); });
        }

        private static float Elapsed(Device dev) => dev.Tx == null ? 0 : Time.unscaledTime - dev.Tx.Start;
        private static float Total(Device dev) => dev.Tx?.Times == null ? 0 : dev.Tx.Times[dev.Tx.Times.Length - 1];
        private static float Progress(Device dev) => Total(dev) <= 0 ? 0 : Mathf.Clamp01(Elapsed(dev) / Total(dev));

        /// <summary>扣手续费的币：转账用转出的币，合约调用用账单上的币。</summary>
        private static string FeeAssetOf(Device dev) => Pending != null && Pending.Contract ? (Pending.FeeAsset ?? Pending.Asset ?? Assets.PSC) : dev.Asset;

        /// <summary>这笔交易的价值（信用点），决定确认数。</summary>
        private static double ValueOf(Device dev)
        {
            if (Pending != null && Pending.Contract) return Pending.Amount * Session.Price(Pending.Asset ?? Assets.PSC);
            ReadAmount(dev, out double v);
            return v * Session.Price(dev.Asset);
        }

        private static string StatusText(Device dev)
        {
            float t = Elapsed(dev);
            var tx = dev.Tx;
            if (tx?.Times == null) return "";
            if (t < tx.Times[0]) return L.T("hw.tx.signing");
            if (t < tx.Times[1]) return L.T("hw.tx.mempool");
            if (tx.Dropped) return L.T("hw.tx.stuck");
            if (t < tx.Times[2]) return L.T("hw.tx.packed", tx.Block);
            int conf = 1;
            while (conf < tx.Confs && t >= tx.Times[2 + conf]) conf++;
            return L.T("hw.tx.confirm", tx.Block, conf, tx.Confs);
        }

        /// <summary>左键 ×：拒绝签名 / 返回上一页。</summary>
        private static void Reject(Device dev)
        {
            dev.Note = null;
            switch (dev.Screen)
            {
                case Screen.Review:
                    if (Pending != null && Pending.Contract) { Pending = null; dev.Screen = Screen.Home; TerminalWindow.Refresh(L.T("hw.rejected")); }
                    else dev.Screen = Screen.Send;
                    break;
                case Screen.Send: dev.Screen = Screen.Home; break;
                case Screen.Done: dev.Screen = Screen.Home; dev.Tx = null; break;
                default: break;
            }
            Open(dev);
        }

        /// <summary>右键 ✓：发送页上是"下一步"；核对页上要按住（见 <see cref="HoldToSign"/>），单击只提示。</summary>
        private static void Confirm(Device dev)
        {
            switch (dev.Screen)
            {
                case Screen.Home: dev.Screen = Screen.Send; dev.Note = null; break;
                case Screen.Send:
                    dev.Note = CheckSend(dev);
                    if (dev.Note == null) dev.Screen = Screen.Review;
                    break;
                case Screen.Review: dev.Note = L.T("hw.hold"); break;
                case Screen.Done: dev.Screen = Screen.Home; dev.Tx = null; dev.Note = null; break;
                default: return;
            }
            Open(dev);
        }

        /// <summary>按住 ✓ 满时间：签名并广播。</summary>
        internal static void Sign(string address)
        {
            if (!Devices.TryGetValue(address, out var dev) || dev.Screen != Screen.Review || dev.Wallet == null) return;
            var d = Session.Data;
            d.Block += 1 + d.Rng.Range(0, 3);
            if (Pending != null && Pending.Contract)
            {
                string a = Pending.Asset ?? Assets.PSC, fa = FeeAssetOf(dev);
                double spend = Pending.Spend ?? Pending.Amount;
                if (a == fa ? dev.Wallet.Avail(a) + 1e-9 < spend + FeeIn(fa, dev.Tier) : dev.Wallet.Avail(a) + 1e-9 < spend || dev.Wallet.Avail(fa) + 1e-9 < FeeIn(fa, dev.Tier)) { dev.Note = L.T("hw.err.fee"); Open(dev); return; }
                dev.Tx = new Tx { Bill = Pending, Block = d.Block, Start = Time.unscaledTime };
            }
            else
            {
                string err = CheckSend(dev);
                if (err != null) { dev.Note = err; dev.Screen = Screen.Send; Open(dev); return; }
                ReadAmount(dev, out double v);
                string to = ChainAddress.Normalize(dev.To);
                dev.Tx = new Tx { To = to, Asset = dev.Asset, Amount = v, Bill = Match(to), Block = d.Block, Start = Time.unscaledTime };
            }
            bool cong = Congested;
            dev.Tx.FeeAsset = FeeAssetOf(dev);
            dev.Tx.Fee = FeeIn(dev.Tx.FeeAsset, dev.Tier);
            dev.Tx.Confs = TxRules.Confirmations(ValueOf(dev));
            dev.Tx.Dropped = d.Rng.Chance(TxRules.DropChance(dev.Tier, cong));
            dev.Tx.Times = Timeline(dev.Tx.Confs, TxRules.Slowdown(dev.Tier, cong), dev.Tx.Dropped);
            if (dev.Tx.Bill != null) Pending = null;
            dev.Screen = Screen.Broadcast;
            dev.Note = null;
            Open(dev);
        }

        /// <summary>动画走完（或点了跳过）：交易落地。付账单的执行终端那边的动作；转给自己的钱包或 Poinbase 的按比例带上污点；没人用的地址，币就没了。</summary>
        private static void Finish(Device dev)
        {
            var tx = dev.Tx;
            if (tx == null || tx.Finished) return;
            tx.Finished = true;
            var d = Session.Data;
            var w = dev.Wallet;
            Log.Guard("钱包交易落地", () =>
            {
                if (w == null) return;
                // 手续费先扣（被挤出内存池也照扣）
                if (tx.Fee > 0 && tx.FeeAsset != null) Wallet.Transfer(w, new Wallet(), tx.FeeAsset, Math.Min(tx.Fee, w.Avail(tx.FeeAsset)), Session.Price);
                if (tx.Dropped)
                {
                    // 没成交：账单还在，可以重新付
                    if (tx.Bill != null && Pending == null) Pending = tx.Bill;
                    TerminalWindow.Refresh(L.T("hw.tx.dropped"));
                    return;
                }
                int day = StoreStation.GetDayCounter();
                double amt = tx.To != null ? Math.Min(tx.Amount, w.Avail(tx.Asset)) : 0;
                if (tx.To != null)
                {
                    TxRules.Record(w.History, new TxRecord { Addr = tx.To, Asset = tx.Asset, Amount = amt, Day = day });
                    d.Ledger.Add(new LedgerEntry { Block = tx.Block, Day = day, From = w.Address, To = tx.To, Asset = tx.Asset, Amount = amt, Fee = tx.FeeAsset == tx.Asset ? tx.Fee : 0 });
                    var mine = d.Own(tx.To);
                    if (mine != null) TxRules.Record(mine.History, new TxRecord { Addr = w.Address, Asset = tx.Asset, Amount = amt, Day = day, In = true });
                    // 地址投毒：链上公开的转账被人盯上，过一两天会收到一笔来自相似地址的 0 币转入
                    else if (d.Rng.Chance(Scales.R(Tuning.TxPoisonChance))) d.PoisonPlans.Add(new PoisonPlan { Wallet = w.Address, Target = tx.To, Day = day + d.Rng.Range(1, Tuning.TxPoisonDelayMax) });
                }
                if (tx.Bill != null && !tx.Bill.Contract)
                {
                    // 多付的部分对方不退
                    double extra = amt - tx.Bill.Amount;
                    if (extra > 1e-9) Wallet.Transfer(w, new Wallet(), tx.Asset, extra, Session.Price);
                }
                if (tx.Bill != null) { tx.Bill.Execute(w); return; }
                if (tx.To == null) return;
                var own = d.Own(tx.To);
                Wallet target = own ?? (IsPoinbase(tx.To) ? d.Poinbase : new Wallet());
                Wallet.Transfer(w, target, tx.Asset, amt, Session.Price);
                if (own == null && target != d.Poinbase) Log.Info($"转进没人用的地址 {tx.To}：{amt} {tx.Asset}");
                if (own == null && d.Scams.ScamAddrs.Any(a => ChainAddress.Same(a, tx.To))) PlayerStore.Instance.AddNightLog(L.T("nightlog.scam.sent", Fmt.Num(amt), tx.Asset), "cc7755");
                TerminalWindow.Refresh(null);
                if (own != null) RefreshDevice(own);
            });
            Session.Save();
            dev.Screen = Screen.Done;
            dev.To = "";
            if (IsOpen(dev)) Open(dev);
        }

        /// <summary>别的交易让这个钱包的余额变了：开着的话刷新主页。</summary>
        public static void RefreshDevice(Wallet w, bool any = false)
        {
            if (w == null || !Devices.TryGetValue(w.Address, out var dev) || !IsOpen(dev)) return;
            if (any || dev.Screen == Screen.Home || dev.Screen == Screen.Send) Open(dev);
        }

        /// <summary>每帧推进出块动画：只改状态文字和进度条，不重建窗口。</summary>
        internal static void Tick()
        {
            foreach (var dev in Devices.Values.ToList())
            {
                if (dev.Tx == null || dev.Tx.Finished || dev.Screen != Screen.Broadcast) continue;
                if (Elapsed(dev) >= Total(dev)) { Finish(dev); continue; }
                if (!IsOpen(dev)) continue;
                var m = CustomUIManager.Instance;
                m.GetElement(dev.Id, "status")?.SetText(StatusText(dev));
                m.GetElement(dev.Id, "progress")?.SetProgress(Progress(dev));
            }
        }
    }

    /// <summary>常驻的驱动：每帧推进钱包的出块动画（窗口关着也照样走完）。</summary>
    public sealed class WalletDriver : MonoBehaviour
    {
        private static WalletDriver instance;

        public static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("pd_wallet_driver");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<WalletDriver>();
        }

        public static void Remove()
        {
            if (instance != null) Destroy(instance.gameObject);
            instance = null;
        }

        private void Update() { Log.Guard("钱包动画", WalletWindow.Tick); Log.Guard("出金动画", Funds.Tick); }
        private void LateUpdate() => Log.Guard("终端刷新", TerminalWindow.Flush);
    }

    /// <summary>✓ 键上的按住计时：按满 1.2 秒签名，鼠标挪开或松手就重来。进度用游戏自带的按住圆环显示。</summary>
    public sealed class HoldToSign : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private float Seconds = (float)Tuning.TxHoldSecs;
        private float held = -1f;
        public string Device;

        public void OnPointerDown(PointerEventData e) { Seconds = WalletWindow.HoldSecs(Device); held = 0f; }
        public void OnPointerUp(PointerEventData e) => Stop();
        public void OnPointerExit(PointerEventData e) => Stop();

        private void Stop()
        {
            if (held >= 0f) CustomUIManager.Instance?.HideHoldRing();
            held = -1f;
        }

        private void Update()
        {
            if (held < 0f) return;
            held += Time.unscaledDeltaTime;
            CustomUIManager.Instance?.ShowHoldRing(held / Seconds);
            if (held >= Seconds) { Stop(); Log.Guard("签名", () => WalletWindow.Sign(Device)); }
        }

        private void OnDisable() => Stop();
    }
}

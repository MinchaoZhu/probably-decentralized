using System;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using Decentralized.Domain;

namespace Decentralized
{
    /// <summary>
    /// 矿机的收款地址：挖矿产出每晚发到这台矿机设置的地址，不跟着当前连接的钱包走。
    /// 没设置、或地址对应的钱包已销毁，产出就丢了（和别的转到没人用的地址一样）。地址存在矿机物品的标签里。
    /// </summary>
    public static class Payout
    {
        public const string Tag = "PD_PAYOUT";
        public const string WindowId = "pd_payout";

        private static readonly ConditionalWeakTable<GameItem, MachineryText> Texts = new ConditionalWeakTable<GameItem, MachineryText>();

        public static string Get(GameItem m) => m != null && m.IsTag(Tag) ? m.GetTagReadonly(Tag).GetString() ?? "" : "";

        public static void Set(GameItem m, string address)
        {
            if (m == null) return;
            if (string.IsNullOrEmpty(address)) m.ModifyTag(Tag, s => s.Disable());
            else m.ModifyTag(Tag, s => s.Enable().SetString(address));
            Refresh(m);
        }

        /// <summary>状态面板里这一行的文字：钱包名加短地址；没设置显示"未设置"。</summary>
        public static string Describe(GameItem m)
        {
            string a = Get(m);
            if (string.IsNullOrEmpty(a)) return L.T("payout.unset");
            var w = Session.Data?.Own(a);
            return w != null ? WalletWindow.NameOf(w) + " " + ChainAddress.Short(a) : ChainAddress.Short(a);
        }

        public static void Register(GameItem m, MachineryText text) { Texts.Remove(m); Texts.Add(m, text); Refresh(m); }

        public static void Refresh(GameItem m)
        {
            if (m != null && Texts.TryGetValue(m, out var t)) t.SetStatus(L.T("payout.label"), Describe(m));
        }

        /// <summary>机器窗口里的面板内容：一行文字加一个"设置"键。</summary>
        public static MachineryStack Build(Func<GameItem> owner, out MachineryText text)
        {
            var t = new MachineryText("", 6, TMPro.HorizontalAlignmentOptions.Left, false);
            var stack = new MachineryStack(true, 6).Add(t).Add(new MachineryButton(L.T("payout.set"), () => Log.Guard("矿机收款地址", () => { Log.Info("打开收款地址窗口"); Open(owner()); })));
            text = t;
            return stack;
        }

        // ---------- 设置窗口 ----------

        private static GameItem _target;
        private static string _manual = "", _note;

        public static void Open(GameItem m)
        {
            if (m == null || Session.Data == null) return;
            _target = m; _manual = ""; _note = null;
            Show();
        }

        private static void Show()
        {
            var m = _target;
            if (m == null) return;
            var b = CustomUIManager.Window(WindowId, L.T("payout.h"));
            if (b == null) return;
            b.SetSize(430f, 420f).SetDraggable(true).SetCloseOnEscape(true).UseMachineryStyle();
            b.BeginPanel(410f, -1f, TextAnchor.UpperLeft, 10f, 6f);
            b.AddLabel(L.T("payout.cur", Describe(m)));
            b.AddSpace(4f);
            var d = Session.Data;
            if (d.Wallets.Count > 0)
            {
                b.AddLabel(L.T("payout.pick"));
                foreach (var w in d.Wallets.ToList())
                {
                    var wallet = w;
                    b.AddButton(WalletWindow.NameOf(wallet) + "  " + ChainAddress.Short(wallet.Address), () => Apply(wallet.Address));
                }
                b.AddSpace(4f);
            }
            b.AddLabel(L.T("payout.manual"));
            b.AddInput("0x", _manual, s => _manual = s, "payout", s => _manual = s);
            b.BeginRow(6f);
            b.AddButton(L.T("payout.set"), () =>
            {
                string a = (_manual ?? "").Trim();
                if (!ChainAddress.Valid(a)) { _note = L.T("payout.err"); Show(); return; }
                Apply(a);
            });
            b.AddButton(L.T("payout.clear"), () => Apply(""));
            b.End();
            if (!string.IsNullOrEmpty(_note)) b.AddLabel(_note);
            b.End();
            b.Show();
        }

        private static void Apply(string address)
        {
            Set(_target, address);
            Log.Info($"矿机收款地址：{(string.IsNullOrEmpty(address) ? "清除" : address)}");
            Session.Save();
            if (CustomUIManager.Instance != null) CustomUIManager.Instance.CloseWindow(WindowId);
        }
    }
}

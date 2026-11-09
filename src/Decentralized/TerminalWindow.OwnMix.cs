using System;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Decentralized.Domain;

namespace Decentralized
{
    /// <summary>灰网页的"自营混币器"（买下场外换币台就有）：状态、口碑、风声、池子，费率、开张或暂停、注资、取回、卷款跑路。规则见 <see cref="OwnMixer"/>。</summary>
    public static partial class TerminalWindow
    {
        private static string _ownAsset = Assets.CRT;
        private static bool _ownRugConfirm;
        private static readonly float[] OwnWBase = { 70f, 110f, 110f, 110f };
        private static float[] OwnW => Sc(OwnWBase);
        private static readonly TextAnchor[] OwnAligns = { TextAnchor.MiddleLeft, TextAnchor.MiddleRight, TextAnchor.MiddleRight, TextAnchor.MiddleRight };

        private static void OwnMixPage(CustomUIBuilder b)
        {
            var d = Session.Data;
            var m = d.OwnMix;
            int day = StoreStation.GetDayCounter();
            double enforce = StoryMods.Current.Sanction(day);
            Columns(b, l =>
            {
                l.AddHeader(L.T("ownmix.h"));
                string status = m.Closed ? L.T("ownmix.status.closed") : m.Seized(day) ? L.T("ownmix.status.seized", m.SeizedUntil + 1) : m.Open ? L.T("ownmix.status.open") : L.T("ownmix.status.paused");
                l.AddLabel(status, m.Closed || m.Seized(day) ? "ownmix_status_note" : "ownmix_status_dim");
                var w = new[] { 220f, 200f };
                TableRow(l, w, L.T("ownmix.k.fee"), (m.Fee * 100).ToString("0", CultureInfo.InvariantCulture) + "%");
                TableRow(l, w, L.T("ownmix.k.rating"), ((1 - m.Rating) * 100).ToString("0", CultureInfo.InvariantCulture) + "%");
                TableRow(l, w, L.T("ownmix.k.heat"), L.T("ownmix.heat." + m.HeatLevel(enforce)));
                TableRow(l, w, L.T("ownmix.k.anon"), Fmt.Credits(m.Anonymity(Session.Price)));
                TableRow(l, w, L.T("ownmix.k.fees"), Fmt.Credits(m.FeesTotal));
                l.AddSeparator();
                TableHeader(l, OwnW.Sum() + 8f, OwnW, OwnAligns, L.T("col.asset"), L.T("ownmix.col.capital"), L.T("ownmix.col.earned"), L.T("ownmix.col.pending"));
                foreach (var a in OwnMixer.AssetsList)
                {
                    var p = m.Pool(a);
                    double pend = m.Total(a) - m.Free(a);
                    TableRow(l, OwnW.Sum() + 8f, OwnW, OwnAligns, new[] { a, Fmt.Num(p.Capital), Fmt.Num(p.Earned), Fmt.Num(pend) });
                }
            }, r =>
            {
                if (m.Closed || m.Seized(day)) { r.AddLabel(m.Closed ? L.T("ownmix.status.closed") : L.T("ownmix.status.seized", m.SeizedUntil + 1), "ownmix_closed_note"); return; }
                var w = d.Hot;
                r.AddHeader(L.T("ownmix.k.fee"));
                r.BeginRow(4f);
                foreach (var f in Tuning.OwnMixFees)
                {
                    double fee = f;
                    PledgerSkin.Pick(r, (fee * 100).ToString("0", CultureInfo.InvariantCulture) + "%", Math.Abs(fee - m.Fee) < 1e-9, () => { m.Fee = fee; Session.Save(); Open(); }, _picked);
                }
                r.End();
                r.AddButton(m.Open ? L.T("ownmix.pause") : L.T("ownmix.open"), () =>
                {
                    if (m.Open) m.Open = false; else m.Start(Session.Price);
                    Log.Info($"自营混币器：{(m.Open ? "开张" : "暂停")}，费率 {m.Fee:P0}");
                    Session.Save(); Open();
                });
                r.AddSeparator();
                r.BeginRow(4f);
                foreach (var a in OwnMixer.AssetsList)
                {
                    var asset = a;
                    PledgerSkin.Pick(r, asset, asset == _ownAsset, () => { _ownAsset = asset; Open(); }, _picked);
                }
                r.End();
                r.AddLabel(L.T("mixer.balance", _ownAsset, Fmt.Num(w.Avail(_ownAsset))));
                r.AddLabel(L.T("ownmix.free", Fmt.Num(m.Free(_ownAsset)), _ownAsset));
                AmountRow(r, true, _ownAsset);
                r.BeginRow(6f);
                r.AddButton(L.T("ownmix.inject"), () =>
                {
                    if (!ReadAmount(out double v)) return;
                    string asset = _ownAsset;
                    BillFor(d.Address("ownmix"), L.T("pay.ownmix", Fmt.Num(v), asset), asset, v, pw =>
                    {
                        if (!m.Inject(pw, asset, v)) { _message = L.T("msg.balance"); Open(); return; }
                        _message = L.T("ownmix.injected", Fmt.Num(v), asset);
                        Log.Info($"自营混币器注资 {v} {asset}，匿名集 {m.Anonymity(Session.Price):0}");
                        Session.Save(); Open();
                    });
                });
                r.AddButton(L.T("ownmix.withdraw"), () =>
                {
                    if (!ReadAmount(out double v)) return;
                    string asset = _ownAsset;
                    // 页面上的可取回数量是四舍五入后的，照着填会比实际多一点点：差在显示精度以内就按全部取回
                    double free = m.Free(asset);
                    if (v > free && v - free < 0.01) v = free;
                    if (v > free + 1e-9) { _message = L.T("ownmix.err"); Open(); return; }
                    _message = L.T("hw.wait");
                    Open();
                    WalletWindow.OpenForBill(new WalletWindow.Bill
                    {
                        Contract = true, Party = d.Address("ownmix"), Asset = asset, Amount = 0, Spend = 0, FeeAsset = BestFeeAsset(w),
                        Summary = L.T("ownmix.withdraw.sum", Fmt.Num(v), asset),
                        Execute = pw =>
                        {
                            if (!m.Withdraw(pw, asset, v)) { _message = L.T("ownmix.err"); Open(); return; }
                            _message = L.T("ownmix.withdrawn", Fmt.Num(v), asset);
                            Log.Info($"自营混币器取回 {v} {asset}，钱包里 {asset} 脏 {pw.DirtyOf(asset):0.##}");
                            Session.Save(); Open();
                        },
                    });
                });
                r.End();
                r.AddSeparator();
                double total = OwnMixer.AssetsList.Sum(a => m.Total(a) * Session.Price(a));
                if (total <= 1e-9) return;
                r.AddButton(_ownRugConfirm ? L.T("ownmix.rug.confirm", Fmt.Credits(total)) : L.T("ownmix.rug", Fmt.Credits(total)), () =>
                {
                    if (!_ownRugConfirm) { _ownRugConfirm = true; Open(); return; }
                    _ownRugConfirm = false;
                    _message = L.T("hw.wait");
                    Open();
                    WalletWindow.OpenForBill(new WalletWindow.Bill
                    {
                        Contract = true, Party = d.Address("ownmix"), Asset = Assets.CRT, Amount = 0, Spend = 0, FeeAsset = BestFeeAsset(w),
                        Summary = L.T("ownmix.rug.sum"),
                        Execute = pw =>
                        {
                            if (m.Closed) return;
                            double value = OwnMixer.AssetsList.Sum(a => m.Total(a) * Session.Price(a));
                            m.Rug(pw);
                            PlayerStore.Instance.secData.CommitCrime("FRAUD", (int)Tuning.OwnMixRugCrime);
                            StoreReputation.ModReputation(StoreReputation.FACTION_BLACK_MARKET, -Tuning.OwnMixRugRep);
                            News.Show("pd_news_ownmix_rug_" + StoreStation.GetDayCounter(), L.T("news.ownmix.rug.title"), L.T("news.ownmix.rug.desc"), 4);
                            _message = L.T("ownmix.rugged", Fmt.Credits(value));
                            Log.Info($"自营混币器卷款跑路：{value:0}，钱包 {pw.Address}");
                            Session.Save(); Open();
                        },
                    });
                });
            });
        }
    }
}

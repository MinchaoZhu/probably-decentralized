using System;
using System.Collections.Generic;
using System.Linq;
using Decentralized.Domain;
using UnityEngine;

namespace Decentralized
{
    /// <summary>
    /// Poinbase 的出金：转到钱包地址。托管账户发起，几秒钟的确认动画（模拟交易所触发合约执行）走完才落地；
    /// 手续费固定中间档，从转出的币里扣。动画走完前币还在账户里，中途存档、退出都不会丢币。
    /// 地址格式或校验位不对拒绝；自己的钱包到账；别的格式正确的地址按黑洞处理（币就没了）。
    /// </summary>
    internal static class Funds
    {
        internal sealed class Tx
        {
            public int Id, Confs;
            public string Asset, To;
            public double Amount, Fee;
            public float Start, Total;
            public bool Finished;
        }

        internal static readonly List<Tx> Active = new List<Tx>();
        private static int seq;

        /// <summary>地址检查：返回 null 表示可以转，否则是错误文案的键。</summary>
        internal static string Check(string to)
        {
            var d = Session.Data;
            to = ChainAddress.Normalize(to ?? "");
            if (!ChainAddress.Valid(to)) return "funds.err.addr";
            if (ChainAddress.Same(to, d.Address("poinbase"))) return "funds.err.self";
            if (d.Own(to) == null && WalletWindow.Known(to)) return "funds.err.known";
            return null;
        }

        private static bool Congested
        {
            get { var d = Session.Data; int day = StoreStation.GetDayCounter(); return day >= d.CongestedFrom && day <= d.CongestedUntil; }
        }

        /// <summary>开始一笔出金。余额不足或手续费吃掉全部数额返回错误键，成功返回 null。</summary>
        internal static string Start(string asset, double amount, string to)
        {
            var d = Session.Data;
            var err = Check(to);
            if (err != null) return err;
            var pb = d.Poinbase;
            if (pb.Avail(asset) + 1e-9 < amount) return pb.Frozen.ContainsKey(asset) ? "msg.frozen" : "msg.balance";
            double price = Session.Price(asset);
            double fee = price > 0 ? TxRules.FeeCredits(FeeTier.Normal, Congested, d.Gas) / price : 0;
            if (fee >= amount) return "funds.err.fee";
            double value = amount * price;
            int confs = TxRules.Confirmations(value);
            Active.Add(new Tx
            {
                Id = ++seq, Asset = asset, To = ChainAddress.Normalize(to), Amount = amount, Fee = fee, Confs = confs,
                Start = Time.unscaledTime, Total = (float)(Tuning.FundsBaseSecs + confs * Tuning.FundsConfSecs),
            });
            WalletDriver.Ensure();
            return null;
        }

        internal static float Progress(Tx t) => t.Total <= 0 ? 1 : Mathf.Clamp01((Time.unscaledTime - t.Start) / t.Total);

        internal static int Conf(Tx t) => Math.Max(1, Math.Min(t.Confs, 1 + (int)((Time.unscaledTime - t.Start) / Math.Max(0.01f, t.Total) * t.Confs)));

        internal static string StatusText(Tx t) => L.T("funds.confirm", Conf(t), t.Confs);

        /// <summary>每帧：更新开着的窗口里的进度条，走完的落地。</summary>
        internal static void Tick()
        {
            if (Active.Count == 0) return;
            var m = CustomUIManager.Instance;
            foreach (var t in Active.ToList())
            {
                if (Time.unscaledTime - t.Start >= t.Total) { Finish(t); continue; }
                m?.GetElement(TerminalWindow.Id, "funds_status_" + t.Id)?.SetText(StatusText(t));
                m?.GetElement(TerminalWindow.Id, "funds_prog_" + t.Id)?.SetProgress(Progress(t));
            }
        }

        private static void Finish(Tx t)
        {
            if (t.Finished) return;
            t.Finished = true;
            Active.Remove(t);
            Log.Guard("出金落地", () =>
            {
                var d = Session.Data;
                var pb = d.Poinbase;
                double amt = Math.Min(t.Amount, pb.Avail(t.Asset));
                double fee = Math.Min(t.Fee, amt);
                if (fee > 0) Wallet.Transfer(pb, new Wallet(), t.Asset, fee, Session.Price);
                double net = amt - fee;
                var own = d.Own(t.To);
                Wallet.Transfer(pb, own ?? new Wallet(), t.Asset, net, Session.Price);
                d.Block += 1;
                d.Ledger.Add(new LedgerEntry { Block = d.Block, Day = StoreStation.GetDayCounter(), From = d.Address("poinbase"), To = t.To, Asset = t.Asset, Amount = net, Fee = fee });
                if (own != null)
                {
                    TxRules.Record(own.History, new TxRecord { Addr = d.Address("poinbase"), Asset = t.Asset, Amount = net, Day = StoreStation.GetDayCounter(), In = true });
                    WalletWindow.RefreshDevice(own, true);
                }
                else Log.Info($"出金转进没人用的地址 {t.To}：{net} {t.Asset}");
                Session.Save();
                TerminalWindow.Refresh(own != null
                    ? L.T("funds.done", Fmt.Num(net), t.Asset, WalletWindow.NameOf(own))
                    : L.T("funds.done.void", Fmt.Num(net), t.Asset));
            });
        }
    }
}

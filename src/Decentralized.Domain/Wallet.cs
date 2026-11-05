using System;
using System.Collections.Generic;
using System.Linq;

namespace Decentralized.Domain
{
    /// <summary>
    /// 一个链上地址：各币种余额，加上整个地址的污点额与成本（信用点等值）。
    /// 污点和成本按转出的价值比例随币走。
    /// </summary>
    public sealed class Wallet
    {
        public string Address;
        public Dictionary<string, double> Balances = new Dictionary<string, double>();
        /// <summary>污点额（信用点等值）。</summary>
        public double Taint;
        /// <summary>成本（信用点）：出金时只对超出成本的部分征税。</summary>
        public double Basis;
        /// <summary>被执法标记。</summary>
        public bool Flagged;
        /// <summary>钱包编号（"钱包 n"），添加时定下，销毁别的钱包也不变。</summary>
        public string Name;
        /// <summary>玩家起的名字；空时用默认名。</summary>
        public string Label;
        /// <summary>转账记录（最新的在前），PLEDGER 发送页的"最近"列表从这里取。</summary>
        public List<TxRecord> History = new List<TxRecord>();

        /// <summary>硬件钱包被动过手脚（供应链投毒）：别人手里有助记词。玩家看不到。</summary>
        public bool Poisoned;

        /// <summary>被投毒的钱包被转空前的期望天数：门槛处 7 天，按价值线性降到满额处 1 天；不到门槛不会被转。</summary>
        public static double DrainDays(double value)
        {
            double t = Math.Min(1, (value - Tuning.WalletDrainMin) / (Tuning.WalletDrainFull - Tuning.WalletDrainMin));
            return Tuning.WalletDrainSlowDays - (Tuning.WalletDrainSlowDays - 1) * t;
        }

        /// <summary>被投毒的钱包今晚被转空的概率 = 1 / 期望天数（每晚独立掷一次）。</summary>
        public static double DrainChance(double value) => value < Tuning.WalletDrainMin ? 0 : 1 / DrainDays(value);

        /// <summary>转空：全部余额（含冻结的）连同污点、成本一起转走，返回转走的价值（信用点）。</summary>
        public double Drain(Func<string, double> price)
        {
            double v = Value(price);
            Balances.Clear(); Frozen.Clear(); FrozenUntil.Clear(); BuyQty.Clear(); BuyCost.Clear();
            Taint = 0; Basis = 0;
            return v;
        }

        /// <summary>买入记录：每种币买入的数量与花费（信用点等值），用来显示买入均价。卖出或转出时按比例减少。</summary>
        public Dictionary<string, double> BuyQty = new Dictionary<string, double>(), BuyCost = new Dictionary<string, double>();

        public void RecordBuy(string asset, double qty, double costCredits)
        {
            if (qty <= 0) return;
            BuyQty[asset] = (BuyQty.TryGetValue(asset, out var q) ? q : 0) + qty;
            BuyCost[asset] = (BuyCost.TryGetValue(asset, out var c) ? c : 0) + Math.Max(0, costCredits);
        }

        /// <summary>买入均价（信用点）；没有买入记录（例如全是挖出来的）返回 null。</summary>
        public double? AvgBuy(string asset) => BuyQty.TryGetValue(asset, out var q) && q > 1e-9 ? BuyCost[asset] / q : (double?)null;

        /// <summary>被发行方冻结的数量（只有可冻结的币：CRT 在镇压期间、CRC），和解冻的日子。冻结的币仍计入余额与估值，但不能花、不能转。</summary>
        public Dictionary<string, double> Frozen = new Dictionary<string, double>();
        public Dictionary<string, int> FrozenUntil = new Dictionary<string, int>();

        /// <summary>可动用的余额（扣掉冻结部分）。</summary>
        public double Avail(string asset) => Math.Max(0, Get(asset) - (Frozen.TryGetValue(asset, out var f) ? f : 0));

        /// <summary>冻结该币当前余额的 share 比例，直到 untilDay（含）。已冻结的不重复冻。返回冻结的数量。</summary>
        public double Freeze(string asset, double share, int untilDay)
        {
            double amount = Avail(asset) * Math.Max(0, Math.Min(1, share));
            if (amount <= 1e-9) return 0;
            Frozen[asset] = (Frozen.TryGetValue(asset, out var f) ? f : 0) + amount;
            FrozenUntil[asset] = Math.Max(untilDay, FrozenUntil.TryGetValue(asset, out var u) ? u : 0);
            return amount;
        }

        /// <summary>解冻到期的币，返回 (币种, 数量) 列表。</summary>
        public List<KeyValuePair<string, double>> Thaw(int day)
        {
            var list = new List<KeyValuePair<string, double>>();
            foreach (var a in new List<string>(FrozenUntil.Keys))
                if (day >= FrozenUntil[a])
                {
                    list.Add(new KeyValuePair<string, double>(a, Frozen.TryGetValue(a, out var f) ? f : 0));
                    Frozen.Remove(a); FrozenUntil.Remove(a);
                }
            return list;
        }

        public double Get(string asset) => Balances.TryGetValue(asset, out var v) ? v : 0;

        public void Add(string asset, double amount)
        {
            if (amount == 0) return;
            if (amount < 0 && BuyQty.TryGetValue(asset, out var bq))
            {
                double before = Get(asset), keep = before > 1e-12 ? Math.Max(0, 1 + amount / before) : 0;
                BuyQty[asset] = bq * keep; BuyCost[asset] *= keep;
                if (BuyQty[asset] < 1e-9) { BuyQty.Remove(asset); BuyCost.Remove(asset); }
            }
            double v = Get(asset) + amount;
            if (v < -1e-9) throw new InvalidOperationException($"{Address}: {asset} 余额不足");
            if (Math.Abs(v) < 1e-12) Balances.Remove(asset);
            else Balances[asset] = v;
        }

        /// <summary>按价格表估值（信用点）。</summary>
        /// <summary>花掉（销毁）一笔币：污点与成本按这笔币占钱包总价值的比例一起带走。</summary>
        public void Spend(string asset, double amount, Func<string, double> price)
        {
            double total = Value(price), share = total > 0 ? Math.Min(1, amount * price(asset) / total) : 0;
            double taint = Taint * share, basis = Basis * share;
            Add(asset, -amount); Taint -= taint; Basis -= basis;
        }

        public double Value(Func<string, double> price) => Balances.Sum(kv => kv.Value * price(kv.Key));

        /// <summary>
        /// 转出：taint 和 basis 按转出价值占地址总价值的比例带走；被标记的地址转出可追踪的币，收款地址也被标记。
        /// Ponero 等不可追踪资产转入时，污点和标记都不随之进入目标地址。
        /// </summary>
        public static void Transfer(Wallet from, Wallet to, string asset, double amount, Func<string, double> price)
        {
            if (amount <= 0) return;
            double total = from.Value(price);
            double share = total > 0 ? Math.Min(1.0, amount * price(asset) / total) : 0;
            double taint = from.Taint * share, basis = from.Basis * share;
            from.Add(asset, -amount);
            from.Taint -= taint;
            from.Basis -= basis;
            to.Add(asset, amount);
            var spec = Assets.Get(asset);
            if (spec == null || spec.Traceable) { to.Taint += taint; if (from.Flagged) to.Flagged = true; }
            to.Basis += basis;
        }
    }
}

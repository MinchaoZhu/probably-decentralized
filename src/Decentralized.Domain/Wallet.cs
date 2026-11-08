using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Decentralized.Domain
{
    /// <summary>
    /// 一个链上地址：各币种余额，每种币里有几枚是脏的（<see cref="Dirty"/>），加上整个地址的成本（信用点）。
    /// 污点随具体的币进出：收到带污点的币时记下其中几枚脏；花掉、转出这种币时按它的脏比例带走；兑换成别的币时脏比例跟过去。
    /// 成本按转出的价值比例随币走。
    /// </summary>
    public sealed class Wallet
    {
        public string Address;
        public Dictionary<string, double> Balances = new Dictionary<string, double>();
        /// <summary>每种币里脏的枚数（0 ≤ 脏 ≤ 余额）。</summary>
        public Dictionary<string, double> Dirty = new Dictionary<string, double>();
        /// <summary>旧存档（数据版本 5 之前）整个钱包的污点额（信用点）：只在读档迁移时读一次，之后为 0。</summary>
        [JsonProperty("Taint")] public double LegacyTaint;
        /// <summary>成本（信用点）：出金时只对超出成本的部分征税。</summary>
        public double Basis;
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
            Dirty.Clear(); Basis = 0;
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
            double had = Get(asset), v = had + amount;
            if (v < -1e-9) throw new InvalidOperationException($"{Address}: {asset} 余额不足");
            if (Math.Abs(v) < 1e-12) Balances.Remove(asset);
            else Balances[asset] = v;
            // 拿走的币按这种币的脏比例带走脏枚数
            if (amount < 0 && Dirty.TryGetValue(asset, out var d)) SetDirty(asset, had > 1e-12 ? d * Math.Max(0, v) / had : 0);
        }

        /// <summary>这种币里脏的枚数（不超过余额）。</summary>
        public double DirtyOf(string asset) => Dirty.TryGetValue(asset, out var d) ? Math.Max(0, Math.Min(d, Get(asset))) : 0;

        /// <summary>这种币的脏比例（0–1）。</summary>
        public double DirtyRatio(string asset) { double g = Get(asset); return g > 1e-12 ? DirtyOf(asset) / g : 0; }

        /// <summary>这种币里干净的、能动用的枚数（不超过可动用余额）。钱包往 Poinbase 最多只能充这么多。</summary>
        public double CleanAvail(string asset) => Math.Max(0, Math.Min(Avail(asset), Get(asset) - DirtyOf(asset)));

        /// <summary>按现价算的脏币总价值（信用点）：钱包总表、日志用。</summary>
        public double Taint(Func<string, double> price) => Balances.Keys.Sum(a => DirtyOf(a) * price(a));

        private void SetDirty(string asset, double d)
        {
            d = Math.Max(0, Math.Min(d, Get(asset)));
            if (d < 1e-9) Dirty.Remove(asset); else Dirty[asset] = d;
        }

        /// <summary>收到 amount 枚币，其中 dirty 枚是脏的（不超过 amount）。</summary>
        public void AddDirty(string asset, double amount, double dirty)
        {
            if (amount <= 0) return;
            Add(asset, amount);
            if (dirty > 0) SetDirty(asset, DirtyOf(asset) + Math.Min(dirty, amount));
        }

        /// <summary>把这种币里干净的 qty 枚记成脏的（刚收到的兑付被判内幕、操纵时用）；不够的部分不记，返回没记上的枚数。</summary>
        public double MarkDirty(string asset, double qty)
        {
            if (qty <= 0) return 0;
            double clean = Math.Max(0, Get(asset) - DirtyOf(asset)), mark = Math.Min(qty, clean);
            SetDirty(asset, DirtyOf(asset) + mark);
            return qty - mark;
        }

        /// <summary>余额按比例缩水（CRC 托管银行减记）：脏枚数同比例缩水。</summary>
        public void Scale(string asset, double factor)
        {
            double g = Get(asset);
            if (g <= 0) return;
            double d = DirtyOf(asset);
            Add(asset, g * Math.Max(0, factor) - g);
            SetDirty(asset, d * Math.Max(0, factor));
        }

        /// <summary>兑换：付出 amount 枚 from（按它的脏比例带走脏枚数），收到 got 枚 to，脏比例跟过去；clean 为真时（兑入 Ponero）收到的是干净的。</summary>
        public void Convert(string from, double amount, string to, double got, bool clean = false)
        {
            double r = DirtyRatio(from);
            Add(from, -amount);
            AddDirty(to, got, clean ? 0 : got * r);
        }

        /// <summary>花掉（销毁）一笔币：脏枚数按这种币的脏比例带走，成本按这笔币占钱包总价值的比例带走。</summary>
        public void Spend(string asset, double amount, Func<string, double> price)
        {
            double total = Value(price), share = total > 0 ? Math.Min(1, amount * price(asset) / total) : 0;
            double basis = Basis * share;
            Add(asset, -amount); Basis -= basis;
        }

        /// <summary>按价格表估值（信用点）。</summary>

        public double Value(Func<string, double> price) => Balances.Sum(kv => kv.Value * price(kv.Key));

        /// <summary>按信用点额折成的诈骗罪证：额 × <see cref="Tuning.PredictFraudShare"/>，单次封顶 <see cref="Tuning.PredictFraudCap"/>（永续被判操纵、Tumbler 泄露用）。</summary>
        public static double FraudFor(double credits) => Math.Min(Tuning.PredictFraudCap, Math.Max(0, credits) * Tuning.PredictFraudShare);

        /// <summary>
        /// 只转干净的币（往 Poinbase 充值）：脏枚数全部留在原钱包，成本按价值比例带走。数量超过 <see cref="CleanAvail"/> 的部分不转，返回实际转的数量。
        /// </summary>
        public static double TransferClean(Wallet from, Wallet to, string asset, double amount, Func<string, double> price)
        {
            amount = Math.Min(amount, from.CleanAvail(asset));
            if (amount <= 0) return 0;
            double total = from.Value(price);
            double share = total > 0 ? Math.Min(1.0, amount * price(asset) / total) : 0;
            double basis = from.Basis * share, dirty = from.DirtyOf(asset);
            from.Add(asset, -amount);
            from.SetDirty(asset, dirty);
            from.Basis -= basis;
            to.Add(asset, amount);
            to.Basis += basis;
            return amount;
        }

        /// <summary>
        /// 转出：这种币按它的脏比例把脏枚数带过去，成本按转出价值占地址总价值的比例带走。
        /// Ponero 等不可追踪资产转入时，不带脏枚数。
        /// </summary>
        public static void Transfer(Wallet from, Wallet to, string asset, double amount, Func<string, double> price)
        {
            if (amount <= 0) return;
            double total = from.Value(price);
            double share = total > 0 ? Math.Min(1.0, amount * price(asset) / total) : 0;
            double basis = from.Basis * share, dirty = amount * from.DirtyRatio(asset);
            from.Add(asset, -amount);
            from.Basis -= basis;
            var spec = Assets.Get(asset);
            to.AddDirty(asset, amount, spec == null || spec.Traceable ? dirty : 0);
            to.Basis += basis;
        }

        /// <summary>
        /// 存档迁移：旧的整钱包污点额（信用点）按当前价格摊成各币的脏枚数：脏比例 = min(1, 污点 / 可追踪币的价值)，每种可追踪的币按这个比例记脏。
        /// extra 是另外挪进来的旧污点（Poinbase 账户、永续仓位），一起摊；超出的部分作废。旧字段清零。
        /// </summary>
        public void MigrateTaint(Func<string, double> price, double extra = 0)
        {
            double t = LegacyTaint + extra;
            LegacyTaint = 0;
            if (t <= 0) return;
            var traceable = Balances.Keys.Where(a => Assets.Get(a) == null || Assets.Get(a).Traceable).ToList();
            double v = traceable.Sum(a => Get(a) * price(a));
            if (v <= 0) return;
            double r = Math.Min(1, t / v);
            foreach (var a in traceable) SetDirty(a, DirtyOf(a) + Get(a) * r);
        }
    }
}

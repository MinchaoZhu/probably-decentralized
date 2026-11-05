using System;
using System.Collections.Generic;
using System.Linq;

namespace Decentralized.Domain
{
    /// <summary>一个钱包在革命军质押池里的仓位：计息的本金、排队等解锁的部分，以及存入时带来的污点与成本。</summary>
    public sealed class StakeLot
    {
        public string Wallet;
        public double Amount;
        public List<KeyValuePair<int, double>> Unlocking = new List<KeyValuePair<int, double>>();
        public double Taint, Basis;

        public double Total => Amount + Unlocking.Sum(u => u.Value);
    }

    /// <summary>一次到期或利息的结果，供夜间日志使用。</summary>
    public sealed class StakeEvent
    {
        public string Wallet;
        public double Interest, Released;
    }

    /// <summary>
    /// 革命军 CRA 质押池：质押的 CRA 每晚按日息计息（利息并入本金），取出要先申请，等 <see cref="Tuning.CraUnlockDays"/> 天，期间不计息、
    /// 也不能提前取。到期自动回到质押时的钱包。质押的币不会被冻结，但仓位里仍是 CRA：螺旋来临时到期取回的币只值当时的价格。
    /// 污点与成本随币存入、按比例随币取出。
    /// </summary>
    public sealed class Staking
    {
        public List<StakeLot> Lots = new List<StakeLot>();

        public StakeLot Of(string wallet) => Lots.Find(l => ChainAddress.Same(l.Wallet, wallet));

        /// <summary>全部计息中的质押量，补贴池按它算支出。</summary>
        public double Earning => Lots.Sum(l => l.Amount);

        /// <summary>质押：从钱包取出 CRA，污点与成本按价值比例随币留在仓位里。余额不足返回 false。</summary>
        public bool Stake(Wallet w, double amount, Func<string, double> price)
        {
            if (amount <= 0 || w.Avail(Assets.CRA) + 1e-9 < amount) return false;
            double total = w.Value(price);
            double share = total > 0 ? Math.Min(1, amount * price(Assets.CRA) / total) : 0;
            double taint = w.Taint * share, basis = w.Basis * share;
            w.Add(Assets.CRA, -amount); w.Taint -= taint; w.Basis -= basis;
            var lot = Of(w.Address) ?? AddLot(w.Address);
            lot.Amount += amount; lot.Taint += taint; lot.Basis += basis;
            return true;
        }

        private StakeLot AddLot(string wallet) { var l = new StakeLot { Wallet = wallet }; Lots.Add(l); return l; }

        /// <summary>申请取出：这部分从计息本金里挪到排队区，day + 解锁天数 到期。额度不足返回 false。</summary>
        public bool RequestUnstake(string wallet, double amount, int day)
        {
            var lot = Of(wallet);
            if (lot == null || amount <= 0 || lot.Amount + 1e-9 < amount) return false;
            lot.Amount -= amount;
            lot.Unlocking.Add(new KeyValuePair<int, double>(day + Tuning.CraUnlockDays, amount));
            return true;
        }

        /// <summary>每晚：计息（日息 rate，利息并入本金），到期的部分回到钱包。返回有变化的仓位。</summary>
        public List<StakeEvent> Night(int day, double rate, Func<string, Wallet> wallet)
        {
            var evs = new List<StakeEvent>();
            foreach (var lot in Lots.ToList())
            {
                var ev = new StakeEvent { Wallet = lot.Wallet };
                ev.Interest = lot.Amount * rate;
                lot.Amount += ev.Interest;
                foreach (var u in lot.Unlocking.Where(x => day >= x.Key).ToList())
                {
                    double total = lot.Total;
                    double share = total > 0 ? Math.Min(1, u.Value / total) : 0;
                    double taint = lot.Taint * share, basis = lot.Basis * share;
                    lot.Taint -= taint; lot.Basis -= basis;
                    lot.Unlocking.Remove(u);
                    var w = wallet(lot.Wallet);
                    w.Add(Assets.CRA, u.Value); w.Taint += taint; w.Basis += basis;
                    ev.Released += u.Value;
                }
                if (lot.Total < 1e-9) Lots.Remove(lot);
                if (ev.Interest > 0 || ev.Released > 0) evs.Add(ev);
            }
            return evs;
        }
    }
}

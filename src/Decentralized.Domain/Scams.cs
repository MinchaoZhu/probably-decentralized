using System;
using System.Collections.Generic;
using System.Linq;

namespace Decentralized.Domain
{
    /// <summary>空投：签名领取。正规空投不要授权或只要有限授权，给一点币；钓鱼空投要求对某个币"无上限授权"，几晚后把这个钱包里的这种币转走。</summary>
    public sealed class Airdrop
    {
        public int Id, Expire;
        public string Name, Asset;
        /// <summary>领取要授权的额度：0 = 不要授权；double.PositiveInfinity = 无上限。</summary>
        public double Approve;
        public double Reward;
        public bool Scam, Claimed;
        /// <summary>已为这个空投授权的钱包地址（签过授权合约）；空 = 还没授权。领取要用授权过的钱包。</summary>
        public string AuthWallet;
    }

    /// <summary>钱包对某个合约的授权。钓鱼授权到 DrainDay 那晚把这种币转走，撤销后作废。</summary>
    public sealed class Approval
    {
        public int Id, DrainDay = -1;
        public string Wallet, Asset, Spender;
        public double Limit;
        public bool Scam;
    }

    /// <summary>第三方收益池（CRC）：日息、储备、有没有时间锁、有没有审计都看得见；是不是骗局看不见，跑路时存款全部没了。</summary>
    public sealed class YieldPool
    {
        public int Id, OpenDay;
        public string Name;
        public double Rate, Reserve, Cap;
        public bool Timelock, Audited, Scam, Closed;
        public Dictionary<string, double> Deposits = new Dictionary<string, double>();
        public Dictionary<string, double> Taint = new Dictionary<string, double>(), Basis = new Dictionary<string, double>();
        /// <summary>时间锁池子的取款队列：钱包 → (到账日, 数额)。</summary>
        public List<YieldWithdraw> Pending = new List<YieldWithdraw>();
        public double Total => Deposits.Values.Sum();
        public double Of(string wallet) => Deposits.TryGetValue(wallet, out var v) ? v : 0;
    }

    public sealed class YieldWithdraw { public string Wallet; public int Day; public double Amount, Taint, Basis; }

    /// <summary>夜间发生的事。Kind：drain（钓鱼授权转走）/ rug（收益池跑路）/ dry（正规池储备耗尽，退本金）/ paid（取款到账）/ wickback（插针回去了）。</summary>
    public sealed class ScamEvent
    {
        public string Kind, Name, Wallet, Asset;
        public double Amount;
    }

    /// <summary>NPC 骗局：空投与授权、第三方收益池。插针在 <see cref="Market"/> 里。</summary>
    public sealed class Scams
    {
        public List<Airdrop> Airdrops = new List<Airdrop>();
        public List<Approval> Approvals = new List<Approval>();
        public List<YieldPool> Pools = new List<YieldPool>();
        public int Seq;
        /// <summary>骗子用过的收款地址（假客服、钓鱼）：转到这些地址时晨间日志说明被骗。</summary>
        public List<string> ScamAddrs = new List<string>();

        private static readonly string[] AirdropNames = { "Poinbase", "PSC Foundation", "RatDigg", "Revolution DAO", "Gutter Labs", "Station Rewards" };
        private static readonly string[] PoolNames = { "Nebula Yield", "Rustwater Vault", "Orbit Farm", "Deck 9 Savings", "Gutterflow Pool", "Halo Staking", "Ironclad Earn" };

        public static double ScamChance(double rate, bool timelock, bool audited)
        {
            int i = Array.IndexOf(Tuning.YieldRates, rate);
            double p = i >= 0 ? Tuning.YieldScamByRate[i] : 0.5;
            if (timelock) p -= Tuning.YieldScamTimelock;
            if (audited) p -= Tuning.YieldScamAudit;
            return Math.Max(0.02, Math.Min(0.95, p));
        }

        // ——— 空投 ———

        /// <summary>新空投。钓鱼的名字常是正规名字的仿冒（一个字符不同），要求对一种币无上限授权，奖励看起来更高。</summary>
        public Airdrop NewAirdrop(int day, Rng rng, string[] assets)
        {
            bool scam = rng.Chance(Scales.R(Tuning.AirdropScam));
            string name = AirdropNames[rng.Range(0, AirdropNames.Length - 1)];
            if (scam && rng.Chance(0.6)) name = Typo(name, rng);
            string asset = assets[rng.Range(0, assets.Length - 1)];
            double reward = Math.Round(rng.Range(Tuning.AirdropRewardMin, Tuning.AirdropRewardMax));
            if (scam) reward = Math.Round(reward * rng.Range(1.5, 3.0));
            // 钓鱼：多数要无上限授权，三成要一个看着像有限、其实远大于奖励的额度
            double approve = scam ? (rng.Chance(0.3) ? Math.Round(reward * rng.Range(20.0, 100.0) / 10) * 10 : double.PositiveInfinity) : rng.Chance(0.5) ? 0 : Math.Round(rng.Range(10.0, 60.0));
            var a = new Airdrop { Id = ++Seq, Name = name, Asset = asset, Approve = approve, Reward = reward, Scam = scam, Expire = day + Tuning.AirdropDays };
            Airdrops.Add(a);
            return a;
        }

        private static string Typo(string s, Rng rng)
        {
            var swaps = new Dictionary<char, char> { ['o'] = '0', ['i'] = '1', ['l'] = '1', ['e'] = '3', ['a'] = '4', ['s'] = '5', ['n'] = 'm' };
            var idx = Enumerable.Range(0, s.Length).Where(i => swaps.ContainsKey(char.ToLowerInvariant(s[i]))).ToList();
            if (idx.Count == 0) return s + "s";
            int k = idx[rng.Range(0, idx.Count - 1)];
            return s.Substring(0, k) + swaps[char.ToLowerInvariant(s[k])] + s.Substring(k + 1);
        }

        /// <summary>这个钱包是否已为这个空投授权。</summary>
        public static bool Authorized(Airdrop a, Wallet w) => a != null && w != null && !string.IsNullOrEmpty(a.AuthWallet) && ChainAddress.Same(a.AuthWallet, w.Address);

        /// <summary>为空投授权钱包（签完授权合约后调用）；换了钱包要重新授权。</summary>
        public static bool Authorize(Airdrop a, Wallet w, int day)
        {
            if (a == null || w == null || a.Claimed || day > a.Expire) return false;
            a.AuthWallet = w.Address;
            return true;
        }

        /// <summary>领取：正规空投给奖励（干净的 CRC），钓鱼空投给一点点诱饵；有授权的记一条授权，钓鱼授权定下转走的那晚。</summary>
        public Approval Claim(Airdrop a, Wallet w, int day, Rng rng, out double got)
        {
            got = 0;
            if (a.Claimed || day > a.Expire) return null;
            a.Claimed = true;
            got = a.Scam ? Math.Round(a.Reward * 0.05, 2) : a.Reward;
            w.Add(Assets.CRC, got); w.Basis += got;
            if (a.Approve <= 0) return null;
            var ap = new Approval { Id = ++Seq, Wallet = w.Address, Asset = a.Asset, Spender = a.Name, Limit = a.Approve, Scam = a.Scam, DrainDay = a.Scam ? day + rng.Range(Tuning.AirdropDrainMin, Tuning.AirdropDrainMax) : -1 };
            Approvals.Add(ap);
            return ap;
        }

        public bool Revoke(int id) => Approvals.RemoveAll(x => x.Id == id) > 0;

        // ——— 收益池 ———

        public YieldPool NewPool(int day, Rng rng)
        {
            double rate = Tuning.YieldRates[rng.Range(0, Tuning.YieldRates.Length - 1)];
            bool timelock = rng.Chance(0.5), audited = rng.Chance(0.4);
            bool scam = rng.Chance(ScamChance(rate, timelock, audited));
            double cap = Tuning.YieldCap0 + Tuning.YieldCapPerDay * day;
            var used = Pools.Where(p => !p.Closed).Select(p => p.Name).ToList();
            var names = PoolNames.Where(n => !used.Contains(n)).ToList();
            var p2 = new YieldPool
            {
                Id = ++Seq, OpenDay = day, Name = names.Count > 0 ? names[rng.Range(0, names.Count - 1)] : "Pool " + Seq,
                Rate = rate, Timelock = timelock, Audited = audited, Scam = scam, Cap = cap,
                // 正规池的储备够付若干天满额利息；骗局池报一个好看的数字（不会真付到那么多天，跑路前一直照付）
                Reserve = Math.Round(rate * cap * Tuning.YieldReserveDays * (scam ? rng.Range(0.8, 1.6) : rng.Range(0.6, 1.2))),
            };
            Pools.Add(p2);
            return p2;
        }

        public bool Deposit(YieldPool p, Wallet w, double amount, Func<string, double> pr)
        {
            if (p.Closed || amount <= 0 || w.Avail(Assets.CRC) + 1e-9 < amount || p.Total + amount > p.Cap + 1e-9) return false;
            double total = w.Value(pr), share = total > 0 ? Math.Min(1, amount * pr(Assets.CRC) / total) : 0;
            double taint = w.Taint * share, basis = w.Basis * share;
            w.Add(Assets.CRC, -amount); w.Taint -= taint; w.Basis -= basis;
            p.Deposits[w.Address] = p.Of(w.Address) + amount;
            p.Taint[w.Address] = (p.Taint.TryGetValue(w.Address, out var t) ? t : 0) + taint;
            p.Basis[w.Address] = (p.Basis.TryGetValue(w.Address, out var b) ? b : 0) + basis;
            return true;
        }

        /// <summary>取出：没有时间锁的马上到账；有时间锁的进队列，几天后到账（跑路了就没了）。返回马上到账的数额。</summary>
        public double Withdraw(YieldPool p, Wallet w, int day)
        {
            double amt = p.Of(w.Address);
            if (amt <= 0 || p.Closed) return 0;
            double taint = p.Taint.TryGetValue(w.Address, out var t) ? t : 0, basis = p.Basis.TryGetValue(w.Address, out var b) ? b : 0;
            p.Deposits.Remove(w.Address); p.Taint.Remove(w.Address); p.Basis.Remove(w.Address);
            if (p.Timelock) { p.Pending.Add(new YieldWithdraw { Wallet = w.Address, Day = day + Tuning.YieldLockDays, Amount = amt, Taint = taint, Basis = basis }); return 0; }
            Pay(w, amt, taint, basis);
            return amt;
        }

        private static void Pay(Wallet w, double amt, double taint, double basis)
        {
            w.Add(Assets.CRC, amt); w.Taint += taint; w.Basis += basis;
        }

        // ——— 夜间 ———

        /// <summary>
        /// 夜间：钓鱼授权到期转走；收益池计息、储备耗尽的正规池退本金关闭、骗局池按概率跑路；时间锁取款到账；过期空投清掉；随机新空投和新收益池。
        /// </summary>
        public List<ScamEvent> Night(int day, Rng rng, Func<string, Wallet> own, string[] airdropAssets, Func<string, double> pr = null)
        {
            var evs = new List<ScamEvent>();
            foreach (var ap in Approvals.Where(x => x.Scam && x.DrainDay >= 0 && x.DrainDay <= day).ToList())
            {
                Approvals.Remove(ap);
                var w = own(ap.Wallet);
                if (w == null) continue;
                double amt = Math.Min(ap.Limit, w.Avail(ap.Asset));
                if (amt <= 1e-9) continue;
                if (pr != null) w.Spend(ap.Asset, amt, pr); else w.Add(ap.Asset, -amt);
                evs.Add(new ScamEvent { Kind = "drain", Name = ap.Spender, Wallet = ap.Wallet, Asset = ap.Asset, Amount = amt });
            }
            foreach (var p in Pools.Where(x => !x.Closed).ToList())
            {
                if (p.Scam && p.Total > 0 && day > p.OpenDay && rng.Chance(Scales.R(Tuning.YieldRugNightly)))
                {
                    foreach (var kv in p.Deposits) evs.Add(new ScamEvent { Kind = "rug", Name = p.Name, Wallet = kv.Key, Asset = Assets.CRC, Amount = kv.Value });
                    foreach (var q in p.Pending) evs.Add(new ScamEvent { Kind = "rug", Name = p.Name, Wallet = q.Wallet, Asset = Assets.CRC, Amount = q.Amount });
                    p.Deposits.Clear(); p.Pending.Clear(); p.Closed = true;
                    continue;
                }
                double interest = p.Total * p.Rate;
                if (!p.Scam && interest > p.Reserve)
                {
                    foreach (var kv in p.Deposits.ToList())
                    {
                        var w = own(kv.Key);
                        if (w != null) Pay(w, kv.Value, p.Taint.TryGetValue(kv.Key, out var t) ? t : 0, p.Basis.TryGetValue(kv.Key, out var b) ? b : 0);
                        evs.Add(new ScamEvent { Kind = "dry", Name = p.Name, Wallet = kv.Key, Asset = Assets.CRC, Amount = kv.Value });
                    }
                    p.Deposits.Clear(); p.Closed = true;
                }
                else
                {
                    p.Reserve -= interest;
                    foreach (var k in p.Deposits.Keys.ToList()) p.Deposits[k] *= 1 + p.Rate;
                }
                foreach (var q in p.Pending.Where(x => x.Day <= day).ToList())
                {
                    p.Pending.Remove(q);
                    var w = own(q.Wallet);
                    if (w != null) Pay(w, q.Amount, q.Taint, q.Basis);
                    evs.Add(new ScamEvent { Kind = "paid", Name = p.Name, Wallet = q.Wallet, Asset = Assets.CRC, Amount = q.Amount });
                }
            }
            Pools.RemoveAll(p => p.Closed && p.Pending.Count == 0 && day - p.OpenDay > 20);
            Airdrops.RemoveAll(a => a.Expire < day + 1);
            if (rng.Chance(Tuning.AirdropChance)) NewAirdrop(day + 1, rng, airdropAssets);
            if (Pools.Count(p => !p.Closed) < Tuning.YieldPoolMax && rng.Chance(Tuning.YieldPoolChance)) NewPool(day + 1, rng);
            return evs;
        }
    }
}

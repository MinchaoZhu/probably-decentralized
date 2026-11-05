using System;

namespace Decentralized.Domain
{
    /// <summary>夜间发生的事。Kind：collapse（付不出利息，挤兑）/ reported（被举报）；Amount 是被没收的池子。</summary>
    public sealed class PonziEvent
    {
        public string Kind;
        public double Amount;
    }

    /// <summary>
    /// 高息理财（庞氏）：玩家定一个日息，下层的人按日息和下层声望往池子里存 CRC。每晚先拿新存入的钱付利息，不够的从发行人钱包的 CRC 补，
    /// 补不上就挤兑崩盘；池子越大、日息越高越容易被举报，被举报同样崩盘。崩盘时池子被没收；玩家也可以随时卷走池子（污点全额）。
    /// 每个存档只能开一次。
    /// </summary>
    public sealed class Ponzi
    {
        public bool Open, Done;
        public double Rate, Pool, OwnPaid, AdCost, Inflow;
        /// <summary>发行人的钱包地址：宣传费、垫付的利息从它出，卷走的池子进它。</summary>
        public string Wallet;
        public int OpenDay = -1;

        public static double Cap(int day) => Tuning.PonziCap0 + Tuning.PonziCapPerDay * day;

        /// <summary>每晚新存入（均值）：基数随天数，日息高于 4% 时按指数放大，下层声望越高越多。</summary>
        public static double InflowMean(int day, double rate, int lowerRep) =>
            (Tuning.PonziBase0 + Tuning.PonziBasePerDay * day) * Math.Pow(rate / 0.04, Tuning.PonziRateExp) * Math.Max(0.3, Math.Min(1.5, 0.5 + lowerRep / 100.0));

        public static double ReportChance(double rate, double pool, int day) =>
            Math.Min(0.9, Tuning.PonziReport0 + Tuning.PonziReportRate * rate + Tuning.PonziReportSize * Math.Min(1, pool / Cap(day)));

        public bool CanOpen(int day) => !Open && !Done && day >= Tuning.PonziMinDay;

        /// <summary>开张：宣传费从发行人钱包的 CRC 里扣。</summary>
        public bool Start(Wallet acct, double rate, int day)
        {
            if (!CanOpen(day) || acct.Avail(Assets.CRC) + 1e-9 < Tuning.PonziAdCost) return false;
            acct.Add(Assets.CRC, -Tuning.PonziAdCost);
            Open = true; Rate = rate; Pool = 0; OwnPaid = 0; AdCost = Tuning.PonziAdCost; OpenDay = day; Wallet = acct.Address;
            return true;
        }

        /// <summary>夜间结算。返回崩盘 / 被举报事件（没有事就返回 null）。</summary>
        public PonziEvent Night(int day, int lowerRep, Wallet acct, Rng rng)
        {
            if (!Open) return null;
            double mean = InflowMean(day, Rate, lowerRep);
            double inflow = Math.Min(Cap(day) - Pool, mean * rng.Range(0.6, 1.4));
            inflow = Math.Max(0, inflow); Inflow = inflow;
            double interest = Pool * Rate;
            double fromInflow = Math.Min(interest, inflow), rest = interest - fromInflow;
            if (rest > acct.Avail(Assets.CRC) + 1e-9)
            {
                var ev = new PonziEvent { Kind = "collapse", Amount = Pool + inflow - fromInflow };
                Open = false; Done = true; Pool = 0;
                return ev;
            }
            if (rest > 0) { acct.Add(Assets.CRC, -rest); OwnPaid += rest; }
            Pool += inflow - fromInflow;
            Pool -= Pool * Tuning.PonziWithdraw;
            if (rng.Chance(ReportChance(Rate, Pool, day)))
            {
                var ev = new PonziEvent { Kind = "reported", Amount = Pool };
                Open = false; Done = true; Pool = 0;
                return ev;
            }
            return null;
        }

        /// <summary>卷走：池子全部进账户，污点全额（骗局所得）。返回卷走的数额。</summary>
        /// <summary>卷走：池子全部进发行人钱包，污点全额（骗局所得）。</summary>
        public double Rug(Wallet acct)
        {
            if (!Open) return 0;
            double take = Pool;
            acct.Add(Assets.CRC, take); acct.Taint += take;
            Open = false; Done = true; Pool = 0;
            return take;
        }
    }
}

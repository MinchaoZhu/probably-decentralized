using System;
using System.Collections.Generic;
using System.Linq;

namespace Decentralized.Domain
{
    /// <summary>一次互换的报价：得到数量、经过几个池、相对中间价的损失（手续费加滑点）。</summary>
    public sealed class SwapQuote
    {
        public string From, To;
        public double In, Out, Impact;
        public int Hops;
    }

    /// <summary>
    /// 影响行情的游戏事件种类。接入层把游戏的新闻事件与派系行动映射到这里（例如全站停电、电力短缺 → Blackout）。
    /// </summary>
    public enum MarketEvent { Blackout, Crackdown, GangWar, RevRaidUpper, LowerUnrest }

    /// <summary>
    /// PSC 行情与全部交易对。PSC 有一个参考价（主交易对：合规交易所 PSC/CRC）；
    /// 夜间参考价向价格锚回归并加噪声，其余交易对由 NPC 套利者拉向参考价。
    /// </summary>
    public sealed class Market
    {
        public double Sentiment = 1.0;
        /// <summary>供给因子：出块奖励减半抬高挖矿成本，价格锚随之上移；减半前一周有预期行情。每晚向目标靠拢。</summary>
        public double Supply = 1.0;
        public List<Pool> Pools = new List<Pool>();
        /// <summary>开门时的参考价，夜间用它和收盘价算当日均价（合约的标记价）。</summary>
        public double OpenPrice = Tuning.PscStartPrice;
        /// <summary>玩家自己的现货大单把价格推高（正）或压低（负）的对数幅度，每晚随价格回归一起衰减。操纵检出和预测盘用它估"没有玩家时的价格"。</summary>
        public double PlayerPush;
        /// <summary>今天的插针（对数幅度，0 = 没有）和当晚回去的比例（0 = 真跌真涨）。</summary>
        public double Wick, WickBack;

        /// <summary>夜间结算开始时：插针按比例回去（结算、预测盘读的是回去后的价格）。</summary>
        public void UndoWick(int day)
        {
            if (Wick == 0) return;
            Main.SetPrice(Price * Math.Exp(-Wick * WickBack), Tuning.PoolDepth(day));
            Wick = 0; WickBack = 0;
        }

        /// <summary>
        /// 插针：开门后价格瞬间被打到一边（大户扫强平、砸盘吸筹），大部分当晚回去，一部分是真的。
        /// 标记价用开门前的价格和收盘价平均，强平只吃一半。返回幅度（0 = 今天没有）。
        /// </summary>
        public double TryWick(Rng rng, int day)
        {
            Wick = 0; WickBack = 0;
            if (!rng.Chance(Tuning.WickChance)) return 0;
            double size = rng.Range(Tuning.WickMin, Tuning.WickMax) * (rng.Chance(0.5) ? 1 : -1);
            Wick = size;
            WickBack = rng.Chance(Tuning.WickReal) ? 0 : rng.Range(Tuning.WickRevertMin, Tuning.WickRevertMax);
            Main.SetPrice(Price * Math.Exp(size), Tuning.PoolDepth(day));
            return size;
        }
        public double NeutralPrice => Price * Math.Exp(-PlayerPush);
        /// <summary>被查概率：推价幅度（绝对值）超过 ManipFree 后随幅度升高。</summary>
        public static double ManipChance(double push)
        {
            push = Math.Abs(push);
            return push < Tuning.ManipFree ? 0 : Math.Min(Tuning.ManipMax, Tuning.ManipBase + Tuning.ManipSlope * (push - Tuning.ManipFree));
        }

        public const double ArbRate = Tuning.SideArbRate;          // 非主交易对每晚被拉回参考价的比例
        public const double SideDepthShare = Tuning.SideDepthShare;   // 非主交易对的深度占主交易对的比例

        public static Market CreateDefault(int day)
        {
            var m = new Market();
            double depth = Tuning.PoolDepth(day), p = Tuning.PscStartPrice;
            m.Pools.Add(Pool.CreateBook(Venue.Exchange, Assets.PSC, Assets.CRC, p, depth, Tuning.ExchangeFee));
            m.Pools.Add(Pool.Create(Venue.Dex, Assets.PSC, Assets.CRC, p, depth * SideDepthShare, Tuning.DexFee));
            m.Pools.Add(Pool.CreateStable(Venue.Dex, Assets.CRT, Assets.CRC, 1, depth * SideDepthShare, Tuning.StableFee, Tuning.StableAmp));
            m.Pools.Add(Pool.CreateStable(Venue.Dex, Assets.CRA, Assets.CRC, 1, depth * SideDepthShare, Tuning.StableFee, Tuning.StableAmp));
            m.Pools.Add(Pool.Create(Venue.Dex, Assets.SPARK, Assets.CRC, Tuning.SparkStart, depth * Tuning.SparkDepthShare, Tuning.DexFee));
            m.AddPoinSpark(Tuning.SparkStart, day);
            return m;
        }

        public Pool Main => Pools[0];

        /// <summary>旧存档：稳定币对是恒定乘积池，按当前价换成 StableSwap 池；其他 DEX 池按新深度重设（不等到夜里）。</summary>
        public void UpgradeDexPools(int day)
        {
            for (int i = 0; i < Pools.Count; i++)
            {
                var p = Pools[i];
                if (p.Venue != Venue.Dex || p.Stable) continue;
                if (p.Base == Assets.CRT || p.Base == Assets.CRA)
                    Pools[i] = Pool.CreateStable(Venue.Dex, p.Base, p.Quote, p.Price, Tuning.PoolDepth(day) * SideDepthShare, Tuning.StableFee, Tuning.StableAmp);
                else p.SetPrice(p.Price, Tuning.PoolDepth(day) * DepthShare(p));
            }
        }

        /// <summary>Poinbase 的 SPARK/CRC 订单簿（旧存档没有时按当前价补上）。</summary>
        public void AddPoinSpark(double price, int day)
        {
            if (Find(Venue.Exchange, Assets.SPARK, Assets.CRC) == null)
                Pools.Add(Pool.CreateBook(Venue.Exchange, Assets.SPARK, Assets.CRC, price, Tuning.PoolDepth(day) * Tuning.PoinSparkDepthShare, Tuning.ExchangeFee));
        }

        /// <summary>非主交易对的深度占主池的比例。</summary>
        private static double DepthShare(Pool p) =>
            p.Base != Assets.SPARK ? SideDepthShare : p.Venue == Venue.Exchange ? Tuning.PoinSparkDepthShare : Tuning.SparkDepthShare;

        /// <summary>DEX 上 X/CRC 的池（所有币都以 CRC 计价）。</summary>
        public Pool DexPool(string asset) => Find(Venue.Dex, asset, Assets.CRC);

        /// <summary>
        /// 任意两币互换的报价：CRC 在一端走一个池，否则 X → CRC → Y 两跳（两次手续费、滑点叠加）。
        /// 不改池子；没有路由（同币、没有池）返回 null。
        /// </summary>
        public SwapQuote Quote(string from, string to, double amount)
        {
            if (from == to || amount <= 0) return null;
            var a = from == Assets.CRC ? null : DexPool(from);
            var b = to == Assets.CRC ? null : DexPool(to);
            if ((from != Assets.CRC && a == null) || (to != Assets.CRC && b == null)) return null;
            double crc = a == null ? amount : a.QuoteOut(amount);
            double mid = a == null ? amount : amount * a.Price;
            double outAmt = b == null ? crc : b.BaseOut(crc);
            double ideal = b == null ? mid : mid / b.Price;
            return new SwapQuote { From = from, To = to, In = amount, Out = outAmt, Hops = (a != null ? 1 : 0) + (b != null ? 1 : 0), Impact = ideal > 0 ? Math.Max(0, 1 - outAmt / ideal) : 0 };
        }

        /// <summary>成交：按 <see cref="Quote"/> 的路由改动池子，返回得到的数量（没有路由返回 0）。</summary>
        public double Swap(string from, string to, double amount)
        {
            if (Quote(from, to, amount) == null) return 0;
            double crc = from == Assets.CRC ? amount : DexPool(from).SellBase(amount);
            return to == Assets.CRC ? crc : DexPool(to).BuyBase(crc);
        }

        public Pool Find(Venue venue, string baseAsset, string quote) =>
            Pools.FirstOrDefault(x => x.Venue == venue && x.Base == baseAsset && x.Quote == quote);

        /// <summary>PSC 的参考价（信用点）。</summary>
        public double Price => Main.Price;

        /// <summary>历次减半抽到的传导指数之和：价格锚的供给因子目标为 2^此值。</summary>
        public double HalvingLog;

        /// <summary>减半当晚：抽本次的传导指数。</summary>
        public void OnHalving(Rng rng) => HalvingLog += Tuning.HalvingPassMin + rng.NextDouble() * (Tuning.HalvingPassMax - Tuning.HalvingPassMin);

        /// <summary>供给因子的目标：已发生的减半各自抬高锚；减半前 7 天内再逐日加预期。</summary>
        public double SupplyTarget(int day)
        {
            double t = Math.Pow(2, HalvingLog);
            int toHalving = Tuning.HalvingDays - day % Tuning.HalvingDays;   // 减半当晚 day % HalvingDays == 0，那天之后又是一个完整周期
            if (toHalving <= Tuning.HalvingRampDays) t *= 1 + Tuning.HalvingRamp * (Tuning.HalvingRampDays - toHalving + 1) / Tuning.HalvingRampDays;
            return t;
        }

        public void UpdateSupply(int day) => Supply += (SupplyTarget(day) - Supply) * Tuning.SupplyAdjust;

        public double Anchor(ICollection<MarketEvent> active)
        {
            double a = Tuning.PscStartPrice * Sentiment * Supply;
            if (active.Contains(MarketEvent.Blackout)) a *= 1.10;
            if (active.Contains(MarketEvent.Crackdown)) a *= 0.92;
            return a;
        }

        public static double Volatility(ICollection<MarketEvent> active)
        {
            double v = Tuning.PscVol;
            if (active.Contains(MarketEvent.GangWar)) v *= 2;
            else if (active.Contains(MarketEvent.RevRaidUpper)) v *= 1.5;
            return v;
        }

        /// <summary>开门：一次日内波动（幅度为日波动率的一半）。</summary>
        public void Open(ICollection<MarketEvent> active, Rng rng, int day)
        {
            double p = Price * Math.Exp(rng.Gauss() * Volatility(active) / 2);
            Main.SetPrice(p, Tuning.PoolDepth(day));
            OpenPrice = p;
        }

        /// <summary>NPC 大户：每周约 2 笔、每笔 <see cref="Tuning.WhaleMin"/>–<see cref="Tuning.WhaleMax"/> 信用点，随机买或卖。返回成交额（正为买入），没有交易时为 0。</summary>
        public double Whale(Rng rng)
        {
            if (!rng.Chance(2.0 / 7)) return 0;
            double size = rng.Range(Tuning.WhaleMin, Tuning.WhaleMax);
            if (rng.Chance(0.5)) { Main.BuyBase(size); return size; }
            Main.SellBase(size / Price);
            return -size;
        }

        /// <summary>当日均价：合约强平与借贷估值用它，白天的插针不触发强平。</summary>
        public double Mark => (OpenPrice + Price) / 2;

        /// <summary>
        /// 夜间：情绪做均值回归随机游走；参考价向锚回归 15% 并加噪声，限制在锚的 0.3–3 倍；
        /// 其余交易对按各自计价币的价格拉向参考价。
        /// </summary>
        public void Night(ICollection<MarketEvent> active, Rng rng, int day, Func<string, double> stablePrice)
        {
            if (Wick != 0) { Main.SetPrice(Price * Math.Exp(-Wick * WickBack), Tuning.PoolDepth(day)); Wick = 0; WickBack = 0; }
            PlayerPush *= 1 - Tuning.Revert;
            Sentiment *= Math.Exp(Tuning.SentimentKappa * -Math.Log(Sentiment) + rng.Gauss() * Tuning.SentimentVol);
            double anchor = Anchor(active);
            double lp = Math.Log(Price);
            lp += Tuning.Revert * (Math.Log(anchor) - lp) + rng.Gauss() * Volatility(active);
            double p = Math.Max(Tuning.PscStartPrice * Tuning.PriceFloor, Math.Min(Tuning.PscStartPrice * Tuning.PriceCeiling, Math.Exp(lp)));
            double depth = Tuning.PoolDepth(day);
            Main.SetPrice(p, depth);
            foreach (var pool in Pools.Skip(1))
            {
                double baseCredits = pool.Base == Assets.PSC ? p : stablePrice(pool.Base);
                double quoteCredits = pool.Quote == Assets.PSC ? p : stablePrice(pool.Quote);
                if (quoteCredits <= 0) continue;
                double share = DepthShare(pool);
                // 稳定币对：参考价偏离挂钩超过界线时流动性变差（放大系数降低），回到挂钩附近恢复
                if (pool.Stable)
                {
                    double dev = Math.Abs(baseCredits / quoteCredits - 1);
                    pool.Amp = dev > Tuning.StableBrokenBand ? Tuning.StableAmpBroken : dev > Tuning.StableDepegBand ? Tuning.StableAmpDepeg : Tuning.StableAmp;
                    if (dev > Tuning.StableBrokenBand) share *= Tuning.StableBrokenDepth;
                }
                pool.Revert(baseCredits / quoteCredits, ArbRate, depth * share / quoteCredits);
            }
        }
    }
}

using System;

namespace Decentralized.Domain
{
    public enum Venue { Exchange, Dex, Darknet }

    /// <summary>
    /// 一个交易对。DEX 是恒定乘积池（x·y = k）：Base 是被交易的币，Quote 是计价币，价格 = Quote 储备 / Base 储备。
    /// Poinbase 主池是订单簿（<see cref="Book"/>）：价格只取决于当天的累计净买入量，冲击随单子变大而增长得比线性慢
    /// （价格 = 参考价 × exp(κ · sign(x) · |x|^α)，x = 净买入的名义价值 / 深度），拆单不占便宜，同量买进再卖出回到原价。
    /// 稳定币对（<see cref="Stable"/>）是 StableSwap 池（Curve 的曲线，放大系数 A）：挂钩附近几乎没有滑点，偏离越大越接近恒定乘积；
    /// 储备按"价值单位"记（Base 数量 × 挂钩价），挂钩价夜里随参考价重设。
    /// 每个交易对各有深度；夜间由 NPC 套利者把价格按比例拉向目标价，订单簿的累计量清零。
    /// </summary>
    public sealed class Pool
    {
        public string Base, Quote;
        public Venue Venue;
        public double BaseReserve, QuoteReserve;
        public double Fee;
        /// <summary>订单簿模式：参考价、当天累计净买入的 Base 数量、深度（Quote）。</summary>
        public bool Book;
        public double Ref, Net, Depth;
        /// <summary>StableSwap 模式：放大系数、挂钩价（储备里 Base 一侧按 数量 × 挂钩价 记）。</summary>
        public bool Stable;
        public double Amp, Peg;

        public string Id => $"{Venue}:{Base}/{Quote}";

        public double Price => Book ? PriceAt(Net) : Stable ? Peg * StableSlope(BaseReserve, QuoteReserve) : QuoteReserve / BaseReserve;

        public static Pool CreateStable(Venue venue, string baseAsset, string quote, double price, double depth, double fee, double amp) =>
            new Pool { Venue = venue, Base = baseAsset, Quote = quote, Stable = true, Amp = amp, Peg = price, BaseReserve = depth, QuoteReserve = depth, Fee = fee };

        // ——— StableSwap（两个币）：Ann(x + y) + D = Ann·D + D³ / (4xy)，Ann = 4A ———

        private double Ann => 4 * Amp;

        private double StableD(double x, double y)
        {
            double s = x + y, d = s;
            if (s <= 0) return 0;
            for (int i = 0; i < 255; i++)
            {
                double dp = d * d * d / (4 * x * y), prev = d;
                d = (Ann * s + 2 * dp) * d / ((Ann - 1) * d + 3 * dp);
                if (Math.Abs(d - prev) <= 1e-12 * d) break;
            }
            return d;
        }

        /// <summary>已知一侧储备 x 和不变量 D，求另一侧。</summary>
        private double StableOther(double x, double d)
        {
            double c = d * d / (2 * x) * d / (2 * Ann), b = x + d / Ann, y = d;
            for (int i = 0; i < 255; i++)
            {
                double prev = y;
                y = (y * y + c) / (2 * y + b - d);
                if (Math.Abs(y - prev) <= 1e-12 * y) break;
            }
            return y;
        }

        /// <summary>边际价格（每个价值单位的 Base 换多少 Quote）：不变量对两侧偏导之比。平衡时为 1。</summary>
        private double StableSlope(double x, double y)
        {
            double d = StableD(x, y), d3 = d * d * d;
            return (Ann + d3 / (4 * x * x * y)) / (Ann + d3 / (4 * x * y * y));
        }

        public static Pool CreateBook(Venue venue, string baseAsset, string quote, double price, double depth, double fee) =>
            new Pool { Venue = venue, Base = baseAsset, Quote = quote, Book = true, Ref = price, Depth = depth, Fee = fee };

        private double PriceAt(double net)
        {
            double x = net * Ref / Depth;
            return Ref * Math.Exp(Math.Sign(x) * Tuning.BookK * Math.Pow(Math.Abs(x), Tuning.BookAlpha));
        }

        /// <summary>订单簿上累计净买入从 a 走到 b 的成交额（Quote，不含手续费；b &lt; a 为负）。辛普森积分，同一区间正反两个方向结果互为相反数。</summary>
        private double Cost(double a, double b)
        {
            const int n = 128;
            double h = (b - a) / n, sum = PriceAt(a) + PriceAt(b);
            for (int i = 1; i < n; i++) sum += PriceAt(a + i * h) * (i % 2 == 1 ? 4 : 2);
            return sum * h / 3;
        }

        /// <summary>订单簿：花 quote（不含手续费）能买到多少 Base。牛顿迭代解 Cost(Net, Net + b) = quote。</summary>
        private double BookBaseFor(double quote)
        {
            double b = quote / Price;
            for (int i = 0; i < 30; i++)
            {
                double step = (Cost(Net, Net + b) - quote) / PriceAt(Net + b);
                b = Math.Max(0, b - step);
                if (Math.Abs(step) < 1e-10 * (1 + b)) break;
            }
            return b;
        }

        public static Pool Create(Venue venue, string baseAsset, string quote, double price, double quoteDepth, double fee) =>
            new Pool { Venue = venue, Base = baseAsset, Quote = quote, QuoteReserve = quoteDepth, BaseReserve = quoteDepth / price, Fee = fee };

        /// <summary>预估：卖出 baseIn 个 Base 能得到多少 Quote（不改池子）。</summary>
        public double QuoteOut(double baseIn)
        {
            if (Book) return baseIn > 0 ? Cost(Net - baseIn, Net) * (1 - Fee) : 0;
            if (Stable) return baseIn > 0 ? QuoteReserve - StableOther(BaseReserve + baseIn * Peg * (1 - Fee), StableD(BaseReserve, QuoteReserve)) : 0;
            double x = BaseReserve + baseIn * (1 - Fee);
            return QuoteReserve - BaseReserve * QuoteReserve / x;
        }

        /// <summary>预估：花 quoteIn 个 Quote 能买到多少 Base（不改池子）。</summary>
        public double BaseOut(double quoteIn)
        {
            if (Book) return quoteIn > 0 ? BookBaseFor(quoteIn * (1 - Fee)) : 0;
            if (Stable) return quoteIn > 0 ? (BaseReserve - StableOther(QuoteReserve + quoteIn * (1 - Fee), StableD(BaseReserve, QuoteReserve))) / Peg : 0;
            double y = QuoteReserve + quoteIn * (1 - Fee);
            return BaseReserve - BaseReserve * QuoteReserve / y;
        }

        /// <summary>预估：买到 baseOut 个 Base 要付多少 Quote（含手续费，不改池子）；超出池子储备返回 +∞。</summary>
        public double QuoteIn(double baseOut)
        {
            if (baseOut <= 0) return 0;
            if (Book) return Cost(Net, Net + baseOut) / (1 - Fee);
            if (Stable)
            {
                double x = BaseReserve - baseOut * Peg;
                return x <= 0 ? double.PositiveInfinity : (StableOther(x, StableD(BaseReserve, QuoteReserve)) - QuoteReserve) / (1 - Fee);
            }
            if (baseOut >= BaseReserve) return double.PositiveInfinity;
            return (BaseReserve * QuoteReserve / (BaseReserve - baseOut) - QuoteReserve) / (1 - Fee);
        }

        /// <summary>卖出 Base，返回得到的 Quote。手续费留在池里。</summary>
        public double SellBase(double baseIn)
        {
            if (baseIn <= 0) return 0;
            double out_ = QuoteOut(baseIn);
            if (Book) { Net -= baseIn; return out_; }
            if (Stable) { BaseReserve += baseIn * Peg; QuoteReserve -= out_; return out_; }
            BaseReserve += baseIn;
            QuoteReserve -= out_;
            return out_;
        }

        /// <summary>用 Quote 买入 Base，返回得到的 Base。</summary>
        public double BuyBase(double quoteIn)
        {
            if (quoteIn <= 0) return 0;
            double out_ = BaseOut(quoteIn);
            if (Book) { Net += out_; return out_; }
            if (Stable) { QuoteReserve += quoteIn; BaseReserve -= out_ * Peg; return out_; }
            QuoteReserve += quoteIn;
            BaseReserve -= out_;
            return out_;
        }

        /// <summary>一笔交易的价格冲击（交易后价格相对交易前的变化比例）。</summary>
        public double ImpactOfBuy(double quoteIn)
        {
            double before = Price;
            if (Book) return PriceAt(Net + BaseOut(quoteIn)) / before - 1;
            if (Stable)
            {
                var copy = (Pool)MemberwiseClone();
                copy.BuyBase(quoteIn);
                return copy.Price / before - 1;
            }
            double y = QuoteReserve + quoteIn * (1 - Fee);
            double x = BaseReserve * QuoteReserve / y;
            return (y / x) / before - 1;
        }

        /// <summary>
        /// 夜间：价格在对数空间里向 target 靠拢 rate 的比例，同时把 Quote 一侧的深度重设为 quoteDepth。
        /// </summary>
        public void Revert(double target, double rate, double quoteDepth)
        {
            double lp = Math.Log(Price);
            lp += rate * (Math.Log(target) - lp);
            SetPrice(Math.Exp(lp), quoteDepth);
        }

        public void SetPrice(double price, double quoteDepth)
        {
            if (Book) { Ref = price; Net = 0; Depth = quoteDepth; return; }
            if (Stable) { Peg = price; BaseReserve = quoteDepth; QuoteReserve = quoteDepth; return; }
            QuoteReserve = quoteDepth;
            BaseReserve = quoteDepth / price;
        }
    }
}

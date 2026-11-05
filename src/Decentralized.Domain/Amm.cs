using System;

namespace Decentralized.Domain
{
    public enum Venue { Exchange, Dex, Darknet }

    /// <summary>
    /// 一个交易对 = 一个恒定乘积池（x·y = k）。Base 是被交易的币，Quote 是计价币；价格 = Quote 储备 / Base 储备。
    /// 每个交易对各有深度；夜间由 NPC 套利者把价格按比例拉向目标价。
    /// </summary>
    public sealed class Pool
    {
        public string Base, Quote;
        public Venue Venue;
        public double BaseReserve, QuoteReserve;
        public double Fee;

        public string Id => $"{Venue}:{Base}/{Quote}";

        public double Price => QuoteReserve / BaseReserve;

        public static Pool Create(Venue venue, string baseAsset, string quote, double price, double quoteDepth, double fee) =>
            new Pool { Venue = venue, Base = baseAsset, Quote = quote, QuoteReserve = quoteDepth, BaseReserve = quoteDepth / price, Fee = fee };

        /// <summary>预估：卖出 baseIn 个 Base 能得到多少 Quote（不改池子）。</summary>
        public double QuoteOut(double baseIn)
        {
            double x = BaseReserve + baseIn * (1 - Fee);
            return QuoteReserve - BaseReserve * QuoteReserve / x;
        }

        /// <summary>预估：花 quoteIn 个 Quote 能买到多少 Base（不改池子）。</summary>
        public double BaseOut(double quoteIn)
        {
            double y = QuoteReserve + quoteIn * (1 - Fee);
            return BaseReserve - BaseReserve * QuoteReserve / y;
        }

        /// <summary>卖出 Base，返回得到的 Quote。手续费留在池里。</summary>
        public double SellBase(double baseIn)
        {
            if (baseIn <= 0) return 0;
            double out_ = QuoteOut(baseIn);
            BaseReserve += baseIn;
            QuoteReserve -= out_;
            return out_;
        }

        /// <summary>用 Quote 买入 Base，返回得到的 Base。</summary>
        public double BuyBase(double quoteIn)
        {
            if (quoteIn <= 0) return 0;
            double out_ = BaseOut(quoteIn);
            QuoteReserve += quoteIn;
            BaseReserve -= out_;
            return out_;
        }

        /// <summary>一笔交易的价格冲击（交易后价格相对交易前的变化比例）。</summary>
        public double ImpactOfBuy(double quoteIn)
        {
            double before = Price;
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
            QuoteReserve = quoteDepth;
            BaseReserve = quoteDepth / price;
        }
    }
}

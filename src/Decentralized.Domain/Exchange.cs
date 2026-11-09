using System;

namespace Decentralized.Domain
{
    /// <summary>一次出金到银行的结果。</summary>
    public sealed class CashoutResult
    {
        public double Requested;
        public double Received;
    }

    /// <summary>
    /// 合规交易所 Poinbase：只收干净的币（钱包往充值地址最多转“价值 − 污点”，见 <see cref="Wallet.TransferClean"/>），账户没有污点。
    /// 出金到银行把 CRC 兑回信用点，扣手续费，不做审查。
    /// </summary>
    public sealed class Exchange
    {
        /// <summary>Poinbase 上架的币：只有这些能充值、能在现货交易。黑市的 CRT、革命军的 CRA 合规交易所不收。</summary>
        public static readonly string[] Listed = { Assets.PSC, Assets.CRC, Assets.SPARK };

        /// <summary>存档迁移（数据版本 5）：旧存档里 Poinbase 账户和永续仓位带的污点额（信用点），清零后返回总数，由调用方挪到 1 号钱包一起摊。</summary>
        public static double TakeLegacyTaint(Wallet account, System.Collections.Generic.IEnumerable<PerpPosition> positions)
        {
            double t = account.LegacyTaint;
            account.LegacyTaint = 0;
            foreach (var p in positions) { t += p.LegacyTaint; p.LegacyTaint = 0; }
            return t;
        }

        /// <summary>
        /// 从账户里取出 amount 个 CRC 兑成信用点（按 CRC 当前价格，平时为 1；脱锚期间按市价），扣手续费。
        /// </summary>
        public CashoutResult Cashout(Wallet account, double amount, Func<string, double> price)
        {
            amount = Math.Min(amount, account.Get(Assets.CRC));
            var r = new CashoutResult { Requested = amount };
            if (amount <= 0) return r;
            double value = amount * price(Assets.CRC);
            account.Add(Assets.CRC, -amount);
            r.Received = value * (1 - Tuning.ExchangeFee);
            return r;
        }
    }
}

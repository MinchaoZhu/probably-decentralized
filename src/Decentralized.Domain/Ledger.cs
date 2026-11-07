using System;
using System.Collections.Generic;
using System.Linq;

namespace Decentralized.Domain
{
    /// <summary>浏览器里的一条链上转账：区块、天、转出方、转入方、币种、数额、手续费。</summary>
    public sealed class LedgerEntry
    {
        public long Block;
        public int Day;
        public string From, To, Asset;
        public double Amount, Fee;
    }

    /// <summary>
    /// 全局账本：只记真实发生的转账（玩家钱包的每一笔进出，含合约调用和地址投毒的 0 币转入），不生成 NPC 流水；最新的在前，超过 <see cref="Tuning.LedgerMax"/> 条丢最旧的。
    /// </summary>
    public sealed class Ledger
    {
        public List<LedgerEntry> Entries = new List<LedgerEntry>();

        public void Add(LedgerEntry e)
        {
            if (e == null || e.Amount < 0) return;
            Entries.Insert(0, e);
            if (Entries.Count > Tuning.LedgerMax) Entries.RemoveRange(Tuning.LedgerMax, Entries.Count - Tuning.LedgerMax);
        }

        /// <summary>某个地址相关（转出或转入）的记录，最新的在前，最多 limit 条。address 为空时返回全部最近的。</summary>
        public List<LedgerEntry> Of(string address, int limit)
        {
            if (string.IsNullOrWhiteSpace(address)) return Entries.Take(limit).ToList();
            return Entries.Where(e => ChainAddress.Same(e.From, address) || ChainAddress.Same(e.To, address)).Take(limit).ToList();
        }
    }
}

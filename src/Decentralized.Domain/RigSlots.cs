using System;

namespace Decentralized.Domain
{
    /// <summary>
    /// 矿架的显卡槽：默认 2 个，投入电子元件逐个升级，最多 5 个。每张显卡占 2×2 格，槽在矿架窗口里是一列 2 格宽的格子，
    /// 每个槽两行；未解锁的行是不可放的格子。游戏里电子元件的价值是 33 / 350 / 2,500（普通 / 稀有 / 超科技）。
    /// </summary>
    public static class RigSlots
    {
        public const int Base = 2, Max = 5;
        public const int CellWidth = 3, CellsPerSlot = 2;

        /// <summary>升级到第 n 个槽需要的电子元件（物品 id，件数）。n 从 3 开始。</summary>
        public static (string Item, int Count) Cost(int slot)
        {
            switch (slot)
            {
                case 3: return ("common_electronic", 4);
                case 4: return ("rare_electronic", 1);
                case 5: return ("rare_electronic", 2);
                default: throw new ArgumentOutOfRangeException(nameof(slot));
            }
        }

        /// <summary>当前槽数下，下一次升级需要什么；已满时返回 null。</summary>
        public static (string Item, int Count)? Next(int slots) => slots >= Max ? ((string, int)?)null : Cost(slots + 1);

        /// <summary>
        /// 投入 count 件 item：只收下一级需要的那种；累计到够用就解锁一个槽，多出的留作下一级进度（若下一级是同一种元件）。
        /// 返回实际收下的件数；其余退回（不收）。
        /// </summary>
        public static int Insert(ref int slots, ref int progress, string item, int count)
        {
            if (slots >= Max || count <= 0) return 0;
            var need = Cost(slots + 1);
            if (item != need.Item) return 0;
            int take = Math.Min(count, need.Count - progress);
            progress += take;
            if (progress >= need.Count)
            {
                slots++;
                progress = 0;
            }
            return take;
        }

        /// <summary>显卡格子的形状字符串：宽 3（一张卡 3×2 格），高 2 × 最大槽数；前 slots × 2 行可放（0），其余不可放（1）。</summary>
        public static string Shape(int slots)
        {
            var sb = new System.Text.StringBuilder();
            for (int row = 0; row < Max * CellsPerSlot; row++)
                for (int col = 0; col < CellWidth; col++)
                    sb.Append(row < slots * CellsPerSlot ? '0' : '1');
            return sb.ToString();
        }
    }
}

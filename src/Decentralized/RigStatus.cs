using System.Runtime.CompilerServices;
using UnityEngine;

namespace Decentralized
{
    /// <summary>矿架窗口里的状态面板：显卡数、耗电、算力。每台矿架一组文字，更新矿架时一并刷新。</summary>
    public static class RigStatus
    {
        private static readonly ConditionalWeakTable<GameItem, MachineryText[]> Table = new ConditionalWeakTable<GameItem, MachineryText[]>();

        public static void Add(GameItem rig, MachineryText[] lines) { Table.Remove(rig); Table.Add(rig, lines); }

        public static void Show(GameItem rig, int cards, int slots, int kj, double mhs)
        {
            if (!Table.TryGetValue(rig, out var t) || t.Length < 3) return;
            t[0].SetStatus(L.T("rig.st.cards"), $"{cards}/{slots}");
            t[1].SetStatus(L.T("rig.st.power"), L.T("rig.st.kj", kj));
            t[2].SetStatus(L.T("rig.st.hash"), $"{Fmt.Num(mhs)} MH/s");
        }

        /// <summary>RatDigg 矿机的状态面板：算力、耗电、成色（烧毁显示“已烧毁”）。</summary>
        public static void ShowAsic(GameItem m, double ghs, int kj, double health, bool dead)
        {
            if (!Table.TryGetValue(m, out var t) || t.Length < 3) return;
            t[0].SetStatus(L.T("rig.st.hash"), $"{Fmt.Num(ghs)} GH/s");
            t[1].SetStatus(L.T("rig.st.power"), L.T("rig.st.kj", kj));
            t[2].SetStatus(L.T("rig.st.cond"), dead ? L.T("asic.st.dead") : Mathf.RoundToInt((float)health) + "%");
        }
    }
}

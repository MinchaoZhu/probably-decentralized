using System.Collections.Generic;

namespace Decentralized.Domain
{
    /// <summary>资产的性质。新增币种只是往 <see cref="Assets"/> 里加一条。</summary>
    public sealed class AssetSpec
    {
        public string Id;
        /// <summary>挂钩 1 信用点的稳定币。</summary>
        public bool Stable;
        /// <summary>发行方可以冻结地址（CRC；CRT 只在镇压期间）。</summary>
        public bool Freezable;
        /// <summary>链上可追踪：污点随币传播。Ponero 为 false，换入时污点停止传播。</summary>
        public bool Traceable = true;
        /// <summary>合规交易所可以上架。</summary>
        public bool Compliant = true;
        /// <summary>程序生成或玩家发行的代币。</summary>
        public bool Issued;
    }

    /// <summary>资产登记。信用点（游戏现金）不在链上，不登记。</summary>
    public static class Assets
    {
        public const string PSC = "PSC";
        public const string CRC = "CRC";
        public const string CRT = "CRT";
        public const string CRA = "CRA";
        public const string SPARK = "SPARK";
        public const string Ponero = "PONERO";

        private static readonly Dictionary<string, AssetSpec> Table = new Dictionary<string, AssetSpec>();

        static Assets()
        {
            Register(new AssetSpec { Id = PSC });
            Register(new AssetSpec { Id = CRC, Stable = true, Freezable = true });
            Register(new AssetSpec { Id = CRT, Stable = true, Freezable = true });
            Register(new AssetSpec { Id = CRA, Stable = true });
            Register(new AssetSpec { Id = SPARK });
            Register(new AssetSpec { Id = Ponero, Traceable = false, Compliant = false });
        }

        public static void Register(AssetSpec spec) => Table[spec.Id] = spec;

        public static bool TryGet(string id, out AssetSpec spec) => Table.TryGetValue(id, out spec);

        public static AssetSpec Get(string id) => Table.TryGetValue(id, out var s) ? s : null;

        public static IEnumerable<AssetSpec> All => Table.Values;
    }
}

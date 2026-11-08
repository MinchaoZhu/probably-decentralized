using System;

namespace Decentralized
{
    /// <summary>人脉网络里的"Web3 治理社区"：整个 mod 的入口。买下后交付交易终端和一个硬件钱包（放前台，钱包拖到终端上加载）、一个矿架（放机器区）、一张 RTX 60 和一本显卡鉴定手册，并开始每天的行情与挖矿结算；更多显卡、矿架向杰克逊博士购买。</summary>
    public static class Unlock
    {
        public const string Id = "PD_WEB3_COMMUNITY";
        public const int Price = Domain.Tuning.UnlockPrice;
        /// <summary>人脉网络"基础"页里本 mod 自己的一栏（像水商升级那样成组）：入口、混币器等 Web3 相关升级都放在这里。栏按这个键分组，显示的名称走回调。</summary>
        public const string Card = "PD_WEB3";

        public const string MixerId = "PD_MIXER";
        public const int MixerPrice = Domain.Tuning.MixerUnlockPrice;
        public static bool MixerUnlocked => PlayerStore.Instance != null && ModHelper.IsNetworkUpgradeUnlocked(MixerId);

        public const string BlackId = "PD_BLACKMARKET";

        /// <summary>场外换币台：小偷灯多一个目标"换币客户"，买下时附赠一盏小偷灯（已切到这个目标）。</summary>
        public const string OtcId = "PD_OTC";
        public static bool OtcUnlocked => PlayerStore.Instance != null && ModHelper.IsNetworkUpgradeUnlocked(OtcId);
        public static bool BlackUnlocked => PlayerStore.Instance != null && ModHelper.IsNetworkUpgradeUnlocked(BlackId);

        public static bool IsUnlocked => PlayerStore.Instance != null && ModHelper.IsNetworkUpgradeUnlocked(Id);

        public static void Register()
        {
            // 升级放在默认的"基础"页，自成一栏；标题、说明和栏名都用回调：OnEnable 时 mod 本地化表还没加载
            // 登记定义后游戏会访问 PlayerStore.Instance，主菜单阶段为空会抛异常；定义已登记，读档时游戏会把升级补进这一局。
            // 每个升级单独 try，免得前一个抛异常时后面的没登记上。
            Add(Id, Price, () => L.T("upgrade.title"), () => L.T("upgrade.desc"), OnPurchased, "terminal", null);
            Add(MixerId, MixerPrice, () => L.T("mixer.upgrade.title"), () => L.T("mixer.upgrade.desc"), () => Log.Info("混币器已解锁"), "ui_web3", new[] { Id });
            Add(BlackId, Domain.Tuning.BmUnlockPrice, () => L.T("black.upgrade.title"), () => L.T("black.upgrade.desc"), () => Log.Info("Web3 黑市已解锁"), "ui_web3", new[] { Id });
            Add(OtcId, Domain.Tuning.OtcUnlockPrice, () => L.T("otc.upgrade.title"), () => L.T("otc.upgrade.desc"), OnOtcPurchased, "ui_web3", new[] { Id });
        }

        private static void Add(string id, int price, Func<string> title, Func<string> desc, Action onUnlock, string icon, string[] prerequisites)
        {
            try { ModHelper.AddNetworkUpgrade(ModInfo.Id, id, price, Card, title, desc, onUnlock, iconKey: ModInfo.Id + ":" + icon, prerequisites: prerequisites, subtitleLabel: () => L.T("upgrade.tab")); }
            catch (NullReferenceException) { Log.Info($"人脉升级 {id} 已登记（主菜单阶段访问 PlayerStore 失败，已忽略）"); }
        }

        private static void OnOtcPurchased()
        {
            var lamp = ModHelper.SpawnItem(BlackLampHelper.BLACK_LAMP_ID);
            OtcLamp.SetTarget(lamp, OtcLamp.Target);
            Items.GiveToFront(lamp);
            Log.Info("场外换币台已解锁，附赠小偷灯");
        }

        private static void OnPurchased()
        {
            Items.GiveToFront(ModHelper.SpawnItem(Items.Terminal));
            Items.GiveToFront(Items.NewHwWallet(false));
            Items.GiveToMachineBay(ModHelper.SpawnItem(Items.Rig));
            Items.GiveToFront(ModHelper.SpawnItem(Items.GpuId("RTX60")));
            Items.GiveToFront(ModHelper.SpawnItem(Items.Guide));
            Log.Info("已加入 Web3 治理社区");
        }
    }
}

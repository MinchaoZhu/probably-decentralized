using System;
using System.Collections.Generic;
using Decentralized.Domain;

namespace Decentralized
{
    /// <summary>
    /// 二手显卡贩子：偶尔上门，把几张卡摆上柜台，玩家按外观自己判断（翻面看背面）。
    /// 老实人按真实价值卖，中间商加价，骗子夹带假卡并按标称型号报价；柜台价写在物品的报价标签上，物品归玩家之后只值真实价值。
    /// 另外把显卡登记为鉴定公会的鉴定物品，正确价值与实际售价是同一个数。
    /// </summary>
    public static class UsedDealerClient
    {
        public const string Appraisal = "pd_gpu_appraisal";
                private static int lastVisitDay = -1;

        public static void Subscribe()
        {
            ModHook.OnGenerateCustomerNormal += OnGenerate;
            ShowcaseCases.Register();
            ModHelper.AddAppraisalItem(ModInfo.Id, Appraisal, Items.GpuId("RTX60"), 2, CreateForAppraisal, u => (int)CardValue.True(Items.ToUnit(u)),
                Items.IsGpu, Items.Guide, () => L.T("appraisal.gpu"));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Appraisal client (test)", () => Log.Guard("鉴定客户（测试）", () =>
            {
                while (AppraisalRankHelper.GetRank() < 2) AppraisalRankHelper.Promote();
                var pool = AppraisalItemRegistry.GetPool(AppraisalRankHelper.GetRank());
                Log.Info($"鉴定等级 {AppraisalRankHelper.GetRank()}，可抽的物品：{string.Join(", ", pool.ConvertAll(t => t.id))}");
                var client = StoreClientListAppraisal.CreateAppraisalRandom();
                PlayerStore.Instance.currentClientInstance = StoreClientInstance.CreateClientInstance(client);   // 测试：直接换成鉴定客户
                for (int i = 0; i < 3; i++) PlayerStore.Instance.AddDirectSellingItemToTable(CreateForAppraisal());
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Appraisal cards to counter (test)", () => Log.Guard("鉴定显卡（测试）", () =>
            {
                for (int i = 0; i < 3; i++) PlayerStore.Instance.AddDirectSellingItemToTable(CreateForAppraisal());
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Used dealer visit (test)", () => Log.Guard("二手贩子（测试）", () =>
            {
                var m = PlayerStore.Instance.storeClientManager;
                var c = Create(DealerKind.Scammer, 40);
                PlayerStore.Instance.currentClientInstance = StoreClientInstance.CreateClientInstance(c);   // 测试：直接换成这位贩子
                Place(DealerKind.Scammer, UsedDealers.Cards(DealerKind.Scammer, 40, Session.Data.Rng), Session.Data.Rng);
                Log.Info($"二手贩子（测试）已排入，队列 {m.clientStack.Count} 人");
            }));
        }

        public static void Unsubscribe()
        {
            ModHook.OnGenerateCustomerNormal -= OnGenerate;
            ModHelper.RemoveAppraisalItems(ModInfo.Id);
        }

        private static void OnGenerate(StoreClientManager manager)
        {
            if (Session.Data == null || !Unlock.IsUnlocked) return;
            Log.Guard("二手贩子", () =>
            {
                int day = StoreStation.GetDayCounter();
                if (day < Tuning.UsedFirstDay || day == lastVisitDay || !Session.Data.Rng.Chance(Tuning.UsedVisitChance)) return;
                lastVisitDay = day;
                manager.AddClient(Create(UsedDealers.Kind(day, Session.Data.Rng)));
            });
        }

        /// <summary>鉴定公会送来的卡：一成是假卡；默认估价是按标称型号算的价。</summary>
        private static GameItem CreateForAppraisal()
        {
            if (ShowcaseCases.Override != null) { var o = ShowcaseCases.Override(); ShowcaseCases.Override = null; return o; }
            var rng = Session.Data?.Rng ?? new Rng(1);
            int day = StoreStation.GetDayCounter();
            var cards = UsedDealers.Cards(DealerKind.Middleman, day, rng);
            var c = cards[0];
            var item = Items.NewCard(c.Model, c.Health, c.FakeOf, rng, DealerKind.Middleman);   // 带一份说法：鉴定时玩家要指出哪些不实
            AppraisalHelper.InitAppraisalItem(item, (int)item.unitValue);
            return item;
        }

        internal static StoreClient Create(DealerKind kind, int? atDay = null)
        {
            int day = atDay ?? StoreStation.GetDayCounter();
            var rng = Session.Data.Rng;
            var cards = UsedDealers.Cards(kind, day, rng);
            var client = new StoreClient
            {
                identifier = "pd_used_dealer",
                displayName = L.T("dealer.name"),
                spriteName = SpriteDict.GetRandomScavSpriteName(),
                clientIntent = StoreClient.ClientIntent.SELL,
            };
            StoreClientFactionSetup.InitScav(client);
            client.mainDialogue.SetText(client.displayName, L.T("dealer.line1." + rng.Range(1, 3))).NextDialogue().SetText(client.displayName, L.T("dealer.line2." + rng.Range(1, 3)))
                .SetEndAction(() => Place(kind, cards, rng));
            client.AddBasicDialog();
            client.CompleteClientCreation();
            return client;
        }

        private static void Place(DealerKind kind, List<UsedCard> cards, Rng rng) => Log.Guard("二手贩子上货", () =>
                {
                    NegociationUIManager.Instance.OpenUI();
                    if (ShowcaseCases.Override != null)
                    {
                        var one = ShowcaseCases.Override(); ShowcaseCases.Override = null;
                        PlayerStore.Instance.AddDirectSellingItemToTable(one);
                        return;
                    }
                    var placed = new List<string>();
                    foreach (var c in cards)
                    {
                        var item = Items.NewCard(c.Model, c.Health, c.FakeOf, rng, kind, c.MarkupPct);
                        PlayerStore.Instance.AddDirectSellingItemToTable(item);
                        placed.Add($"{c.Model}{(c.FakeOf != null ? "←" + c.FakeOf : "")} {c.Health:0}% 加价 {c.MarkupPct}% 柜台价 {item.unitValue}（真实 {CardValue.True(Items.ToUnit(item))}）");
                    }
                    Log.Info($"二手贩子（{kind}）：{string.Join("，", placed)}");
                });
    }
}

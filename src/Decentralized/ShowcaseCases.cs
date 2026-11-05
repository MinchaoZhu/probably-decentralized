using System;
using System.Linq;
using System.Collections.Generic;
using Decentralized.Domain;

namespace Decentralized
{
    /// <summary>
    /// 展示页用的显卡鉴定样例（只有测试按钮用）：型号、成色、外观破绽的固定组合，外观写死（种子为 0），
    /// 说法按“隐瞒全部”（骗子 / 鉴定件）或“如实”写。每按一次测试按钮出下一个样例。
    /// </summary>
    public static class ShowcaseCases
    {
        public sealed class Case
        {
            public string Name;
            public string Label, Chip;     // 标称型号；真实芯片（null 表示真卡）
            public double Health;
            public int Fan, Fingers, Bracket, Print, Sticker, Serial, CountBad, PinBad;
            public bool Honest;            // 卖家如实说
        }

        public static readonly List<Case> All = new List<Case>
        {
            new Case { Name = "全新原装", Label = "RTX60", Health = 100, Honest = true },
            new Case { Name = "轻度使用", Label = "RTX70", Health = 82, Fan = 1, Honest = true },
            new Case { Name = "无封条二手", Label = "RX80", Health = 70, Fan = 1, Fingers = 1, Sticker = 2 },
            new Case { Name = "重度老化", Label = "RTX80", Health = 38, Fan = 2, Fingers = 2, Bracket = 1, Sticker = 2 },
            new Case { Name = "旗舰九成新", Label = "RTX90", Health = 92, Honest = true },
            new Case { Name = "PMD 高端磨损", Label = "RX800", Health = 55, Fan = 2, Fingers = 1, Bracket = 1 },
            new Case { Name = "旗舰氧化", Label = "RX900", Health = 28, Fan = 2, Fingers = 2, Bracket = 1, Sticker = 2 },
            new Case { Name = "低档冒充旗舰：接口数对不上", Label = "RTX90", Chip = "RTX60", Health = 90, CountBad = 1, PinBad = 1 },
            new Case { Name = "重印丝印", Label = "RX900", Chip = "RX80", Health = 80, Print = 2, CountBad = 1 },
            new Case { Name = "丝印印着真实型号", Label = "RTX80", Chip = "RTX60", Health = 75, Print = 1, Serial = 2, PinBad = 1 },
            new Case { Name = "手贴序列号", Label = "RX800", Chip = "RX70", Health = 65, Serial = 1, Sticker = 2, CountBad = 1, PinBad = 1 },
            new Case { Name = "全套破绽加老化", Label = "RTX90", Chip = "RTX70", Health = 45, Fan = 1, Fingers = 2, Print = 2, Serial = 2, Sticker = 2, PinBad = 1 },
        };

        private static int _next;
        /// <summary>设了之后，贩子和鉴定客户上货时只拿这一件。</summary>
        public static Func<GameItem> Override;
        private static GameItem _last;

        private static void Place(GameItem item)
        {
            try { _last?.Destroy(); } catch { }
            _last = item;
            GraphUtils.EmporiumTryAdd(item);
        }

        public static GameItem Make(Case c, DealerKind kind)
        {
            var item = ModHelper.SpawnItem(Items.GpuId(c.Label));
            var look = new CardLook { Seed = 0, Fan = c.Fan, Fingers = c.Fingers, Bracket = c.Bracket, Print = c.Print, Sticker = c.Sticker, Serial = c.Serial, CountBad = c.CountBad, PinBad = c.PinBad };
            Items.Store(item, new Unit { Model = c.Label, FakeOf = c.Chip, Health = c.Health, Look = look });
            GeneralHelper.SetItemOwned(item, false);
            var u = Items.ToUnit(item);
            var rng = Session.Data?.Rng ?? new Rng(7);
            var claim = c.Honest ? Claim.Truth(u) : CardLabels.DealerClaim(u, DealerKind.Scammer, rng);
            Items.SetClaim(item, claim, 0, kind == DealerKind.Honest ? 0 : (c.Chip != null ? 0 : 8));
            return item;
        }

        public static void Register()
        {
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Showcase: queue dealer case (test)", () => Log.Guard("展示样例（贩子，排队）", () =>
            {
                var c = All[_next++ % All.Count];
                Override = () => Make(c, DealerKind.Middleman);
                PlayerStore.Instance.storeClientManager.AddNextClient(UsedDealerClient.Create(DealerKind.Middleman, 40));
                PlayerStore.Instance.TryCallNextClient();
                Log.Info($"展示样例（贩子，排队）{_next}/{All.Count}：{c.Name}");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Showcase: queue appraisal case (test)", () => Log.Guard("展示样例（鉴定，排队）", () =>
            {
                var c = All[_next++ % All.Count];
                while (AppraisalRankHelper.GetRank() < 2) AppraisalRankHelper.Promote();
                Override = () => { var it = Make(c, DealerKind.Honest); AppraisalHelper.InitAppraisalItem(it, (int)it.unitValue); return it; };
                PlayerStore.Instance.storeClientManager.AddNextClient(StoreClientListAppraisal.CreateAppraisalRandom());
                PlayerStore.Instance.TryCallNextClient();
                Log.Info($"展示样例（鉴定，排队）{_next}/{All.Count}：{c.Name}");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Showcase: next dealer case (test)", () => Log.Guard("展示样例（贩子）", () =>
            {
                var c = All[_next++ % All.Count];
                var dealer = UsedDealerClient.Create(DealerKind.Middleman, 40);
                PlayerStore.Instance.currentClientInstance = StoreClientInstance.CreateClientInstance(dealer);
                Place(Make(c, DealerKind.Middleman));
                Log.Info($"展示样例（贩子）{_next}/{All.Count}：{c.Name}");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Showcase: next appraisal case (test)", () => Log.Guard("展示样例（鉴定）", () =>
            {
                var c = All[_next++ % All.Count];
                while (AppraisalRankHelper.GetRank() < 2) AppraisalRankHelper.Promote();
                var client = StoreClientListAppraisal.CreateAppraisalRandom();
                PlayerStore.Instance.currentClientInstance = StoreClientInstance.CreateClientInstance(client);
                var item = Make(c, DealerKind.Honest);
                AppraisalHelper.InitAppraisalItem(item, (int)item.unitValue);
                Place(item);
                Log.Info($"展示样例（鉴定）{_next}/{All.Count}：{c.Name}");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Clear desk + half of storage and machine bays (test)", () => Log.Guard("清柜台与一半储物", () =>
            {
                var e = EmporiumEntry.Instance;
                int counter = 0, bays = 0;
                foreach (var inv in new[] { e.backInvinvElement, e.backInvinvElementCounter, e.frontInvinvElement })
                    if (inv != null) foreach (var it in inv.childItems.ToList()) { it.Destroy(); counter++; }
                var storage = e.invElement.childItems.Where(i => i.identifier != null && (i.identifier.StartsWith("storage_bay") || i.identifier.StartsWith("machine_bay"))).ToList();
                foreach (var kind in storage.GroupBy(i => i.identifier))
                {
                    var list = kind.ToList();
                    for (int i = 0; i < list.Count; i += 2) { list[i].Destroy(); bays++; }
                }
                Log.Info($"清空桌面 {counter} 件；储藏区和机器区 {storage.Count} 个里去掉 {bays} 个");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Paper: dump starters (test)", () => Log.Guard("纸张起点", () =>
            {
                var sb = new System.Text.StringBuilder("\n");
                foreach (var st in PaperStarters.All)
                    sb.AppendLine("STARTER " + st.id + " " + Newtonsoft.Json.JsonConvert.SerializeObject(st.data, Newtonsoft.Json.Formatting.None));
                foreach (var id in PaperStarters.BuiltInTemplateIds())
                    sb.AppendLine("TEMPLATE " + id + " " + Newtonsoft.Json.JsonConvert.SerializeObject(PaperStarters.GetBuiltInTemplate(id), Newtonsoft.Json.Formatting.None));
                Log.Info(sb.ToString());
                Log.Info("纸张起点已写出");
            }));
            ModHelper.AddDebugFunction(ModInfo.Id, ModInfo.Name, "Showcase: reset case index (test)", () => _next = 0);
        }
    }
}

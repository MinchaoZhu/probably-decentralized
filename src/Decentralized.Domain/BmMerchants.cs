using System.Collections.Generic;
using System.Linq;

namespace Decentralized.Domain
{
    /// <summary>商家货架上的一项货：游戏物品 id、货的种类、违禁等级（只对违禁品有意义，随黑市声望放开）。</summary>
    public struct BmEntry
    {
        public string Id;
        public GoodKind Kind;
        public int Level;
    }

    /// <summary>
    /// 商家类型的货表。除了矿卡，其余都按玩家能走的路线配货：矿工、酿酒师、化学师、药剂师、枪械师、水培农场主、养殖户、水务、仓储、工具、奢侈品，
    /// 加上违禁品（军火、证件芯片）。大宗商家什么都卖。
    /// </summary>
    public static class Merchants
    {
        private static BmEntry C(string id, int level) => new BmEntry { Id = id, Kind = GoodKind.Contraband, Level = level };
        private static BmEntry T(string id) => new BmEntry { Id = id, Kind = GoodKind.Tech, Level = 3 };
        private static BmEntry G(string id) => new BmEntry { Id = id, Kind = GoodKind.Goods, Level = 0 };
        private static BmEntry[] Gs(params string[] ids) => ids.Select(G).ToArray();

        public static readonly Dictionary<MerchantType, BmEntry[]> Table = new Dictionary<MerchantType, BmEntry[]>
        {
            [MerchantType.Rigs] = new[] { T("pd_rig"), T("energy_credit_ext"), T("energy_credit_hyp"), T("energy_credit_breeder"), T("powerblock"), T("recharger"), T("current_converter"), G("rare_electronic") },
            [MerchantType.Modules] = new[] { T("system_module_overclock"), T("system_module_performance"), T("system_module_quality"), T("system_module_eco"), T("system_module_efficiancy"),
                T("system_module_fineness"), T("system_module_streamlining"), T("module_extractor_advanced"), T("module_bay_expansion_kit") },
            [MerchantType.Arms] = new[] { C("handmade_pistol", 1), C("printed_gun", 1), C("stun_baton", 1), C("revolver", 2), C("heavy_handmade_pistol", 2), C("flashbang_grenade", 2), C("smoke_grenade", 2),
                C("shotgun", 3), C("smg", 3), C("c4", 3), G("combat_knife"), G("combat_machete"), G("hatchet"), G("crowbar"), G("kitchen_cleaver"), G("skinning_knife"), G("stun_gun"), G("tazer") },
            [MerchantType.GunMods] = Gs("silencer2", "laser2", "match_barrel", "trigger_group", "mag_10mm_high", "mag_22_high", "mag_10mm_smg_high", "rds", "compensator", "stock", "grip", "bipod", "barrel",
                "printer_chip_gun_part", "printer_chip_silencer", "printer_chip_handmade_pistol_frame", "printer_chip_heavy_handmade_pistol_frame", "printer_module_cnc", "printer_module_metal", "gun_part", "printer_plastic"),
            [MerchantType.Docs] = new[] { C("eng_keycard", 1), C("med_keycard", 1), C("ser_keycard", 1), C("sup_keycard", 1), C("crypto_module_ser", 1), C("crypto_module_sup", 1),
                C("sci_keycard", 2), C("sec_keycard", 2), C("crypto_module_eng", 2), C("crypto_module_med", 2), C("cmd_keycard", 3), C("crypto_module_sec", 3) },
            [MerchantType.Pharma] = new[] { C("unlicensed_phagimycin_pill", 2), C("dream_dust", 2), C("fanta_pill", 3), C("nightmare_dust", 3), C("zyanide_pill", 2),
                G("black_injector"), G("pure_white_injector"), G("large_purple_injector"), G("pink_injector"), G("pink_alt_injector"), G("blue_blood_bag"), G("surgery_tool"), G("zerochew"), G("phagimycin_pill") },
            [MerchantType.Chem] = new[] { C("restricted_chemical", 2), G("common_chemical"), G("flux_agent"), G("advanced_flux_agent"), G("chem_module"), G("evaporator_module_solvent_recovery"),
                G("chem_scanner"), G("med_bottle_blue"), G("med_bottle_orange"), G("med_bottle_red"), G("med_bottle_small_pink"), G("med_bottle_violet"), G("100ml_cylinder"), G("5ml_dropper") },
            [MerchantType.Brewer] = new[] { G("wine_yeast"), G("wine_superyeast"), G("wine_yeast_infinite"), C("wine_yeast_red", 1), G("wine_berry"), G("bloomberry_seed"), G("nudka"), G("red_beer"), G("beerCase"), G("wine_bottle") },
            [MerchantType.Farm] = new[] { G("nutrifruit_seed"), G("kotton_seed"), G("fillerweed_seed"), G("hydroreed_seed"), G("bloomberry_seed"), C("dream_cap_seed", 1),
                G("farm_module_nutrifruit"), G("farm_module_kotton_loom"), G("farm_module_fillerweed_production"), G("farm_module_hydroreed_filter"), G("farm_module_dream_cap"), G("farm_module_smuggler"),
                G("plant_analyser"), G("plant_dna_disk"), G("nutrient_tablet"), G("hydroreed_filter") },
            [MerchantType.Husbandry] = Gs("tilapia_roe", "sturgeon_roe", "caviar", "ras", "serum_green", "serum_red",
                "livestock_feed_dispenser", "auto_butcher", "industrial_fish_feed", "animal_cage"),
            [MerchantType.Miner] = Gs("common_ore", "rare_ore", "metal_ingot", "scrap_metal", "common_electronic", "rare_electronic", "hypertech_electronic", "metal_scanner", "wire", "advanced_flux_agent"),
            [MerchantType.Water] = Gs("water_filter_adv", "portable_water_purifier", "water_tank", "uv_filter", "water_tablet_container", "water_test_container", "aquascan", "bottled_water_premium", "water_jug", "water_ration"),
            [MerchantType.Storage] = Gs("smuggler_bay", "smuggler_bay_mod", "smuggler_bay_mini", "storage_bay_large", "storage_bay_expansion_kit", "hypertech_bay", "machine_bay_ext", "storage_bay_chem",
                "storage_bay_gun", "backpack_large_military"),
            [MerchantType.Tools] = Gs("turbo_booster", "turbo_booster_adv", "turbo_booster_hypertech", "welder", "xray_scanner", "magnifier", "labeler_blue", "toolbox", "wire_cutter", "mind_reprogrammer"),
            [MerchantType.Luxury] = new[] { C("pheromone_perfume", 2), G("neuroactive_perfume"), G("self_refilling_neuroactive_perfume"), G("skincare_cream"), G("packet_red_cigarette"), G("gamblers_coin"), G("kotton_fabric"), G("armor_lining") },
        };

        /// <summary>
        /// 各类商家收的币：硬件和技术类收 PSC，违禁类收 CRT 和隐私币，农业养殖水务收 PSC 和 CRA，个人卖家收 CRT 和隐私币。
        /// 平台现货收 PSC、CRT、CRA。不在表里的币要先换：Web3 内置的自动换币要多付滑点（<see cref="Tuning.BmAutoSwapSlip"/>），也可以自己去 DEX 换。
        /// </summary>
        public static readonly string[] PlatformAccepts = { Assets.PSC, Assets.CRT, Assets.CRA };

        public static string[] Accepts(MerchantType type)
        {
            switch (type)
            {
                case MerchantType.Rigs: case MerchantType.Modules: case MerchantType.Miner: case MerchantType.Tools: case MerchantType.Storage:
                case MerchantType.GpuNew: case MerchantType.GpuUsed: case MerchantType.Asic: case MerchantType.Elite: case MerchantType.Bulk:
                    return new[] { Assets.PSC, Assets.CRT };
                case MerchantType.Arms: case MerchantType.GunMods: case MerchantType.Docs: case MerchantType.Pharma: case MerchantType.Chem:
                case MerchantType.Luxury: case MerchantType.Personal:
                    return new[] { Assets.CRT, Assets.Ponero };
                default:   // Brewer、Farm、Husbandry、Water
                    return new[] { Assets.PSC, Assets.CRA };
            }
        }

        private static readonly Dictionary<string, BmEntry> ById = BuildIndex();

        private static Dictionary<string, BmEntry> BuildIndex()
        {
            var d = new Dictionary<string, BmEntry>();
            foreach (var e in Table.Values.SelectMany(x => x)) d[e.Id] = e;
            foreach (var kv in BlackMarket.Pool) d[kv.Key] = new BmEntry { Id = kv.Key, Kind = GoodKind.Contraband, Level = kv.Value };
            foreach (var id in BlackMarket.TechPool) d[id] = new BmEntry { Id = id, Kind = GoodKind.Tech, Level = 3 };
            return d;
        }

        /// <summary>这个物品 id 在黑市上是不是一件货（现货柜台、商家货架里出现过的，都能转手出售）。</summary>
        public static bool IsGood(string id) => ById.ContainsKey(id);

        public static BmEntry Info(string id) => ById[id];
    }
}

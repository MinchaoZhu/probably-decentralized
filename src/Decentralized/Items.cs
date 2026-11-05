using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Decentralized.Domain;
using UnityEngine;

namespace Decentralized
{
    /// <summary>
    /// mod 物品：交易终端、矿架、显卡（每个型号一个物品 id）。显卡的健康度、假卡、翻新、烧毁存在物品标签上，随存档保存。
    /// 读档时游戏按 id 重新调用工厂，再把存档里的标签与子物品（矿架里的显卡、电池）恢复回去。
    /// </summary>
    public static class Items
    {
        public const string Terminal = "pd_terminal";
        public const string Rig = "pd_rig";
        public const string Guide = "pd_gpu_guide";
        public const string GpuPrefix = "pd_gpu_";
        public const string HwWallet = "pd_hw_wallet";
        public const string KeyDrive = "pd_key_drive";
        /// <summary>硬件钱包被动过手脚（供应链投毒）。外观、说明都不显示。</summary>
        public const string PoisonTag = "PD_POISONED";

        public const string HealthTag = "PD_HEALTH";
        public const string FakeTag = "PD_FAKE_OF";
        public const string RefurbTag = "PD_REFURBISHED";
        public const string DeadTag = "PD_DEAD";
        public const string LookTag = "PD_LOOK", BackTag = "PD_BACK", SeedTag = "PD_SEED", ClaimTag = "PD_CLAIM", ExposedTag = "PD_EXPOSED", MarkupTag = "PD_MARKUP", EditedTag = "PD_EDITED";

        public static Action OpenTerminal;
        public static Action OpenGpuHandbook;
        /// <summary>硬件钱包拖到交易终端上：添加一个钱包（参数：是否被投毒）。</summary>
        public static Action<bool> AddWallet;
        public static Func<bool> CanAddWallet;

        public static void Subscribe()
        {
            ModHook.OnModItemDirectoryInit += Register;
            ModHook.OnLeavingStoreEarly += SweepFront;
            ModHook.OnGoingSleepEarly += SweepFront;
        }

        public static void Unsubscribe()
        {
            ModHook.OnModItemDirectoryInit -= Register;
            ModHook.OnLeavingStoreEarly -= SweepFront;
            ModHook.OnGoingSleepEarly -= SweepFront;
        }

        public static string GpuId(string model) => GpuPrefix + model.ToLowerInvariant();

        public static bool IsGpu(GameItem item) => item != null && item.identifier != null && item.identifier.StartsWith(GpuPrefix);

        private static void Register(ModItemDirectory dir)
        {
            dir.Add(Terminal, CreateTerminal);
            dir.Add(Rig, CreateRig);
            WrapElectronics(dir);
            WrapTurbos(dir);
            dir.Add(Guide, CreateGuide);
            dir.Add(HwWallet, CreateHwWallet);
            dir.Add(KeyDrive, CreateKeyDrive);
            AsicItems.Register(dir);
            foreach (var spec in Hardware.Specs.Values.Where(s => !s.Asic && !s.Scrap))
            {
                var s = spec;
                dir.Add(GpuId(s.Model), () => CreateGpu(s));
            }
        }

        // ---------- 交易终端 ----------

        private static GameItem CreateTerminal()
        {
            var item = ItemDirectory.CreateEmptyItem(Terminal);
            item.SetSpriteAndShapeFromMod(ModInfo.Id, "terminal");
            item.name = L.T("item.terminal.name");
            item.shortDescription = L.T("item.terminal.desc");
            item.unitValue = 0;
            item.EnableTag(GeneralHelper.IMPORTANT_TAG);
            item.EnableTag(TypeHelper.NOT_FOR_RESALE);
            item.mayActivateSlotItemFunc = (_, _, _) => true;
            item.canActivateSlotItemFunc = (_, _, _) => true;
            item.onActivateSlotItemFunc = (_, _, _) => Log.Guard("打开交易终端", () => OpenTerminal?.Invoke());
            ModHelper.SetSource(item, ModInfo.Id, ModInfo.Name);
            return item;
        }

        /// <summary>显卡鉴定手册：型号对照、序列号规则、外观折扣。做法同游戏里的鉴定手册（海明威鉴定指南）：纸张图、文档类型、拖放音效。</summary>
        private static GameItem CreateGuide()
        {
            var item = ItemDirectory.CreateEmptyItem(Guide);
            item.SetSpriteAndShape("Items/items_misc", "guide1");
            item.EnableTag("DOCUMENT");
            item.SetGameItemType("DOCUMENT");
            item.EnableTag("GUIDE_PAPER_TAG");
            BookHelper.InitPaperGuide(item);
            item.name = L.T("item.guide.name");
            item.shortDescription = L.T("item.guide.desc");
            item.mayActivateSlotItemFunc = (_, _, _) => true;
            item.canActivateSlotItemFunc = (_, _, _) => true;
            item.onActivateSlotItemFunc = (_, _, _) => Log.Guard("打开鉴定手册", () => OpenGpuHandbook?.Invoke());
            item.unitValue = 50;
            item.EnableTag(GeneralHelper.IMPORTANT_TAG);
            item.EnableTag(TypeHelper.NOT_FOR_RESALE);
            ModHelper.SetSource(item, ModInfo.Id, ModInfo.Name);
            return item;
        }

        /// <summary>
        /// PLEDGER 硬件钱包：和游戏里的升级材料一样，拖到交易终端上用掉，钱包 +1。杰克逊博士卖的是正品；
        /// Web3 商城卖的便宜一些，外观和说明完全一样，其中一部分被动过手脚（<see cref="PoisonTag"/>）。
        /// </summary>
        private static GameItem CreateHwWallet()
        {
            var item = ItemDirectory.CreateEmptyItem(HwWallet);
            item.SetSpriteAndShapeFromMod(ModInfo.Id, "hw_wallet");
            item.name = L.T("item.hwwallet.name");
            item.shortDescription = L.T("item.hwwallet.desc");
            item.unitValue = Tuning.WalletJacksonPrice;
            item.SetGameItemType("MATERIAL");
            item.maySelectSlotItemFunc = (_, _, _) => true;
            item.canSelectSlotItemFunc = (_, _, _) => true;
            item.mayThisTargetItemFunc = (Func<GameItem, GameItem, bool>)Delegate.Combine(item.mayThisTargetItemFunc, (Func<GameItem, GameItem, bool>)((s, t) => CanTarget(t)));
            item.canThisTargetItemFunc = (Func<GameItem, GameItem, bool>)Delegate.Combine(item.canThisTargetItemFunc, (Func<GameItem, GameItem, bool>)((s, t) => CanTarget(t)));
            item.onThisTargetItemFunc = (Action<GameItem, GameItem>)Delegate.Combine(item.onThisTargetItemFunc, (Action<GameItem, GameItem>)((s, t) => Log.Guard("添加硬件钱包", () =>
            {
                if (!CanTarget(t)) return;
                bool poisoned = s.IsTag(PoisonTag);
                s.Destroy();
                AddWallet?.Invoke(poisoned);
            })));
            ModHelper.SetSource(item, ModInfo.Id, ModInfo.Name);
            return item;
        }

        /// <summary>Tumbler 的密钥盘：违禁品（高），搜查时被翻出来按价值记罪证。</summary>
        private static GameItem CreateKeyDrive()
        {
            var item = ItemDirectory.CreateEmptyItem(KeyDrive);
            item.SetSpriteAndShapeFromMod(ModInfo.Id, "key_drive");
            item.name = L.T("item.keydrive.name");
            item.shortDescription = L.T("item.keydrive.desc");
            item.unitValue = Tuning.TumblerDriveValue;
            ContrabandHelper.InitContrabandItem(item, 3);
            ModHelper.SetSource(item, ModInfo.Id, ModInfo.Name);
            return item;
        }

        private static bool CanTarget(GameItem t) => t != null && t.identifier == Terminal && (CanAddWallet?.Invoke() ?? false);

        public static GameItem NewHwWallet(bool poisoned)
        {
            var item = ModHelper.SpawnItem(HwWallet);
            if (item != null && poisoned) item.EnableTag(PoisonTag);
            return item;
        }

        // ---------- 显卡 ----------

        /// <summary>RTX70 → "RTX 70"，RX800 → "RX 800"。</summary>
        public static string DisplayModel(string model) => Regex.Replace(Regex.Replace(model, @"(?<=[A-Za-z])(?=\d)", " "), @"(?<=\d)(?=[A-Z])", " ");

        private static GameItem CreateGpu(HardwareSpec spec)
        {
            var item = ItemDirectory.CreateEmptyItem(GpuId(spec.Model));
            item.SetGameItemType("MATERIAL");
            InspectableHelper.InitInspectableItem(item);
            item.onLoaded = Refresh;
            // 右键"切换"翻面；外观差异用叠加图画在同一张底图上
            item.EnableTag(ToggleAppearanceHelper.TOGGLE_APPEARANCE_TAG);
            item.mayToggleSlotItemFunc = (_, _, _) => true;
            item.canToggleSlotItemFunc = (_, _, _) => true;
            item.onToggleSlotItemFunc = (toggled, _, _) => Log.Guard("显卡翻面", () =>
            {
                Flip(toggled);
            });
            item.getItemSpriteModifierForItemFunc = Overlays;
            // 摆给顾客的时候，顾客可能看出说法与实物对不上
            item.onSlotAddItemFunc = (Action<GameItem, GameInventory, SlotMarker>)Delegate.Combine(item.onSlotAddItemFunc,
                (Action<GameItem, GameInventory, SlotMarker>)((it, _, __) => Log.Guard("顾客验货", () => OnOffered(it))));
            ModHelper.SetSource(item, ModInfo.Id, ModInfo.Name);
            Refresh(item);
            return item;
        }

        /// <summary>按给定的状态造一张卡（二手卡、假卡）：写入标签并生成外观。</summary>
        public static GameItem NewCard(string model, double health, string fakeOf, Rng rng, DealerKind? seller = null, int markupPct = 0)
        {
            var item = ModHelper.SpawnItem(GpuId(model));
            var u = new Unit { Model = model, FakeOf = fakeOf, Health = health, Look = CardLook.Generate(rng, health, fakeOf != null, Hardware.Specs[model].Tier, fakeOf != null ? Hardware.Specs[fakeOf].Tier : Hardware.Specs[model].Tier) };
            if (health >= 100 && fakeOf == null) u.Look.Sticker = 0;   // 全新卡：封条完好
            Store(item, u);
            if (seller != null)
            {
                GeneralHelper.SetItemOwned(item, false);   // 卖家的货：买下之前不属于玩家，说法才有效
                SetClaim(item, CardLabels.DealerClaim(u, seller.Value, rng), 0, markupPct);
            }
            return item;
        }

        // ---------- 标签栏：卖家的说法与指出 ----------

        private static object checkedClient;
        private static readonly HashSet<int> checkedItems = new HashSet<int>();

        /// <summary>被顾客看穿时的信誉损失：骗的价差每满一周房租扣 1 点，最少 1，最多 25。</summary>
        public static int RepLoss(long gap) => (int)Math.Max(1, Math.Min(Tuning.RepLossMax, Math.Round(gap / (double)Math.Max(1, PlayerStore.Instance.rentValue))));

        /// <summary>
        /// 自己的卡摆给想买的顾客：说法里隐瞒的每一项，顾客有 50% 的概率看出来（型号档次不符的必看出来，供电口和风扇数一眼可见）；看出来就拆穿这一项（价格回到真实情况），
        /// 并且掉一点信誉。每位顾客对每张卡只看一次。说法比实际更差（把好卡说差了）顾客不会去指。
        /// </summary>
        public static void OfferForTest(GameItem item) { checkedItems.Clear(); OnOffered(item, true); }

        private static void OnOffered(GameItem item, bool force = false)
        {
            if (item.IsTag(DeadTag) || !GeneralHelper.IsItemOwned(item) || (!force && !item.IsRootedInBackInv())) return;
            var ci = PlayerStore.Instance.currentClientInstance;
            var client = ci?.GetClientBlueprint();
            if (client == null || (client.clientIntent != StoreClient.ClientIntent.BUY && client.clientIntent != StoreClient.ClientIntent.SELLNBUY)) return;
            var claim = GetClaim(item);
            if (claim == null) return;
            if (!ReferenceEquals(checkedClient, ci)) { checkedClient = ci; checkedItems.Clear(); }
            if (!checkedItems.Add(item.uniqueId)) return;
            var u = ToUnit(item);
            int exposed = Exposed(item);
            int lies = CardLabels.Lies(u, claim.Value, exposed);
            if (lies == 0) return;
            var rng = Session.Data.Rng;
            int found = 0;
            foreach (int r in CardLabels.Rows)
                if ((lies & r) != 0 && rng.Chance((r == CardLabels.RowCount || r == CardLabels.RowPin) ? Tuning.CustomerSpotTierMismatch : Tuning.CustomerSpotChance))
                {
                    found |= r;
                    if (r != CardLabels.RowSpec && CardLabels.IsEvidence(r, claim.Value, Claim.Truth(u))) found |= CardLabels.RowSpec | CardLabels.RowCount | CardLabels.RowPin | CardLabels.RowPrint | CardLabels.RowSerial;
                }
            if (found == 0) return;
            long before = item.unitValue;
            item.ModifyTag(ExposedTag, t => t.Enable().SetInt(exposed | found));
            Refresh(item);
            int rep = RepLoss(before - item.unitValue);
            string faction = string.IsNullOrEmpty(client.clientFaction) || StoreReputation.GetStoreReputation(client.clientFaction) == null ? "FACTION_LOWER_LEVEL" : client.clientFaction;
            StoreReputation.ModReputation(faction, -rep);
            EmporiumAudioPlayer.Instance?.audioFail?.Play();
            Log.Info($"顾客看出标签不实：{found}，价格 {item.unitValue}，信誉 −{rep}");
            DialogUIManager.Instance.StartDialogue(new Dialogue().SetText("CLIENT", L.T((found & CardLabels.RowSpec) != 0 ? "cust.fake." + rng.Range(1, 2) : "cust.worn." + rng.Range(1, 2))));
        }

        /// <summary>标签上的说法；没有说法的卡（自己挖出来、正规渠道的新卡）一律按真实情况算。</summary>
        public static Claim? GetClaim(GameItem item)
        {
            if (!item.IsTag(ClaimTag)) return null;
            var c = Claim.Decode(item.GetTagReadonly(ClaimTag).GetString());
            return string.IsNullOrEmpty(c.Model) ? (Claim?)null : c;
        }

        /// <summary>已指出的标签。型号一旦被拆穿，四个真伪行（型号、供电与风扇、序列号、丝印）一起亮出真实情况。</summary>
        public static int Exposed(GameItem item)
        {
            int e = item.IsTag(ExposedTag) ? item.GetTagReadonly(ExposedTag).GetInt() : 0;
            if ((e & CardLabels.RowSpec) != 0) e |= CardLabels.RowSpec | CardLabels.RowCount | CardLabels.RowPin | CardLabels.RowSerial | CardLabels.RowPrint;
            return e;
        }

        public static void SetClaim(GameItem item, Claim claim, int exposed, int markupPct = 0)
        {
            item.ModifyTag(ClaimTag, t => t.Enable().SetString(claim.Encode()));
            item.ModifyTag(ExposedTag, t => t.Enable().SetInt(exposed));
            if (markupPct != 0) item.ModifyTag(MarkupTag, t => t.Enable().SetInt(markupPct));
            Refresh(item);
        }

        /// <summary>现在标签上写的型号：说法里的型号，被拆穿之后是真实芯片。</summary>
        public static string BelievedModel(GameItem item, Unit u)
        {
            var c = GetClaim(item);
            if (c == null) return u.Model;
            return (Exposed(item) & CardLabels.RowSpec) != 0 ? (u.FakeOf ?? u.Model) : c.Value.Model;
        }

        /// <summary>玩家指出一个标签：记下来，说法改回真实情况；返回 0 说法属实、1 隐瞒了不利的情况、2 拆穿假货。</summary>
        public static int Point(GameItem item, int row)
        {
            var u = ToUnit(item);
            var claim = GetClaim(item) ?? Claim.Truth(u);
            int exposed = Exposed(item);
            int kind = CardLabels.Point(u, claim, row, ref exposed);
            item.ModifyTag(ExposedTag, t => t.Enable().SetInt(exposed));
            if (!item.IsTag(ClaimTag)) item.ModifyTag(ClaimTag, t => t.Enable().SetString(claim.Encode()));
            Refresh(item);
            if (item.IsTag("APPRAISAL_ITEM")) AppraisalHelper.SetDefaultAppraisalValue(item, (int)item.unitValue);
            return kind;
        }

        /// <summary>用标签机改说法（随便改）：这一项重新算作"没人指出过"。</summary>
        public static void EditClaim(GameItem item, int row, int value, string model = null)
        {
            var u = ToUnit(item);
            var claim = GetClaim(item) ?? Claim.Truth(u);
            claim = row == CardLabels.RowSpec ? new Claim { Model = model ?? claim.Model, Fan = claim.Fan, Fingers = claim.Fingers, Bracket = claim.Bracket, Print = claim.Print, Sticker = claim.Sticker } : claim.With(row, value);
            int exposed = Exposed(item) & ~row;
            item.EnableTag(EditedTag);
            SetClaim(item, claim, exposed);
        }

        /// <summary>说明：算力和耗电按标称写；成色在归玩家之前只给粗略等级（要看外观自己判断），归玩家之后给精确健康度。</summary>
        private static string Desc(GameItem item, Unit u)
        {
            var label = Hardware.Specs[BelievedModel(item, u)];
            string cond = GeneralHelper.IsItemOwned(item)
                ? Mathf.RoundToInt((float)u.Health) + "%"
                : L.T(u.Health >= 85 ? "item.gpu.grade.1" : u.Health >= 60 ? "item.gpu.grade.2" : u.Health >= 35 ? "item.gpu.grade.3" : "item.gpu.grade.4");
            return L.T("item.gpu.desc", Fmt.Num(label.DisplayHash), Fmt.Num(label.Kj), cond);
        }

        private static void SyncValue(GameItem item)
        {
            if (item.IsTag(DeadTag)) return;
            // 买下之后卖家的说法作废：卡回到真实情况，除非玩家用标签机自己改过
            if (GeneralHelper.IsItemOwned(item) && item.IsTag(ClaimTag) && !item.IsTag(EditedTag))
            {
                item.ModifyTag(ClaimTag, t => t.Disable());
                item.ModifyTag(ExposedTag, t => t.Disable());
                item.ModifyTag(MarkupTag, t => t.Disable());
            }
            var u = ToUnit(item);
            string desc = Desc(item, u);
            if (item.shortDescription != desc) item.shortDescription = desc;
            string name = L.T("item.gpu.name", Hardware.Specs[BelievedModel(item, u)].Brand, DisplayModel(BelievedModel(item, u)));
            if (item.name != name) item.name = name;
            long value = CardLabels.Value(u, GetClaim(item), Exposed(item));
            if (!GeneralHelper.IsItemOwned(item) && item.IsTag(MarkupTag)) value = Math.Max(1, (long)Math.Round(value * (1 + item.GetTagReadonly(MarkupTag).GetInt() / 100.0)));
            if (item.unitValue != value) item.unitValue = value;
        }

        public static Unit ToUnit(GameItem item)
        {
            bool asic = AsicItems.Is(item);
            string model = item.identifier.Substring(asic ? AsicItems.Prefix.Length : GpuPrefix.Length);
            model = Hardware.Specs.Keys.FirstOrDefault(k => k.ToLowerInvariant() == model) ?? (asic ? "M1" : "RTX60");
            var u = new Unit { Model = model, Health = 100 };
            if (asic)
            {
                // 矿机没有外观、没有真假：不记外观种子，算力、损耗、暴毙都按规格表
                if (item.IsTag(HealthTag)) u.Health = item.GetTagReadonly(HealthTag).GetDouble();
                u.Dead = item.IsTag(DeadTag);
                return u;
            }
            if (item.IsTag(HealthTag)) u.Health = item.GetTagReadonly(HealthTag).GetDouble();
            if (item.IsTag(FakeTag)) u.FakeOf = item.GetTagReadonly(FakeTag).GetString();
            u.Refurbished = item.IsTag(RefurbTag);
            u.Dead = item.IsTag(DeadTag);
            if (item.IsTag(LookTag))
            {
                var enc = item.GetTagReadonly(LookTag).GetString();
                u.Look = CardLook.Legacy(enc, CardLook.Decode(enc), u.Model, u.FakeOf);   // 旧存档的假卡补上风扇数、供电口破绽
            }
            // 外观种子：没有记录的卡（正规渠道的新卡）用物品编号当种子，之后同样随损耗变旧
            u.Look.Seed = item.IsTag(SeedTag) ? (uint)item.GetTagReadonly(SeedTag).GetInt() : (uint)(item.uniqueId * 2654435761u) | 1u;
            return u;
        }

        public static void Store(GameItem item, Unit u)
        {
            item.ModifyTag(HealthTag, s => s.Enable().SetDouble(u.Health));
            if (u.FakeOf != null) item.ModifyTag(FakeTag, s => s.Enable().SetString(u.FakeOf));
            if (u.Refurbished) item.EnableTag(RefurbTag);
            if (u.Dead) item.EnableTag(DeadTag);
            if (!AsicItems.Is(item))
            {
                item.ModifyTag(LookTag, s => s.Enable().SetString(u.Look.Encode()));
                item.ModifyTag(SeedTag, s => s.Enable().SetInt((int)u.Look.Seed));
            }
            Refresh(item);
        }

        /// <summary>按标签更新名字、说明、价值和图：显示的是标签上的型号（假卡也显示被冒充的型号）。</summary>
        public static void Refresh(GameItem item)
        {
            if (AsicItems.Is(item)) { AsicItems.Refresh(item); return; }
            var u = ToUnit(item);
            var label = u.Label;
            if (u.Dead)
            {
                item.name = L.T("item.gpu.burnt.name", label.Brand, DisplayModel(label.Model));
                item.shortDescription = L.T("item.gpu.burnt.desc");
                item.unitValue = Math.Max(1, label.Price / 20);
                item.SetSpriteAndShapeFromMod(ModInfo.Id, item.IsTag(BackTag) ? "gpu_burnt_b" : "gpu_burnt_f");
                return;
            }
            item.SetSpriteAndShapeFromMod(ModInfo.Id, LookKeys(item, new List<string>()));
            SyncValue(item);
        }

        public static bool IsBack(GameItem item) => item.IsTag(BackTag);

        public static void Flip(GameItem item)
        {
            if (IsBack(item)) item.ModifyTag(BackTag, t => t.Disable()); else item.EnableTag(BackTag);
            Refresh(item);
        }

        /// <summary>当前面的底图名与叠加图名：正面三个维度，背面丝印与贴纸；烧毁的卡不叠。</summary>
        /// <summary>实物上的风扇数：由真实芯片的档次决定（低档次的板子套高档次的壳，风扇数露馅）。</summary>
        public static int FansOf(Unit u) => u.ObservedCount;

        public static string LookKeys(GameItem item, List<string> overlays)
        {
            var u = ToUnit(item);
            var l = u.Appearance;
            bool back = IsBack(item);
            string brand = u.Label.Brand == Hardware.Probidia ? "gpu_probidia" : "gpu_pmd";
            if (u.Dead) return back ? "gpu_burnt_b" : "gpu_burnt_f";
            if (!back)
            {
                if (l.Fan > 0) overlays.Add($"gpu_ov_fan{l.Fan}_{FansOf(u)}");
                if (l.Fingers > 0) overlays.Add("gpu_ov_fin" + l.Fingers);
                if (l.Bracket > 0) overlays.Add("gpu_ov_brk1");
            }
            else
            {
                // 丝印：0 标称型号；1 印的是真实芯片型号；2 标称型号但字歪（重印过）
                string model = l.Print == 1 && u.FakeOf != null ? u.FakeOf : u.Model;
                overlays.Add((l.Print == 2 ? "gpu_ov_txs_" : "gpu_ov_txt_") + model.ToLowerInvariant());
                if (l.Sticker == 2) overlays.Add("gpu_ov_stk2");
            }
            return brand + (back ? "_b" : $"_f_p{u.ObservedPin}n{u.ObservedCount}");
        }

        public static Sprite ModSprite(string key)
        {
            var mod = ModLoader.Mods.Find(m => m.Manifest.ID.Equals(ModInfo.Id));
            return mod != null && mod.Sprites.TryGetValue(key, out var sprite) ? sprite : null;
        }

        /// <summary>物品栏里的外观叠加图。</summary>
        private static List<ItemSpriteModifier> Overlays(GameItem item)
        {
            var list = new List<ItemSpriteModifier>();
            SyncValue(item);   // 归属变化（买下）后价格随之变回真实价值
            if (item.IsTag(DeadTag)) return list;
            var keys = new List<string>();
            LookKeys(item, keys);
            int priority = 0;
            foreach (var k in keys)
            {
                var sprite = ModSprite(k);
                if (sprite != null) list.Add(new ItemSpriteModifier(sprite, Vector2.zero, 0, false, priority++, false, Color.white));
            }
            return list;
        }

        // ---------- 矿架 ----------

        public const string SlotsTag = "PD_SLOTS", ProgressTag = "PD_SLOT_PROGRESS";

        private static int SlotsOf(GameItem rig) => rig.IsTag(SlotsTag) ? rig.GetTagReadonly(SlotsTag).GetInt() : RigSlots.Base;

        private static GameItem CreateRig()
        {
            var battery = new GameSlotInventory();
            var modules = new GameGridInventory(MachineHelper.DEFAULT_MODULE_BAY_CONFIG, 7);
            // 显卡每张 3×2 格：3 格宽的格子，每张卡 3×2 格，每个槽两行；未解锁的行不可放
            var cards = new GameGridInventory(RigSlots.Shape(RigSlots.Base), RigSlots.CellWidth);
            // 状态面板：显卡数、耗电、算力（只读）
            var stCount = new MachineryText("", 6, TMPro.HorizontalAlignmentOptions.Left, false);
            var stPower = new MachineryText("", 6, TMPro.HorizontalAlignmentOptions.Left, false);
            var stHash = new MachineryText("", 6, TMPro.HorizontalAlignmentOptions.Left, false);
            GameItem rigItem = null;
            var status = new MachineryStack(true, 6).Add(stCount).Add(stPower).Add(stHash).Add(Payout.Build(() => rigItem, out var stAddr));
            var window = MachineHelper.CreateMachineryWindow(true,
                MachineHelper.PowerPanel(battery),
                MachineHelper.Panel(L.T("rig.label.modules"), modules, "module"),
                MachineHelper.Panel(L.T("rig.label.cards"), cards, "cards"),
                MachineHelper.Panel(L.T("rig.label.status"), status, "status", false));
            var rig = ItemDirectory.CreateEmptyItem(Rig).SetContentWindow(window);
            rig.SetSpriteAndShapeFromMod(ModInfo.Id, "rig");
            rigItem = rig;
            RigStatus.Add(rig, new[] { stCount, stPower, stHash });
            Payout.Register(rig, stAddr);
            RigInv.Remove(rig); RigInv.Add(rig, Tuple.Create(cards, (GameInventory)modules));
            rig.name = L.T("item.rig.name");
            rig.unitValue = Tuning.RigPrice;
            MachineHelper.SetupBatterySlot(battery, rig);
            rig.EnableTag("CONTAINER_TAG");
            rig.EnableTag("STANDARD_MACHINE_TAG");
            MachineryHelper.InitMachinery(rig, Tuning.RigKj);
            // 游戏的通用模块槽（含模块相邻效果）；模块变动后游戏会按原版公式改写耗电，再由 UpdateRig 按本 mod 的规则重算
            MachineHelper.SetupModuleBay(modules, rig, () => UpdateRig(rig, modules, cards));
            cards.mayInventoryAddItemFunc = (item, _) => GeneralHelper.IsItemOwned(item) && IsGpu(item) && !item.IsTag(DeadTag);
            cards.onSlotAddItemEndFunc = (_, _, _) => UpdateRig(rig, modules, cards);
            cards.onSlotRemoveItemEndFunc = (_, _, _) => UpdateRig(rig, modules, cards);
            rig.onLoaded = _ => { ApplySlots(rig, cards); UpdateRig(rig, modules, cards); };
            rig.onCycleEndEarlySlotItemFunc = (_, _, _) => Log.Guard("矿架扣电", () => NightDraw(rig, battery, modules, cards));
            ModHelper.SetSource(rig, ModInfo.Id, ModInfo.Name);
            ApplySlots(rig, cards);
            UpdateRig(rig, modules, cards);
            return rig;
        }

        /// <summary>按标签里的槽数设置显卡格子的形状（游戏自己扩展模块槽也是 SetShape 加 Validate）。</summary>
        private static void ApplySlots(GameItem rig, GameGridInventory cards)
        {
            cards.SetShape(RigSlots.Shape(SlotsOf(rig)), RigSlots.CellWidth);
            rig.contentWindow?.Validate();
        }

        /// <summary>
        /// 电子元件作用在矿架上：游戏按“材料作用于目标”判断，元件自己的判断函数对矿架一律为否（三方判断取与），
        /// 所以要在元件上加一个对矿架为真的判断。用原版目录里的工厂造元件，再补上这三个函数。
        /// </summary>
        private static void WrapElectronics(ModItemDirectory dir)
        {
            try
            {
                foreach (var id in new[] { "common_electronic", "rare_electronic", "hypertech_electronic" })
                {
                    var vanilla = DirectoryMaster.GetFactory<GameItem>(id);
                    if (vanilla == null) { Log.Warn($"[Rig] 找不到原版 {id}"); continue; }
                    var make = vanilla;
                    dir.Set(id, () =>
                    {
                        var it = make();
                        Func<GameItem, GameItem, bool> can = (src, t) => t != null && ((t.identifier == Rig && CanUpgradeWith(t, src)) || AsicItems.CanRepairWith(t, src));
                        it.mayThisTargetItemFunc = (Func<GameItem, GameItem, bool>)Delegate.Combine(it.mayThisTargetItemFunc, can);
                        it.canThisTargetItemFunc = (Func<GameItem, GameItem, bool>)Delegate.Combine(it.canThisTargetItemFunc, can);
                        it.onThisTargetItemFunc = (Action<GameItem, GameItem>)Delegate.Combine(it.onThisTargetItemFunc, (Action<GameItem, GameItem>)((src, t) =>
                        {
                            if (t != null && t.identifier == Rig && CanUpgradeWith(t, src)) Log.Guard("矿架升级槽位", () => TakeUpgrade(t, src));
                            else if (AsicItems.CanRepairWith(t, src)) Log.Guard("矿机修理", () => AsicItems.TakeRepair(t, src));
                        }));
                        return it;
                    });
                    Log.Info($"[Rig] 已给 {id} 加上升级矿架的作用");
                }
            }
            catch (Exception ex) { Log.Error("[Rig] 包装电子元件失败：" + ex); }
        }

        /// <summary>
        /// 涡轮增压器不能作用于矿架和 ASIC 矿机：它们带"标准机器"标签，原版涡轮只看这个标签，还会手动触发目标的夜间结算（等于白天多扣一次电）。
        /// 在原版涡轮工厂外包一层，对这两类目标返回否；是替换判断函数而不是追加（追加是"或"，原来的"是"还会生效）。
        /// </summary>
        private static void WrapTurbos(ModItemDirectory dir)
        {
            foreach (var id in new[] { "turbo_booster", "turbo_booster_adv", "turbo_booster_hypertech" })
            {
                var make = DirectoryMaster.GetFactory<GameItem>(id);
                if (make == null) { Log.Warn($"[Rig] 找不到原版 {id}"); continue; }
                dir.Set(id, () =>
                {
                    var it = make();
                    var may = it.mayThisTargetItemFunc;
                    var can = it.canThisTargetItemFunc;
                    it.mayThisTargetItemFunc = (s, t) => !IsMiner(t) && (may?.Invoke(s, t) ?? false);
                    it.canThisTargetItemFunc = (s, t) => !IsMiner(t) && (can?.Invoke(s, t) ?? false);
                    return it;
                });
            }
        }

        private static bool IsMiner(GameItem t) => t != null && (t.identifier == Rig || AsicItems.Is(t));

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameItem, Tuple<GameGridInventory, GameInventory>> RigInv = new System.Runtime.CompilerServices.ConditionalWeakTable<GameItem, Tuple<GameGridInventory, GameInventory>>();

        private static bool CanUpgradeWith(GameItem rig, GameItem src)
        {
            var need = RigSlots.Next(SlotsOf(rig));
            return src != null && rig.identifier == Rig && need != null && src.identifier == need.Value.Item && GeneralHelper.IsItemOwned(src);
        }

        private static void TakeUpgrade(GameItem rig, GameItem item)
        {
            if (!RigInv.TryGetValue(rig, out var inv)) return;
            var cards = inv.Item1; var modules = inv.Item2;
            int slots = SlotsOf(rig);
            int progress = rig.IsTag(ProgressTag) ? rig.GetTagReadonly(ProgressTag).GetInt() : 0;
            int before = slots;
            int took = RigSlots.Insert(ref slots, ref progress, item.identifier, 1);
            if (took <= 0) return;
            rig.ModifyTag(SlotsTag, s => s.Enable().SetInt(slots));
            rig.ModifyTag(ProgressTag, s => s.Enable().SetInt(progress));
            // 消耗收下的件数；放不下的部分留在升级槽里（玩家取回）
            if (item.unitCount <= 1) item.Destroy(); else item.GetSplitItem(1).Destroy();
            if (slots != before)
            {
                ApplySlots(rig, cards);
                EmporiumAudioPlayer.Instance?.ratchet?.Play();
            }
            UpdateRig(rig, (GameGridInventory)modules, cards);
        }

        public static List<GameItem> Cards(GameInventory cards) => cards.childItems.Where(IsGpu).ToList();

        /// <summary>按模块槽里的模块重新汇总三项百分比（游戏自己的公式，含相邻效果），换算成本 mod 的模块效果。</summary>
        public static ModuleBonus Bonus(GameItem rig, GameInventory modules)
        {
            ModuleHelper.ComputeModuleEffect(modules.childItems, rig);
            return ModuleBonus.From(
                rig.GetTagReadonly(ModuleHelper.TOTAL_PERCENTAGE_PERFORMANCE_BONUS_INT).GetInt(),
                rig.GetTagReadonly(ModuleHelper.TOTAL_PERCENTAGE_POWER_BONUS_INT).GetInt(),
                rig.GetTagReadonly(ModuleHelper.TOTAL_PERCENTAGE_QUALITY_BONUS_INT).GetInt());
        }

        private static int RigKj(GameInventory cards, ModuleBonus b) =>
            Tuning.RigKj + Mathf.CeilToInt((float)Cards(cards).Select(ToUnit).Sum(u => u.Kj(b)));

        private static void UpdateRig(GameItem rig, GameInventory modules, GameInventory cards)
        {
            var b = Bonus(rig, modules);
            int kj = RigKj(cards, b);
            // 基础耗电 = 机身 + 所有显卡（不含模块）。游戏装卸模块时用这个基础值重算耗电，所以它必须随显卡变化，
            // 否则游戏会把耗电改回"只有机身的 2 kJ"再按百分比折算。
            int baseKj = RigKj(cards, ModuleBonus.None);
            rig.ModifyTag("MACHINERY_DRAWN_POWER_BASE", s => s.Enable().SetInt(baseKj));
            rig.ModifyTag(MachineryHelper.MACHINERY_DRAWN_POWER, s => s.Enable().SetInt(kj));
            double hash = Cards(cards).Select(ToUnit).Sum(u => u.Hash(b));
            int slots = SlotsOf(rig);
            rig.shortDescription = L.T("item.rig.desc", slots, Fmt.Num(hash * 10), kj);
            RigStatus.Show(rig, Cards(cards).Count, slots, kj, hash * 10);
            Payout.Refresh(rig);
            var next = RigSlots.Next(slots);
            if (next != null)
            {
                int progress = rig.IsTag(ProgressTag) ? rig.GetTagReadonly(ProgressTag).GetInt() : 0;
                rig.shortDescription += " " + L.T("item.rig.next", slots + 1, L.T("mat." + next.Value.Item), progress, next.Value.Count);
            }
            if (b.Hash != 1 || b.Kj != 1 || b.Wear != 1)
                rig.shortDescription += " " + L.T("item.rig.modules", Pct(b.Hash), Pct(b.Kj), Pct(b.Wear));
        }

        private static string Pct(double mult)
        {
            int p = (int)Math.Round((mult - 1) * 100);
            return p > 0 ? "+" + p + "%" : p + "%";
        }

        /// <summary>夜间早段：按矿架上的显卡总耗电（含模块效果）从电池扣电；扣电成功，登记今晚参与挖矿的显卡和模块效果。</summary>
        private static void NightDraw(GameItem rig, GameSlotInventory battery, GameInventory modules, GameInventory cards)
        {
            if (!Session.TonightDrawn.Add(rig.uniqueId)) return;
            var list = Cards(cards).Where(c => !c.IsTag(DeadTag)).ToList();
            Session.TonightRigs++;
            if (list.Count == 0) return;
            var b = Bonus(rig, modules);
            int kj = RigKj(cards, b);
            if (battery.childItems.Count == 0 || !PowerHelper.DrawPowerSource(battery.childItem, kj)) return;
            Session.TonightPoweredRigs++;
            Session.TonightKj += kj;
            foreach (var c in list) { Session.TonightCards.Add(new KeyValuePair<GameItem, ModuleBonus>(c, b)); Session.TonightMachine[c] = rig; }
            UpdateRig(rig, modules, cards);
        }

        /// <summary>
        /// 放进储藏区（游戏 EmporiumTryAdd 的默认目标，放不下时游戏自己找位置）。
        /// 不放柜台：测试中柜台上的物品过夜后会消失（未在正常流程中确认）；ModHelper.MoveItemTo* 放满时会静默失败。
        /// </summary>
        /// <summary>优先放进机器区（machine_bay*）的格子里，没有位置时退回 <see cref="Give"/>。矿架这类机器该待在机器区。</summary>
        public static void GiveToMachineBay(GameItem item)
        {
            GeneralHelper.SetItemOwned(item, true);
            var e = EmporiumEntry.Instance;
            if (e?.invElement != null)
                foreach (var box in e.invElement.childItems.Where(i => i.identifier != null && i.identifier.StartsWith("machine_bay")).ToList())
                    if (GraphUtils.CanAccept(box, item) && GraphUtils.TryAcceptAll(box, item) == 0) return;
            Give(item);
        }

        /// <summary>放到前台（解锁送的东西第一眼能看到）；前台放不下时退回储藏区。游戏离店和过夜会清空前台，<see cref="SweepFront"/> 在那之前把 mod 的物品收进储藏区。</summary>
        public static void GiveToFront(GameItem item)
        {
            GeneralHelper.SetItemOwned(item, true);
            var front = EmporiumEntry.Instance?.frontInvinvElement;
            if (front != null && GraphUtils.CanAccept(front, item) && GraphUtils.TryAcceptAll(front, item) == 0) return;
            Give(item);
        }

        /// <summary>离店、睡觉前：前台上属于 mod 的物品收进储藏区（游戏随后会清空前台，没拿走的终端、钱包会丢）。</summary>
        private static void SweepFront()
        {
            Log.Guard("收起前台的 mod 物品", () =>
            {
                var e = EmporiumEntry.Instance;
                if (e?.frontInvinvElement == null || e.invElement == null) return;
                foreach (var it in e.frontInvinvElement.childItems.ToList())
                    if (it.identifier != null && it.identifier.StartsWith("pd_") && GeneralHelper.IsItemOwned(it)) GraphUtils.TryAcceptAll(e.invElement, it);
            });
        }

        public static void Give(GameItem item)
        {
            GeneralHelper.SetItemOwned(item, true);
            GraphUtils.EmporiumTryAdd(item);
        }
    }
}

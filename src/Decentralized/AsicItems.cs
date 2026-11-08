using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Linq;
using Decentralized.Domain;
using UnityEngine;

namespace Decentralized
{
    /// <summary>
    /// RatDigg 矿机（M1 / M2 / M3）：独立的机器，窗口里有电池槽、模块槽（和矿架同一套效果）和修理槽，没有显卡槽。
    /// 夜间和矿架一样先从电池扣电、有电才挖；损耗与暴毙沿用 <see cref="Unit"/>（暴毙系数 0.5，每天损耗 0.8）。
    /// 暴毙后成了"损坏的矿机"：往修理槽里按顺序放电子元件修好（见 <see cref="AsicRepair"/>），健康扣 20 点。
    /// </summary>
    public static class AsicItems
    {
        public const string Prefix = "pd_asic_";
        public const string StepTag = "PD_REPAIR_STEP", ProgTag = "PD_REPAIR_PROG";

        public static string Id(string model) => Prefix + model.ToLowerInvariant();
        public static bool Is(GameItem it) => it != null && it.identifier != null && it.identifier.StartsWith(Prefix);

        public static void Register(ModItemDirectory dir)
        {
            foreach (var spec in Hardware.Specs.Values.Where(s => s.Asic))
            {
                var s = spec;
                dir.Add(Id(s.Model), () => Create(s));
            }
        }

        private static int Step(GameItem m) => m.IsTag(StepTag) ? m.GetTagReadonly(StepTag).GetInt() : 0;
        private static int Prog(GameItem m) => m.IsTag(ProgTag) ? m.GetTagReadonly(ProgTag).GetInt() : 0;

        private static readonly ConditionalWeakTable<GameItem, GameInventory> ModulesOf = new ConditionalWeakTable<GameItem, GameInventory>();

        /// <summary>这件电子元件能不能修这台矿机：机器烧坏了，而且这一步要的就是这种元件。电子元件拖到机器上修，没有修理槽。</summary>
        public static bool CanRepairWith(GameItem m, GameItem src)
        {
            if (!Is(m) || src == null || !GeneralHelper.IsItemOwned(src) || !GeneralHelper.IsItemOwned(m)) return false;
            var u = Items.ToUnit(m);
            var need = AsicRepair.Need(u.Model, Step(m));
            return u.Dead && need != null && src.identifier == need.Value.Item;
        }

        /// <summary>修理：每次收一件，攒够这一步再进下一步，全部攒够就修好。</summary>
        public static void TakeRepair(GameItem m, GameItem item)
        {
            if (!CanRepairWith(m, item) || !ModulesOf.TryGetValue(m, out var modules)) return;
            var u = Items.ToUnit(m);
            int step = Step(m), prog = Prog(m);
            int took = AsicRepair.Insert(u.Model, ref step, ref prog, item.identifier, 1);
            if (took <= 0) return;
            m.ModifyTag(StepTag, s => s.Enable().SetInt(step));
            m.ModifyTag(ProgTag, s => s.Enable().SetInt(prog));
            if (item.unitCount <= 1) item.Destroy(); else item.GetSplitItem(1).Destroy();
            if (AsicRepair.Done(u.Model, step))
            {
                u.Dead = false;
                u.Health = Math.Max(10, u.Health - Tuning.AsicRepairHealthCut);
                m.ModifyTag(Items.DeadTag, s => s.Disable());
                m.ModifyTag(StepTag, s => s.Enable().SetInt(0));
                m.ModifyTag(ProgTag, s => s.Enable().SetInt(0));
                Items.Store(m, u);
                Log.Info($"矿机 {u.Model} 修好了，健康 {u.Health:0}");
            }
            EmporiumAudioPlayer.Instance?.ratchet?.Play();
            Update(m, modules);
        }

        private static GameItem Create(HardwareSpec spec)
        {
            var battery = new GameSlotInventory();
            var modules = new GameGridInventory(MachineHelper.DEFAULT_MODULE_BAY_CONFIG, 7);
            GameItem machine = null;
            var stHash = new MachineryText("", 6, TMPro.HorizontalAlignmentOptions.Left, false);
            var stPower = new MachineryText("", 6, TMPro.HorizontalAlignmentOptions.Left, false);
            var stCond = new MachineryText("", 6, TMPro.HorizontalAlignmentOptions.Left, false);
            var addr = new MachineryStack(true, 6).Add(stHash).Add(stPower).Add(stCond).Add(Payout.Build(() => machine, out var stAddr));
            var window = MachineHelper.CreateMachineryWindow(true,
                MachineHelper.PowerPanel(battery),
                MachineHelper.Panel(L.T("rig.label.modules"), modules, "module"),
                MachineHelper.Panel(L.T("rig.label.status"), addr, "status", false));
            var m = ItemDirectory.CreateEmptyItem(Id(spec.Model)).SetContentWindow(window);
            machine = m;
            Payout.Register(m, stAddr);
            RigStatus.Add(m, new[] { stHash, stPower, stCond });
            m.SetSpriteAndShapeFromMod(ModInfo.Id, "asic_" + spec.Model.ToLowerInvariant());
            m.name = L.T("item.asic.name", spec.Model);
            m.unitValue = spec.Price;
            MachineHelper.SetupBatterySlot(battery, m);
            m.EnableTag("CONTAINER_TAG");
            m.EnableTag("STANDARD_MACHINE_TAG");
            MachineryHelper.InitMachinery(m, (int)Math.Ceiling(spec.Kj));
            MachineHelper.SetupModuleBay(modules, m, () => Update(m, modules));
            ModulesOf.Remove(m); ModulesOf.Add(m, modules);
            m.onLoaded = _ => Update(m, modules);
            m.onCycleEndEarlySlotItemFunc = (_, _, _) => Log.Guard("矿机扣电", () => NightDraw(m, battery, modules));
            ModHelper.SetSource(m, ModInfo.Id, ModInfo.Name);
            Update(m, modules);
            return m;
        }

        /// <summary>名字、说明、价值与耗电（耗电按模块效果重算）。</summary>
        private static void Update(GameItem m, GameInventory modules)
        {
            var u = Items.ToUnit(m);
            var b = Items.Bonus(m, modules);
            int baseKj = (int)Math.Ceiling(u.Label.Kj);
            m.ModifyTag("MACHINERY_DRAWN_POWER_BASE", s => s.Enable().SetInt(baseKj));
            m.ModifyTag(MachineryHelper.MACHINERY_DRAWN_POWER, s => s.Enable().SetInt(u.Dead ? 0 : (int)Math.Ceiling(u.Kj(b))));
            RigStatus.ShowAsic(m, u.Dead ? 0 : u.Hash(b) * 10 / 1000, u.Dead ? 0 : (int)Math.Ceiling(u.Kj(b)), u.Health, u.Dead);
            Payout.Refresh(m);
            Describe(m, u);
        }

        /// <summary>按标签刷新名字、说明、价值（夜间损耗之后也走这里）。</summary>
        public static void Refresh(GameItem m) => Describe(m, Items.ToUnit(m));

        private static void Describe(GameItem m, Unit u)
        {
            var spec = u.Label;
            m.name = L.T(u.Dead ? "item.asic.dead.name" : "item.asic.name", spec.Model);
            if (u.Dead)
            {
                var need = AsicRepair.Need(u.Model, Step(m));
                m.shortDescription = need == null ? L.T("item.asic.dead.fixed") : L.T("item.asic.dead.desc", L.T("mat." + need.Value.Item), need.Value.Count - Prog(m));
                m.unitValue = Math.Max(1, (long)Math.Round(spec.Price * Tuning.AsicDeadValue));
                return;
            }
            string cond = GeneralHelper.IsItemOwned(m)
                ? Mathf.RoundToInt((float)u.Health) + "%"
                : L.T(u.Health >= 85 ? "item.gpu.grade.1" : u.Health >= 60 ? "item.gpu.grade.2" : u.Health >= 35 ? "item.gpu.grade.3" : "item.gpu.grade.4");
            m.shortDescription = L.T("item.asic.desc", Fmt.Num(spec.DisplayHash), Fmt.Num(spec.Kj), cond);
            m.unitValue = CardValue.Of(spec.Model, u.Health);
        }

        private static void NightDraw(GameItem m, GameSlotInventory battery, GameInventory modules)
        {
            if (TurboMine.Boosting) { Boost(m, battery, modules); return; }
            if (!Session.TonightDrawn.Add(m.uniqueId)) return;
            var u = Items.ToUnit(m);
            Session.TonightRigs++;
            if (u.Dead) return;
            var b = Items.Bonus(m, modules);
            int kj = (int)Math.Ceiling(u.Kj(b));
            if (battery.childItems.Count == 0 || !PowerHelper.DrawPowerSource(battery.childItem, kj)) return;
            Session.TonightPoweredRigs++;
            Session.TonightKj += kj;
            Session.TonightCards.Add(new KeyValuePair<GameItem, ModuleBonus>(m, b));
            Session.TonightMachine[m] = m;
            Update(m, modules);
        }

        /// <summary>涡轮增压：扣一晚的电，立刻挖一轮（不影响今晚照常挖矿）。</summary>
        private static void Boost(GameItem m, GameSlotInventory battery, GameInventory modules)
        {
            var u = Items.ToUnit(m);
            if (u.Dead) return;
            var b = Items.Bonus(m, modules);
            int kj = (int)Math.Ceiling(u.Kj(b));
            if (battery.childItems.Count == 0 || !PowerHelper.DrawPowerSource(battery.childItem, kj)) { Log.Info("涡轮增压：矿机电池电量不够，这一轮没挖"); return; }
            TurboMine.Round(m, new List<KeyValuePair<GameItem, ModuleBonus>> { new KeyValuePair<GameItem, ModuleBonus>(m, b) });
            Update(m, modules);
        }

        /// <summary>造一台矿机：按健康度写标签。</summary>
        public static GameItem New(string model, double health)
        {
            var item = ModHelper.SpawnItem(Id(model));
            Items.Store(item, new Unit { Model = model, Health = health });
            return item;
        }
    }
}

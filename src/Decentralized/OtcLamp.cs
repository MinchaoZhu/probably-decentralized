using System;
using System.Collections.Generic;
using UnityEngine;

namespace Decentralized
{
    /// <summary>
    /// 借用原版小偷灯招揽换币客户：在原版小偷灯的工厂外包一层，切换顺序末尾加一个目标"换币客户"（解锁换币台后才有）。
    /// 原版按灯的目标去公开的顾客表（<see cref="StoreClientListDict.storeClientDict"/>）里造顾客，所以顾客面板、灯的提示、概率都走原版逻辑。
    /// 霓虹色：底图换成原版图集里熄灭的暗灯（存档里记的仍是原版的图，卸载后不缺图），上面用原版的图片叠加层
    /// （getItemSpriteModifierForItemFunc，运行时委托，不进存档）画黄绿色光晕。原版遇到不认识的目标会点亮成第一种颜色，我们排在它后面改回熄灭的底图。
    /// 切换只接管进出我们目标的那一步，其余交给原来的切换函数，别的 mod 往灯上加的目标也照样能切到。
    /// </summary>
    public static class OtcLamp
    {
        /// <summary>灯的目标 = 顾客表里的顾客标识。</summary>
        public const string Target = "pd_otc";
        private const string OverlaySprite = "otc_lamp_on";
        /// <summary>原版暗灯所在的图集与熄灭时的图（BlackLampHelper 里是私有常量）。</summary>
        private const string LampAtlas = "Items/items_sign", LampOff = "black_lamp";

        private static readonly Action<GameItem, GameInventory, SlotMarker> OnShowcaseChanged = (item, _, __) => Log.Guard("展示柜刷新换币灯", () => Refresh(item));

        public static bool IsLamp(GameItem item) => item != null && item.identifier == BlackLampHelper.BLACK_LAMP_ID;
        public static bool IsOurs(GameItem item) => IsLamp(item) && BlackLampHelper.GetTarget(item) == Target;

        /// <summary>在原版小偷灯的工厂外包一层（物品目录初始化时）。</summary>
        public static void Wrap(ModItemDirectory dir)
        {
            try
            {
                var make = DirectoryMaster.GetFactory<GameItem>(BlackLampHelper.BLACK_LAMP_ID);
                if (make == null) { Log.Warn("[OTC] 找不到原版小偷灯"); return; }
                dir.Set(BlackLampHelper.BLACK_LAMP_ID, () =>
                {
                    var lamp = make();
                    var prev = lamp.onActivateSlotItemFunc;
                    lamp.onActivateSlotItemFunc = (it, inv, slot) => Log.Guard("小偷灯切换", () => Cycle(it, inv, slot, prev));
                    var prevSprites = lamp.getItemSpriteModifierForItemFunc;
                    lamp.getItemSpriteModifierForItemFunc = it => Overlay(it, prevSprites);
                    return lamp;
                });
                Log.Info("[OTC] 已包装原版小偷灯");
            }
            catch (Exception ex) { Log.Error("[OTC] 包装小偷灯失败：" + ex); }
        }

        /// <summary>
        /// 切换：当前是"换币客户"时回到原版第一个目标；否则先走原来的切换，原来的切换绕回第一个目标、而且换币台已解锁时，改成"换币客户"。
        /// </summary>
        private static void Cycle(GameItem lamp, GameInventory inv, SlotMarker slot, Action<GameItem, GameInventory, SlotMarker> prev)
        {
            string first = BlackLampHelper.TARGETS[0];
            if (IsOurs(lamp))
            {
                SetTarget(lamp, first);
                BlackLampHelper.RefreshLampSprite(lamp);
                AudioManager.Instance.Play2("click_vintage");
            }
            else
            {
                string before = BlackLampHelper.GetTarget(lamp);
                prev?.Invoke(lamp, inv, slot);
                if (Unlock.OtcUnlocked && before != first && BlackLampHelper.GetTarget(lamp) == first) SetTarget(lamp, Target);
            }
            Refresh(lamp);
        }

        public static void SetTarget(GameItem lamp, string target) => lamp.ModifyTag(BlackLampHelper.LAMP_TARGET_TAG, s => { s.Enable().SetString(target); });

        /// <summary>目标是"换币客户"、而且灯在展示柜里时，叠一层霓虹灯图（盖住原版退回的点亮图）。</summary>
        private static List<ItemSpriteModifier> Overlay(GameItem lamp, Func<GameItem, List<ItemSpriteModifier>> prev)
        {
            var list = prev?.Invoke(lamp) ?? new List<ItemSpriteModifier>();
            if (IsOurs(lamp) && InShowcase(lamp))
            {
                var sprite = Items.ModSprite(OverlaySprite);
                if (sprite != null) list.Add(new ItemSpriteModifier(sprite, Vector2.zero, 0, isFlipped: false, 10, haveMask: false, Color.white));
            }
            return list;
        }

        private static bool InShowcase(GameItem item)
        {
            var sc = EmporiumEntry.Instance?.showcaseElement;
            return sc != null && sc.childItems.Contains(item);
        }

        /// <summary>换币灯在展示柜里：底图改成熄灭的暗灯（光晕由叠加层画）；然后重画。</summary>
        private static void Refresh(GameItem item)
        {
            if (!IsLamp(item)) return;
            if (IsOurs(item) && InShowcase(item)) item.SetSpriteQuick(LampAtlas, LampOff);
            item.Validate();
        }

        /// <summary>
        /// 读档、开新游戏后：在展示柜的放入、取出回调后面挂上刷新（原版在这两个回调里重设灯的底图，我们排在它后面重画叠加层），
        /// 先解绑再挂，避免读档几次挂几次；再把展示柜里的灯刷新一遍。
        /// </summary>
        public static void Attach()
        {
            var sc = EmporiumEntry.Instance?.showcaseElement;
            if (sc == null) return;
            sc.onSlotAddItemLateFunc = (Action<GameItem, GameInventory, SlotMarker>)Delegate.Remove(sc.onSlotAddItemLateFunc, OnShowcaseChanged);
            sc.onSlotAddItemLateFunc = (Action<GameItem, GameInventory, SlotMarker>)Delegate.Combine(sc.onSlotAddItemLateFunc, OnShowcaseChanged);
            sc.onSlotRemoveItemEndFunc = (Action<GameItem, GameInventory, SlotMarker>)Delegate.Remove(sc.onSlotRemoveItemEndFunc, OnShowcaseChanged);
            sc.onSlotRemoveItemEndFunc = (Action<GameItem, GameInventory, SlotMarker>)Delegate.Combine(sc.onSlotRemoveItemEndFunc, OnShowcaseChanged);
            foreach (var item in sc.childItems) Refresh(item);
        }
    }
}

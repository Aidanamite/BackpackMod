using HarmonyLib;
using InControl;
using SRML;
using SRML.Console;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using SRML.Utils.Enum;
using SRML.SR;
using AssetsLib;
using System.Linq;
using static AssetsLib.TextureUtils;
using static AssetsLib.UIUtils;
using static AssetsLib.TextUtils;
using SRML.Config.Attributes;
using MonomiPark.SlimeRancher.Persist;
using SRML.SR.SaveSystem.Data.Ammo;
using SRML.SR.SaveSystem.Data;
using SRML.SR.SaveSystem;
using MonomiPark.SlimeRancher.DataModel;
using Console = SRML.Console.Console;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace BackpackMod
{
    public class Main : ModEntryPoint
    {
        internal static Assembly modAssembly = Assembly.GetExecutingAssembly();
        internal static string modName = $"{modAssembly.GetName().Name}";
        internal static string modDir = $"{Environment.CurrentDirectory}\\SRML\\Mods\\{modName}";
        public static Sprite icon = LoadImage("icon.png", wrapMode: TextureWrapMode.Clamp).CreateSprite();
        public static Main instance;
        internal static PlayerAction openMenu;
        internal static PlayerAction quickMove;

        public Main() => instance = this;

        public override void PreLoad()
        {
            TranslationPatcher.AddPediaTranslation("m.upgrade.name.personal.backpack", "Backpack");
            TranslationPatcher.AddPediaTranslation("m.upgrade.desc.personal.backpack", "Adds a handy expansion to your vacpack in the form a backpack");
            LookupRegistry.RegisterUpgradeEntry(Ids.BACKPACK.Define(icon, 10000));
            PersonalUpgradeRegistry.RegisterDefaultUpgrade(Ids.BACKPACK);
            (openMenu = BindingRegistry.RegisterBindedAction("key.backpack")).AddDefaultBinding(Key.U);
            (quickMove = BindingRegistry.RegisterBindedAction("key.backpack_quickmove")).AddDefaultBinding(Key.Shift);
            TranslationPatcher.AddUITranslation("key.key.backpack", "Open Backpack");
            TranslationPatcher.AddUITranslation("key.key.backpack_quickmove", "Backpack Quickmove");
            TranslationPatcher.AddUITranslation("t.backpack", "Backpack");
            TranslationPatcher.AddUITranslation("b.shoot", "Shoot");
            HarmonyInstance.PatchAll();
        }
        public override void Update()
        {
            if (openMenu.WasPressed && SceneContext.Instance.PlayerState.HasUpgrade(Ids.BACKPACK))
                BackpackMenu();
        }
        public static void BackpackMenu()
        {
            CreateSelectionUI("t.backpack", icon, new List<ModeOption>
            {
                new ModeOption(icon,"b.deposit",() => OpenBackpack(BackpackMode.Deposit,BackpackMenu)),
                new ModeOption(icon,"b.withdraw",() => OpenBackpack(BackpackMode.Withdraw,BackpackMenu)),
                new ModeOption(icon,"b.shoot",() => OpenBackpack(BackpackMode.Shoot,BackpackMenu))
            }, true);
        }
        public enum BackpackMode
        {
            Withdraw,
            Deposit,
            Shoot
        }
        public static void OpenBackpack(BackpackMode mode, Action onClose = null)
        {
            var options = new List<(string,IInventoryItem)>();
            var take = SceneContext.Instance.PlayerState.ammoDict[Ids.BACKPACK_AMMO];
            var put = SceneContext.Instance.PlayerState.Ammo;
            Func<Ammo.Slot, Func<bool>> action;
            if (mode == BackpackMode.Shoot)
            {
                var vac = SceneContext.Instance.Player.GetComponentInChildren<WeaponVacuum>();
                action = x => () =>
                {
                    if (x.count > 0)
                    {
                        var l = vac.player.ammoMode;
                        vac.player.ammoMode = Ids.BACKPACK_AMMO;
                        take.selectedAmmoIdx = Array.IndexOf(take.Slots, x);
                        var newObj = take.GetSelectedStored();
                        vac.Expel(newObj, false);
                        vac.ShootEffect();
                        x.count--;
                        vac.player.ammoMode = l;
                        return true;
                    }
                    return false;
                };
            }
            else
            {
                if (mode == BackpackMode.Deposit)
                {
                    var t = take;
                    take = put;
                    put = t;
                }
                action = x => () => put.TryMoveAmmo(take, x, quickMove.Bindings.First().GetState(quickMove.ActiveDevice) ? x.count : 1);
            }
            foreach (var item in take.Slots)
                if (!item.IsEmpty() && (mode != BackpackMode.Withdraw || put.CouldContain(item.id)))
                    options.Add((Identifiable.GetName(item.id), new IdentInventoryItem(item.id,
                        () => item.count,
                        action(item)
                    )));
            options.Sort((x, y) => string.Compare(x.Item1,y.Item1));
            CreateInventoryUI("t.backpack", icon, options.ConvertAll(x => x.Item2), false, onClose);
        }
        public static void Log(string message) => instance.ConsoleInstance.Log($"[{modName}]: " + message);
        public static void LogError(string message) => instance.ConsoleInstance.LogError($"[{modName}]: " + message);
        public static void LogWarning(string message) => instance.ConsoleInstance.LogWarning($"[{modName}]: " + message);
        public static void LogSuccess(string message) => instance.ConsoleInstance.LogSuccess($"[{modName}]: " + message);
    }

    [EnumHolder]
    static class Ids
    {
        public static readonly PlayerState.Upgrade BACKPACK;
        public static readonly PlayerState.AmmoMode BACKPACK_AMMO = (PlayerState.AmmoMode)100;
    }

    [ConfigFile("settings")]
    static class Config
    {
        public static int slotCount = 50;
        public static bool autoStoreIfFull = true;
    }

    [HarmonyPatch(typeof(PlayerState), "Reset")]
    class Patch_PlayerState_Reset
    {
        public static void Postfix(PlayerState __instance)
        {
            __instance.ammoDict.Add(Ids.BACKPACK_AMMO, new Ammo(new HashSet<Identifiable.Id>(new[] { Identifiable.Id.NONE }, new Yes<Identifiable.Id>()), Config.slotCount, Config.slotCount, new Predicate<Identifiable.Id>[Config.slotCount], (x, y) => __instance.ammoDict[PlayerState.AmmoMode.DEFAULT].GetSlotMaxCount(x, y)));
        }
        class Yes<T> : IEqualityComparer<T>
        {
            bool IEqualityComparer<T>.Equals(T x, T y) => true;
            int IEqualityComparer<T>.GetHashCode(T obj) => 0;
        }
    }

    [HarmonyPatch(typeof(PlayerState), "InitModel")]
    class Patch_PlayerState_InitModel
    {
        public static void Postfix(PlayerState __instance, PlayerModel model)
        {
            model.ammoDict[Ids.BACKPACK_AMMO] = new AmmoModel();
            __instance.ammoDict[Ids.BACKPACK_AMMO].InitModel(model.ammoDict[Ids.BACKPACK_AMMO]);
        }
    }

    [HarmonyPatch(typeof(PlayerState), "SetModel")]
    class Patch_PlayerState_SetModel
    {
        public static void Postfix(PlayerState __instance, PlayerModel model)
        {
            if (model.ammoDict.TryGetValue(Ids.BACKPACK_AMMO,out var m))
            __instance.ammoDict[Ids.BACKPACK_AMMO].SetModel(m);
        }
    }

    [HarmonyPatch(typeof(MonomiPark.SlimeRancher.SavedGame), "AmmoDataToSlots", new Type[] { typeof(Dictionary<PlayerState.AmmoMode, List<AmmoDataV02>>) })]
    class Patch_SavedGame_AmmoDataToSlots
    {
        public static void Prefix(Dictionary<PlayerState.AmmoMode, List<AmmoDataV02>> ammo)
        {
            if (ammo.TryGetValue(Ids.BACKPACK_AMMO, out var data))
            {
                for (int i = data.Count; i > Config.slotCount; i--)
                    data.RemoveAt(Config.slotCount - 1);
                for (int i = data.Count; i < Config.slotCount; i++)
                    data.Insert(data.Count - 1, new AmmoDataV02() { id = Identifiable.Id.NONE, count = 0, emotionData = new SlimeEmotionDataV02() });
            }
        }
    }

    [HarmonyPatch(typeof(Ammo),"MaybeAddToSlot",typeof(Identifiable.Id), typeof(Identifiable))]
    class Patch_TryAddAmmo
    {
        static void Postfix(Ammo __instance, Identifiable.Id id, Identifiable identifiable, ref bool __result)
        {
            if (Config.autoStoreIfFull && !__result
                && __instance == SceneContext.Instance?.PlayerState?.Ammo
                && SceneContext.Instance.PlayerState.HasUpgrade(Ids.BACKPACK)
                && SceneContext.Instance.PlayerState.ammoDict.TryGetValue(Ids.BACKPACK_AMMO, out var ammo)
                && __instance.CouldContain(id)
                )
                __result = ammo.MaybeAddToSlot(id, identifiable);
        }
    }

    static class ExtentionMethods
    {
        public static UpgradeDefinition Define(this PlayerState.Upgrade upgrade, Sprite sprite, int cost)
        {
            var o = ScriptableObject.CreateInstance<UpgradeDefinition>();
            o.upgrade = upgrade;
            o.icon = sprite;
            o.cost = cost;
            return o;
        }
        public static int IndexOf<T>(this T[] t, Func<T, int, bool> predicate)
        {
            for (int i = 0; i < t.Length; i++)
                if (predicate(t[i], i))
                    return i;
            return -1;
        }
        public static bool TryMoveAmmo(this Ammo ammo, Ammo fromAmmo, Ammo.Slot slot, int amt = 1)
        {
            if (slot.IsEmpty())
                return false;
            if (!ammo.CouldAddToSlot(slot.id))
                return false;
            var ammoPersist = PersistentAmmoManager.GetPersistentAmmoForAmmo(ammo.ammoModel);
            var ammoPersist2 = PersistentAmmoManager.GetPersistentAmmoForAmmo(fromAmmo.ammoModel);
            var ind = ammo.GetAmmoIdx(slot.id);
            var fromInd = Array.IndexOf(fromAmmo.Slots, slot);
            amt = Mathf.Min(amt, slot.count);
            if (ind == null)
            {
                ind = ammo.Slots.IndexOf((x, y) => x.IsEmpty() && (ammo.GetSlotPredicate(y)?.Invoke(slot.id) ?? true));
                if (ind == -1)
                    return false;
                amt = Mathf.Min(ammo.GetSlotMaxCount(slot.id, ind.Value), amt);
                ammo.Slots[ind.Value] = slot.Take(amt);
                ammoPersist.DataModel.slots[ind.Value] = ammoPersist2.DataModel.slots[fromInd].Take(ammo.Slots[ind.Value].count);
            }
            else
            {
                amt = Mathf.Min(ammo.GetSlotMaxCount(slot.id, ind.Value) - ammo.Slots[ind.Value].count, amt);
                amt = slot.Move(ammo.Slots[ind.Value], amt);
                ammoPersist2.DataModel.slots[fromInd].Move(ammoPersist.DataModel.slots[ind.Value], amt);
            }
            if (slot.IsEmpty())
                fromAmmo.Slots[fromInd] = null;
            return true;
        }
        public static bool IsEmpty(this Ammo.Slot slot) => slot == null || slot.id == Identifiable.Id.NONE || slot.count <= 0;
        public static Ammo.Slot Take(this Ammo.Slot slot, int amount)
        {
            if (slot.IsEmpty())
                return new Ammo.Slot(default, default);
            var s = new Ammo.Slot(slot.id, Mathf.Min(amount, slot.count)) { emotions = slot.emotions.Clone() };
            slot.count -= s.count;
            return s;
        }
        public static PersistentAmmoSlot Take(this PersistentAmmoSlot slot, int amount)
        {
            var s = new PersistentAmmoSlot();
            if (slot == null)
                return s;
            for (int i = 0; i < amount; i++)
                if (slot.Count > 0)
                    s.PushTop(slot.PopTop() ?? new CompoundDataPiece(""));
                else
                    s.PushTop(new CompoundDataPiece(""));
            return s;
        }
        public static int Move(this Ammo.Slot slot, Ammo.Slot target, int amount)
        {
            if (slot.IsEmpty())
                return 0;
            amount = Mathf.Min(amount, slot.count);
            target.AverageIn(slot.emotions, amount);
            target.count += amount;
            slot.count -= amount;
            return amount;
        }
        public static void Move(this PersistentAmmoSlot slot, PersistentAmmoSlot target, int amount)
        {
            for (int i = 0; i < amount; i++)
                if (slot?.Count > 0)
                    target.PushTop(slot.PopTop() ?? new CompoundDataPiece(""));
                else
                    target.PushTop(new CompoundDataPiece(""));
        }
        public static SlimeEmotionData Clone(this SlimeEmotionData data)
        {
            if (data == null)
                return null;
            var n = new SlimeEmotionData();
            foreach (var p in data)
                n[p.Key] = p.Value;
            return n;
        }

        public static void AverageIn(this Ammo.Slot data, SlimeEmotionData emotions, int amt = 1)
        {
            if (emotions == null)
                return;
            if (data.emotions == null)
            {
                data.emotions = emotions.Clone();
                return;
            }
            float weight = amt / data.count;
            float num = 1f - weight;
            foreach (var emotion in emotions)
                data.emotions[emotion.Key] = data.emotions[emotion.Key] * num + emotion.Value * weight;
        }

        public static bool CouldContain(this Ammo ammo, Identifiable.Id id) => ammo.potentialAmmo.Contains(id) && ammo.slotPreds.Any(x => x == null || x(id));
    }
}
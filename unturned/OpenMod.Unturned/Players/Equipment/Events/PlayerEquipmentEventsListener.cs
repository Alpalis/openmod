﻿extern alias JetBrainsAnnotations;
using System;
using HarmonyLib;
using JetBrainsAnnotations::JetBrains.Annotations;
using OpenMod.Unturned.Events;
using OpenMod.Unturned.Items;
using SDG.Unturned;

namespace OpenMod.Unturned.Players.Equipment.Events
{
    [UsedImplicitly]
    internal class PlayerEquipmentEventsListener : UnturnedPlayerEventsListener
    {
        public PlayerEquipmentEventsListener(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public override void Subscribe()
        {
            OnItemEquipped += Events_OnItemEquipped;
            OnItemUnequipped += Events_OnItemUnequipped;
        }

        public override void Unsubscribe()
        {
            OnItemEquipped -= Events_OnItemEquipped;
            OnItemUnequipped -= Events_OnItemUnequipped;
        }

        public override void SubscribePlayer(Player player)
        {
            player.equipment.onEquipRequested += OnEquipRequested;
            player.equipment.onDequipRequested += OnDequipRequested;
        }

        public override void UnsubscribePlayer(Player player)
        {
            player.equipment.onEquipRequested -= OnEquipRequested;
            player.equipment.onDequipRequested -= OnDequipRequested;
        }

        private void Events_OnItemEquipped(Player nativePlayer)
        {
            var player = GetUnturnedPlayer(nativePlayer)!;

            var item = new Item(nativePlayer.equipment.itemID, 1, nativePlayer.equipment.quality,
                nativePlayer.equipment.state);

            var @event = new UnturnedPlayerItemEquippedEvent(player, new UnturnedItem(item));

            Emit(@event);
        }

        private void Events_OnItemUnequipped(Player nativePlayer)
        {
            var player = GetUnturnedPlayer(nativePlayer)!;

            var @event = new UnturnedPlayerItemUnequippedEvent(player);

            Emit(@event);
        }

        private void OnEquipRequested(PlayerEquipment equipment, ItemJar jar, ItemAsset asset, ref bool shouldAllow)
        {
            var player = GetUnturnedPlayer(equipment.player)!;

            var @event = new UnturnedPlayerItemEquippingEvent(player, new UnturnedItem(jar.item))
            {
                IsCancelled = !shouldAllow
            };

            Emit(@event);

            shouldAllow = !@event.IsCancelled;
        }

        private void OnDequipRequested(PlayerEquipment equipment, ref bool shouldAllow)
        {
            var player = GetUnturnedPlayer(equipment.player)!;

            var inv = player.Player.inventory;

            var page = equipment.equippedPage;

            var index = inv.getIndex(page, equipment.equipped_x, equipment.equipped_y);

            var jar = inv.getItem(page, index);

            if (jar?.item == null)
                return;

            var @event = new UnturnedPlayerItemUnequippingEvent(player, new UnturnedItem(jar.item))
            {
                IsCancelled = !shouldAllow
            };

            Emit(@event);

            shouldAllow = !@event.IsCancelled;
        }

        private delegate void ItemEquipped(Player player);
        private static event ItemEquipped? OnItemEquipped;

        private delegate void ItemUnequipped(Player player);
        private static event ItemUnequipped? OnItemUnequipped;

        [UsedImplicitly]
        [HarmonyPatch]
        private static class Patches
        {
            // ReSharper disable InconsistentNaming
            [UsedImplicitly]
            [HarmonyPatch(typeof(PlayerEquipment), nameof(PlayerEquipment.ReceiveEquip))]
            [HarmonyPrefix]
            public static void PreTellEquip(PlayerEquipment __instance, out ushort __state)
            {
                __state = __instance.itemID;
            }

            [UsedImplicitly]
            [HarmonyPatch(typeof(PlayerEquipment), nameof(PlayerEquipment.ReceiveEquip))]
            [HarmonyPostfix]
            public static void PostTellEquip(PlayerEquipment __instance, ushort __state)
            {
                if (__state == 0 && __instance.itemID == 0)
                    return;

                if (__state != 0)
                {
                    OnItemUnequipped?.Invoke(__instance.player);
                }

                if (__instance.useable != null)
                {
                    OnItemEquipped?.Invoke(__instance.player);
                }
            }
            // ReSharper restore InconsistentNaming
        }
    }
}

﻿extern alias JetBrainsAnnotations;
using HarmonyLib;
using JetBrainsAnnotations::JetBrains.Annotations;
using OpenMod.UnityEngine.Extensions;
using OpenMod.Unturned.Events;
using SDG.Unturned;
using System;
using UnityEngine;

namespace OpenMod.Unturned.Players.Movement.Events
{
    [UsedImplicitly]
    internal class PlayerMovementEventsListener : UnturnedPlayerEventsListener
    {
        public PlayerMovementEventsListener(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public override void Subscribe()
        {
            OnGestureUpdated += Events_OnGestureUpdated;
            OnTeleporting += Events_OnTeleporting;
        }

        public override void Unsubscribe()
        {
            OnGestureUpdated -= Events_OnGestureUpdated;
            OnTeleporting -= Events_OnTeleporting;
        }

        public override void SubscribePlayer(Player player)
        {
            player.stance.onStanceUpdated += () => OnStanceUpdated(player);
        }

        public override void UnsubscribePlayer(Player player)
        {
            player.stance.onStanceUpdated -= () => OnStanceUpdated(player);
        }

        private void Events_OnTeleporting(Player nativePlayer, ref Vector3 position, ref float yaw, ref bool cancel) // lgtm [cs/too-many-ref-parameters]
        {
            var player = GetUnturnedPlayer(nativePlayer)!;

            var @event = new UnturnedPlayerTeleportingEvent(player, position.ToSystemVector(), yaw)
            {
                IsCancelled = cancel
            };

            Emit(@event);

            position = @event.Position.ToUnityVector();
            yaw = @event.Yaw;
            cancel = @event.IsCancelled;
        }

        private void OnStanceUpdated(Player nativePlayer)
        {
            var player = GetUnturnedPlayer(nativePlayer)!;

            var @event = new UnturnedPlayerStanceUpdatedEvent(player);

            Emit(@event);
        }

        private void Events_OnGestureUpdated(Player nativePlayer, EPlayerGesture gesture)
        {
            var player = GetUnturnedPlayer(nativePlayer)!;

            var @event = new UnturnedPlayerGestureUpdatedEvent(player, gesture);

            Emit(@event);
        }

        private delegate void GestureUpdated(Player player, EPlayerGesture gesture);
        private static event GestureUpdated? OnGestureUpdated;

        private delegate void Teleporting(Player player,
            ref Vector3 position, ref float yaw, ref bool cancel);
        private static event Teleporting? OnTeleporting;

        [UsedImplicitly]
        [HarmonyPatch]
        internal static class Patches
        {
            [UsedImplicitly]
            [HarmonyPatch(typeof(PlayerAnimator), "sendGesture")]
            [HarmonyPostfix]
            public static void SendGesture(PlayerAnimator __instance, EPlayerGesture gesture)
            {
                if (gesture == EPlayerGesture.REST_START && __instance.player.stance.stance != EPlayerStance.CROUCH)
                {
                    return;
                }

                OnGestureUpdated?.Invoke(__instance.player, gesture);
            }

            [UsedImplicitly]
            [HarmonyPatch(typeof(Player), "teleportToLocationUnsafe")]
            [HarmonyPrefix]
            public static bool TeleportToLocationUnsafe(Player __instance, ref Vector3 position, ref float yaw)
            {
                var cancel = false;

                OnTeleporting?.Invoke(__instance, ref position, ref yaw, ref cancel);

                return !cancel;
            }
        }
    }
}
﻿using Cysharp.Threading.Tasks;
using OpenMod.Extensions.Games.Abstractions.Items;
using SDG.Unturned;
using System.Threading.Tasks;

namespace OpenMod.Unturned.Items
{
    public class UnturnedInventoryItem : IInventoryItem
    {
        public UnturnedInventoryItem(UnturnedPlayerInventory inventory, ItemJar itemJar)
        {
            Inventory = inventory;
            ItemJar = itemJar;
            Item = new UnturnedItem(itemJar.item, () => DestroyAsync());
        }

        public UnturnedPlayerInventory Inventory { get; }

        public ItemJar ItemJar { get; }

        public IItem Item { get; }

        public Task DropAsync()
        {
            ItemManager.dropItem(ItemJar.item,
                Inventory.Player.transform.position + Inventory.Player.transform.forward * 0.5f,
                true, true, false);

            return DestroyAsync();
        }

        public Task<bool> DestroyAsync()
        {
            async UniTask<bool> DestroyTask()
            {
                await UniTask.SwitchToMainThread();

                byte? page = null;
                var index = -1;

                for (byte p = 0; p < PlayerInventory.PAGES - 2; p++)
                {
                    var itemJars = Inventory.Inventory.items[p].items;

                    if (itemJars == null) continue;

                    index = itemJars.IndexOf(ItemJar);

                    if (index < 0) continue;

                    page = p;
                    break;
                }

                if (page != null)
                    Inventory.Inventory.removeItem(page.Value, (byte)index);

                return true;
            }

            return DestroyTask().AsTask();
        }
    }
}
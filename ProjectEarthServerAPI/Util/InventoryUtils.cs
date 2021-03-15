﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ProjectEarthServerAPI.Models;
using ProjectEarthServerAPI.Models.Player;
using ProjectEarthServerAPI.Util;

namespace ProjectEarthServerAPI.Util
{
    /// <summary>
    /// Utilities for interfacing with Player Inventory
    /// </summary>
    public class InventoryUtils
    {

        public static InventoryUtilResult RemoveItemFromInv(string playerId, Guid itemIdToRemove,
            [Optional] string unstackableItemId, int countToRemove = 1)
        {
            var inv = ReadInventory(playerId);

            var itementry = inv.result.stackableItems.Find(match => match.id == itemIdToRemove);
            if (itementry != null)
            {
                if (countToRemove > itementry.owned)
                {
                    return InventoryUtilResult.NotEnoughItemsAvailable;
                }
                else
                {
                    itementry.owned -= countToRemove;
                    itementry.seen.on = DateTime.UtcNow;
                }
                WriteInventory(playerId, inv);
                return InventoryUtilResult.Success;
            }
            else
            {
                var unstackableItem = inv.result.nonStackableItems.Find(match => match.id == itemIdToRemove);
                if (unstackableItem != null)
                {
                    var instance = unstackableItem.instances.Find(match => match.id == unstackableItemId);
                    unstackableItem.instances.Remove(instance);
                    unstackableItem.seen.on = DateTime.UtcNow;

                    WriteInventory(playerId, inv);
                    return InventoryUtilResult.Success;
                }

                return InventoryUtilResult.ItemNotFoundInInv; // Item not in inventory, so not able to be removed
            }
        }

        public static Tuple<InventoryUtilResult, int> GetItemCountFromInv(string playerId, Guid itemId)
        {
            var inv = ReadInventory(playerId);;

            var itementry = inv.result.stackableItems.Find(match => match.id == itemId);

            if (itementry != null)
            {
                return new Tuple<InventoryUtilResult, int>(InventoryUtilResult.Success, itementry.owned);
            }
            else
            {
                var unstackableItem = inv.result.nonStackableItems.Find(match => match.id == itemId);
                if (unstackableItem != null)
                {
                    return new Tuple<InventoryUtilResult, int>(InventoryUtilResult.Success, 1); // unstackable Item, so count is always 1
                }
            }

            return new Tuple<InventoryUtilResult, int>(InventoryUtilResult.ItemNotFoundInInv, 0); // Item not in inventory, so count 0
        }

        public static InventoryUtilResult AddItemToInv(string playerId, Guid itemIdToAdd, int count = 1, bool isStackableItem = true, string instanceId = null)
        {
            try
            {
                var inv = ReadInventory(playerId);

                if (!isStackableItem)
                {
                    var itementry = inv.result.nonStackableItems.Find(match => match.id == itemIdToAdd);

                    if (itementry != null && instanceId != null)
                    {
                        itementry.instances.Add(new InventoryResponse.Instance{health = 100.00, id = instanceId});
                        itementry.seen.on = DateTime.UtcNow;
                    }
                    else
                    {
                        // TODO: Figure out what to do here. New instance somehow?
                    }
                }
                else
                {
                    var itementry = inv.result.stackableItems.Find(match => match.id == itemIdToAdd);

                    if (itementry != null)
                    {
                        itementry.owned += count;
                        itementry.seen.on = DateTime.UtcNow;
                    }
                    else
                    {
                        inv.result.stackableItems.Add(new InventoryResponse.StackableItem()
                        {
                            fragments = 1,
                            id = itemIdToAdd,
                            owned = count,
                            seen = new InventoryResponse.Seen(){on = DateTime.UtcNow},
                            unlocked = new InventoryResponse.Unlocked(){on = DateTime.UtcNow}
                        });
                    }
                }


                WriteInventory(playerId, inv);

                TokenUtils.AddItemToken(playerId, itemIdToAdd);

                Console.WriteLine($"Added item {itemIdToAdd} to inventory. User ID: {playerId}");
                return InventoryUtilResult.Success;

            }
            catch
            {
                Console.WriteLine($"Adding item to inventory failed! User ID: {playerId} Item to add: {itemIdToAdd}");
                return InventoryUtilResult.NoSpecificError;
            }
        }

        public static Tuple<InventoryUtilResult, double> EditHealthOfItem(string playerId, Guid itemId, string unstackableItemInstanceId, double newHealth) // TODO: Actually Edit lmao
        {
            try
            {
                var inv = ReadInventory(playerId);

                var unstackableItem = inv.result.nonStackableItems.Find(result => result.id == itemId);
                var unstackableItemInstance =
                    unstackableItem?.instances.Find(match => match.id == unstackableItemInstanceId);

                if (unstackableItemInstance != null)
                    return new Tuple<InventoryUtilResult, double>(InventoryUtilResult.Success,
                        unstackableItemInstance.health);

                return new Tuple<InventoryUtilResult, double>(InventoryUtilResult.UnstackableItemInstanceNotFound, 0.0);
            }
            catch
            {
                return new Tuple<InventoryUtilResult, double>(InventoryUtilResult.NoSpecificError, 0.0);
            }
        }

        public static Tuple<InventoryUtilResult, InventoryResponse.Hotbar[]> EditHotbar(string playerId, InventoryResponse.Hotbar[] newHotbar)
        {
            var inv = ReadInventory(playerId);

            for (int i = 0; i < inv.result.hotbar.Length - 1; i++)
            {
                if (newHotbar[i]?.id != inv.result.hotbar[i]?.id | newHotbar[i]?.count != inv.result.hotbar[i]?.count)
                {
                    if (newHotbar[i] == null)
                    {
                        if (inv.result.hotbar[i].instanceId == null)
                        {
                            AddItemToInv(playerId, inv.result.hotbar[i].id, inv.result.hotbar[i].count,
                                true);
                        }
                        else
                        {
                            AddItemToInv(playerId, inv.result.hotbar[i].id, 1, false, inv.result.hotbar[i].instanceId.id);
                        }
                    }
                    else
                    {
                        if (inv.result.hotbar[i] != null)
                        {
                            RemoveItemFromInv(playerId, newHotbar[i].id,
                                newHotbar[i].instanceId?.id, newHotbar[i].count - inv.result.hotbar[i].count);
                        }
                        else
                        {
                            RemoveItemFromInv(playerId, newHotbar[i].id,
                                newHotbar[i].instanceId?.id, newHotbar[i].count);
                        }
                    }
                }
            }
            var newinv = ReadInventory(playerId);
            newinv.result.hotbar = newHotbar;

            WriteInventory(playerId, newinv);

            return new Tuple<InventoryUtilResult, InventoryResponse.Hotbar[]>(InventoryUtilResult.Success, newHotbar);
        }

        /*
         * Theoretically we can just replace these function with their generic variants,
         * but I thought keeping them for ease of use would be nice.
         */

        public static InventoryResponse ReadInventory(string playerId)
        {
            return GenericUtils.ParseJsonFile<InventoryResponse>(playerId, "inventory");
        }

        private static bool WriteInventory(string playerId, InventoryResponse inv)
        {
            return GenericUtils.WriteJsonFile(playerId, inv, "inventory");
        }

        public enum InventoryUtilResult
        {
            Success = 1,
            NotEnoughItemsAvailable,
            ItemNotFoundInInv,
            InventoryCreated,
            NoSpecificError,
            UnstackableItemInstanceNotFound
        }
    }                                   
}

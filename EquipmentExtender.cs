using BepInEx;
using SpaceCraft; 
using System;
using HarmonyLib; 
using UnityEngine; 
using Unity.Netcode; 
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Linq; 
namespace ThePlanetCrafterEquipmentExtend
{
    /// <summary>
    /// EquipmentExtender is the main mod class for extending equipment functionality in The Planet Crafter.
    /// 
    /// Responsibilities:
    /// - Registers Harmony patches to modify equipment and inventory behavior.
    /// - Adds logic for custom equipment slot handling and restrictions.
    /// - Adjusts UI and inventory display for extended equipment.
    /// - Integrates with Unity and BepInEx plugin systems.
    /// </summary>
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)] // Registers this class as a BepInEx plugin
    public class EquipmentExtender : BaseUnityPlugin // Inherits from BaseUnityPlugin for Unity modding
    {
        public const string pluginGuid = "mindlated.theplanetcrafter.EquipmentExtend"; // Unique plugin identifier
        public const string pluginName = "EquipmentExtend"; // Plugin name
        public const string pluginVersion = "0.1.0"; // Plugin version

        /// <summary>
        /// Called by Unity when the plugin is loaded. Registers Harmony patches.
        /// </summary>
        public void Awake()
        {
            Harmony.CreateAndPatchAll(typeof(EquipmentExtender), null); // Register all Harmony patches in this class
        }

        /// <summary>
        /// Harmony prefix patch for Inventory.HasSameEquipableTypeItemInEquipment.
        /// Prevents equipping multiple items of the same type, except for backpacks and oxygen tanks.
        /// </summary>
        /// <param name="inventory">The inventory to check.</param>
        /// <param name="groupItemClicked">The item being equipped.</param>
        /// <returns>True if an item of the same type exists (except for special cases), false otherwise.</returns>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Inventory), "HasSameEquipableTypeItemInEquipment")]
        public static bool HasSameEquipableTypeItemInEquipment_Patch(Inventory inventory, GroupItem groupItemClicked)
        {
            // If the item is not equipable, allow
            if (groupItemClicked.GetEquipableType() == DataConfig.EquipableType.Null)
            {
                return false;
            }
            foreach (WorldObject obj in inventory.GetInsideWorldObjects())
            {
                var type = ((GroupItem)obj.GetGroup()).GetEquipableType();
                if (type == groupItemClicked.GetEquipableType() && IsEquipableTypeUnique(type))
                {
                    return true; // Found a duplicate type
                }
            }
            return false; // No duplicate found
        }

        /// <summary>
        /// Returns true if the equipable type should be unique (not stackable).
        /// </summary>
        private static bool IsEquipableTypeUnique(DataConfig.EquipableType type)
        {
            // Allow multiple BackpackIncrease, OxygenTank, and Speed (exoskeleton) items
            return type != DataConfig.EquipableType.BackpackIncrease
                && type != DataConfig.EquipableType.OxygenTank
                && type != DataConfig.EquipableType.Speed
                && type != DataConfig.EquipableType.EquipmentIncrease;
        }

        /// <summary>
        /// Harmony postfix patch for PlayerEquipment.UpdateAfterEquipmentChange.
        /// Updates the player's RunSpeed based on all equipped speed items.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerEquipment), "UpdateAfterEquipmentChange")]
        public static void UpdateAfterEquipmentChange_Postfix(PlayerEquipment __instance)
        {
            // Update RunSpeed with all equipped speed boots (as before)
            var playerMovable = __instance.GetComponent<PlayerMovable>();
            var equipmentInventory = Traverse.Create(__instance).Field("equipmentInventory").GetValue<Inventory>();
            if (playerMovable != null && equipmentInventory != null)
            {
                float baseRunSpeed = 9f; // Default base run speed, adjust as needed
                float totalPercent = 0f;

                foreach (WorldObject obj in equipmentInventory.GetInsideWorldObjects())
                {
                    var groupItem = obj.GetGroup() as GroupItem;
                    if (groupItem != null && groupItem.GetEquipableType() == DataConfig.EquipableType.Speed)
                    {
                        int tier = 0;
                        if (int.TryParse(groupItem.id.Substring(groupItem.id.Length - 1), out tier))
                        {
                            if (tier == 1) totalPercent += 15f;
                            else if (tier == 2) totalPercent += 30f;
                            else if (tier == 3) totalPercent += 45f;
                        }
                    }
                }
                playerMovable.RunSpeed = baseRunSpeed * (1f + totalPercent / 100f);
            }

            // Update equipment inventory size based on all EquipmentIncrease items
            if (equipmentInventory != null)
            {
                int baseEquipmentSize = 4; // Default base equipment slots (no EquipmentIncrease)
                int totalIncrease = 0;
                foreach (WorldObject obj in equipmentInventory.GetInsideWorldObjects())
                {
                    var groupItem = obj.GetGroup() as GroupItem;
                    if (groupItem != null && groupItem.GetEquipableType() == DataConfig.EquipableType.EquipmentIncrease)
                    {
                        // Extract tier from groupItem.id (last char)
                        int tier = 0;
                        if (int.TryParse(groupItem.id.Substring(groupItem.id.Length - 1), out tier))
                        {
                            if (tier == 1) totalIncrease += 4;
                            else if (tier == 2) totalIncrease += 8;
                            else if (tier == 3) totalIncrease += 12;
                            else if (tier == 4) totalIncrease += 16;
                        }
                        else
                        {
                            // fallback: use group value if tier not found
                            totalIncrease += groupItem.GetGroupValue();
                        }
                    }
                }
                int newSize = baseEquipmentSize + totalIncrease;
                // Only set if different to avoid unnecessary UI refreshes
                if (equipmentInventory.GetSize() != newSize)
                {
                    equipmentInventory.SetSize(newSize);
                    equipmentInventory.RefreshDisplayerContent();
                }
            }
        }

        /// <summary>
        /// Harmony prefix patch for PlayerEquipment.RemoveItemFromEquipment.
        /// Custom logic for removing equipment, especially handling equipment and backpack dependencies.
        /// </summary>
        /// <param name="worldObject">The equipment item to remove.</param>
        /// <param name="__instance">The PlayerEquipment instance.</param>
        /// <returns>False to skip the original method, true to continue.</returns>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(PlayerEquipment), "RemoveItemFromEquipment")]
        static bool Prefix(WorldObject worldObject, PlayerEquipment __instance)
        {

            // Call the private method DefinePlayerInventory to ensure inventories are set up
            Traverse.Create(__instance).Method("DefinePlayerInventory").GetValue();

            // Access the `equipmentInventory` and `playerInventory` fields via Traverse (reflection)
            var equipmentInventory = Traverse.Create(__instance).Field("equipmentInventory").GetValue<Inventory>();
            var playerInventory = Traverse.Create(__instance).Field("playerInventory").GetValue<Inventory>();

            // Custom logic for removing equipment
            if (equipmentInventory.GetInsideWorldObjects().Contains(worldObject))
            {

                // Check if the item is an EquipmentIncrease type
                var isEquipmentIncrease = ((GroupItem)worldObject.GetGroup()).GetEquipableType() == DataConfig.EquipableType.EquipmentIncrease;
                var hasBackpacksOn = false;
                foreach (WorldObject insideWorldObject in equipmentInventory.GetInsideWorldObjects())
                {
                    GroupItem groupItem = (GroupItem)insideWorldObject.GetGroup();

                    // Check if any equipped item is a BackpackIncrease
                    if (groupItem.GetEquipableType() == DataConfig.EquipableType.BackpackIncrease)
                    {
                        hasBackpacksOn = true;
                    }
                }
            
                // Prevent removing EquipmentIncrease if backpacks are still equipped
                if (isEquipmentIncrease && hasBackpacksOn)
                {
                    InformationsDisplayer informationsDisplayer = Managers.GetManager<DisplayersHandler>().GetInformationsDisplayer();
                    float informationTime = 2.5f;
                    informationsDisplayer.AddInformation(informationTime, "Unequip all backpacks first", DataConfig.UiInformationsType.Tutorial, null);
                    Managers.GetManager<GlobalAudioHandler>().PlayCantDo();
                    return false; // Block removal
                }

                // Transfer the item from equipment to player inventory
                InventoriesHandler.Instance.TransferItem(equipmentInventory, playerInventory, worldObject, delegate (bool success)
                {
                    if (success)
                    {
                        // Update after successful equipment change
                        Traverse.Create(__instance).Method("UpdateAfterEquipmentChange", worldObject, false, false).GetValue();
                    }
                    else
                    {
                        // Fallback: check and dirty inventories
                        InventoriesHandler.Instance.CheckInventoryWatchAndDirty(playerInventory);
                        InventoriesHandler.Instance.CheckInventoryWatchAndDirty(equipmentInventory);
                    }
                });
            }

            // Skip the original method
            return false;
        }

        /// <summary>
        /// Harmony finalizer patch for PlayerGaugesHandler.UpdateGaugesDependingOnEquipmentServerRpc.
        /// Adjusts oxygen gauge values based on equipped oxygen tanks.
        /// </summary>
        /// <param name="inventoryId">Inventory ID to update.</param>
        /// <param name="____initialValue">Initial oxygen value.</param>
        /// <param name="____oxygenValue">Current oxygen value (networked).</param>
        /// <param name="____oxygenMaxValue">Max oxygen value (networked).</param>
        [HarmonyFinalizer]
        [HarmonyPatch(typeof(PlayerGaugesHandler), "UpdateGaugesDependingOnEquipmentServerRpc")]
        public static void UpdateGaugesDependingOnEquipmentServerRpc_Patch(int inventoryId, float ____initialValue, NetworkVariable<float> ____oxygenValue, NetworkVariable<float> ____oxygenMaxValue)
        {
            Inventory inventoryById = InventoriesHandler.Instance.GetInventoryById(inventoryId); // Get inventory by ID
            float num = ____initialValue; // Start with initial value
            foreach (WorldObject insideWorldObject in inventoryById.GetInsideWorldObjects())
            {
                GroupItem groupItem = (GroupItem)insideWorldObject.GetGroup();
                // Add value for each equipped oxygen tank
                if (groupItem.GetEquipableType() == DataConfig.EquipableType.OxygenTank)
                {
                    num = num + (float)groupItem.GetGroupValue();
                }
            }

            // Clamp current oxygen value to max
            if (____oxygenValue.Value > num)
            {
                ____oxygenValue.Value = num;
            }

            // Set max oxygen value
            ____oxygenMaxValue.Value = num;
        }

        /// <summary>
        /// Harmony postfix patch for InventoryDisplayer.TrueRefreshContent.
        /// Adjusts the inventory UI grid and adds custom scrollbars for large inventories.
        /// </summary>
        /// <param name="__instance">InventoryDisplayer instance.</param>
        /// <param name="____grid">GridLayoutGroup for inventory UI.</param>
        /// <param name="____inventory">Inventory being displayed.</param>
        /// <param name="____originalSizeDelta">Original UI size delta.</param>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(InventoryDisplayer), "TrueRefreshContent")]
        public static void TrueRefreshContent(InventoryDisplayer __instance, GridLayoutGroup ____grid, Inventory ____inventory, ref Vector2 ____originalSizeDelta)
        {
            if (____inventory.GetSize() < 29) // Only modify for large inventories
            {
                return;
            }
            string name = __instance.transform.parent.name; // Get parent name
            int num = 6; // Default grid columns
            float num2 = 1f; // UI scaling factor
            int num3 = (num < 6) ? 6 : num; // Ensure minimum columns
            if (num3 > 7)
            {
                // Adjust cell size for wide grids
                float num4 = (545f - (float)num3 * ____grid.spacing.x) / (float)num3;
                ____grid.cellSize = new Vector2(num4, num4);
                foreach (object obj in ____grid.transform)
                {
                    Transform transform = ((Transform)obj).Find("Drop");
                    float num5 = num4 * 1.45f / 100f;
                    transform.GetComponent<RectTransform>().localScale = new Vector3(num5, num5, 0f);
                    transform.GetComponent<RectTransform>().pivot = new Vector2(1f, 0f);
                }
            }
            int num6 = (int)Math.Ceiling((double)((float)____inventory.GetSize() / (float)num3)); // Rows needed
            float num7 = (float)num3 * (____grid.cellSize.x + ____grid.spacing.x); // Total width
            float num8 = (float)num6 * (____grid.cellSize.y + ____grid.spacing.y); // Total height
            if (num8 > __instance.GetComponent<RectTransform>().sizeDelta.y)
            {
                num8 = __instance.GetComponent<RectTransform>().sizeDelta.y;
            }
            UiWindowContainer uiWindowContainer = (UiWindowContainer)Managers.GetManager<WindowsHandler>().GetWindowViaUiId(DataConfig.UiType.Container);
            Transform transform2 = __instance.transform.parent.parent.Find("GroupLoading");
            if (transform2 != null && transform2.gameObject.activeInHierarchy && __instance.transform.parent.name.ToString() != "PlayerInventoryContainer")
            {
                num8 = __instance.GetComponent<RectTransform>().sizeDelta.y - 260f;
            }
            Vector2 vector = new Vector2(num7 + 20f, num8 + 5f); // New size for grid
            if (____grid.transform.parent.name != "ViewPort")
            {
                // Create a new ScrollView for the grid
                GameObject gameObject = new GameObject
                {
                    name = "ScrollView"
                };
                gameObject.transform.SetParent(____grid.transform.parent);
                RectTransform rectTransform = gameObject.AddComponent<RectTransform>();
                rectTransform.localScale = ____grid.transform.localScale;
                rectTransform.sizeDelta = vector;
                rectTransform.anchorMax = new Vector2(1f, 1f);
                rectTransform.anchorMin = new Vector2(0f, 0f);
                rectTransform.anchoredPosition = new Vector2(0f, -50f);
                GameObject gameObject2 = new GameObject
                {
                    name = "ViewPort"
                };
                gameObject2.transform.SetParent(gameObject.transform);
                RectTransform rectTransform2 = gameObject2.AddComponent<RectTransform>();
                rectTransform2.anchoredPosition = Vector2.zero;
                rectTransform2.localScale = rectTransform.localScale;
                rectTransform2.sizeDelta = new Vector2(vector.x, vector.y * num2);
                GameObject gameObject3 = EquipmentExtender.AddScrollbar(gameObject2, "ScrollbarVertical");
                RectTransform component = gameObject3.GetComponent<RectTransform>();
                component.localScale = rectTransform.localScale;
                component.anchorMin = new Vector2(1f, 0f);
                component.anchorMax = Vector2.one;
                component.pivot = Vector2.one;
                component.anchoredPosition = Vector2.zero;
                component.sizeDelta = new Vector2(10f, 17f);
                Image image = gameObject2.AddComponent<Image>();
                Texture2D texture2D = new Texture2D((int)Mathf.Ceil(rectTransform2.rect.width), (int)Mathf.Ceil(rectTransform2.rect.height));
                image.sprite = Sprite.Create(texture2D, new Rect(0f, 0f, (float)texture2D.width, (float)texture2D.height), Vector2.zero);
                ____grid.transform.SetParent(gameObject2.transform);
                RectTransform component2 = ____grid.GetComponent<RectTransform>();
                component2.anchorMax = new Vector2(0.5f, 0.5f);
                component2.anchorMin = new Vector2(0.5f, 0.5f);
                component2.sizeDelta = new Vector2(num7, (float)num6 * (____grid.cellSize.y + ____grid.spacing.y));
                gameObject2.AddComponent<Mask>().showMaskGraphic = false;
                ScrollRect scrollRect = gameObject.AddComponent<ScrollRect>();
                scrollRect.movementType = ScrollRect.MovementType.Clamped;
                scrollRect.horizontal = false;
                scrollRect.viewport = gameObject2.GetComponent<RectTransform>();
                scrollRect.content = ____grid.transform.GetComponent<RectTransform>();
                scrollRect.verticalScrollbar = gameObject3.GetComponent<Scrollbar>();
                scrollRect.verticalScrollbar.direction = Scrollbar.Direction.BottomToTop;
                scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
                scrollRect.verticalScrollbarSpacing = -3f;
                scrollRect.verticalNormalizedPosition = 1f;
                scrollRect.scrollSensitivity = 10f;
            }
            else
            {
                ____grid.GetComponent<RectTransform>().sizeDelta = new Vector2(num7, (float)num6 * (____grid.cellSize.y + ____grid.spacing.y));
            }
            // Replace EventTrigger with MyEventTrigger for all grid children
            foreach (EventTrigger eventTrigger in ____grid.transform.GetComponentsInChildren<EventTrigger>(true))
            {
                eventTrigger.gameObject.AddComponent<MyEventTrigger>().triggers.AddRange(eventTrigger.triggers);
                UnityEngine.Object.DestroyImmediate(eventTrigger);
            }
            if (____grid.transform.parent.name == "ViewPort")
            {
                __instance.StopAllCoroutines();
                Vector3 position = ____grid.transform.parent.GetChild(0).GetComponent<RectTransform>().position;
                __instance.iconsContainer.GetComponent<RectTransform>().position = new Vector3(position.x - (num7 - 40f) * ____grid.transform.lossyScale.x, position.y - 50f * ____grid.transform.lossyScale.y, position.z);
            }
        }

        /// <summary>
        /// Adds a vertical scrollbar to a UI parent object.
        /// </summary>
        /// <param name="obj_parent">The parent GameObject to add the scrollbar to.</param>
        /// <param name="scrollbar_name">The name for the scrollbar GameObject.</param>
        /// <returns>The created scrollbar GameObject.</returns>
        public static GameObject AddScrollbar(GameObject obj_parent, string scrollbar_name)
        {
            GameObject gameObject = new GameObject(scrollbar_name); // Create scrollbar object
            gameObject.transform.SetParent(obj_parent.transform);
            gameObject.layer = LayerMask.NameToLayer("UI");
            RectTransform rectTransform = gameObject.AddComponent<RectTransform>();
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = new Vector3(20f, 20f);
            Texture2D texture2D = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            Color[] pixels = Enumerable.Repeat<Color>(new Color(0.5f, 0.5f, 0.5f, 0.5f), 1).ToArray<Color>();
            texture2D.SetPixels(pixels);
            texture2D.Apply();
            Image image = gameObject.AddComponent<Image>();
            image.sprite = Sprite.Create(texture2D, new Rect(0f, 0f, (float)texture2D.width, (float)texture2D.height), Vector2.zero);
            image.type = Image.Type.Sliced;
            Scrollbar scrollbar = gameObject.AddComponent<Scrollbar>();
            GameObject gameObject2 = new GameObject("Sliding Area");
            gameObject2.transform.SetParent(gameObject.transform);
            gameObject2.layer = LayerMask.NameToLayer("UI");
            RectTransform rectTransform2 = gameObject2.AddComponent<RectTransform>();
            rectTransform2.anchoredPosition = Vector2.zero;
            rectTransform2.anchorMin = Vector2.zero;
            rectTransform2.anchorMax = Vector2.one;
            rectTransform2.sizeDelta = new Vector2(-20f, -20f);
            GameObject gameObject3 = new GameObject("Handle");
            gameObject3.transform.SetParent(gameObject2.transform);
            gameObject3.layer = LayerMask.NameToLayer("UI");
            RectTransform rectTransform3 = gameObject3.AddComponent<RectTransform>();
            rectTransform3.anchorMin = Vector2.zero;
            rectTransform3.anchorMax = new Vector2(0.2f, 1f);
            rectTransform3.anchoredPosition = Vector2.zero;
            rectTransform3.sizeDelta = new Vector2(20f, 20f);
            texture2D = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            pixels = Enumerable.Repeat<Color>(new Color(1f, 1f, 1f, 1f), 1).ToArray<Color>();
            texture2D.SetPixels(pixels);
            texture2D.Apply();
            Image image2 = gameObject3.AddComponent<Image>();
            image2.sprite = Sprite.Create(texture2D, new Rect(0f, 0f, (float)texture2D.width, (float)texture2D.height), Vector2.zero);
            image2.type = Image.Type.Sliced;
            scrollbar.targetGraphic = image2;
            scrollbar.handleRect = rectTransform3;
            return gameObject;
        }
    }
}

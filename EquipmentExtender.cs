using BepInEx;
using SpaceCraft;
using System;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.EventSystems;
using System.Linq;

namespace ThePlanetCrafterEquipmentExtend
{
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class EquipmentExtender : BaseUnityPlugin
    {
        public const string pluginGuid = "mindlated.theplanetcrafter.EquipmentExtend";
        public const string pluginName = "EquipmentExtend";
        public const string pluginVersion = "0.2.0";

        public void Awake()
        {
            Harmony.CreateAndPatchAll(typeof(EquipmentExtender), null);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Inventory), "HasSameEquipableTypeItemInEquipment")]
        public static bool HasSameEquipableTypeItemInEquipment_Patch(Inventory inventory, GroupItem groupItemClicked)
        {
            if (groupItemClicked.GetEquipableType() == DataConfig.EquipableType.Null)
            {
                return false;
            }
            using (IEnumerator<WorldObject> enumerator = inventory.GetInsideWorldObjects().GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    var equipType = ((GroupItem)enumerator.Current.GetGroup()).GetEquipableType();
                    // Ajoute ici d'autres types stackables si besoin (ex: Speed pour exosquelette)
                    if (equipType == groupItemClicked.GetEquipableType()
                        && equipType != DataConfig.EquipableType.BackpackIncrease
                        && equipType != DataConfig.EquipableType.OxygenTank
                        && equipType != DataConfig.EquipableType.Speed
                        && equipType != DataConfig.EquipableType.EquipmentIncrease)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PlayerEquipment), "RemoveItemFromEquipment")]
        static bool Prefix(WorldObject worldObject, PlayerEquipment __instance)
        {
            // Call the private method DefinePlayerInventory
            Traverse.Create(__instance).Method("DefinePlayerInventory").GetValue();

            // Access the `equipmentInventory` and `playerInventory` fields via Traverse
            var equipmentInventory = Traverse.Create(__instance).Field("equipmentInventory").GetValue<Inventory>();
            var playerInventory = Traverse.Create(__instance).Field("playerInventory").GetValue<Inventory>();

            if (equipmentInventory.GetInsideWorldObjects().Contains(worldObject))
            {
                var isEquipmentIncrease = ((GroupItem)worldObject.GetGroup()).GetEquipableType() == DataConfig.EquipableType.EquipmentIncrease;
                var hasBackpacksOn = false;
                foreach (WorldObject insideWorldObject in equipmentInventory.GetInsideWorldObjects())
                {
                    GroupItem groupItem = (GroupItem)insideWorldObject.GetGroup();
                    if (groupItem.GetEquipableType() == DataConfig.EquipableType.BackpackIncrease)
                    {
                        hasBackpacksOn = true;
                    }
                }
                if (isEquipmentIncrease && hasBackpacksOn)
                {
                    InformationsDisplayer informationsDisplayer = Managers.GetManager<DisplayersHandler>().GetInformationsDisplayer();
                    float informationTime = 2.5f;
                    informationsDisplayer.AddInformation(informationTime, "Unequip all backpacks first", DataConfig.UiInformationsType.Tutorial, null);
                    Managers.GetManager<GlobalAudioHandler>().PlayCantDo();
                    return false;
                }
                InventoriesHandler.Instance.TransferItem(equipmentInventory, playerInventory, worldObject, delegate (bool success)
                {
                    if (success)
                    {
                        Traverse.Create(__instance).Method("UpdateAfterEquipmentChange", worldObject, false, false).GetValue();
                    }
                    else
                    {
                        InventoriesHandler.Instance.CheckInventoryWatchAndDirty(playerInventory);
                        InventoriesHandler.Instance.CheckInventoryWatchAndDirty(equipmentInventory);
                    }
                });
            }
            // Skip the original method
            return false;
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(PlayerGaugesHandler), "UpdateGaugesDependingOnEquipmentServerRpc")]
        public static void UpdateGaugesDependingOnEquipmentServerRpc_Patch(int inventoryId, float ____initialValue, NetworkVariable<float> ____oxygenValue, NetworkVariable<float> ____oxygenMaxValue)
        {
            Inventory inventoryById = InventoriesHandler.Instance.GetInventoryById(inventoryId);
            float num = ____initialValue;
            foreach (WorldObject insideWorldObject in inventoryById.GetInsideWorldObjects())
            {
                GroupItem groupItem = (GroupItem)insideWorldObject.GetGroup();
                if (groupItem.GetEquipableType() == DataConfig.EquipableType.OxygenTank)
                {
                    num = num + (float)groupItem.GetGroupValue();
                }
            }
            if (____oxygenValue.Value > num)
            {
                ____oxygenValue.Value = num;
            }
            ____oxygenMaxValue.Value = num;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(InventoryDisplayer), "TrueRefreshContent")]
        public static void TrueRefreshContent(InventoryDisplayer __instance, GridLayoutGroup ____grid, Inventory ____inventory, ref Vector2 ____originalSizeDelta)
        {
            if (____inventory.GetSize() < 29)
            {
                return;
            }
            string name = __instance.transform.parent.name;
            int num = 6;
            float num2 = 1f;
            int num3 = (num < 6) ? 6 : num;
            if (num3 > 7)
            {
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
            int num6 = (int)Math.Ceiling((double)((float)____inventory.GetSize() / (float)num3));
            float num7 = (float)num3 * (____grid.cellSize.x + ____grid.spacing.x);
            float num8 = (float)num6 * (____grid.cellSize.y + ____grid.spacing.y);
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
            Vector2 vector = new Vector2(num7 + 20f, num8 + 5f);
            if (____grid.transform.parent.name != "ViewPort")
            {
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

        public static GameObject AddScrollbar(GameObject obj_parent, string scrollbar_name)
        {
            GameObject gameObject = new GameObject(scrollbar_name);
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

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerEquipment), "UpdateAfterEquipmentChange")]
        public static void UpdateAfterEquipmentChange_Postfix(PlayerEquipment __instance)
        {
            var equipmentInventory = Traverse.Create(__instance).Field("equipmentInventory").GetValue<Inventory>();
            var playerMovable = __instance.GetComponent<PlayerMovable>();
            if (equipmentInventory == null || playerMovable == null) return;

            float totalPercent = 0f;
            foreach (var obj in equipmentInventory.GetInsideWorldObjects())
            {
                var groupItem = obj.GetGroup() as GroupItem;
                if (groupItem != null && groupItem.GetEquipableType() == DataConfig.EquipableType.Speed)
                {
                    totalPercent += groupItem.GetGroupValue();
                }
            }
            playerMovable.SetMoveSpeedChangePercentage(totalPercent);
        }
    }
}

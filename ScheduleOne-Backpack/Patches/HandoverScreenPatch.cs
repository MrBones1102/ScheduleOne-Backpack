using Backpack.Config;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

#if IL2CPP
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Handover;
using Il2CppScheduleOne.UI.Items;
using Il2CppTMPro;
using SlotList = Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot>;
#elif MONO
using ScheduleOne.DevUtilities;
using ScheduleOne.ItemFramework;
using ScheduleOne.UI;
using ScheduleOne.UI.Handover;
using ScheduleOne.UI.Items;
using TMPro;
using SlotList = System.Collections.Generic.List<ScheduleOne.ItemFramework.ItemSlot>;
#endif

namespace Backpack.Patches;

/// <summary>
/// Adds a "Backpack" panel to the deal/handover screen, next to the game's own vehicle panel,
/// so items can be handed over directly from the backpack.
/// The panel is a clone of the vehicle panel, so it inherits its look and drag/drop behaviour.
/// </summary>
[HarmonyPatch(typeof(HandoverScreen))]
public static class HandoverScreenPatch
{
    private const string ContainerName = "BackpackContainer";
    private const float PanelSpacing = 10f;

    private static RectTransform _container;
    private static Vector2 _vehiclePosition;
    private static readonly List<ItemSlotUI> SlotUIs = [];
    internal static SlotList BoundSlots { get; private set; }

    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    public static void Start(HandoverScreen __instance)
    {
        try
        {
            BuildContainer(__instance);
        }
        catch (Exception e)
        {
            Logger.Error(e, "Failed to create backpack panel for the handover screen.");
        }
    }

    [HarmonyPatch("OnOpen")]
    [HarmonyPostfix]
    public static void OnOpen(HandoverScreen __instance)
    {
        try
        {
            Show(__instance);
        }
        catch (Exception e)
        {
            Logger.Error(e, "Failed to show backpack panel on the handover screen.");
        }
    }

    [HarmonyPatch("OnClose")]
    [HarmonyPrefix]
    public static void OnClose()
    {
        try
        {
            Hide();
        }
        catch (Exception e)
        {
            Logger.Error(e, "Failed to hide backpack panel on the handover screen.");
        }
    }

    private static void BuildContainer(HandoverScreen screen)
    {
        if (_container != null)
            return;

        var template = screen._vehicleContainer;
        var templateSlots = screen._vehicleSlotUIs;
        if (template == null || templateSlots == null || templateSlots.Length == 0)
        {
            Logger.Warning("Handover screen has no vehicle panel to copy; selling from the backpack is unavailable.");
            return;
        }

        // Resolve the template's slot UIs and subtitle to paths before cloning. Slot objects usually
        // share a name, so the paths use sibling indices instead of names.
        var slotPaths = new List<int[]>();
        for (var i = 0; i < templateSlots.Length; i++)
        {
            var path = templateSlots[i] == null ? null : GetIndexPath(templateSlots[i].transform, template);
            if (path != null)
                slotPaths.Add(path);
        }

        var subtitlePath = screen._vehicleSubtitle == null ? null : GetIndexPath(screen._vehicleSubtitle.transform, template);

        var clone = UnityEngine.Object.Instantiate(template.gameObject, template.parent);
        clone.name = ContainerName;
        clone.SetActive(false);
        _container = clone.GetComponent<RectTransform>();
        _vehiclePosition = template.anchoredPosition;

        SlotUIs.Clear();
        foreach (var path in slotPaths)
        {
            var slotUI = FollowIndexPath(clone.transform, path)?.GetComponent<ItemSlotUI>();
            if (slotUI == null)
                continue;

            ClearItemVisuals(slotUI);
            SlotUIs.Add(slotUI);
        }

        if (subtitlePath != null)
        {
            var subtitle = FollowIndexPath(clone.transform, subtitlePath)?.GetComponent<TextMeshProUGUI>();
            if (subtitle != null)
                subtitle.text = PlayerBackpack.StorageName;
        }

        // Relabel any other "Vehicle" captions in the copied panel.
        foreach (var label in clone.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (label.text != null && label.text.IndexOf("vehicle", StringComparison.OrdinalIgnoreCase) >= 0)
                label.text = PlayerBackpack.StorageName;
        }

        Logger.Info($"Created backpack panel for the handover screen with {SlotUIs.Count} slots.");
        Logger.Debug($"Vehicle panel: pos={template.anchoredPosition} size={template.rect.size} anchors={template.anchorMin}-{template.anchorMax} pivot={template.pivot} slotParentGrid={SlotUIs.FirstOrDefault()?.transform.parent.GetComponent<GridLayoutGroup>() != null}");
    }

    private static void Show(HandoverScreen screen)
    {
        Hide();

        var backpack = PlayerBackpack.Instance;
        if (backpack == null || !backpack.IsAvailable || !Configuration.Instance.CanSellFromBackpack)
            return;

        if (_container == null)
            BuildContainer(screen);
        if (_container == null || SlotUIs.Count == 0)
            return;

        var slots = backpack.ItemSlots;
        EnsureSlotUICount(slots.Count);

        for (var i = 0; i < SlotUIs.Count; i++)
        {
            var slotUI = SlotUIs[i];
            if (i < slots.Count)
            {
                slotUI.gameObject.SetActive(true);
                slotUI.AssignSlot(slots[i]);
            }
            else
            {
                slotUI.gameObject.SetActive(false);
            }
        }

        BoundSlots = slots;
        _container.gameObject.SetActive(true);
        Position(screen);

        var itemUIManager = Singleton<ItemUIManager>.Instance;
        if (itemUIManager != null && itemUIManager.QuickMoveEnabled)
            AddToQuickMove(itemUIManager, slots);
    }

    private static void Hide()
    {
        if (BoundSlots == null)
            return;

        BoundSlots = null;
        foreach (var slotUI in SlotUIs)
        {
            if (slotUI != null && slotUI.assignedSlot != null)
                slotUI.ClearSlot();
        }

        if (_container != null)
            _container.gameObject.SetActive(false);
    }

    /// <summary>
    /// Shows the backpack where the vehicle panel would be, or directly below it when a vehicle is nearby.
    /// </summary>
    private static void Position(HandoverScreen screen)
    {
        var vehicle = screen._vehicleContainer;
        if (vehicle == null || !vehicle.gameObject.activeSelf)
        {
            _container.anchoredPosition = _vehiclePosition;
            return;
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(vehicle);
        _container.anchoredPosition = vehicle.anchoredPosition - new Vector2(0f, vehicle.rect.height + PanelSpacing);
    }

    private static void EnsureSlotUICount(int count)
    {
        var prefab = SlotUIs[0];
        while (SlotUIs.Count < count)
        {
            var slotObject = UnityEngine.Object.Instantiate(prefab.gameObject, prefab.transform.parent);
            slotObject.name = $"{prefab.name} ({SlotUIs.Count})";
            var slotUI = slotObject.GetComponent<ItemSlotUI>();
            ClearItemVisuals(slotUI);
            SlotUIs.Add(slotUI);
        }
    }

    internal static void AddToQuickMove(ItemUIManager manager, SlotList slots)
    {
        var primary = manager.PrimarySlots;
        if (primary == null)
            return;

        for (var i = 0; i < slots.Count; i++)
        {
            if (!primary.Contains(slots[i]))
                primary.Add(slots[i]);
        }
    }

    /// <summary>
    /// Removes item icons that were copied along with the template slot.
    /// </summary>
    private static void ClearItemVisuals(ItemSlotUI slotUI)
    {
        var itemContainer = slotUI.ItemContainer;
        if (itemContainer == null)
            return;

        for (var i = itemContainer.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(itemContainer.GetChild(i).gameObject);
    }

    private static int[] GetIndexPath(Transform target, Transform root)
    {
        var path = new List<int>();
        for (var t = target; t != root; t = t.parent)
        {
            if (t == null)
                return null;
            path.Add(t.GetSiblingIndex());
        }

        path.Reverse();
        return path.ToArray();
    }

    private static Transform FollowIndexPath(Transform root, int[] path)
    {
        var t = root;
        foreach (var index in path)
        {
            if (index >= t.childCount)
                return null;
            t = t.GetChild(index);
        }

        return t;
    }
}

/// <summary>
/// Keeps the backpack in the quick-move (shift-click) set if the game re-enables quick move while the panel is open.
/// Separate class so a signature change here cannot take the handover patches down with it.
/// </summary>
[HarmonyPatch(typeof(ItemUIManager))]
public static class ItemUIManagerPatch
{
    [HarmonyPatch(nameof(ItemUIManager.EnableQuickMove), [typeof(SlotList), typeof(SlotList)])]
    [HarmonyPostfix]
    public static void EnableQuickMove(ItemUIManager __instance)
    {
        if (HandoverScreenPatch.BoundSlots != null)
            HandoverScreenPatch.AddToQuickMove(__instance, HandoverScreenPatch.BoundSlots);
    }
}

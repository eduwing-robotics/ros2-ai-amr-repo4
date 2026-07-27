using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class scr_Factory2DPalletMarkerController : MonoBehaviour
{
    [Serializable]
    public sealed class PalletSlotView
    {
        public string slotId;
        public GameObject full2DZoneRoot;
        public GameObject full2DPalletIcon;
        public GameObject miniMapZoneRoot;
        public GameObject miniMapPalletIcon;
    }

    [SerializeField] private PalletSlotView[] palletSlots = Array.Empty<PalletSlotView>();
    [SerializeField] private bool resetIconsOnEnable = true;

    private readonly Dictionary<string, PalletSlotView> slotsById =
        new Dictionary<string, PalletSlotView>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> occupiedBySlotId =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> reportedWarnings =
        new HashSet<string>(StringComparer.Ordinal);

    private bool initialized;

    private void Awake()
    {
        InitializeIfNeeded();
    }

    private void OnEnable()
    {
        if (!initialized)
        {
            InitializeIfNeeded();
            return;
        }

        SetAllZoneRootsVisible();
        ReapplyCachedStates();
    }

    public void ResetPalletMarkers()
    {
        BuildSlotLookup();
        occupiedBySlotId.Clear();
        SetAllZoneRootsVisible();

        for (int index = 0; index < palletSlots.Length; index++)
        {
            PalletSlotView slot = palletSlots[index];
            if (slot == null)
            {
                continue;
            }

            SetActiveIfChanged(slot.full2DPalletIcon, false);
            SetActiveIfChanged(slot.miniMapPalletIcon, false);
        }
    }

    public bool ApplyPalletSlotState(string slotId, bool occupied)
    {
        if (!TryGetSlot(slotId, out PalletSlotView slot))
        {
            ReportWarningOnce(
                $"unknown-slot:{slotId}",
                $"[Factory2DPallet] Unknown pallet slot '{slotId ?? "<null>"}'. State was ignored.");
            return false;
        }

        occupiedBySlotId[slot.slotId] = occupied;
        SetZoneRootsVisible(slot);
        SetActiveIfChanged(slot.full2DPalletIcon, occupied);
        SetActiveIfChanged(slot.miniMapPalletIcon, occupied);
        return true;
    }

    public bool TryApplyServerPalletState(string slotId, string serverState)
    {
        string normalizedState = NormalizeServerState(serverState);
        switch (normalizedState)
        {
            case "PLACED":
            case "OCCUPIED":
            case "LOADED_IN_SLOT":
                return ApplyPalletSlotState(slotId, true);

            case "REMOVED":
            case "EMPTY":
            case "PICKED_UP":
                return ApplyPalletSlotState(slotId, false);

            default:
                bool slotExists = TryGetSlot(slotId, out _);
                if (slotExists)
                {
                    ApplyPalletSlotState(slotId, false);
                }

                ReportWarningOnce(
                    $"unknown-state:{slotId}:{normalizedState}",
                    $"[Factory2DPallet] Pallet slot '{slotId ?? "<null>"}' has no explicit server state " +
                    $"('{serverState ?? "<null>"}'). The icon remains off.");
                return false;
        }
    }

    public void SetAllZoneRootsVisible()
    {
        for (int index = 0; index < palletSlots.Length; index++)
        {
            SetZoneRootsVisible(palletSlots[index]);
        }
    }

    private void InitializeIfNeeded()
    {
        if (initialized)
        {
            return;
        }

        BuildSlotLookup();
        ValidateSerializedReferences();
        initialized = true;

        if (resetIconsOnEnable)
        {
            ResetPalletMarkers();
        }
        else
        {
            SetAllZoneRootsVisible();
        }
    }

    private void BuildSlotLookup()
    {
        slotsById.Clear();
        if (palletSlots == null)
        {
            palletSlots = Array.Empty<PalletSlotView>();
            return;
        }

        for (int index = 0; index < palletSlots.Length; index++)
        {
            PalletSlotView slot = palletSlots[index];
            if (slot == null || string.IsNullOrWhiteSpace(slot.slotId))
            {
                continue;
            }

            string normalizedSlotId = slot.slotId.Trim();
            slot.slotId = normalizedSlotId;
            if (!slotsById.TryAdd(normalizedSlotId, slot))
            {
                ReportWarningOnce(
                    $"duplicate-slot:{normalizedSlotId}",
                    $"[Factory2DPallet] Duplicate pallet slot id '{normalizedSlotId}' was ignored.");
            }
        }
    }

    private bool TryGetSlot(string slotId, out PalletSlotView slot)
    {
        if (slotsById.Count == 0)
        {
            BuildSlotLookup();
        }

        if (string.IsNullOrWhiteSpace(slotId))
        {
            slot = null;
            return false;
        }

        return slotsById.TryGetValue(slotId.Trim(), out slot);
    }

    private void ReapplyCachedStates()
    {
        for (int index = 0; index < palletSlots.Length; index++)
        {
            PalletSlotView slot = palletSlots[index];
            if (slot == null || string.IsNullOrWhiteSpace(slot.slotId))
            {
                continue;
            }

            bool occupied = occupiedBySlotId.TryGetValue(slot.slotId, out bool cachedOccupied) &&
                            cachedOccupied;
            SetActiveIfChanged(slot.full2DPalletIcon, occupied);
            SetActiveIfChanged(slot.miniMapPalletIcon, occupied);
        }
    }

    private void ValidateSerializedReferences()
    {
        if (palletSlots.Length == 0)
        {
            ReportWarningOnce("no-slots", "[Factory2DPallet] No pallet slots are assigned.");
            return;
        }

        for (int index = 0; index < palletSlots.Length; index++)
        {
            PalletSlotView slot = palletSlots[index];
            if (slot == null)
            {
                ReportWarningOnce(
                    $"null-slot:{index}",
                    $"[Factory2DPallet] Pallet slot element {index} is null.");
                continue;
            }

            string displayId = string.IsNullOrWhiteSpace(slot.slotId) ? $"index {index}" : slot.slotId;
            ValidateReference(displayId, nameof(slot.full2DZoneRoot), slot.full2DZoneRoot);
            ValidateReference(displayId, nameof(slot.full2DPalletIcon), slot.full2DPalletIcon);

            if (slot.miniMapPalletIcon == null)
            {
                ReportWarningOnce(
                    $"missing:{displayId}:{nameof(slot.miniMapPalletIcon)}",
                    $"[Factory2DPallet] {displayId}: MiniMap pallet child Image is missing. " +
                    "No runtime object will be created.");
            }
            else
            {
                ValidateReference(displayId, nameof(slot.miniMapZoneRoot), slot.miniMapZoneRoot);
            }
        }
    }

    private void ValidateReference(string slotId, string fieldName, UnityEngine.Object reference)
    {
        if (reference != null)
        {
            return;
        }

        ReportWarningOnce(
            $"missing:{slotId}:{fieldName}",
            $"[Factory2DPallet] {slotId}: Serialized reference '{fieldName}' is missing.");
    }

    private static void SetZoneRootsVisible(PalletSlotView slot)
    {
        if (slot == null)
        {
            return;
        }

        SetActiveIfChanged(slot.full2DZoneRoot, true);
        SetActiveIfChanged(slot.miniMapZoneRoot, true);
    }

    private static void SetActiveIfChanged(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }

    private static string NormalizeServerState(string serverState)
    {
        if (string.IsNullOrWhiteSpace(serverState))
        {
            return string.Empty;
        }

        return serverState.Trim()
            .Replace('-', '_')
            .Replace(' ', '_')
            .ToUpperInvariant();
    }

    private void ReportWarningOnce(string key, string message)
    {
        if (reportedWarnings.Add(key))
        {
            Debug.LogWarning(message, this);
        }
    }
}

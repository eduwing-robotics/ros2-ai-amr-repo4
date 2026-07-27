using System;
using System.Collections.Generic;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class ControlTowerMapNavStatusData
{
    public int robot_id;
    public string map_id;
    public string localization_state;
    public string amcl_state;
    public bool initial_pose_set;
    public string localization_quality;
    public string scan_match_state;
    public string nav2_state;
    public string planner_state;
    public string controller_state;
    public int current_target_wp;
    public int current_wp_index;
    public int total_waypoints;
    public string route_state;
    public string goal_result;
    public int replan_count;
    public string updated_at;
    [NonSerialized] public bool has_initial_pose_set;
    [NonSerialized] public bool has_current_target_wp;
    [NonSerialized] public bool has_current_wp_index;
    [NonSerialized] public bool has_total_waypoints;
    [NonSerialized] public bool has_replan_count;
}

[Serializable]
public class ControlTowerWaypointRouteData
{
    public int robot_id;
    public string route_id;
    public string route_name;
    public int current_wp_index;
    public int total_waypoints;
    public string route_state;
    public ControlTowerWaypointData[] waypoints;
    [NonSerialized] public bool has_current_wp_index;
    [NonSerialized] public bool has_total_waypoints;
}

[Serializable]
public class ControlTowerWaypointData
{
    public string waypoint_id;
    public string waypoint_name;
    public int sequence;
    public float x;
    public float y;
    public string status;
}

[Serializable]
public class ControlTowerObstacleRecoveryData
{
    public int robot_id;
    public string obstacle_state;
    public string obstacle_type;
    public float obstacle_distance;
    public float obstacle_x;
    public float obstacle_y;
    public string recovery_state;
    public string recovery_behavior;
    public int recovery_retry_count;
    public string detected_at;
    public string updated_at;
    public string message;
    [NonSerialized] public bool has_obstacle_distance;
    [NonSerialized] public bool has_obstacle_x;
    [NonSerialized] public bool has_obstacle_y;
    [NonSerialized] public bool has_recovery_retry_count;
}

public class scr_MapStatusRouteController : MonoBehaviour
{
    private const int ExpectedWaypointSlotCount = 14;
    private const int ExpectedPathSegmentCount = 13;

    [SerializeField] private scr_FactoryFull2DMapController coordinateSource;
    [SerializeField] private RectTransform mapArea;
    [SerializeField] private GameObject waypointGroup;
    [SerializeField] private GameObject patrolPathGroup;
    [SerializeField] private GameObject robotMarkerGroup;
    [SerializeField] private RectTransform[] waypointSlots;
    [SerializeField] private TMP_Text[] waypointLabels;
    [SerializeField] private RectTransform[] pathSegments;
    [Header("Runtime Progress Highlight")]
    [SerializeField] private Color currentWaypointColor = new(1f, 0.9f, 0.1f, 1f);
    [SerializeField] private Color currentRouteColor = new(1f, 0.55f, 0.1f, 1f);
    [SerializeField] private Color completedWaypointColor = new(0.38f, 0.42f, 0.48f, 1f);
    [SerializeField] private Color completedRouteColor = new(0.26f, 0.3f, 0.36f, 1f);
    [SerializeField] private Color nextWaypointColor = new(0.35f, 0.7f, 1f, 1f);

    private readonly HashSet<string> overflowWarnings = new();
    private readonly List<ControlTowerWaypointData> orderedWaypointsCache = new();
    private ControlTowerWaypointRouteData currentRoute;
    private bool warnedMissingCoordinateSource;
    private bool hasRouteGeometryHash;
    private int routeGeometryHash;
    private bool referencesValidated;
    private bool[] validWaypointSlots = Array.Empty<bool>();
    private bool[] validWaypointLabels = Array.Empty<bool>();
    private bool[] validPathSegments = Array.Empty<bool>();
    private Vector2[] projectedMapLocalPositions = Array.Empty<Vector2>();
    private bool[] hasProjectedMapLocalPosition = Array.Empty<bool>();
    private Image[] waypointImages = Array.Empty<Image>();
    private Image[] pathSegmentImages = Array.Empty<Image>();
    private Color[] defaultWaypointColors = Array.Empty<Color>();
    private Color[] defaultRouteColors = Array.Empty<Color>();
    private bool visualColorsCached;
    private int lastProgressVisualHash = int.MinValue;
    private int lastActivationFrame = -1;
    private static readonly ProfilerMarker StatusRouteUpdateMarker = new("ControlTower.Map.StatusRoute.Update");

    public scr_FactoryFull2DMapController CoordinateSource => coordinateSource;

    private void OnEnable()
    {
        OnViewActivated();
    }

    public void OnViewActivated()
    {
        if (lastActivationFrame == Time.frameCount)
        {
            return;
        }

        lastActivationFrame = Time.frameCount;
        PrepareRuntimeReferences();
        PopulateOrderedWaypoints(currentRoute);
        RefreshRouteVisuals(!hasRouteGeometryHash);
    }

    public void ApplyRoute(ControlTowerWaypointRouteData route)
    {
        currentRoute = route;
        PrepareRuntimeReferences();
        int nextGeometryHash = ComputeRouteGeometryHash(route);
        bool geometryChanged = !hasRouteGeometryHash || nextGeometryHash != routeGeometryHash;
        hasRouteGeometryHash = true;
        routeGeometryHash = nextGeometryHash;
        PopulateOrderedWaypoints(route);
        RefreshRouteVisuals(geometryChanged);
    }

    public void ClearRoute()
    {
        currentRoute = null;
        orderedWaypointsCache.Clear();
        hasRouteGeometryHash = false;
        routeGeometryHash = 0;
        PrepareRuntimeReferences();
        HideRouteVisuals();
    }

    public void RefreshGeometryFromCalibrationChange()
    {
        PrepareRuntimeReferences();
        hasRouteGeometryHash = false;
        PopulateOrderedWaypoints(currentRoute);
        RefreshRouteVisuals(true);
    }

    private void PrepareRuntimeReferences()
    {
        ValidateInspectorReferencesOnce();
        EnsureParentGroupsActive();
    }

    private void RefreshRouteVisuals(bool refreshGeometry)
    {
        using (StatusRouteUpdateMarker.Auto())
        {
            RefreshRouteVisualsInternal(refreshGeometry);
        }
    }

    private void RefreshRouteVisualsInternal(bool refreshGeometry)
    {
        if (currentRoute == null || currentRoute.waypoints == null || currentRoute.waypoints.Length == 0)
        {
            HideRouteVisuals();
            return;
        }

        if (orderedWaypointsCache.Count == 0)
        {
            HideRouteVisuals();
            return;
        }

        if (coordinateSource == null)
        {
            HideRouteVisuals();
            if (!warnedMissingCoordinateSource)
            {
                Debug.LogWarning("[MapStatusRoute] Coordinate source is missing. Waypoint route cannot be projected.");
                warnedMissingCoordinateSource = true;
            }
            return;
        }

        EnsureParentGroupsActive();

        int slotCount = waypointSlots != null ? waypointSlots.Length : 0;
        int visibleCount = Mathf.Min(slotCount, orderedWaypointsCache.Count);
        int currentWaypointIndex = FindCurrentWaypointIndex();
        EnsureProjectionCacheCapacity(slotCount);
        if (refreshGeometry)
        {
            Array.Clear(hasProjectedMapLocalPosition, 0, hasProjectedMapLocalPosition.Length);
        }

        for (int i = 0; i < slotCount; i++)
        {
            if (!IsValidWaypointSlot(i))
            {
                continue;
            }

            bool visible = i < visibleCount;
            RectTransform slot = waypointSlots[i];
            if (slot.gameObject.activeSelf != visible)
            {
                slot.gameObject.SetActive(visible);
            }
            if (!visible)
            {
                continue;
            }

            ControlTowerWaypointData waypoint = orderedWaypointsCache[i];
            if (refreshGeometry)
            {
                bool projected = coordinateSource.TryConvertRosToMapLocalPosition(waypoint.x, waypoint.y, slot, out Vector2 mapLocalPosition);
                hasProjectedMapLocalPosition[i] = projected;
                if (!projected)
                {
                    if (slot.gameObject.activeSelf)
                    {
                        slot.gameObject.SetActive(false);
                    }
                    continue;
                }

                projectedMapLocalPositions[i] = mapLocalPosition;
                Vector2 targetPosition = ConvertMapLocalToGroupLocal(mapLocalPosition, waypointGroup.transform as RectTransform);
                if ((slot.anchoredPosition - targetPosition).sqrMagnitude > 0.000001f)
                {
                    slot.anchoredPosition = targetPosition;
                }
            }

            TMP_Text label = GetWaypointLabel(i);
            if (label != null)
            {
                string nextLabel = BuildWaypointLabel(
                    waypoint,
                    i == currentWaypointIndex,
                    i == currentWaypointIndex + 1 && currentWaypointIndex + 1 < visibleCount);
                if (label.text != nextLabel)
                {
                    label.text = nextLabel;
                }
            }
        }

        WarnIfRouteExceedsSlots(currentRoute, orderedWaypointsCache.Count, slotCount);
        if (refreshGeometry)
        {
            RefreshPathSegments(visibleCount);
        }

        RefreshProgressHighlight(currentWaypointIndex);
    }

    private void HideRouteVisuals()
    {
        EnsureParentGroupsActive();
        SetAllActive(waypointSlots, validWaypointSlots, false);
        SetAllActive(pathSegments, validPathSegments, false);
        RefreshProgressHighlight(-1);
    }

    private int FindCurrentWaypointIndex()
    {
        if (currentRoute == null || orderedWaypointsCache.Count == 0)
        {
            return -1;
        }

        for (int i = 0; i < orderedWaypointsCache.Count; i++)
        {
            ControlTowerWaypointData waypoint = orderedWaypointsCache[i];
            if (waypoint != null && IsCurrentWaypointStatus(waypoint.status))
            {
                return i;
            }
        }

        if (!currentRoute.has_current_wp_index || currentRoute.current_wp_index < 0)
        {
            return -1;
        }

        for (int i = 0; i < orderedWaypointsCache.Count; i++)
        {
            ControlTowerWaypointData waypoint = orderedWaypointsCache[i];
            if (waypoint != null && waypoint.sequence == currentRoute.current_wp_index)
            {
                return i;
            }
        }

        return currentRoute.current_wp_index < orderedWaypointsCache.Count
            ? currentRoute.current_wp_index
            : -1;
    }

    private void RefreshProgressHighlight(int currentWaypointIndex)
    {
        CacheVisualColorsOnce();

        int nextWaypointIndex = IsValidWaypointSlot(currentWaypointIndex + 1) &&
            currentWaypointIndex + 1 < orderedWaypointsCache.Count
            ? currentWaypointIndex + 1
            : -1;
        int progressVisualHash = ComputeProgressVisualHash(currentWaypointIndex, nextWaypointIndex);
        if (lastProgressVisualHash == progressVisualHash)
        {
            return;
        }

        for (int i = 0; i < waypointImages.Length; i++)
        {
            Color color = defaultWaypointColors[i];
            if (i == currentWaypointIndex)
            {
                color = currentWaypointColor;
            }
            else if (i == nextWaypointIndex)
            {
                color = nextWaypointColor;
            }
            else if (i < orderedWaypointsCache.Count && IsCompletedWaypoint(orderedWaypointsCache[i]))
            {
                color = completedWaypointColor;
            }

            SetImageColor(waypointImages[i], color);
        }

        for (int i = 0; i < pathSegmentImages.Length; i++)
        {
            Color color = defaultRouteColors[i];
            if (i == currentWaypointIndex && i + 1 < orderedWaypointsCache.Count)
            {
                color = currentRouteColor;
            }
            else if (i + 1 < orderedWaypointsCache.Count &&
                     IsCompletedWaypoint(orderedWaypointsCache[i]) &&
                     IsCompletedWaypoint(orderedWaypointsCache[i + 1]))
            {
                color = completedRouteColor;
            }

            SetImageColor(pathSegmentImages[i], color);
        }

        lastProgressVisualHash = progressVisualHash;
    }

    private int ComputeProgressVisualHash(int currentWaypointIndex, int nextWaypointIndex)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + currentWaypointIndex;
            hash = hash * 31 + nextWaypointIndex;
            hash = hash * 31 + orderedWaypointsCache.Count;
            foreach (ControlTowerWaypointData waypoint in orderedWaypointsCache)
            {
                hash = hash * 31 + (waypoint?.sequence ?? 0);
                hash = hash * 31 + (waypoint?.status?.GetHashCode() ?? 0);
            }

            return hash;
        }
    }

    private void CacheVisualColorsOnce()
    {
        if (visualColorsCached)
        {
            return;
        }

        int waypointCount = waypointSlots?.Length ?? 0;
        waypointImages = new Image[waypointCount];
        defaultWaypointColors = new Color[waypointCount];
        for (int i = 0; i < waypointCount; i++)
        {
            if (!IsValidWaypointSlot(i))
            {
                continue;
            }

            Image image = waypointSlots[i].GetComponent<Image>();
            waypointImages[i] = image;
            if (image != null)
            {
                defaultWaypointColors[i] = image.color;
            }
        }

        int segmentCount = pathSegments?.Length ?? 0;
        pathSegmentImages = new Image[segmentCount];
        defaultRouteColors = new Color[segmentCount];
        for (int i = 0; i < segmentCount; i++)
        {
            if (!IsValidPathSegment(i))
            {
                continue;
            }

            Image image = pathSegments[i].GetComponent<Image>();
            pathSegmentImages[i] = image;
            if (image != null)
            {
                defaultRouteColors[i] = image.color;
            }
        }

        visualColorsCached = true;
    }

    private static void SetImageColor(Image image, Color color)
    {
        if (image != null && image.color != color)
        {
            image.color = color;
        }
    }

    private void RefreshPathSegments(int waypointCount)
    {
        int segmentCount = pathSegments != null ? pathSegments.Length : 0;
        int neededSegments = Mathf.Clamp(waypointCount - 1, 0, segmentCount);
        RectTransform pathGroupRect = patrolPathGroup != null ? patrolPathGroup.transform as RectTransform : null;
        for (int i = 0; i < segmentCount; i++)
        {
            if (!IsValidPathSegment(i))
            {
                continue;
            }

            RectTransform segment = pathSegments[i];
            bool visible = i < neededSegments &&
                pathGroupRect != null &&
                i + 1 < hasProjectedMapLocalPosition.Length &&
                hasProjectedMapLocalPosition[i] &&
                hasProjectedMapLocalPosition[i + 1];
            if (segment.gameObject.activeSelf != visible)
            {
                segment.gameObject.SetActive(visible);
            }
            if (!visible)
            {
                continue;
            }

            Vector2 start = ConvertMapLocalToGroupLocal(projectedMapLocalPositions[i], pathGroupRect);
            Vector2 end = ConvertMapLocalToGroupLocal(projectedMapLocalPositions[i + 1], pathGroupRect);
            ApplySegmentBetweenPoints(segment, start, end);
        }
    }

    private void PopulateOrderedWaypoints(ControlTowerWaypointRouteData route)
    {
        orderedWaypointsCache.Clear();
        if (route?.waypoints == null)
        {
            return;
        }

        foreach (ControlTowerWaypointData waypoint in route.waypoints)
        {
            if (waypoint != null)
            {
                orderedWaypointsCache.Add(waypoint);
            }
        }

        orderedWaypointsCache.Sort(CompareWaypointSequence);
    }

    private static int ComputeRouteGeometryHash(ControlTowerWaypointRouteData route)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + (route?.route_id?.GetHashCode() ?? 0);
            ControlTowerWaypointData[] waypoints = route?.waypoints;
            hash = hash * 31 + (waypoints?.Length ?? 0);
            if (waypoints == null)
            {
                return hash;
            }

            foreach (ControlTowerWaypointData waypoint in waypoints)
            {
                if (waypoint == null)
                {
                    hash = hash * 31;
                    continue;
                }

                hash = hash * 31 + waypoint.sequence;
                hash = hash * 31 + waypoint.x.GetHashCode();
                hash = hash * 31 + waypoint.y.GetHashCode();
            }

            return hash;
        }
    }

    private static void ApplySegmentBetweenPoints(RectTransform segment, Vector2 start, Vector2 end)
    {
        Vector2 delta = end - start;
        Vector2 size = segment.sizeDelta;
        size.x = delta.magnitude;
        if ((segment.sizeDelta - size).sqrMagnitude > 0.000001f)
        {
            segment.sizeDelta = size;
        }

        Vector2 center = (start + end) * 0.5f;
        if ((segment.anchoredPosition - center).sqrMagnitude > 0.000001f)
        {
            segment.anchoredPosition = center;
        }

        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        Vector3 localEuler = segment.localEulerAngles;
        if (Mathf.Abs(Mathf.DeltaAngle(localEuler.z, angle)) > 0.001f)
        {
            localEuler.z = angle;
            segment.localEulerAngles = localEuler;
        }
    }

    private Vector2 ConvertMapLocalToGroupLocal(Vector2 mapLocalPosition, RectTransform targetGroup)
    {
        if (mapArea == null || targetGroup == null)
        {
            return mapLocalPosition;
        }

        Vector3 worldPosition = mapArea.TransformPoint(new Vector3(mapLocalPosition.x, mapLocalPosition.y, 0f));
        Vector3 groupLocal = targetGroup.InverseTransformPoint(worldPosition);
        return new Vector2(groupLocal.x, groupLocal.y);
    }

    private static int CompareWaypointSequence(ControlTowerWaypointData left, ControlTowerWaypointData right)
    {
        int leftSequence = left.sequence > 0 ? left.sequence : int.MaxValue;
        int rightSequence = right.sequence > 0 ? right.sequence : int.MaxValue;
        int sequenceCompare = leftSequence.CompareTo(rightSequence);
        if (sequenceCompare != 0)
        {
            return sequenceCompare;
        }

        return string.Compare(GetWaypointDisplayName(left), GetWaypointDisplayName(right), StringComparison.OrdinalIgnoreCase);
    }

    private void WarnIfRouteExceedsSlots(ControlTowerWaypointRouteData route, int waypointCount, int slotCount)
    {
        if (waypointCount <= slotCount)
        {
            return;
        }

        string routeKey = string.IsNullOrWhiteSpace(route.route_id) ? route.route_name : route.route_id;
        if (string.IsNullOrWhiteSpace(routeKey))
        {
            routeKey = $"robot-{route.robot_id}";
        }

        if (overflowWarnings.Add(routeKey))
        {
            Debug.LogWarning($"[MapStatusRoute] Route has {waypointCount} waypoints but only {slotCount} slots are available. Extra waypoints were skipped.");
        }
    }

    private TMP_Text GetWaypointLabel(int index)
    {
        if (waypointLabels != null && index >= 0 && index < waypointLabels.Length &&
            index < validWaypointLabels.Length && validWaypointLabels[index])
        {
            return waypointLabels[index];
        }

        return null;
    }

    private static string GetWaypointDisplayName(ControlTowerWaypointData waypoint)
    {
        if (waypoint == null)
        {
            return "--";
        }

        if (!string.IsNullOrWhiteSpace(waypoint.waypoint_id))
        {
            return waypoint.waypoint_id.Trim();
        }

        if (!string.IsNullOrWhiteSpace(waypoint.waypoint_name))
        {
            return waypoint.waypoint_name.Trim();
        }

        return waypoint.sequence > 0 ? $"WP{waypoint.sequence}" : "--";
    }

    private static string FormatWaypointStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "--";
        }

        return status.Trim().ToUpperInvariant() switch
        {
            "COMPLETED" or "COMPLETE" or "DONE" => "완료",
            "CURRENT" or "ACTIVE" or "MOVING" or "NAVIGATING" => "이동 중",
            "PENDING" or "WAITING" => "대기",
            "SKIPPED" => "건너뜀",
            "FAILED" or "FAIL" or "ERROR" => "실패",
            _ => status.Trim()
        };
    }

    private static bool IsCurrentWaypointStatus(string status)
    {
        string normalized = string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim().ToUpperInvariant();
        return normalized is "CURRENT" or "ACTIVE" or "MOVING" or "NAVIGATING";
    }

    private static bool IsCompletedWaypoint(ControlTowerWaypointData waypoint)
    {
        string normalized = string.IsNullOrWhiteSpace(waypoint?.status)
            ? string.Empty
            : waypoint.status.Trim().ToUpperInvariant();
        return normalized is "COMPLETED" or "COMPLETE" or "DONE";
    }

    private static string BuildWaypointLabel(ControlTowerWaypointData waypoint, bool isCurrent, bool isNext)
    {
        if (waypoint == null)
        {
            return "--";
        }

        string sequenceLabel = waypoint.sequence > 0
            ? waypoint.sequence.ToString()
            : GetWaypointDisplayName(waypoint);
        if (!isCurrent && !isNext)
        {
            return sequenceLabel;
        }

        return isCurrent
            ? $"{sequenceLabel}\n이동 중"
            : $"{sequenceLabel}\n대기";
    }

    private void ValidateInspectorReferencesOnce()
    {
        if (referencesValidated)
        {
            return;
        }

        referencesValidated = true;
        int waypointCount = waypointSlots?.Length ?? 0;
        int labelCount = waypointLabels?.Length ?? 0;
        int segmentCount = pathSegments?.Length ?? 0;
        validWaypointSlots = new bool[waypointCount];
        validWaypointLabels = new bool[labelCount];
        validPathSegments = new bool[segmentCount];

        ValidateRequiredReference(coordinateSource, nameof(coordinateSource));
        ValidateRequiredReference(mapArea, nameof(mapArea));
        ValidateRequiredReference(waypointGroup, nameof(waypointGroup));
        ValidateRequiredReference(patrolPathGroup, nameof(patrolPathGroup));
        ValidateArrayLength(nameof(waypointSlots), waypointCount, ExpectedWaypointSlotCount);
        ValidateArrayLength(nameof(waypointLabels), labelCount, ExpectedWaypointSlotCount);
        ValidateArrayLength(nameof(pathSegments), segmentCount, ExpectedPathSegmentCount);

        Transform waypointParent = waypointGroup != null ? waypointGroup.transform : null;
        for (int i = 0; i < waypointCount; i++)
        {
            RectTransform slot = waypointSlots[i];
            string expectedName = $"WP_{i + 1:00}";
            bool valid = slot != null && slot.name == expectedName && slot.parent == waypointParent;
            validWaypointSlots[i] = valid;
            if (!valid)
            {
                LogInvalidReference($"{nameof(waypointSlots)}[{i}]", expectedName, waypointParent, slot);
            }
        }

        for (int i = 0; i < labelCount; i++)
        {
            TMP_Text label = waypointLabels[i];
            RectTransform slot = i < waypointCount ? waypointSlots[i] : null;
            string expectedName = $"Text_WP_{i + 1:00}";
            bool valid = label != null && label.name == expectedName && slot != null && label.transform.IsChildOf(slot);
            validWaypointLabels[i] = valid;
            if (!valid)
            {
                LogInvalidReference($"{nameof(waypointLabels)}[{i}]", expectedName, slot, label);
            }
        }

        Transform pathParent = patrolPathGroup != null ? patrolPathGroup.transform : null;
        for (int i = 0; i < segmentCount; i++)
        {
            RectTransform segment = pathSegments[i];
            string expectedName = $"RouteSegment_{i + 1:00}";
            bool valid = segment != null && segment.name == expectedName && segment.parent == pathParent;
            validPathSegments[i] = valid;
            if (!valid)
            {
                LogInvalidReference($"{nameof(pathSegments)}[{i}]", expectedName, pathParent, segment);
            }
        }
    }

    private void EnsureParentGroupsActive()
    {
        if (waypointGroup != null && !waypointGroup.activeSelf)
        {
            waypointGroup.SetActive(true);
        }

        if (patrolPathGroup != null && !patrolPathGroup.activeSelf)
        {
            patrolPathGroup.SetActive(true);
        }
    }

    private void EnsureProjectionCacheCapacity(int count)
    {
        if (projectedMapLocalPositions.Length == count && hasProjectedMapLocalPosition.Length == count)
        {
            return;
        }

        projectedMapLocalPositions = new Vector2[count];
        hasProjectedMapLocalPosition = new bool[count];
    }

    private bool IsValidWaypointSlot(int index)
    {
        return index >= 0 && index < validWaypointSlots.Length && validWaypointSlots[index];
    }

    private bool IsValidPathSegment(int index)
    {
        return index >= 0 && index < validPathSegments.Length && validPathSegments[index];
    }

    private void ValidateRequiredReference(UnityEngine.Object reference, string fieldName)
    {
        if (reference == null)
        {
            Debug.LogWarning($"[MapStatusRoute] Inspector reference is missing: {fieldName}. No runtime fallback search will be used.", this);
        }
    }

    private void ValidateArrayLength(string fieldName, int actual, int expected)
    {
        if (actual != expected)
        {
            Debug.LogWarning($"[MapStatusRoute] Inspector reference count mismatch: {fieldName} expected={expected}, actual={actual}. Valid entries only will be used.", this);
        }
    }

    private void LogInvalidReference(string fieldName, string expectedName, Transform expectedParent, Component actual)
    {
        string expectedParentPath = expectedParent != null ? GetHierarchyPath(expectedParent) : "<missing parent>";
        string actualPath = actual != null ? GetHierarchyPath(actual.transform) : "<null>";
        Debug.LogWarning(
            $"[MapStatusRoute] Invalid Inspector reference: {fieldName}, expected={expectedParentPath}/{expectedName}, actual={actualPath}. Entry skipped.",
            this);
    }

    private static string GetHierarchyPath(Transform target)
    {
        if (target == null)
        {
            return "<null>";
        }

        string path = target.name;
        for (Transform parent = target.parent; parent != null; parent = parent.parent)
        {
            path = $"{parent.name}/{path}";
        }

        return path;
    }

    private static void SetAllActive(RectTransform[] items, bool[] validItems, bool active)
    {
        if (items == null)
        {
            return;
        }

        for (int i = 0; i < items.Length; i++)
        {
            if (validItems == null || i >= validItems.Length || !validItems[i])
            {
                continue;
            }

            RectTransform item = items[i];
            if (item != null && item.gameObject.activeSelf != active)
            {
                item.gameObject.SetActive(active);
            }
        }
    }
}

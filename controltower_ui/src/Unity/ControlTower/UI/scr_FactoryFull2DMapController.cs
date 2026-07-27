using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UI;

public class scr_FactoryFull2DMapController : MonoBehaviour
{
    private const float DefaultMapWidthPx = 52f;
    private const float DefaultMapHeightPx = 52f;
    private const float DefaultResolution = 0.05f;
    private const float DefaultOriginX = -0.506f;
    private const float DefaultOriginY = -0.607f;
    private const float LegacyMapWidthPx = 55f;
    private const float LegacyMapHeightPx = 55f;
    private const float LegacyOriginX = -0.682f;
    private const float LegacyOriginY = -0.732f;
    private const string RealMapLayoutRootName = "RealMapLayoutRoot";
    private const string Conveyor01ZoneObjectName = "B_ConveyorZone_01";
    private const string Conveyor02ZoneObjectName = "B_ConveyorZone_02";
    private const string PalletZoneObjectName = "C_PalletArea";
    private const string ChargingZoneObjectName = "A_ChargingZone";
    private const string EntryZoneObjectName = "D_EntryZone";
    private const string Conveyor01ZoneDisplayName = "컨베이어 1 구역";
    private const string Conveyor02ZoneDisplayName = "컨베이어 2 구역";
    private const string PalletZoneDisplayName = "팔레트 구역";
    private const string ChargingZoneDisplayName = "충전존 구역";
    private const string EntryZoneDisplayName = "직원 출입구 구역";

    [SerializeField] private scr_ControlTowerUIManager uiManager;
    [SerializeField] private RectTransform mapArea;
    [SerializeField] private RectTransform markerTb3_01;
    [SerializeField] private RectTransform markerTb3_02;
    [SerializeField] private RectTransform markerTb3_03;
    [SerializeField] private RectTransform headingArrowTb3_01;
    [SerializeField] private RectTransform headingArrowTb3_02;
    [SerializeField] private RectTransform headingArrowTb3_03;
    [SerializeField] private RectTransform markerNoHelmet;
    [SerializeField] private RectTransform markerFall;
    [SerializeField] private RectTransform markerFire;
    [SerializeField] private Image markerImageTb3_01;
    [SerializeField] private Image markerImageTb3_02;
    [SerializeField] private Image markerImageTb3_03;

    [Header("SLAM Map Conversion")]
    public float mapWidthPx = DefaultMapWidthPx;
    public float mapHeightPx = DefaultMapHeightPx;
    public float resolution = DefaultResolution;
    public float originX = DefaultOriginX;
    public float originY = DefaultOriginY;
    public bool hideWhenOutOfBounds;
    public bool logCoordinateDebug;

    [Header("Calibration")]
    [SerializeField] private bool useCalibration = true;
    [SerializeField] private bool swapXY;
    [SerializeField] private bool flipX;
    [SerializeField] private bool flipY;
    [SerializeField] private float scaleX = 1f;
    [SerializeField] private float scaleY = 1f;
    [SerializeField] private float offsetX;
    [SerializeField] private float offsetY;

    [Header("Display Orientation")]
    [SerializeField] private bool useGlobalCameraOrientation = true;
    [SerializeField] private bool mirrorXForGlobalCamera = true;

    [Header("Map Display Scale")]
    [SerializeField] private bool useMapDisplayScale = true;
    [SerializeField] private float fullMapDisplayScale = 10f;

    [Header("Marker Bounds")]
    [SerializeField] private bool clampMarkerInsideMap = true;
    [SerializeField] private float markerClampPadding = 2f;

    [Header("Heading Calibration")]
    [SerializeField] private bool useHeadingCalibration = true;
    [SerializeField] private bool invertYaw;
    [SerializeField] private float headingOffsetDeg;

    [Header("TB3-03 Visual Alignment")]
    [SerializeField] private bool useTb3_03VisualAlignment = true;
    [SerializeField] private float tb3_03MapOffsetX;
    [SerializeField] private float tb3_03MapOffsetY;
    [SerializeField] private float tb3_03LocalRightOffset;
    [SerializeField] private float tb3_03LocalForwardOffset;
    [SerializeField] private float tb3_03HeadingOffsetDeg;

    [Header("Heading Visuals")]
    [SerializeField] private bool showHeadingArrow = true;
    [SerializeField] private float headingArrowSize = 24f;
    [SerializeField] private float headingArrowOffset = 12f;
    [SerializeField] private float robotImageHeadingOffsetDeg = 0f;

    [Header("Robot Smooth Follow")]
    [SerializeField] private float robotPositionSmoothTime = 0.10f;
    [SerializeField] private float robotRotationFollowSpeed = 12f;
    [SerializeField] private float robotPoseStaleSeconds = 3f;

    private readonly Dictionary<string, RobotMapState> statesByRobot = new();
    private readonly Dictionary<string, RobotSmoothPose> smoothPosesByRobot = new();
    private readonly Dictionary<string, RobotMarkerVisualBaseline> visualBaselinesByRobot = new();
    private readonly Dictionary<string, RobotMarkerVisualState> visualStatesByRobot = new();
    private readonly Dictionary<string, float> lastCoordinateDebugLogTimeByRobot = new();
    private string selectedRobotId = "tb3-01";
    private bool pendingImmediateSync;
    private int lastImmediateSyncFrame = -1;
    private RectTransform alertZoneLayoutRoot;
    private RectTransform alertZoneConveyor01;
    private RectTransform alertZoneConveyor02;
    private RectTransform alertZonePallet;
    private RectTransform alertZoneCharging;
    private RectTransform alertZoneEntry;
    private bool alertZoneReferencesAttempted;
    private bool alertZoneReferenceWarningShown;

    private const float PositionSettleEpsilonSqr = 0.0001f;
    private const float PositionVelocitySettleEpsilonSqr = 0.0001f;
    private const float RotationSettleEpsilonDeg = 0.05f;
    private const float CoordinateDebugLogIntervalSeconds = 1f;
    private static readonly ProfilerMarker Full2DUpdateMarker = new("ControlTower.Map.Full2D.Update");

    public bool UseMapDisplayScale => useMapDisplayScale;
    public float FullMapDisplayScale => fullMapDisplayScale;
    public bool UseGlobalCameraOrientation => useGlobalCameraOrientation;
    public bool MirrorXForGlobalCamera => mirrorXForGlobalCamera;

    private struct RobotMapState
    {
        public float X;
        public float Y;
        public float Heading;
        public string Status;
        public float LastValidPoseReceiveTime;
        public bool HasValidPose;
    }

    private struct RobotSmoothPose
    {
        public Vector2 TargetPosition;
        public Vector2 CurrentPosition;
        public Vector2 PositionVelocity;
        public float TargetYawDeg;
        public float CurrentYawDeg;
        public bool HasInitialPose;
        public bool NeedsActivation;
    }

    private struct RobotMarkerVisualBaseline
    {
        public Vector3 MarkerRootScale;
        public Vector3 BodyScale;
        public Vector3 HeadingArrowScale;
        public Color BodyColor;
    }

    private enum RobotMarkerVisualState
    {
        NoData,
        Fresh,
        Stale,
        Disconnected
    }

    private void OnEnable()
    {
        UpgradeLegacyMapDefaults();
        ResolveReferences();
        CacheVisualBaselines();
        ConfigureHeadingArrowVisibility();
        SubscribeToUiManager();
        OnViewActivated();
    }

    private void OnDisable()
    {
        UnsubscribeFromUiManager();
        RestoreAllMarkerScales();
        visualStatesByRobot.Clear();
    }

    private void OnDestroy()
    {
        RestoreAllMarkerScales();
    }

    private void Update()
    {
        if (mapArea == null || !mapArea.gameObject.activeInHierarchy)
        {
            return;
        }

        using (Full2DUpdateMarker.Auto())
        {
            if (pendingImmediateSync)
            {
                pendingImmediateSync = !SyncAllRobotMarkersToLatestPoseImmediate();
                if (!pendingImmediateSync)
                {
                    lastImmediateSyncFrame = Time.frameCount;
                    return;
                }
            }

            UpdateRobotSmoothPoses();
            UpdateAllMarkerVisuals();
        }
    }

    public void OnViewActivated()
    {
        ResolveReferences();
        CacheVisualBaselines();
        ConfigureHeadingArrowVisibility();
        SubscribeToUiManager();

        if (lastImmediateSyncFrame == Time.frameCount)
        {
            return;
        }

        pendingImmediateSync = !SyncAllRobotMarkersToLatestPoseImmediate();
        if (!pendingImmediateSync)
        {
            lastImmediateSyncFrame = Time.frameCount;
        }
    }

    private void SubscribeToUiManager()
    {
        if (uiManager == null)
        {
            return;
        }

        uiManager.SelectedRobotChanged -= HandleSelectedRobotChanged;
        uiManager.SelectedRobotChanged += HandleSelectedRobotChanged;
        uiManager.RobotStateUpdated -= HandleRobotStateUpdated;
        uiManager.RobotStateUpdated += HandleRobotStateUpdated;
    }

    private void UnsubscribeFromUiManager()
    {
        if (uiManager == null)
        {
            return;
        }

        uiManager.SelectedRobotChanged -= HandleSelectedRobotChanged;
        uiManager.RobotStateUpdated -= HandleRobotStateUpdated;
    }

    private void HandleSelectedRobotChanged(string robotId)
    {
        selectedRobotId = robotId;
        UpdateAllMarkerVisuals();
    }

    private void HandleRobotStateUpdated(string robotId, float x, float y, float heading, string status)
    {
        RobotMapState state = statesByRobot.TryGetValue(robotId, out RobotMapState previousState)
            ? previousState
            : default;
        RectTransform marker = GetMarker(robotId);
        bool wasVisible = previousState.HasValidPose &&
                          ShouldShowRobotMarker(previousState) &&
                          marker != null && marker.gameObject.activeSelf;
        state.X = x;
        state.Y = y;
        state.Heading = heading;
        state.Status = status;
        state.LastValidPoseReceiveTime = Time.unscaledTime;
        state.HasValidPose = true;
        statesByRobot[robotId] = state;

        // A robot returning from an unavailable state must appear at its latest pose, not animate from an authored position.
        if (ShouldShowRobotMarker(state))
        {
            UpdateMarkerPosition(robotId, false, !wasVisible);
        }
        UpdateMarkerVisual(robotId, GetMarker(robotId), GetMarkerImage(robotId));
    }

    private bool SyncAllRobotMarkersToLatestPoseImmediate()
    {
        ResolveReferences();
        if (uiManager == null || mapArea == null)
        {
            return false;
        }

        selectedRobotId = uiManager.SelectedRobotId;
        for (int robotId = 1; robotId <= 3; robotId++)
        {
            string robotName = $"tb3-{robotId:00}";
            if (uiManager.TryGetRobotState(robotName, out float x, out float y, out float heading, out string status))
            {
                float receiveTime = uiManager.TryGetRobotPoseReceiveTime(robotName, out float cachedReceiveTime)
                    ? cachedReceiveTime
                    : Time.unscaledTime;
                statesByRobot[robotName] = new RobotMapState
                {
                    X = x,
                    Y = y,
                    Heading = heading,
                    Status = status,
                    LastValidPoseReceiveTime = receiveTime,
                    HasValidPose = true
                };
                if (ShouldShowRobotMarker(statesByRobot[robotName]))
                {
                    UpdateMarkerPosition(robotName, false, true);
                }
            }
        }

        CacheVisualBaselines();
        UpdateAllMarkerVisuals();
        return true;
    }

    private void UpdateMarkerPosition(string robotId, bool markPoseReceived, bool immediate = false)
    {
        if (!statesByRobot.TryGetValue(robotId, out RobotMapState state) || mapArea == null)
        {
            return;
        }

        if (!ShouldShowRobotMarker(state))
        {
            return;
        }

        RectTransform marker = GetMarker(robotId);
        if (marker == null)
        {
            return;
        }

        bool converted = TryConvertRosToMapPosition(state.X, state.Y, marker, out Vector2 rawUi, out Vector2 calibratedUi, out Vector2 clampedUi, out float pixelX, out float pixelY, out float screenTopX, out float screenTopY, out float displayNormX, out float displayNormY);
        if (!converted)
        {
            return;
        }

        bool outOfBounds = pixelX < 0f || pixelX > mapWidthPx || pixelY < 0f || pixelY > mapHeightPx;
        if (hideWhenOutOfBounds && outOfBounds)
        {
            return;
        }

        float rawYawDeg = state.Heading * Mathf.Rad2Deg;
        float displayYawDeg = ApplyDisplayOrientationToYaw(rawYawDeg);
        float calibratedYawDeg = ApplyHeadingCalibration(displayYawDeg);
        Vector2 commonMapped = clampedUi;
        Vector2 mapOffset = Vector2.zero;
        float localRightOffset = 0f;
        float localForwardOffset = 0f;
        Vector2 finalTarget = commonMapped;
        float finalYawDeg = calibratedYawDeg;

        if (useTb3_03VisualAlignment && string.Equals(robotId, "tb3-03", StringComparison.Ordinal))
        {
            mapOffset = new Vector2(tb3_03MapOffsetX, tb3_03MapOffsetY);
            localRightOffset = tb3_03LocalRightOffset;
            localForwardOffset = tb3_03LocalForwardOffset;
            finalTarget += mapOffset + RotateRobotLocalOffset2D(
                calibratedYawDeg,
                localRightOffset,
                localForwardOffset);
            finalYawDeg += tb3_03HeadingOffsetDeg;
        }

        SetRobotTargetPose(robotId, marker, finalTarget, finalYawDeg, immediate);
        if (markPoseReceived || state.LastValidPoseReceiveTime <= 0f)
        {
            state.LastValidPoseReceiveTime = Time.unscaledTime;
        }

        state.HasValidPose = true;
        statesByRobot[robotId] = state;

        if (string.Equals(robotId, "tb3-03", StringComparison.Ordinal) &&
            ShouldLogCoordinateDebug(robotId))
        {
            Debug.Log(
                $"[TB3-03 Alignment Full2D] rawPose=({state.X:F3},{state.Y:F3},{state.Heading:F3}) " +
                $"commonMapped=({commonMapped.x:F3},{commonMapped.y:F3}) " +
                $"mapOffset=({mapOffset.x:F3},{mapOffset.y:F3}) " +
                $"localOffset=({localRightOffset:F3},{localForwardOffset:F3}) " +
                $"finalTarget=({finalTarget.x:F3},{finalTarget.y:F3}) finalYaw={finalYawDeg:F2}");
        }
    }

    private static Vector2 RotateRobotLocalOffset2D(
        float yawDeg,
        float rightOffset,
        float forwardOffset)
    {
        float yawRad = yawDeg * Mathf.Deg2Rad;
        float sinYaw = Mathf.Sin(yawRad);
        float cosYaw = Mathf.Cos(yawRad);
        Vector2 right = new Vector2(cosYaw, sinYaw);
        Vector2 forward = new Vector2(-sinYaw, cosYaw);
        return right * rightOffset + forward * forwardOffset;
    }

    private bool ShouldLogCoordinateDebug(string robotId)
    {
        if (!logCoordinateDebug || mapArea == null || !mapArea.gameObject.activeInHierarchy)
        {
            return false;
        }

        float now = Time.unscaledTime;
        if (lastCoordinateDebugLogTimeByRobot.TryGetValue(robotId, out float previousTime) &&
            now - previousTime < CoordinateDebugLogIntervalSeconds)
        {
            return false;
        }

        lastCoordinateDebugLogTimeByRobot[robotId] = now;
        return true;
    }

    private void SetRobotTargetPose(
        string robotId,
        RectTransform marker,
        Vector2 targetPosition,
        float targetYawDeg,
        bool immediate)
    {
        RobotSmoothPose pose = smoothPosesByRobot.TryGetValue(robotId, out RobotSmoothPose cachedPose) ? cachedPose : default;
        pose.TargetPosition = targetPosition;
        pose.TargetYawDeg = targetYawDeg;

        if (immediate)
        {
            pose.CurrentPosition = targetPosition;
            pose.CurrentYawDeg = targetYawDeg;
            pose.PositionVelocity = Vector2.zero;
            pose.HasInitialPose = true;
            pose.NeedsActivation = false;

            if ((marker.anchoredPosition - targetPosition).sqrMagnitude > PositionSettleEpsilonSqr)
            {
                marker.anchoredPosition = targetPosition;
            }

            UpdateRobotHeadingVisuals(robotId, targetYawDeg);
        }
        else if (!pose.HasInitialPose)
        {
            pose.CurrentPosition = marker.anchoredPosition;
            pose.CurrentYawDeg = targetYawDeg;
            pose.PositionVelocity = Vector2.zero;
            pose.HasInitialPose = true;
            pose.NeedsActivation = false;
        }

        smoothPosesByRobot[robotId] = pose;
    }

    private void UpdateRobotSmoothPoses()
    {
        UpdateRobotSmoothPose("tb3-01", markerTb3_01);
        UpdateRobotSmoothPose("tb3-02", markerTb3_02);
        UpdateRobotSmoothPose("tb3-03", markerTb3_03);
    }

    private void UpdateRobotSmoothPose(string robotId, RectTransform marker)
    {
        if (marker == null || !smoothPosesByRobot.TryGetValue(robotId, out RobotSmoothPose pose) || !pose.HasInitialPose)
        {
            return;
        }

        if (!ShouldShowRobotMarker(robotId))
        {
            pose.NeedsActivation = false;
            smoothPosesByRobot[robotId] = pose;
            return;
        }

        bool positionSettled = (pose.CurrentPosition - pose.TargetPosition).sqrMagnitude <= PositionSettleEpsilonSqr &&
                               pose.PositionVelocity.sqrMagnitude <= PositionVelocitySettleEpsilonSqr;
        bool rotationSettled = Mathf.Abs(Mathf.DeltaAngle(pose.CurrentYawDeg, pose.TargetYawDeg)) <= RotationSettleEpsilonDeg;
        if (positionSettled && rotationSettled)
        {
            pose.CurrentPosition = pose.TargetPosition;
            pose.CurrentYawDeg = pose.TargetYawDeg;
            pose.PositionVelocity = Vector2.zero;
            if ((marker.anchoredPosition - pose.TargetPosition).sqrMagnitude > PositionSettleEpsilonSqr)
            {
                marker.anchoredPosition = pose.TargetPosition;
            }

            smoothPosesByRobot[robotId] = pose;
            return;
        }

        float deltaTime = Time.unscaledDeltaTime;
        float smoothTime = Mathf.Max(0.0001f, robotPositionSmoothTime);
        pose.CurrentPosition = Vector2.SmoothDamp(
            pose.CurrentPosition,
            pose.TargetPosition,
            ref pose.PositionVelocity,
            smoothTime,
            Mathf.Infinity,
            deltaTime);

        float rotationStep = Mathf.Clamp01(Mathf.Max(0f, robotRotationFollowSpeed) * deltaTime);
        pose.CurrentYawDeg = Mathf.LerpAngle(pose.CurrentYawDeg, pose.TargetYawDeg, rotationStep);

        marker.anchoredPosition = pose.CurrentPosition;
        UpdateRobotHeadingVisuals(robotId, pose.CurrentYawDeg);
        smoothPosesByRobot[robotId] = pose;
    }

    public void SetIncidentMarker(string incidentType, float rosX, float rosY, bool visible)
    {
        ResolveReferences();
        RectTransform marker = GetIncidentMarker(incidentType);
        if (marker == null)
        {
            return;
        }

        if (!visible)
        {
            marker.gameObject.SetActive(false);
            return;
        }

        if (TryConvertRosToMapPosition(rosX, rosY, marker, out Vector2 rawUi, out Vector2 calibratedUi, out Vector2 clampedUi, out float pixelX, out float pixelY, out float screenTopX, out float screenTopY, out float displayNormX, out float displayNormY))
        {
            Vector2 parentLocalUi = ConvertMapLocalToMarkerParentLocal(clampedUi, marker);
            marker.anchoredPosition = parentLocalUi;
            marker.gameObject.SetActive(true);

            if (logCoordinateDebug)
            {
                string parentName = marker.parent != null ? marker.parent.name : "<none>";
                Debug.Log($"[Factory2DMap Incident] {NormalizeIncidentType(incidentType)} ros=({rosX:F2},{rosY:F2}) pixel=({pixelX:F1},{pixelY:F1}) screenTop=({screenTopX:F1},{screenTopY:F1}) displayNorm=({displayNormX:F3},{displayNormY:F3}) rawUi=({rawUi.x:F1},{rawUi.y:F1}) calUi=({calibratedUi.x:F1},{calibratedUi.y:F1}) mapLocal=({clampedUi.x:F1},{clampedUi.y:F1}) parent={parentName} parentLocal=({parentLocalUi.x:F1},{parentLocalUi.y:F1})");
            }
        }
    }

    private Vector2 ConvertMapLocalToMarkerParentLocal(Vector2 mapLocalPosition, RectTransform marker)
    {
        RectTransform markerParent = marker != null ? marker.parent as RectTransform : null;
        if (mapArea == null || markerParent == null || markerParent == mapArea)
        {
            return mapLocalPosition;
        }

        Vector3 worldPosition = mapArea.TransformPoint(new Vector3(mapLocalPosition.x, mapLocalPosition.y, 0f));
        Vector3 parentLocal = markerParent.InverseTransformPoint(worldPosition);
        return new Vector2(parentLocal.x, parentLocal.y);
    }

    private bool TryConvertRosToMapPosition(float rosX, float rosY, RectTransform marker, out Vector2 rawUi, out Vector2 calibratedUi, out Vector2 clampedUi, out float pixelX, out float pixelY, out float screenTopX, out float screenTopY, out float displayNormX, out float displayNormY)
    {
        rawUi = Vector2.zero;
        calibratedUi = Vector2.zero;
        clampedUi = Vector2.zero;
        pixelX = 0f;
        pixelY = 0f;
        screenTopX = 0f;
        screenTopY = 0f;
        displayNormX = 0f;
        displayNormY = 0f;
        if (marker == null ||
            !TryConvertRosToCalibratedMapLocalPosition(
                rosX,
                rosY,
                out rawUi,
                out calibratedUi,
                out pixelX,
                out pixelY,
                out screenTopX,
                out screenTopY,
                out displayNormX,
                out displayNormY))
        {
            return false;
        }

        clampedUi = ClampMarkerPositionInsideMap(calibratedUi, marker);
        return true;
    }

    private bool TryConvertRosToCalibratedMapLocalPosition(
        float rosX,
        float rosY,
        out Vector2 rawUi,
        out Vector2 calibratedUi,
        out float pixelX,
        out float pixelY,
        out float screenTopX,
        out float screenTopY,
        out float displayNormX,
        out float displayNormY)
    {
        rawUi = Vector2.zero;
        calibratedUi = Vector2.zero;
        pixelX = 0f;
        pixelY = 0f;
        screenTopX = 0f;
        screenTopY = 0f;
        displayNormX = 0f;
        displayNormY = 0f;

        if (mapArea == null)
        {
            return false;
        }

        Rect rect = mapArea.rect;
        pixelX = (rosX - originX) / resolution;
        pixelY = (rosY - originY) / resolution;
        screenTopX = pixelX;
        screenTopY = mapHeightPx - pixelY;
        float slamNormX = mapWidthPx > 0f ? pixelX / mapWidthPx : 0f;
        float slamNormY = mapHeightPx > 0f ? pixelY / mapHeightPx : 0f;
        displayNormX = slamNormX;
        displayNormY = slamNormY;
        if (useGlobalCameraOrientation && mirrorXForGlobalCamera)
        {
            displayNormX = 1f - displayNormX;
        }

        displayNormX = Mathf.Clamp01(displayNormX);
        displayNormY = Mathf.Clamp01(displayNormY);
        rawUi = new Vector2((displayNormX - 0.5f) * rect.width, (displayNormY - 0.5f) * rect.height);
        calibratedUi = ApplyCalibration(rawUi);
        return true;
    }

    public bool TryProjectNormalizedMapPosition(Vector2 normalizedPosition, RectTransform marker, out Vector2 clampedUi)
    {
        clampedUi = Vector2.zero;
        if (mapArea == null || marker == null)
        {
            return false;
        }

        Rect rect = mapArea.rect;
        float displayNormX = Mathf.Clamp01(normalizedPosition.x);
        float displayNormY = Mathf.Clamp01(normalizedPosition.y);
        if (useGlobalCameraOrientation && mirrorXForGlobalCamera)
        {
            displayNormX = 1f - displayNormX;
        }

        Vector2 rawUi = new Vector2((displayNormX - 0.5f) * rect.width, (displayNormY - 0.5f) * rect.height);
        Vector2 calibratedUi = ApplyCalibration(rawUi);
        clampedUi = ClampMarkerPositionInsideMap(calibratedUi, marker);
        return true;
    }

    public bool TryConvertRosToMapLocalPosition(float rosX, float rosY, RectTransform marker, out Vector2 clampedUi)
    {
        clampedUi = Vector2.zero;
        return TryConvertRosToMapPosition(
            rosX,
            rosY,
            marker,
            out _,
            out _,
            out clampedUi,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _);
    }

    public bool TryConvertRosToMapLocalPosition(float rosX, float rosY, out Vector2 mapLocalPosition)
    {
        mapLocalPosition = Vector2.zero;
        return TryConvertRosToCalibratedMapLocalPosition(
            rosX,
            rosY,
            out _,
            out mapLocalPosition,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _);
    }

    public bool TryResolveNearestZoneFromRos(float rosX, float rosY, out string zoneDisplayName)
    {
        zoneDisplayName = string.Empty;
        if (!IsFiniteCoordinate(rosX) ||
            !IsFiniteCoordinate(rosY) ||
            !TryConvertRosToMapLocalPosition(rosX, rosY, out Vector2 mapLocalPoint) ||
            !TryCacheAlertZoneReferences())
        {
            return false;
        }

        float bestDistanceSqr = float.PositiveInfinity;
        TryUpdateNearestZone(mapLocalPoint, alertZoneConveyor01, Conveyor01ZoneDisplayName, ref bestDistanceSqr, ref zoneDisplayName);
        TryUpdateNearestZone(mapLocalPoint, alertZoneConveyor02, Conveyor02ZoneDisplayName, ref bestDistanceSqr, ref zoneDisplayName);
        TryUpdateNearestZone(mapLocalPoint, alertZonePallet, PalletZoneDisplayName, ref bestDistanceSqr, ref zoneDisplayName);
        TryUpdateNearestZone(mapLocalPoint, alertZoneCharging, ChargingZoneDisplayName, ref bestDistanceSqr, ref zoneDisplayName);
        TryUpdateNearestZone(mapLocalPoint, alertZoneEntry, EntryZoneDisplayName, ref bestDistanceSqr, ref zoneDisplayName);
        return !string.IsNullOrEmpty(zoneDisplayName);
    }

    private bool TryCacheAlertZoneReferences()
    {
        if (!alertZoneReferencesAttempted)
        {
            alertZoneReferencesAttempted = true;
            ResolveReferences();
            alertZoneLayoutRoot = mapArea != null
                ? mapArea.Find(RealMapLayoutRootName) as RectTransform
                : null;
            if (alertZoneLayoutRoot != null)
            {
                alertZoneConveyor01 = alertZoneLayoutRoot.Find(Conveyor01ZoneObjectName) as RectTransform;
                alertZoneConveyor02 = alertZoneLayoutRoot.Find(Conveyor02ZoneObjectName) as RectTransform;
                alertZonePallet = alertZoneLayoutRoot.Find(PalletZoneObjectName) as RectTransform;
                alertZoneCharging = alertZoneLayoutRoot.Find(ChargingZoneObjectName) as RectTransform;
                alertZoneEntry = alertZoneLayoutRoot.Find(EntryZoneObjectName) as RectTransform;
            }
        }

        bool complete = alertZoneLayoutRoot != null &&
                        alertZoneConveyor01 != null &&
                        alertZoneConveyor02 != null &&
                        alertZonePallet != null &&
                        alertZoneCharging != null &&
                        alertZoneEntry != null;
        if (!complete && !alertZoneReferenceWarningShown)
        {
            alertZoneReferenceWarningShown = true;
            Debug.LogWarning(
                "[Factory2DMap] Alert zone references are incomplete. Expected exact paths below Image_FactoryFloor/" +
                "RealMapLayoutRoot: B_ConveyorZone_01, B_ConveyorZone_02, C_PalletArea, A_ChargingZone, D_EntryZone.");
        }

        return complete;
    }

    private void TryUpdateNearestZone(
        Vector2 mapLocalPoint,
        RectTransform zone,
        string displayName,
        ref float bestDistanceSqr,
        ref string bestDisplayName)
    {
        float distanceSqr = GetSquaredDistanceFromMapPointToZone(mapLocalPoint, zone);
        if (distanceSqr < bestDistanceSqr)
        {
            bestDistanceSqr = distanceSqr;
            bestDisplayName = displayName;
        }
    }

    private float GetSquaredDistanceFromMapPointToZone(Vector2 mapLocalPoint, RectTransform zone)
    {
        Vector3 mapWorldPoint = mapArea.TransformPoint(new Vector3(mapLocalPoint.x, mapLocalPoint.y, 0f));
        Vector3 zoneLocalPoint = zone.InverseTransformPoint(mapWorldPoint);
        Rect zoneRect = zone.rect;
        Vector3 nearestZoneLocalPoint = new(
            Mathf.Clamp(zoneLocalPoint.x, zoneRect.xMin, zoneRect.xMax),
            Mathf.Clamp(zoneLocalPoint.y, zoneRect.yMin, zoneRect.yMax),
            0f);
        Vector3 nearestWorldPoint = zone.TransformPoint(nearestZoneLocalPoint);
        Vector3 nearestMapLocalPoint = mapArea.InverseTransformPoint(nearestWorldPoint);
        Vector2 difference = mapLocalPoint - new Vector2(nearestMapLocalPoint.x, nearestMapLocalPoint.y);
        return difference.sqrMagnitude;
    }

    private static bool IsFiniteCoordinate(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private Vector2 ClampMarkerPositionInsideMap(Vector2 position, RectTransform marker)
    {
        if (!clampMarkerInsideMap || mapArea == null || marker == null)
        {
            return position;
        }

        Rect mapRect = mapArea.rect;
        Rect markerRect = marker.rect;
        float halfMarkerWidth = markerRect.width * 0.5f + markerClampPadding;
        float halfMarkerHeight = markerRect.height * 0.5f + markerClampPadding;
        float minX = -mapRect.width * 0.5f + halfMarkerWidth;
        float maxX = mapRect.width * 0.5f - halfMarkerWidth;
        float minY = -mapRect.height * 0.5f + halfMarkerHeight;
        float maxY = mapRect.height * 0.5f - halfMarkerHeight;

        if (minX > maxX || minY > maxY)
        {
            return Vector2.zero;
        }

        return new Vector2(
            Mathf.Clamp(position.x, minX, maxX),
            Mathf.Clamp(position.y, minY, maxY));
    }

    private void UpgradeLegacyMapDefaults()
    {
        if (Mathf.Approximately(mapWidthPx, LegacyMapWidthPx))
        {
            mapWidthPx = DefaultMapWidthPx;
        }

        if (Mathf.Approximately(mapHeightPx, LegacyMapHeightPx))
        {
            mapHeightPx = DefaultMapHeightPx;
        }

        if (Mathf.Approximately(resolution, 0f))
        {
            resolution = DefaultResolution;
        }

        if (Mathf.Approximately(originX, LegacyOriginX))
        {
            originX = DefaultOriginX;
        }

        if (Mathf.Approximately(originY, LegacyOriginY))
        {
            originY = DefaultOriginY;
        }
    }

    private Vector2 ApplyCalibration(Vector2 rawUi)
    {
        if (!useCalibration)
        {
            return rawUi;
        }

        float x = rawUi.x;
        float y = rawUi.y;
        if (swapXY)
        {
            (x, y) = (y, x);
        }

        if (flipX)
        {
            x = -x;
        }

        if (flipY)
        {
            y = -y;
        }

        x = x * scaleX + offsetX;
        y = y * scaleY + offsetY;
        return new Vector2(x, y);
    }

    private float ApplyDisplayOrientationToYaw(float rawYawDeg)
    {
        float displayYawDeg = -rawYawDeg;
        if (useGlobalCameraOrientation && mirrorXForGlobalCamera)
        {
            displayYawDeg = 180f - displayYawDeg;
        }

        return displayYawDeg;
    }

    private float ApplyHeadingCalibration(float displayYawDeg)
    {
        float calibratedYawDeg = displayYawDeg;
        if (useHeadingCalibration && invertYaw)
        {
            calibratedYawDeg = -calibratedYawDeg;
        }

        calibratedYawDeg += headingOffsetDeg;
        return calibratedYawDeg;
    }

    private void UpdateRobotHeadingVisuals(string robotId, float calibratedYawDeg)
    {
        Image markerImage = GetMarkerImage(robotId);
        RectTransform markerImageRect = markerImage != null ? markerImage.rectTransform : null;
        if (markerImageRect != null)
        {
            markerImageRect.localRotation = Quaternion.Euler(
                0f,
                0f,
                calibratedYawDeg + robotImageHeadingOffsetDeg);
        }

        UpdateHeadingArrow(robotId, calibratedYawDeg, markerImageRect);
    }

    private void UpdateHeadingArrow(
        string robotId,
        float calibratedYawDeg,
        RectTransform rotatedMarkerImage)
    {
        RectTransform arrow = GetHeadingArrow(robotId);
        if (arrow == null)
        {
            return;
        }

        if (!showHeadingArrow)
        {
            return;
        }

        if (rotatedMarkerImage != null && arrow.IsChildOf(rotatedMarkerImage) &&
            arrow.parent != null)
        {
            RectTransform marker = GetMarker(robotId);
            Transform rotationReference = marker != null ? marker.parent : rotatedMarkerImage.parent;
            Quaternion referenceRotation = rotationReference != null
                ? rotationReference.rotation
                : Quaternion.identity;
            Quaternion desiredWorldRotation =
                referenceRotation * Quaternion.Euler(0f, 0f, calibratedYawDeg);
            arrow.localRotation =
                Quaternion.Inverse(arrow.parent.rotation) * desiredWorldRotation;
        }
        else
        {
            arrow.localRotation = Quaternion.Euler(0f, 0f, calibratedYawDeg);
        }
    }

    private void UpdateAllMarkerVisuals()
    {
        CacheVisualBaselines();
        UpdateMarkerVisual("tb3-01", markerTb3_01, markerImageTb3_01);
        UpdateMarkerVisual("tb3-02", markerTb3_02, markerImageTb3_02);
        UpdateMarkerVisual("tb3-03", markerTb3_03, markerImageTb3_03);
    }

    private void UpdateMarkerVisual(string robotId, RectTransform marker, Image markerImage)
    {
        if (marker == null)
        {
            return;
        }

        RobotMarkerVisualState nextState = GetRobotMarkerVisualState(robotId);
        bool stateChanged = !visualStatesByRobot.TryGetValue(robotId, out RobotMarkerVisualState previousState) ||
                            previousState != nextState;
        if (stateChanged)
        {
            visualStatesByRobot[robotId] = nextState;
            ApplyMarkerStateColor(robotId, markerImage, nextState);

            if (logCoordinateDebug)
            {
                Vector3 currentScale = markerImage != null ? markerImage.rectTransform.localScale : marker.localScale;
                Vector3 baseScale = visualBaselinesByRobot.TryGetValue(robotId, out RobotMarkerVisualBaseline baseline)
                    ? baseline.BodyScale
                    : currentScale;
                Debug.Log($"[Factory2DMap] {robotId} visual={nextState} scale={currentScale} baseScale={baseScale}");
            }
        }

        if (stateChanged || nextState == RobotMarkerVisualState.Disconnected)
        {
            ApplyMarkerScale(robotId, marker, markerImage);
        }

        ApplyRobotMarkerVisibility(marker, ShouldShowRobotMarker(nextState));
    }

    private bool ShouldShowRobotMarker(string robotId)
    {
        return ShouldShowRobotMarker(GetRobotMarkerVisualState(robotId));
    }

    private bool ShouldShowRobotMarker(RobotMapState state)
    {
        return state.HasValidPose &&
               (uiManager == null || uiManager.IsWebSocketConnected) &&
               !IsDisconnectedState(state.Status);
    }

    private static bool ShouldShowRobotMarker(RobotMarkerVisualState state)
    {
        return state is RobotMarkerVisualState.Fresh or RobotMarkerVisualState.Stale;
    }

    private static void ApplyRobotMarkerVisibility(RectTransform marker, bool visible)
    {
        if (marker != null && marker.gameObject.activeSelf != visible)
        {
            marker.gameObject.SetActive(visible);
        }
    }

    private RobotMarkerVisualState GetRobotMarkerVisualState(string robotId)
    {
        if (!statesByRobot.TryGetValue(robotId, out RobotMapState state) || !state.HasValidPose)
        {
            return RobotMarkerVisualState.NoData;
        }

        if ((uiManager != null && !uiManager.IsWebSocketConnected) || IsDisconnectedState(state.Status))
        {
            return RobotMarkerVisualState.Disconnected;
        }

        float staleAfter = Mathf.Max(0.1f, robotPoseStaleSeconds);
        return Time.unscaledTime - state.LastValidPoseReceiveTime > staleAfter
            ? RobotMarkerVisualState.Stale
            : RobotMarkerVisualState.Fresh;
    }

    private void ApplyMarkerStateColor(string robotId, Image markerImage, RobotMarkerVisualState state)
    {
        if (markerImage == null || !visualBaselinesByRobot.TryGetValue(robotId, out RobotMarkerVisualBaseline baseline))
        {
            return;
        }

        Color color = state switch
        {
            RobotMarkerVisualState.NoData => new Color(0.50f, 0.50f, 0.50f, baseline.BodyColor.a),
            RobotMarkerVisualState.Stale => new Color(0.96f, 0.62f, 0.04f, baseline.BodyColor.a),
            RobotMarkerVisualState.Disconnected => new Color(0.94f, 0.27f, 0.27f, baseline.BodyColor.a),
            _ => baseline.BodyColor
        };
        markerImage.color = color;
    }

    private void ApplyMarkerScale(string robotId, RectTransform marker, Image markerImage)
    {
        if (!visualBaselinesByRobot.TryGetValue(robotId, out RobotMarkerVisualBaseline baseline))
        {
            return;
        }

        if ((marker.localScale - baseline.MarkerRootScale).sqrMagnitude > 0.000001f)
        {
            marker.localScale = baseline.MarkerRootScale;
        }
        RectTransform body = markerImage != null ? markerImage.rectTransform : null;
        if (body != null)
        {
            if ((body.localScale - baseline.BodyScale).sqrMagnitude > 0.000001f)
            {
                body.localScale = baseline.BodyScale;
            }
        }

        RectTransform arrow = GetHeadingArrow(robotId);
        if (arrow != null)
        {
            if ((arrow.localScale - baseline.HeadingArrowScale).sqrMagnitude > 0.000001f)
            {
                arrow.localScale = baseline.HeadingArrowScale;
            }
        }
    }

    private void CacheVisualBaselines()
    {
        CacheVisualBaseline("tb3-01", markerTb3_01, markerImageTb3_01, headingArrowTb3_01);
        CacheVisualBaseline("tb3-02", markerTb3_02, markerImageTb3_02, headingArrowTb3_02);
        CacheVisualBaseline("tb3-03", markerTb3_03, markerImageTb3_03, headingArrowTb3_03);
    }

    private void CacheVisualBaseline(string robotId, RectTransform marker, Image markerImage, RectTransform arrow)
    {
        if (marker == null || markerImage == null || visualBaselinesByRobot.ContainsKey(robotId))
        {
            return;
        }

        visualBaselinesByRobot[robotId] = new RobotMarkerVisualBaseline
        {
            MarkerRootScale = marker.localScale,
            BodyScale = markerImage.rectTransform.localScale,
            HeadingArrowScale = arrow != null ? arrow.localScale : Vector3.one,
            BodyColor = markerImage.color
        };
    }

    private void RestoreAllMarkerScales()
    {
        RestoreMarkerScale("tb3-01", markerTb3_01, markerImageTb3_01, headingArrowTb3_01);
        RestoreMarkerScale("tb3-02", markerTb3_02, markerImageTb3_02, headingArrowTb3_02);
        RestoreMarkerScale("tb3-03", markerTb3_03, markerImageTb3_03, headingArrowTb3_03);
    }

    private void RestoreMarkerScale(string robotId, RectTransform marker, Image markerImage, RectTransform arrow)
    {
        if (!visualBaselinesByRobot.TryGetValue(robotId, out RobotMarkerVisualBaseline baseline))
        {
            return;
        }

        if (marker != null)
        {
            marker.localScale = baseline.MarkerRootScale;
        }

        if (markerImage != null)
        {
            markerImage.rectTransform.localScale = baseline.BodyScale;
        }

        if (arrow != null)
        {
            arrow.localScale = baseline.HeadingArrowScale;
        }
    }

    private void ConfigureHeadingArrowVisibility()
    {
        // Keep legacy layout values serialized without rewriting the authored RectTransform.
        _ = headingArrowSize;
        _ = headingArrowOffset;
        SetHeadingArrowVisibility(headingArrowTb3_01);
        SetHeadingArrowVisibility(headingArrowTb3_02);
        SetHeadingArrowVisibility(headingArrowTb3_03);
    }

    private void SetHeadingArrowVisibility(RectTransform arrow)
    {
        if (arrow != null && arrow.gameObject.activeSelf != showHeadingArrow)
        {
            arrow.gameObject.SetActive(showHeadingArrow);
        }
    }

    private static bool IsDisconnectedState(string status)
    {
        string normalized = string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim().ToUpperInvariant();
        return normalized is "OFFLINE" or "DISCONNECTED" or "CONNECTION_LOST" or "COMM_LOST";
    }

    private RectTransform GetMarker(string robotId)
    {
        return robotId switch
        {
            "tb3-01" => markerTb3_01,
            "tb3-02" => markerTb3_02,
            "tb3-03" => markerTb3_03,
            _ => null
        };
    }

    private RectTransform GetHeadingArrow(string robotId)
    {
        return robotId switch
        {
            "tb3-01" => headingArrowTb3_01,
            "tb3-02" => headingArrowTb3_02,
            "tb3-03" => headingArrowTb3_03,
            _ => null
        };
    }

    private Image GetMarkerImage(string robotId)
    {
        return robotId switch
        {
            "tb3-01" => markerImageTb3_01,
            "tb3-02" => markerImageTb3_02,
            "tb3-03" => markerImageTb3_03,
            _ => null
        };
    }

    private RectTransform GetIncidentMarker(string incidentType)
    {
        string normalized = NormalizeIncidentType(incidentType);
        return normalized switch
        {
            "NO_HELMET" => markerNoHelmet,
            "FALL" => markerFall,
            "FIRE" => markerFire,
            _ => null
        };
    }

    private static string NormalizeIncidentType(string incidentType)
    {
        string normalized = string.IsNullOrWhiteSpace(incidentType) ? string.Empty : incidentType.Trim().ToUpperInvariant();
        return normalized switch
        {
            "EVENT_HELMET" => "NO_HELMET",
            "EVENT_FALL" => "FALL",
            "EVENT_FIRE" => "FIRE",
            _ => normalized
        };
    }

    private void ResolveReferences()
    {
        if (uiManager == null)
        {
#if UNITY_2023_1_OR_NEWER
            uiManager = FindFirstObjectByType<scr_ControlTowerUIManager>();
#else
            uiManager = FindObjectOfType<scr_ControlTowerUIManager>();
#endif
        }

        mapArea ??= FindRectTransform("Image_FactoryFloor");
        mapArea ??= FindRectTransform("Image_MapArea_Background");
        markerTb3_01 ??= FindRectTransform("Marker_TB3_01");
        markerTb3_02 ??= FindRectTransform("Marker_TB3_02");
        markerTb3_03 ??= FindRectTransform("Marker_TB3_03");
        headingArrowTb3_01 ??= FindChildRectTransform(markerTb3_01, "HeadingArrow_TB3_01");
        headingArrowTb3_02 ??= FindChildRectTransform(markerTb3_02, "HeadingArrow_TB3_02");
        headingArrowTb3_03 ??= FindChildRectTransform(markerTb3_03, "HeadingArrow_TB3_03");
        markerNoHelmet ??= FindRectTransform("Marker_NoHelmet");
        markerFall ??= FindRectTransform("Marker_Fall");
        markerFire ??= FindRectTransform("Marker_Fire");
        markerImageTb3_01 ??= markerTb3_01 != null ? markerTb3_01.GetComponentInChildren<Image>(true) : null;
        markerImageTb3_02 ??= markerTb3_02 != null ? markerTb3_02.GetComponentInChildren<Image>(true) : null;
        markerImageTb3_03 ??= markerTb3_03 != null ? markerTb3_03.GetComponentInChildren<Image>(true) : null;
    }

    private static RectTransform FindRectTransform(string objectName)
    {
        GameObject item = FindSceneObject(objectName);
        return item != null ? item.GetComponent<RectTransform>() : null;
    }

    private static GameObject FindSceneObject(string objectName)
    {
        foreach (GameObject item in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (item.name == objectName && item.scene.IsValid())
            {
                return item;
            }
        }

        return null;
    }

    private static RectTransform FindChildRectTransform(RectTransform parent, string objectName)
    {
        if (parent == null)
        {
            return null;
        }

        RectTransform[] children = parent.GetComponentsInChildren<RectTransform>(true);
        foreach (RectTransform child in children)
        {
            if (child != null && child.name == objectName)
            {
                return child;
            }
        }

        return null;
    }
}

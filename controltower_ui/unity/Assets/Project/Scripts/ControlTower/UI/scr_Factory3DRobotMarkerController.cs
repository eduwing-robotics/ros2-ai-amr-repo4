using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

public class scr_Factory3DRobotMarkerController : MonoBehaviour
{
    [SerializeField] private scr_ControlTowerUIManager uiManager;
    [Header("Model References")]
    [SerializeField] private Transform targetRoot;
    [SerializeField] private Transform tb3_01_root;
    [SerializeField] private Transform tb3_02_root;
    [SerializeField] private Transform tb3_03_root;
    [SerializeField] private Transform noHelmetEventMarker;
    [SerializeField] private Transform fireEventMarker;
    [SerializeField] private Transform fallEventMarker;
    [Header("SLAM Map Conversion")]
    public float mapWidthPx = 52f;
    public float mapHeightPx = 52f;
    public float resolution = 0.05f;
    public float originX = -0.506f;
    public float originY = -0.607f;
    public float worldScale = 2.181818f;
    public float worldOffsetX = -1.512727f;
    public float worldOffsetZ = -1.403636f;
    public float worldHeight = 0.2f;
    public float yawOffsetDeg;
    public bool logCoordinateDebug;
    [Header("3D Stage Bounds")]
    [SerializeField] private bool useStageLocalBounds = true;
    [SerializeField] private float stageLocalWidth = 5.2f;
    [SerializeField] private float stageLocalDepth = 5.2f;
    [SerializeField] private float stageLocalCenterX;
    [SerializeField] private float stageLocalCenterZ;
    [Header("3D Position Calibration")]
    [SerializeField] private bool useCalibration3D = true;
    [SerializeField] private bool swapXY3D = true;
    [SerializeField] private bool flipX3D = true;
    [SerializeField] private bool flipY3D = true;
    [SerializeField] private float scaleX3D = 1f;
    [SerializeField] private float scaleZ3D = 1f;
    [SerializeField] private float offsetX3D;
    [SerializeField] private float offsetZ3D;
    [Header("3D Heading Calibration")]
    [SerializeField] private bool useHeadingCalibration3D = true;
    [SerializeField] private bool invertYaw3D;
    [SerializeField] private float headingOffsetDeg3D = 180f;
    [Header("TB3-03 Visual Alignment 3D")]
    [SerializeField] private bool useTb3_03VisualAlignment3D = true;
    [SerializeField] private float tb3_03WorldOffsetX;
    [SerializeField] private float tb3_03WorldOffsetZ;
    [SerializeField] private float tb3_03LocalRightOffset3D;
    [SerializeField] private float tb3_03LocalForwardOffset3D;
    [SerializeField] private float tb3_03HeightOffset3D;
    [SerializeField] private float tb3_03HeadingOffsetDeg3D;
    [Header("3D Smooth Follow")]
    [SerializeField, Min(0.0001f)] private float positionSmoothTime = 0.12f;
    [SerializeField, Min(0f)] private float poseInterpolationDelaySeconds = 0.12f;
    [SerializeField, Min(0.0001f)] private float rotationSmoothTime = 0.09f;
    [SerializeField, Min(0f)] private float maxPoseGapSeconds = 0.5f;
    [SerializeField, Min(0f)] private float teleportDistanceMeters = 0.75f;
    [SerializeField, Min(0f)] private float maxVisualLinearSpeed = 4f;
    [SerializeField, Min(0f)] private float maxVisualAngularSpeedDegPerSecond = 360f;
    [SerializeField, Min(0f)] private float positionDeadbandMeters = 0.003f;
    [SerializeField, Min(0f)] private float yawDeadbandDegrees = 0.5f;
    [SerializeField, Min(0f)] private float velocityDeadband = 0.005f;
    [SerializeField, Min(0f)] private float angularVelocityDeadband = 0.01f;
    [HideInInspector, SerializeField] private float robotPositionSmoothTime3D = 0.12f;
    [HideInInspector, SerializeField] private float robotRotationFollowSpeed3D = 12f;
    [Header("3D Wheel Animation")]
    [SerializeField] private Transform tb3_01LeftTire;
    [SerializeField] private Transform tb3_01RightTire;
    [SerializeField] private Transform tb3_02LeftTire;
    [SerializeField] private Transform tb3_02RightTire;
    [SerializeField] private Transform tb3_03LeftTire;
    [SerializeField] private Transform tb3_03RightTire;
    [SerializeField] private Vector3 robotForwardLocalAxis = Vector3.left;
    [SerializeField, Min(0.0001f)] private float wheelRadiusMeters = 0.033f;
    [SerializeField, Min(0.0001f)] private float wheelSeparationMeters = 0.160f;
    [SerializeField] private Vector3 leftWheelLocalRotationAxis = Vector3.forward;
    [SerializeField] private Vector3 rightWheelLocalRotationAxis = Vector3.forward;
    [SerializeField] private float leftWheelDirectionSign = 1f;
    [SerializeField] private float rightWheelDirectionSign = 1f;
    [SerializeField, Min(0f)] private float wheelAccelerationDegPerSecondSquared = 1080f;
    [SerializeField, Min(0f)] private float maxVisualWheelSpeedDegPerSecond = 1440f;
    [SerializeField, Min(0f)] private float robotMotionStaleSeconds3D = 3f;

    private readonly Dictionary<string, RobotVisualState> statesByRobot = new();
    private readonly Dictionary<string, RobotWheelState> wheelStatesByRobot = new();
    private readonly Dictionary<string, Color[]> baseColors = new();
    private readonly Dictionary<string, Material[]> runtimeMaterialsByRobot = new();
    private readonly Dictionary<string, RobotVisualMode> visualModesByRobot = new();
    private readonly Dictionary<string, float> lastCoordinateDebugLogTimeByRobot = new();
    private readonly HashSet<string> unexpectedScaleWarningsShown = new();
    private readonly HashSet<string> wheelWarningsShown = new();
    private readonly HashSet<string> wheelReadyLogsShown = new();
    private Vector3 originalScaleTb3_01;
    private Vector3 originalScaleTb3_02;
    private Vector3 originalScaleTb3_03;
    private bool hasOriginalScaleTb3_01;
    private bool hasOriginalScaleTb3_02;
    private bool hasOriginalScaleTb3_03;
    private bool missingModelWarningShown;
    private bool wasFactory3DViewActive;
    private bool pendingImmediateSync;
    private int lastImmediateSyncFrame = -1;
    private bool hasCalibrationSignature;
    private int calibrationSignature;
    private RobotPoseRuntimeState poseStateTb3_01;
    private RobotPoseRuntimeState poseStateTb3_02;
    private RobotPoseRuntimeState poseStateTb3_03;
    private const int PoseSampleCapacity = 4;
    private const float PositionSettleEpsilonSqr = 0.000001f;
    private const float RotationSettleEpsilonDeg = 0.05f;
    private const float WheelSpeedSettleEpsilon = 0.01f;
    private const float CoordinateDebugLogIntervalSeconds = 1f;
    private static readonly ProfilerMarker RobotUpdateMarker = new("ControlTower.Factory3D.RobotUpdate");
    private static readonly ProfilerMarker WheelUpdateMarker = new("ControlTower.Factory3D.WheelUpdate");
    private static readonly ProfilerMarker RendererStatusColorMarker = new("ControlTower.Renderer.StatusColor");

    private struct RobotVisualState
    {
        public float RosX;
        public float RosY;
        public float Yaw;
        public string Status;
        public float LinearVelocity;
        public float AngularVelocity;
        public float ReceiveTime;
    }

    private struct RobotPoseSample3D
    {
        public Vector3 LocalPosition;
        public float YawDeg;
        public float LinearVelocity;
        public float AngularVelocity;
        public float ReceiveTime;
    }

    private sealed class RobotPoseRuntimeState
    {
        public string RobotId;
        public Transform Root;
        public Vector3 OriginalLocalPosition;
        public Quaternion OriginalLocalRotation;
        public readonly RobotPoseSample3D[] Samples = new RobotPoseSample3D[PoseSampleCapacity];
        public int SampleCount;
        public bool HasValidPose;
        public bool HasRenderedPose;
        public bool ForceSnapOnNextRender;
        public Vector3 LatestTargetLocalPosition;
        public float LatestTargetYawDeg;
        public Vector3 RenderedLocalPosition;
        public float RenderedYawDeg;
        public Vector3 PositionSmoothVelocity;
        public float YawSmoothVelocity;
        public float LastPoseReceiveTime;
        public float CurrentLinearVisualSpeed;
        public float CurrentAngularVisualSpeed;
    }

    private enum RobotVisualMode
    {
        NoData,
        Normal,
        Alert,
        Stale,
        Disconnected
    }

    private sealed class RobotWheelState
    {
        public Transform LeftTire;
        public Transform RightTire;
        public Quaternion InitialLeftLocalRotation;
        public Quaternion InitialRightLocalRotation;
        public Vector3 InitialLeftLocalScale;
        public Vector3 InitialRightLocalScale;
        public float AccumulatedLeftAngleDeg;
        public float AccumulatedRightAngleDeg;
        public float CurrentLeftSpeedDegPerSecond;
        public float CurrentRightSpeedDegPerSecond;
        public bool HasLeftBaseline;
        public bool HasRightBaseline;
        public bool IsConfigurationValid;
    }

    private void Awake()
    {
        ResolveReferences();
        CaptureOriginalRobotScales();
        InitializePoseRuntimeStates();
        ResolveAndInitializeWheelReferences();
        calibrationSignature = CalculateCalibrationSignature();
        hasCalibrationSignature = true;
    }

    private void OnEnable()
    {
        ResolveReferences();
        CaptureOriginalRobotScales();
        InitializePoseRuntimeStates();
        ResolveAndInitializeWheelReferences();
        RestoreAllRobotRootScales(false);
        RestoreAllWheelScales(false);
        Subscribe();
        wasFactory3DViewActive = false;
        pendingImmediateSync = true;
        OnViewActivated();
    }

    private void OnDisable()
    {
        Unsubscribe();
        wasFactory3DViewActive = false;
        RestoreAllRobotRootScales(false);
        RestoreAllWheelScales(false);
    }

    private void OnDestroy()
    {
        RestoreAllRobotRootScales(false);
        RestoreAllWheelTransforms();
    }

    private void Update()
    {
        CheckForCalibrationChange();

        bool factory3DViewActive = uiManager == null || uiManager.IsFactory3DViewActive;
        if (!factory3DViewActive)
        {
            wasFactory3DViewActive = false;
            pendingImmediateSync = true;
            return;
        }

        if (!wasFactory3DViewActive || pendingImmediateSync)
        {
            OnViewActivated();
        }

        float deltaTime = Time.unscaledDeltaTime;
        using (RobotUpdateMarker.Auto())
        {
            UpdateRenderedPose(poseStateTb3_01, deltaTime);
            UpdateRenderedPose(poseStateTb3_02, deltaTime);
            UpdateRenderedPose(poseStateTb3_03, deltaTime);
            UpdateVisual("tb3-01", tb3_01_root);
            UpdateVisual("tb3-02", tb3_02_root);
            UpdateVisual("tb3-03", tb3_03_root);
        }

        using (WheelUpdateMarker.Auto())
        {
            UpdateWheelAnimations();
        }
    }

    public void OnViewActivated()
    {
        ResolveReferences();
        CaptureOriginalRobotScales();
        InitializePoseRuntimeStates();
        ResolveAndInitializeWheelReferences();
        Subscribe();

        if (uiManager == null || !uiManager.IsFactory3DViewActive)
        {
            pendingImmediateSync = true;
            wasFactory3DViewActive = false;
            return;
        }

        if (lastImmediateSyncFrame == Time.frameCount && wasFactory3DViewActive)
        {
            return;
        }

        SyncAllRobotMarkersToLatestPoseImmediate();
        StopAllWheelSpeedsImmediately();
        RestoreAllRobotRootScales(false);
        RestoreAllWheelScales(false);
        pendingImmediateSync = false;
        wasFactory3DViewActive = true;
        lastImmediateSyncFrame = Time.frameCount;
    }

    private void LateUpdate()
    {
        if (uiManager != null && !uiManager.IsFactory3DViewActive)
        {
            return;
        }

        RestoreAllRobotRootScales(true);
        RestoreAllWheelScales(true);
    }

    private void Subscribe()
    {
        if (uiManager == null) return;
        uiManager.RobotStateUpdated -= HandleRobotStateUpdated;
        uiManager.RobotStateUpdated += HandleRobotStateUpdated;
    }

    private void Unsubscribe()
    {
        if (uiManager == null) return;
        uiManager.RobotStateUpdated -= HandleRobotStateUpdated;
    }

    private void HandleRobotStateUpdated(string robotId, float x, float y, float yaw, string status)
    {
        statesByRobot[robotId] = BuildRobotVisualState(robotId, x, y, yaw, status);
        ApplyPose(robotId, GetRobotRoot(robotId));
    }

    private RobotVisualState BuildRobotVisualState(string robotId, float x, float y, float yaw, string status)
    {
        float linearVelocity = 0f;
        float angularVelocity = 0f;
        float receiveTime = Time.unscaledTime;
        if (uiManager != null && uiManager.TryGetRobotMotionState(
                robotId,
                out float cachedLinearVelocity,
                out float cachedAngularVelocity,
                out _,
                out float cachedReceiveTime))
        {
            linearVelocity = IsFinite(cachedLinearVelocity) ? cachedLinearVelocity : 0f;
            angularVelocity = IsFinite(cachedAngularVelocity) ? cachedAngularVelocity : 0f;
            if (cachedReceiveTime > 0f)
            {
                receiveTime = cachedReceiveTime;
            }
        }

        return new RobotVisualState
        {
            RosX = x,
            RosY = y,
            Yaw = yaw,
            Status = status,
            LinearVelocity = linearVelocity,
            AngularVelocity = angularVelocity,
            ReceiveTime = receiveTime
        };
    }

    private void SyncAllRobotMarkersToLatestPoseImmediate()
    {
        SyncRobotMarkerToLatestPoseImmediate("tb3-01");
        SyncRobotMarkerToLatestPoseImmediate("tb3-02");
        SyncRobotMarkerToLatestPoseImmediate("tb3-03");
    }

    private void SyncRobotMarkerToLatestPoseImmediate(string robotId)
    {
        if (uiManager != null &&
            uiManager.TryGetRobotState(robotId, out float x, out float y, out float yaw, out string status))
        {
            statesByRobot[robotId] = BuildRobotVisualState(robotId, x, y, yaw, status);
            ApplyPose(robotId, GetRobotRoot(robotId), true);
        }

        RobotPoseRuntimeState runtimeState = GetPoseRuntimeState(robotId);
        if (runtimeState != null && runtimeState.HasValidPose && runtimeState.SampleCount > 0)
        {
            SnapRenderedPoseToLatest(runtimeState);
        }

        UpdateVisual(robotId, GetRobotRoot(robotId));
    }

    private void ApplyPose(string robotId, Transform robotRoot, bool forceSnap = false)
    {
        if (robotRoot == null) return;
        if (!statesByRobot.TryGetValue(robotId, out RobotVisualState state)) return;
        RobotPoseRuntimeState runtimeState = GetPoseRuntimeState(robotId);
        if (runtimeState == null || runtimeState.Root != robotRoot) return;

        Vector2 local3D = ConvertRosToLocal3D(state.RosX, state.RosY, out float pixelX, out float pixelY, out Vector2 norm3D, out Vector2 calibratedNorm3D);
        float unityX = local3D.x;
        float unityZ = local3D.y;
        float rawYawDeg = state.Yaw * Mathf.Rad2Deg;
        float calibratedYawDeg = ApplyHeadingCalibration3D(rawYawDeg);
        float rotationY = -calibratedYawDeg + yawOffsetDeg;
        Vector3 commonWorld = new Vector3(unityX, runtimeState.OriginalLocalPosition.y, unityZ);
        Vector2 worldOffset = Vector2.zero;
        float localRightOffset = 0f;
        float localForwardOffset = 0f;
        Vector3 finalTarget = commonWorld;
        float finalYawDeg = rotationY;

        if (useTb3_03VisualAlignment3D && string.Equals(robotId, "tb3-03", StringComparison.Ordinal))
        {
            worldOffset = new Vector2(tb3_03WorldOffsetX, tb3_03WorldOffsetZ);
            localRightOffset = tb3_03LocalRightOffset3D;
            localForwardOffset = tb3_03LocalForwardOffset3D;
            finalTarget.x += worldOffset.x;
            finalTarget.z += worldOffset.y;
            finalTarget += CalculateRobotLocalOffset3D(
                runtimeState,
                rotationY,
                localRightOffset,
                localForwardOffset);
            finalTarget.y += tb3_03HeightOffset3D;
            finalYawDeg += tb3_03HeadingOffsetDeg3D;
        }

        QueuePoseSample(runtimeState, finalTarget, finalYawDeg, state, forceSnap);

        if (string.Equals(robotId, "tb3-03", StringComparison.Ordinal) &&
            ShouldLogCoordinateDebug(robotId))
        {
            Debug.Log(
                $"[TB3-03 Alignment 3D] rawPose=({state.RosX:F3},{state.RosY:F3},{state.Yaw:F3}) " +
                $"commonWorld=({commonWorld.x:F3},{commonWorld.y:F3},{commonWorld.z:F3}) " +
                $"worldOffset=({worldOffset.x:F3},{worldOffset.y:F3}) " +
                $"localOffset=({localRightOffset:F3},{localForwardOffset:F3}) " +
                $"finalTarget=({finalTarget.x:F3},{finalTarget.y:F3},{finalTarget.z:F3}) finalYaw={finalYawDeg:F2}");
        }
    }

    private Vector3 CalculateRobotLocalOffset3D(
        RobotPoseRuntimeState runtimeState,
        float yawDeg,
        float rightOffset,
        float forwardOffset)
    {
        Quaternion targetRotation = runtimeState.OriginalLocalRotation *
                                    Quaternion.AngleAxis(yawDeg, Vector3.up);
        Vector3 forwardAxis = robotForwardLocalAxis.sqrMagnitude > 0.000001f
            ? robotForwardLocalAxis.normalized
            : Vector3.forward;
        Vector3 forward = targetRotation * forwardAxis;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.000001f)
        {
            return Vector3.zero;
        }

        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        return right * rightOffset + forward * forwardOffset;
    }

    private bool ShouldLogCoordinateDebug(string robotId)
    {
        if (!logCoordinateDebug || uiManager == null || !uiManager.IsFactory3DViewActive)
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

    private void InitializePoseRuntimeStates()
    {
        poseStateTb3_01 = InitializePoseRuntimeState(poseStateTb3_01, "tb3-01", tb3_01_root);
        poseStateTb3_02 = InitializePoseRuntimeState(poseStateTb3_02, "tb3-02", tb3_02_root);
        poseStateTb3_03 = InitializePoseRuntimeState(poseStateTb3_03, "tb3-03", tb3_03_root);
    }

    private static RobotPoseRuntimeState InitializePoseRuntimeState(
        RobotPoseRuntimeState existing,
        string robotId,
        Transform robotRoot)
    {
        if (robotRoot == null)
        {
            return existing;
        }

        if (existing != null && existing.Root == robotRoot)
        {
            return existing;
        }

        return new RobotPoseRuntimeState
        {
            RobotId = robotId,
            Root = robotRoot,
            OriginalLocalPosition = robotRoot.localPosition,
            OriginalLocalRotation = robotRoot.localRotation,
            RenderedLocalPosition = robotRoot.localPosition,
            RenderedYawDeg = 0f
        };
    }

    private RobotPoseRuntimeState GetPoseRuntimeState(string robotId)
    {
        return robotId switch
        {
            "tb3-01" => poseStateTb3_01,
            "tb3-02" => poseStateTb3_02,
            "tb3-03" => poseStateTb3_03,
            _ => null
        };
    }

    private void QueuePoseSample(
        RobotPoseRuntimeState runtimeState,
        Vector3 targetPosition,
        float targetYawDeg,
        RobotVisualState visualState,
        bool forceSnap)
    {
        if (runtimeState == null || !IsFinite(targetPosition) || !IsFinite(targetYawDeg))
        {
            return;
        }

        float receiveTime = visualState.ReceiveTime > 0f ? visualState.ReceiveTime : Time.unscaledTime;
        RobotPoseSample3D sample = new()
        {
            LocalPosition = targetPosition,
            YawDeg = NormalizeAngleDeg(targetYawDeg),
            LinearVelocity = visualState.LinearVelocity,
            AngularVelocity = visualState.AngularVelocity,
            ReceiveTime = receiveTime
        };

        if (runtimeState.SampleCount > 0)
        {
            int latestIndex = runtimeState.SampleCount - 1;
            RobotPoseSample3D previous = runtimeState.Samples[latestIndex];
            bool isStationary = Mathf.Abs(sample.LinearVelocity) <= velocityDeadband &&
                                Mathf.Abs(sample.AngularVelocity) <= angularVelocityDeadband;
            float sampleDistance = DistanceXZ(previous.LocalPosition, sample.LocalPosition);
            float sampleYawDelta = Mathf.Abs(Mathf.DeltaAngle(previous.YawDeg, sample.YawDeg));
            if (isStationary &&
                sampleDistance <= positionDeadbandMeters &&
                sampleYawDelta <= yawDeadbandDegrees)
            {
                sample.LocalPosition = previous.LocalPosition;
                sample.YawDeg = previous.YawDeg;
            }

            if (Mathf.Abs(sample.ReceiveTime - previous.ReceiveTime) <= 0.0001f)
            {
                runtimeState.Samples[latestIndex] = sample;
            }
            else
            {
                float poseGap = sample.ReceiveTime - previous.ReceiveTime;
                bool teleport = teleportDistanceMeters > 0f && sampleDistance > teleportDistanceMeters;
                if (forceSnap || teleport)
                {
                    runtimeState.SampleCount = 0;
                    runtimeState.ForceSnapOnNextRender = true;
                }
                else if (poseGap > Mathf.Max(0f, maxPoseGapSeconds))
                {
                    runtimeState.SampleCount = 0;
                }

                AppendPoseSample(runtimeState, sample);
            }
        }
        else
        {
            AppendPoseSample(runtimeState, sample);
            runtimeState.ForceSnapOnNextRender = true;
        }

        if (forceSnap)
        {
            runtimeState.ForceSnapOnNextRender = true;
        }

        runtimeState.HasValidPose = true;
        runtimeState.LatestTargetLocalPosition = sample.LocalPosition;
        runtimeState.LatestTargetYawDeg = sample.YawDeg;
        runtimeState.LastPoseReceiveTime = receiveTime;
    }

    private static void AppendPoseSample(RobotPoseRuntimeState runtimeState, RobotPoseSample3D sample)
    {
        if (runtimeState.SampleCount >= PoseSampleCapacity)
        {
            for (int i = 1; i < PoseSampleCapacity; i++)
            {
                runtimeState.Samples[i - 1] = runtimeState.Samples[i];
            }

            runtimeState.SampleCount = PoseSampleCapacity - 1;
        }

        runtimeState.Samples[runtimeState.SampleCount] = sample;
        runtimeState.SampleCount++;
    }

    private void UpdateRenderedPose(RobotPoseRuntimeState runtimeState, float deltaTime)
    {
        if (runtimeState == null || runtimeState.Root == null || !runtimeState.HasValidPose || runtimeState.SampleCount == 0)
        {
            return;
        }

        bool disconnected = uiManager != null && !uiManager.IsWebSocketConnected;
        bool stale = runtimeState.LastPoseReceiveTime <= 0f ||
                     Time.unscaledTime - runtimeState.LastPoseReceiveTime > robotMotionStaleSeconds3D;

        if (runtimeState.ForceSnapOnNextRender || !runtimeState.HasRenderedPose)
        {
            SnapRenderedPoseToLatest(runtimeState);
            return;
        }

        if (disconnected || stale || deltaTime <= 0f)
        {
            runtimeState.CurrentLinearVisualSpeed = 0f;
            runtimeState.CurrentAngularVisualSpeed = 0f;
            runtimeState.PositionSmoothVelocity = Vector3.zero;
            runtimeState.YawSmoothVelocity = 0f;
            return;
        }

        float renderTime = Time.unscaledTime - Mathf.Max(0f, poseInterpolationDelaySeconds);
        EvaluatePoseAtTime(
            runtimeState,
            renderTime,
            out Vector3 targetPosition,
            out float targetYawDeg,
            out float targetLinearVelocity,
            out float targetAngularVelocity);

        bool targetStationary = Mathf.Abs(targetLinearVelocity) <= velocityDeadband &&
                                Mathf.Abs(targetAngularVelocity) <= angularVelocityDeadband;
        if (targetStationary &&
            DistanceXZ(runtimeState.RenderedLocalPosition, targetPosition) <= positionDeadbandMeters)
        {
            targetPosition = runtimeState.RenderedLocalPosition;
        }

        if (targetStationary &&
            Mathf.Abs(Mathf.DeltaAngle(runtimeState.RenderedYawDeg, targetYawDeg)) <= yawDeadbandDegrees)
        {
            targetYawDeg = runtimeState.RenderedYawDeg;
        }

        Vector3 previousPosition = runtimeState.RenderedLocalPosition;
        float previousYawDeg = runtimeState.RenderedYawDeg;
        float linearSpeedLimit = Mathf.Max(0.01f, maxVisualLinearSpeed);
        float positionSmoothDuration = positionSmoothTime > 0f
            ? positionSmoothTime
            : Mathf.Max(0.0001f, robotPositionSmoothTime3D);
        runtimeState.RenderedLocalPosition = Vector3.SmoothDamp(
            runtimeState.RenderedLocalPosition,
            targetPosition,
            ref runtimeState.PositionSmoothVelocity,
            positionSmoothDuration,
            linearSpeedLimit,
            deltaTime);
        runtimeState.RenderedLocalPosition.y = runtimeState.OriginalLocalPosition.y;

        float angularSpeedLimit = maxVisualAngularSpeedDegPerSecond > 0f
            ? maxVisualAngularSpeedDegPerSecond
            : Mathf.Max(1f, robotRotationFollowSpeed3D * 30f);
        runtimeState.RenderedYawDeg = Mathf.SmoothDampAngle(
            runtimeState.RenderedYawDeg,
            targetYawDeg,
            ref runtimeState.YawSmoothVelocity,
            Mathf.Max(0.0001f, rotationSmoothTime),
            angularSpeedLimit,
            deltaTime);

        float remainingPositionDistance = DistanceXZ(runtimeState.RenderedLocalPosition, targetPosition);
        if (remainingPositionDistance * remainingPositionDistance <= PositionSettleEpsilonSqr &&
            runtimeState.PositionSmoothVelocity.sqrMagnitude <= PositionSettleEpsilonSqr)
        {
            runtimeState.RenderedLocalPosition = targetPosition;
            runtimeState.RenderedLocalPosition.y = runtimeState.OriginalLocalPosition.y;
            runtimeState.PositionSmoothVelocity = Vector3.zero;
        }

        if (Mathf.Abs(Mathf.DeltaAngle(runtimeState.RenderedYawDeg, targetYawDeg)) <= RotationSettleEpsilonDeg &&
            Mathf.Abs(runtimeState.YawSmoothVelocity) <= RotationSettleEpsilonDeg)
        {
            runtimeState.RenderedYawDeg = targetYawDeg;
            runtimeState.YawSmoothVelocity = 0f;
        }

        UpdateVisualMotionSpeeds(runtimeState, previousPosition, previousYawDeg, deltaTime);
        WriteRenderedPose(runtimeState);
    }

    private static void EvaluatePoseAtTime(
        RobotPoseRuntimeState runtimeState,
        float renderTime,
        out Vector3 localPosition,
        out float yawDeg,
        out float linearVelocity,
        out float angularVelocity)
    {
        RobotPoseSample3D first = runtimeState.Samples[0];
        if (runtimeState.SampleCount == 1 || renderTime <= first.ReceiveTime)
        {
            localPosition = first.LocalPosition;
            yawDeg = first.YawDeg;
            linearVelocity = first.LinearVelocity;
            angularVelocity = first.AngularVelocity;
            return;
        }

        for (int i = 0; i < runtimeState.SampleCount - 1; i++)
        {
            RobotPoseSample3D from = runtimeState.Samples[i];
            RobotPoseSample3D to = runtimeState.Samples[i + 1];
            if (renderTime > to.ReceiveTime)
            {
                continue;
            }

            float duration = Mathf.Max(0.0001f, to.ReceiveTime - from.ReceiveTime);
            float t = Mathf.Clamp01((renderTime - from.ReceiveTime) / duration);
            localPosition = Vector3.Lerp(from.LocalPosition, to.LocalPosition, t);
            yawDeg = Mathf.LerpAngle(from.YawDeg, to.YawDeg, t);
            linearVelocity = Mathf.Lerp(from.LinearVelocity, to.LinearVelocity, t);
            angularVelocity = Mathf.Lerp(from.AngularVelocity, to.AngularVelocity, t);
            return;
        }

        RobotPoseSample3D latest = runtimeState.Samples[runtimeState.SampleCount - 1];
        localPosition = latest.LocalPosition;
        yawDeg = latest.YawDeg;
        linearVelocity = latest.LinearVelocity;
        angularVelocity = latest.AngularVelocity;
    }

    private void SnapRenderedPoseToLatest(RobotPoseRuntimeState runtimeState)
    {
        RobotPoseSample3D latest = runtimeState.Samples[runtimeState.SampleCount - 1];
        runtimeState.RenderedLocalPosition = latest.LocalPosition;
        runtimeState.RenderedLocalPosition.y = runtimeState.OriginalLocalPosition.y;
        runtimeState.RenderedYawDeg = latest.YawDeg;
        runtimeState.PositionSmoothVelocity = Vector3.zero;
        runtimeState.YawSmoothVelocity = 0f;
        runtimeState.CurrentLinearVisualSpeed = 0f;
        runtimeState.CurrentAngularVisualSpeed = 0f;
        runtimeState.HasRenderedPose = true;
        runtimeState.ForceSnapOnNextRender = false;
        runtimeState.Samples[0] = latest;
        runtimeState.SampleCount = 1;
        StopWheelSpeedsImmediately(runtimeState.RobotId);
        WriteRenderedPose(runtimeState);
    }

    private void UpdateVisualMotionSpeeds(
        RobotPoseRuntimeState runtimeState,
        Vector3 previousPosition,
        float previousYawDeg,
        float deltaTime)
    {
        Vector3 displacement = runtimeState.RenderedLocalPosition - previousPosition;
        displacement.y = 0f;
        Quaternion renderedRotation = runtimeState.OriginalLocalRotation *
                                      Quaternion.AngleAxis(runtimeState.RenderedYawDeg, Vector3.up);
        Vector3 localForward = renderedRotation * robotForwardLocalAxis.normalized;
        localForward.y = 0f;
        if (localForward.sqrMagnitude > 0.000001f)
        {
            localForward.Normalize();
        }

        float linearVelocity = localForward.sqrMagnitude > 0.000001f
            ? Vector3.Dot(displacement, localForward) / deltaTime
            : displacement.magnitude / deltaTime;
        float angularVelocity = Mathf.DeltaAngle(previousYawDeg, runtimeState.RenderedYawDeg) *
                                Mathf.Deg2Rad / deltaTime;
        float linearSpeedLimit = Mathf.Max(0.01f, maxVisualLinearSpeed);
        runtimeState.CurrentLinearVisualSpeed = Mathf.Abs(linearVelocity) <= velocityDeadband
            ? 0f
            : Mathf.Clamp(linearVelocity, -linearSpeedLimit, linearSpeedLimit);
        float maxAngularVelocity = Mathf.Max(1f, maxVisualAngularSpeedDegPerSecond) * Mathf.Deg2Rad;
        runtimeState.CurrentAngularVisualSpeed = Mathf.Abs(angularVelocity) <= angularVelocityDeadband
            ? 0f
            : Mathf.Clamp(angularVelocity, -maxAngularVelocity, maxAngularVelocity);
    }

    private static void WriteRenderedPose(RobotPoseRuntimeState runtimeState)
    {
        Transform robotRoot = runtimeState.Root;
        if ((robotRoot.localPosition - runtimeState.RenderedLocalPosition).sqrMagnitude > PositionSettleEpsilonSqr)
        {
            robotRoot.localPosition = runtimeState.RenderedLocalPosition;
        }

        Quaternion renderedRotation = runtimeState.OriginalLocalRotation *
                                      Quaternion.AngleAxis(runtimeState.RenderedYawDeg, Vector3.up);
        if (Quaternion.Angle(robotRoot.localRotation, renderedRotation) > RotationSettleEpsilonDeg)
        {
            robotRoot.localRotation = renderedRotation;
        }
    }

    private void CheckForCalibrationChange()
    {
        int currentSignature = CalculateCalibrationSignature();
        if (!hasCalibrationSignature)
        {
            calibrationSignature = currentSignature;
            hasCalibrationSignature = true;
            return;
        }

        if (currentSignature == calibrationSignature)
        {
            return;
        }

        calibrationSignature = currentSignature;
        ApplyPose("tb3-01", tb3_01_root, true);
        ApplyPose("tb3-02", tb3_02_root, true);
        ApplyPose("tb3-03", tb3_03_root, true);
    }

    private int CalculateCalibrationSignature()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + mapWidthPx.GetHashCode();
            hash = hash * 31 + mapHeightPx.GetHashCode();
            hash = hash * 31 + resolution.GetHashCode();
            hash = hash * 31 + originX.GetHashCode();
            hash = hash * 31 + originY.GetHashCode();
            hash = hash * 31 + worldScale.GetHashCode();
            hash = hash * 31 + worldOffsetX.GetHashCode();
            hash = hash * 31 + worldOffsetZ.GetHashCode();
            hash = hash * 31 + useStageLocalBounds.GetHashCode();
            hash = hash * 31 + stageLocalWidth.GetHashCode();
            hash = hash * 31 + stageLocalDepth.GetHashCode();
            hash = hash * 31 + stageLocalCenterX.GetHashCode();
            hash = hash * 31 + stageLocalCenterZ.GetHashCode();
            hash = hash * 31 + useCalibration3D.GetHashCode();
            hash = hash * 31 + swapXY3D.GetHashCode();
            hash = hash * 31 + flipX3D.GetHashCode();
            hash = hash * 31 + flipY3D.GetHashCode();
            hash = hash * 31 + scaleX3D.GetHashCode();
            hash = hash * 31 + scaleZ3D.GetHashCode();
            hash = hash * 31 + offsetX3D.GetHashCode();
            hash = hash * 31 + offsetZ3D.GetHashCode();
            hash = hash * 31 + useHeadingCalibration3D.GetHashCode();
            hash = hash * 31 + invertYaw3D.GetHashCode();
            hash = hash * 31 + headingOffsetDeg3D.GetHashCode();
            hash = hash * 31 + yawOffsetDeg.GetHashCode();
            return hash;
        }
    }

    private static float DistanceXZ(Vector3 first, Vector3 second)
    {
        float deltaX = first.x - second.x;
        float deltaZ = first.z - second.z;
        return Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public void SetIncidentMarker(string incidentType, float rosX, float rosY, bool visible)
    {
        ResolveReferences();
        Transform marker = GetIncidentMarker(incidentType);
        if (marker == null)
        {
            return;
        }

        if (!visible)
        {
            marker.gameObject.SetActive(false);
            return;
        }

        Vector2 local3D = ConvertRosToLocal3D(rosX, rosY, out _, out _, out _, out _);
        marker.localPosition = ConvertRobotLocalToMarkerParentLocal(local3D, marker);
        marker.gameObject.SetActive(true);
    }

    private Vector3 ConvertRobotLocalToMarkerParentLocal(Vector2 robotLocalXZ, Transform marker)
    {
        Transform referenceParent = tb3_01_root != null ? tb3_01_root.parent : targetRoot;
        if (referenceParent == null || marker == null || marker.parent == referenceParent)
        {
            Vector3 sameParentPosition = marker != null ? marker.localPosition : Vector3.zero;
            sameParentPosition.x = robotLocalXZ.x;
            sameParentPosition.z = robotLocalXZ.y;
            return sameParentPosition;
        }

        Vector3 referenceLocal = new Vector3(robotLocalXZ.x, 0f, robotLocalXZ.y);
        Vector3 worldPosition = referenceParent.TransformPoint(referenceLocal);
        worldPosition.y = marker.position.y;
        return marker.parent != null ? marker.parent.InverseTransformPoint(worldPosition) : worldPosition;
    }

    private Vector2 ConvertRosToLocal3D(float rosX, float rosY, out float pixelX, out float pixelY, out Vector2 norm3D, out Vector2 calibratedNorm3D)
    {
        pixelX = (rosX - originX) / resolution;
        pixelY = (rosY - originY) / resolution;
        float screenX = pixelX;
        float screenZ = mapHeightPx - pixelY;
        norm3D = new Vector2(
            Mathf.Clamp01(SafeDivide(screenX, mapWidthPx)),
            Mathf.Clamp01(SafeDivide(screenZ, mapHeightPx)));
        calibratedNorm3D = ApplyNormalizedCalibration3D(norm3D);
        return CalculateLocalPosition3D(calibratedNorm3D, rosX, rosY);
    }

    private Vector2 ApplyNormalizedCalibration3D(Vector2 normalizedPosition)
    {
        if (!useCalibration3D) return normalizedPosition;

        float x = normalizedPosition.x;
        float z = normalizedPosition.y;

        if (swapXY3D)
        {
            (x, z) = (z, x);
        }

        if (flipX3D) x = 1f - x;
        if (flipY3D) z = 1f - z;

        return new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(z));
    }

    private Vector2 CalculateLocalPosition3D(Vector2 calibratedNorm3D, float rosX, float rosY)
    {
        if (!useStageLocalBounds)
        {
            Vector2 legacyRaw = new Vector2(rosX, rosY);
            Vector2 legacyCalibrated = ApplyLegacyMetricCalibration3D(legacyRaw);
            return new Vector2(
                legacyCalibrated.x * worldScale + worldOffsetX,
                legacyCalibrated.y * worldScale + worldOffsetZ);
        }

        float width = Mathf.Max(0.001f, stageLocalWidth);
        float depth = Mathf.Max(0.001f, stageLocalDepth);
        float localX = (calibratedNorm3D.x - 0.5f) * width;
        float localZ = (calibratedNorm3D.y - 0.5f) * depth;
        localX = localX * scaleX3D + offsetX3D + stageLocalCenterX;
        localZ = localZ * scaleZ3D + offsetZ3D + stageLocalCenterZ;

        float halfWidth = width * 0.5f;
        float halfDepth = depth * 0.5f;
        localX = Mathf.Clamp(localX, stageLocalCenterX - halfWidth, stageLocalCenterX + halfWidth);
        localZ = Mathf.Clamp(localZ, stageLocalCenterZ - halfDepth, stageLocalCenterZ + halfDepth);

        return new Vector2(localX, localZ);
    }

    public bool TryProjectStageLocalToNormalized(Vector3 stageLocalPosition, out Vector2 normalizedPosition)
    {
        normalizedPosition = Vector2.zero;
        if (!useStageLocalBounds)
        {
            return false;
        }

        float width = Mathf.Max(0.001f, stageLocalWidth);
        float depth = Mathf.Max(0.001f, stageLocalDepth);
        float normalizedX = ((stageLocalPosition.x - stageLocalCenterX) / width) + 0.5f;
        float normalizedZ = ((stageLocalPosition.z - stageLocalCenterZ) / depth) + 0.5f;
        normalizedPosition = new Vector2(Mathf.Clamp01(normalizedX), Mathf.Clamp01(normalizedZ));
        return true;
    }

    private Vector2 ApplyLegacyMetricCalibration3D(Vector2 rawPosition)
    {
        if (!useCalibration3D) return rawPosition;

        float x = rawPosition.x;
        float z = rawPosition.y;

        if (swapXY3D)
        {
            (x, z) = (z, x);
        }

        if (flipX3D) x = -x;
        if (flipY3D) z = -z;

        return new Vector2(x, z);
    }

    private float ApplyHeadingCalibration3D(float rawYawDeg)
    {
        float calibratedYawDeg = rawYawDeg;

        if (useHeadingCalibration3D)
        {
            if (swapXY3D)
            {
                calibratedYawDeg = 90f - calibratedYawDeg;
            }

            if (flipX3D)
            {
                calibratedYawDeg = 180f - calibratedYawDeg;
            }

            if (flipY3D)
            {
                calibratedYawDeg = -calibratedYawDeg;
            }

            if (invertYaw3D)
            {
                calibratedYawDeg = -calibratedYawDeg;
            }

            calibratedYawDeg += headingOffsetDeg3D;
        }

        return NormalizeAngleDeg(calibratedYawDeg);
    }

    private void UpdateVisual(string robotId, Transform robotRoot)
    {
        if (robotRoot == null) return;
        bool hasState = statesByRobot.TryGetValue(robotId, out RobotVisualState state);

        if (!robotRoot.gameObject.activeSelf)
        {
            robotRoot.gameObject.SetActive(true);
        }

        CacheBaseColors(robotId, robotRoot);

        RobotVisualMode visualMode = hasState
            ? GetRobotVisualMode(robotId, state.Status)
            : RobotVisualMode.NoData;
        bool visualModeChanged = !visualModesByRobot.TryGetValue(robotId, out RobotVisualMode previousMode) ||
                                 previousMode != visualMode;
        visualModesByRobot[robotId] = visualMode;
        if (visualMode != RobotVisualMode.Alert && !visualModeChanged)
        {
            return;
        }

        if (!baseColors.TryGetValue(robotId, out Color[] colors) ||
            !runtimeMaterialsByRobot.TryGetValue(robotId, out Material[] materials)) return;
        float alertBlend = .5f + .5f * Mathf.Sin(Time.unscaledTime * 7f);
        Color statusColor = visualMode switch
        {
            RobotVisualMode.Alert => Color.Lerp(
                new Color32(0xEF, 0x44, 0x44, 255),
                new Color32(0xFF, 0x9A, 0x3D, 255),
                alertBlend),
            RobotVisualMode.Stale => new Color32(0xF5, 0x9E, 0x0B, 255),
            RobotVisualMode.Disconnected => new Color32(0xEF, 0x44, 0x44, 255),
            RobotVisualMode.NoData => new Color32(0x94, 0xA3, 0xB8, 255),
            _ => Color.white
        };
        using (RendererStatusColorMarker.Auto())
        {
            for (int i = 0; i < materials.Length && i < colors.Length; i++)
            {
                Material material = materials[i];
                if (material != null)
                {
                    material.color = visualMode == RobotVisualMode.Normal ? colors[i] : statusColor;
                }
            }
        }
    }

    private RobotVisualMode GetRobotVisualMode(string robotId, string status)
    {
        if ((uiManager != null && !uiManager.IsWebSocketConnected) || IsDisconnectedStatus(status))
        {
            return RobotVisualMode.Disconnected;
        }

        RobotPoseRuntimeState runtimeState = GetPoseRuntimeState(robotId);
        if (string.Equals(status, "STALE", StringComparison.OrdinalIgnoreCase) ||
            runtimeState == null ||
            runtimeState.LastPoseReceiveTime <= 0f ||
            Time.unscaledTime - runtimeState.LastPoseReceiveTime > robotMotionStaleSeconds3D)
        {
            return RobotVisualMode.Stale;
        }

        return IsAlertStatus(status) ? RobotVisualMode.Alert : RobotVisualMode.Normal;
    }

    private void CacheBaseColors(string robotId, Transform robotRoot)
    {
        if (baseColors.ContainsKey(robotId)) return;

        Renderer[] renderers = robotRoot.GetComponentsInChildren<Renderer>(true);
        Color[] colors = new Color[renderers.Length];
        Material[] materials = new Material[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            Material material = renderers[i] != null ? renderers[i].material : null;
            materials[i] = material;
            colors[i] = material != null ? material.color : Color.white;
        }

        baseColors[robotId] = colors;
        runtimeMaterialsByRobot[robotId] = materials;
    }

    private void ResolveAndInitializeWheelReferences()
    {
        Transform wheelRoot01 = ResolveWheelRobotRoot(tb3_01_root, "TB3_01_Root_Fixed");
        Transform wheelRoot02 = ResolveWheelRobotRoot(tb3_02_root, "TB3_02_Root_Fixed");
        Transform wheelRoot03 = ResolveWheelRobotRoot(tb3_03_root, "TB3_03_Root_Fixed");

        tb3_01LeftTire = ResolveWheelReference(tb3_01LeftTire, wheelRoot01, "TB3_01_Model", "left_tire");
        tb3_01RightTire = ResolveWheelReference(tb3_01RightTire, wheelRoot01, "TB3_01_Model", "right_tire");
        tb3_02LeftTire = ResolveWheelReference(tb3_02LeftTire, wheelRoot02, "TB3_02_Model", "left_tire");
        tb3_02RightTire = ResolveWheelReference(tb3_02RightTire, wheelRoot02, "TB3_02_Model", "right_tire");
        tb3_03LeftTire = ResolveWheelReference(tb3_03LeftTire, wheelRoot03, "TB3_03_Model", "left_tire");
        tb3_03RightTire = ResolveWheelReference(tb3_03RightTire, wheelRoot03, "TB3_03_Model", "right_tire");

        InitializeWheelState("tb3-01", tb3_01LeftTire, tb3_01RightTire);
        InitializeWheelState("tb3-02", tb3_02LeftTire, tb3_02RightTire);
        InitializeWheelState("tb3-03", tb3_03LeftTire, tb3_03RightTire);
    }

    private Transform ResolveWheelRobotRoot(Transform configuredRoot, string fixedRootName)
    {
        if (configuredRoot != null && configuredRoot.name == fixedRootName)
        {
            return configuredRoot;
        }

        Transform fixedRoot = targetRoot != null ? FindDescendant(targetRoot, fixedRootName) : null;
        return fixedRoot != null ? fixedRoot : configuredRoot;
    }

    private static Transform ResolveWheelReference(
        Transform configuredTire,
        Transform robotRoot,
        string modelName,
        string tireName)
    {
        if (robotRoot == null)
        {
            return null;
        }

        if (configuredTire != null &&
            configuredTire.name == tireName &&
            configuredTire.IsChildOf(robotRoot))
        {
            return configuredTire;
        }

        Transform exactPath = robotRoot.Find($"{modelName}/ModelRoot/turtlebot3_burger/{tireName}");
        if (exactPath != null && exactPath.name == tireName)
        {
            return exactPath;
        }

        Transform burgerRoot = FindUniqueDescendant(robotRoot, "turtlebot3_burger");
        Transform directChild = burgerRoot != null ? burgerRoot.Find(tireName) : null;
        return directChild != null && directChild.name == tireName ? directChild : null;
    }

    private void InitializeWheelState(string robotId, Transform leftTire, Transform rightTire)
    {
        if (wheelStatesByRobot.TryGetValue(robotId, out RobotWheelState existing) &&
            existing.LeftTire == leftTire &&
            existing.RightTire == rightTire)
        {
            return;
        }

        RobotWheelState state = new()
        {
            LeftTire = leftTire,
            RightTire = rightTire
        };

        if (leftTire != null)
        {
            state.InitialLeftLocalRotation = leftTire.localRotation;
            state.InitialLeftLocalScale = leftTire.localScale;
            state.HasLeftBaseline = true;
        }
        else
        {
            WarnWheelOnce($"{robotId}:left-missing", $"[Factory3DWheel] {robotId} left_tire was not found under its robot root.");
        }

        if (rightTire != null)
        {
            state.InitialRightLocalRotation = rightTire.localRotation;
            state.InitialRightLocalScale = rightTire.localScale;
            state.HasRightBaseline = true;
        }
        else
        {
            WarnWheelOnce($"{robotId}:right-missing", $"[Factory3DWheel] {robotId} right_tire was not found under its robot root.");
        }

        wheelStatesByRobot[robotId] = state;
        bool validConfiguration = ValidateWheelConfiguration(robotId);
        state.IsConfigurationValid = validConfiguration;
        if (state.HasLeftBaseline && state.HasRightBaseline && validConfiguration && wheelReadyLogsShown.Add(robotId))
        {
            Debug.Log(
                $"[Factory3DWheel] {robotId} wheel animation ready\n" +
                $"Forward={robotForwardLocalAxis.normalized}\n" +
                $"Left={GetHierarchyPath(leftTire)} Axis={leftWheelLocalRotationAxis.normalized}\n" +
                $"Right={GetHierarchyPath(rightTire)} Axis={rightWheelLocalRotationAxis.normalized}\n" +
                $"Radius={wheelRadiusMeters:0.###}m Separation={wheelSeparationMeters:0.###}m");
        }
    }

    private bool ValidateWheelConfiguration(string robotId)
    {
        bool valid = true;
        if (wheelRadiusMeters <= 0f)
        {
            WarnWheelOnce($"{robotId}:radius", $"[Factory3DWheel] {robotId} wheelRadiusMeters must be greater than zero.");
            valid = false;
        }

        if (wheelSeparationMeters <= 0f)
        {
            WarnWheelOnce($"{robotId}:separation", $"[Factory3DWheel] {robotId} wheelSeparationMeters must be greater than zero.");
            valid = false;
        }

        if (leftWheelLocalRotationAxis.sqrMagnitude <= 0.000001f ||
            rightWheelLocalRotationAxis.sqrMagnitude <= 0.000001f)
        {
            WarnWheelOnce($"{robotId}:axis", $"[Factory3DWheel] {robotId} wheel local rotation axis must not be zero.");
            valid = false;
        }

        if (robotForwardLocalAxis.sqrMagnitude <= 0.000001f)
        {
            WarnWheelOnce($"{robotId}:forward-axis", $"[Factory3DWheel] {robotId} robot forward local axis must not be zero.");
            valid = false;
        }

        return valid;
    }

    private void UpdateWheelAnimations()
    {
        if (uiManager == null || !uiManager.IsFactory3DViewActive)
        {
            return;
        }

        float deltaTime = Time.unscaledDeltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        UpdateRobotWheelAnimation("tb3-01", deltaTime);
        UpdateRobotWheelAnimation("tb3-02", deltaTime);
        UpdateRobotWheelAnimation("tb3-03", deltaTime);
    }

    private void StopAllWheelSpeedsImmediately()
    {
        StopWheelSpeedsImmediately("tb3-01");
        StopWheelSpeedsImmediately("tb3-02");
        StopWheelSpeedsImmediately("tb3-03");
    }

    private void StopWheelSpeedsImmediately(string robotId)
    {
        if (!wheelStatesByRobot.TryGetValue(robotId, out RobotWheelState state))
        {
            return;
        }

        state.CurrentLeftSpeedDegPerSecond = 0f;
        state.CurrentRightSpeedDegPerSecond = 0f;
    }

    private void UpdateRobotWheelAnimation(string robotId, float deltaTime)
    {
        if (!wheelStatesByRobot.TryGetValue(robotId, out RobotWheelState state) ||
            (!state.HasLeftBaseline && !state.HasRightBaseline))
        {
            return;
        }

        float targetLeftSpeed = 0f;
        float targetRightSpeed = 0f;
        RobotPoseRuntimeState poseState = GetPoseRuntimeState(robotId);
        bool hasMotionState = poseState != null && poseState.HasRenderedPose;
        float linearVelocity = hasMotionState ? poseState.CurrentLinearVisualSpeed : 0f;
        float angularVelocity = hasMotionState ? poseState.CurrentAngularVisualSpeed : 0f;
        string fsmState = statesByRobot.TryGetValue(robotId, out RobotVisualState visualState)
            ? visualState.Status
            : string.Empty;
        bool hasFiniteVelocity =
            IsFinite(linearVelocity) && IsFinite(angularVelocity);
        bool isStale = !hasMotionState ||
                       poseState.LastPoseReceiveTime <= 0f ||
                       Time.unscaledTime - poseState.LastPoseReceiveTime > robotMotionStaleSeconds3D;
        bool mustStop = !uiManager.IsWebSocketConnected || isStale || IsWheelStopStatus(fsmState);

        if (state.IsConfigurationValid && hasMotionState && hasFiniteVelocity && !mustStop)
        {
            float halfSeparation = wheelSeparationMeters * 0.5f;
            float leftLinearVelocity = linearVelocity - angularVelocity * halfSeparation;
            float rightLinearVelocity = linearVelocity + angularVelocity * halfSeparation;
            float maximumSpeed = Mathf.Max(0f, maxVisualWheelSpeedDegPerSecond);
            targetLeftSpeed = Mathf.Clamp(
                leftLinearVelocity / wheelRadiusMeters * Mathf.Rad2Deg * leftWheelDirectionSign,
                -maximumSpeed,
                maximumSpeed);
            targetRightSpeed = Mathf.Clamp(
                rightLinearVelocity / wheelRadiusMeters * Mathf.Rad2Deg * rightWheelDirectionSign,
                -maximumSpeed,
                maximumSpeed);
        }
        else if (hasMotionState && !hasFiniteVelocity)
        {
            WarnWheelOnce($"{robotId}:invalid-velocity", $"[Factory3DWheel] {robotId} received a non-finite wheel velocity input.");
        }

        float acceleration = Mathf.Max(0f, wheelAccelerationDegPerSecondSquared);
        state.CurrentLeftSpeedDegPerSecond = acceleration > 0f
            ? Mathf.MoveTowards(state.CurrentLeftSpeedDegPerSecond, targetLeftSpeed, acceleration * deltaTime)
            : targetLeftSpeed;
        state.CurrentRightSpeedDegPerSecond = acceleration > 0f
            ? Mathf.MoveTowards(state.CurrentRightSpeedDegPerSecond, targetRightSpeed, acceleration * deltaTime)
            : targetRightSpeed;

        if (Mathf.Abs(targetLeftSpeed) <= WheelSpeedSettleEpsilon &&
            Mathf.Abs(targetRightSpeed) <= WheelSpeedSettleEpsilon &&
            Mathf.Abs(state.CurrentLeftSpeedDegPerSecond) <= WheelSpeedSettleEpsilon &&
            Mathf.Abs(state.CurrentRightSpeedDegPerSecond) <= WheelSpeedSettleEpsilon)
        {
            state.CurrentLeftSpeedDegPerSecond = 0f;
            state.CurrentRightSpeedDegPerSecond = 0f;
            return;
        }

        if (state.HasLeftBaseline && state.LeftTire != null)
        {
            state.AccumulatedLeftAngleDeg = WrapWheelAngle(
                state.AccumulatedLeftAngleDeg + state.CurrentLeftSpeedDegPerSecond * deltaTime);
            state.LeftTire.localRotation = state.InitialLeftLocalRotation * Quaternion.AngleAxis(
                state.AccumulatedLeftAngleDeg,
                leftWheelLocalRotationAxis.normalized);
        }

        if (state.HasRightBaseline && state.RightTire != null)
        {
            state.AccumulatedRightAngleDeg = WrapWheelAngle(
                state.AccumulatedRightAngleDeg + state.CurrentRightSpeedDegPerSecond * deltaTime);
            state.RightTire.localRotation = state.InitialRightLocalRotation * Quaternion.AngleAxis(
                state.AccumulatedRightAngleDeg,
                rightWheelLocalRotationAxis.normalized);
        }
    }

    private void RestoreAllWheelScales(bool warnOnUnexpectedChange)
    {
        foreach (KeyValuePair<string, RobotWheelState> item in wheelStatesByRobot)
        {
            RestoreWheelScale(item.Key, "left", item.Value.LeftTire, item.Value.HasLeftBaseline, item.Value.InitialLeftLocalScale, warnOnUnexpectedChange);
            RestoreWheelScale(item.Key, "right", item.Value.RightTire, item.Value.HasRightBaseline, item.Value.InitialRightLocalScale, warnOnUnexpectedChange);
        }
    }

    private void RestoreWheelScale(
        string robotId,
        string side,
        Transform tire,
        bool hasBaseline,
        Vector3 initialScale,
        bool warnOnUnexpectedChange)
    {
        if (tire == null || !hasBaseline || ApproximatelyEqual(tire.localScale, initialScale))
        {
            return;
        }

        Vector3 unexpectedScale = tire.localScale;
        tire.localScale = initialScale;
        if (warnOnUnexpectedChange && wheelWarningsShown.Add($"{robotId}:scale"))
        {
            Debug.LogWarning(
                $"[Factory3DWheel] Unexpected tire scale writer detected for {robotId} {side}\n" +
                $"Current={FormatScale(unexpectedScale)} Restored={FormatScale(initialScale)}");
        }
    }

    private void RestoreAllWheelTransforms()
    {
        foreach (RobotWheelState state in wheelStatesByRobot.Values)
        {
            if (state.HasLeftBaseline && state.LeftTire != null)
            {
                state.LeftTire.localRotation = state.InitialLeftLocalRotation;
                state.LeftTire.localScale = state.InitialLeftLocalScale;
            }

            if (state.HasRightBaseline && state.RightTire != null)
            {
                state.RightTire.localRotation = state.InitialRightLocalRotation;
                state.RightTire.localScale = state.InitialRightLocalScale;
            }
        }
    }

    private void WarnWheelOnce(string key, string message)
    {
        if (wheelWarningsShown.Add(key))
        {
            Debug.LogWarning(message);
        }
    }

    private static bool IsWheelStopStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return status.Contains("EMERGENCY", StringComparison.OrdinalIgnoreCase) ||
               IsDisconnectedStatus(status) ||
               string.Equals(status, "STALE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDisconnectedStatus(string status)
    {
        return string.Equals(status, "OFFLINE", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "DISCONNECTED", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "CONNECTION_LOST", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "COMM_LOST", StringComparison.OrdinalIgnoreCase);
    }

    private static float WrapWheelAngle(float angleDeg)
    {
        return Mathf.Repeat(angleDeg + 180f, 360f) - 180f;
    }

    private static string GetHierarchyPath(Transform item)
    {
        if (item == null) return "<missing>";

        string path = item.name;
        Transform parent = item.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }

    private static Transform FindUniqueDescendant(Transform root, string objectName)
    {
        Transform match = null;
        foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
        {
            if (item.name != objectName) continue;
            if (match != null) return null;
            match = item;
        }

        return match;
    }

    private void CaptureOriginalRobotScales()
    {
        if (tb3_01_root != null && !hasOriginalScaleTb3_01)
        {
            originalScaleTb3_01 = tb3_01_root.localScale;
            hasOriginalScaleTb3_01 = true;
        }

        if (tb3_02_root != null && !hasOriginalScaleTb3_02)
        {
            originalScaleTb3_02 = tb3_02_root.localScale;
            hasOriginalScaleTb3_02 = true;
        }

        if (tb3_03_root != null && !hasOriginalScaleTb3_03)
        {
            originalScaleTb3_03 = tb3_03_root.localScale;
            hasOriginalScaleTb3_03 = true;
        }
    }

    private void RestoreAllRobotRootScales(bool warnOnUnexpectedChange)
    {
        RestoreRobotRootScale(
            "tb3-01",
            tb3_01_root,
            hasOriginalScaleTb3_01,
            originalScaleTb3_01,
            warnOnUnexpectedChange);
        RestoreRobotRootScale(
            "tb3-02",
            tb3_02_root,
            hasOriginalScaleTb3_02,
            originalScaleTb3_02,
            warnOnUnexpectedChange);
        RestoreRobotRootScale(
            "tb3-03",
            tb3_03_root,
            hasOriginalScaleTb3_03,
            originalScaleTb3_03,
            warnOnUnexpectedChange);
    }

    private void RestoreRobotRootScale(
        string robotId,
        Transform robotRoot,
        bool hasOriginalScale,
        Vector3 originalScale,
        bool warnOnUnexpectedChange)
    {
        if (robotRoot == null || !hasOriginalScale ||
            ApproximatelyEqual(robotRoot.localScale, originalScale))
        {
            return;
        }

        Vector3 unexpectedScale = robotRoot.localScale;
        robotRoot.localScale = originalScale;
        if (warnOnUnexpectedChange && unexpectedScaleWarningsShown.Add(robotId))
        {
            Debug.LogWarning(
                "[Factory3DMarker] Unexpected scale writer detected\n" +
                $"Robot={robotId}\n" +
                $"Current={FormatScale(unexpectedScale)}\n" +
                $"Restored={FormatScale(originalScale)}");
        }
    }

    private bool ResolveReferences()
    {
        if (uiManager == null)
        {
#if UNITY_2023_1_OR_NEWER
            uiManager = FindFirstObjectByType<scr_ControlTowerUIManager>();
#else
            uiManager = FindObjectOfType<scr_ControlTowerUIManager>();
#endif
        }

        if (targetRoot == null) targetRoot = FindSceneObject("TurtleBot3_Group")?.transform;
        if (targetRoot != null)
        {
            tb3_01_root = ResolveRobotPlacementRoot(tb3_01_root, targetRoot, "TB3_01_Root_Fixed");
            tb3_02_root = ResolveRobotPlacementRoot(tb3_02_root, targetRoot, "TB3_02_Root_Fixed");
            tb3_03_root = ResolveRobotPlacementRoot(tb3_03_root, targetRoot, "TB3_03_Root_Fixed");
        }

        noHelmetEventMarker ??= FindSceneTransform("NO_HELMET_3D");
        fireEventMarker ??= FindSceneTransform("FIRE_3D");
        fallEventMarker ??= FindSceneTransform("FALL_3D");

        if ((tb3_01_root == null || tb3_02_root == null || tb3_03_root == null) && !missingModelWarningShown)
        {
            Debug.LogWarning("[Factory3DMap] TurtleBot3 model roots were not found. Assign TurtleBot3_Group and TB3 roots in the Inspector.");
            missingModelWarningShown = true;
        }

        return uiManager != null;
    }

    private static Transform ResolveRobotPlacementRoot(
        Transform configuredRoot,
        Transform robotsRoot,
        string fixedRootName)
    {
        if (configuredRoot != null && configuredRoot.name == fixedRootName)
        {
            return configuredRoot;
        }

        return robotsRoot != null ? FindDescendant(robotsRoot, fixedRootName) : null;
    }

    private Transform GetRobotRoot(string robotId)
    {
        return robotId switch
        {
            "tb3-01" => tb3_01_root,
            "tb3-02" => tb3_02_root,
            "tb3-03" => tb3_03_root,
            _ => null
        };
    }

    private Transform GetIncidentMarker(string incidentType)
    {
        string normalized = NormalizeIncidentType(incidentType);
        return normalized switch
        {
            "NO_HELMET" => noHelmetEventMarker,
            "FIRE" => fireEventMarker,
            "FALL" => fallEventMarker,
            _ => null
        };
    }

    private static string NormalizeIncidentType(string incidentType)
    {
        string normalized = string.IsNullOrWhiteSpace(incidentType) ? string.Empty : incidentType.Trim().ToUpperInvariant();
        return normalized switch
        {
            "EVENT_HELMET" => "NO_HELMET",
            "EVENT_FIRE" => "FIRE",
            "EVENT_FALL" => "FALL",
            _ => normalized
        };
    }

    private static bool IsAlertStatus(string status)
    {
        string value = status ?? string.Empty;
        return value.Contains("EMERGENCY", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("ALERT", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("STUCK", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ApproximatelyEqual(Vector3 current, Vector3 expected)
    {
        return Mathf.Approximately(current.x, expected.x) &&
               Mathf.Approximately(current.y, expected.y) &&
               Mathf.Approximately(current.z, expected.z);
    }

    private static string FormatScale(Vector3 scale)
    {
        return FormattableString.Invariant(
            $"({scale.x:0.####},{scale.y:0.####},{scale.z:0.####})");
    }

    private static float NormalizeAngleDeg(float angleDeg)
    {
        while (angleDeg > 180f) angleDeg -= 360f;
        while (angleDeg < -180f) angleDeg += 360f;
        return angleDeg;
    }

    private static float SafeDivide(float value, float divisor)
    {
        return Mathf.Abs(divisor) < 0.0001f ? 0f : value / divisor;
    }

    private static GameObject FindSceneObject(string objectName)
    {
        foreach (GameObject item in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (item.name == objectName && item.scene.IsValid()) return item;
        }

        return null;
    }

    private static Transform FindSceneTransform(string objectName)
    {
        GameObject item = FindSceneObject(objectName);
        return item != null ? item.transform : null;
    }

    private static Transform FindDescendant(Transform root, string objectName)
    {
        foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
        {
            if (item.name == objectName) return item;
        }

        return null;
    }
}

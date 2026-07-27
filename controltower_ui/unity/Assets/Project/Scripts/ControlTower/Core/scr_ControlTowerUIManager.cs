using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityEngine.Networking;

/// <summary>
/// 최종 ControlTower UI의 런타임 동작만 관리한다.
/// RectTransform, Anchor, SizeDelta, Color, FontSize, Alignment는 런타임에서 수정하지 않는다.
/// </summary>
public class scr_ControlTowerUIManager : MonoBehaviour
{
    [Header("Top Bar")]
    [SerializeField] private TMP_Text textDateTime;
    [SerializeField] private TMP_Text textTopStatus;

    [Header("Main Views")]
    [SerializeField] private GameObject panelMainDashboardView;
    [SerializeField] private GameObject panelMainFactoryView;
    [SerializeField] private GameObject panelMainRobotView;
    [SerializeField] private GameObject panelMainMapStatusView;
    [SerializeField] private GameObject panelMainCameraView;
    [SerializeField] private GameObject buttonBackToDashboardObject;
    [SerializeField] private scr_MapStatusRouteController mapStatusRouteController;

    [Header("Status Text")]
    [SerializeField] private TMP_Text textBodyRobotStatus;
    [SerializeField] private TMP_Text textBodyConnection;
    [SerializeField] private TMP_Text textBodyTodayEventList;
    [SerializeField] private TMP_Text textEventAlertBody;
    [SerializeField] private TMP_Text textBodyTodaySummary;
    [SerializeField] private TMP_Text textBodySystemStatus;
    [FormerlySerializedAs("eventLogBodyText")]
    [SerializeField] private TMP_Text textBodyEventLogScroll;
    [SerializeField, HideInInspector] private ScrollRect eventLogScrollRect;
    [SerializeField] private TMP_Text textRobotOverviewBody;
    [SerializeField] private TMP_Text textRobotTimelineBody;
    [SerializeField] private TMP_Text textCommandStateBody;
    [SerializeField] private TMP_Text textRobotAlertBody;
    [SerializeField] private GameObject previewTb3_01;
    [SerializeField] private GameObject previewTb3_02;
    [SerializeField] private GameObject previewTb3_03;
    [SerializeField, HideInInspector] private TMP_Text textRobotPreviewPlaceholder;
    [SerializeField] private float robotPreviewRotationSpeedDegrees = 15f;
    [SerializeField] private TMP_Text textSlamLocalizationBody;
    [SerializeField] private TMP_Text textNav2MissionBody;
    [SerializeField] private TMP_Text textWaypointRouteBody;
    [SerializeField] private TMP_Text textObstacleRecoveryBody;
    [SerializeField, HideInInspector] private TMP_Text textCameraViewDetail;
    [SerializeField, HideInInspector] private TMP_Text textMainCameraFeedSelected;
    [SerializeField] private TMP_Text textGlobalCctvBody;
    [SerializeField] private TMP_Text textTb3CameraBody;
    [SerializeField] private TMP_Text textAiDetectionBody;
    [SerializeField] private TMP_Text textCameraAiStatusBody;
    [SerializeField] private TMP_Text textDashboardFactoryOverviewBody;
    [SerializeField] private TMP_Text textDashboardRobotStatusBody;
    [SerializeField] private TMP_Text textDashboardMapNav2Body;
    [SerializeField] private TMP_Text textDashboardCameraAiBody;
    [SerializeField, HideInInspector] private TMP_Text textDashboardSystemHealthBody;
    [SerializeField, HideInInspector] private TMP_Text textDashboardRobotReadyCount;
    [SerializeField, HideInInspector] private TMP_Text textSystemHealthServerValue;
    [SerializeField, HideInInspector] private TMP_Text textSystemHealthWebSocketValue;
    [SerializeField, HideInInspector] private TMP_Text textSystemHealthRos2Value;
    [SerializeField, HideInInspector] private TMP_Text textSystemHealthAiModelValue;
    [SerializeField, HideInInspector] private TMP_Text textSystemHealthDbValue;
    [SerializeField, HideInInspector] private TMP_Text textSystemHealthHealthPercent;
    [SerializeField] private TMP_Text textDashboardRecentTimelineBody;
    [SerializeField] private Button buttonDashboardLogAll;
    [SerializeField] private Button buttonDashboardLogRobot;
    [SerializeField] private Button buttonDashboardLogControl;
    [SerializeField] private Button buttonDashboardLogCamera;
    [SerializeField] private Button buttonDashboardLogSystem;
    [SerializeField] private Button buttonDashboardLogError;
    [SerializeField] private TMP_Text textSelectedLogFilter;
    [SerializeField] private float dashboardRobotPreviewRotationSpeedDegrees = 15f;
    [SerializeField] private GameObject rawImageFactory3DMapPreview;
    [SerializeField] private GameObject imageMapAreaBackground;
    [SerializeField] private GameObject panelMini2DMap;
    [SerializeField] private GameObject panelFactory3DViewControls;
    [SerializeField] private GameObject panelFactory2DGlobalCamera;
    [SerializeField] private RawImage rawImageFactory2DGlobalCctv;
    [SerializeField] private TMP_Text textFactoryViewTitle;
    [SerializeField] private TMP_Text textMini2DMapTitle;
    [SerializeField] private Button buttonToggleFactoryMapMode;
    [SerializeField] private TMP_Text textToggleFactoryMapMode;
    [SerializeField] private scr_FactoryFull2DMapController full2DMapController;
    [SerializeField] private scr_FactoryMini2DMapController mini2DMapController;
    [SerializeField] private scr_Factory3DRobotMarkerController factory3DRobotMarkerController;
    [SerializeField] private scr_Factory2DPeopleMarkerController factory2DPeopleMarkerController;

    [Header("Runtime Markers")]
    [SerializeField] private GameObject imageEventMarker;
    [SerializeField] private GameObject textLabelEvent;
    [SerializeField] private GameObject popupLayerObject;
    [SerializeField] private GameObject popupAlertMessage;
    [SerializeField] private TMP_Text textPopupTitle;
    [SerializeField] private TMP_Text textPopupMessage;
    [SerializeField] private TMP_Text textPopupAlertBody;
    [SerializeField] private TMP_Text textPopupSnapshotBody;
    [SerializeField, HideInInspector] private TMP_Text textAlertPopupIndex;
    [SerializeField, HideInInspector] private TMP_Text textPopupPendingCount;
    [SerializeField, HideInInspector] private Button buttonAlertFilterPending;
    [SerializeField, HideInInspector] private Button buttonAlertFilterCleared;
    [SerializeField, HideInInspector] private Button buttonAlertList;
    [SerializeField, HideInInspector] private GameObject alertListRootObject;
    [SerializeField, HideInInspector] private GameObject panelPopupList;
    [SerializeField, HideInInspector] private Button buttonPopupList;
    [SerializeField, HideInInspector] private Button buttonPopupListClose;
    [SerializeField, HideInInspector] private Transform alertListContent;
    [SerializeField, HideInInspector] private Button buttonAlertListItemTemplate;
    [SerializeField, HideInInspector] private TMP_Text textPopupListMessage;
    [SerializeField, HideInInspector] private ScrollRect scrollRectAlertList;
    [SerializeField, HideInInspector] private Image popupSnapshotPlaceholderImage;
    [SerializeField, HideInInspector] private Image imageEventSnapshotPlaceholder;
    private RawImage popupSnapshotPlaceholderRawImage;
    private RawImage rawImageEventSnapshotPlaceholder;
    private TMP_Text textEventSnapshotPlaceholder;
    [SerializeField, HideInInspector] private TMP_Text textEventAlertPendingCount;
    [SerializeField, HideInInspector] private Button buttonEventAlertPrev;
    [SerializeField, HideInInspector] private TMP_Text textEventAlertIndex;
    [SerializeField, HideInInspector] private Button buttonEventAlertNext;
    [SerializeField, HideInInspector] private Button buttonEventAlertDetail;
    [SerializeField] private RectTransform markerTb3_01;
    [SerializeField] private RectTransform markerTb3_02;
    [SerializeField] private RectTransform markerTb3_03;
    [SerializeField] private RectTransform factoryFloorRect;

    [Header("View Buttons")]
    [SerializeField] private Button buttonFactoryView;
    [SerializeField] private Button buttonRobotView;
    [SerializeField] private Button buttonMapStatusView;
    [SerializeField] private Button buttonCameraView;

    [Header("Robot Select Buttons")]
    [SerializeField] private Button buttonSelectTb3_01;
    [SerializeField] private Button buttonSelectTb3_02;
    [SerializeField] private Button buttonSelectTb3_03;

    [Header("Manual Buttons")]
    [SerializeField] private Button buttonManualForward;
    [SerializeField] private Button buttonManualLeft;
    [SerializeField] private Button buttonManualStop;
    [SerializeField] private Button buttonManualRight;
    [SerializeField] private Button buttonManualBackward;

    [Header("Command Buttons")]
    [SerializeField] private Button buttonStartPatrol;
    [SerializeField, HideInInspector] private Button buttonPauseMission;
    [SerializeField] private Button buttonResumePlay;
    [SerializeField] private Button buttonManualControl;
    [SerializeField] private Button buttonManualExit;
    [SerializeField, HideInInspector] private Button buttonClearAlert;
    [SerializeField] private Button buttonReturnCharger;
    [SerializeField] private Button buttonReset;
    [SerializeField, HideInInspector] private Button buttonTestEvent;
    [SerializeField] private Button buttonEmergencyStop;
    [SerializeField] private Button buttonPopupConfirm;
    [SerializeField] private Button buttonPopupAck;
    [SerializeField] private Button buttonPopupClear;
    [SerializeField] private Button buttonPopupClose;
    [SerializeField] private Button buttonDashboardFactoryCard;
    [SerializeField] private Button buttonDashboardRobotCard;
    [SerializeField] private Button buttonDashboardMapCard;
    [SerializeField] private Button buttonDashboardCameraCard;
    [SerializeField] private Button buttonBackToDashboard;
    [SerializeField] private Button buttonMainFeedGlobalCctv;
    [SerializeField] private Button buttonMainFeedTb3_01;
    [SerializeField] private Button buttonMainFeedTb3_02;
    [SerializeField] private Button buttonMainFeedTb3_03;

    [Header("Demo Settings")]
    // Inspector에서 Max Log Lines를 8로 설정해야 함.
    [SerializeField] private int maxLogLines = 8;
    [SerializeField] private string operatorId = "OPERATOR_01";
    [SerializeField] private string selectedRobotId = "tb3-01";
    [SerializeField] private string mapId = "factory_1f";
    [SerializeField] private float mapMinX = -1.0f;
    [SerializeField] private float mapMaxX = 1.0f;
    [SerializeField] private float mapMinY = -1.0f;
    [SerializeField] private float mapMaxY = 1.0f;
    private string InspectorMapId => mapId;

    [Header("Robot Command V2")]
    public string robotCommandEndpoint = "/api/v1/monitor/robot-command";
    public string selectedManualTargetType = "NONE";
    public string selectedManualTargetId = "";

    [Header("Server Bridge")]
    [SerializeField] private scr_ControlTowerWebSocketClient webSocketClient;
    [SerializeField] private scr_ControlTowerCameraStreamManager cameraStreamManager;
    [SerializeField] private scr_ControlTowerRobotApiClient robotApiClient;
    [SerializeField] private scr_ControlTowerDashboardRuntimeBinder dashboardRuntimeBinder;
    [SerializeField] private scr_StaffEntranceBarrierController staffEntranceBarrierController;
    [SerializeField] private scr_Personnel3DMarkerController personnel3DMarkerController;
    [SerializeField] private scr_TB3ForkliftRuntimeController forkliftRuntimeController;
    private scr_TB3ForkliftPalletCarryController forkliftPalletCarryController;
    [SerializeField] private string dashboardServerBaseUrl = "http://127.0.0.1:8000";

    [Header("Right Control Optional UI")]
    [SerializeField] private TMP_Text textControlRobot;
    [SerializeField] private TMP_Text textForkliftHeight;
    [SerializeField] private Button buttonControlSelectTb3_01;
    [SerializeField] private Button buttonControlSelectTb3_02;
    [SerializeField] private Button buttonControlSelectTb3_03;
    [SerializeField] private Button buttonForkliftLiftUp;
    [SerializeField] private Button buttonForkliftLiftDown;

    [Header("Personnel Status UI")]
    [SerializeField] private TMP_Text textTopAttendanceInCount;
    [SerializeField] private TMP_Text textTopAttendanceOutCount;
    [SerializeField] private TMP_Text textTopVisitorTodayCount;
    private TMP_Text textTopNoHelmetCount;
    private TMP_Text textTopFallCount;
    private TMP_Text textTopFireCount;
    private TMP_Text textTopLowBatteryCount;
    private TMP_Text textTopPatrolCount;
    private TMP_Text textTopCctvCount;
    [SerializeField] private TMP_Text textDashboardAttendanceInValue;
    [SerializeField] private TMP_Text textDashboardAttendanceOutValue;
    [SerializeField] private TMP_Text textDashboardVisitorTodayValue;
    [SerializeField] private TMP_Text textDashboardLastAccessEventValue;

    private const int MaxTodayEventLines = 4;
    private const int MaxDashboardTimelineLines = 5;
    private const int LowBatteryLogThreshold = 33;
    private const int BatteryLogDeltaThreshold = 5;

    private readonly List<string> eventLogLines = new();
    private readonly Dictionary<string, string> lastOperationalLogByKey = new();
    private readonly List<string> dashboardTimelineLines = new();
    private readonly List<string> dashboardTimelineLevels = new();
    private int currentAlertId;
    private string currentAlertType = "NONE";
    private int retryAttempt;
    private bool eventLogMissingWarningShown;
    private bool isWebSocketConnected;
    private bool hasSystemStatusFromServer;
    private bool hasCameraStatusFromStream;
    private bool hasCameraAiStatusFromServer;
    private string currentServerStatus = "Offline";
    private string currentWebSocketStatus = "Disconnected";
    private readonly Dictionary<int, RobotWsLogSnapshot> lastWsLogByRobot = new();
    private readonly Dictionary<string, RobotStateData> robotStatesById = new();
    private readonly Dictionary<string, ControlTowerMapNavStatusData> mapNavStatusByRobotId = new();
    private readonly Dictionary<string, ControlTowerWaypointRouteData> waypointRouteByRobotId = new();
    private readonly Dictionary<string, bool> mapStatusMovingByRobotId = new();
    private readonly HashSet<string> mapStatusRouteAwaitingByRobotId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ControlTowerObstacleRecoveryData> obstacleRecoveryByRobotId = new();
    private string lastSelectionMismatchLogKey = string.Empty;
    private int todayNoHelmetCount;
    private int todayFallCount;
    private int todayFireCount;
    private int summaryAttendanceCurrentIn;
    private int summaryAttendanceOut;
    private int summaryVisitorTotal;
    private int summaryNoHelmetCount;
    private int summaryFallCount;
    private int summaryFireCount;
    private string todaySummaryRequestState = "UNKNOWN";
    private bool hasSummaryAttendanceCurrentIn;
    private bool hasSummaryAttendanceOut;
    private bool hasSummaryVisitorTotal;
    private bool hasSummaryNoHelmetCount;
    private bool hasSummaryFallCount;
    private bool hasSummaryFireCount;
    private string lastAccessEvent = "-";
    private bool cameraTotalStatusWarningShown;
    private bool simultaneousPatrolWarningShown;
    private string cameraSourceGlobalStatus = "UNKNOWN";
    private string cameraSourceTb3_01Status = "UNKNOWN";
    private string cameraSourceTb3_02Status = "UNKNOWN";
    private string cameraAiStatusUpdatedAt = "--";
    private readonly Dictionary<string, CameraAiStreamWsData> cameraAiStreamsBySource = new(StringComparer.OrdinalIgnoreCase);
    private CameraAiModelWsData cameraAiModelStatus;
    private readonly HashSet<string> processedAccessEventKeys = new();
    private readonly Dictionary<string, string> employeeAttendanceStateById = new();
    private readonly Dictionary<string, string> visitorAttendanceStateById = new();
    private readonly Dictionary<int, ActiveAlertItem> activeAlertsByLogId = new();
    private readonly List<int> activeAlertLogIds = new();
    private readonly Dictionary<int, ActiveAlertItem> incidentHistoryByLogId = new();
    private readonly List<int> incidentHistoryLogIds = new();
    private readonly HashSet<int> todayIncidentEventLogIds = new();
    private readonly HashSet<int> acknowledgedAlertLogIds = new();
    private readonly Dictionary<int, Sprite> alertSnapshotSpritesByLogId = new();
    private readonly Dictionary<int, string> alertSnapshotPhotoUrlsByLogId = new();
    private readonly HashSet<int> loadingAlertSnapshotIds = new();
    private readonly HashSet<int> failedAlertSnapshotIds = new();
    private TMP_Text cameraAiStatusTemplateSource;
    private string cameraAiStatusEditModeTemplate = string.Empty;
    private int selectedAlertLogId;
    private int pendingRealtimeAlertPopupLogId;
    private bool pendingRealtimeAlertPopupRetryScheduled;
    private int currentCameraSnapshotLogId;
    private string currentAlertListFilter = "NEW";
    private bool suppressQueuePopupAutoOpen;

    private struct RobotWsLogSnapshot
    {
        public string Status;
        public int Battery;
        public string PauseReason;
    }

    private struct RobotStateData
    {
        public string FsmState;
        public string MissionState;
        public string Battery;
        public string Speed;
        public string PositionX;
        public string PositionY;
        public string Theta;
        public string Nav2Status;
        public string PauseReason;
        public float WorldX;
        public float WorldY;
        public float Heading;
        public float LinearVelocity;
        public float AngularVelocity;
        public int CurrentTargetWaypoint;
        public float LastPoseReceiveTime;
    }

    private class RobotTimelineViewEntry
    {
        public string Timestamp;
        public string State;
        public string PauseReason;
    }

    private struct RobotCommandViewState
    {
        public string Command;
        public string RobotId;
        public string Result;
        public string Message;
        public string ServerTimestamp;
    }

    private class ActiveAlertItem
    {
        public int LogId;
        public string IncidentType;
        public int RobotNumericId;
        public string RobotDisplay;
        public string DetectedBy;
        public string EmployeeId;
        public string LocationDisplay;
        public float LocationX;
        public float LocationY;
        public bool HasLocation;
        public string ConfidenceDisplay;
        public string Timestamp;
        public string Message;
        public string PhotoUrl;
        public string Status;
        public string CameraId;
        public string ClearedAt;
    }

    public event Action<string> SelectedRobotChanged;
    public event Action<string, float, float, float, string> RobotStateUpdated;
    public string SelectedRobotId => selectedRobotId;
    public bool IsWebSocketConnected => isWebSocketConnected;
    public bool IsFactory3DViewActive =>
        isFactory3DMapMode &&
        panelMainFactoryView != null &&
        panelMainFactoryView.activeInHierarchy;

    private string currentFsmState = "--";
    private string currentMissionState = "--";
    private string currentBattery = "-- %";
    private string currentSpeed = "-- m/s";
    private string currentPositionX = "--";
    private string currentPositionY = "--";
    private string currentTheta = "--";
    private string currentGoal = "--";
    private string currentWaypointIndex = "--";
    private string savedWaypointIndex = "--";
    private string currentRetryCount = "--";
    private string currentNav2Status = "--";
    private string currentCommStatus = "Offline";
    private string currentPauseReason = "--";
    private string lastServerEvent = "--";

    private string currentLocalization = "--";
    private string currentWaypointLoop = "--";
    private string currentPathState = "--";

    private string currentAiEvent = "--";
    private string currentSeverity = "--";
    private string currentCameraLocation = "-";
    private string currentConfidence = "-";
    private string currentObstacleSource = "-";
    private string currentServerVerdict = "--";
    private string currentPhotoUrl = "-";
    private string currentDetectionBox = "-";
    private string currentCameraStatus = "Disconnected";
    private string currentStreamType = "PiCam";
    private string currentLastFrame = "-";
    private string currentGlobalLastFrame = "--";
    private string currentSelectedTb3FrameRobotId = string.Empty;
    private string currentSelectedTb3LastFrame = "--";
    private string currentRotateState = "Fixed";
    private string currentGlobalCamStatus = "Disconnected";
    private string currentLastDetection = "--";
    private string currentDetectedRobot = "-";
    private string currentDetectedZone = "-";
    private string currentAiModelStatus = "--";
    private string currentRos2Status = "--";
    private string lastCommand = "--";
    private string lastCommandResult = "--";
    private string manualMode = "Off";
    private string lastAck = "--";
    private string lastRobotAlert = "--";
    private string lastAlertLevel = "--";
    private string lastAlertDetectedBy = "-";
    private string lastRecommendedAction = "--";
    private string currentEventAlertRobotDisplay = "--";
    private string currentEventAlertLocationDisplay = "--";
    private string currentEventAlertConfidenceDisplay = "--";
    private string currentEventAlertMessageDisplay = "--";
    private string currentPopupAlertType = "NONE";
    private string currentPopupLevel = "Normal";
    private string currentPopupRobotId = "-";
    private string currentPopupLocation = "-";
    private string currentPopupDetectedBy = "-";
    private string currentPopupConfidence = "-";
    private string currentPopupRecommendedAction = "None";
    private string currentPopupLastMessage = "None";
    private bool buttonPopupAckBound;
    private bool buttonPopupClearBound;
    private bool buttonPopupCloseBound;
    private bool buttonAlertListBound;
    private bool buttonAlertFilterPendingBound;
    private bool buttonAlertFilterClearedBound;
    private bool buttonPopupListCloseBound;
    private bool buttonPopupListBound;
    private bool buttonEventAlertPrevBound;
    private bool buttonEventAlertNextBound;
    private bool buttonEventAlertDetailBound;
    private bool alertListOpenedFromDetail;
    private bool buttonDashboardFactoryCardBound;
    private bool buttonDashboardRobotCardBound;
    private bool buttonDashboardMapCardBound;
    private bool buttonDashboardCameraCardBound;
    private bool buttonBackToDashboardBound;
    private bool buttonToggleFactoryMapModeBound;
    private bool buttonMainFeedGlobalCctvBound;
    private bool buttonMainFeedTb3_01Bound;
    private bool buttonMainFeedTb3_02Bound;
    private bool buttonMainFeedTb3_03Bound;
    private bool buttonControlSelectTb3_01Bound;
    private bool buttonControlSelectTb3_02Bound;
    private bool buttonControlSelectTb3_03Bound;
    private bool buttonForkliftLiftUpBound;
    private bool buttonForkliftLiftDownBound;
    private bool isLiftCommandPending;
    private bool cameraSnapshotBindingReportWritten;
    private bool isFactory3DMapMode = true;
    private string currentMainCameraFeedLabel = "TB3-01";
    private string selectedMainFeedRobotId = "tb3-01";
    private readonly HashSet<GameObject> manualTeleopTriggerBoundObjects = new();
    private readonly HashSet<string> mainFeedMissingWarnings = new();
    private readonly HashSet<string> dashboardLogFilterMissingWarnings = new();
    private readonly List<string> serverPatrolTimelineLines = new();
    private readonly Dictionary<string, List<RobotTimelineViewEntry>> robotTimelineEntriesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> robotPatrolLogStatusById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RobotCommandViewState> robotCommandStateById = new(StringComparer.OrdinalIgnoreCase);
    private DashboardLogFilter selectedDashboardLogFilter = DashboardLogFilter.All;
    private Coroutine manualTeleopHoldCoroutine;
    private Coroutine todaySummaryReloadCoroutine;
    private bool todaySummaryReloadInProgress;
    private bool todaySummaryReloadRequestedWhileBusy;
    private string activeManualTeleopCommand = string.Empty;
    private bool isManualTeleopHolding;
    private bool commonStatusDirty;
    private bool topSummaryDirty;
    private bool robotViewDirty;
    private bool mapStatusDirty;
    private bool cameraViewDirty;
    private bool dashboardDirty;
    private bool factoryStatusDirty;
    private bool previewCamerasResolved;
    private bool dashboardReferencesResolved;
    private bool dashboardRobotSummaryReferencesResolved;
    private bool dashboardSystemHealthValueReferencesResolved;
    private TMP_Text dashboardMapNav2TemplateSource;
    private string dashboardMapNav2EditModeTemplate;
    private Camera factory3DPreviewCamera;
    private Camera robotViewPreviewCamera;
    private Camera dashboardPreviewCamera01;
    private Camera dashboardPreviewCamera02;
    private Camera dashboardPreviewCamera03;
    private const float ManualTeleopRepeatIntervalSeconds = 0.15f;
    private const float TodaySummaryReloadDebounceSeconds = 0.75f;
    private const int MaxServerPatrolTimelineLines = 8;
    private const int DefaultDashboardWaypointTotal = 14;
    private const int MaxDashboardRecentLogLines = 5;
    private static readonly ProfilerMarker UiRefreshMarker = new("ControlTower.UI.Refresh");
    private static readonly ProfilerMarker DashboardRefreshMarker = new("ControlTower.Dashboard.Refresh");
    private static readonly ProfilerMarker AlertImageLoadMarker = new("ControlTower.Alert.ImageLoad");

    private enum DashboardLogFilter
    {
        All,
        Robot,
        Control,
        Camera,
        System,
        Error
    }

    private void Awake()
    {
        BindButtons();
        ResolveWebSocketClient();
        ResolveRobotApiClient();
        ResolveDashboardRuntimeBinder();
        ResolvePersonnelRuntimeReferences();
        ResolveForkliftRuntimeController();
    }

    private void Start()
    {
        EnsureEventLogTextBound();
        InitializeText();
        EnsureMainCameraFeedSelectionReferences();
        BindMainCameraFeedButtons();
        RefreshMainCameraFeedSelectedText();
        EnsureBottomCameraPreviewVisible();
        EnsureCameraPreviewStreamsConnected();
        EnsurePopupReferences();
        TryShowPendingRealtimeAlertPopup();
        EnsureCameraViewSnapshotReference();
        LogCameraSnapshotBindingsOnce();
        BindRightControlButtons();
        RefreshRightControlRobotText();
        RefreshForkliftHeightText();
        EnsureFactoryViewRuntimeReferences();
        ApplyFactoryViewKoreanLabels();
        RefreshFactoryChargingZoneStatus();
        RefreshFactoryIncidentMarkers();
        RefreshFactory2DPeopleMarkers();
        EnsureFactoryConveyorRuntimeControllers();
        ResolvePreviewCamerasOnce();
        ShowDashboardView();
        HidePopup();
        SetEventMarkerVisible(false);
        _ = LoadDashboardInitialDataAsync();
    }

    private void Update()
    {
        FlushPendingUiRefreshes();
        RotateActiveRobotPreview();
        RotateActiveDashboardRobotPreview();
    }

    private void QueueRobotStateUiRefresh(bool affectsSelectedRobot)
    {
        topSummaryDirty = true;
        dashboardDirty = true;
        factoryStatusDirty = true;
        if (!affectsSelectedRobot)
        {
            return;
        }

        commonStatusDirty = true;
        robotViewDirty = true;
        mapStatusDirty = true;
        cameraViewDirty = true;
    }

    private void FlushPendingUiRefreshes()
    {
        if (!commonStatusDirty && !topSummaryDirty && !robotViewDirty && !mapStatusDirty &&
            !cameraViewDirty && !dashboardDirty && !factoryStatusDirty)
        {
            return;
        }

        using (UiRefreshMarker.Auto())
        {
            if (commonStatusDirty)
            {
                commonStatusDirty = false;
                UpdateTopStatus(currentFsmState);
                UpdateRobotStatus(
                    currentFsmState,
                    currentMissionState,
                    currentBattery,
                    currentSpeed,
                    currentPositionX,
                    currentPositionY,
                    currentTheta,
                    currentGoal,
                    currentWaypointIndex,
                    currentNav2Status,
                    currentCommStatus);
            }

            if (topSummaryDirty)
            {
                topSummaryDirty = false;
                RefreshTopSummaryCardTexts();
            }

            if (robotViewDirty)
            {
                robotViewDirty = false;
                if (IsViewActive(panelMainRobotView))
                {
                    RefreshRobotViewPanel();
                }
            }

            if (mapStatusDirty)
            {
                mapStatusDirty = false;
                if (IsViewActive(panelMainMapStatusView))
                {
                    RefreshMapStatusViewPanel();
                }
            }

            if (cameraViewDirty)
            {
                cameraViewDirty = false;
                if (IsViewActive(panelMainCameraView))
                {
                    RefreshCameraViewPanel();
                }
            }

            if (dashboardDirty)
            {
                dashboardDirty = false;
                if (IsViewActive(panelMainDashboardView))
                {
                    RefreshDashboardViewPanel();
                }
            }

            if (factoryStatusDirty)
            {
                factoryStatusDirty = false;
                if (IsViewActive(panelMainFactoryView))
                {
                    RefreshFactoryChargingZoneStatus();
                }
            }
        }
    }

    private static bool IsViewActive(GameObject view)
    {
        return view != null && view.activeInHierarchy;
    }

    private void OnDestroy()
    {
        UnbindDashboardLogFilterButtons();
        ReleaseAlertSnapshotCache();
    }

    private void BindButtons()
    {
        EnsureLeftRobotSelectReferences();
        EnsureManualAndCommandButtonReferences();

        if (buttonFactoryView != null) buttonFactoryView.onClick.AddListener(OnClickFactoryView);
        if (buttonRobotView != null) buttonRobotView.onClick.AddListener(OnClickRobotView);
        if (buttonMapStatusView != null) buttonMapStatusView.onClick.AddListener(OnClickMapStatusView);
        if (buttonCameraView != null) buttonCameraView.onClick.AddListener(OnClickCameraView);

        if (buttonSelectTb3_01 != null) buttonSelectTb3_01.onClick.AddListener(() => SelectRobot("tb3-01"));
        if (buttonSelectTb3_02 != null) buttonSelectTb3_02.onClick.AddListener(() => SelectRobot("tb3-02"));
        if (buttonSelectTb3_03 != null) buttonSelectTb3_03.onClick.AddListener(() => SelectRobot("tb3-03"));

        BindManualTeleopHoldButton(buttonManualForward, "MANUAL_FORWARD", 0.15f, 0f);
        BindManualTeleopHoldButton(buttonManualLeft, "MANUAL_LEFT", 0f, 0.5f);
        if (buttonManualStop != null) buttonManualStop.onClick.AddListener(() => SendManualCommand("MANUAL_STOP"));
        BindManualTeleopHoldButton(buttonManualRight, "MANUAL_RIGHT", 0f, -0.5f);
        BindManualTeleopHoldButton(buttonManualBackward, "MANUAL_BACKWARD", -0.15f, 0f);

        if (buttonManualControl != null && !HasPersistentListener(buttonManualControl, nameof(OnClickManualControl))) buttonManualControl.onClick.AddListener(OnClickManualControl);
        if (buttonManualExit != null && !HasPersistentListener(buttonManualExit, nameof(OnClickManualExit))) buttonManualExit.onClick.AddListener(OnClickManualExit);
        if (buttonClearAlert != null) buttonClearAlert.onClick.AddListener(OnClickClearAlert);
        if (buttonStartPatrol != null && !HasPersistentListener(buttonStartPatrol, nameof(OnClickStartPatrol))) buttonStartPatrol.onClick.AddListener(OnClickStartPatrol);
        if (buttonPauseMission != null && !HasPersistentListener(buttonPauseMission, nameof(OnClickPauseMission))) buttonPauseMission.onClick.AddListener(OnClickPauseMission);
        if (buttonResumePlay != null && !HasPersistentListener(buttonResumePlay, nameof(OnClickResumePlay))) buttonResumePlay.onClick.AddListener(OnClickResumePlay);
        if (buttonReturnCharger != null && !HasPersistentListener(buttonReturnCharger, nameof(OnClickReturnCharger))) buttonReturnCharger.onClick.AddListener(OnClickReturnCharger);
        if (buttonReset != null && !HasPersistentListener(buttonReset, nameof(OnClickReset))) buttonReset.onClick.AddListener(OnClickReset);
        if (buttonEmergencyStop != null && !HasPersistentListener(buttonEmergencyStop, nameof(OnClickEmergencyStop))) buttonEmergencyStop.onClick.AddListener(OnClickEmergencyStop);
        if (buttonPopupConfirm != null && !IsPopupListButton(buttonPopupConfirm)) buttonPopupConfirm.onClick.AddListener(OnClickPopupConfirm);
        BindPopupActionButtons();
        BindDashboardButtons();
        BindFactoryMapModeButton();
        BindMainCameraFeedButtons();
        BindRightControlButtons();
    }

    private static bool HasPersistentListener(Button button, string methodName)
    {
        if (button == null || string.IsNullOrWhiteSpace(methodName))
        {
            return false;
        }

        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
        {
            if (button.onClick.GetPersistentTarget(i) != null &&
                button.onClick.GetPersistentMethodName(i) == methodName)
            {
                return true;
            }
        }

        return false;
    }

    private void BindManualTeleopHoldButton(Button button, string command, float linearX, float angularZ)
    {
        if (button == null || button.gameObject == null || manualTeleopTriggerBoundObjects.Contains(button.gameObject))
        {
            return;
        }

        EventTrigger eventTrigger = button.gameObject.GetComponent<EventTrigger>();
        if (eventTrigger == null)
        {
            eventTrigger = button.gameObject.AddComponent<EventTrigger>();
        }

        AddManualTeleopEventTrigger(eventTrigger, EventTriggerType.PointerDown, _ => StartManualTeleopHold(command, linearX, angularZ));
        AddManualTeleopEventTrigger(eventTrigger, EventTriggerType.PointerUp, _ => StopManualTeleopHold());
        AddManualTeleopEventTrigger(eventTrigger, EventTriggerType.PointerExit, _ => StopManualTeleopHold());
        manualTeleopTriggerBoundObjects.Add(button.gameObject);
    }

    private static void AddManualTeleopEventTrigger(EventTrigger eventTrigger, EventTriggerType eventType, UnityEngine.Events.UnityAction<BaseEventData> callback)
    {
        EventTrigger.Entry entry = new EventTrigger.Entry
        {
            eventID = eventType
        };
        entry.callback.AddListener(callback);
        eventTrigger.triggers.Add(entry);
    }

    private void BindPopupActionButtons()
    {
        EnsureAlertQueueReferences();

        if (!buttonPopupAckBound && buttonPopupAck != null)
        {
            buttonPopupAck.onClick.AddListener(ConfirmCurrentAlert);
            buttonPopupAckBound = true;
        }

        if (!buttonPopupClearBound && buttonPopupClear != null)
        {
            buttonPopupClear.onClick.AddListener(ClearCurrentAlert);
            buttonPopupClearBound = true;
        }

        if (!buttonPopupCloseBound && buttonPopupClose != null)
        {
            buttonPopupClose.onClick.AddListener(HideAlertPopup);
            buttonPopupCloseBound = true;
        }

        if (!buttonAlertListBound && buttonAlertList != null)
        {
            buttonAlertList.onClick.AddListener(ShowAlertListPopup);
            buttonAlertListBound = true;
        }

        if (!buttonPopupListBound && buttonPopupList != null)
        {
            buttonPopupList.onClick.AddListener(OpenAlertListFromDetailPopup);
            buttonPopupListBound = true;
        }

        if (!buttonAlertFilterPendingBound && buttonAlertFilterPending != null)
        {
            buttonAlertFilterPending.onClick.AddListener(() => SetAlertListFilter("NEW"));
            buttonAlertFilterPendingBound = true;
        }

        if (!buttonAlertFilterClearedBound && buttonAlertFilterCleared != null)
        {
            buttonAlertFilterCleared.onClick.AddListener(() => SetAlertListFilter("CLEARED"));
            buttonAlertFilterClearedBound = true;
        }

        if (!buttonPopupListCloseBound && buttonPopupListClose != null)
        {
            buttonPopupListClose.onClick.AddListener(HideAlertListPopup);
            buttonPopupListCloseBound = true;
        }

        if (!buttonEventAlertPrevBound && buttonEventAlertPrev != null)
        {
            buttonEventAlertPrev.onClick.AddListener(SelectPreviousActiveAlert);
            buttonEventAlertPrevBound = true;
        }

        if (!buttonEventAlertNextBound && buttonEventAlertNext != null)
        {
            buttonEventAlertNext.onClick.AddListener(SelectNextActiveAlert);
            buttonEventAlertNextBound = true;
        }

        if (!buttonEventAlertDetailBound && buttonEventAlertDetail != null)
        {
            buttonEventAlertDetail.onClick.AddListener(ShowSelectedAlertDetail);
            buttonEventAlertDetailBound = true;
        }
    }

    private void BindDashboardButtons()
    {
        if (!buttonDashboardFactoryCardBound && buttonDashboardFactoryCard != null)
        {
            buttonDashboardFactoryCard.onClick.AddListener(ShowFactoryView);
            buttonDashboardFactoryCardBound = true;
        }

        if (!buttonDashboardRobotCardBound && buttonDashboardRobotCard != null)
        {
            buttonDashboardRobotCard.onClick.AddListener(ShowRobotView);
            buttonDashboardRobotCardBound = true;
        }

        if (!buttonDashboardMapCardBound && buttonDashboardMapCard != null)
        {
            buttonDashboardMapCard.onClick.AddListener(ShowMapStatusView);
            buttonDashboardMapCardBound = true;
        }

        if (!buttonDashboardCameraCardBound && buttonDashboardCameraCard != null)
        {
            buttonDashboardCameraCard.onClick.AddListener(ShowCameraView);
            buttonDashboardCameraCardBound = true;
        }

        if (!buttonBackToDashboardBound && buttonBackToDashboard != null)
        {
            buttonBackToDashboard.onClick.AddListener(ShowDashboardView);
            buttonBackToDashboardBound = true;
        }

        BindDashboardLogFilterButtons();
    }

    private void BindDashboardLogFilterButtons()
    {
        ResolveDashboardLogFilterButtons();
        WarnMissingDashboardLogFilterButton(buttonDashboardLogAll, "ALL");
        WarnMissingDashboardLogFilterButton(buttonDashboardLogRobot, "ROBOT");
        WarnMissingDashboardLogFilterButton(buttonDashboardLogControl, "CONTROL");
        WarnMissingDashboardLogFilterButton(buttonDashboardLogCamera, "CAMERA");
        WarnMissingDashboardLogFilterButton(buttonDashboardLogSystem, "SYSTEM");
        WarnMissingDashboardLogFilterButton(buttonDashboardLogError, "ERROR");
        BindDashboardLogFilterButton(buttonDashboardLogAll, SelectDashboardLogAll);
        BindDashboardLogFilterButton(buttonDashboardLogRobot, SelectDashboardLogRobot);
        BindDashboardLogFilterButton(buttonDashboardLogControl, SelectDashboardLogControl);
        BindDashboardLogFilterButton(buttonDashboardLogCamera, SelectDashboardLogCamera);
        BindDashboardLogFilterButton(buttonDashboardLogSystem, SelectDashboardLogSystem);
        BindDashboardLogFilterButton(buttonDashboardLogError, SelectDashboardLogError);
    }

    private void UnbindDashboardLogFilterButtons()
    {
        UnbindDashboardLogFilterButton(buttonDashboardLogAll, SelectDashboardLogAll);
        UnbindDashboardLogFilterButton(buttonDashboardLogRobot, SelectDashboardLogRobot);
        UnbindDashboardLogFilterButton(buttonDashboardLogControl, SelectDashboardLogControl);
        UnbindDashboardLogFilterButton(buttonDashboardLogCamera, SelectDashboardLogCamera);
        UnbindDashboardLogFilterButton(buttonDashboardLogSystem, SelectDashboardLogSystem);
        UnbindDashboardLogFilterButton(buttonDashboardLogError, SelectDashboardLogError);
    }

    private static void BindDashboardLogFilterButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        PrepareDashboardLogFilterButton(button);
        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private static void UnbindDashboardLogFilterButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null || action == null)
        {
            return;
        }

        button.onClick.RemoveListener(action);
    }

    private static void PrepareDashboardLogFilterButton(Button button)
    {
        button.interactable = true;

        Navigation navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;

        Graphic targetGraphic = button.targetGraphic;
        if (targetGraphic == null)
        {
            targetGraphic = button.GetComponent<Graphic>() ?? FindPreferredButtonGraphic(button);
            button.targetGraphic = targetGraphic;
        }

        if (targetGraphic != null)
        {
            targetGraphic.raycastTarget = true;
        }

        TMP_Text[] childTexts = button.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text childText in childTexts)
        {
            if (childText != null && childText != targetGraphic)
            {
                childText.raycastTarget = false;
            }
        }

        Graphic[] childGraphics = button.GetComponentsInChildren<Graphic>(true);
        foreach (Graphic childGraphic in childGraphics)
        {
            if (childGraphic == null || childGraphic == targetGraphic || childGraphic.gameObject == button.gameObject)
            {
                continue;
            }

            childGraphic.raycastTarget = false;
        }
    }

    private static Graphic FindPreferredButtonGraphic(Button button)
    {
        Image[] childImages = button.GetComponentsInChildren<Image>(true);
        foreach (Image childImage in childImages)
        {
            if (childImage != null &&
                (childImage.name.IndexOf("Background", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 childImage.name.IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return childImage;
            }
        }

        foreach (Image childImage in childImages)
        {
            if (childImage != null)
            {
                return childImage;
            }
        }

        Graphic[] childGraphics = button.GetComponentsInChildren<Graphic>(true);
        foreach (Graphic childGraphic in childGraphics)
        {
            if (childGraphic != null && childGraphic is not TMP_Text)
            {
                return childGraphic;
            }
        }

        return null;
    }

    public void SelectDashboardLogAll() => SelectDashboardLogCategory("ALL");
    public void SelectDashboardLogRobot() => SelectDashboardLogCategory("ROBOT");
    public void SelectDashboardLogControl() => SelectDashboardLogCategory("CONTROL");
    public void SelectDashboardLogCamera() => SelectDashboardLogCategory("CAMERA");
    public void SelectDashboardLogSystem() => SelectDashboardLogCategory("SYSTEM");
    public void SelectDashboardLogError() => SelectDashboardLogCategory("ERROR");

    private void SelectDashboardLogCategory(string category)
    {
        selectedDashboardLogFilter = NormalizeDashboardLogFilterCategory(category);
        RefreshDashboardTimelineText();
    }

    private void WarnMissingDashboardLogFilterButton(Button button, string category)
    {
        if (button != null || !dashboardLogFilterMissingWarnings.Add(category))
        {
            return;
        }

        Debug.LogWarning($"[DashboardLogFilter] Button component not found for {category}. Dashboard log text will still update with current filter.");
    }

    private void BindFactoryMapModeButton()
    {
        if (!buttonToggleFactoryMapModeBound && buttonToggleFactoryMapMode != null)
        {
            buttonToggleFactoryMapMode.onClick.AddListener(ToggleFactoryMapMode);
            buttonToggleFactoryMapModeBound = true;
        }
    }

    private void BindMainCameraFeedButtons()
    {
        EnsureMainCameraFeedSelectionReferences();

        if (!buttonMainFeedGlobalCctvBound && buttonMainFeedGlobalCctv != null)
        {
            buttonMainFeedGlobalCctv.onClick.AddListener(SelectMainFeedGlobalCctv);
            buttonMainFeedGlobalCctvBound = true;
        }

        if (!buttonMainFeedTb3_01Bound && buttonMainFeedTb3_01 != null)
        {
            buttonMainFeedTb3_01.onClick.AddListener(SelectMainFeedTb3_01);
            buttonMainFeedTb3_01Bound = true;
        }

        if (!buttonMainFeedTb3_02Bound && buttonMainFeedTb3_02 != null)
        {
            buttonMainFeedTb3_02.onClick.AddListener(SelectMainFeedTb3_02);
            buttonMainFeedTb3_02Bound = true;
        }

        if (!buttonMainFeedTb3_03Bound && buttonMainFeedTb3_03 != null)
        {
            buttonMainFeedTb3_03.onClick.AddListener(SelectMainFeedTb3_03);
            buttonMainFeedTb3_03Bound = true;
        }
    }

    private void BindRightControlButtons()
    {
        EnsureRightControlReferences();

        if (!buttonControlSelectTb3_01Bound && buttonControlSelectTb3_01 != null)
        {
            buttonControlSelectTb3_01.onClick.AddListener(SelectControlRobotTb3_01);
            buttonControlSelectTb3_01Bound = true;
        }

        if (!buttonControlSelectTb3_02Bound && buttonControlSelectTb3_02 != null)
        {
            buttonControlSelectTb3_02.onClick.AddListener(SelectControlRobotTb3_02);
            buttonControlSelectTb3_02Bound = true;
        }

        if (!buttonControlSelectTb3_03Bound && buttonControlSelectTb3_03 != null)
        {
            buttonControlSelectTb3_03.onClick.AddListener(SelectControlRobotTb3_03);
            buttonControlSelectTb3_03Bound = true;
        }

        if (!buttonForkliftLiftUpBound && buttonForkliftLiftUp != null)
        {
            if (!HasPersistentListener(buttonForkliftLiftUp, nameof(ForkliftLiftUp)))
            {
                buttonForkliftLiftUp.onClick.AddListener(ForkliftLiftUp);
            }
            buttonForkliftLiftUpBound = true;
        }

        if (!buttonForkliftLiftDownBound && buttonForkliftLiftDown != null)
        {
            if (!HasPersistentListener(buttonForkliftLiftDown, nameof(ForkliftLiftDown)))
            {
                buttonForkliftLiftDown.onClick.AddListener(ForkliftLiftDown);
            }
            buttonForkliftLiftDownBound = true;
        }

        RefreshForkliftInteractable();
    }

    private void InitializeText()
    {
        EnsureLeftSummaryAndSystemTextReferences();
        if (textDateTime != null) textDateTime.text = DateTime.Now.ToString("yyyy-MM-dd  HH:mm");
        UpdateConnectionStatus("Offline", "Disconnected", "Waiting");

        if (textBodyTodayEventList != null)
        {
            textBodyTodayEventList.text = "--";
        }

        if (textBodyTodaySummary != null)
        {
            RefreshTodaySummaryText();
        }

        RefreshLeftSystemStatusText();

        RefreshPersonnelStatusTexts();
        ApplySelectedRobotStateFromCache();
        SetEventAlert("--", "--", "--", "--", "--", "--");
        RefreshEventLogText();
    }

    [Serializable]
    private class DashboardTodaySummaryResponse
    {
        public bool ok = false;
        public DashboardTodaySummary today_summary = null;
    }

    [Serializable]
    private class DashboardTodaySummary
    {
        public DashboardAttendanceSummary attendance = null;
        public DashboardVisitorSummary visitor = null;
        public DashboardViolationSummary violation = null;
        public DashboardEmergencySummary emergency = null;
    }

    [Serializable]
    private class DashboardAttendanceSummary
    {
        public int current_in = 0;
        public int today_check_out = 0;
        public int check_out = 0;
        public int checked_out = 0;
        public int today_out = 0;

        public int GetCheckoutCount()
        {
            return Mathf.Max(today_check_out, check_out, checked_out, today_out);
        }
    }

    [Serializable]
    private class DashboardVisitorSummary
    {
        public int today_total = 0;
    }

    [Serializable]
    private class DashboardViolationSummary
    {
        public int NO_HELMET = 0;
    }

    [Serializable]
    private class DashboardEmergencySummary
    {
        public int FALL = 0;
        public int FIRE = 0;
    }

    [Serializable]
    private class IncidentRecordsResponse
    {
        public bool ok = false;
        public IncidentRecordItem[] records = null;
        public IncidentRecordData data = null;
    }

    [Serializable]
    private class IncidentRecordData
    {
        public IncidentRecordItem[] records = null;
        public IncidentRecordItem[] incidents = null;
        public IncidentRecordItem[] items = null;
    }

    [Serializable]
    private class IncidentRecordItem
    {
        public int log_id = 0;
        public int id = 0;
        public int alert_id = 0;
        public string detected_at = null;
        public string timestamp = null;
        public string incident_type = null;
        public int robot_id = 0;
        public string camera_id = null;
        public string detected_by = null;
        public string employee_id = null;
        public float location_x = 0f;
        public float location_y = 0f;
        public string photo_url = null;
        public float confidence = 0f;
        public string status = null;
        public string cleared_at = null;
        public IncidentAiDetails ai_details = null;
        public string message = null;
    }

    [Serializable]
    private class IncidentAiDetails
    {
        public float confidence = 0f;
    }

    private async Task LoadDashboardInitialDataAsync()
    {
        await LoadTodaySummaryAsync();
        await LoadDashboardRecordEndpointAsync("/api/v1/attendance/records?limit=100", "attendance records");
        await LoadDashboardRecordEndpointAsync("/api/v1/visitor-access/records?limit=100", "visitor records");
        await LoadDashboardRecordEndpointAsync("/api/v1/incidents/records?limit=100", "incident records");
    }

    private async Task LoadTodaySummaryAsync()
    {
        todaySummaryRequestState = "LOADING";
        RefreshDashboardRuntimeBinderState();

        DashboardApiResult result = await GetDashboardJsonAsync("/api/v1/dashboard/today-summary");
        if (!result.Success)
        {
            todaySummaryRequestState = "FAILED";
            RefreshDashboardRuntimeBinderState();
            AddEventLog("API", $"today-summary load warning: {result.Message}");
            return;
        }

        try
        {
            DashboardTodaySummaryResponse response = JsonUtility.FromJson<DashboardTodaySummaryResponse>(result.Body);
            if (response == null || !response.ok || response.today_summary == null)
            {
                todaySummaryRequestState = "FAILED";
                RefreshDashboardRuntimeBinderState();
                AddEventLog("API", "today-summary load warning: invalid response");
                return;
            }

            string body = result.Body ?? string.Empty;
            hasSummaryAttendanceCurrentIn = response.today_summary.attendance != null && HasJsonField(body, "current_in");
            hasSummaryAttendanceOut = response.today_summary.attendance != null &&
                                      (HasJsonField(body, "today_check_out") ||
                                       HasJsonField(body, "check_out") ||
                                       HasJsonField(body, "checked_out") ||
                                       HasJsonField(body, "today_out"));
            hasSummaryVisitorTotal = response.today_summary.visitor != null && HasJsonField(body, "today_total");
            hasSummaryNoHelmetCount = response.today_summary.violation != null && HasJsonField(body, "NO_HELMET");
            hasSummaryFallCount = response.today_summary.emergency != null && HasJsonField(body, "FALL");
            hasSummaryFireCount = response.today_summary.emergency != null && HasJsonField(body, "FIRE");

            summaryAttendanceCurrentIn = Mathf.Max(0, response.today_summary.attendance != null ? response.today_summary.attendance.current_in : 0);
            summaryAttendanceOut = Mathf.Max(0, response.today_summary.attendance != null ? response.today_summary.attendance.GetCheckoutCount() : 0);
            summaryVisitorTotal = Mathf.Max(0, response.today_summary.visitor != null ? response.today_summary.visitor.today_total : 0);
            summaryNoHelmetCount = Mathf.Max(0, response.today_summary.violation != null ? response.today_summary.violation.NO_HELMET : 0);
            summaryFallCount = Mathf.Max(0, response.today_summary.emergency != null ? response.today_summary.emergency.FALL : 0);
            summaryFireCount = Mathf.Max(0, response.today_summary.emergency != null ? response.today_summary.emergency.FIRE : 0);

            todaySummaryRequestState = "SUCCESS";
            RefreshTodaySummaryText();
            RefreshPersonnelStatusTexts();
            AddDashboardTimelineEvent("SYSTEM", "[SYSTEM] Today summary loaded");
            AddEventLog("API", "today-summary loaded");
        }
        catch (Exception exception)
        {
            todaySummaryRequestState = "FAILED";
            RefreshDashboardRuntimeBinderState();
            AddEventLog("API", $"today-summary parse warning: {exception.Message}");
        }
    }

    private void RequestTodaySummaryReloadFromServerAccessEvent()
    {
        if (todaySummaryReloadInProgress)
        {
            todaySummaryReloadRequestedWhileBusy = true;
            return;
        }

        if (todaySummaryReloadCoroutine != null)
        {
            return;
        }

        todaySummaryReloadCoroutine = StartCoroutine(ReloadTodaySummaryAfterAccessEventDebounce());
    }

    private IEnumerator ReloadTodaySummaryAfterAccessEventDebounce()
    {
        yield return new WaitForSecondsRealtime(TodaySummaryReloadDebounceSeconds);
        todaySummaryReloadCoroutine = null;
        todaySummaryReloadInProgress = true;
        Task summaryTask = LoadTodaySummaryAsync();
        while (!summaryTask.IsCompleted)
        {
            yield return null;
        }

        todaySummaryReloadInProgress = false;
        if (summaryTask.Exception != null)
        {
            Debug.LogWarning($"[PeopleStatus] today-summary reload failed after server access event: {summaryTask.Exception.GetBaseException().Message}");
        }

        if (todaySummaryReloadRequestedWhileBusy)
        {
            todaySummaryReloadRequestedWhileBusy = false;
            RequestTodaySummaryReloadFromServerAccessEvent();
        }
    }

    private async Task LoadDashboardRecordEndpointAsync(string endpoint, string label)
    {
        DashboardApiResult result = await GetDashboardJsonAsync(endpoint);
        if (!result.Success)
        {
            AddEventLog("API", $"{label} load warning: {result.Message}");
            return;
        }

        if (label.Equals("incident records", StringComparison.OrdinalIgnoreCase))
        {
            RestoreActiveAlertsFromIncidentRecords(result.Body);
        }

        AddEventLog("API", $"{label} loaded");
    }

    private async Task<DashboardApiResult> GetDashboardJsonAsync(string endpoint)
    {
        string baseUrl = string.IsNullOrWhiteSpace(dashboardServerBaseUrl)
            ? "http://127.0.0.1:8000"
            : dashboardServerBaseUrl.TrimEnd('/');
        string url = endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? endpoint
            : baseUrl + endpoint;

        try
        {
            using UnityWebRequest request = UnityWebRequest.Get(url);
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            bool success = request.result == UnityWebRequest.Result.Success && request.responseCode >= 200 && request.responseCode < 300;
            return new DashboardApiResult
            {
                Success = success,
                StatusCode = request.responseCode,
                Body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty,
                Message = success ? "HTTP OK" : $"{request.responseCode} {request.error}"
            };
        }
        catch (Exception exception)
        {
            return new DashboardApiResult
            {
                Success = false,
                StatusCode = 0,
                Body = string.Empty,
                Message = exception.Message
            };
        }
    }

    private struct DashboardApiResult
    {
        public bool Success;
        public long StatusCode;
        public string Body;
        public string Message;
    }

    private static bool HasJsonField(string json, string fieldName)
    {
        return !string.IsNullOrWhiteSpace(json) &&
               !string.IsNullOrWhiteSpace(fieldName) &&
               json.IndexOf($"\"{fieldName}\"", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void RefreshTodaySummaryText()
    {
        EnsureLeftSummaryAndSystemTextReferences();
        RefreshTopSummaryCardTexts();
        if (textBodyTodaySummary != null)
        {
            textBodyTodaySummary.text =
                $"출근자 : {FormatSummaryValuePlain(summaryAttendanceCurrentIn, hasSummaryAttendanceCurrentIn)}\n" +
                $"퇴근자 : {FormatSummaryValuePlain(summaryAttendanceOut, hasSummaryAttendanceOut)}\n" +
                $"방문자 : {FormatSummaryValuePlain(summaryVisitorTotal, hasSummaryVisitorTotal)}\n" +
                $"안전모 미착용 : {FormatSummaryValuePlain(summaryNoHelmetCount, hasSummaryNoHelmetCount)}\n" +
                $"쓰러짐 감지 : {FormatSummaryValuePlain(summaryFallCount, hasSummaryFallCount)}\n" +
                $"화재 감지 : {FormatSummaryValuePlain(summaryFireCount, hasSummaryFireCount)}";
        }

        RefreshDashboardViewPanel();
    }

    private void RefreshPersonnelStatusTexts()
    {
        ResolvePersonnelRuntimeReferences();
        RefreshTopSummaryCardTexts();
        SetTextValueIfBound(textDashboardAttendanceInValue, $"출근자 : {FormatSummaryValuePlain(summaryAttendanceCurrentIn, hasSummaryAttendanceCurrentIn)}");
        SetTextValueIfBound(textDashboardAttendanceOutValue, $"퇴근자 : {FormatSummaryValuePlain(summaryAttendanceOut, hasSummaryAttendanceOut)}");
        SetTextValueIfBound(textDashboardVisitorTodayValue, $"방문자 : {FormatSummaryValuePlain(summaryVisitorTotal, hasSummaryVisitorTotal)}");
        SetTextValueIfBound(textDashboardLastAccessEventValue, $"최근 출입 : {FormatLastAccessForPeopleStatus()}");

        ResolveDashboardRuntimeBinder();
        dashboardRuntimeBinder?.SetPeopleStatus(
            hasSummaryAttendanceCurrentIn ? summaryAttendanceCurrentIn : -1,
            hasSummaryAttendanceOut ? summaryAttendanceOut : -1,
            hasSummaryVisitorTotal ? summaryVisitorTotal : -1,
            lastAccessEvent);
        Debug.Log($"[PeopleStatus] serverSummary in={FormatSummaryValuePlain(summaryAttendanceCurrentIn, hasSummaryAttendanceCurrentIn)} out={FormatSummaryValuePlain(summaryAttendanceOut, hasSummaryAttendanceOut)} visitors={FormatSummaryValuePlain(summaryVisitorTotal, hasSummaryVisitorTotal)}");
        RefreshDashboardViewPanel();
    }

    private void RefreshTopSummaryCardTexts()
    {
        ResolveTopSummaryCardReferences();

        SetTextValueIfBound(textTopNoHelmetCount, $"안전모 미착용 {FormatSummaryValuePlain(summaryNoHelmetCount, hasSummaryNoHelmetCount)}");
        SetTextValueIfBound(textTopFallCount, $"쓰러짐 감지 {FormatSummaryValuePlain(summaryFallCount, hasSummaryFallCount)}");
        SetTextValueIfBound(textTopFireCount, $"화재 감지 {FormatSummaryValuePlain(summaryFireCount, hasSummaryFireCount)}");
        SetTextValueIfBound(textTopLowBatteryCount, $"배터리 충전 {BuildChargingRobotCountText()}");
        SetTextValueIfBound(textTopPatrolCount, $"순찰 로봇 {BuildPatrollingRobotText()}");
        SetTextValueIfBound(textTopCctvCount, $"카메라 {BuildCameraSummaryText()}");
        SetTextValueIfBound(textTopAttendanceInCount, $"출근자 {FormatSummaryValuePlain(summaryAttendanceCurrentIn, hasSummaryAttendanceCurrentIn)}");
        SetTextValueIfBound(textTopAttendanceOutCount, $"퇴근자 {FormatSummaryValuePlain(summaryAttendanceOut, hasSummaryAttendanceOut)}");
        SetTextValueIfBound(textTopVisitorTodayCount, $"방문자 {FormatSummaryValuePlain(summaryVisitorTotal, hasSummaryVisitorTotal)}");
    }

    private static string FormatSummaryValue(int value, bool hasServerField)
    {
        return hasServerField ? Mathf.Max(0, value).ToString("00") : "--";
    }

    private static string FormatSummaryValuePlain(int value, bool hasServerField)
    {
        return hasServerField ? Mathf.Max(0, value).ToString() : "--";
    }

    private string BuildChargingRobotCountText()
    {
        if (!HasAnyPatrolRobotState())
        {
            return "--";
        }

        int chargingCount = 0;
        foreach (string robotId in GetPatrolTurtlebotIds())
        {
            if (robotStatesById.TryGetValue(robotId, out RobotStateData state) &&
                IsNormalizedState(state.FsmState, "CHARGING"))
            {
                chargingCount++;
            }
        }

        return chargingCount.ToString();
    }

    private string BuildPatrollingRobotText()
    {
        if (!HasAnyPatrolRobotState())
        {
            return "--";
        }

        List<string> patrollingRobotIds = new();
        foreach (string robotId in GetPatrolTurtlebotIds())
        {
            if (robotStatesById.TryGetValue(robotId, out RobotStateData state) &&
                IsNormalizedState(state.FsmState, "PATROLLING"))
            {
                patrollingRobotIds.Add(robotId.ToUpperInvariant());
            }
        }

        if (patrollingRobotIds.Count == 0)
        {
            return "0";
        }

        if (patrollingRobotIds.Count > 1)
        {
            if (!simultaneousPatrolWarningShown)
            {
                simultaneousPatrolWarningShown = true;
                Debug.LogWarning($"[EventSummary] Multiple patrol robots reported PATROLLING: {string.Join(", ", patrollingRobotIds)}");
            }

            return "--";
        }

        simultaneousPatrolWarningShown = false;
        return patrollingRobotIds[0];
    }

    private string BuildCameraSummaryText()
    {
        if (!AreAllCameraSummarySourcesKnown())
        {
            if (!cameraTotalStatusWarningShown)
            {
                cameraTotalStatusWarningShown = true;
                Debug.Log("[EventSummary] Camera summary waits for confirmed source states: Global CCTV, TB3-01, TB3-02.");
            }

            return "--";
        }

        int connectedCount = 0;
        connectedCount += IsCameraSummarySourceConnected(cameraSourceGlobalStatus) ? 1 : 0;
        connectedCount += IsCameraSummarySourceConnected(cameraSourceTb3_01Status) ? 1 : 0;
        connectedCount += IsCameraSummarySourceConnected(cameraSourceTb3_02Status) ? 1 : 0;
        return $"{connectedCount}/3";
    }

    public void ApplyCameraSourceStatus(string sourceId, string status)
    {
        string source = NormalizeCameraSummarySourceId(sourceId);
        if (string.IsNullOrEmpty(source))
        {
            return;
        }

        string normalizedStatus = NormalizeCameraSummaryStatus(status);
        if (normalizedStatus == "UNKNOWN")
        {
            return;
        }

        string previousStatus;
        switch (source)
        {
            case "global":
                previousStatus = cameraSourceGlobalStatus;
                cameraSourceGlobalStatus = normalizedStatus;
                break;
            case "tb3-01":
                previousStatus = cameraSourceTb3_01Status;
                cameraSourceTb3_01Status = normalizedStatus;
                break;
            case "tb3-02":
                previousStatus = cameraSourceTb3_02Status;
                cameraSourceTb3_02Status = normalizedStatus;
                break;
            default:
                return;
        }

        if (string.Equals(previousStatus, normalizedStatus, StringComparison.Ordinal))
        {
            return;
        }

        topSummaryDirty = true;
        cameraViewDirty = true;
        dashboardDirty = true;
    }

    private bool AreAllCameraSummarySourcesKnown()
    {
        return IsCameraSummarySourceKnown(cameraSourceGlobalStatus) &&
               IsCameraSummarySourceKnown(cameraSourceTb3_01Status) &&
               IsCameraSummarySourceKnown(cameraSourceTb3_02Status);
    }

    private static bool IsCameraSummarySourceKnown(string status)
    {
        return IsCameraSummarySourceConnected(status) ||
               string.Equals(status, "CONNECTING", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "WAITING", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "VIDEO_WAITING", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, "DISCONNECTED", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCameraSummarySourceConnected(string status)
    {
        return string.Equals(status, "CONNECTED", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCameraSummarySourceId(string sourceId)
    {
        string normalized = string.IsNullOrWhiteSpace(sourceId) ? string.Empty : sourceId.Trim().ToLowerInvariant();
        return normalized switch
        {
            "global" => "global",
            "global cctv" => "global",
            "global_cctv" => "global",
            "tb3-1" => "tb3-01",
            "tb3_01" => "tb3-01",
            "tb3-01" => "tb3-01",
            "tb3-2" => "tb3-02",
            "tb3_02" => "tb3-02",
            "tb3-02" => "tb3-02",
            _ => string.Empty
        };
    }

    private static string NormalizeCameraSummaryStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "UNKNOWN";
        }

        string normalized = status.Trim().ToUpperInvariant();
        return normalized switch
        {
            "CONNECTED" => "CONNECTED",
            "STREAMING" => "CONNECTED",
            "WAITING" or "VIDEO WAITING" or "VIDEO_WAITING" => "VIDEO_WAITING",
            "CONNECTING" or "INITIALIZING" => "CONNECTING",
            "DISCONNECTED" => "DISCONNECTED",
            "NO STREAM" => "DISCONNECTED",
            "CLOSED" => "DISCONNECTED",
            "FAILED" => "DISCONNECTED",
            "ERROR" => "DISCONNECTED",
            _ => "UNKNOWN"
        };
    }

    public void ApplyCameraAiStatusFromServer(CameraAiStatusWsData status)
    {
        if (status == null)
        {
            return;
        }

        hasCameraAiStatusFromServer = true;
        cameraAiStatusUpdatedAt = NormalizeDashValue(status.updated_at);
        cameraAiStreamsBySource.Clear();

        if (status.streams != null)
        {
            foreach (CameraAiStreamWsData stream in status.streams)
            {
                string sourceKey = NormalizeCameraAiStreamSourceKey(stream);
                if (string.IsNullOrEmpty(sourceKey))
                {
                    continue;
                }

                cameraAiStreamsBySource[sourceKey] = stream;
            }
        }

        cameraAiModelStatus = status.ai;
        currentAiModelStatus = ResolveAiModelSummaryStatus(status.ai);
        topSummaryDirty = true;
        cameraViewDirty = true;
        dashboardDirty = true;
        RefreshBottomCameraPreviewPanel();
    }

    private static string NormalizeCameraAiStreamSourceKey(CameraAiStreamWsData stream)
    {
        if (stream == null)
        {
            return string.Empty;
        }

        string cameraId = (stream.camera_id ?? string.Empty).Trim().ToUpperInvariant();
        string channel = (stream.channel ?? string.Empty).Trim().ToUpperInvariant();
        string sourceType = (stream.source_type ?? string.Empty).Trim().ToUpperInvariant();

        if (cameraId.Contains("GLOBAL") || channel.Contains("GLOBAL") || sourceType.Contains("GLOBAL"))
        {
            return "global";
        }

        int robotNumber = stream.robot_id > 0 ? stream.robot_id : ExtractCameraRobotNumber(cameraId, channel);
        return robotNumber switch
        {
            1 => "tb3-01",
            2 => "tb3-02",
            3 => "tb3-03",
            _ => string.Empty
        };
    }

    private static int ExtractCameraRobotNumber(params string[] values)
    {
        if (values == null)
        {
            return 0;
        }

        foreach (string value in values)
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(normalized))
            {
                continue;
            }

            if (normalized.Contains("TB3-CAM-01") || normalized.Contains("TB3-01") || normalized.EndsWith("/1", StringComparison.Ordinal))
            {
                return 1;
            }

            if (normalized.Contains("TB3-CAM-02") || normalized.Contains("TB3-02") || normalized.EndsWith("/2", StringComparison.Ordinal))
            {
                return 2;
            }

            if (normalized.Contains("TB3-CAM-03") || normalized.Contains("TB3-03") || normalized.EndsWith("/3", StringComparison.Ordinal))
            {
                return 3;
            }

            string digits = string.Empty;
            for (int i = normalized.Length - 1; i >= 0; i--)
            {
                if (!char.IsDigit(normalized[i]))
                {
                    break;
                }

                digits = normalized[i] + digits;
            }

            if (!string.IsNullOrEmpty(digits) &&
                int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return parsed;
            }
        }

        return 0;
    }

    private static string ResolveCameraSummaryStatusFromServer(CameraAiStreamWsData stream)
    {
        if (stream == null)
        {
            return "UNKNOWN";
        }

        string streamStatus = (stream.stream_status ?? string.Empty).Trim().ToUpperInvariant();
        if (IsOneOf(streamStatus, "ERROR", "FAILED", "DISCONNECTED", "NO_STREAM", "NO STREAM", "CLOSED", "OFFLINE", "TIMEOUT"))
        {
            return "DISCONNECTED";
        }

        if (stream.has_connected && !stream.connected)
        {
            return "DISCONNECTED";
        }

        if (stream.has_connected && stream.connected &&
            string.Equals(streamStatus, "STREAMING", StringComparison.OrdinalIgnoreCase) &&
            stream.has_frame_received && stream.frame_received)
        {
            return "CONNECTED";
        }

        if (IsOneOf(streamStatus, "CONNECTING", "WAITING", "INITIALIZING") ||
            (stream.has_connected && stream.connected))
        {
            return "WAITING";
        }

        return "UNKNOWN";
    }

    private static string ResolveAiModelSummaryStatus(CameraAiModelWsData ai)
    {
        if (ai == null)
        {
            return "--";
        }

        string status = FirstNonEmptyMapStatusValue(ai.model_status, ai.inference_status).Trim().ToUpperInvariant();
        if (IsOneOf(status, "RUNNING", "READY", "ACTIVE", "OK"))
        {
            return "RUNNING";
        }

        if (IsOneOf(status, "LOADING", "INITIALIZING", "WAITING", "STARTING"))
        {
            return "WAITING";
        }

        if (IsOneOf(status, "ERROR", "FAILED", "OFFLINE", "DISCONNECTED"))
        {
            return "ERROR";
        }

        return "--";
    }

    private static bool IsOneOf(string value, params string[] candidates)
    {
        if (string.IsNullOrWhiteSpace(value) || candidates == null)
        {
            return false;
        }

        foreach (string candidate in candidates)
        {
            if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasAnyPatrolRobotState()
    {
        foreach (string robotId in GetPatrolTurtlebotIds())
        {
            if (robotStatesById.ContainsKey(robotId))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> GetPatrolTurtlebotIds()
    {
        yield return "tb3-01";
        yield return "tb3-02";
    }

    private static bool IsNormalizedState(string state, string expected)
    {
        return !string.IsNullOrWhiteSpace(state) &&
               state.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private string FormatLastAccessForPeopleStatus()
    {
        return string.IsNullOrWhiteSpace(lastAccessEvent) || lastAccessEvent == "-" ? "--" : lastAccessEvent;
    }

    public void OnClickFactoryView()
    {
        ShowFactoryView();
        AddEventLog("UI", "Factory View selected.");
    }

    public void OnClickRobotView()
    {
        ShowRobotView();
        AddEventLog("UI", "Robot View selected.");
    }

    public void OnClickMapStatusView()
    {
        ShowMapStatusView();
        AddEventLog("UI", "Map Status View selected.");
    }

    public void OnClickCameraView()
    {
        ShowCameraView();
        AddEventLog("UI", "Camera View selected.");
    }

    public void ShowDashboardView()
    {
        SetCameraViewStreamTargetsActive(false);
        EnsureBottomCameraPreviewVisible();
        EnsureDashboardReferences();
        ShowMainView(panelMainDashboardView != null ? panelMainDashboardView : panelMainFactoryView, panelMainDashboardView != null ? "DASHBOARD_VIEW" : "FACTORY_VIEW");
        RefreshDashboardViewPanel();
    }

    public void ShowFactoryView()
    {
        SetCameraViewStreamTargetsActive(false);
        EnsureBottomCameraPreviewVisible();
        EnsureFactoryViewRuntimeReferences();
        ApplyFactoryViewKoreanLabels();
        RefreshFactoryChargingZoneStatus();
        RefreshFactoryIncidentMarkers();
        RefreshFactory2DPeopleMarkers();
        EnsureFactoryConveyorRuntimeControllers();
        ShowMainView(panelMainFactoryView, "FACTORY_VIEW");
        ShowFactory3DMapMode();
    }

    public void ShowRobotView()
    {
        SetCameraViewStreamTargetsActive(false);
        EnsureBottomCameraPreviewVisible();
        ShowMainView(panelMainRobotView, "ROBOT_VIEW");
        UpdateRobotViewFromSelectedRobot();
    }

    public void ShowMapStatusView()
    {
        SetCameraViewStreamTargetsActive(false);
        EnsureBottomCameraPreviewVisible();
        ShowMainView(panelMainMapStatusView, "MAP_STATUS_VIEW");
        EnsureMapStatusRouteController();
        mapStatusRouteController?.CoordinateSource?.OnViewActivated();
        mapStatusRouteController?.OnViewActivated();
        UpdateMapStatusViewFromSelectedRobot();
    }

    public void ShowCameraView()
    {
        ShowMainView(panelMainCameraView, "CAMERA_VIEW");
        SyncMainCameraFeedWithSelectedRobot(false);
        UpdateCameraViewFromSelectedRobot();
        ResolveCameraStreamManager();
        if (cameraStreamManager != null)
        {
            cameraStreamManager.SetCameraViewActive(true);
            cameraStreamManager.SetSelectedRobot(selectedRobotId);
            cameraStreamManager.SetMainCameraFeedSelection(selectedMainFeedRobotId);
            cameraStreamManager.ConnectCameraStreams();
        }
        EnsureBottomCameraPreviewVisible();
    }

    public void OnClickStartPatrol()
    {
        SendAutoRobotCommand("PATROL_START");
    }

    public void OnClickPauseMission()
    {
        LogNonManualCommandDisabled("PAUSE_MISSION");
    }

    public void OnClickResumePlay()
    {
        SendAutoRobotCommand("RESUME");
    }

    public void OnClickManualControl()
    {
        EnterManualMode();
    }

    public void OnClickManualExit()
    {
        ExitManualMode();
    }

    public void OnClickReturnCharger()
    {
        SendAutoRobotCommand("RETURN_TO_CHARGER");
    }

    public void OnClickEmergencyStop()
    {
        SendAutoRobotCommand("EMERGENCY_STOP");
    }

    public void OnClickReset()
    {
        SendAutoRobotCommand("RESET");
    }

    public void OnClickTestEvent()
    {
        LogMockOrTestPathDisabled(nameof(OnClickTestEvent));
    }

    private static void LogNonManualCommandDisabled(string command)
    {
        Debug.LogWarning($"[ControlTower] Non-manual command disabled in live UI: {command}. Wait for server ROBOT_STATUS.");
    }

    private static void LogMockOrTestPathDisabled(string pathName)
    {
        Debug.LogWarning($"[ControlTower] Mock/Test path disabled in live UI: {pathName}.");
    }

    private static void LogLegacyServerPathDisabled(string pathName)
    {
        Debug.LogWarning($"[ControlTower] Legacy server path disabled in live UI: {pathName}. Use ROBOT_STATUS instead.");
    }

    public void OnClickClearAlert()
    {
        if (currentAlertId > 0)
        {
            LogAlertAckJson(currentAlertId, "CLEAR", "Operator cleared alert");
            currentAlertId = 0;
        }

        SetEventAlert("None", "Normal", "None", "None", "None");
        SetEventMarkerVisible(false);
        SetCameraDetail("None", "Normal", "-", "-", "-", "-");
        HidePopup();
        AddEventLog("INFO", "Alert cleared.");
    }

    private void OnClickPopupConfirm()
    {
        ConfirmCurrentAlert();
    }

    private void SelectRobot(string robotId)
    {
        selectedRobotId = NormalizeRobotKey(robotId);
        SyncManualTargetWithSelectedRobot();
        lastSelectionMismatchLogKey = string.Empty;
        SelectedRobotChanged?.Invoke(selectedRobotId);
        LogSelectedRobotJson(selectedRobotId);
        ApplySelectedRobotStateFromCache();
        RefreshAllStatusTexts();
        UpdateRobotViewFromSelectedRobot();
        UpdateMapStatusViewFromSelectedRobot();
        UpdateCameraViewFromSelectedRobot();
        UpdateBottomCameraPreviewFromSelectedRobot();
        RefreshDashboardViewPanel();
        SyncMainCameraFeedWithSelectedRobot(IsCameraViewActive());
        ResolveCameraStreamManager();
        if (cameraStreamManager != null)
        {
            cameraStreamManager.SetSelectedRobot(selectedRobotId);
        }
        RefreshRightControlRobotText();
        RefreshForkliftInteractable();
        RefreshForkliftHeightText();
        AddEventLog("UI", $"Selected robot changed: {selectedRobotId}");
    }

    public void SelectControlRobotTb3_01()
    {
        SelectRobot("tb3-01");
        RefreshRightControlRobotText();
    }

    public void SelectControlRobotTb3_02()
    {
        SelectRobot("tb3-02");
        RefreshRightControlRobotText();
    }

    public void SelectControlRobotTb3_03()
    {
        SelectRobot("tb3-03");
        RefreshRightControlRobotText();
    }

    public void ForkliftLiftUp()
    {
        _ = SendForkliftLiftCommandAsync(1f, "LIFT_UP");
    }

    public void ForkliftLiftDown()
    {
        _ = SendForkliftLiftCommandAsync(-1f, "LIFT_DOWN");
    }

    public void ForkliftStop()
    {
        _ = SendForkliftLiftCommandAsync(0f, "LIFT_STOP");
    }

    private async Task<bool> SendForkliftLiftCommandAsync(float lift, string commandName)
    {
        if (!CanControlForklift(true))
        {
            return false;
        }

        isLiftCommandPending = true;
        RefreshForkliftInteractable();
        lastCommand = commandName;
        lastCommandResult = "Sending";
        lastAck = "--";
        SetRobotCommandViewState("tb3-03", commandName, "SENDING", "리프트 명령 처리 중", "--");
        RefreshRobotViewPanel();
        AddEventLog("CONTROL", lift > 0f ? "리프트 상승 명령 전송" : lift < 0f ? "리프트 하강 명령 전송" : "리프트 정지 명령 전송");

        ResolveRobotApiClient();
        if (robotApiClient == null)
        {
            CompleteForkliftCommandFailure(commandName, "리프트 통신 오류");
            isLiftCommandPending = false;
            RefreshForkliftInteractable();
            return false;
        }

        RobotApiResult result;
        try
        {
            result = await robotApiClient.SendLiftTeleopAsync("tb3-03", lift);
        }
        catch (Exception exception)
        {
            isLiftCommandPending = false;
            Debug.LogException(exception);
            CompleteForkliftCommandFailure(commandName, "리프트 통신 오류");
            RefreshForkliftInteractable();
            return false;
        }

        isLiftCommandPending = false;

        if (result.Success)
        {
            ResolveForkliftRuntimeController();
            if (lift > 0f)
            {
                if (forkliftRuntimeController != null)
                {
                    forkliftRuntimeController.LiftUp();
                    ResolveForkliftPalletCarryController();
                    if (forkliftPalletCarryController != null &&
                        forkliftPalletCarryController.HasPickupCandidate &&
                        !forkliftPalletCarryController.IsCarryingPallet)
                    {
                        forkliftPalletCarryController.TryPickupCurrentCandidate();
                    }
                }
            }
            else if (lift < 0f)
            {
                forkliftRuntimeController?.LiftDown();
            }
            else
            {
                forkliftRuntimeController?.StopLift();
            }

            string message = FormatForkliftResponseMessage(result.Message, true);
            lastCommandResult = "Accepted";
            lastAck = message;
            SetRobotCommandViewState("tb3-03", commandName, "ACCEPTED", message, "--");
            AddEventLog("CONTROL", message);
            RefreshForkliftHeightText();
            RefreshRobotViewPanel();
            RefreshForkliftInteractable();
            return true;
        }

        string failureMessage = result.Rejected
            ? FormatForkliftResponseMessage(result.Message, false)
            : "리프트 통신 오류";
        CompleteForkliftCommandFailure(commandName, failureMessage);
        RefreshForkliftInteractable();
        return false;
    }

    private static string FormatForkliftResponseMessage(string message, bool success)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return success ? "수동 조작 전송 완료" : "리프트 명령을 거부하였습니다.";
        }

        string trimmed = message.Trim();
        if (ContainsHangul(trimmed))
        {
            return trimmed;
        }

        return success ? "수동 조작 전송 완료" : "리프트 명령을 거부하였습니다.";
    }

    private void CompleteForkliftCommandFailure(string commandName, string message)
    {
        lastCommandResult = "Rejected";
        lastAck = message;
        SetRobotCommandViewState("tb3-03", commandName, "REJECTED", message, "--");
        RefreshRobotViewPanel();
        AddEventLog("ERROR", message);
    }

    private bool CanControlForklift(bool writeLog = false)
    {
        if (!string.Equals(selectedRobotId, "tb3-03", StringComparison.OrdinalIgnoreCase))
        {
            if (writeLog) AddEventLog("CONTROL", "TB3-03에서만 사용할 수 있습니다.");
            return false;
        }

        if (!isWebSocketConnected || !robotStatesById.TryGetValue("tb3-03", out RobotStateData state))
        {
            if (writeLog) AddEventLog("ERROR", "리프트 통신 오류");
            return false;
        }

        string fsmState = (state.FsmState ?? string.Empty).Trim().ToUpperInvariant();
        if (fsmState != "MANUAL_CONTROL" && fsmState != "MANUAL")
        {
            if (writeLog) AddEventLog("CONTROL", "수동 제어 모드에서만 사용할 수 있습니다.");
            return false;
        }

        if (isLiftCommandPending)
        {
            if (writeLog) AddEventLog("CONTROL", "리프트 명령 처리 중");
            return false;
        }

        return true;
    }

    private void SendManualCommand(string command)
    {
        switch (command)
        {
            case "MANUAL_FORWARD":
                SendManualRobotCommand(command, 0.15f, 0f, 300);
                break;
            case "MANUAL_BACKWARD":
                SendManualRobotCommand(command, -0.10f, 0f, 300);
                break;
            case "MANUAL_LEFT":
                SendManualRobotCommand(command, 0f, 0.6f, 300);
                break;
            case "MANUAL_RIGHT":
                SendManualRobotCommand(command, 0f, -0.6f, 300);
                break;
            default:
                SendManualRobotCommand("MANUAL_STOP", 0f, 0f, 0);
                break;
        }
        string shortCommand = GetShortManualCommand(command);
        AddEventLog("MANUAL", shortCommand);
    }

    private void StartManualTeleopHold(string command, float linearX, float angularZ)
    {
        SyncManualTargetWithSelectedRobot();
        StopManualTeleopHold(false);
        isManualTeleopHolding = true;
        activeManualTeleopCommand = command;
        AddEventLog("MANUAL", $"{GetShortManualCommand(command)} hold");
        manualTeleopHoldCoroutine = StartCoroutine(ManualTeleopHoldRoutine(command, linearX, angularZ));
    }

    private void StopManualTeleopHold(bool sendStopPacket = true)
    {
        if (manualTeleopHoldCoroutine != null)
        {
            StopCoroutine(manualTeleopHoldCoroutine);
            manualTeleopHoldCoroutine = null;
        }

        if (!isManualTeleopHolding)
        {
            return;
        }

        isManualTeleopHolding = false;
        activeManualTeleopCommand = string.Empty;
        if (sendStopPacket)
        {
            SyncManualTargetWithSelectedRobot();
            _ = SendRobotCommandV2Async("MANUAL", "MANUAL_STOP", 0f, 0f, 0, selectedManualTargetType, selectedManualTargetId, true);
            AddEventLog("MANUAL", "STOP release");
        }
    }

    private IEnumerator ManualTeleopHoldRoutine(string command, float linearX, float angularZ)
    {
        WaitForSeconds wait = new WaitForSeconds(ManualTeleopRepeatIntervalSeconds);
        bool firstPacket = true;
        while (isManualTeleopHolding && activeManualTeleopCommand == command)
        {
            SyncManualTargetWithSelectedRobot();
            _ = SendRobotCommandV2Async("MANUAL", command, linearX, angularZ, 0, selectedManualTargetType, selectedManualTargetId, firstPacket);
            firstPacket = false;
            yield return wait;
        }
    }

    private void ApplyManualCommandUiState(string command)
    {
        // Manual input must not synthesize robot state. ROBOT_STATUS is the source of truth.
    }

    public void ApplyRobotFsmState(string state)
    {
        string normalizedState = string.IsNullOrWhiteSpace(state) ? "IDLE" : state.Trim().ToUpperInvariant();
        currentFsmState = normalizedState;
        currentCommStatus = "Server Demo";

        switch (normalizedState)
        {
            case "IDLE":
                retryAttempt = 0;
                currentMissionState = "Waiting";
                currentBattery = "-- %";
                currentSpeed = "-- m/s";
                currentPositionX = "--";
                currentPositionY = "--";
                currentTheta = "--";
                currentGoal = "-";
                currentWaypointIndex = "0 / 0";
                savedWaypointIndex = "-";
                currentRetryCount = "0 / 2";
                currentNav2Status = "Waiting";
                currentLocalization = "AMCL Waiting";
                currentWaypointLoop = "0 / 0";
                currentPathState = "No active path";
                ResetAlertAndCameraDetails();
                break;

            case "LOCALIZING":
                currentMissionState = "Localization started";
                currentBattery = "87 %";
                currentSpeed = "0.00 m/s";
                currentGoal = "LOCALIZE";
                currentNav2Status = "Localizing";
                currentLocalization = "AMCL Initializing";
                currentPathState = "Waiting for stable pose";
                ResetAlertAndCameraDetails();
                break;

            case "PATROLLING":
                currentMissionState = "Waypoint Patrol";
                currentBattery = currentBattery == "-- %" ? "87 %" : currentBattery;
                currentSpeed = "0.15 m/s";
                currentPositionX = "1.20";
                currentPositionY = "0.40";
                currentTheta = "0.00";
                currentGoal = currentGoal == "CHARGER" ? "saved waypoint" : "WP_01";
                currentWaypointIndex = currentWaypointIndex == "0 / 0" ? "1 / 5" : currentWaypointIndex;
                savedWaypointIndex = savedWaypointIndex == "-" ? "1" : savedWaypointIndex;
                currentRetryCount = "0 / 2";
                currentNav2Status = "Moving";
                currentLocalization = "AMCL Stable";
                currentWaypointLoop = "1 / 5";
                currentPathState = "Active path";
                ResetAlertAndCameraDetails();
                break;

            case "ARRIVED":
                currentMissionState = "Waypoint arrived";
                currentSpeed = "0.00 m/s";
                currentNav2Status = "Arrived";
                currentPathState = "Waypoint reached";
                ResetAlertAndCameraDetails();
                break;

            case "RETRYING":
                retryAttempt = Mathf.Clamp(retryAttempt <= 0 ? 1 : retryAttempt, 1, 2);
                currentMissionState = "Goal retrying";
                currentSpeed = "0.05 m/s";
                currentRetryCount = $"{retryAttempt} / 2";
                currentNav2Status = "Retrying goal";
                currentPathState = "Goal retrying";
                SetEventAlert("RETRYING", "WARNING", "None", "Goal response timeout", "Goal retrying", "Monitor retry result");
                SetEventMarkerVisible(true);
                break;

            case "STUCK":
                currentMissionState = "Robot stuck";
                currentSpeed = "0.00 m/s";
                currentRetryCount = "2 / 2";
                currentNav2Status = "Stuck";
                currentPathState = "Goal retry failed";
                SetEventAlert("STUCK", "ALERT", "Possible blocked path", "None", "Retry failed", "Manual recovery or retry");
                SetCameraDetail("None", "ALERT", "-", "-", "-", "-", "Navigation", "STUCK");
                SetEventMarkerVisible(true);
                ShowAlertPopup("STUCK", "ALERT", GetRobotNumberFromSelectedRobotId(), currentCameraLocation, "Navigation", currentConfidence, "Manual recovery or retry", "Retry failed.");
                break;

            case "PAUSED":
            case "EVENT_PAUSED":
            case "WAITING_PLAY":
                currentMissionState = normalizedState == "WAITING_PLAY" ? "Waiting operator play" : "Mission Paused";
                currentSpeed = "0.00 m/s";
                currentNav2Status = "Paused";
                currentPathState = "Paused by event";
                SetEventAlert(normalizedState, "WARNING", "None", "Operator or event pause", "Mission paused", "Resume Play or manual recovery");
                SetEventMarkerVisible(true);
                break;

            case "RESUMING":
                currentMissionState = "Resume from saved waypoint";
                currentSpeed = "0.10 m/s";
                currentGoal = "saved waypoint";
                currentNav2Status = "Resuming";
                currentPathState = "Resume path requested";
                ResetAlertAndCameraDetails();
                break;

            case "MANUAL_CONTROL":
                currentMissionState = "Manual";
                currentSpeed = "0.00 m/s";
                currentGoal = "Manual";
                currentWaypointIndex = "-";
                currentNav2Status = "Manual";
                currentPathState = "Manual active";
                ResetAlertAndCameraDetails();
                break;

            case "MANUAL_PAUSED":
                currentMissionState = "Manual";
                currentSpeed = "0.00 m/s";
                currentGoal = "Manual";
                currentWaypointIndex = "-";
                currentNav2Status = "Manual";
                currentPathState = "Manual stop";
                SetEventAlert("MANUAL_PAUSED", "INFO", "None", "None", "Manual stop", "Resume or manual");
                SetEventMarkerVisible(true);
                break;

            case "LOW_BATTERY":
                currentMissionState = "Low battery";
                currentBattery = "29 %";
                currentSpeed = "0.00 m/s";
                currentGoal = "CHARGER";
                currentNav2Status = "Low battery";
                currentPathState = "Return to charger required";
                SetEventAlert("LOW_BATTERY", "WARNING", "None", "None", "Battery low: 29 %", "Return to charger");
                SetEventMarkerVisible(true);
                ShowAlertPopup("LOW_BATTERY", "WARNING", GetRobotNumberFromSelectedRobotId(), currentCameraLocation, "Battery", "-", "Return to charger", "Battery low: 29 %.");
                break;

            case "RETURNING_TO_CHARGER":
                currentMissionState = "Returning to charger";
                currentBattery = "29 %";
                currentSpeed = "0.10 m/s";
                currentGoal = "CHARGER";
                currentNav2Status = "Moving to charger";
                currentPathState = "Path to charger";
                SetEventAlert("LOW_BATTERY", "WARNING", "None", "None", "Robot returning to charger", "Monitor docking");
                SetEventMarkerVisible(true);
                break;

            case "CHARGING":
                currentMissionState = "Battery State: Charging";
                currentBattery = "Charging";
                currentSpeed = "0.00 m/s";
                currentGoal = "CHARGER";
                currentNav2Status = "Docked / Charging";
                currentPathState = "Docked at charger";
                SetEventAlert("CHARGING", "INFO", "None", "None", "Robot docked and charging", "Wait for charge complete");
                SetEventMarkerVisible(false);
                HidePopup();
                break;

            case "RESUMING_AFTER_CHARGE":
                currentMissionState = "Resume mission after charge";
                currentBattery = "100 %";
                currentSpeed = "0.10 m/s";
                currentGoal = "saved waypoint";
                currentNav2Status = "Resuming after charge";
                currentPathState = "Resume from saved waypoint";
                ResetAlertAndCameraDetails();
                break;

            case "EMERGENCY_STOP":
                currentMissionState = "Emergency Stop";
                currentSpeed = "0.00 m/s";
                currentNav2Status = "Stopped";
                currentPathState = "Emergency stopped";
                SetEventAlert("EMERGENCY_STOP", "ALERT", "None", "Manual emergency stop", "Emergency stop requested", "Reset after safety check");
                SetEventMarkerVisible(true);
                ShowAlertPopup("EMERGENCY_STOP", "ALERT", GetRobotNumberFromSelectedRobotId(), currentCameraLocation, "Operator", "-", "Reset after safety check", "Emergency Stop command has been triggered.");
                break;

            case "OBSTACLE_WAITING":
                currentMissionState = "Camera obstacle detected";
                currentSpeed = "0.00 m/s";
                currentNav2Status = "Waiting";
                currentPathState = "Path paused by obstacle";
                SetEventAlert("OBSTACLE_WAITING", "WARNING", "Low obstacle", "Low obstacle detected by camera", "OBSTACLE_LOW_OBJECT", "Wait for CLEAR verdict or manual recovery");
                SetCameraDetail("OBSTACLE_LOW_OBJECT", "WARNING", "Front camera", "0.86", "-", "x=180 y=220 w=120 h=90", "Camera", "WAITING");
                SetEventMarkerVisible(true);
                ShowPopup("OBSTACLE WAITING", "Low obstacle detected by camera.\nWaiting for CLEAR verdict or manual recovery.");
                break;

            default:
                currentMissionState = $"Unknown FSM state: {normalizedState}";
                currentNav2Status = "Unknown";
                SetEventAlert("UNKNOWN_STATE", "WARNING", "None", "None", normalizedState, "Check server FSM payload");
                SetEventMarkerVisible(true);
                break;
        }

        RefreshAllStatusTexts();
    }

    public void SimReceiveLocalizationStarted()
    {
        LogMockOrTestPathDisabled(nameof(SimReceiveLocalizationStarted));
    }

    public void SimReceiveLocalizationStable()
    {
        LogMockOrTestPathDisabled(nameof(SimReceiveLocalizationStable));
    }

    public void SimReceiveArrived()
    {
        LogMockOrTestPathDisabled(nameof(SimReceiveArrived));
    }

    public void SimReceiveGoalRetrying()
    {
        LogMockOrTestPathDisabled(nameof(SimReceiveGoalRetrying));
    }

    public void SimReceiveStuck()
    {
        LogMockOrTestPathDisabled(nameof(SimReceiveStuck));
    }

    public void SimReceiveObstacleWaiting()
    {
        LogMockOrTestPathDisabled(nameof(SimReceiveObstacleWaiting));
    }

    public void SimReceiveObstacleClear()
    {
        LogMockOrTestPathDisabled(nameof(SimReceiveObstacleClear));
    }

    public void SimReceiveLowBattery()
    {
        LogMockOrTestPathDisabled(nameof(SimReceiveLowBattery));
    }

    public void SimReceiveCharging()
    {
        LogMockOrTestPathDisabled(nameof(SimReceiveCharging));
    }

    public void SimReceiveChargeComplete()
    {
        LogMockOrTestPathDisabled(nameof(SimReceiveChargeComplete));
    }

    public void SimReceiveManualPaused()
    {
        LogMockOrTestPathDisabled(nameof(SimReceiveManualPaused));
    }

    public void SimReceiveEmergencyReset()
    {
        LogMockOrTestPathDisabled(nameof(SimReceiveEmergencyReset));
    }

    public void ApplyRobotStateFromServer(int robotId, float x, float y, string status, float battery, float linearVel, float angularVel, string pauseReason, bool hasHeading = false, float heading = 0f, int currentTargetWaypoint = 0)
    {
        string serverRobotId = ConvertRobotId(robotId);
        string serverFsmState = string.IsNullOrWhiteSpace(status) ? "UNKNOWN" : status.Trim().ToUpperInvariant();
        float cachedHeading = heading;
        if (!hasHeading && robotStatesById.TryGetValue(serverRobotId, out RobotStateData previousState))
        {
            cachedHeading = previousState.Heading;
        }

        robotStatesById[serverRobotId] = new RobotStateData
        {
            FsmState = serverFsmState,
            MissionState = "--",
            Battery = $"{battery:0}%",
            Speed = $"{linearVel:0.00} m/s",
            PositionX = x.ToString("0.00"),
            PositionY = y.ToString("0.00"),
            Theta = cachedHeading.ToString("0.00"),
            Nav2Status = "--",
            PauseReason = pauseReason,
            WorldX = x,
            WorldY = y,
            Heading = cachedHeading,
            LinearVelocity = linearVel,
            AngularVelocity = angularVel,
            CurrentTargetWaypoint = currentTargetWaypoint,
            LastPoseReceiveTime = Time.unscaledTime
        };

        currentCommStatus = "WebSocket";
        lastServerEvent = "robot_state_update";
        currentServerStatus = "Online";
        currentWebSocketStatus = "Connected";
        isWebSocketConnected = true;

        UpdateMapStatusMovementState(serverRobotId);

        RobotStateUpdated?.Invoke(serverRobotId, x, y, cachedHeading, serverFsmState);
        bool affectsSelectedRobot = string.Equals(
            serverRobotId,
            NormalizeRobotKey(selectedRobotId),
            StringComparison.OrdinalIgnoreCase);
        if (affectsSelectedRobot)
        {
            ApplySelectedRobotStateFromCache();
            RefreshForkliftInteractable();
        }

        LogSelectedRobotPreservedIfNeeded(serverRobotId);
        QueueRobotStateUiRefresh(affectsSelectedRobot);
        AddRobotStateChangeLogs(robotId, serverFsmState, battery, pauseReason);
    }

    public bool TryGetRobotState(string robotId, out float x, out float y, out float heading, out string fsmState)
    {
        if (robotStatesById.TryGetValue(robotId, out RobotStateData state))
        {
            x = state.WorldX;
            y = state.WorldY;
            heading = state.Heading;
            fsmState = state.FsmState;
            return true;
        }

        x = 0f;
        y = 0f;
        heading = 0f;
        fsmState = "WAITING";
        return false;
    }

    public bool TryGetRobotPoseReceiveTime(string robotId, out float receiveTime)
    {
        if (robotStatesById.TryGetValue(robotId, out RobotStateData state))
        {
            receiveTime = state.LastPoseReceiveTime;
            return true;
        }

        receiveTime = 0f;
        return false;
    }

    public bool TryGetRobotMotionState(
        string robotId,
        out float linearVelocity,
        out float angularVelocity,
        out string fsmState,
        out float receiveTime)
    {
        string robotKey = NormalizeRobotKey(robotId);
        if (robotStatesById.TryGetValue(robotKey, out RobotStateData state))
        {
            linearVelocity = state.LinearVelocity;
            angularVelocity = state.AngularVelocity;
            fsmState = state.FsmState;
            receiveTime = state.LastPoseReceiveTime;
            return true;
        }

        linearVelocity = 0f;
        angularVelocity = 0f;
        fsmState = "WAITING";
        receiveTime = 0f;
        return false;
    }

    public void ApplyMapNavStatusFromServer(ControlTowerMapNavStatusData status)
    {
        if (status == null)
        {
            return;
        }

        string robotKey = ConvertRobotId(status.robot_id);
        mapNavStatusByRobotId.TryGetValue(robotKey, out ControlTowerMapNavStatusData previousStatus);
        mapNavStatusByRobotId[robotKey] = MergeMapNavStatus(previousStatus, status);
        UpdateMapStatusMovementState(robotKey);
        if (string.Equals(robotKey, NormalizeRobotKey(selectedRobotId), StringComparison.OrdinalIgnoreCase))
        {
            mapStatusDirty = true;
            dashboardDirty = true;
        }
    }

    public void ApplyWaypointRouteFromServer(ControlTowerWaypointRouteData route)
    {
        if (route == null)
        {
            return;
        }

        string robotKey = ConvertRobotId(route.robot_id);
        waypointRouteByRobotId.TryGetValue(robotKey, out ControlTowerWaypointRouteData previousRoute);
        if (IsExplicitRouteClear(route))
        {
            waypointRouteByRobotId.Remove(robotKey);
            mapStatusRouteAwaitingByRobotId.Remove(robotKey);
        }
        else
        {
            ControlTowerWaypointRouteData mergedRoute = MergeWaypointRoute(previousRoute, route);
            if (HasRouteGeometry(mergedRoute))
            {
                waypointRouteByRobotId[robotKey] = mergedRoute;
                mapStatusRouteAwaitingByRobotId.Remove(robotKey);
            }
        }

        if (string.Equals(robotKey, NormalizeRobotKey(selectedRobotId), StringComparison.OrdinalIgnoreCase))
        {
            mapStatusDirty = true;
            dashboardDirty = true;
        }
    }

    private void UpdateMapStatusMovementState(string robotKey)
    {
        string key = NormalizeRobotKey(robotKey);
        bool wasMoving = mapStatusMovingByRobotId.TryGetValue(key, out bool previousMoving) && previousMoving;
        bool isMoving = IsRobotMovingForMapStatus(key);
        mapStatusMovingByRobotId[key] = isMoving;

        if (!wasMoving && isMoving)
        {
            if (!waypointRouteByRobotId.TryGetValue(key, out ControlTowerWaypointRouteData route) || !HasRouteGeometry(route))
            {
                mapStatusRouteAwaitingByRobotId.Add(key);
            }

            if (string.Equals(key, NormalizeRobotKey(selectedRobotId), StringComparison.OrdinalIgnoreCase))
            {
                mapStatusDirty = true;
                if (IsViewActive(panelMainMapStatusView))
                {
                    RefreshMapStatusViewPanel();
                }
            }
        }
    }

    private bool IsRobotMovingForMapStatus(string robotKey)
    {
        if (!robotStatesById.TryGetValue(robotKey, out RobotStateData robotState))
        {
            return false;
        }

        if (IsMapStatusMovingState(robotState.FsmState) || IsMapStatusMovingState(robotState.MissionState))
        {
            return true;
        }

        if (mapNavStatusByRobotId.TryGetValue(robotKey, out ControlTowerMapNavStatusData navStatus) && navStatus != null &&
            (IsMapStatusMovingState(navStatus.nav2_state) || IsMapStatusMovingState(navStatus.route_state)))
        {
            return true;
        }

        return Mathf.Abs(robotState.LinearVelocity) > 0.001f || Mathf.Abs(robotState.AngularVelocity) > 0.001f;
    }

    private static bool IsMapStatusMovingState(string state)
    {
        string normalized = (state ?? string.Empty).Trim().ToUpperInvariant();
        return normalized is "PATROLLING" or "MOVING" or "NAVIGATING" or "RUNNING" or "ACTIVE";
    }

    private static bool HasRouteGeometry(ControlTowerWaypointRouteData route)
    {
        return route?.waypoints != null && route.waypoints.Length > 0;
    }

    private static bool IsExplicitRouteClear(ControlTowerWaypointRouteData route)
    {
        if (route?.waypoints == null || route.waypoints.Length > 0)
        {
            return false;
        }

        string routeState = (route.route_state ?? string.Empty).Trim().ToUpperInvariant();
        string routeId = (route.route_id ?? string.Empty).Trim().ToUpperInvariant();
        return routeState is "CLEAR" or "CLEARED" or "RESET" or "CANCELLED" or "CANCELED" or "NONE" ||
               routeId is "CLEAR" or "CLEARED" or "RESET" or "NONE";
    }

    private static ControlTowerMapNavStatusData MergeMapNavStatus(
        ControlTowerMapNavStatusData previous,
        ControlTowerMapNavStatusData incoming)
    {
        if (previous == null)
        {
            return incoming;
        }

        return new ControlTowerMapNavStatusData
        {
            robot_id = incoming.robot_id,
            map_id = MergeMapStatusCacheValue(incoming.map_id, previous.map_id),
            localization_state = MergeMapStatusCacheValue(incoming.localization_state, previous.localization_state),
            amcl_state = MergeMapStatusCacheValue(incoming.amcl_state, previous.amcl_state),
            initial_pose_set = incoming.has_initial_pose_set ? incoming.initial_pose_set : previous.initial_pose_set,
            localization_quality = MergeMapStatusCacheValue(incoming.localization_quality, previous.localization_quality),
            scan_match_state = MergeMapStatusCacheValue(incoming.scan_match_state, previous.scan_match_state),
            nav2_state = MergeMapStatusCacheValue(incoming.nav2_state, previous.nav2_state),
            planner_state = MergeMapStatusCacheValue(incoming.planner_state, previous.planner_state),
            controller_state = MergeMapStatusCacheValue(incoming.controller_state, previous.controller_state),
            current_target_wp = incoming.has_current_target_wp ? incoming.current_target_wp : previous.current_target_wp,
            current_wp_index = incoming.has_current_wp_index ? incoming.current_wp_index : previous.current_wp_index,
            total_waypoints = incoming.has_total_waypoints ? incoming.total_waypoints : previous.total_waypoints,
            route_state = MergeMapStatusCacheValue(incoming.route_state, previous.route_state),
            goal_result = MergeMapStatusCacheValue(incoming.goal_result, previous.goal_result),
            replan_count = incoming.has_replan_count ? incoming.replan_count : previous.replan_count,
            updated_at = MergeMapStatusCacheValue(incoming.updated_at, previous.updated_at),
            has_initial_pose_set = incoming.has_initial_pose_set || previous.has_initial_pose_set,
            has_current_target_wp = incoming.has_current_target_wp || previous.has_current_target_wp,
            has_current_wp_index = incoming.has_current_wp_index || previous.has_current_wp_index,
            has_total_waypoints = incoming.has_total_waypoints || previous.has_total_waypoints,
            has_replan_count = incoming.has_replan_count || previous.has_replan_count
        };
    }

    private static ControlTowerWaypointRouteData MergeWaypointRoute(
        ControlTowerWaypointRouteData previous,
        ControlTowerWaypointRouteData incoming)
    {
        if (previous == null)
        {
            return incoming;
        }

        return new ControlTowerWaypointRouteData
        {
            robot_id = incoming.robot_id,
            route_id = MergeMapStatusCacheValue(incoming.route_id, previous.route_id),
            route_name = MergeMapStatusCacheValue(incoming.route_name, previous.route_name),
            current_wp_index = incoming.has_current_wp_index ? incoming.current_wp_index : previous.current_wp_index,
            total_waypoints = incoming.has_total_waypoints ? incoming.total_waypoints : previous.total_waypoints,
            route_state = MergeMapStatusCacheValue(incoming.route_state, previous.route_state),
            waypoints = HasRouteGeometry(incoming) ? incoming.waypoints : previous.waypoints,
            has_current_wp_index = incoming.has_current_wp_index || previous.has_current_wp_index,
            has_total_waypoints = incoming.has_total_waypoints || previous.has_total_waypoints
        };
    }

    private static string MergeMapStatusCacheValue(string incoming, string previous)
    {
        if (!string.IsNullOrWhiteSpace(incoming) && incoming.Trim() != "--")
        {
            return incoming.Trim();
        }

        return !string.IsNullOrWhiteSpace(previous) ? previous.Trim() : "--";
    }

    public void ApplyObstacleRecoveryFromServer(ControlTowerObstacleRecoveryData recovery)
    {
        if (recovery == null)
        {
            return;
        }

        string robotKey = ConvertRobotId(recovery.robot_id);
        obstacleRecoveryByRobotId[robotKey] = recovery;
        if (string.Equals(robotKey, NormalizeRobotKey(selectedRobotId), StringComparison.OrdinalIgnoreCase))
        {
            mapStatusDirty = true;
            dashboardDirty = true;
        }
    }

    private void ApplySelectedRobotStateFromCache()
    {
        if (robotStatesById.TryGetValue(selectedRobotId, out RobotStateData selectedState))
        {
            currentFsmState = selectedState.FsmState;
            currentMissionState = selectedState.MissionState;
            currentBattery = selectedState.Battery;
            currentSpeed = selectedState.Speed;
            currentPositionX = selectedState.PositionX;
            currentPositionY = selectedState.PositionY;
            currentTheta = selectedState.Theta;
            currentNav2Status = selectedState.Nav2Status;
            currentCommStatus = isWebSocketConnected ? "WebSocket" : currentCommStatus;
            currentPauseReason = selectedState.PauseReason;
            currentGoal = "--";
            currentWaypointIndex = FormatTargetWaypointForDisplay(selectedState.CurrentTargetWaypoint);
            savedWaypointIndex = selectedState.CurrentTargetWaypoint > 0 ? selectedState.CurrentTargetWaypoint.ToString() : "--";
            currentWaypointLoop = currentWaypointIndex;
            currentPathState = "--";
            currentRetryCount = "--";
            UpdateManualModeFromServerState(selectedState.FsmState);
            return;
        }

        currentFsmState = "--";
        currentMissionState = "--";
        currentBattery = "-- %";
        currentSpeed = "-- m/s";
        currentPositionX = "--";
        currentPositionY = "--";
        currentTheta = "--";
        currentNav2Status = "--";
        currentCommStatus = isWebSocketConnected ? "WebSocket" : "Offline";
        currentPauseReason = "--";
        currentGoal = "--";
        currentWaypointIndex = "--";
        savedWaypointIndex = "--";
        currentWaypointLoop = "--";
        currentPathState = "--";
        currentRetryCount = "--";
    }

    private static string FormatTargetWaypointForDisplay(int currentTargetWaypoint)
    {
        return currentTargetWaypoint > 0 ? currentTargetWaypoint.ToString() : "--";
    }

    private void UpdateManualModeFromServerState(string serverState)
    {
        string normalizedState = string.IsNullOrWhiteSpace(serverState) ? string.Empty : serverState.Trim().ToUpperInvariant();
        if (normalizedState == "MANUAL_CONTROL" || normalizedState == "MANUAL")
        {
            manualMode = "On";
        }
        else if (normalizedState == "MANUAL_PAUSED" || normalizedState == "MANUAL_STOP")
        {
            manualMode = "Paused";
        }
        else if (!string.IsNullOrEmpty(normalizedState))
        {
            manualMode = "Off";
        }
    }

    private void LogSelectedRobotPreservedIfNeeded(string serverRobotId)
    {
        if (serverRobotId == selectedRobotId)
        {
            lastSelectionMismatchLogKey = string.Empty;
            return;
        }

        string mismatchKey = $"{selectedRobotId}|{serverRobotId}";
        if (lastSelectionMismatchLogKey == mismatchKey)
        {
            return;
        }

        lastSelectionMismatchLogKey = mismatchKey;
        AddEventLog("UI", $"Keep selected robot {selectedRobotId} while server state received for {serverRobotId}");
    }

    public void SetWebSocketConnectionState(bool connected, string detail)
    {
        isWebSocketConnected = connected;
        currentServerStatus = connected ? "Online" : "Offline";
        currentWebSocketStatus = connected ? "Connected" : "Disconnected";
        currentCommStatus = connected ? "WebSocket" : "Offline";
        currentRos2Status = connected ? "Waiting" : "--";
        UpdateConnectionStatus(currentServerStatus, currentWebSocketStatus, "Waiting");
        RefreshLeftSystemStatusText();
        UpdateTopStatus(currentFsmState);
        UpdateDashboardFromSystemStatus();
        cameraViewDirty = true;
        RefreshForkliftInteractable();
        AddEventLog("WS", connected ? "Connected" : detail);
    }

    public void AddExternalEventLog(string level, string message)
    {
        AddEventLog(level, message);
    }

    public void RefreshRobotViewPanel()
    {
        if (panelMainRobotView != null && !panelMainRobotView.activeInHierarchy)
        {
            return;
        }

        EnsureRobotViewTextReferences();

        SetTextValueIfBound(textRobotOverviewBody, BuildRobotOverviewUnifiedText());
        SetTextValueIfBound(textRobotTimelineBody, BuildRobotTimelineText());
        SetTextValueIfBound(textCommandStateBody, BuildRobotCommandStateText());
        SetTextValueIfBound(textRobotAlertBody, BuildRobotAlertText());

        ApplySelectedRobotPreview();
    }

    /*
    private string BuildRobotViewDetailText()
    {
        bool hasState = TryGetSelectedRobotState(out RobotStateData state);
        string patrolStatus = GetSelectedRobotPatrolLogStatus();
        return
            $"로봇 ID : {FormatRobotIdUpper(selectedRobotId)}\n" +
            $"FSM 상태 : {FormatRobotStateField(hasState, state.FsmState)}\n" +
            $"시나리오 상태 : {patrolStatus}\n" +
            $"배터리 : {FormatRobotStateField(hasState, state.Battery)}\n" +
            $"선속도 : {FormatRobotStateField(hasState, state.Speed)}\n" +
            $"각속도 : {FormatRobotAngularVelocity(hasState, state)}\n" +
            $"위치 : X {FormatRobotStateField(hasState, state.PositionX)}\n" +
            $"위치 : Y {FormatRobotStateField(hasState, state.PositionY)}\n" +
            $"방향 Yaw : {FormatRobotStateField(hasState, state.Theta)}\n" +
            $"현재 목표 : {FormatRobotTargetWaypoint(hasState, state)}\n" +
            $"일시정지 사유 : {FormatRobotPauseReason(hasState, state)}\n" +
            "통신 상태 : --";
    }

    */
    private string BuildRobotOverviewText()
    {
        bool hasState = TryGetSelectedRobotState(out RobotStateData state);
        return
            $"선택 로봇 : {FormatRobotIdUpper(selectedRobotId)}\n" +
            $"현재 FSM : {FormatRobotStateField(hasState, state.FsmState)}\n" +
            $"배터리 : {FormatRobotStateField(hasState, state.Battery)}\n" +
            $"현재 목표 : {FormatRobotTargetWaypoint(hasState, state)}\n" +
            $"시나리오 상태 : {GetSelectedRobotPatrolLogStatus()}\n" +
            $"일시정지 사유 : {FormatRobotPauseReason(hasState, state)}";
    }

    private string BuildRobotOverviewUnifiedText()
    {
        bool hasState = TryGetSelectedRobotState(out RobotStateData state);
        string patrolStatus = GetSelectedRobotPatrolLogStatus();
        return
            $"선택 로봇 : {FormatRobotIdUpper(selectedRobotId)}\n" +
            $"FSM 상태 : {FormatRobotStateField(hasState, state.FsmState)}\n" +
            $"순찰 상태 : {patrolStatus}\n" +
            $"배터리 : {FormatRobotStateField(hasState, state.Battery)}\n" +
            $"선속도 : {FormatRobotStateField(hasState, state.Speed)}\n" +
            $"각속도 : {FormatRobotAngularVelocity(hasState, state)}\n" +
            $"위치 X : {FormatRobotStateField(hasState, state.PositionX)}\n" +
            $"위치 Y : {FormatRobotStateField(hasState, state.PositionY)}\n" +
            $"방향 Yaw : {FormatRobotStateField(hasState, state.Theta)}\n" +
            $"현재 목표 : {FormatRobotTargetWaypoint(hasState, state)}\n" +
            $"일시정지 사유 : {FormatRobotPauseReason(hasState, state)}\n" +
            "통신 상태 : --";
    }

    private string BuildRobotTimelineText()
    {
        string robotId = NormalizeRobotKey(selectedRobotId);
        if (!robotTimelineEntriesById.TryGetValue(robotId, out List<RobotTimelineViewEntry> entries) || entries.Count == 0)
        {
            return "상태 이력 없음";
        }

        List<RobotTimelineViewEntry> sorted = new(entries);
        sorted.Sort(CompareRobotTimelineEntriesDescending);
        int count = Mathf.Min(7, sorted.Count);
        List<string> lines = new();
        for (int i = 0; i < count; i++)
        {
            RobotTimelineViewEntry entry = sorted[i];
            string timeText = FormatServerTimestampTimeOnly(entry.Timestamp);
            string stateText = FormatServerStateForDisplay(entry.State);
            string reasonText = NormalizeDashValue(entry.PauseReason);
            lines.Add(reasonText == "--"
                ? $"{timeText} | {stateText}"
                : $"{timeText} | {stateText} | {reasonText}");
        }

        return string.Join("\n", lines);
    }

    private string BuildRobotCommandStateText()
    {
        _ = manualMode;
        string robotId = NormalizeRobotKey(selectedRobotId);
        if (!robotCommandStateById.TryGetValue(robotId, out RobotCommandViewState commandState))
        {
            return
                "최근 명령 : --\n" +
                "대상 로봇 : --\n" +
                "명령 결과 : --\n" +
                "응답 메시지 : --\n" +
                "처리 시각 : --";
        }

        return
            $"최근 명령 : {NormalizeDashValue(commandState.Command)}\n" +
            $"대상 로봇 : {FormatRobotIdUpper(commandState.RobotId)}\n" +
            $"명령 결과 : {FormatCommandResultForRobotView(commandState.Result)}\n" +
            $"응답 메시지 : {NormalizeDashValue(commandState.Message)}\n" +
            $"처리 시각 : {NormalizeDashValue(commandState.ServerTimestamp)}";
    }

    private string BuildRobotAlertText()
    {
        if (!TryGetLatestActiveAlertForRobot(selectedRobotId, out ActiveAlertItem item))
        {
            return "현재 로봇 경고 없음";
        }

        return
            $"이벤트 종류 : {FormatIncidentTypeForKoreanDisplay(item.IncidentType)}\n" +
            $"처리 상태 : {NormalizeDashValue(item.Status)}\n" +
            $"신뢰도 : {NormalizeDashValue(item.ConfidenceDisplay)}\n" +
            $"발생 위치 : {FormatRobotAlertLocation(item)}\n" +
            $"발생 시각 : {FormatUserFacingDateTime(item.Timestamp)}\n" +
            $"메시지 : {NormalizeDashValue(item.Message)}";
    }

    private bool TryGetSelectedRobotState(out RobotStateData state)
    {
        return robotStatesById.TryGetValue(NormalizeRobotKey(selectedRobotId), out state);
    }

    private static string NormalizeRobotKey(string robotId)
    {
        return string.IsNullOrWhiteSpace(robotId)
            ? "--"
            : robotId.Trim().ToLowerInvariant().Replace("_", "-");
    }

    private static string FormatRobotStateField(bool hasState, string value)
    {
        return hasState ? NormalizeDashValue(value) : "--";
    }

    private static string FormatRobotAngularVelocity(bool hasState, RobotStateData state)
    {
        return hasState ? state.AngularVelocity.ToString("0.00", CultureInfo.InvariantCulture) : "--";
    }

    private static string FormatRobotTargetWaypoint(bool hasState, RobotStateData state)
    {
        return hasState && state.CurrentTargetWaypoint > 0
            ? state.CurrentTargetWaypoint.ToString(CultureInfo.InvariantCulture)
            : "--";
    }

    private static string FormatRobotPauseReason(bool hasState, RobotStateData state)
    {
        return hasState ? NormalizeDashValue(state.PauseReason) : "--";
    }

    private string GetSelectedRobotPatrolLogStatus()
    {
        return robotPatrolLogStatusById.TryGetValue(NormalizeRobotKey(selectedRobotId), out string status)
            ? NormalizeDashValue(status)
            : "--";
    }

    private static string FormatServerTimestampTimeOnly(string timestamp)
    {
        if (string.IsNullOrWhiteSpace(timestamp))
        {
            return "--";
        }

        string trimmed = timestamp.Trim();
        return DateTime.TryParse(trimmed, out DateTime parsed) ? parsed.ToString("HH:mm:ss") : trimmed;
    }

    private static int CompareRobotTimelineEntriesDescending(RobotTimelineViewEntry left, RobotTimelineViewEntry right)
    {
        if (left == null && right == null) return 0;
        if (left == null) return 1;
        if (right == null) return -1;

        bool leftParsed = DateTime.TryParse(left.Timestamp, out DateTime leftTime);
        bool rightParsed = DateTime.TryParse(right.Timestamp, out DateTime rightTime);
        if (leftParsed && rightParsed)
        {
            return rightTime.CompareTo(leftTime);
        }

        return string.Compare(right.Timestamp, left.Timestamp, StringComparison.Ordinal);
    }

    private void AppendRobotTimelineEntry(string robotId, string timestamp, string state, string pauseReason)
    {
        string robotKey = NormalizeRobotKey(robotId);
        if (!robotTimelineEntriesById.TryGetValue(robotKey, out List<RobotTimelineViewEntry> entries))
        {
            entries = new List<RobotTimelineViewEntry>();
            robotTimelineEntriesById[robotKey] = entries;
        }

        entries.Add(new RobotTimelineViewEntry
        {
            Timestamp = timestamp,
            State = state,
            PauseReason = pauseReason
        });

        while (entries.Count > MaxServerPatrolTimelineLines * 3)
        {
            entries.RemoveAt(0);
        }

        if (string.Equals(robotKey, NormalizeRobotKey(selectedRobotId), StringComparison.OrdinalIgnoreCase))
        {
            RefreshRobotViewPanel();
            RefreshDashboardViewPanel();
        }
    }

    private void SetRobotCommandViewState(string robotId, string command, string result, string message, string serverTimestamp)
    {
        string robotKey = NormalizeRobotKey(robotId);
        robotCommandStateById[robotKey] = new RobotCommandViewState
        {
            Command = command,
            RobotId = robotKey,
            Result = result,
            Message = message,
            ServerTimestamp = serverTimestamp
        };

        if (string.Equals(robotKey, NormalizeRobotKey(selectedRobotId), StringComparison.OrdinalIgnoreCase))
        {
            RefreshRobotViewPanel();
        }
    }

    private static string FormatCommandResultForRobotView(string result)
    {
        string normalized = string.IsNullOrWhiteSpace(result) ? string.Empty : result.Trim().ToUpperInvariant();
        return normalized switch
        {
            "ACCEPTED" or "SUCCESS" or "SUCCEEDED" or "OK" or "SENT" => "수락",
            "REJECTED" => "거부",
            "FAILED" or "FAIL" or "ERROR" => "전송 실패",
            "SENDING" => "전송 중",
            _ => string.IsNullOrWhiteSpace(normalized) ? "--" : NormalizeDashValue(result)
        };
    }

    private bool TryGetLatestActiveAlertForRobot(string robotId, out ActiveAlertItem latest)
    {
        latest = null;
        int robotNumber = ParseRobotNumber(robotId);
        if (robotNumber <= 0)
        {
            return false;
        }

        foreach (int logId in activeAlertLogIds)
        {
            if (!activeAlertsByLogId.TryGetValue(logId, out ActiveAlertItem item) ||
                item == null ||
                item.RobotNumericId != robotNumber)
            {
                continue;
            }

            if (latest == null || CompareAlertItemsByTimestampAscending(latest, item) < 0)
            {
                latest = item;
            }
        }

        return latest != null;
    }

    private static int ParseRobotNumber(string robotId)
    {
        if (string.IsNullOrWhiteSpace(robotId))
        {
            return 0;
        }

        string normalized = robotId.Trim().ToLowerInvariant().Replace("_", "-");
        string[] parts = normalized.Split('-');
        string candidate = parts.Length > 1 ? parts[parts.Length - 1] : normalized;
        return int.TryParse(candidate, out int parsed) ? parsed : 0;
    }

    private static string FormatRobotAlertLocation(ActiveAlertItem item)
    {
        if (item == null)
        {
            return "--";
        }

        return NormalizeDashValue(item.LocationDisplay);
    }

    public void UpdateRobotViewFromSelectedRobot()
    {
        RefreshRobotViewPanel();
        UpdateRobot3DPreviewFromSelectedRobot();
    }

    public void RefreshRobot3DPreviewPanel()
    {
        ApplySelectedRobotPreview();
    }

    public void UpdateRobot3DPreviewFromSelectedRobot()
    {
        ApplySelectedRobotPreview();
    }

    public void SetRobot3DPreviewStatus(string selectedRobotId, string status)
    {
        ApplySelectedRobotPreview();
    }

    private void ApplySelectedRobotPreview()
    {
        ResolveRobotPreviewReferences();

        GameObject selectedPreview = GetSelectedRobotPreviewObject();
        SetPreviewObjectActive(previewTb3_01, selectedPreview == previewTb3_01);
        SetPreviewObjectActive(previewTb3_02, selectedPreview == previewTb3_02);
        SetPreviewObjectActive(previewTb3_03, selectedPreview == previewTb3_03);

        if (textRobotPreviewPlaceholder != null)
        {
            textRobotPreviewPlaceholder.gameObject.SetActive(selectedPreview == null);
        }
    }

    private void RotateActiveRobotPreview()
    {
        if (panelMainRobotView == null || !panelMainRobotView.activeInHierarchy)
        {
            return;
        }

        GameObject selectedPreview = GetSelectedRobotPreviewObject();
        if (selectedPreview == null || !selectedPreview.activeInHierarchy)
        {
            return;
        }

        selectedPreview.transform.Rotate(0f, robotPreviewRotationSpeedDegrees * Time.deltaTime, 0f, Space.Self);
    }

    private void RotateActiveDashboardRobotPreview()
    {
        ResolveDashboardRuntimeBinder();
        if (dashboardRuntimeBinder == null)
        {
            return;
        }

        if (panelMainDashboardView == null)
        {
            panelMainDashboardView = FindSceneGameObjectByName("Panel_Main_DashboardView");
        }

        bool dashboardActive = panelMainDashboardView != null && panelMainDashboardView.activeInHierarchy;
        dashboardRuntimeBinder.TickDashboardRobotPreview(
            selectedRobotId,
            dashboardActive,
            dashboardRobotPreviewRotationSpeedDegrees);
    }

    private void ResolveRobotPreviewReferences()
    {
        previewTb3_01 ??= FindSceneGameObjectByName("Preview_TB3_01");
        previewTb3_02 ??= FindSceneGameObjectByName("Preview_TB3_02");
        previewTb3_03 ??= FindSceneGameObjectByName("Preview_TB3_03");
        textRobotPreviewPlaceholder ??= FindRobotViewTextByName("Text_RobotPreviewPlaceholder");
    }

    private GameObject GetSelectedRobotPreviewObject()
    {
        string normalized = NormalizeRobotKey(selectedRobotId);
        return normalized switch
        {
            "tb3-01" => previewTb3_01,
            "tb3-02" => previewTb3_02,
            "tb3-03" => previewTb3_03,
            _ => null
        };
    }

    private static void SetPreviewObjectActive(GameObject previewObject, bool active)
    {
        if (previewObject != null && previewObject.activeSelf != active)
        {
            previewObject.SetActive(active);
        }
    }

    public void SetRobotViewVisible(bool visible)
    {
        if (panelMainRobotView != null)
        {
            panelMainRobotView.SetActive(visible);
        }
    }

    public void RefreshMapStatusViewPanel()
    {
        if (panelMainMapStatusView != null && !panelMainMapStatusView.activeInHierarchy)
        {
            return;
        }

        EnsureMapStatusViewTextReferences();
        EnsureMapStatusRouteController();

        string robotKey = NormalizeRobotKey(selectedRobotId);
        bool hasMapNav = mapNavStatusByRobotId.TryGetValue(robotKey, out ControlTowerMapNavStatusData mapNavStatus) && mapNavStatus != null;
        bool hasRoute = waypointRouteByRobotId.TryGetValue(robotKey, out ControlTowerWaypointRouteData waypointRoute) && waypointRoute != null;
        bool hasObstacle = obstacleRecoveryByRobotId.TryGetValue(robotKey, out ControlTowerObstacleRecoveryData obstacleRecovery) && obstacleRecovery != null;
        bool hasRobotState = robotStatesById.TryGetValue(robotKey, out RobotStateData robotState);

        SetTextValueIfBound(textSlamLocalizationBody, BuildSlamLocalizationText(hasMapNav, mapNavStatus));
        SetTextValueIfBound(textNav2MissionBody, BuildNav2MissionText(hasMapNav, mapNavStatus, hasRoute, waypointRoute, hasRobotState, robotState));
        SetTextValueIfBound(textWaypointRouteBody, BuildWaypointRouteText(hasRoute, waypointRoute));
        SetTextValueIfBound(textObstacleRecoveryBody, BuildObstacleRecoveryText(hasObstacle, obstacleRecovery));

        RefreshSelectedMapStatusRoute(hasRoute, waypointRoute);
    }

    public void UpdateMapStatusViewFromSelectedRobot()
    {
        EnsureSelectedRobotMapStatusRouteAvailability();
        RefreshMapStatusViewPanel();
    }

    private void EnsureSelectedRobotMapStatusRouteAvailability()
    {
        string robotKey = NormalizeRobotKey(selectedRobotId);
        bool isMoving = IsRobotMovingForMapStatus(robotKey);
        mapStatusMovingByRobotId[robotKey] = isMoving;
        if (isMoving && (!waypointRouteByRobotId.TryGetValue(robotKey, out ControlTowerWaypointRouteData route) || !HasRouteGeometry(route)))
        {
            mapStatusRouteAwaitingByRobotId.Add(robotKey);
        }
    }

    private string BuildSlamLocalizationText(bool hasMapNav, ControlTowerMapNavStatusData data)
    {
        if (!hasMapNav)
        {
            return "맵 ID : 정보 수신 대기\n" +
                   "Localization 상태 : 상태 수신 대기\n" +
                   "AMCL 상태 : 상태 수신 대기\n" +
                   "초기 위치 설정 : 확인 중\n" +
                   "위치 추정 품질 : 확인 중\n" +
                   "Scan Matching : 상태 수신 대기\n" +
                   "마지막 갱신 : 수신 대기";
        }

        return
            $"맵 ID : {FormatMapStatusValue(hasMapNav, data?.map_id)}\n" +
            $"Localization 상태 : {(hasMapNav ? LocalizeLocalizationState(data?.localization_state) : "--")}\n" +
            $"AMCL 상태 : {(hasMapNav ? LocalizeAmclState(data?.amcl_state) : "--")}\n" +
            $"초기 위치 설정 : {FormatInitialPoseSet(hasMapNav, data)}\n" +
            $"위치 추정 품질 : {FormatLocalizationQuality(hasMapNav, data)}\n" +
            $"Scan Matching : {(hasMapNav ? LocalizeScanMatchingState(data?.scan_match_state) : "--")}\n" +
            $"마지막 갱신 : {FormatUserFacingDateTime(hasMapNav ? data?.updated_at : null)}";
    }

    private string BuildNav2MissionText(
        bool hasMapNav,
        ControlTowerMapNavStatusData data,
        bool hasRoute,
        ControlTowerWaypointRouteData route,
        bool hasRobotState,
        RobotStateData robotState)
    {
        if (!hasMapNav)
        {
            return "Nav2 상태 : 상태 수신 대기\n" +
                   "Planner 상태 : 상태 수신 대기\n" +
                   "Controller 상태 : 상태 수신 대기\n" +
                   "현재 목표 : 대기\n" +
                   "현재 Waypoint : 대기\n" +
                   "전체 Waypoint : 경로 준비 중\n" +
                   "경로 상태 : 경로 준비 중\n" +
                   "목표 결과 : 결과 없음\n" +
                   "재계획 횟수 : 기록 없음";
        }

        return
            $"Nav2 상태 : {(hasMapNav ? LocalizeNav2State(data?.nav2_state) : "--")}\n" +
            $"Planner 상태 : {(hasMapNav ? LocalizePlannerControllerState(data?.planner_state) : "--")}\n" +
            $"Controller 상태 : {(hasMapNav ? LocalizePlannerControllerState(data?.controller_state) : "--")}\n" +
            $"현재 목표 : {FormatCurrentTargetWaypoint(hasMapNav, data, hasRobotState, robotState, hasRoute ? route : null)}\n" +
            $"현재 Waypoint : {FormatRouteCurrentWaypoint(hasRoute, route, hasMapNav, data)}\n" +
            $"전체 Waypoint : {FormatRouteTotalWaypoints(hasRoute, route, hasMapNav, data)}\n" +
            $"경로 상태 : {FormatRouteState(hasRoute, route, hasMapNav, data)}\n" +
            $"목표 결과 : {(hasMapNav ? LocalizeGoalResult(data?.goal_result) : "--")}\n" +
            $"재계획 횟수 : {FormatMapStatusInt(hasMapNav && data != null && data.has_replan_count, data != null ? data.replan_count : 0)}";
    }

    private string BuildWaypointRouteText(bool hasRoute, ControlTowerWaypointRouteData route)
    {
        if (!hasRoute || !HasRouteGeometry(route))
        {
            string robotKey = NormalizeRobotKey(selectedRobotId);
            bool isAwaitingRoute = mapStatusRouteAwaitingByRobotId.Contains(robotKey) || IsRobotMovingForMapStatus(robotKey);
            return isAwaitingRoute
                ? "WAYPOINT 경로\n활성 경로 확인 중"
                : "WAYPOINT 경로\n경로 준비 중";
        }

        if (hasRoute && route?.waypoints != null && route.waypoints.Length > 0)
        {
            return BuildCompactWaypointRouteText(route);
        }

        if (!hasRoute || route?.waypoints == null || route.waypoints.Length == 0)
        {
            return "경로 데이터 없음";
        }

        List<ControlTowerWaypointData> orderedWaypoints = GetOrderedWaypointData(route);
        if (orderedWaypoints.Count == 0)
        {
            return "경로 데이터 없음";
        }

        int completedCount = 0;
        foreach (ControlTowerWaypointData waypoint in orderedWaypoints)
        {
            if (waypoint != null && string.Equals(waypoint.status?.Trim(), "COMPLETED", StringComparison.OrdinalIgnoreCase))
            {
                completedCount++;
            }
        }

        int totalCount = route.has_total_waypoints && route.total_waypoints > 0 ? route.total_waypoints : orderedWaypoints.Count;
        int currentIndex = FindCurrentRouteWaypointIndex(orderedWaypoints, route);
        int nextIndex = currentIndex >= 0 && currentIndex + 1 < orderedWaypoints.Count ? currentIndex + 1 : -1;
        List<string> lines = new()
        {
            $"경로 ID : {FormatMapStatusValue(true, route.route_id)}",
            $"경로명 : {FormatMapStatusValue(true, route.route_name)}",
            $"완료 : {completedCount}/{totalCount}",
            $"현재 : {FormatRouteProgressLine(orderedWaypoints, currentIndex)}",
            $"다음 : {FormatRouteProgressLine(orderedWaypoints, nextIndex)}",
            $"경로 상태 : {FormatMapStatusValue(true, route.route_state)}"
        };

        List<int> recentIndices = BuildRecentRouteProgressIndices(orderedWaypoints, currentIndex, nextIndex);
        lines.Add("최근 진행");
        foreach (int index in recentIndices)
        {
            lines.Add(FormatRouteProgressLine(orderedWaypoints, index));
        }

        return string.Join("\n", lines);
    }

    private static string BuildCompactWaypointRouteText(ControlTowerWaypointRouteData route)
    {
        List<ControlTowerWaypointData> orderedWaypoints = GetOrderedWaypointData(route);
        int completedCount = 0;
        int latestCompletedIndex = -1;
        for (int i = 0; i < orderedWaypoints.Count; i++)
        {
            if (IsCompletedMapWaypointStatus(orderedWaypoints[i]?.status))
            {
                completedCount++;
                latestCompletedIndex = i;
            }
        }

        int totalCount = route.has_total_waypoints && route.total_waypoints > 0
            ? route.total_waypoints
            : orderedWaypoints.Count;
        int currentIndex = FindCurrentRouteWaypointIndex(orderedWaypoints, route);
        int nextIndex = currentIndex >= 0 && currentIndex + 1 < orderedWaypoints.Count ? currentIndex + 1 : -1;
        string currentSegment = currentIndex >= 0 && nextIndex >= 0
            ? $"{FormatWaypointSequence(orderedWaypoints[currentIndex])} → {FormatWaypointSequence(orderedWaypoints[nextIndex])}"
            : "--";
        string latestCompleted = latestCompletedIndex >= 0
            ? FormatWaypointSequence(orderedWaypoints[latestCompletedIndex])
            : "--";

        return
            $"경로 ID : {FormatMapStatusValue(true, route.route_id)}\n" +
            $"경로명 : {FormatRouteDisplayName(route.route_name)}\n" +
            $"진행률 : {completedCount} / {totalCount} 완료\n" +
            $"현재 구간 : {currentSegment}\n" +
            $"운행 상태 : {LocalizeRouteState(route.route_state)}\n" +
            $"최근 완료 : {latestCompleted}";
    }

    private string BuildObstacleRecoveryText(bool hasObstacle, ControlTowerObstacleRecoveryData data)
    {
        return
            $"장애물 상태 : {(hasObstacle ? LocalizeObstacleState(data?.obstacle_state) : "--")}\n" +
            $"장애물 종류 : {(hasObstacle ? LocalizeObstacleType(data?.obstacle_type) : "--")}\n" +
            $"장애물 거리 : {FormatObstacleDistance(hasObstacle, data)}\n" +
            $"장애물 위치 : {FormatObstaclePosition(hasObstacle, data)}\n" +
            $"복구 상태 : {(hasObstacle ? LocalizeRecoveryState(data?.recovery_state) : "--")}\n" +
            $"복구 동작 : {(hasObstacle ? LocalizeRecoveryState(data?.recovery_behavior) : "--")}\n" +
            $"재시도 횟수 : {FormatMapStatusInt(hasObstacle && data != null && data.has_recovery_retry_count, data != null ? data.recovery_retry_count : 0)}\n" +
            $"최근 발생 시각 : {FormatUserFacingDateTime(hasObstacle ? FirstNonEmptyMapStatusValue(data?.detected_at, data?.updated_at) : null)}\n" +
            $"서버 메시지 : {(hasObstacle ? LocalizeObstacleServerMessage(data?.message) : "--")}";
    }

    private static string FormatObstacleDistance(bool hasObstacle, ControlTowerObstacleRecoveryData data)
    {
        return hasObstacle && data != null && data.has_obstacle_distance
            ? $"{data.obstacle_distance:0.00} m"
            : "--";
    }

    private static string LocalizeObstacleState(string rawState)
    {
        if (string.IsNullOrWhiteSpace(rawState))
        {
            return "--";
        }

        string normalized = NormalizeDisplayCode(rawState);
        return normalized switch
        {
            "SLOWDOWN" or "SLOW_DOWN" => "감속 운행",
            "STOP" or "STOPPED" => "정지",
            "PAUSED" => "일시정지",
            "BLOCKED" => "경로 차단",
            "DETECTED" => "장애물 감지",
            "CLEAR" or "NORMAL" => "정상",
            "NO_OBSTACLE" => "장애물 없음",
            "EMERGENCY_STOP" => "긴급 정지",
            "UNKNOWN" => "상태 확인 필요",
            _ => ContainsLatinLetter(rawState) ? "상태 확인 필요" : rawState.Trim()
        };
    }

    private static string LocalizeRecoveryState(string rawState)
    {
        if (string.IsNullOrWhiteSpace(rawState))
        {
            return "--";
        }

        string normalized = NormalizeDisplayCode(rawState);
        return normalized switch
        {
            "IDLE" or "WAITING" => "대기",
            "RECOVERING" or "RECOVERY" => "복구 중",
            "RETRYING" => "재시도 중",
            "RESUMING" => "운행 재개 중",
            "RESUMED" => "운행 재개",
            "RECOVERED" or "SUCCESS" => "복구 완료",
            "FAILED" or "FAILURE" => "복구 실패",
            "CANCELLED" => "복구 취소",
            "NONE" => "--",
            _ => ContainsLatinLetter(rawState) ? "상태 확인 필요" : rawState.Trim()
        };
    }

    private static string LocalizeObstacleType(string rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType))
        {
            return "--";
        }

        string normalized = NormalizeDisplayCode(rawType);
        return normalized switch
        {
            "PERSON" or "HUMAN" => "사람",
            "PALLET" => "팔레트",
            "BOX" => "적재 상자",
            "FORKLIFT" => "지게차",
            "ROBOT" => "로봇",
            "STATIC_OBSTACLE" => "고정 장애물",
            "DYNAMIC_OBSTACLE" => "이동 장애물",
            "UNKNOWN" => "종류 확인 필요",
            _ => ContainsLatinLetter(rawType) ? "종류 확인 필요" : rawType.Trim()
        };
    }

    private static string LocalizeObstacleServerMessage(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return "--";
        }

        string trimmed = rawMessage.Trim();
        string normalized = trimmed.ToUpperInvariant();
        if (normalized.Contains("SCAN_LOST"))
        {
            return normalized.Contains("PAUSED") || trimmed.Contains("일시정지", StringComparison.Ordinal)
                ? "라이다 스캔 신호 유실로 일시정지"
                : "라이다 스캔 신호 유실";
        }

        if (normalized.Contains("RECOVERY_FAILED"))
        {
            return "장애물 복구에 실패했습니다.";
        }

        if (normalized.Contains("RECOVERY_SUCCESS"))
        {
            return "장애물 복구가 완료되었습니다.";
        }

        if (normalized.Contains("SCAN_TIMEOUT"))
        {
            return "라이다 스캔 수신 시간이 초과되었습니다.";
        }

        if (normalized.Contains("EMERGENCY_STOP"))
        {
            return "긴급 정지 상태";
        }

        if (normalized.Contains("OBSTACLE_DETECTED"))
        {
            return "전방 장애물 감지";
        }

        if (normalized.Contains("PATH_BLOCKED"))
        {
            return "이동 경로가 장애물로 차단됨";
        }

        return ContainsLatinLetter(trimmed) ? "서버 상태를 확인해 주세요." : trimmed;
    }

    private static string NormalizeDisplayCode(string value)
    {
        return value.Trim()
            .Replace('-', '_')
            .Replace(' ', '_')
            .ToUpperInvariant();
    }

    private static bool ContainsLatinLetter(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (char character in value)
        {
            if ((character >= 'A' && character <= 'Z') ||
                (character >= 'a' && character <= 'z'))
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshSelectedMapStatusRoute(bool hasRoute, ControlTowerWaypointRouteData route)
    {
        EnsureMapStatusRouteController();
        if (mapStatusRouteController == null)
        {
            return;
        }

        if (!hasRoute || route?.waypoints == null || route.waypoints.Length == 0)
        {
            mapStatusRouteController.ClearRoute();
            return;
        }

        mapStatusRouteController.ApplyRoute(route);
    }

    private static string FormatMapStatusValue(bool hasServerData, string value)
    {
        return hasServerData ? NormalizeDashValue(value) : "--";
    }

    private static string LocalizeLocalizationState(string rawState)
    {
        return LocalizeMapStatusCode(rawState, new Dictionary<string, string>
        {
            ["LOCALIZED"] = "위치 추정 완료",
            ["UNLOCALIZED"] = "위치 추정 필요",
            ["LOCALIZING"] = "위치 추정 중",
            ["LOST"] = "위치 추정 유실",
            ["INITIALIZING"] = "초기화 중",
            ["READY"] = "준비",
            ["ERROR"] = "오류",
            ["UNKNOWN"] = "상태 확인 필요"
        }, "상태 확인 필요");
    }

    private static string LocalizeAmclState(string rawState)
    {
        return LocalizeMapStatusCode(rawState, new Dictionary<string, string>
        {
            ["ACTIVE"] = "활성",
            ["INACTIVE"] = "비활성",
            ["INITIALIZING"] = "초기화 중",
            ["READY"] = "준비",
            ["RUNNING"] = "동작 중",
            ["ERROR"] = "오류",
            ["UNKNOWN"] = "상태 확인 필요"
        }, "상태 확인 필요");
    }

    private static string LocalizeScanMatchingState(string rawState)
    {
        return LocalizeMapStatusCode(rawState, new Dictionary<string, string>
        {
            ["GOOD"] = "양호",
            ["NORMAL"] = "보통",
            ["FAIR"] = "보통",
            ["BAD"] = "불량",
            ["POOR"] = "불량",
            ["LOST"] = "매칭 유실",
            ["ACTIVE"] = "동작 중",
            ["INACTIVE"] = "비활성",
            ["FAILED"] = "실패",
            ["UNKNOWN"] = "상태 확인 필요"
        }, "상태 확인 필요");
    }

    private static string LocalizeNav2State(string rawState)
    {
        return LocalizeMapStatusCode(rawState, new Dictionary<string, string>
        {
            ["ACTIVE"] = "활성",
            ["INACTIVE"] = "비활성",
            ["READY"] = "준비",
            ["RUNNING"] = "동작 중",
            ["NAVIGATING"] = "주행 중",
            ["PAUSED"] = "일시정지",
            ["IDLE"] = "대기",
            ["RECOVERING"] = "복구 중",
            ["ERROR"] = "오류",
            ["UNKNOWN"] = "상태 확인 필요"
        }, "상태 확인 필요");
    }

    private static string LocalizePlannerControllerState(string rawState)
    {
        return LocalizeMapStatusCode(rawState, new Dictionary<string, string>
        {
            ["ACTIVE"] = "활성",
            ["INACTIVE"] = "비활성",
            ["READY"] = "준비",
            ["RUNNING"] = "동작 중",
            ["PLANNING"] = "경로 계획 중",
            ["CONTROLLING"] = "주행 제어 중",
            ["PAUSED"] = "일시정지",
            ["IDLE"] = "대기",
            ["FAILED"] = "실패",
            ["ERROR"] = "오류",
            ["UNKNOWN"] = "상태 확인 필요"
        }, "상태 확인 필요");
    }

    private static string LocalizeRouteState(string rawState)
    {
        return LocalizeMapStatusCode(rawState, new Dictionary<string, string>
        {
            ["NONE"] = "없음",
            ["ACTIVE"] = "진행 중",
            ["RUNNING"] = "진행 중",
            ["PLANNING"] = "경로 계획 중",
            ["PAUSED"] = "일시정지",
            ["COMPLETED"] = "완료",
            ["SUCCEEDED"] = "완료",
            ["FAILED"] = "실패",
            ["CANCELLED"] = "취소",
            ["CANCELED"] = "취소",
            ["ABORTED"] = "중단"
        }, "상태 확인 필요");
    }

    private static string LocalizeGoalResult(string rawState)
    {
        return LocalizeMapStatusCode(rawState, new Dictionary<string, string>
        {
            ["SUCCEEDED"] = "성공",
            ["SUCCESS"] = "성공",
            ["FAILED"] = "실패",
            ["FAILURE"] = "실패",
            ["ABORTED"] = "중단",
            ["CANCELLED"] = "취소",
            ["CANCELED"] = "취소",
            ["RUNNING"] = "진행 중",
            ["PENDING"] = "대기",
            ["UNKNOWN"] = "상태 확인 필요",
            ["NONE"] = "--"
        }, "결과 확인 필요");
    }

    private static string LocalizeMapStatusCode(string rawState, Dictionary<string, string> displayByCode, string unknownDisplay)
    {
        if (string.IsNullOrWhiteSpace(rawState))
        {
            return "--";
        }

        string normalized = NormalizeDisplayCode(rawState);
        return displayByCode.TryGetValue(normalized, out string display)
            ? display
            : ContainsLatinLetter(rawState) ? unknownDisplay : rawState.Trim();
    }

    private static string FormatInitialPoseSet(bool hasServerData, ControlTowerMapNavStatusData data)
    {
        if (!hasServerData || data == null || !data.has_initial_pose_set)
        {
            return "--";
        }

        return data.initial_pose_set ? "설정됨" : "미설정";
    }

    private static string FormatLocalizationQuality(bool hasServerData, ControlTowerMapNavStatusData data)
    {
        if (!hasServerData || data == null)
        {
            return "--";
        }

        string value = NormalizeDashValue(data.localization_quality);
        if (value == "--")
        {
            return "--";
        }

        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? $"{(parsed <= 1f ? parsed * 100f : parsed):0.#}%"
            : value;
    }

    private static string FormatCurrentTargetWaypoint(
        bool hasMapNav,
        ControlTowerMapNavStatusData mapNav,
        bool hasRobotState,
        RobotStateData robotState,
        ControlTowerWaypointRouteData route)
    {
        int targetSequence = 0;
        if (hasMapNav && mapNav != null && mapNav.has_current_target_wp && mapNav.current_target_wp > 0)
        {
            targetSequence = mapNav.current_target_wp;
        }
        else if (hasRobotState && robotState.CurrentTargetWaypoint > 0)
        {
            targetSequence = robotState.CurrentTargetWaypoint;
        }

        return ContainsRouteWaypointSequence(route, targetSequence)
            ? FormatWaypointSequence(targetSequence)
            : "--";
    }

    private static string FormatRouteCurrentWaypoint(
        bool hasRoute,
        ControlTowerWaypointRouteData route,
        bool hasMapNav,
        ControlTowerMapNavStatusData mapNav)
    {
        if (hasRoute && route != null)
        {
            List<ControlTowerWaypointData> orderedWaypoints = GetOrderedWaypointData(route);
            int currentIndex = FindCurrentRouteWaypointIndex(orderedWaypoints, route);
            return currentIndex >= 0
                ? FormatWaypointSequence(orderedWaypoints[currentIndex])
                : "--";
        }

        return "--";
    }

    private static string FormatRouteTotalWaypoints(
        bool hasRoute,
        ControlTowerWaypointRouteData route,
        bool hasMapNav,
        ControlTowerMapNavStatusData mapNav)
    {
        if (hasRoute && route != null)
        {
            if (route.has_total_waypoints)
            {
                return route.total_waypoints > 0
                    ? route.total_waypoints.ToString(CultureInfo.InvariantCulture)
                    : "--";
            }

            if (route.waypoints != null && route.waypoints.Length > 0)
            {
                return route.waypoints.Length.ToString(CultureInfo.InvariantCulture);
            }
        }

        return FormatPositiveMapStatusInt(
            hasMapNav && mapNav != null && mapNav.has_total_waypoints,
            mapNav != null ? mapNav.total_waypoints : 0);
    }

    private static string FormatRouteState(
        bool hasRoute,
        ControlTowerWaypointRouteData route,
        bool hasMapNav,
        ControlTowerMapNavStatusData mapNav)
    {
        if (hasRoute && route != null && !string.IsNullOrWhiteSpace(route.route_state))
        {
            return LocalizeRouteState(route.route_state);
        }

        return hasMapNav ? LocalizeRouteState(mapNav?.route_state) : "--";
    }

    private static bool ContainsRouteWaypointSequence(ControlTowerWaypointRouteData route, int sequence)
    {
        if (route?.waypoints == null || sequence <= 0)
        {
            return false;
        }

        foreach (ControlTowerWaypointData waypoint in route.waypoints)
        {
            if (waypoint != null && waypoint.sequence == sequence)
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatWaypointSequence(ControlTowerWaypointData waypoint)
    {
        return waypoint != null && waypoint.sequence > 0
            ? FormatWaypointSequence(waypoint.sequence)
            : "--";
    }

    private static string FormatWaypointSequence(int sequence)
    {
        return sequence > 0 ? $"WP-{sequence:00}" : "--";
    }

    private static string FormatRouteDisplayName(string rawRouteName)
    {
        if (string.Equals(rawRouteName?.Trim(), "factory_lap", StringComparison.OrdinalIgnoreCase))
        {
            return "공장 순찰";
        }

        return NormalizeDashValue(rawRouteName);
    }

    private static string FormatPositiveMapStatusInt(bool hasServerData, int value)
    {
        return hasServerData && value > 0 ? value.ToString(CultureInfo.InvariantCulture) : "--";
    }

    private static string FormatMapStatusInt(bool hasServerData, int value)
    {
        return hasServerData ? value.ToString(CultureInfo.InvariantCulture) : "--";
    }

    private static string FormatNonNegativeMapStatusInt(bool hasServerData, int value)
    {
        return hasServerData && value >= 0 ? value.ToString(CultureInfo.InvariantCulture) : "--";
    }

    private static string FormatMapStatusFloat(bool hasServerData, float value)
    {
        return hasServerData ? value.ToString("0.00", CultureInfo.InvariantCulture) : "--";
    }

    private static string FormatObstaclePosition(bool hasObstacle, ControlTowerObstacleRecoveryData data)
    {
        if (!hasObstacle || data == null || !data.has_obstacle_x || !data.has_obstacle_y)
        {
            return "--";
        }

        return $"X {data.obstacle_x:0.00}, Y {data.obstacle_y:0.00}";
    }

    private static string FirstNonEmptyMapStatusValue(params string[] values)
    {
        if (values == null)
        {
            return "--";
        }

        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "--";
    }

    private static List<ControlTowerWaypointData> GetOrderedWaypointData(ControlTowerWaypointRouteData route)
    {
        List<ControlTowerWaypointData> ordered = new();
        if (route?.waypoints == null)
        {
            return ordered;
        }

        foreach (ControlTowerWaypointData waypoint in route.waypoints)
        {
            if (waypoint != null)
            {
                ordered.Add(waypoint);
            }
        }

        ordered.Sort(CompareMapWaypointSequence);
        return ordered;
    }

    private static int CompareMapWaypointSequence(ControlTowerWaypointData left, ControlTowerWaypointData right)
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

    private static string FormatMapWaypointStatus(string status)
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

    private static bool IsCurrentMapWaypointStatus(string status)
    {
        string normalized = string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim().ToUpperInvariant();
        return normalized is "CURRENT" or "ACTIVE" or "MOVING" or "NAVIGATING";
    }

    private static bool IsCompletedMapWaypointStatus(string status)
    {
        string normalized = string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim().ToUpperInvariant();
        return normalized is "COMPLETED" or "COMPLETE" or "DONE";
    }

    private static int FindCurrentRouteWaypointIndex(List<ControlTowerWaypointData> orderedWaypoints, ControlTowerWaypointRouteData route)
    {
        if (orderedWaypoints == null || orderedWaypoints.Count == 0)
        {
            return -1;
        }

        for (int i = 0; i < orderedWaypoints.Count; i++)
        {
            if (orderedWaypoints[i] != null && IsCurrentMapWaypointStatus(orderedWaypoints[i].status))
            {
                return i;
            }
        }

        if (route == null || !route.has_current_wp_index)
        {
            return -1;
        }

        for (int i = 0; i < orderedWaypoints.Count; i++)
        {
            if (orderedWaypoints[i] != null && orderedWaypoints[i].sequence == route.current_wp_index)
            {
                return i;
            }
        }

        return route.current_wp_index >= 0 && route.current_wp_index < orderedWaypoints.Count
            ? route.current_wp_index
            : -1;
    }

    private static string FormatRouteProgressLine(List<ControlTowerWaypointData> orderedWaypoints, int index)
    {
        if (orderedWaypoints == null || index < 0 || index >= orderedWaypoints.Count || orderedWaypoints[index] == null)
        {
            return "--";
        }

        ControlTowerWaypointData waypoint = orderedWaypoints[index];
        return $"{FormatWaypointSequence(waypoint)} {FormatMapWaypointStatus(waypoint.status)}";
    }

    private static List<int> BuildRecentRouteProgressIndices(
        List<ControlTowerWaypointData> orderedWaypoints,
        int currentIndex,
        int nextIndex)
    {
        List<int> result = new(3);
        if (orderedWaypoints == null || orderedWaypoints.Count == 0)
        {
            return result;
        }

        int latestCompletedIndex = -1;
        for (int i = 0; i < orderedWaypoints.Count; i++)
        {
            if (orderedWaypoints[i] != null && IsCompletedMapWaypointStatus(orderedWaypoints[i].status))
            {
                latestCompletedIndex = i;
            }
        }

        AddUniqueRouteIndex(result, latestCompletedIndex, orderedWaypoints.Count);
        AddUniqueRouteIndex(result, currentIndex, orderedWaypoints.Count);
        AddUniqueRouteIndex(result, nextIndex, orderedWaypoints.Count);

        int fallbackStart = currentIndex >= 0
            ? Mathf.Max(0, currentIndex - 1)
            : Mathf.Max(0, orderedWaypoints.Count - 3);
        for (int i = fallbackStart; i < orderedWaypoints.Count && result.Count < 3; i++)
        {
            AddUniqueRouteIndex(result, i, orderedWaypoints.Count);
        }

        return result;
    }

    private static void AddUniqueRouteIndex(List<int> indices, int index, int waypointCount)
    {
        if (indices.Count >= 3 || index < 0 || index >= waypointCount || indices.Contains(index))
        {
            return;
        }

        indices.Add(index);
    }

    public void SetMapStatusViewVisible(bool visible)
    {
        if (panelMainMapStatusView != null)
        {
            panelMainMapStatusView.SetActive(visible);
        }
    }

    public void RefreshCameraViewPanel()
    {
        if (panelMainCameraView != null && !panelMainCameraView.activeInHierarchy)
        {
            return;
        }

        EnsureCameraViewTextReferences();
        CacheCameraAiStatusEditModeTemplate();
        EnsureCameraViewSnapshotReference();
        RefreshMainCameraFeedSelectedText();
        ActiveAlertItem latestCameraAlert = GetLatestCameraStatusAlertItem();
        RefreshCameraViewSnapshot(latestCameraAlert);

        string globalBody = hasCameraAiStatusFromServer && cameraAiStreamsBySource.TryGetValue("global", out CameraAiStreamWsData globalStream)
            ? BuildGlobalCctvServerBody(globalStream)
            : $"스트림 : 글로벌 CCTV\n" +
              $"연결 상태 : {FormatCameraConnectionStatusDisplay(currentGlobalCamStatus)}\n" +
              $"영상 수신 : {FormatCameraFrameReceiveDisplay(currentGlobalCamStatus, currentGlobalLastFrame)}\n" +
              $"마지막 수신 : {FormatCameraLastFrameDisplay(currentGlobalLastFrame)}";
        SetTextValueIfBound(textGlobalCctvBody, globalBody);

        string tb3RobotKey = GetCameraViewTb3RobotKey();
        string tb3Body = hasCameraAiStatusFromServer && cameraAiStreamsBySource.TryGetValue(tb3RobotKey, out CameraAiStreamWsData tb3Stream)
            ? BuildTb3CameraServerBody(tb3RobotKey, tb3Stream)
            : $"선택 로봇 : {GetCameraViewTb3RobotDisplay()}\n" +
              $"카메라 채널 : {GetCameraViewTb3ChannelDisplay()}\n" +
              $"연결 상태 : {GetCameraViewTb3ConnectionDisplay()}\n" +
              $"영상 수신 : {GetCameraViewTb3FrameReceiveDisplay()}\n" +
              $"로봇 FSM : {GetCameraViewTb3FsmDisplay()}\n" +
              $"마지막 수신 : {GetCameraViewTb3LastFrameDisplay()}";
        SetTextValueIfBound(textTb3CameraBody, tb3Body);

        SetTextValueIfBound(textAiDetectionBody, BuildCameraAiDetectionBody());

        SetTextValueIfBound(textCameraAiStatusBody, BuildCameraAiStatusTemplateBody(latestCameraAlert));

        RefreshBottomCameraPreviewPanel();
    }

    public void RefreshBottomCameraPreviewPanel()
    {
        ResolveCameraStreamManager();
        cameraStreamManager?.RefreshBottomPreviewTextNow();
    }

    private string BuildGlobalCctvServerBody(CameraAiStreamWsData stream)
    {
        return
            $"연결 상태 : {FormatCameraServerConnectionStatus(stream)}\n" +
            $"스트림 상태 : {NormalizeDashValue(stream?.stream_status)}\n" +
            $"프레임 수신 : {FormatCameraServerFrameReceived(stream)}\n" +
            $"FPS : {FormatOptionalFloat(stream != null && stream.has_fps, stream != null ? stream.fps : 0f, "0.0")}\n" +
            $"스트림 지연 : {FormatOptionalFloatWithSuffix(stream != null && stream.has_stream_latency_ms, stream != null ? stream.stream_latency_ms : 0f, "0", " ms")}\n" +
            $"해상도 : {NormalizeDashValue(stream?.resolution)}\n" +
            $"마지막 수신 : {NormalizeDashValue(stream?.last_frame_at)}\n" +
            $"오류 메시지 : {NormalizeDashValue(stream?.error_message)}";
    }

    private string BuildTb3CameraServerBody(string robotKey, CameraAiStreamWsData stream)
    {
        return
            $"선택 채널 : {NormalizeDashValue(stream?.channel)}\n" +
            $"로봇 ID : {FormatCameraRobotLabel(robotKey)}\n" +
            $"연결 상태 : {FormatCameraServerConnectionStatus(stream)}\n" +
            $"스트림 상태 : {NormalizeDashValue(stream?.stream_status)}\n" +
            $"프레임 수신 : {FormatCameraServerFrameReceived(stream)}\n" +
            $"FPS : {FormatOptionalFloat(stream != null && stream.has_fps, stream != null ? stream.fps : 0f, "0.0")}\n" +
            $"스트림 지연 : {FormatOptionalFloatWithSuffix(stream != null && stream.has_stream_latency_ms, stream != null ? stream.stream_latency_ms : 0f, "0", " ms")}\n" +
            $"해상도 : {NormalizeDashValue(stream?.resolution)}\n" +
            $"마지막 수신 : {NormalizeDashValue(stream?.last_frame_at)}\n" +
            $"오류 메시지 : {NormalizeDashValue(stream?.error_message)}";
    }

    private string BuildCameraAiStatusTemplateBody(ActiveAlertItem item)
    {
        string cameraInput = TryGetDashboardCameraConnectionCount(out int connectedCount, out int totalCount)
            ? $"{connectedCount}/{totalCount} 연결"
            : "--";
        string eventChannel = isWebSocketConnected
            ? "연결됨"
            : "연결 끊김";
        string latestDetection = item != null
            ? FormatUserFacingDateTime(item.Timestamp)
            : "감지 이력 없음";
        string detectionType = item != null
            ? FormatIncidentTypeForKoreanDisplay(item.IncidentType)
            : "--";
        string relatedRobot = item != null ? NormalizeDashValue(item.RobotDisplay) : "--";
        string location = item != null ? NormalizeDashValue(item.LocationDisplay) : "--";
        string confidence = item != null ? NormalizeDashValue(item.ConfidenceDisplay) : "--";
        string snapshot = GetCameraAlertSnapshotStatus(item);

        return ApplyCameraAiStatusValuesToEditModeTemplate(
            cameraInput,
            eventChannel,
            latestDetection,
            detectionType,
            relatedRobot,
            location,
            confidence,
            snapshot);
    }

    private string ApplyCameraAiStatusValuesToEditModeTemplate(
        string cameraInput,
        string eventChannel,
        string latestDetection,
        string detectionType,
        string relatedRobot,
        string location,
        string confidence,
        string snapshot)
    {
        if (string.IsNullOrEmpty(cameraAiStatusEditModeTemplate))
        {
            return string.Empty;
        }

        string[] lines = cameraAiStatusEditModeTemplate.Replace("\r\n", "\n").Split('\n');
        for (int index = 0; index < lines.Length; index++)
        {
            int separatorIndex = lines[index].IndexOf(':');
            if (separatorIndex < 0)
            {
                continue;
            }

            string label = lines[index].Substring(0, separatorIndex).Trim();
            string value = label switch
            {
                "카메라 입력" => cameraInput,
                "이벤트 채널" => eventChannel,
                "최근 감지" => latestDetection,
                "감지 유형" => detectionType,
                "관련 로봇" => relatedRobot,
                "발생 위치" => location,
                "신뢰도" => confidence,
                "스냅샷" => snapshot,
                _ => null
            };

            if (value != null)
            {
                lines[index] = $"{lines[index].Substring(0, separatorIndex + 1)} {value}";
            }
        }

        return string.Join("\n", lines);
    }

    private string GetCameraAlertSnapshotStatus(ActiveAlertItem item)
    {
        if (item == null)
        {
            return "--";
        }

        if (TryGetCachedAlertSnapshot(item, out _))
        {
            return "수신됨";
        }

        if (IsMissingSnapshotPhotoUrl(item.PhotoUrl))
        {
            return "없음";
        }

        if (loadingAlertSnapshotIds.Contains(item.LogId))
        {
            return "수신 중";
        }

        return failedAlertSnapshotIds.Contains(item.LogId) ? "수신 실패" : "수신 중";
    }

    private string BuildCameraAiStatusServerBody()
    {
        CameraAiModelWsData ai = cameraAiModelStatus;
        return
            $"모델 상태 : {NormalizeDashValue(ai?.model_status)}\n" +
            $"모델명 : {NormalizeDashValue(ai?.model_name)}\n" +
            $"모델 버전 : {NormalizeDashValue(ai?.model_version)}\n" +
            $"추론 상태 : {NormalizeDashValue(ai?.inference_status)}\n" +
            $"추론 FPS : {FormatOptionalFloat(ai != null && ai.has_inference_fps, ai != null ? ai.inference_fps : 0f, "0.0")}\n" +
            $"추론 지연 : {FormatOptionalFloatWithSuffix(ai != null && ai.has_inference_latency_ms, ai != null ? ai.inference_latency_ms : 0f, "0", " ms")}\n" +
            $"감지 활성 : {FormatOptionalBool(ai != null && ai.has_detection_enabled, ai != null && ai.detection_enabled)}\n" +
            $"마지막 추론 : {NormalizeDashValue(ai?.last_inference_at)}\n" +
            $"마지막 감지 : {NormalizeDashValue(ai?.last_detection_at)}\n" +
            $"오류 메시지 : {NormalizeDashValue(ai?.error_message)}";
    }

    private string BuildCameraAiDetectionBody()
    {
        ActiveAlertItem item = GetCurrentCameraAlertItem();
        if (item == null)
        {
            return
                $"이벤트 종류 : {NormalizeDashValue(currentAiEvent)}\n" +
                $"감지 카메라 : --\n" +
                $"감지 주체 : {NormalizeDashValue(currentObstacleSource)}\n" +
                $"관련 로봇 : {NormalizeDashValue(currentEventAlertRobotDisplay)}\n" +
                $"신뢰도 : {NormalizeDashValue(currentConfidence)}\n" +
                $"발생 위치 : {FormatCameraAlertLocationDisplay(currentCameraLocation)}\n" +
                $"발생 시각 : --\n" +
                $"처리 상태 : --\n" +
                $"메시지 : {NormalizeDashValue(currentEventAlertMessageDisplay)}";
        }

        return
            $"이벤트 종류 : {FormatIncidentTypeForKoreanDisplay(item.IncidentType)}\n" +
            $"감지 카메라 : {NormalizeDashValue(item.CameraId)}\n" +
            $"감지 주체 : {NormalizeDashValue(item.DetectedBy)}\n" +
            $"관련 로봇 : {NormalizeDashValue(item.RobotDisplay)}\n" +
            $"신뢰도 : {NormalizeDashValue(item.ConfidenceDisplay)}\n" +
            $"발생 위치 : {NormalizeDashValue(item.LocationDisplay)}\n" +
            $"발생 시각 : {FormatUserFacingDateTime(item.Timestamp)}\n" +
            $"처리 상태 : {NormalizeDashValue(item.Status)}\n" +
            $"메시지 : {NormalizeDashValue(item.Message)}";
    }

    private ActiveAlertItem GetCurrentCameraAlertItem()
    {
        if (currentCameraSnapshotLogId > 0 && incidentHistoryByLogId.TryGetValue(currentCameraSnapshotLogId, out ActiveAlertItem snapshotItem))
        {
            return snapshotItem;
        }

        if (selectedAlertLogId > 0 && incidentHistoryByLogId.TryGetValue(selectedAlertLogId, out ActiveAlertItem selectedItem))
        {
            return selectedItem;
        }

        return null;
    }

    private ActiveAlertItem GetLatestCameraStatusAlertItem()
    {
        ActiveAlertItem latest = null;
        foreach (ActiveAlertItem item in incidentHistoryByLogId.Values)
        {
            if (item == null || item.LogId <= 0 || IsBlankAlertValue(item.IncidentType))
            {
                continue;
            }

            if (latest == null || CompareAlertItemsByTimestampAscending(latest, item) < 0)
            {
                latest = item;
            }
        }

        return latest;
    }

    private static string FormatCameraServerConnectionStatus(CameraAiStreamWsData stream)
    {
        if (stream == null || !stream.has_connected)
        {
            return "--";
        }

        return stream.connected ? "연결됨" : "연결 끊김";
    }

    private static string FormatCameraServerFrameReceived(CameraAiStreamWsData stream)
    {
        if (stream == null || !stream.has_frame_received)
        {
            return "--";
        }

        return stream.frame_received ? "수신됨" : "대기";
    }

    private static string FormatOptionalFloat(bool hasValue, float value, string format)
    {
        return hasValue ? value.ToString(format, CultureInfo.InvariantCulture) : "--";
    }

    private static string FormatOptionalFloatWithSuffix(bool hasValue, float value, string format, string suffix)
    {
        return hasValue ? value.ToString(format, CultureInfo.InvariantCulture) + suffix : "--";
    }

    private static string FormatOptionalBool(bool hasValue, bool value)
    {
        return hasValue ? (value ? "활성" : "비활성") : "--";
    }

    public void UpdateCameraViewFromSelectedRobot()
    {
        currentDetectedRobot = selectedRobotId;
        RefreshCameraViewPanel();
    }

    public void UpdateBottomCameraPreviewFromSelectedRobot()
    {
        currentDetectedRobot = selectedRobotId;
        RefreshBottomCameraPreviewPanel();
    }

    public void UpdateCameraViewFromAlert()
    {
        bool hasEvent = !string.IsNullOrWhiteSpace(currentAiEvent) && currentAiEvent != "None";
        currentLastDetection = hasEvent ? currentAiEvent : "--";
        currentDetectedRobot = hasEvent ? currentEventAlertRobotDisplay : selectedRobotId;
        currentDetectedZone = hasEvent ? currentCameraLocation : "-";
        RefreshCameraViewPanel();
    }

    public void UpdateBottomCameraPreviewFromAlert()
    {
        RefreshBottomCameraPreviewPanel();
    }

    public void SetBottomCameraPreviewStatus(string globalCctvStatus, string tb3CameraStatus, string lastDetection, string eventType, string confidence)
    {
        currentGlobalCamStatus = string.IsNullOrWhiteSpace(globalCctvStatus) ? currentGlobalCamStatus : globalCctvStatus;
        currentCameraStatus = string.IsNullOrWhiteSpace(tb3CameraStatus) ? currentCameraStatus : tb3CameraStatus;
        currentLastDetection = string.IsNullOrWhiteSpace(lastDetection) ? currentLastDetection : lastDetection;
        currentAiEvent = string.IsNullOrWhiteSpace(eventType) ? currentAiEvent : eventType;
        currentConfidence = string.IsNullOrWhiteSpace(confidence) ? currentConfidence : confidence;
        RefreshBottomCameraPreviewPanel();
    }

    public void ApplyCameraStreamStatus(string globalStatus, string tb3Status)
    {
        hasCameraStatusFromStream = true;
        currentGlobalCamStatus = string.IsNullOrWhiteSpace(globalStatus) ? currentGlobalCamStatus : globalStatus;
        currentCameraStatus = string.IsNullOrWhiteSpace(tb3Status) ? currentCameraStatus : tb3Status;
        currentStreamType = "WebSocket JPEG";
        cameraViewDirty = true;
    }

    public void ApplyCameraFrameApplied(string sourceId, string lastFrameTime)
    {
        string source = string.IsNullOrWhiteSpace(sourceId) ? string.Empty : sourceId.Trim().ToLowerInvariant();
        string timeText = string.IsNullOrWhiteSpace(lastFrameTime) ? "--" : lastFrameTime.Trim();
        bool frameTimeChanged = false;
        if (source == "global" || source == "global_cctv" || source == "global cctv")
        {
            frameTimeChanged = !string.Equals(currentGlobalLastFrame, timeText, StringComparison.Ordinal);
            currentGlobalLastFrame = timeText;
        }

        if (frameTimeChanged)
        {
            cameraViewDirty = true;
        }
    }

    public void ApplySelectedTb3CameraFrameApplied(string robotId, string lastFrameTime)
    {
        currentSelectedTb3FrameRobotId = NormalizeRobotKey(robotId);
        currentSelectedTb3LastFrame = string.IsNullOrWhiteSpace(lastFrameTime) ? "--" : lastFrameTime.Trim();
        currentLastFrame = currentSelectedTb3LastFrame;
        cameraViewDirty = true;
        dashboardDirty = true;
    }

    public void SelectMainFeedGlobalCctv()
    {
        SelectMainCameraFeed("global");
    }

    public void SelectMainFeedTb3_01()
    {
        SelectMainCameraFeed("tb3-01");
    }

    public void SelectMainFeedTb3_02()
    {
        SelectMainCameraFeed("tb3-02");
    }

    public void SelectMainFeedTb3_03()
    {
        SelectMainCameraFeed("tb3-03");
    }

    public void SelectMainCameraFeed(string feedId)
    {
        string feedLabel = FormatMainCameraFeedLabel(feedId);
        currentMainCameraFeedLabel = feedLabel;
        selectedMainFeedRobotId = NormalizeMainCameraFeedId(feedId);
        RefreshMainCameraFeedSelectedText();

        ResolveCameraStreamManager();
        bool connected = cameraStreamManager != null && cameraStreamManager.SelectMainCameraFeed(feedId);
        RefreshCameraViewPanel();
        if (!connected)
        {
            AddEventLog("CAM", $"{feedLabel} feed selected but stream is not connected");
            return;
        }

        AddEventLog("CAM", $"Main Feed : {feedLabel}");
    }

    private void SyncMainCameraFeedWithSelectedRobot(bool applyStreamNow)
    {
        selectedMainFeedRobotId = NormalizeMainCameraFeedId(selectedRobotId);
        currentMainCameraFeedLabel = FormatMainCameraFeedLabel(selectedMainFeedRobotId);
        RefreshMainCameraFeedSelectedText();
        RefreshCameraViewPanel();
        RefreshBottomCameraPreviewPanel();

        ResolveCameraStreamManager();
        if (cameraStreamManager == null)
        {
            return;
        }

        if (applyStreamNow)
        {
            cameraStreamManager.SelectMainCameraFeed(selectedMainFeedRobotId);
        }
        else
        {
            cameraStreamManager.SetMainCameraFeedSelection(selectedMainFeedRobotId);
        }
    }

    private bool IsCameraViewActive()
    {
        return panelMainCameraView != null && panelMainCameraView.activeInHierarchy;
    }

    public void ShowFactory3DMapMode()
    {
        EnsureFactoryViewRuntimeReferences();
        isFactory3DMapMode = true;

        if (rawImageFactory3DMapPreview != null) rawImageFactory3DMapPreview.SetActive(true);
        if (panelMini2DMap != null) panelMini2DMap.SetActive(true);
        if (panelFactory3DViewControls != null) panelFactory3DViewControls.SetActive(true);
        if (panelFactory2DGlobalCamera != null) panelFactory2DGlobalCamera.SetActive(false);
        if (imageMapAreaBackground != null) imageMapAreaBackground.SetActive(false);

        mini2DMapController?.OnViewActivated();
        factory3DRobotMarkerController?.OnViewActivated();
        UpdatePreviewCameraRenderingState();
        UpdateFactoryMapModeButtonLabel();
        AddEventLog("UI", "Factory 3D Map Mode selected");
    }

    public void ShowFactory2DMapMode()
    {
        EnsureFactoryViewRuntimeReferences();
        isFactory3DMapMode = false;

        if (imageMapAreaBackground != null) imageMapAreaBackground.SetActive(true);
        if (rawImageFactory3DMapPreview != null) rawImageFactory3DMapPreview.SetActive(false);
        if (panelMini2DMap != null) panelMini2DMap.SetActive(false);
        if (panelFactory3DViewControls != null) panelFactory3DViewControls.SetActive(false);
        if (panelFactory2DGlobalCamera != null) panelFactory2DGlobalCamera.SetActive(true);

        full2DMapController?.OnViewActivated();
        UpdatePreviewCameraRenderingState();
        UpdateFactoryMapModeButtonLabel();
        AddEventLog("UI", "Factory 2D Map Mode selected");
    }

    public void ToggleFactoryMapMode()
    {
        if (isFactory3DMapMode)
        {
            ShowFactory2DMapMode();
        }
        else
        {
            ShowFactory3DMapMode();
        }
    }

    public void SetFactory3DMapPreviewVisible(bool visible)
    {
        EnsureFactory3DMapReferences();

        if (rawImageFactory3DMapPreview != null)
        {
            rawImageFactory3DMapPreview.SetActive(visible);
        }
    }

    public void RefreshIconSlots()
    {
        SetAlertIconState(currentAiEvent);
    }

    public void SetIconSlotVisible(string iconName, bool visible)
    {
        if (string.IsNullOrWhiteSpace(iconName))
        {
            return;
        }

        GameObject iconObject = FindSceneGameObjectByName(iconName.Trim());
        if (iconObject != null)
        {
            iconObject.SetActive(visible);
        }
    }

    public void SetAlertIconState(string alertType)
    {
        string normalizedAlert = string.IsNullOrWhiteSpace(alertType) ? string.Empty : alertType.Trim().ToUpperInvariant();
        SetIconSlotVisible("Icon_SummaryNoHelmet", normalizedAlert == "NO_HELMET");
        SetIconSlotVisible("Icon_SummaryTrespass", normalizedAlert == "TRESPASS");
        SetIconSlotVisible("Icon_SummaryFall", normalizedAlert == "FALL");
        SetIconSlotVisible("Icon_SummaryFire", normalizedAlert == "FIRE");
        SetIconSlotVisible("Icon_SummaryLowBattery", normalizedAlert == "LOW_BATTERY");
        SetIconSlotVisible("Icon_FactoryEventMarker", normalizedAlert == "NO_HELMET" || normalizedAlert == "TRESPASS" || normalizedAlert == "FALL" || normalizedAlert == "FIRE");
        SetIconSlotVisible("Icon_PopupAlertType", !string.IsNullOrEmpty(normalizedAlert) && normalizedAlert != "NONE");
    }

    public void RefreshDashboardViewPanel()
    {
        if (panelMainDashboardView != null && !panelMainDashboardView.activeInHierarchy)
        {
            return;
        }

        using (DashboardRefreshMarker.Auto())
        {
            RefreshDashboardViewPanelInternal();
        }
    }

    private void RefreshDashboardViewPanelInternal()
    {
        EnsureDashboardReferences();

        SetTextValueIfBound(textDashboardFactoryOverviewBody, BuildDashboardFactoryOverviewBody());
        SetTextValueIfBound(textDashboardRobotStatusBody, BuildDashboardRobotStatusBody());

        RefreshDashboardRobotSummaryTexts();

        SetTextValueIfBound(textDashboardMapNav2Body, BuildDashboardMapNav2Body());
        SetTextValueIfBound(textDashboardCameraAiBody, BuildDashboardCameraAiBody());

        RefreshDashboardSystemHealthValueTexts();

        RefreshDashboardTimelineText();
        RefreshDashboardRuntimeBinderState();
    }

    public void UpdateDashboardFromRobotState()
    {
        RefreshDashboardViewPanel();
    }

    public void UpdateDashboardFromAlert()
    {
        RefreshDashboardViewPanel();
    }

    public void UpdateDashboardFromSystemStatus()
    {
        RefreshDashboardViewPanel();
    }

    private void RefreshDashboardSystemHealthValueTexts()
    {
        EnsureDashboardSystemHealthValueReferences();

        SetTextValueIfBound(textSystemHealthServerValue, $"서버 : {FormatDashboardConnectionStatus(currentServerStatus)}");
        SetTextValueIfBound(textSystemHealthWebSocketValue, $"WebSocket : {FormatDashboardConnectionStatus(currentWebSocketStatus)}");
        SetTextValueIfBound(textSystemHealthRos2Value, $"ROS2 : {FormatDashboardRos2Status()}");
        SetTextValueIfBound(textSystemHealthAiModelValue, "AI 모델 : --");
        SetTextValueIfBound(textSystemHealthDbValue, "DB : --");
        SetTextValueIfBound(textSystemHealthHealthPercent, "전체 상태 : --");
    }

    private void RefreshDashboardRobotSummaryTexts()
    {
        EnsureDashboardRobotSummaryReferences();
        SetTextValueIfBound(textDashboardRobotReadyCount, "순찰 준비 : --");
    }

    private string BuildDashboardFactoryOverviewBody()
    {
        return
            "가동 로봇 : --\n" +
            $"안전모 미착용 : {FormatSummaryValuePlain(summaryNoHelmetCount, hasSummaryNoHelmetCount)}\n" +
            $"쓰러짐 감지 : {FormatSummaryValuePlain(summaryFallCount, hasSummaryFallCount)}\n" +
            $"화재 감지 : {FormatSummaryValuePlain(summaryFireCount, hasSummaryFireCount)}";
    }

    private string FormatCurrentAlertForFactoryOverview()
    {
        if (string.IsNullOrWhiteSpace(lastRobotAlert) ||
            lastRobotAlert.Equals("None", StringComparison.OrdinalIgnoreCase) ||
            lastRobotAlert.Equals("Normal", StringComparison.OrdinalIgnoreCase))
        {
            return "--";
        }

        return lastRobotAlert.Trim();
    }

    private string BuildDashboardRobotStatusBody()
    {
        string displayRobotId = string.IsNullOrWhiteSpace(selectedRobotId) ? "--" : selectedRobotId;
        string displayFsm = "--";
        string displayBattery = "--";
        string displayCurrentTarget = "--";
        string displayCommState = "--";

        if (robotStatesById.TryGetValue(displayRobotId, out RobotStateData selectedState))
        {
            displayFsm = FormatServerFieldForDashboard(selectedState.FsmState);
            displayBattery = FormatServerFieldForDashboard(selectedState.Battery);
            displayCurrentTarget = FormatRobotTargetWaypoint(true, selectedState);
            displayCommState = "수신됨";
        }

        return
            $"선택 로봇 : {displayRobotId}\n" +
            $"FSM 상태 : {displayFsm}\n" +
            $"배터리 : {displayBattery}\n" +
            $"현재 목표 : {displayCurrentTarget}\n" +
            $"통신 상태 : {displayCommState}";
    }

    private string BuildDashboardMapNav2Body()
    {
        string robotKey = NormalizeRobotKey(selectedRobotId);
        bool hasMapNav = mapNavStatusByRobotId.TryGetValue(robotKey, out ControlTowerMapNavStatusData mapNavStatus) && mapNavStatus != null;
        bool hasRoute = waypointRouteByRobotId.TryGetValue(robotKey, out ControlTowerWaypointRouteData route) && route != null;
        bool hasRobotState = robotStatesById.TryGetValue(robotKey, out RobotStateData robotState);

        CacheDashboardMapNav2EditModeTemplate();
        return ApplyDashboardMapNav2ValuesToTemplate(
            dashboardMapNav2EditModeTemplate,
            FormatMapStatusValue(hasMapNav, mapNavStatus?.localization_state),
            FormatMapStatusValue(hasMapNav, mapNavStatus?.nav2_state),
            FormatCurrentTargetWaypoint(hasMapNav, mapNavStatus, hasRobotState, robotState, hasRoute ? route : null),
            FormatDashboardCurrentWaypoint(hasRoute, route, hasRobotState, robotState),
            FormatRouteState(hasRoute, route, hasMapNav, mapNavStatus));
    }

    private void CacheDashboardMapNav2EditModeTemplate()
    {
        if (textDashboardMapNav2Body == null || dashboardMapNav2TemplateSource == textDashboardMapNav2Body)
        {
            return;
        }

        dashboardMapNav2TemplateSource = textDashboardMapNav2Body;
        dashboardMapNav2EditModeTemplate = textDashboardMapNav2Body.text ?? string.Empty;
    }

    private static string ApplyDashboardMapNav2ValuesToTemplate(
        string template,
        string localizationState,
        string nav2State,
        string currentTarget,
        string currentWaypoint,
        string routeState)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template ?? string.Empty;
        }

        string[] lines = template.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = ReplaceDashboardMapNav2TemplateValue(
                lines[i],
                localizationState,
                nav2State,
                currentTarget,
                currentWaypoint,
                routeState);
        }

        return string.Join("\n", lines);
    }

    private static string ReplaceDashboardMapNav2TemplateValue(
        string line,
        string localizationState,
        string nav2State,
        string currentTarget,
        string currentWaypoint,
        string routeState)
    {
        if (string.IsNullOrEmpty(line))
        {
            return line;
        }

        int separatorIndex = line.IndexOf(':');
        if (separatorIndex < 0)
        {
            return line;
        }

        string label = line.Substring(0, separatorIndex).Trim();
        string value = label switch
        {
            "Localization 상태" => localizationState,
            "Nav2 상태" => nav2State,
            "현재 목표" => currentTarget,
            "현재 Waypoint" => currentWaypoint,
            "경로 상태" => routeState,
            _ => null
        };

        return value == null
            ? line
            : $"{line.Substring(0, separatorIndex + 1)} {value}";
    }

    private static string FormatDashboardCurrentWaypoint(
        bool hasRoute,
        ControlTowerWaypointRouteData route,
        bool hasRobotState,
        RobotStateData robotState)
    {
        string routeWaypoint = FormatRouteCurrentWaypoint(hasRoute, route, false, null);
        if (routeWaypoint != "--")
        {
            return routeWaypoint;
        }

        return hasRobotState && robotState.CurrentTargetWaypoint > 0
            ? robotState.CurrentTargetWaypoint.ToString(CultureInfo.InvariantCulture)
            : "--";
    }

    private string BuildDashboardCameraAiBody()
    {
        return
            $"글로벌 영상 : {FormatDashboardCameraSourceStatus(cameraSourceGlobalStatus)}\n" +
            $"TB3-01 영상 : {FormatDashboardCameraSourceStatus(cameraSourceTb3_01Status)}\n" +
            $"TB3-02 영상 : {FormatDashboardCameraSourceStatus(cameraSourceTb3_02Status)}\n" +
            $"AI 모델 : {FormatDashboardAiModelStatus(currentAiModelStatus)}";
    }

    private static string FormatDashboardCameraSourceStatus(string status)
    {
        if (!IsCameraSummarySourceKnown(status))
        {
            return "--";
        }

        string normalized = status.Trim().ToUpperInvariant();
        if (normalized == "CONNECTING")
        {
            return "연결 중";
        }

        if (normalized == "VIDEO_WAITING")
        {
            return "영상 대기";
        }

        return normalized switch
        {
            "CONNECTED" => "연결됨",
            "WAITING" => "대기",
            "DISCONNECTED" => "연결 끊김",
            _ => "--"
        };
    }

    private static string FormatDashboardAiModelStatus(string status)
    {
        string normalized = string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim().ToUpperInvariant();
        return normalized switch
        {
            "RUNNING" or "READY" or "ACTIVE" or "OK" => "정상",
            "WAITING" or "LOADING" or "INITIALIZING" => "대기",
            "ERROR" or "FAILED" or "OFFLINE" or "DISCONNECTED" => "오류",
            _ => "--"
        };
    }

    private string FormatDashboardRos2Status()
    {
        if (hasSystemStatusFromServer)
        {
            return NormalizeDashValue(currentRos2Status);
        }

        return isWebSocketConnected ? NormalizeDashValue(currentRos2Status) : "--";
    }

    private string FormatDashboardConnectionStatus(string status)
    {
        string normalized = NormalizeDashValue(status);
        if (normalized == "--")
        {
            return "--";
        }

        if (normalized.Equals("Online", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Connected", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("WebSocket", StringComparison.OrdinalIgnoreCase))
        {
            return "연결됨";
        }

        if (normalized.Equals("Offline", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Disconnected", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Closed", StringComparison.OrdinalIgnoreCase))
        {
            return "--";
        }

        return normalized;
    }

    private static string FormatServerFieldForDashboard(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();
    }

    private static void SetTextValueIfBound(TMP_Text targetText, string value)
    {
        if (targetText != null && targetText.text != value)
        {
            targetText.text = value;
        }
    }

    private void RefreshDashboardRuntimeBinderState()
    {
        ResolveDashboardRuntimeBinder();
        if (dashboardRuntimeBinder == null)
        {
            return;
        }

        dashboardRuntimeBinder.SetSelectedRobot(selectedRobotId);
        ApplyDashboardRobotBattery("tb3-01");
        ApplyDashboardRobotBattery("tb3-02");
        ApplyDashboardRobotBattery("tb3-03");
        dashboardRuntimeBinder.SetPeopleSummaryStatusColors(
            todaySummaryRequestState,
            hasSummaryAttendanceCurrentIn,
            hasSummaryAttendanceOut,
            hasSummaryVisitorTotal);

        dashboardRuntimeBinder.SetFactoryPatrolCoverageUnavailable();
        string selectedRobotKey = NormalizeRobotKey(selectedRobotId);
        bool hasMapNavStatus = mapNavStatusByRobotId.TryGetValue(selectedRobotKey, out ControlTowerMapNavStatusData dashboardMapNavStatus) &&
                               dashboardMapNavStatus != null;
        dashboardRuntimeBinder.SetMapNavigationStatusColors(
            dashboardMapNavStatus?.localization_state,
            dashboardMapNavStatus?.nav2_state,
            hasMapNavStatus);

        bool hasWaypointProgress = TryGetDashboardWaypointProgress(out int completedWaypoints, out int totalWaypoints);
        dashboardRuntimeBinder.SetMapWaypointCompletionProgress(
            completedWaypoints,
            totalWaypoints,
            hasWaypointProgress);

        if (TryGetDashboardCameraConnectionCount(out int connectedCameraCount, out int totalCameraCount))
        {
            dashboardRuntimeBinder.SetCameraConnectionRatio(connectedCameraCount, totalCameraCount);
        }
        else
        {
            dashboardRuntimeBinder.SetCameraActivityUnavailable();
        }

        dashboardRuntimeBinder.SetCameraSourceDotStates(
            cameraSourceGlobalStatus,
            cameraSourceTb3_01Status,
            cameraSourceTb3_02Status,
            currentAiModelStatus);

        dashboardRuntimeBinder.SetSystemStatusColors(
            currentServerStatus,
            currentWebSocketStatus,
            currentRos2Status,
            currentAiModelStatus,
            "--");

        dashboardRuntimeBinder.SetSystemHealthUnavailable();
    }

    private void ApplyDashboardRobotBattery(string robotId)
    {
        string key = NormalizeRobotKey(robotId);
        float batteryPercent = GetRobotBatteryPercentOrUnknown(key);
        string robotStatus = robotStatesById.TryGetValue(key, out RobotStateData state) ? state.FsmState : string.Empty;
        dashboardRuntimeBinder.SetRobotBattery(key, batteryPercent, robotStatus);
    }

    private bool TryGetDashboardWaypointProgress(out int completedWaypoints, out int totalWaypoints)
    {
        completedWaypoints = 0;
        totalWaypoints = DefaultDashboardWaypointTotal;

        string robotKey = NormalizeRobotKey(selectedRobotId);
        if (!waypointRouteByRobotId.TryGetValue(robotKey, out ControlTowerWaypointRouteData route) || route == null)
        {
            return false;
        }

        totalWaypoints = route.has_total_waypoints && route.total_waypoints > 0
            ? route.total_waypoints
            : (route.waypoints != null && route.waypoints.Length > 0
                ? route.waypoints.Length
                : DefaultDashboardWaypointTotal);
        totalWaypoints = Mathf.Max(1, totalWaypoints);

        if (route.waypoints == null || route.waypoints.Length == 0)
        {
            return false;
        }

        foreach (ControlTowerWaypointData waypoint in route.waypoints)
        {
            if (waypoint != null &&
                string.Equals(waypoint.status?.Trim(), "COMPLETED", StringComparison.OrdinalIgnoreCase))
            {
                completedWaypoints++;
            }
        }

        completedWaypoints = Mathf.Clamp(completedWaypoints, 0, totalWaypoints);
        return true;
    }

    private bool TryGetDashboardCameraConnectionCount(out int connectedCount, out int totalCount)
    {
        totalCount = 3;
        connectedCount = 0;

        if (!AreAllCameraSummarySourcesKnown())
        {
            return false;
        }

        connectedCount += IsCameraSummarySourceConnected(cameraSourceGlobalStatus) ? 1 : 0;
        connectedCount += IsCameraSummarySourceConnected(cameraSourceTb3_01Status) ? 1 : 0;
        connectedCount += IsCameraSummarySourceConnected(cameraSourceTb3_02Status) ? 1 : 0;
        return true;
    }

    private void RefreshDashboardTimelineMarkers()
    {
        // TimelineDot_01~03 may be used as dashboard log filter controls.
        // Runtime no longer toggles them by recent log count.
    }

    private string ClassifyDashboardTimelineEvent(string level, string message)
    {
        string source = $"{level} {message}".ToUpperInvariant();
        if (source.Contains("ERROR") || source.Contains("EMERGENCY") || source.Contains("FALL") || source.Contains("FIRE") ||
            source.Contains("STOP") || source.Contains("FAILED") || source.Contains("DISCONNECTED"))
        {
            return "ERROR";
        }

        if (source.Contains("WARNING") || source.Contains("WARN") || source.Contains("NO_HELMET") || source.Contains("LOW_BATTERY") ||
            source.Contains("WAITING"))
        {
            return "WARNING";
        }

        if (source.Contains("CAM") || source.Contains("CAMERA"))
        {
            return "CAMERA";
        }

        if (source.Contains("ROBOT") || source.Contains("CHARGING") || source.Contains("SYSTEM") || source.Contains("NAV") ||
            source.Contains("BATTERY") || source.Contains("WS"))
        {
            return "SYSTEM";
        }

        return "NORMAL";
    }

    public void AddDashboardTimelineEvent(string message)
    {
        AddDashboardTimelineEvent("INFO", message);
    }

    private void AddDashboardTimelineEvent(string level, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string normalizedLevel = string.IsNullOrWhiteSpace(level) ? "INFO" : level.Trim().ToUpperInvariant();
        dashboardTimelineLines.Add($"[{DateTime.Now:HH:mm}] {FormatDashboardTimelineMessage(normalizedLevel, message)}");
        dashboardTimelineLevels.Add(normalizedLevel);
        while (dashboardTimelineLines.Count > MaxDashboardTimelineLines)
        {
            dashboardTimelineLines.RemoveAt(0);
        }

        while (dashboardTimelineLevels.Count > MaxDashboardTimelineLines)
        {
            dashboardTimelineLevels.RemoveAt(0);
        }

    }

    private static string FormatDashboardTimelineMessage(string level, string message)
    {
        string trimmedMessage = message.Trim();
        if (trimmedMessage.StartsWith("[", StringComparison.Ordinal))
        {
            return trimmedMessage;
        }

        string prefix = ResolveDashboardTimelinePrefix(level, trimmedMessage);
        return $"[{prefix}] {trimmedMessage}";
    }

    private static string ResolveDashboardTimelinePrefix(string level, string message)
    {
        string source = $"{level} {message}".ToUpperInvariant();
        if (source.Contains("ERROR") || source.Contains("FAILED") || source.Contains("DISCONNECTED") ||
            source.Contains("FIRE") || source.Contains("FALL") || source.Contains("EMERGENCY") || source.Contains("STOP"))
        {
            return "ERROR";
        }

        if (source.Contains("WARNING") || source.Contains("WARN") || source.Contains("NO_HELMET") ||
            source.Contains("LOW_BATTERY") || source.Contains("WAITING"))
        {
            return "WARN";
        }

        if (source.Contains("CAM") || source.Contains("CAMERA") || source.Contains("CCTV"))
        {
            return "CAM";
        }

        if (source.Contains("ROBOT") || source.Contains("TB3") || source.Contains("PATROL") ||
            source.Contains("CHARGING") || source.Contains("BATTERY"))
        {
            return "ROBOT";
        }

        if (source.Contains("NAV") || source.Contains("SYSTEM") || source.Contains("WS") || source.Contains("SERVER"))
        {
            return "SYSTEM";
        }

        return "INFO";
    }

    public void SetBackToDashboardVisible(bool visible)
    {
        EnsureDashboardReferences();

        if (buttonBackToDashboardObject != null)
        {
            buttonBackToDashboardObject.SetActive(visible);
        }
        else if (buttonBackToDashboard != null)
        {
            buttonBackToDashboard.gameObject.SetActive(visible);
        }
    }

    private void RefreshDashboardTimelineText()
    {
        EnsureDashboardReferences();

        if (textDashboardRecentTimelineBody == null)
        {
            return;
        }

        List<string> timelineLines = BuildDashboardRecentOperationalLogLines(MaxDashboardRecentLogLines);
        SetTextValueIfBound(textSelectedLogFilter, GetDashboardLogFilterTitle(selectedDashboardLogFilter));
        if (timelineLines.Count == 0)
        {
            SetTextValueIfBound(textDashboardRecentTimelineBody, GetDashboardLogEmptyMessage(selectedDashboardLogFilter));
            RefreshDashboardTimelineMarkers();
            return;
        }

        SetTextValueIfBound(textDashboardRecentTimelineBody, string.Join("\n", timelineLines));
        RefreshDashboardTimelineMarkers();
    }

    private static string GetDashboardLogFilterTitle(DashboardLogFilter selectedFilter)
    {
        return selectedFilter switch
        {
            DashboardLogFilter.Robot => "로봇 로그 · 최신 5건",
            DashboardLogFilter.Control => "제어 로그 · 최신 5건",
            DashboardLogFilter.Camera => "카메라 로그 · 최신 5건",
            DashboardLogFilter.System => "시스템 로그 · 최신 5건",
            DashboardLogFilter.Error => "오류 로그 · 최신 5건",
            _ => "전체 로그 · 최신 5건"
        };
    }

    private static string GetDashboardLogEmptyMessage(DashboardLogFilter selectedFilter)
    {
        return selectedFilter switch
        {
            DashboardLogFilter.Robot => "로봇 로그 없음",
            DashboardLogFilter.Control => "제어 로그 없음",
            DashboardLogFilter.Camera => "카메라 로그 없음",
            DashboardLogFilter.System => "시스템 로그 없음",
            DashboardLogFilter.Error => "오류 로그 없음",
            _ => "로그 없음"
        };
    }

    private List<string> BuildDashboardRecentOperationalLogLines(int maxCount)
    {
        List<string> lines = new();
        DashboardLogFilter selectedCategory = selectedDashboardLogFilter;
        int limit = Mathf.Max(1, maxCount);

        for (int i = eventLogLines.Count - 1; i >= 0 && lines.Count < limit; i--)
        {
            string line = eventLogLines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!MatchesDashboardLogFilter(line, selectedCategory))
            {
                continue;
            }

            lines.Add(line.Trim());
        }

        return lines;
    }

    private static DashboardLogFilter NormalizeDashboardLogFilterCategory(string category)
    {
        string normalized = string.IsNullOrWhiteSpace(category) ? "ALL" : category.Trim().ToUpperInvariant();
        return normalized switch
        {
            "ROBOT" => DashboardLogFilter.Robot,
            "CONTROL" => DashboardLogFilter.Control,
            "CAMERA" => DashboardLogFilter.Camera,
            "SYSTEM" => DashboardLogFilter.System,
            "ERROR" => DashboardLogFilter.Error,
            _ => DashboardLogFilter.All
        };
    }

    private static bool MatchesDashboardLogFilter(string line, DashboardLogFilter selectedCategory)
    {
        if (selectedCategory == DashboardLogFilter.All)
        {
            return true;
        }

        string category = ExtractOperationalLogCategory(line);
        return selectedCategory switch
        {
            DashboardLogFilter.Robot => string.Equals(category, "로봇", StringComparison.OrdinalIgnoreCase),
            DashboardLogFilter.Control => string.Equals(category, "제어", StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(category, "명령", StringComparison.OrdinalIgnoreCase),
            DashboardLogFilter.Camera => string.Equals(category, "카메라", StringComparison.OrdinalIgnoreCase),
            DashboardLogFilter.System => string.Equals(category, "시스템", StringComparison.OrdinalIgnoreCase),
            DashboardLogFilter.Error => string.Equals(category, "오류", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private static string ExtractOperationalLogCategory(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return string.Empty;
        }

        int firstClose = line.IndexOf(']');
        if (firstClose < 0)
        {
            return string.Empty;
        }

        int secondOpen = line.IndexOf('[', firstClose + 1);
        if (secondOpen < 0)
        {
            return string.Empty;
        }

        int secondClose = line.IndexOf(']', secondOpen + 1);
        if (secondClose <= secondOpen)
        {
            return string.Empty;
        }

        return line.Substring(secondOpen + 1, secondClose - secondOpen - 1).Trim();
    }

    private static bool ContainsAny(string source, params string[] tokens)
    {
        if (string.IsNullOrWhiteSpace(source) || tokens == null)
        {
            return false;
        }

        foreach (string token in tokens)
        {
            if (!string.IsNullOrWhiteSpace(token) &&
                source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private List<string> BuildDashboardTimelineLinesForSelectedRobot(int maxCount)
    {
        List<string> lines = new();
        string robotId = NormalizeRobotKey(selectedRobotId);
        if (!robotTimelineEntriesById.TryGetValue(robotId, out List<RobotTimelineViewEntry> entries) || entries.Count == 0)
        {
            return lines;
        }

        List<RobotTimelineViewEntry> sorted = new(entries);
        sorted.Sort(CompareRobotTimelineEntriesDescending);
        int count = Mathf.Min(Mathf.Max(0, maxCount), sorted.Count);
        for (int i = 0; i < count; i++)
        {
            RobotTimelineViewEntry entry = sorted[i];
            string timeText = FormatServerTimestampHourMinute(entry.Timestamp);
            string stateText = FormatServerStateForDisplay(entry.State);
            string reasonText = NormalizeDashValue(entry.PauseReason);
            lines.Add(reasonText == "--"
                ? $"[{timeText}] {stateText}"
                : $"[{timeText}] {stateText} · {reasonText}");
        }

        return lines;
    }

    private static string FormatServerTimestampHourMinute(string timestamp)
    {
        if (string.IsNullOrWhiteSpace(timestamp))
        {
            return "--";
        }

        string trimmed = timestamp.Trim();
        if (DateTime.TryParse(trimmed, out DateTime parsed))
        {
            return parsed.ToString("HH:mm");
        }

        return trimmed.Length >= 5 ? trimmed.Substring(0, 5) : trimmed;
    }

    public void SetCameraViewVisible(bool visible)
    {
        if (panelMainCameraView != null)
        {
            panelMainCameraView.SetActive(visible);
        }
    }

    public void ApplyViolationAlertFromServer(int violationId, string violationType, string employeeId, string detectedBy, int robotId, string robotLocation, string photoUrl, string confidence, string detectionBox, string alertMessage = null, string eventTimestamp = null, string alertStatus = "NEW", string cameraId = null)
    {
        string eventName = string.IsNullOrWhiteSpace(violationType) ? "VIOLATION" : violationType.Trim().ToUpperInvariant();
        string source = string.IsNullOrWhiteSpace(detectedBy) ? "Unknown" : detectedBy;
        string eventDisplay = FormatIncidentTypeForKoreanDisplay(eventName);
        ActiveAlertItem item = BuildActiveAlertItem(violationId, eventName, robotId, detectedBy, robotLocation, photoUrl, confidence, alertMessage, eventTimestamp, alertStatus, employeeId, null, cameraId);

        currentAlertId = violationId;
        currentAlertType = "VIOLATION";
        lastServerEvent = "violation_alert";
        currentAiEvent = eventName;
        currentSeverity = "WARNING";
        currentCameraLocation = item.LocationDisplay;
        currentConfidence = string.IsNullOrWhiteSpace(confidence) ? "-" : confidence;
        currentPhotoUrl = string.IsNullOrWhiteSpace(photoUrl) ? "-" : photoUrl;
        currentDetectionBox = string.IsNullOrWhiteSpace(detectionBox) ? "-" : detectionBox;
        currentObstacleSource = source;
        currentServerVerdict = "ALERT";
        currentEventAlertRobotDisplay = ShouldDisplayIncidentAsGlobal(robotId, detectedBy) ? "GLOBAL" : FormatAlertRobotDisplay(robotId);
        currentEventAlertLocationDisplay = item.LocationDisplay;
        currentEventAlertConfidenceDisplay = FormatConfidenceForKoreanDisplay(confidence);
        currentEventAlertMessageDisplay = string.IsNullOrWhiteSpace(alertMessage) ? "--" : alertMessage.Trim();
        IncrementIncidentSummaryCount(eventName);
        bool isNewRealtimeAlert = item.LogId > 0 && !incidentHistoryByLogId.ContainsKey(item.LogId);

        SetEventAlert(eventDisplay, "WARNING", "None", eventDisplay, currentEventAlertMessageDisplay, "ACK or dispatch robot", item.Timestamp);
        SetCameraDetail(currentAiEvent, currentSeverity, currentCameraLocation, currentConfidence, currentPhotoUrl, currentDetectionBox, currentObstacleSource, currentServerVerdict);
        SetEventMarkerVisible(true);
        RefreshCameraViewSnapshot(item);
        AddOrUpdateActiveAlert(item, false);
        if (isNewRealtimeAlert)
        {
            ShowRealtimeAlertPopup(item);
        }
        AppendIncidentToTodayEvents(item);
        RefreshAllStatusTexts();
        UpdateDashboardFromAlert();
        AddEventLog("ALERT", $"{eventName} {currentEventAlertRobotDisplay} location={item.LocationDisplay}", eventTimestamp);
    }

    public void ApplyEmergencyAlertFromServer(int emergencyId, string emergencyType, string detectedBy, int robotId, string robotLocation, string photoUrl, string confidence, string detectionBox, string alertMessage = null, string eventTimestamp = null, string alertStatus = "NEW", string cameraId = null)
    {
        string eventName = string.IsNullOrWhiteSpace(emergencyType) ? "EMERGENCY" : emergencyType.Trim().ToUpperInvariant();
        string source = string.IsNullOrWhiteSpace(detectedBy) ? "Unknown" : detectedBy;
        string eventDisplay = FormatIncidentTypeForKoreanDisplay(eventName);
        ActiveAlertItem item = BuildActiveAlertItem(emergencyId, eventName, robotId, detectedBy, robotLocation, photoUrl, confidence, alertMessage, eventTimestamp, alertStatus, null, null, cameraId);

        currentAlertId = emergencyId;
        currentAlertType = "EMERGENCY";
        lastServerEvent = "emergency_alert";
        currentAiEvent = eventName;
        currentSeverity = "CRITICAL";
        currentCameraLocation = item.LocationDisplay;
        currentConfidence = string.IsNullOrWhiteSpace(confidence) ? "-" : confidence;
        currentPhotoUrl = string.IsNullOrWhiteSpace(photoUrl) ? "-" : photoUrl;
        currentDetectionBox = string.IsNullOrWhiteSpace(detectionBox) ? "-" : detectionBox;
        currentObstacleSource = source;
        currentServerVerdict = "EMERGENCY";
        currentEventAlertRobotDisplay = ShouldDisplayIncidentAsGlobal(robotId, detectedBy) ? "GLOBAL" : FormatAlertRobotDisplay(robotId);
        currentEventAlertLocationDisplay = item.LocationDisplay;
        currentEventAlertConfidenceDisplay = FormatConfidenceForKoreanDisplay(confidence);
        currentEventAlertMessageDisplay = string.IsNullOrWhiteSpace(alertMessage) ? "--" : alertMessage.Trim();
        IncrementIncidentSummaryCount(eventName);
        bool isNewRealtimeAlert = item.LogId > 0 && !incidentHistoryByLogId.ContainsKey(item.LogId);

        SetEventAlert(eventDisplay, "CRITICAL", "None", eventDisplay, currentEventAlertMessageDisplay, "Check camera and stop robot if needed", item.Timestamp);
        SetCameraDetail(currentAiEvent, currentSeverity, currentCameraLocation, currentConfidence, currentPhotoUrl, currentDetectionBox, currentObstacleSource, currentServerVerdict);
        SetEventMarkerVisible(true);
        RefreshCameraViewSnapshot(item);
        AddOrUpdateActiveAlert(item, false);
        if (isNewRealtimeAlert)
        {
            ShowRealtimeAlertPopup(item);
        }
        AppendIncidentToTodayEvents(item);
        RefreshAllStatusTexts();
        UpdateDashboardFromAlert();
        AddEventLog("CRITICAL", $"{eventName} {currentEventAlertRobotDisplay} location={item.LocationDisplay}", eventTimestamp);
    }

    public void ApplyEmployeeAttendanceFromServer(string employeeId, string employeeName, string actionType, string timestamp, bool reloadTodaySummary = true)
    {
        string id = NormalizeAccessId(employeeId, "UNKNOWN_EMPLOYEE");
        string name = string.IsNullOrWhiteSpace(employeeName) ? "-" : employeeName.Trim();
        string action = string.IsNullOrWhiteSpace(actionType) ? "unknown" : actionType.Trim().ToLowerInvariant();
        if (IsDuplicateAccessEvent("employee", id, action, timestamp))
        {
            return;
        }

        employeeAttendanceStateById.TryGetValue(id, out string currentState);
        string previousState = string.IsNullOrWhiteSpace(currentState) ? "outside" : currentState;
        if (action == "check_in")
        {
            if (currentState == "inside")
            {
                Debug.Log("[PeopleStatus] Employee check_in ignored: already inside.");
                return;
            }

            employeeAttendanceStateById[id] = "inside";
            personnel3DMarkerController?.ApplyEmployeeAttendance(id, name, action);
            RefreshFactory2DPeopleMarkers();
            Debug.Log($"[PeopleStatus] Employee state {previousState} -> inside. Dashboard counts wait for server today-summary.");
        }
        else if (action == "check_out")
        {
            if (currentState == "checked_out")
            {
                Debug.Log("[PeopleStatus] Employee check_out ignored: already checked_out.");
                return;
            }

            employeeAttendanceStateById[id] = "checked_out";
            personnel3DMarkerController?.ApplyEmployeeAttendance(id, name, action);
            RefreshFactory2DPeopleMarkers();
            Debug.Log($"[PeopleStatus] Employee state {previousState} -> checked_out. Dashboard counts wait for server today-summary.");
        }
        else
        {
            Debug.LogWarning($"[PeopleStatus] Unsupported employee action={action}");
            return;
        }

        RefreshTodaySummaryText();
        if (reloadTodaySummary)
        {
            RequestTodaySummaryReloadFromServerAccessEvent();
        }

        lastAccessEvent = FormatLastAccessLabel(id, action, timestamp, false);
        RefreshPersonnelStatusTexts();
        string message = $"[ATTENDANCE] employee={id} name={name} action={action}";
        AppendTodayEvent(FormatEmployeeAttendanceRecentEvent(action), timestamp);
        AddDashboardTimelineEvent("SYSTEM", message);
        AddEventLog("ATTENDANCE", message, timestamp);
        Debug.Log($"[PeopleStatus] ACCESS updated from employee WebSocket. summaryIn={FormatSummaryValuePlain(summaryAttendanceCurrentIn, hasSummaryAttendanceCurrentIn)} summaryOut={FormatSummaryValuePlain(summaryAttendanceOut, hasSummaryAttendanceOut)}");
    }

    public void ApplyVisitorAttendanceFromServer(string visitorId, string visitorName, string actionType, string timestamp, bool reloadTodaySummary = true)
    {
        string id = NormalizeAccessId(visitorId, "VISITOR-UNKNOWN");
        string name = string.IsNullOrWhiteSpace(visitorName) ? "-" : visitorName.Trim();
        string action = string.IsNullOrWhiteSpace(actionType) ? "unknown" : actionType.Trim().ToLowerInvariant();
        if (IsDuplicateAccessEvent("visitor", id, action, timestamp))
        {
            return;
        }

        visitorAttendanceStateById.TryGetValue(id, out string currentState);
        if (action == "entry")
        {
            if (currentState == "inside")
            {
                Debug.Log("[PeopleStatus] Visitor entry ignored: already inside.");
                return;
            }

            visitorAttendanceStateById[id] = "inside";
            personnel3DMarkerController?.ApplyVisitorAttendance(id, name, action);
            RefreshFactory2DPeopleMarkers();
            Debug.Log("[PeopleStatus] Visitor entered. Dashboard visitor count waits for server today-summary.");
        }
        else if (action == "exit")
        {
            if (currentState == "outside")
            {
                Debug.Log("[PeopleStatus] Visitor exit ignored: already outside.");
                return;
            }

            visitorAttendanceStateById[id] = "outside";
            personnel3DMarkerController?.ApplyVisitorAttendance(id, name, action);
            RefreshFactory2DPeopleMarkers();
            Debug.Log("[PeopleStatus] Visitor exited. Dashboard visitor count waits for server today-summary.");
        }
        else
        {
            Debug.LogWarning($"[PeopleStatus] Unsupported visitor action={action}");
            return;
        }

        RefreshTodaySummaryText();
        if (reloadTodaySummary)
        {
            RequestTodaySummaryReloadFromServerAccessEvent();
        }

        lastAccessEvent = FormatLastAccessLabel(id, action, timestamp, true);
        RefreshPersonnelStatusTexts();
        string message = $"[VISITOR] visitor={id} name={name} action={action}";
        AppendTodayEvent(FormatVisitorAttendanceRecentEvent(action), timestamp);
        AddDashboardTimelineEvent("SYSTEM", message);
        AddEventLog("VISITOR", message, timestamp);
        Debug.Log($"[PeopleStatus] ACCESS updated from visitor WebSocket. summaryVisitors={FormatSummaryValuePlain(summaryVisitorTotal, hasSummaryVisitorTotal)}");
    }

    private bool IsDuplicateAccessEvent(string category, string id, string action, string timestamp)
    {
        string safeTimestamp = string.IsNullOrWhiteSpace(timestamp) ? DateTime.Now.ToString("yyyy-MM-ddTHH:mm") : timestamp.Trim();
        string key = $"{category}|{id}|{action}|{safeTimestamp}";
        if (processedAccessEventKeys.Contains(key))
        {
            return true;
        }

        processedAccessEventKeys.Add(key);
        return false;
    }

    private static string NormalizeAccessId(string rawId, string fallback)
    {
        return string.IsNullOrWhiteSpace(rawId) ? fallback : rawId.Trim();
    }

    public void SimulateEmployeeAttendance(string employeeId, string name, string actionType)
    {
        string id = string.IsNullOrWhiteSpace(employeeId) ? "TEST-001" : employeeId.Trim();
        string action = string.IsNullOrWhiteSpace(actionType) ? "check_in" : actionType.Trim().ToLowerInvariant();
        string timestamp = $"TEST-{DateTime.Now.Ticks}";
        Debug.Log($"[PeopleTest] Employee action={action}");
        ApplyEmployeeAttendanceFromServer(id, string.IsNullOrWhiteSpace(name) ? id : name, action, timestamp, false);
    }

    public void SimulateVisitorAttendance(string visitorId, string name, string actionType)
    {
        string id = string.IsNullOrWhiteSpace(visitorId) ? "VISITOR-001" : visitorId.Trim();
        string action = string.IsNullOrWhiteSpace(actionType) ? "entry" : actionType.Trim().ToLowerInvariant();
        string timestamp = $"TEST-{DateTime.Now.Ticks}";
        Debug.Log($"[PeopleTest] Visitor action={action}");
        ApplyVisitorAttendanceFromServer(id, string.IsNullOrWhiteSpace(name) ? id : name, action, timestamp, false);
    }

    public void ResetPersonnelStatusForTest()
    {
        lastAccessEvent = "-";
        processedAccessEventKeys.Clear();
        employeeAttendanceStateById.Clear();
        visitorAttendanceStateById.Clear();
        staffEntranceBarrierController?.Close();
        RefreshTodaySummaryText();
        RefreshPersonnelStatusTexts();
        Debug.Log("[PeopleTest] Reset Personnel Test State");
    }

    private ActiveAlertItem BuildActiveAlertItem(int logId, string incidentType, int robotId, string detectedBy, string robotLocation, string photoUrl, string confidence, string message, string timestamp, string status, string employeeId = null, string clearedAt = null, string cameraId = null)
    {
        string normalizedIncident = string.IsNullOrWhiteSpace(incidentType) ? "UNKNOWN" : incidentType.Trim().ToUpperInvariant();
        bool hasLocation = TryParseIncidentLocation(robotLocation, out float locationX, out float locationY) &&
                           IsFiniteAlertCoordinate(locationX) &&
                           IsFiniteAlertCoordinate(locationY);
        ActiveAlertItem item = new ActiveAlertItem
        {
            LogId = logId,
            IncidentType = normalizedIncident,
            RobotNumericId = robotId,
            RobotDisplay = ShouldDisplayIncidentAsGlobal(robotId, detectedBy) ? "GLOBAL" : FormatRobotIdUpper(ConvertRobotId(robotId)),
            DetectedBy = string.IsNullOrWhiteSpace(detectedBy) ? "--" : detectedBy.Trim(),
            EmployeeId = string.IsNullOrWhiteSpace(employeeId) ? "--" : employeeId.Trim(),
            LocationDisplay = BuildAlertLocationDisplay(hasLocation, locationX, locationY),
            ConfidenceDisplay = FormatConfidenceForKoreanDisplay(confidence),
            Timestamp = timestamp,
            Message = string.IsNullOrWhiteSpace(message) ? "--" : message.Trim(),
            PhotoUrl = string.IsNullOrWhiteSpace(photoUrl) ? string.Empty : photoUrl.Trim(),
            Status = string.IsNullOrWhiteSpace(status) ? "NEW" : status.Trim().ToUpperInvariant(),
            CameraId = string.IsNullOrWhiteSpace(cameraId) ? "--" : cameraId.Trim(),
            ClearedAt = string.IsNullOrWhiteSpace(clearedAt) ? string.Empty : clearedAt.Trim()
        };

        if (hasLocation)
        {
            item.LocationX = locationX;
            item.LocationY = locationY;
            item.HasLocation = true;
        }

        return item;
    }

    private void AddOrUpdateActiveAlert(ActiveAlertItem item, bool allowAutoOpenDetail)
    {
        AddOrUpdateIncidentHistory(item);
        if (item == null || item.LogId <= 0 || !IsActiveAlertStatus(item.Status))
        {
            RefreshFactoryIncidentMarkers();
            RefreshAlertListPopup();
            RefreshRobotViewPanel();
            return;
        }

        bool wasEmpty = activeAlertLogIds.Count == 0;
        activeAlertsByLogId[item.LogId] = item;
        if (!activeAlertLogIds.Contains(item.LogId))
        {
            activeAlertLogIds.Add(item.LogId);
        }

        if (selectedAlertLogId <= 0 || !activeAlertsByLogId.ContainsKey(selectedAlertLogId))
        {
            selectedAlertLogId = item.LogId;
        }

        RefreshAlertQueueUi();
        RefreshFactoryIncidentMarkers();
        bool detailOpen = IsAlertDetailPopupOpen();
        bool listOpen = IsAlertListPopupOpen();
        if (allowAutoOpenDetail && !detailOpen && !listOpen && !suppressQueuePopupAutoOpen)
        {
            SelectAlertAndShowDetail(item.LogId);
        }
        else if (wasEmpty || selectedAlertLogId == item.LogId)
        {
            RefreshSelectedAlertDisplays();
        }

        RefreshRobotViewPanel();
    }

    private void AddOrUpdateIncidentHistory(ActiveAlertItem item)
    {
        if (item == null || item.LogId <= 0)
        {
            return;
        }

        if (incidentHistoryByLogId.TryGetValue(item.LogId, out ActiveAlertItem existing) &&
            IsBlankAlertValue(item.Message) &&
            !IsBlankAlertValue(existing.Message))
        {
            item.Message = existing.Message;
        }

        incidentHistoryByLogId[item.LogId] = item;
        if (!incidentHistoryLogIds.Contains(item.LogId))
        {
            incidentHistoryLogIds.Add(item.LogId);
        }
    }

    private static bool IsActiveAlertStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        string normalized = status.Trim().ToUpperInvariant();
        return normalized == "NEW" || normalized == "ACKNOWLEDGED";
    }

    private static bool IsClearedAlertStatus(string status)
    {
        string normalized = string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim().ToUpperInvariant();
        return normalized == "CLEARED";
    }

    private static bool IsAcknowledgedAlertStatus(string status)
    {
        string normalized = string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim().ToUpperInvariant();
        return normalized == "ACKNOWLEDGED";
    }

    private void RestoreActiveAlertsFromIncidentRecords(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        try
        {
            string json = body.Trim();
            if (json.StartsWith("[", StringComparison.Ordinal))
            {
                json = "{\"records\":" + json + "}";
            }

            IncidentRecordsResponse response = JsonUtility.FromJson<IncidentRecordsResponse>(json);
            IncidentRecordItem[] records = response?.records ??
                                           response?.data?.records ??
                                           response?.data?.incidents ??
                                           response?.data?.items;
            if (records == null)
            {
                return;
            }

            suppressQueuePopupAutoOpen = true;
            List<ActiveAlertItem> restoredIncidentItems = new();
            foreach (IncidentRecordItem record in records)
            {
                if (record == null)
                {
                    continue;
                }

                int logId = record.alert_id > 0 ? record.alert_id : (record.log_id > 0 ? record.log_id : record.id);
                ActiveAlertItem item = BuildActiveAlertItem(
                    logId,
                    record.incident_type,
                    record.robot_id,
                    record.detected_by,
                    FormatIncidentLocation(record.location_x, record.location_y),
                    BuildAbsoluteDashboardUrl(record.photo_url),
                    GetIncidentConfidenceValue(record),
                    record.message,
                    FirstNonEmptyMapStatusValue(record.detected_at, record.timestamp),
                    record.status,
                    record.employee_id,
                    record.cleared_at,
                    record.camera_id);

                if (IsActiveAlertStatus(item.Status))
                {
                    AddOrUpdateActiveAlert(item, false);
                }
                else if (IsClearedAlertStatus(item.Status))
                {
                    AddOrUpdateIncidentHistory(item);
                }

                restoredIncidentItems.Add(item);
            }

            restoredIncidentItems.Sort(CompareAlertItemsByTimestampAscending);
            foreach (ActiveAlertItem item in restoredIncidentItems)
            {
                AppendIncidentToTodayEvents(item);
            }
        }
        catch (Exception exception)
        {
            AddEventLog("API", $"incident records parse warning: {exception.Message}");
        }
        finally
        {
            suppressQueuePopupAutoOpen = false;
            cameraViewDirty = true;
            RefreshAlertQueueUi();
            RefreshSelectedAlertDisplays();
            RefreshFactoryIncidentMarkers();
        }
    }

    private string BuildAbsoluteDashboardUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        string trimmed = url.Trim();
        if (trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        string baseUrl = string.IsNullOrWhiteSpace(dashboardServerBaseUrl)
            ? "http://127.0.0.1:8000"
            : dashboardServerBaseUrl.TrimEnd('/');
        return trimmed.StartsWith("/", StringComparison.Ordinal) ? baseUrl + trimmed : baseUrl + "/" + trimmed;
    }

    private void RefreshAlertQueueUi()
    {
        EnsureAlertQueueReferences();
        RefreshRightEventAlertFromQueue();
        RefreshAlertListPopup();
        RefreshPopupIndexAndButtons();
    }

    private void RefreshSelectedAlertDisplays()
    {
        if (selectedAlertLogId > 0 && activeAlertsByLogId.TryGetValue(selectedAlertLogId, out ActiveAlertItem item))
        {
            ApplyAlertItemToRightPanel(item);
            if (IsAlertDetailPopupOpen())
            {
                ApplyAlertItemToDetailPopup(item);
            }
        }
        else
        {
            RefreshRightEventAlertFromQueue();
        }

        RefreshPopupIndexAndButtons();
    }

    private void RefreshRightEventAlertFromQueue()
    {
        EnsureAlertQueueReferences();
        int count = activeAlertLogIds.Count;
        SetTextValueIfBound(textEventAlertPendingCount, $"미조치 {count}건");
        SetTextValueIfBound(textEventAlertIndex, count > 0 ? $"{GetSelectedAlertDisplayIndex()} / {count}" : "-- / --");

        bool hasAlerts = count > 0;
        if (buttonEventAlertPrev != null) buttonEventAlertPrev.interactable = count > 1;
        if (buttonEventAlertNext != null) buttonEventAlertNext.interactable = count > 1;
        if (buttonEventAlertDetail != null) buttonEventAlertDetail.interactable = hasAlerts;

        if (!hasAlerts)
        {
            SetEventAlert("--", "--", "--", "--", "--", "--");
            return;
        }

        if (selectedAlertLogId <= 0 || !activeAlertsByLogId.ContainsKey(selectedAlertLogId))
        {
            selectedAlertLogId = activeAlertLogIds[0];
        }

        ApplyAlertItemToRightPanel(activeAlertsByLogId[selectedAlertLogId]);
    }

    private void ApplyAlertItemToRightPanel(ActiveAlertItem item)
    {
        if (item == null)
        {
            return;
        }

        string eventDisplay = FormatIncidentTypeForKoreanDisplay(item.IncidentType);
        currentEventAlertRobotDisplay = item.RobotDisplay;
        currentEventAlertLocationDisplay = item.LocationDisplay;
        currentEventAlertConfidenceDisplay = item.ConfidenceDisplay;
        currentEventAlertMessageDisplay = BuildIncidentDisplayMessage(item);
        SetEventAlert(eventDisplay, "WARNING", item.DetectedBy, eventDisplay, currentEventAlertMessageDisplay, "ACK or clear", item.Timestamp);
        SetTextValueIfBound(textEventAlertIndex, $"{GetSelectedAlertDisplayIndex()} / {activeAlertLogIds.Count}");
    }

    private void RefreshAlertListPopup()
    {
        EnsureAlertQueueReferences();
        int pendingCount = CountPendingIncidentHistory();
        int clearedCount = CountIncidentHistoryByStatus("CLEARED");
        List<int> filteredLogIds = GetFilteredIncidentLogIds();
        SetTextValueIfBound(textPopupPendingCount, $"{filteredLogIds.Count}건");
        SetFilterButtonLabel(buttonAlertFilterPending, $"미조치 {pendingCount}");
        SetFilterButtonLabel(buttonAlertFilterCleared, $"조치 완료 {clearedCount}");

        if (alertListContent == null || buttonAlertListItemTemplate == null)
        {
            return;
        }

        for (int i = alertListContent.childCount - 1; i >= 0; i--)
        {
            Transform child = alertListContent.GetChild(i);
            if (child != null && child.gameObject != buttonAlertListItemTemplate.gameObject)
            {
                Destroy(child.gameObject);
            }
        }

        buttonAlertListItemTemplate.gameObject.SetActive(false);
        bool hasFilteredItems = filteredLogIds.Count > 0;
        if (textPopupListMessage != null)
        {
            textPopupListMessage.gameObject.SetActive(!hasFilteredItems);
            textPopupListMessage.text = string.Equals(currentAlertListFilter, "CLEARED", StringComparison.OrdinalIgnoreCase)
                ? "조치 완료된 알림이 없습니다."
                : "미조치 알림이 없습니다.";
        }

        foreach (int logId in filteredLogIds)
        {
            if (!incidentHistoryByLogId.TryGetValue(logId, out ActiveAlertItem item))
            {
                continue;
            }

            Button row = Instantiate(buttonAlertListItemTemplate, alertListContent);
            row.name = $"Button_AlertListItem_{logId}";
            row.gameObject.SetActive(true);
            int capturedLogId = logId;
            row.onClick.RemoveAllListeners();
            row.onClick.AddListener(() => SelectAlertAndShowDetail(capturedLogId));
            SetChildText(row.transform, "Text_AlertListItemTitle", FormatIncidentTypeForKoreanDisplay(item.IncidentType));
            SetAlertListItemMetaAndStatusText(
                row.transform,
                $"{item.RobotDisplay} · {FormatAlertTime(item.Timestamp)}",
                IsClearedAlertStatus(item.Status) ? "조치 완료" : (IsAcknowledgedAlertStatus(item.Status) || acknowledgedAlertLogIds.Contains(logId) ? "확인됨" : "미확인"));
            SetChildText(row.transform, "Text_AlertListItemArrow", "상세");
            RefreshAlertListItemIcons(row.transform, item, IsAcknowledgedAlertStatus(item.Status) || acknowledgedAlertLogIds.Contains(logId));
        }
    }

    private void SetAlertListFilter(string filter)
    {
        currentAlertListFilter = string.Equals(filter, "CLEARED", StringComparison.OrdinalIgnoreCase) ? "CLEARED" : "NEW";
        RefreshAlertListPopup();
        SetAlertListScrollTop();
        RefreshPopupIndexAndButtons();
    }

    private int CountIncidentHistoryByStatus(string status)
    {
        int count = 0;
        foreach (int logId in incidentHistoryLogIds)
        {
            if (incidentHistoryByLogId.TryGetValue(logId, out ActiveAlertItem item) &&
                string.Equals(item.Status, status, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    private int CountPendingIncidentHistory()
    {
        int count = 0;
        foreach (int logId in incidentHistoryLogIds)
        {
            if (incidentHistoryByLogId.TryGetValue(logId, out ActiveAlertItem item) &&
                IsActiveAlertStatus(item.Status))
            {
                count++;
            }
        }

        return count;
    }

    private int GetCurrentAlertListFilterCount()
    {
        return CountIncidentHistoryByStatus(currentAlertListFilter);
    }

    private List<int> GetFilteredIncidentLogIds()
    {
        List<int> ids = new();
        foreach (int logId in incidentHistoryLogIds)
        {
            if (!incidentHistoryByLogId.TryGetValue(logId, out ActiveAlertItem item))
            {
                continue;
            }

            bool matches = string.Equals(currentAlertListFilter, "CLEARED", StringComparison.OrdinalIgnoreCase)
                ? IsClearedAlertStatus(item.Status)
                : IsActiveAlertStatus(item.Status);
            if (matches)
            {
                ids.Add(logId);
            }
        }

        return ids;
    }

    private void SetFilterButtonLabel(Button button, string label)
    {
        if (button == null)
        {
            return;
        }

        TMP_Text[] texts = button.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text text in texts)
        {
            if (text != null)
            {
                text.text = label;
                return;
            }
        }
    }

    private void SelectPreviousActiveAlert()
    {
        MoveSelectedAlert(-1);
    }

    private void SelectNextActiveAlert()
    {
        MoveSelectedAlert(1);
    }

    private void MoveSelectedAlert(int delta)
    {
        int count = activeAlertLogIds.Count;
        if (count == 0)
        {
            RefreshAlertQueueUi();
            return;
        }

        int index = Mathf.Max(0, activeAlertLogIds.IndexOf(selectedAlertLogId));
        index = (index + delta + count) % count;
        selectedAlertLogId = activeAlertLogIds[index];
        RefreshSelectedAlertDisplays();
        RefreshAlertListPopup();
    }

    private void ShowSelectedAlertDetail()
    {
        if (selectedAlertLogId > 0)
        {
            SelectAlertAndShowDetail(selectedAlertLogId);
        }
    }

    private void SelectAlertAndShowDetail(int logId)
    {
        if (!incidentHistoryByLogId.ContainsKey(logId))
        {
            return;
        }

        selectedAlertLogId = logId;
        if (panelPopupList != null)
        {
            panelPopupList.SetActive(false);
        }

        if (alertListRootObject != null && alertListRootObject != popupLayerObject)
        {
            alertListRootObject.SetActive(false);
        }

        ApplyAlertItemToDetailPopup(incidentHistoryByLogId[logId]);
        RefreshAlertQueueUi();
    }

    private void ShowRealtimeAlertPopup(ActiveAlertItem item)
    {
        if (item == null || item.LogId <= 0 || !IsActiveAlertStatus(item.Status))
        {
            return;
        }

        EnsurePopupReferences();
        if (popupAlertMessage == null)
        {
            pendingRealtimeAlertPopupLogId = item.LogId;
            if (isActiveAndEnabled && !pendingRealtimeAlertPopupRetryScheduled)
            {
                pendingRealtimeAlertPopupRetryScheduled = true;
                StartCoroutine(TryShowPendingRealtimeAlertPopupNextFrame());
            }
            return;
        }

        pendingRealtimeAlertPopupLogId = 0;
        selectedAlertLogId = item.LogId;
        if (panelPopupList != null)
        {
            panelPopupList.SetActive(false);
        }

        if (alertListRootObject != null && alertListRootObject != popupLayerObject)
        {
            alertListRootObject.SetActive(false);
        }

        ApplyAlertItemToDetailPopup(item);
    }

    private void TryShowPendingRealtimeAlertPopup()
    {
        if (pendingRealtimeAlertPopupLogId <= 0 ||
            !activeAlertsByLogId.TryGetValue(pendingRealtimeAlertPopupLogId, out ActiveAlertItem item))
        {
            return;
        }

        ShowRealtimeAlertPopup(item);
    }

    private IEnumerator TryShowPendingRealtimeAlertPopupNextFrame()
    {
        yield return null;
        pendingRealtimeAlertPopupRetryScheduled = false;
        TryShowPendingRealtimeAlertPopup();
    }

    private void ApplyAlertItemToDetailPopup(ActiveAlertItem item)
    {
        EnsurePopupReferences();
        if (item == null)
        {
            return;
        }

        currentAlertId = item.LogId;
        currentAlertType = item.IncidentType;
        currentPopupAlertType = item.IncidentType;
        currentPopupLevel = "WARNING";
        currentPopupRobotId = item.RobotDisplay;
        currentPopupLocation = item.LocationDisplay;
        currentPopupDetectedBy = item.DetectedBy;
        currentPopupConfidence = item.ConfidenceDisplay;
        currentPopupRecommendedAction = acknowledgedAlertLogIds.Contains(item.LogId) ? "조치 완료 가능" : "확인 필요";
        currentPopupLastMessage = BuildIncidentDisplayMessage(item);

        if (popupLayerObject != null) popupLayerObject.SetActive(true);
        if (popupAlertMessage != null) popupAlertMessage.SetActive(true);

        SetTextValueIfBound(textPopupTitle, "이상 상황 알림");
        SetTextValueIfBound(textAlertPopupIndex, $"{GetSelectedAlertListDisplayIndex()} / {GetCurrentAlertListFilterCount()}");

        bool isCleared = IsClearedAlertStatus(item.Status);
        bool isAcknowledged = IsAcknowledgedAlertStatus(item.Status) || acknowledgedAlertLogIds.Contains(item.LogId);
        string body =
            $"이벤트 종류 : {FormatIncidentTypeForKoreanDisplay(item.IncidentType)}\n" +
            $"관련 로봇 : {item.RobotDisplay}\n" +
            $"감지 카메라 : {NormalizeDashValue(item.CameraId)}\n" +
            $"감지 주체 : {NormalizeDashValue(item.DetectedBy)}\n" +
            $"발생 위치 : {item.LocationDisplay}\n" +
            $"신뢰도 : {item.ConfidenceDisplay}\n" +
            $"발생 시각 : {FormatUserFacingDateTime(item.Timestamp)}\n" +
            $"처리 상태 : {(isCleared ? "조치 완료" : (isAcknowledged ? "확인됨 · 미조치" : "미확인 · 미조치"))}\n" +
            $"알림 내용 : {currentPopupLastMessage}";
        SetTextValueIfBound(textPopupAlertBody, body);
        SetTextValueIfBound(textPopupMessage, body);

        if (buttonPopupAck != null)
        {
            buttonPopupAck.interactable = !isCleared && !isAcknowledged;
        }

        if (buttonPopupClear != null)
        {
            buttonPopupClear.interactable = !isCleared && isAcknowledged;
        }

        RefreshPopupSnapshot(item);
    }

    private void RefreshPopupSnapshot(ActiveAlertItem item)
    {
        EnsurePopupReferences();
        if (item == null)
        {
            SetPopupSnapshotVisual(null, "이벤트 이미지 없음");
            return;
        }

        if (TryGetCachedAlertSnapshot(item, out Sprite cachedSprite))
        {
            SetPopupSnapshotVisual(cachedSprite, "이벤트 이미지 표시");
            return;
        }

        if (IsMissingSnapshotPhotoUrl(item.PhotoUrl))
        {
            SetPopupSnapshotVisual(null, "이벤트 이미지 없음");
            return;
        }

        SetPopupSnapshotVisual(null, "이미지 불러오는 중");
        RequestAlertSnapshot(item.LogId, item.PhotoUrl);
    }

    private void RefreshCameraViewSnapshot(ActiveAlertItem item)
    {
        currentCameraSnapshotLogId = item != null ? item.LogId : 0;
        EnsureCameraViewSnapshotReference();
        if (item == null)
        {
            SetCameraSnapshotVisual(null, "이벤트 이미지 없음");
            return;
        }

        if (TryGetCachedAlertSnapshot(item, out Sprite cachedSprite))
        {
            SetCameraSnapshotVisual(cachedSprite, "이벤트 이미지 표시");
            return;
        }

        if (IsMissingSnapshotPhotoUrl(item.PhotoUrl))
        {
            SetCameraSnapshotVisual(null, "이벤트 이미지 없음");
            return;
        }

        if (failedAlertSnapshotIds.Contains(item.LogId))
        {
            SetCameraSnapshotVisual(null, "이벤트 이미지 수신 실패");
            return;
        }

        SetCameraSnapshotVisual(null, "이미지 불러오는 중");
        RequestAlertSnapshot(item.LogId, item.PhotoUrl);
    }

    private bool TryGetCachedAlertSnapshot(ActiveAlertItem item, out Sprite sprite)
    {
        sprite = null;
        if (item == null || !alertSnapshotSpritesByLogId.TryGetValue(item.LogId, out Sprite cachedSprite) || cachedSprite == null)
        {
            return false;
        }

        if (alertSnapshotPhotoUrlsByLogId.TryGetValue(item.LogId, out string cachedUrl) &&
            !IsMissingSnapshotPhotoUrl(item.PhotoUrl) &&
            !string.Equals(cachedUrl, item.PhotoUrl.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        sprite = cachedSprite;
        return true;
    }

    private void RequestAlertSnapshot(int logId, string photoUrl)
    {
        if (logId <= 0 || IsMissingSnapshotPhotoUrl(photoUrl) || loadingAlertSnapshotIds.Contains(logId) || failedAlertSnapshotIds.Contains(logId))
        {
            return;
        }

        loadingAlertSnapshotIds.Add(logId);
        StartCoroutine(LoadAlertSnapshotCoroutine(logId, photoUrl.Trim()));
    }

    private IEnumerator LoadAlertSnapshotCoroutine(int logId, string url)
    {
        using UnityWebRequest request = UnityWebRequestTexture.GetTexture(url);
        yield return request.SendWebRequest();
        loadingAlertSnapshotIds.Remove(logId);

        if (request.result == UnityWebRequest.Result.Success)
        {
            using (AlertImageLoadMarker.Auto())
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(request);
                if (texture != null)
                {
                    Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    alertSnapshotSpritesByLogId.TryGetValue(logId, out Sprite previousSprite);
                    alertSnapshotSpritesByLogId[logId] = sprite;
                    alertSnapshotPhotoUrlsByLogId[logId] = url;
                    failedAlertSnapshotIds.Remove(logId);
                    ApplyLoadedAlertSnapshot(logId, sprite);
                    if (previousSprite != null && previousSprite != sprite)
                    {
                        DestroyRuntimeSnapshotSprite(previousSprite);
                    }
                }
                else
                {
                    ApplyAlertSnapshotFailure(logId);
                }
            }
        }
        else
        {
            ApplyAlertSnapshotFailure(logId);
        }
    }

    private void ApplyLoadedAlertSnapshot(int logId, Sprite sprite)
    {
        cameraViewDirty = true;
        if (currentCameraSnapshotLogId == logId)
        {
            EnsureCameraViewSnapshotReference();
            SetCameraSnapshotVisual(sprite, "이벤트 이미지 표시");
        }

        if (selectedAlertLogId == logId)
        {
            EnsurePopupReferences();
            SetPopupSnapshotVisual(sprite, "이벤트 이미지 표시");
        }
    }

    private void ApplyAlertSnapshotFailure(int logId)
    {
        failedAlertSnapshotIds.Add(logId);
        cameraViewDirty = true;
        if (currentCameraSnapshotLogId == logId)
        {
            SetCameraSnapshotVisual(null, "이벤트 이미지 수신 실패");
        }

        if (selectedAlertLogId == logId)
        {
            SetPopupSnapshotVisual(null, "이벤트 이미지 수신 실패");
        }
    }

    private void SetCameraSnapshotVisual(Sprite sprite, string statusText)
    {
        if (imageEventSnapshotPlaceholder != null)
        {
            imageEventSnapshotPlaceholder.sprite = sprite;
        }

        if (rawImageEventSnapshotPlaceholder != null)
        {
            rawImageEventSnapshotPlaceholder.texture = sprite != null ? sprite.texture : null;
        }

        SetTextValueIfBound(textEventSnapshotPlaceholder, statusText);
    }

    private void SetPopupSnapshotVisual(Sprite sprite, string statusText)
    {
        if (popupSnapshotPlaceholderImage != null)
        {
            popupSnapshotPlaceholderImage.sprite = sprite;
        }

        if (popupSnapshotPlaceholderRawImage != null)
        {
            popupSnapshotPlaceholderRawImage.texture = sprite != null ? sprite.texture : null;
        }

        SetTextValueIfBound(textPopupSnapshotBody, statusText);
    }

    private static bool IsMissingSnapshotPhotoUrl(string photoUrl)
    {
        return string.IsNullOrWhiteSpace(photoUrl) || photoUrl.Trim() == "-";
    }

    private void DestroyRuntimeSnapshotSprite(Sprite sprite)
    {
        if (sprite == null)
        {
            return;
        }

        Texture2D texture = sprite.texture;
        Destroy(sprite);
        if (texture != null)
        {
            Destroy(texture);
        }
    }

    private void ReleaseAlertSnapshotCache()
    {
        HashSet<Texture2D> releasedTextures = new();
        foreach (Sprite sprite in alertSnapshotSpritesByLogId.Values)
        {
            if (sprite == null)
            {
                continue;
            }

            Texture2D texture = sprite.texture;
            Destroy(sprite);
            if (texture != null && releasedTextures.Add(texture))
            {
                Destroy(texture);
            }
        }

        alertSnapshotSpritesByLogId.Clear();
        alertSnapshotPhotoUrlsByLogId.Clear();
        loadingAlertSnapshotIds.Clear();
        failedAlertSnapshotIds.Clear();
    }

    private void ShowAlertListPopup()
    {
        EnsureAlertQueueReferences();
        alertListOpenedFromDetail = IsAlertDetailPopupOpen();
        currentAlertListFilter = activeAlertLogIds.Count > 0 || CountIncidentHistoryByStatus("CLEARED") == 0 ? "NEW" : "CLEARED";
        LogAlertListActiveState("before open");
        if (popupLayerObject != null) popupLayerObject.SetActive(true);
        if (alertListRootObject != null) alertListRootObject.SetActive(true);
        if (panelPopupList != null) panelPopupList.SetActive(true);
        EnsureActiveAncestorChain(panelPopupList != null ? panelPopupList.transform : alertListRootObject != null ? alertListRootObject.transform : null, popupLayerObject != null ? popupLayerObject.transform : null);
        if (popupAlertMessage != null &&
            !IsAncestorOrSelf(popupAlertMessage.transform, panelPopupList != null ? panelPopupList.transform : null) &&
            !IsAncestorOrSelf(popupAlertMessage.transform, alertListRootObject != null ? alertListRootObject.transform : null))
        {
            popupAlertMessage.SetActive(false);
        }
        SetTextValueIfBound(textPopupTitle, "알림 목록");
        RefreshAlertListPopup();
        SetAlertListScrollTop();
        LogAlertListActiveState("after open");
    }

    private void OpenAlertListFromDetailPopup()
    {
        ShowAlertListPopup();
        StartCoroutine(EnsureAlertListVisibleAfterClick());
    }

    private IEnumerator EnsureAlertListVisibleAfterClick()
    {
        yield return null;

        bool layerVisible = popupLayerObject == null || popupLayerObject.activeInHierarchy;
        bool rootVisible = alertListRootObject == null || alertListRootObject.activeInHierarchy;
        bool panelVisible = panelPopupList == null || panelPopupList.activeInHierarchy;
        if (!layerVisible || !rootVisible || !panelVisible)
        {
            Debug.LogWarning($"[AlertList] Re-open after click because active state was lost. layer={FormatActiveState(popupLayerObject)} root={FormatActiveState(alertListRootObject)} panel={FormatActiveState(panelPopupList)}");
            ShowAlertListPopup();
        }
    }

    private void HideAlertListPopup()
    {
        if (panelPopupList != null)
        {
            panelPopupList.SetActive(false);
        }

        if (alertListRootObject != null && alertListRootObject != popupLayerObject)
        {
            alertListRootObject.SetActive(false);
        }

        if (alertListOpenedFromDetail && selectedAlertLogId > 0 && incidentHistoryByLogId.TryGetValue(selectedAlertLogId, out ActiveAlertItem item))
        {
            alertListOpenedFromDetail = false;
            ApplyAlertItemToDetailPopup(item);
            return;
        }

        alertListOpenedFromDetail = false;

        if (!IsAlertDetailPopupOpen() && popupLayerObject != null)
        {
            popupLayerObject.SetActive(false);
        }
    }

    private bool IsAlertDetailPopupOpen()
    {
        return popupAlertMessage != null && popupAlertMessage.activeInHierarchy;
    }

    private bool IsAlertListPopupOpen()
    {
        return panelPopupList != null && panelPopupList.activeInHierarchy;
    }

    private void SetAlertListScrollTop()
    {
        if (scrollRectAlertList != null)
        {
            Canvas.ForceUpdateCanvases();
            scrollRectAlertList.verticalNormalizedPosition = 1f;
        }
    }

    private void RefreshPopupIndexAndButtons()
    {
        int count = activeAlertLogIds.Count;
        string rightIndexText = count > 0 ? $"{GetSelectedAlertDisplayIndex()} / {count}" : "-- / --";
        string popupIndexText = IsAlertDetailPopupOpen() ? $"{GetSelectedAlertListDisplayIndex()} / {GetCurrentAlertListFilterCount()}" : rightIndexText;
        SetTextValueIfBound(textAlertPopupIndex, popupIndexText);
        SetTextValueIfBound(textEventAlertIndex, rightIndexText);
        SetTextValueIfBound(textPopupPendingCount, $"{GetCurrentAlertListFilterCount()}건");
    }

    private int GetSelectedAlertDisplayIndex()
    {
        int index = activeAlertLogIds.IndexOf(selectedAlertLogId);
        return index >= 0 ? index + 1 : (activeAlertLogIds.Count > 0 ? 1 : 0);
    }

    private int GetSelectedAlertListDisplayIndex()
    {
        List<int> filteredIds = GetFilteredIncidentLogIds();
        int index = filteredIds.IndexOf(selectedAlertLogId);
        return index >= 0 ? index + 1 : (filteredIds.Count > 0 ? 1 : 0);
    }

    private static string FormatAlertTime(string timestamp)
    {
        if (!string.IsNullOrWhiteSpace(timestamp) &&
            !timestamp.StartsWith("TEST-", StringComparison.OrdinalIgnoreCase) &&
            DateTime.TryParse(timestamp.Trim(), out DateTime parsed))
        {
            return parsed.ToString("HH:mm:ss");
        }

        return DateTime.Now.ToString("HH:mm:ss");
    }

    private static string FormatUserFacingDateTime(string rawTimestamp)
    {
        if (string.IsNullOrWhiteSpace(rawTimestamp) || rawTimestamp.Trim() == "--")
        {
            return "--";
        }

        string trimmed = rawTimestamp.Trim();
        if (DateTimeOffset.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTimeOffset parsed) ||
            DateTimeOffset.TryParse(
                trimmed,
                CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out parsed))
        {
            return parsed.ToString("yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return "시간 확인 불가";
    }

    private void SetChildText(Transform root, string childName, string value)
    {
        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text text in texts)
        {
            if (text != null && text.name == childName)
            {
                text.text = value;
                return;
            }
        }
    }

    private void SetAlertListItemMetaAndStatusText(Transform rowRoot, string metaTextValue, string statusTextValue)
    {
        TMP_Text metaText = FindChildTextByName(rowRoot, "Text_AlertListItemMeta");
        TMP_Text statusText = FindChildTextByName(rowRoot, "Text_AlertListItemStatus");

        if (metaText == null || statusText == null)
        {
            SetChildText(rowRoot, "Text_AlertListItemMeta", metaTextValue);
            SetChildText(rowRoot, "Text_AlertListItemStatus", statusTextValue);
            return;
        }

        TMP_Text visualMetaText = metaText;
        TMP_Text visualStatusText = statusText;
        RectTransform metaRect = metaText.rectTransform;
        RectTransform statusRect = statusText.rectTransform;
        if (metaRect != null && statusRect != null &&
            metaRect.anchoredPosition.x > statusRect.anchoredPosition.x)
        {
            visualMetaText = statusText;
            visualStatusText = metaText;
        }

        visualMetaText.text = metaTextValue;
        visualStatusText.text = statusTextValue;
    }

    private static TMP_Text FindChildTextByName(Transform root, string childName)
    {
        if (root == null)
        {
            return null;
        }

        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text text in texts)
        {
            if (text != null && text.name == childName)
            {
                return text;
            }
        }

        return null;
    }

    private void RefreshAlertListItemIcons(Transform rowRoot, ActiveAlertItem item, bool acknowledged)
    {
        if (rowRoot == null || item == null)
        {
            return;
        }

        string incidentType = (item.IncidentType ?? string.Empty).Trim().ToUpperInvariant();
        bool isFire = incidentType == "FIRE" || incidentType == "EVENT_FIRE";
        bool isFall = incidentType == "FALL" || incidentType == "EVENT_FALL";
        bool isNoHelmet = incidentType == "NO_HELMET" || incidentType == "EVENT_HELMET";

        SetChildActive(rowRoot, "IconSlot_EventFire", isFire);
        SetChildActive(rowRoot, "IconSlot_EventFall", isFall);
        SetChildActive(rowRoot, "IconSlot_EventNoHelmet", isNoHelmet);

        bool isCleared = IsClearedAlertStatus(item.Status);
        bool showUnconfirmed = !isCleared && !acknowledged;
        bool showAcknowledged = !isCleared && acknowledged;
        bool showCleared = isCleared;

        SetChildActive(rowRoot, "IconSlot_StatusUnconfirmed", showUnconfirmed);
        SetChildActive(rowRoot, "IconSlot_StatusAcknowledged", showAcknowledged);
        SetChildActive(rowRoot, "IconSlot_StatusCleared", showCleared);

        SetChildActive(rowRoot, "Text_AlertListItemStatus", true);
        SetChildActive(rowRoot, "IconSlot_AlertListItemDetail", true);
        SetChildActive(rowRoot, "Text_AlertListItemArrow", true);
    }

    private static void SetChildActive(Transform root, string childName, bool active)
    {
        Transform child = FindChildTransformByName(root, childName);
        if (child != null && child.gameObject.activeSelf != active)
        {
            child.gameObject.SetActive(active);
        }
    }

    private static Transform FindChildTransformByName(Transform root, string childName)
    {
        if (root == null)
        {
            return null;
        }

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child != null && child.name == childName)
            {
                return child;
            }
        }

        return null;
    }

    private void RemoveActiveAlert(int logId)
    {
        if (incidentHistoryByLogId.TryGetValue(logId, out ActiveAlertItem historyItem))
        {
            historyItem.Status = "CLEARED";
            incidentHistoryByLogId[logId] = historyItem;
        }

        activeAlertsByLogId.Remove(logId);
        activeAlertLogIds.Remove(logId);
        acknowledgedAlertLogIds.Remove(logId);
        if (selectedAlertLogId == logId)
        {
            selectedAlertLogId = activeAlertLogIds.Count > 0 ? activeAlertLogIds[0] : 0;
        }

        RefreshAlertQueueUi();
        RefreshFactoryIncidentMarkers();
        RefreshRobotViewPanel();
        if (selectedAlertLogId > 0 && activeAlertsByLogId.TryGetValue(selectedAlertLogId, out ActiveAlertItem nextItem))
        {
            ApplyAlertItemToDetailPopup(nextItem);
        }
        else
        {
            HidePopup();
            SetEventAlert("--", "--", "--", "--", "--", "--");
        }
    }


    private static string FormatLastAccessLabel(string id, string action, string timestamp, bool isVisitor)
    {
        string actionLabel = FormatAccessActionLabel(action, isVisitor);
        string prefix = FormatAccessTimePrefix(timestamp);
        return $"{prefix}{id} {actionLabel}".Trim();
    }

    private static string FormatAccessActionLabel(string action, bool isVisitor)
    {
        string normalized = string.IsNullOrWhiteSpace(action) ? string.Empty : action.Trim().ToLowerInvariant();
        if (isVisitor)
        {
            return normalized == "exit" ? "Exit" : "Entry";
        }

        return normalized == "check_out" ? "Out" : "In";
    }

    private static string FormatAccessTimePrefix(string timestamp)
    {
        if (string.IsNullOrWhiteSpace(timestamp) || timestamp.StartsWith("TEST-", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (DateTime.TryParse(timestamp, out DateTime parsed))
        {
            return $"{parsed:HH:mm} ";
        }

        return string.Empty;
    }

    private void IncrementIncidentSummaryCount(string incidentType)
    {
        Debug.Log($"[DashboardSummary] Local incident count update ignored. Waiting for server final summary. incident={incidentType}");
    }

    public void ApplyPatrolTimelineEventFromServer(int timelineId, int logId, int robotId, string state, string pauseReason, string changedAt)
    {
        string robotName = ConvertRobotId(robotId);
        string timeText = FormatServerTimestampForDisplay(changedAt);
        string stateText = FormatServerStateForDisplay(state);
        string reasonText = FormatPauseReasonForDisplay(pauseReason);
        string detail = string.IsNullOrWhiteSpace(reasonText)
            ? $"{robotName} state {stateText}"
            : $"{robotName} state {stateText} / {reasonText}";

        AppendRobotTimelineEntry(robotName, changedAt, state, pauseReason);
        AppendServerPatrolTimelineLine($"[{timeText}] {detail}");
        AppendServerEventLogLine("PATROL", detail, changedAt);
        Debug.Log($"[ControlTower] patrol_timeline_event displayed timeline_id={timelineId} log_id={logId} robot={robotName} state={state} pause_reason={pauseReason} changed_at={changedAt}");
    }

    public void ApplyPatrolLogUpdateFromServer(int logId, int robotId, string startTime, string endTime, string status)
    {
        string robotName = ConvertRobotId(robotId);
        string timeText = FormatServerTimestampForDisplay(!string.IsNullOrWhiteSpace(endTime) ? endTime : startTime);
        string statusText = FormatServerStateForDisplay(status);
        string detail = $"{robotName} patrol log #{logId}: {statusText}";

        robotPatrolLogStatusById[robotName] = statusText;
        RefreshRobotViewPanel();
        AppendServerEventLogLine("PATROL", detail, !string.IsNullOrWhiteSpace(endTime) ? endTime : startTime);
        Debug.Log($"[ControlTower] patrol_log_update displayed log_id={logId} robot={robotName} status={status} start_time={startTime} end_time={endTime}");
    }

    public void ApplySystemStatusFromServer(string serverStatus, string websocketStatus, string ros2Status, string aiModelStatus)
    {
        hasSystemStatusFromServer = true;
        currentServerStatus = string.IsNullOrWhiteSpace(serverStatus) ? currentServerStatus : serverStatus;
        currentWebSocketStatus = string.IsNullOrWhiteSpace(websocketStatus) ? currentWebSocketStatus : websocketStatus;
        string ros2 = string.IsNullOrWhiteSpace(ros2Status) ? "--" : ros2Status;
        string ai = string.IsNullOrWhiteSpace(aiModelStatus) ? "--" : aiModelStatus;
        currentRos2Status = ros2;
        currentAiModelStatus = ai;

        isWebSocketConnected = currentWebSocketStatus.Equals("Connected", StringComparison.OrdinalIgnoreCase) ||
                               currentWebSocketStatus.Equals("Online", StringComparison.OrdinalIgnoreCase);
        currentCommStatus = isWebSocketConnected ? "WebSocket" : currentCommStatus;
        lastServerEvent = "system_status";
        cameraViewDirty = true;

        UpdateConnectionStatus(currentServerStatus, currentWebSocketStatus, ros2);
        RefreshLeftSystemStatusText();

        UpdateTopStatus(currentFsmState);
        UpdateDashboardFromSystemStatus();
        AddEventLog("SYSTEM", $"Server {currentServerStatus} / ROS2 {ros2} / AI {ai}");
    }

    public void ApplyCommandAckFromServer(int robotId, string command, string resultStatus, string responseMessage)
    {
        lastServerEvent = "command_ack";
        string result = string.IsNullOrWhiteSpace(resultStatus) ? "UNKNOWN" : resultStatus.Trim().ToUpperInvariant();
        string commandName = string.IsNullOrWhiteSpace(command) ? "COMMAND" : command.Trim().ToUpperInvariant();
        string message = string.IsNullOrWhiteSpace(responseMessage) ? result : responseMessage.Trim();
        string robotName = robotId > 0 ? ConvertRobotId(robotId) : selectedRobotId;
        SetRobotCommandViewState(robotName, commandName, result, message, "--");
        AddEventLog("ACK", $"{robotName} {commandName} {result}: {message}");
    }

    public void ApplyAlertAckResultFromServer(string alertType, int alertId, string action, string resultStatus, string responseMessage)
    {
        lastServerEvent = "alert_ack_result";
        string actionName = string.IsNullOrWhiteSpace(action) ? "ACK" : action.Trim().ToUpperInvariant();
        string result = string.IsNullOrWhiteSpace(resultStatus) ? "UNKNOWN" : resultStatus.Trim().ToUpperInvariant();
        AddEventLog("ACK", $"{actionName} {alertType} #{alertId} {result}");

        if (result == "OK" || result == "SUCCESS" || result == "SUCCEEDED")
        {
            if (actionName == "CLEAR")
            {
                currentAlertId = 0;
                currentAlertType = "NONE";
                SetEventAlert("None", "Normal", "None", "None", "None");
                SetEventMarkerVisible(false);
                SetCameraDetail("None", "Normal", "-", "-", "-", "-");
                HidePopup();
                RefreshAllStatusTexts();
            }
            else if (actionName == "ACK")
            {
                HidePopup();
            }
        }
    }

    private void ShowMainView(GameObject targetPanel, string viewMode)
    {
        EnsureDashboardReferences();

        SetActiveIfChanged(panelMainDashboardView, panelMainDashboardView == targetPanel);
        SetActiveIfChanged(panelMainFactoryView, panelMainFactoryView == targetPanel);
        SetActiveIfChanged(panelMainRobotView, panelMainRobotView == targetPanel);
        SetActiveIfChanged(panelMainMapStatusView, panelMainMapStatusView == targetPanel);
        SetActiveIfChanged(panelMainCameraView, panelMainCameraView == targetPanel);
        if (targetPanel != null && !targetPanel.activeSelf)
        {
            targetPanel.SetActive(true);
        }

        UpdatePreviewCameraRenderingState();
        SetBackToDashboardVisible(viewMode != "DASHBOARD_VIEW");
        LogViewChangeJson(viewMode);
    }

    private static void SetActiveIfChanged(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }

    private string ConvertRobotId(int robotId)
    {
        return robotId switch
        {
            1 => "tb3-01",
            2 => "tb3-02",
            3 => "tb3-03",
            _ => $"tb3-{robotId:00}"
        };
    }

    private void UpdateTopStatus(string mode)
    {
        if (textTopStatus != null)
        {
            string connection = isWebSocketConnected ? "Online" : "Offline";
            SetTextValueIfBound(textTopStatus, $"Robot: {selectedRobotId} | Connection: {connection} | Mode: {mode}");
        }
    }

    private void UpdateConnectionStatus(string server, string websocket, string ros2)
    {
        if (textBodyConnection == null) return;

        SetTextValueIfBound(textBodyConnection,
            $"서버 연결 : {FormatConnectionServerKorean(server)}\n" +
            $"제어 WS : {FormatControlWebSocketKorean(websocket)}");
    }

    private void RefreshLeftSystemStatusText()
    {
        EnsureLeftSummaryAndSystemTextReferences();
        if (textBodySystemStatus == null)
        {
            return;
        }

        bool connected = isWebSocketConnected || IsServerConnectedStatus(currentServerStatus);
        SetTextValueIfBound(textBodySystemStatus, connected
            ? "서버 상태 : 온라인\nROS2 상태 : 연결됨\nAI 모델 : 준비"
            : "서버 상태 : --\nROS2 상태 : --\nAI 모델 : --");
    }

    private string BuildSelectedRobotCommStatusText()
    {
        if (!isWebSocketConnected)
        {
            return "오프라인";
        }

        return robotStatesById.ContainsKey(selectedRobotId) ? "연결됨" : "대기";
    }

    private string BuildPauseReasonStatusText()
    {
        if (!robotStatesById.ContainsKey(selectedRobotId))
        {
            return "--";
        }

        if (string.IsNullOrWhiteSpace(currentPauseReason))
        {
            return "없음";
        }

        return FormatPauseReasonForDisplay(currentPauseReason);
    }

    private static string NormalizeDashValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "--";
        }

        string trimmed = value.Trim();
        return trimmed == "-" || trimmed.Equals("None", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            ? "--"
            : trimmed;
    }

    private static string NormalizeBatteryForRobotStatus(string battery)
    {
        string value = NormalizeDashValue(battery);
        if (value == "--")
        {
            return "-- %";
        }

        return value.IndexOf("%", StringComparison.Ordinal) >= 0 ? value : $"{value} %";
    }

    private static string NormalizeSpeedForRobotStatus(string speed)
    {
        string value = NormalizeDashValue(speed);
        if (value == "--")
        {
            return "-- m/s";
        }

        return value.IndexOf("m/s", StringComparison.OrdinalIgnoreCase) >= 0 ? value : $"{value} m/s";
    }

    private static string FormatConnectionServerKorean(string status)
    {
        string normalized = (status ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "ONLINE" or "CONNECTED" or "READY" => "온라인",
            "OFFLINE" or "DISCONNECTED" or "CLOSED" or "FAILED" or "ERROR" => "오프라인",
            _ => string.IsNullOrWhiteSpace(normalized) ? "--" : NormalizeDashValue(status)
        };
    }

    private static string FormatControlWebSocketKorean(string status)
    {
        string normalized = (status ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "" or "WAITING" or "--" => "대기",
            "CONNECTING" => "연결 중",
            "CONNECTED" or "ONLINE" => "연결됨",
            "DISCONNECTED" or "CLOSED" or "OFFLINE" or "FAILED" or "ERROR" => "연결 안됨",
            _ => NormalizeDashValue(status)
        };
    }

    private static string FormatServerStatusKorean(string status)
    {
        string normalized = (status ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "ONLINE" or "CONNECTED" or "READY" => "온라인",
            "OFFLINE" or "DISCONNECTED" or "CLOSED" or "FAILED" or "ERROR" => "--",
            _ => string.IsNullOrWhiteSpace(normalized) ? "--" : NormalizeDashValue(status)
        };
    }

    private static bool IsServerConnectedStatus(string status)
    {
        string normalized = (status ?? string.Empty).Trim().ToUpperInvariant();
        return normalized == "ONLINE" || normalized == "CONNECTED" || normalized == "READY";
    }

    private static string FormatRos2StatusKorean(string status)
    {
        string normalized = (status ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "ONLINE" or "CONNECTED" or "READY" => "연결됨",
            "WAITING" => "대기",
            "OFFLINE" or "DISCONNECTED" or "CLOSED" or "FAILED" or "ERROR" or "--" => "--",
            _ => string.IsNullOrWhiteSpace(normalized) ? "--" : NormalizeDashValue(status)
        };
    }

    private static string FormatAiModelStatusKorean(string status)
    {
        string normalized = (status ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "READY" or "ONLINE" or "CONNECTED" => "준비",
            "WAITING" => "대기",
            "OFFLINE" or "DISCONNECTED" or "CLOSED" or "FAILED" or "ERROR" or "--" => "--",
            _ => string.IsNullOrWhiteSpace(normalized) ? "--" : NormalizeDashValue(status)
        };
    }

    private static string FormatIncidentTypeForKoreanDisplay(string incidentType)
    {
        string normalized = (incidentType ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "" or "--" or "NONE" or "NORMAL" => "--",
            "NO_HELMET" or "EVENT_HELMET" => "안전모 미착용",
            "FALL" or "EVENT_FALL" => "쓰러짐 감지",
            "FIRE" or "EVENT_FIRE" => "화재 감지",
            _ => NormalizeDashValue(incidentType)
        };
    }

    private string BuildIncidentDisplayMessage(ActiveAlertItem item)
    {
        if (item == null)
        {
            return "이상 상황이 감지되었습니다.";
        }

        string incidentType = (item.IncidentType ?? string.Empty).Trim().ToUpperInvariant();
        string subject = BuildIncidentDetectionSubject(item);
        string suffix = incidentType switch
        {
            "FIRE" or "EVENT_FIRE" => "화재가 감지되었습니다.",
            "FALL" or "EVENT_FALL" => "쓰러짐 상황이 감지되었습니다.",
            "NO_HELMET" or "EVENT_HELMET" => "안전모 미착용이 감지되었습니다.",
            _ => "이상 상황이 감지되었습니다."
        };

        return string.IsNullOrWhiteSpace(subject) ? suffix : $"{subject}에서 {suffix}";
    }

    private static string BuildIncidentDetectionSubject(ActiveAlertItem item)
    {
        if (item == null)
        {
            return string.Empty;
        }

        string detectedBy = (item.DetectedBy ?? string.Empty).Trim().ToUpperInvariant();
        if (detectedBy == "GLOBALCAM" || detectedBy == "GLOBAL_CAM" || detectedBy == "GLOBAL_CCTV" || item.RobotNumericId <= 0 ||
            string.Equals(item.RobotDisplay, "GLOBAL", StringComparison.OrdinalIgnoreCase))
        {
            return "글로벌 카메라";
        }

        if ((detectedBy == "ROBOT" || detectedBy == "ROBOT_CAM" || detectedBy == "TB3_CAMERA") &&
            !string.IsNullOrWhiteSpace(item.RobotDisplay) && item.RobotDisplay != "--")
        {
            return item.RobotDisplay;
        }

        return string.Empty;
    }

    private string FormatAlertRobotDisplay(int robotId)
    {
        return robotId > 0 ? FormatRobotIdUpper(ConvertRobotId(robotId)) : "--";
    }

    private static bool ShouldDisplayIncidentAsGlobal(int robotId, string detectedBy)
    {
        if (robotId <= 0)
        {
            return true;
        }

        string normalized = (detectedBy ?? string.Empty).Trim().ToUpperInvariant();
        return normalized == "GLOBALCAM" || normalized == "GLOBAL_CAM" || normalized == "GLOBAL_CCTV" || normalized == "GLOBAL";
    }

    private static string FormatRobotIdUpper(string robotId)
    {
        return string.IsNullOrWhiteSpace(robotId) ? "--" : robotId.Trim().ToUpperInvariant();
    }

    private string BuildAlertLocationDisplay(bool hasLocation, float locationX, float locationY)
    {
        if (!hasLocation ||
            !IsFiniteAlertCoordinate(locationX) ||
            !IsFiniteAlertCoordinate(locationY))
        {
            return "위치 확인 불가";
        }

        if (full2DMapController == null)
        {
            EnsureFactoryViewRuntimeReferences();
        }

        return full2DMapController != null &&
               full2DMapController.TryResolveNearestZoneFromRos(locationX, locationY, out string zoneDisplayName)
            ? zoneDisplayName
            : "위치 확인 불가";
    }

    private string BuildAlertLocationDisplay(string location)
    {
        bool hasLocation = TryParseIncidentLocation(location, out float locationX, out float locationY) &&
                           IsFiniteAlertCoordinate(locationX) &&
                           IsFiniteAlertCoordinate(locationY);
        return BuildAlertLocationDisplay(hasLocation, locationX, locationY);
    }

    private static string FormatIncidentLocation(float x, float y)
    {
        return $"X {x:0.00}, Y {y:0.00}";
    }

    private static bool TryParseIncidentLocation(string location, out float x, out float y)
    {
        x = 0f;
        y = 0f;
        if (string.IsNullOrWhiteSpace(location))
        {
            return false;
        }

        string normalized = location.Trim()
            .Replace("X", "x", StringComparison.Ordinal)
            .Replace("Y", "y", StringComparison.Ordinal)
            .Replace("=", " ")
            .Replace(",", " ");
        string[] tokens = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        bool hasX = false;
        bool hasY = false;
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            if (tokens[i].Equals("x", StringComparison.OrdinalIgnoreCase) &&
                float.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedX))
            {
                x = parsedX;
                hasX = true;
            }
            else if (tokens[i].Equals("y", StringComparison.OrdinalIgnoreCase) &&
                     float.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedY))
            {
                y = parsedY;
                hasY = true;
            }
        }

        return hasX && hasY;
    }

    private static bool IsFiniteAlertCoordinate(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static string GetIncidentConfidenceValue(IncidentRecordItem record)
    {
        if (record == null)
        {
            return string.Empty;
        }

        float confidence = record.ai_details != null ? record.ai_details.confidence : record.confidence;
        return confidence > 0f ? confidence.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private static string FormatConfidenceForKoreanDisplay(string confidence)
    {
        if (string.IsNullOrWhiteSpace(confidence))
        {
            return "--";
        }

        string normalized = confidence.Replace("%", string.Empty).Trim();
        if (!float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
        {
            return NormalizeDashValue(confidence);
        }

        float percent = parsed <= 1f ? parsed * 100f : parsed;
        return $"{Mathf.Clamp(Mathf.RoundToInt(percent), 0, 100)} %";
    }

    private string BuildCompactRecentEventLine(ActiveAlertItem item)
    {
        if (item == null)
        {
            return string.Empty;
        }

        string safeEvent = FormatRecentEventTypeForKoreanDisplay(item.IncidentType);
        if (safeEvent == "--")
        {
            return string.Empty;
        }

        string safeRobot = item.RobotNumericId > 0
            ? FormatRobotIdUpper(ConvertRobotId(item.RobotNumericId))
            : "로봇 미확인";
        if (string.IsNullOrWhiteSpace(safeRobot) || safeRobot == "--")
        {
            safeRobot = "로봇 미확인";
        }

        string safeLocation = FormatCompactRecentEventLocation(item.LocationDisplay);
        return $"[{safeRobot}] {safeEvent} · {safeLocation}";
    }

    private static string FormatRecentEventTypeForKoreanDisplay(string incidentType)
    {
        string normalized = (incidentType ?? string.Empty).Trim().ToUpperInvariant();
        string localized = normalized switch
        {
            "OBSTACLE_DETECTED" or "DETECTED" => "장애물 감지",
            "PATH_BLOCKED" or "BLOCKED" => "경로 차단",
            "SCAN_LOST" => "라이다 신호 유실",
            _ => FormatIncidentTypeForKoreanDisplay(incidentType)
        };

        return ContainsLatinLetter(localized) ? "이벤트 확인 필요" : localized;
    }

    private static string FormatCompactRecentEventLocation(string locationDisplay)
    {
        string normalized = NormalizeDashValue(locationDisplay);
        if (normalized == "--" ||
            normalized.Equals("위치 확인 불가", StringComparison.Ordinal) ||
            normalized.Equals("위치 미확인", StringComparison.Ordinal))
        {
            return "위치 미확인";
        }

        return normalized switch
        {
            "컨베이어 1 구역" => "컨베이어 1",
            "컨베이어 2 구역" => "컨베이어 2",
            "팔레트 구역" => "팔레트",
            "충전존 구역" => "충전존",
            "직원 출입구 구역" => "직원 출입구",
            _ when normalized.EndsWith(" 구역", StringComparison.Ordinal) =>
                normalized.Substring(0, normalized.Length - " 구역".Length),
            _ => normalized
        };
    }

    private void AppendIncidentToTodayEvents(ActiveAlertItem item)
    {
        if (item == null || item.LogId <= 0 || todayIncidentEventLogIds.Contains(item.LogId))
        {
            return;
        }

        string line = BuildCompactRecentEventLine(item);
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        todayIncidentEventLogIds.Add(item.LogId);
        AppendTodayEvent(line, item.Timestamp);
    }

    private static int CompareAlertItemsByTimestampAscending(ActiveAlertItem left, ActiveAlertItem right)
    {
        if (left == null && right == null) return 0;
        if (left == null) return -1;
        if (right == null) return 1;

        bool leftParsed = DateTime.TryParse(left.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime leftTime);
        bool rightParsed = DateTime.TryParse(right.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime rightTime);
        if (leftParsed && rightParsed)
        {
            return leftTime.CompareTo(rightTime);
        }

        return string.Compare(left.Timestamp, right.Timestamp, StringComparison.Ordinal);
    }

    private static string FormatEmployeeAttendanceRecentEvent(string action)
    {
        string normalized = (action ?? string.Empty).Trim().ToLowerInvariant();
        return normalized == "check_out" ? "직원 퇴근" : "직원 출근";
    }

    private static string FormatVisitorAttendanceRecentEvent(string action)
    {
        string normalized = (action ?? string.Empty).Trim().ToLowerInvariant();
        return normalized == "exit" ? "방문자 퇴장" : "방문자 입장";
    }

    private static bool IsBlankAlertValue(string value)
    {
        string normalized = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ||
               normalized == "--" ||
               normalized.Equals("None", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Normal", StringComparison.OrdinalIgnoreCase);
    }

    private void AddRobotStateChangeLogs(int robotId, string status, float battery, string pauseReason)
    {
        string robotName = ConvertRobotId(robotId);
        string normalizedPauseReason = string.IsNullOrWhiteSpace(pauseReason) ? string.Empty : pauseReason.Trim();
        int batteryInt = Mathf.RoundToInt(battery);

        if (!lastWsLogByRobot.TryGetValue(robotId, out RobotWsLogSnapshot previous))
        {
            AddEventLog("WS", $"{robotName} status {status}");
            if (batteryInt <= LowBatteryLogThreshold)
            {
                AddEventLog("BATTERY", $"{robotName} LOW_BATTERY {batteryInt}%");
            }

            if (!string.IsNullOrEmpty(normalizedPauseReason))
            {
                AddEventLog("WS", $"{robotName} paused: {normalizedPauseReason}");
            }

            lastWsLogByRobot[robotId] = new RobotWsLogSnapshot
            {
                Status = status,
                Battery = batteryInt,
                PauseReason = normalizedPauseReason
            };
            return;
        }

        if (previous.Status != status)
        {
            AddEventLog("WS", $"{robotName} status {status}");
        }

        bool enteredLowBattery = previous.Battery > LowBatteryLogThreshold && batteryInt <= LowBatteryLogThreshold;
        bool batteryChangedMeaningfully = Mathf.Abs(previous.Battery - batteryInt) >= BatteryLogDeltaThreshold;
        if (enteredLowBattery)
        {
            AddEventLog("BATTERY", $"{robotName} LOW_BATTERY {batteryInt}%");
        }
        else if (batteryChangedMeaningfully)
        {
            AddEventLog("BATTERY", $"{robotName} battery {previous.Battery}% -> {batteryInt}%");
        }

        if (previous.PauseReason != normalizedPauseReason)
        {
            if (!string.IsNullOrEmpty(normalizedPauseReason))
            {
                AddEventLog("WS", $"{robotName} paused: {normalizedPauseReason}");
            }
        }

        lastWsLogByRobot[robotId] = new RobotWsLogSnapshot
        {
            Status = status,
            Battery = batteryInt,
            PauseReason = normalizedPauseReason
        };
    }

    private void UpdateRobotMarkerPosition(int robotId, float x, float y)
    {
        RectTransform marker = GetRobotMarker(robotId);
        RectTransform floor = GetFactoryFloorRect();
        if (marker == null || floor == null)
        {
            return;
        }

        float normalizedX = Mathf.InverseLerp(mapMinX, mapMaxX, x);
        float normalizedY = Mathf.InverseLerp(mapMinY, mapMaxY, y);
        normalizedX = Mathf.Clamp01(normalizedX);
        normalizedY = Mathf.Clamp01(normalizedY);

        Rect rect = floor.rect;
        Vector2 localPoint = new Vector2(
            Mathf.Lerp(rect.xMin, rect.xMax, normalizedX),
            Mathf.Lerp(rect.yMin, rect.yMax, normalizedY));

        Vector3 worldPoint = floor.TransformPoint(localPoint);
        RectTransform parentRect = marker.parent as RectTransform;
        if (parentRect == null)
        {
            return;
        }

        Vector2 markerLocalPoint = parentRect.InverseTransformPoint(worldPoint);
        marker.anchoredPosition = markerLocalPoint;
    }

    private RectTransform GetRobotMarker(int robotId)
    {
        switch (robotId)
        {
            case 1:
                if (markerTb3_01 == null) markerTb3_01 = FindRectTransformByName("Marker_TB3_01");
                return markerTb3_01;
            case 2:
                if (markerTb3_02 == null) markerTb3_02 = FindRectTransformByName("Marker_TB3_02");
                return markerTb3_02;
            case 3:
                if (markerTb3_03 == null) markerTb3_03 = FindRectTransformByName("Marker_TB3_03");
                return markerTb3_03;
            default:
                return null;
        }
    }

    private RectTransform GetFactoryFloorRect()
    {
        if (factoryFloorRect == null)
        {
            factoryFloorRect = FindRectTransformByName("Image_FactoryFloor");
        }

        return factoryFloorRect;
    }

    private RectTransform FindRectTransformByName(string objectName)
    {
        Transform factoryView = FindTransformByName(transform.root, "Panel_Main_FactoryView");
        if (factoryView == null)
        {
            return null;
        }

        Transform found = FindTransformByName(factoryView, objectName);
        return found != null ? found as RectTransform : null;
    }

    private void EnsureLeftRobotSelectReferences()
    {
        if (buttonSelectTb3_01 == null)
        {
            buttonSelectTb3_01 = FindScopedSceneButton("Panel_RobotSelect", null, "Button_Select_TB3_01");
        }

        if (buttonSelectTb3_02 == null)
        {
            buttonSelectTb3_02 = FindScopedSceneButton("Panel_RobotSelect", null, "Button_Select_TB3_02");
        }

        if (buttonSelectTb3_03 == null)
        {
            buttonSelectTb3_03 = FindScopedSceneButton("Panel_RobotSelect", null, "Button_Select_TB3_03");
        }
    }

    private void EnsureManualAndCommandButtonReferences()
    {
        buttonManualForward ??= FindSceneButtonByName("Button_Manual_Forward");
        buttonManualLeft ??= FindSceneButtonByName("Button_Manual_Left");
        buttonManualStop ??= FindSceneButtonByName("Button_Manual_Stop");
        buttonManualRight ??= FindSceneButtonByName("Button_Manual_Right");
        buttonManualBackward ??= FindSceneButtonByName("Button_Manual_Backward");

        buttonStartPatrol ??= FindSceneButtonByName("Button_StartPatrol");
        buttonPauseMission ??= FindSceneButtonByName("Button_PauseMission");
        buttonResumePlay ??= FindSceneButtonByName("Button_ResumePlay");
        buttonManualControl ??= FindSceneButtonByName("Button_ManualControl");
        buttonManualExit ??= FindSceneButtonByName("Button_ManualExit");
        buttonReturnCharger ??= FindSceneButtonByName("Button_ReturnCharger");
        buttonReset ??= FindSceneButtonByName("Button_Reset");
        buttonEmergencyStop ??= FindSceneButtonByName("Button_EmergencyStop");
    }

    private void EnsureLeftSummaryAndSystemTextReferences()
    {
        if (textBodyTodaySummary == null)
        {
            textBodyTodaySummary = FindScopedSceneText("Panel_TodaySummary", null, "Text_Body_TodaySummary") ??
                                   FindScopedSceneText("Panel_Left_Sidebar", null, "Text_Body_TodaySummary") ??
                                   FindSceneTextByName("Text_Body_TodaySummary");
        }

        if (textBodySystemStatus == null)
        {
            textBodySystemStatus = FindScopedSceneText("Panel_SystemStatus", null, "Text_Body_SystemStatus") ??
                                   FindScopedSceneText("Panel_Left_Sidebar", null, "Text_Body_SystemStatus") ??
                                   FindSceneTextByName("Text_Body_SystemStatus");
        }
    }

    private static TMP_Text FindScopedSceneText(string rootName, string scopeName, string textName)
    {
        GameObject rootObject = FindSceneGameObjectByName(rootName);
        if (rootObject == null)
        {
            return null;
        }

        Transform scope = string.IsNullOrWhiteSpace(scopeName)
            ? rootObject.transform
            : FindDescendantTransform(rootObject.transform, scopeName);
        if (scope == null)
        {
            return null;
        }

        TMP_Text[] texts = scope.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text text in texts)
        {
            if (text != null && text.name == textName)
            {
                return text;
            }
        }

        return null;
    }

    private static Button FindScopedSceneButton(string rootName, string scopeName, string buttonName)
    {
        GameObject rootObject = FindSceneGameObjectByName(rootName);
        if (rootObject == null)
        {
            return null;
        }

        Transform scope = string.IsNullOrWhiteSpace(scopeName)
            ? rootObject.transform
            : FindDescendantTransform(rootObject.transform, scopeName);
        if (scope == null)
        {
            return null;
        }

        Button[] buttons = scope.GetComponentsInChildren<Button>(true);
        foreach (Button button in buttons)
        {
            if (button != null && button.name == buttonName)
            {
                return button;
            }
        }

        return null;
    }

    private static Transform FindDescendantTransform(Transform root, string objectName)
    {
        if (root == null || string.IsNullOrWhiteSpace(objectName))
        {
            return null;
        }

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == objectName)
            {
                return child;
            }
        }

        return null;
    }

    private Transform FindTransformByName(Transform parent, string objectName)
    {
        if (parent.name == objectName)
        {
            return parent;
        }

        foreach (Transform child in parent)
        {
            Transform found = FindTransformByName(child, objectName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private void RefreshAllStatusTexts()
    {
        using (UiRefreshMarker.Auto())
        {
            UpdateTopStatus(currentFsmState);
            RefreshTopSummaryCardTexts();
            UpdateRobotStatus(
                currentFsmState,
                currentMissionState,
                currentBattery,
                currentSpeed,
                currentPositionX,
                currentPositionY,
                currentTheta,
                currentGoal,
                currentWaypointIndex,
                currentNav2Status,
                currentCommStatus);

            SetCameraDetail(
                currentAiEvent,
                currentSeverity,
                currentCameraLocation,
                currentConfidence,
                currentPhotoUrl,
                currentDetectionBox,
                currentObstacleSource,
                currentServerVerdict);

            if (IsViewActive(panelMainRobotView))
            {
                RefreshRobotViewPanel();
            }

            if (IsViewActive(panelMainMapStatusView))
            {
                RefreshMapStatusViewPanel();
            }

            if (IsViewActive(panelMainCameraView))
            {
                RefreshCameraViewPanel();
            }
        }
    }

    private void ResetAlertAndCameraDetails()
    {
        SetEventAlert("None", "Normal", "None", "None", "None");
        SetEventMarkerVisible(false);
        HidePopup();

        currentAlertType = "NONE";
        currentAiEvent = "None";
        currentSeverity = "Normal";
        currentCameraLocation = "-";
        currentConfidence = "-";
        currentObstacleSource = "-";
        currentServerVerdict = "CLEAR";
        currentPhotoUrl = "-";
        currentDetectionBox = "-";
    }

    private void AppendTodayEvent(string message, string serverTimestamp = null)
    {
        if (textBodyTodayEventList == null) return;
        if (string.IsNullOrWhiteSpace(message)) return;
        _ = serverTimestamp;

        List<string> lines = new();
        if (!string.IsNullOrWhiteSpace(textBodyTodayEventList.text) && textBodyTodayEventList.text.Trim() != "--")
        {
            lines.AddRange(textBodyTodayEventList.text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        lines.Insert(0, message.Trim());
        while (lines.Count > MaxTodayEventLines)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        textBodyTodayEventList.text = string.Join("\n", lines);
    }

    private void LogServerEvent(string eventName, string state)
    {
        lastServerEvent = eventName;
        LogServerReceivedJson(eventName, $"\"state\":\"{state}\",\"robot_id\":\"{selectedRobotId}\"");
    }

    private void LogServerReceivedJson(string eventName, string payloadBody)
    {
        string json =
            "{\n" +
            $"  \"event\": \"{eventName}\",\n" +
            $"  \"sent_at\": \"{GetIsoTimestamp()}\",\n" +
            "  \"payload\": {\n" +
            $"    {payloadBody}\n" +
            "  }\n" +
            "}";

        Debug.Log($"[Server -> Unity] {eventName}");
    }

    private void UpdateRobotStatus(string fsmState, string missionState, string battery, string speed, string x, string y, string theta, string goal, string waypoint, string nav2, string comm)
    {
        if (textBodyRobotStatus == null) return;

        SetTextValueIfBound(textBodyRobotStatus,
            $"로봇 ID : {selectedRobotId}\n" +
            $"FSM : {fsmState}\n" +
            $"정지 사유 : {BuildPauseReasonStatusText()}\n" +
            $"배터리 : {NormalizeBatteryForRobotStatus(battery)}\n" +
            $"속도 : {NormalizeSpeedForRobotStatus(speed)}\n" +
            $"위치 X : {NormalizeDashValue(x)}\n" +
            $"위치 Y : {NormalizeDashValue(y)}\n" +
            $"방향 : {NormalizeDashValue(theta)}\n" +
            $"Waypoint : {NormalizeDashValue(waypoint)}\n" +
            $"통신 상태 : {BuildSelectedRobotCommStatusText()}");
    }

    private void UpdateRobotDetail(string fsmState, string missionState, string battery, string speed, string position, string goal, string waypoint, string savedWaypoint, string retryCount, string nav2, string comm, string serverEvent)
    {
        // Text_RobotView_Detail is intentionally unused. Robot View data is written to Text_RobotOverviewBody.
    }

    private void SetCameraDetail(string aiEvent, string severity, string location, string confidence, string photoUrl, string detectionBox, string obstacleSource = "-", string serverVerdict = "CLEAR")
    {
        currentAiEvent = aiEvent;
        currentSeverity = severity;
        currentCameraLocation = location;
        currentConfidence = confidence;
        currentPhotoUrl = photoUrl;
        currentDetectionBox = detectionBox;
        currentObstacleSource = obstacleSource;
        currentServerVerdict = serverVerdict;
        UpdateCameraViewFromAlert();

        if (textCameraViewDetail == null) return;

        SetTextValueIfBound(textCameraViewDetail,
            "CAMERA / AI DETECTION VIEW\n\n" +
            $"Camera Source : {selectedRobotId} / PiCam\n" +
            $"AI Event      : {aiEvent}\n" +
            $"Severity      : {severity}\n" +
            $"Location      : {location}\n" +
            $"Confidence    : {confidence}\n" +
            $"Obstacle Source : {obstacleSource}\n" +
            $"Server Verdict  : {serverVerdict}\n" +
            $"Photo URL     : {photoUrl}\n" +
            $"Detection Box : {detectionBox}");
    }

    private void SetEventAlert(
        string currentAlert,
        string eventLevel,
        string obstacle,
        string aiAlert,
        string lastEvent,
        string recommendedAction = "None",
        string eventTimestamp = null)
    {
        if (textEventAlertBody == null) return;

        lastRobotAlert = currentAlert;
        lastAlertLevel = eventLevel;
        lastAlertDetectedBy = string.IsNullOrWhiteSpace(aiAlert) || aiAlert == "None" ? obstacle : aiAlert;
        lastRecommendedAction = recommendedAction;

        string eventKind = FormatIncidentTypeForKoreanDisplay(currentAlert);
        bool hasActiveAlert = !IsBlankAlertValue(eventKind);
        string robotDisplay = hasActiveAlert ? NormalizeDashValue(currentEventAlertRobotDisplay) : "--";
        string locationDisplay = hasActiveAlert ? NormalizeDashValue(currentEventAlertLocationDisplay) : "--";
        string confidenceDisplay = hasActiveAlert ? NormalizeDashValue(currentEventAlertConfidenceDisplay) : "--";
        string timestampDisplay = hasActiveAlert ? FormatUserFacingDateTime(eventTimestamp) : "--";
        string messageDisplay = hasActiveAlert
            ? NormalizeDashValue(string.IsNullOrWhiteSpace(currentEventAlertMessageDisplay) ? lastEvent : currentEventAlertMessageDisplay)
            : "--";

        SetTextValueIfBound(textEventAlertBody,
            $"이벤트 종류 : {(hasActiveAlert ? eventKind : "--")}\n" +
            $"관련 로봇 : {robotDisplay}\n" +
            $"발생 위치 : {locationDisplay}\n" +
            $"신뢰도 : {confidenceDisplay}\n" +
            $"발생 시각 : {timestampDisplay}\n" +
            $"알림 내용 : {messageDisplay}");
    }

    private void AddEventLog(string level, string message, string serverTimestamp = null)
    {
        string safeLevel = string.IsNullOrWhiteSpace(level) ? "INFO" : level.Trim().ToUpperInvariant();
        string safeMessage = string.IsNullOrWhiteSpace(message) ? "--" : message.Trim();
        Debug.Log($"[EventLogUI] [{safeLevel}] {safeMessage}");

        if (!TryFormatOperationalLog(safeLevel, safeMessage, serverTimestamp, out string line, out string dedupeKey))
        {
            return;
        }

        AddOperationalLogLine(line, dedupeKey);
    }

    private void RefreshEventLogText()
    {
        EnsureEventLogTextBound();

        if (textBodyEventLogScroll == null)
        {
            if (!eventLogMissingWarningShown)
            {
                Debug.LogWarning("[ControlTowerUIManager] eventLogBodyText is null. Connect Text_Body_EventLog_Scroll or the Event Log TMP_Text field in the Inspector.");
                eventLogMissingWarningShown = true;
            }

            return;
        }

        string logText = eventLogLines.Count > 0 ? string.Join("\n", eventLogLines) : "--";
        if (textBodyEventLogScroll.text == logText)
        {
            return;
        }

        textBodyEventLogScroll.text = logText;
        textBodyEventLogScroll.ForceMeshUpdate();
        QueueEventLogScrollToBottom();
    }

    private void AppendServerPatrolTimelineLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        serverPatrolTimelineLines.Add(line.Trim());
        while (serverPatrolTimelineLines.Count > MaxServerPatrolTimelineLines)
        {
            serverPatrolTimelineLines.RemoveAt(0);
        }

        RefreshRobotViewPanel();
    }

    private void AppendServerEventLogLine(string level, string message, string serverTimestamp = null)
    {
        string safeLevel = string.IsNullOrWhiteSpace(level) ? "SERVER" : level.Trim().ToUpperInvariant();
        string safeMessage = string.IsNullOrWhiteSpace(message) ? "--" : message.Trim();
        Debug.Log($"[EventLogUI] {safeMessage}");

        if (!TryFormatOperationalLog(safeLevel, safeMessage, serverTimestamp, out string line, out string dedupeKey))
        {
            return;
        }

        AddOperationalLogLine(line, dedupeKey);
    }

    private void EnsureEventLogTextBound()
    {
        if (textBodyEventLogScroll == null)
        {
            textBodyEventLogScroll = FindEventLogTextByName("Text_Body_EventLog_Scroll");
        }

        if (eventLogScrollRect == null && textBodyEventLogScroll != null)
        {
            eventLogScrollRect = textBodyEventLogScroll.GetComponentInParent<ScrollRect>(true);
        }
    }

    private void AddOperationalLogLine(string line, string dedupeKey)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        string safeKey = string.IsNullOrWhiteSpace(dedupeKey) ? line : dedupeKey.Trim();
        if (!string.IsNullOrEmpty(safeKey) &&
            lastOperationalLogByKey.TryGetValue(safeKey, out string previousLine) &&
            previousLine == line)
        {
            return;
        }

        if (!string.IsNullOrEmpty(safeKey))
        {
            lastOperationalLogByKey[safeKey] = line;
        }

        eventLogLines.Add(line);
        while (eventLogLines.Count > maxLogLines)
        {
            eventLogLines.RemoveAt(0);
        }

        RefreshEventLogText();
        RefreshDashboardTimelineText();
    }

    private void QueueEventLogScrollToBottom()
    {
        EnsureEventLogTextBound();
        if (eventLogScrollRect == null || !isActiveAndEnabled)
        {
            return;
        }

        StartCoroutine(ScrollEventLogToBottomNextFrame());
    }

    private IEnumerator ScrollEventLogToBottomNextFrame()
    {
        yield return null;

        if (eventLogScrollRect != null)
        {
            eventLogScrollRect.verticalNormalizedPosition = 0f;
        }
    }

    private bool TryFormatOperationalLog(string level, string message, string serverTimestamp, out string line, out string dedupeKey)
    {
        line = string.Empty;
        dedupeKey = string.Empty;

        string category;
        string text;
        string timestamp = FormatOperationalTimestamp(serverTimestamp);
        string normalizedLevel = (level ?? string.Empty).Trim().ToUpperInvariant();
        string normalizedMessage = (message ?? string.Empty).Trim();

        switch (normalizedLevel)
        {
            case "UI":
                return TryFormatUiOperationalLog(normalizedMessage, out category, out text, out dedupeKey) &&
                       BuildOperationalLine(timestamp, category, text, out line);
            case "CAM":
                return TryFormatCameraOperationalLog(normalizedMessage, out category, out text, out dedupeKey) &&
                       BuildOperationalLine(timestamp, category, text, out line);
            case "API":
                return TryFormatApiOperationalLog(normalizedMessage, out category, out text, out dedupeKey) &&
                       BuildOperationalLine(timestamp, category, text, out line);
            case "CMD":
            case "ACK":
            case "WARN":
            case "ERROR":
                return TryFormatCommandOrErrorOperationalLog(normalizedLevel, normalizedMessage, out category, out text, out dedupeKey) &&
                       BuildOperationalLine(timestamp, category, text, out line);
            case "WS":
            case "BATTERY":
                return TryFormatRobotOperationalLog(normalizedLevel, normalizedMessage, out category, out text, out dedupeKey) &&
                       BuildOperationalLine(timestamp, category, text, out line);
            case "ALERT":
            case "CRITICAL":
            case "EVENT":
                return TryFormatAlertOperationalLog(normalizedMessage, out category, out text, out dedupeKey) &&
                       BuildOperationalLine(timestamp, category, text, out line);
            case "ATTENDANCE":
            case "VISITOR":
                return TryFormatAccessOperationalLog(normalizedLevel, normalizedMessage, out category, out text, out dedupeKey) &&
                       BuildOperationalLine(timestamp, category, text, out line);
            case "PATROL":
                return TryFormatPatrolOperationalLog(normalizedMessage, out category, out text, out dedupeKey) &&
                       BuildOperationalLine(timestamp, category, text, out line);
            case "SYSTEM":
                category = "시스템";
                text = "시스템 상태 갱신";
                dedupeKey = "SYSTEM_STATUS";
                return BuildOperationalLine(timestamp, category, text, out line);
            default:
                return false;
        }
    }

    private static bool BuildOperationalLine(string timestamp, string category, string text, out string line)
    {
        line = string.Empty;
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string localizedText = LocalizeUserFacingOperationalMessage(category, text);
        if (string.IsNullOrWhiteSpace(localizedText))
        {
            return false;
        }

        line = $"[{timestamp}] [{category}] {localizedText}";
        return true;
    }

    private static string LocalizeUserFacingOperationalMessage(string category, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        string localized = message.Trim();
        localized = ReplaceUserFacingPhrase(localized, "Camera disconnected", "카메라 연결 끊김");
        localized = ReplaceUserFacingPhrase(localized, "Camera connected", "카메라 연결됨");
        localized = ReplaceUserFacingPhrase(localized, "Frame received", "카메라 영상 수신");
        localized = ReplaceUserFacingPhrase(localized, "Request completed", "요청 완료");
        localized = ReplaceUserFacingPhrase(localized, "Emergency Stop", "긴급 정지");
        localized = ReplaceUserFacingPhrase(localized, "Not connected", "연결되지 않음");
        localized = ReplaceUserFacingPhrase(localized, "Disconnected", "연결 끊김");
        localized = ReplaceUserFacingPhrase(localized, "Connecting", "연결 중");
        localized = ReplaceUserFacingPhrase(localized, "Connected", "연결됨");
        localized = ReplaceUserFacingPhrase(localized, "Waiting", "대기 중");
        localized = ReplaceUserFacingPhrase(localized, "Ready", "준비");
        localized = ReplaceUserFacingPhrase(localized, "Paused", "일시정지");
        localized = ReplaceUserFacingPhrase(localized, "Charging", "충전 중");

        if (string.Equals(category, "오류", StringComparison.Ordinal) &&
            ContainsLikelyUnlocalizedEnglishError(localized))
        {
            int separatorIndex = localized.IndexOf(':');
            if (separatorIndex > 0)
            {
                string prefix = localized.Substring(0, separatorIndex).Trim();
                if (ContainsHangul(prefix))
                {
                    string detail = LocalizeUserFacingErrorMessage(localized.Substring(separatorIndex + 1));
                    return $"{prefix}: {detail}";
                }
            }

            return LocalizeUserFacingErrorMessage(localized);
        }

        return localized;
    }

    private static string LocalizeUserFacingErrorMessage(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return "알 수 없는 오류가 발생했습니다.";
        }

        string message = rawMessage.Trim();
        string upper = message.ToUpperInvariant();
        int statusCode = ExtractUserFacingHttpStatusCode(message);

        if (upper.Contains("CANNOT CONNECT TO DESTINATION HOST") ||
            upper.Contains("NO CONNECTION COULD BE MADE"))
        {
            return "서버에 연결할 수 없습니다.";
        }

        if (upper.Contains("CONNECTION REFUSED"))
        {
            return "서버에서 연결을 거부했습니다.";
        }

        if (upper.Contains("REQUEST TIMEOUT") || upper.Contains("THE REQUEST TIMED OUT"))
        {
            return "요청 시간이 초과되었습니다.";
        }

        if (upper.Contains("OPERATION TIMED OUT"))
        {
            return "작업 시간이 초과되었습니다.";
        }

        if (upper.Contains("A TASK WAS CANCELED") || upper.Contains("A TASK WAS CANCELLED"))
        {
            return "요청이 취소되었습니다.";
        }

        if (upper.Contains("THE OPERATION WAS CANCELED") || upper.Contains("THE OPERATION WAS CANCELLED"))
        {
            return "작업이 취소되었습니다.";
        }

        if (upper.Contains("REMOTE PARTY CLOSED") && upper.Contains("WEBSOCKET"))
        {
            return "서버에서 웹소켓 연결을 종료했습니다.";
        }

        if (upper.Contains("WEBSOCKET IS NOT CONNECTED"))
        {
            return "웹소켓이 연결되지 않았습니다.";
        }

        if (upper.Contains("CONNECTION CLOSED"))
        {
            return "연결이 종료되었습니다.";
        }

        if (upper.Contains("CONNECTION LOST"))
        {
            return "서버 연결이 끊어졌습니다.";
        }

        if (upper.Contains("NETWORK IS UNREACHABLE") || upper.Contains("NETWORK UNREACHABLE"))
        {
            return "네트워크에 연결할 수 없습니다.";
        }

        if (upper.Contains("HOST IS UNREACHABLE") || upper.Contains("HOST UNREACHABLE"))
        {
            return "서버에 접근할 수 없습니다.";
        }

        if (upper.Contains("NAME OR SERVICE NOT KNOWN") || upper.Contains("NAME/SERVICE NOT KNOWN"))
        {
            return "서버 주소를 확인할 수 없습니다.";
        }

        if (upper.Contains("JPEG DECODE FAILED") || upper.Contains("FRAME DECODE FAILED"))
        {
            return "카메라 이미지 해석에 실패했습니다.";
        }

        if (upper.Contains("IMAGE DECODE FAILED"))
        {
            return "이미지 해석에 실패했습니다.";
        }

        if (upper.Contains("JSON PARSE ERROR") || upper.Contains("JSON PARSE FAILED") || upper.Contains("PARSE WARNING"))
        {
            return "서버 데이터 형식을 해석할 수 없습니다.";
        }

        if (upper.Contains("INVALID JSON"))
        {
            return "서버 데이터 형식이 올바르지 않습니다.";
        }

        if (upper.Contains("INVALID RESPONSE"))
        {
            return "서버 응답 형식이 올바르지 않습니다.";
        }

        if (statusCode == 404 || upper.Contains("NOT FOUND"))
        {
            return "요청한 데이터를 찾을 수 없습니다.";
        }

        if (statusCode == 401 || upper.Contains("UNAUTHORIZED"))
        {
            return "인증이 필요합니다.";
        }

        if (statusCode == 403 || upper.Contains("FORBIDDEN"))
        {
            return "요청 권한이 없습니다.";
        }

        if (statusCode == 502 || upper.Contains("BAD GATEWAY"))
        {
            return "서버 게이트웨이 오류가 발생했습니다.";
        }

        if (statusCode == 503 || upper.Contains("SERVICE UNAVAILABLE"))
        {
            return "서버를 현재 사용할 수 없습니다.";
        }

        if (statusCode == 500 || upper.Contains("INTERNAL SERVER ERROR"))
        {
            return "서버 내부 오류가 발생했습니다.";
        }

        if (upper.Contains("UNKNOWN ERROR"))
        {
            return "알 수 없는 오류가 발생했습니다.";
        }

        if (ContainsHangul(message) && !ContainsLikelyUnlocalizedEnglishError(message))
        {
            return message;
        }

        return statusCode > 0
            ? $"알 수 없는 오류가 발생했습니다. (코드: {statusCode})"
            : "알 수 없는 오류가 발생했습니다.";
    }

    private static string ReplaceUserFacingPhrase(string source, string oldValue, string newValue)
    {
        int searchStart = 0;
        while (searchStart < source.Length)
        {
            int index = source.IndexOf(oldValue, searchStart, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                break;
            }

            int endIndex = index + oldValue.Length;
            bool hasLeftBoundary = index == 0 || !IsUserFacingWordCharacter(source[index - 1]);
            bool hasRightBoundary = endIndex >= source.Length || !IsUserFacingWordCharacter(source[endIndex]);
            if (!hasLeftBoundary || !hasRightBoundary)
            {
                searchStart = endIndex;
                continue;
            }

            source = source.Substring(0, index) + newValue + source.Substring(endIndex);
            searchStart = index + newValue.Length;
        }

        return source;
    }

    private static bool IsUserFacingWordCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_';
    }

    private static int ExtractUserFacingHttpStatusCode(string message)
    {
        string[] tokens = (message ?? string.Empty).Split(
            new[] { ' ', '\t', ':', ';', ',', '.', '/', '(', ')', '[', ']', '{', '}', '-' },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string token in tokens)
        {
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) &&
                value >= 100 && value <= 599)
            {
                return value;
            }
        }

        return 0;
    }

    private static bool ContainsHangul(string value)
    {
        foreach (char character in value ?? string.Empty)
        {
            if (character >= '\uAC00' && character <= '\uD7A3')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsLikelyUnlocalizedEnglishError(string value)
    {
        string upper = (value ?? string.Empty).ToUpperInvariant();
        if (upper.Contains("EXCEPTION") || upper.Contains(" ERROR") || upper.StartsWith("ERROR", StringComparison.Ordinal) ||
            upper.Contains("FAILED") || upper.Contains("CANNOT ") || upper.Contains("TIMED OUT") ||
            upper.Contains("TIMEOUT") || upper.Contains("UNAUTHORIZED") || upper.Contains("FORBIDDEN") ||
            upper.Contains("NOT FOUND") || upper.Contains("BAD GATEWAY") || upper.Contains("SERVICE UNAVAILABLE"))
        {
            return true;
        }

        int latinWordCount = 0;
        int currentWordLength = 0;
        foreach (char character in value ?? string.Empty)
        {
            if ((character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z'))
            {
                currentWordLength++;
                continue;
            }

            if (currentWordLength >= 2)
            {
                latinWordCount++;
            }

            currentWordLength = 0;
        }

        if (currentWordLength >= 2)
        {
            latinWordCount++;
        }

        return latinWordCount >= 2;
    }

    private static string FormatOperationalTimestamp(string serverTimestamp)
    {
        if (!string.IsNullOrWhiteSpace(serverTimestamp) &&
            !serverTimestamp.StartsWith("TEST-", StringComparison.OrdinalIgnoreCase) &&
            DateTime.TryParse(serverTimestamp.Trim(), out DateTime parsed))
        {
            return parsed.ToString("HH:mm:ss");
        }

        return DateTime.Now.ToString("HH:mm:ss");
    }

    private static bool TryFormatUiOperationalLog(string message, out string category, out string text, out string dedupeKey)
    {
        category = "제어";
        text = string.Empty;
        dedupeKey = string.Empty;

        if (message.StartsWith("Selected robot changed:", StringComparison.OrdinalIgnoreCase))
        {
            string robot = NormalizeRobotLabel(message.Substring(message.IndexOf(':') + 1));
            text = $"제어 로봇 변경: {robot}";
            dedupeKey = $"SELECT_ROBOT:{robot}";
            return true;
        }

        if (message.StartsWith("Selected ", StringComparison.OrdinalIgnoreCase))
        {
            string robot = NormalizeRobotLabel(message.Substring("Selected ".Length));
            text = $"제어 로봇 변경: {robot}";
            dedupeKey = $"SELECT_ROBOT:{robot}";
            return true;
        }

        return false;
    }

    private static bool TryFormatCameraOperationalLog(string message, out string category, out string text, out string dedupeKey)
    {
        category = "카메라";
        text = string.Empty;
        dedupeKey = string.Empty;

        string upper = message.ToUpperInvariant();
        if (upper.Contains("URI=") || upper.Contains("CLEAR PREVIOUS") || upper.Contains("STREAM SWITCHING") || upper.StartsWith("MAIN FEED"))
        {
            return false;
        }

        string label = ExtractCameraLabel(message);
        string cameraLabel = string.Equals(label, "카메라", StringComparison.Ordinal) ? "카메라" : $"{label} 카메라";
        if (upper.Contains("CONNECTING"))
        {
            text = $"{cameraLabel} 연결 중";
            dedupeKey = $"CAM:{label}:CONNECTING";
            return true;
        }

        if (upper.Contains("NOT CONNECTED") || upper.Contains("NO STREAM") || upper.Contains("DISCONNECTED"))
        {
            text = $"{cameraLabel} 연결 끊김";
            dedupeKey = $"CAM:{label}:DISCONNECTED";
            return true;
        }

        if (upper.Contains("CONNECTED"))
        {
            text = $"{cameraLabel} 연결됨";
            dedupeKey = $"CAM:{label}:CONNECTED";
            return true;
        }

        if (upper.Contains("JPEG DECODE FAILED") || upper.Contains("FRAME DECODE FAILED"))
        {
            text = $"{cameraLabel} 이미지 해석 실패";
            dedupeKey = $"CAM:{label}:DECODE_ERROR";
            return true;
        }

        if (upper.Contains("FRAME RECEIVED"))
        {
            text = $"{cameraLabel} 영상 수신";
            dedupeKey = $"CAM:{label}:FRAME_RECEIVED";
            return true;
        }

        if (upper.Contains("ERROR") || upper.Contains("FAILED"))
        {
            text = $"{cameraLabel} 연결 오류";
            dedupeKey = $"CAM:{label}:ERROR";
            return true;
        }

        return false;
    }

    private static string ExtractCameraLabel(string message)
    {
        string upper = (message ?? string.Empty).ToUpperInvariant();
        if (upper.Contains("GLOBAL"))
        {
            return "글로벌";
        }

        if (upper.Contains("TB3-01"))
        {
            return "TB3-01";
        }

        if (upper.Contains("TB3-02"))
        {
            return "TB3-02";
        }

        if (upper.Contains("TB3-03"))
        {
            return "TB3-03";
        }

        return "카메라";
    }

    private static bool TryFormatApiOperationalLog(string message, out string category, out string text, out string dedupeKey)
    {
        category = "오류";
        text = string.Empty;
        dedupeKey = string.Empty;

        string upper = message.ToUpperInvariant();
        if (upper.Contains("LOADED") || upper.Contains("COMPLETED"))
        {
            return false;
        }

        if (upper.Contains("TODAY-SUMMARY"))
        {
            text = $"오늘 현황 조회 실패{FormatErrorSuffix(message)}";
        }
        else if (upper.Contains("ATTENDANCE"))
        {
            text = $"출입 기록 조회 실패{FormatErrorSuffix(message)}";
        }
        else if (upper.Contains("VISITOR"))
        {
            text = $"방문자 기록 조회 실패{FormatErrorSuffix(message)}";
        }
        else if (upper.Contains("INCIDENT"))
        {
            text = $"이벤트 기록 조회 실패{FormatErrorSuffix(message)}";
        }
        else
        {
            text = $"서버 요청 실패{FormatErrorSuffix(message)}";
        }

        dedupeKey = $"API:{text}";
        return true;
    }

    private static string FormatErrorSuffix(string message)
    {
        int index = message.IndexOf(':');
        if (index < 0 || index >= message.Length - 1)
        {
            return string.Empty;
        }

        string detail = message.IndexOf("parse", StringComparison.OrdinalIgnoreCase) >= 0
            ? "서버 데이터 형식을 해석할 수 없습니다."
            : FormatHttpErrorForOperationalLog(message.Substring(index + 1).Trim());
        return string.IsNullOrWhiteSpace(detail) ? string.Empty : $": {detail}";
    }

    private static string FormatHttpErrorForOperationalLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        return LocalizeUserFacingErrorMessage(message);
    }

    private static bool TryFormatCommandOrErrorOperationalLog(string level, string message, out string category, out string text, out string dedupeKey)
    {
        category = level == "ERROR" ? "오류" : "명령";
        text = string.Empty;
        dedupeKey = string.Empty;

        if (level == "ERROR")
        {
            if (message.IndexOf("clear failed for", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string robot = ExtractRobotLabel(message);
                string eventText = FormatIncidentTypeForOperationalLog(message);
                text = $"{eventText} 조치 완료 실패 · {robot}";
            }
            else if (message.IndexOf("command request failed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string command = ExtractCommandToken(message);
                string robot = ExtractRobotLabel(message);
                text = $"{FormatCommandForOperationalLog(command)} 요청 실패 · {robot}";
            }
            else
            {
                text = FormatHttpErrorForOperationalLog(message);
            }

            dedupeKey = $"ERROR:{text}";
            return true;
        }

        if (message.IndexOf("accepted", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            string command = ExtractCommandToken(message);
            string robot = ExtractRobotLabel(message);
            text = $"{FormatCommandForOperationalLog(command)} 수락 · {robot}";
            dedupeKey = $"CMD:{command}:{robot}:ACCEPTED";
            return true;
        }

        if (message.IndexOf("rejected", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            string command = ExtractCommandToken(message);
            string robot = ExtractRobotLabel(message);
            text = $"{FormatCommandForOperationalLog(command)} 거부 · {robot}";
            dedupeKey = $"CMD:{command}:{robot}:REJECTED";
            return true;
        }

        return false;
    }

    private static string ExtractCommandToken(string message)
    {
        string[] tokens = (message ?? string.Empty).Split(new[] { ' ', ':', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string token in tokens)
        {
            string upper = token.Trim().ToUpperInvariant();
            if (upper.Contains("_") || upper == "RESET" || upper == "RESUME")
            {
                return upper;
            }
        }

        return "COMMAND";
    }

    private static string ExtractRobotLabel(string message)
    {
        string[] tokens = (message ?? string.Empty).Split(new[] { ' ', ':', ',', '.', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string token in tokens)
        {
            if (token.StartsWith("tb3-", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeRobotLabel(token);
            }
        }

        return "--";
    }

    private static string FormatCommandForOperationalLog(string command)
    {
        string normalized = (command ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "PATROL_START" or "START_PATROL" => "순찰 시작",
            "RESUME" or "RESUME_MISSION" => "임무 재개",
            "MANUAL_ENTER" or "ENTER_MANUAL_MODE" => "수동 모드",
            "MANUAL_EXIT" or "EXIT_MANUAL_MODE" => "수동 종료",
            "RETURN_TO_CHARGER" => "충전소 복귀",
            "EMERGENCY_STOP" => "긴급 정지",
            "RESET" => "초기화",
            _ => "명령"
        };
    }

    private static bool TryFormatRobotOperationalLog(string level, string message, out string category, out string text, out string dedupeKey)
    {
        category = "로봇";
        text = string.Empty;
        dedupeKey = string.Empty;

        string upper = message.ToUpperInvariant();
        string robot = ExtractRobotLabel(message);
        if (level == "BATTERY")
        {
            if (!upper.Contains("LOW_BATTERY"))
            {
                return false;
            }

            text = $"{robot} 배터리 부족 {ExtractPercent(message)}";
            dedupeKey = $"BATTERY_LOW:{robot}";
            return true;
        }

        if (upper.Contains("STATUS"))
        {
            string state = ExtractAfterKeyword(message, "status");
            text = $"{robot} 상태 변경: {FormatRobotStateForOperationalLog(state)}";
            dedupeKey = $"ROBOT_STATUS:{robot}:{state}";
            return true;
        }

        if (upper.Contains("PAUSED:"))
        {
            string reason = message.Substring(message.IndexOf("paused:", StringComparison.OrdinalIgnoreCase) + "paused:".Length).Trim();
            text = $"{robot} 정지 사유: {FormatPauseReasonForOperationalLog(reason)}";
            dedupeKey = $"PAUSE_REASON:{robot}:{reason.ToUpperInvariant()}";
            return true;
        }

        if (upper == "CONNECTED")
        {
            category = "시스템";
            text = "서버 연결됨";
            dedupeKey = "SERVER_CONNECTED";
            return true;
        }

        if (upper == "CONNECTING" || upper == "WAITING")
        {
            category = "시스템";
            text = "서버 연결 중";
            dedupeKey = "SERVER_CONNECTING";
            return true;
        }

        if (upper == "--" || upper.Contains("DISCONNECTED") || upper.Contains("CONNECTION CLOSED") ||
            upper.Contains("CONNECTION LOST") || upper.Contains("REMOTE PARTY CLOSED") ||
            upper.Contains("WEBSOCKET IS NOT CONNECTED"))
        {
            category = "시스템";
            text = "서버 연결 끊김";
            dedupeKey = "SERVER_DISCONNECTED";
            return true;
        }

        if (level == "WS")
        {
            category = "오류";
            text = $"제어 웹소켓 연결 실패: {LocalizeUserFacingErrorMessage(message)}";
            dedupeKey = $"WS_ERROR:{text}";
            return true;
        }

        return false;
    }

    private static string ExtractAfterKeyword(string message, string keyword)
    {
        int index = message.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return message.Trim();
        }

        return message.Substring(index + keyword.Length).Trim();
    }

    private static string ExtractPercent(string message)
    {
        string[] tokens = (message ?? string.Empty).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string token in tokens)
        {
            if (token.Contains("%"))
            {
                return token.Trim();
            }
        }

        return string.Empty;
    }

    private static string FormatRobotStateForOperationalLog(string state)
    {
        string normalized = (state ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "IDLE" => "대기",
            "PATROLLING" => "순찰 중",
            "PAUSED" => "일시정지",
            "DOCKING" => "도킹 중",
            "CHARGING" => "충전 중",
            "EMERGENCY_STOP" => "긴급 정지",
            "MANUAL" or "MANUAL_CONTROL" => "수동 제어",
            "READY" => "준비",
            "WAITING" => "대기 중",
            "RETURNING_TO_CHARGER" => "충전소 복귀 중",
            _ => string.IsNullOrWhiteSpace(normalized) ? "--" : "상태 확인 필요"
        };
    }

    private static string FormatPauseReasonForOperationalLog(string reason)
    {
        string normalized = (reason ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "EVENT_FIRE" or "FIRE" => "화재 감지",
            "EVENT_FALL" or "FALL" => "쓰러짐 감지",
            "EVENT_HELMET" or "NO_HELMET" => "안전모 미착용",
            "EMERGENCY" or "EMERGENCY_STOP" => "긴급 정지",
            "MANUAL_DONE" => "수동 조작 종료",
            _ => string.IsNullOrWhiteSpace(normalized) ? "--" : "알 수 없는 사유"
        };
    }

    private static bool TryFormatAlertOperationalLog(string message, out string category, out string text, out string dedupeKey)
    {
        category = "이벤트";
        string eventText = FormatIncidentTypeForOperationalLog(message);
        string robot = ExtractRobotLabel(message);
        string locationDisplay = ExtractAlertLocationDisplay(message);
        if (robot == "--" && message.IndexOf("GLOBAL_CAM", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            robot = "GLOBAL";
        }

        if (!string.IsNullOrWhiteSpace(locationDisplay))
        {
            text = $"{eventText} - {locationDisplay}";
        }
        else
        {
            text = robot == "--" ? eventText : $"{eventText} · {robot}";
        }

        dedupeKey = $"ALERT:{eventText}:{robot}:{locationDisplay}";
        return eventText != "--";
    }

    private static string ExtractAlertLocationDisplay(string message)
    {
        const string marker = "location=";
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        int markerIndex = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return string.Empty;
        }

        return message.Substring(markerIndex + marker.Length).Trim();
    }

    private static string FormatIncidentTypeForOperationalLog(string value)
    {
        string upper = (value ?? string.Empty).ToUpperInvariant();
        if (upper.Contains("EVENT_FIRE") || upper.Contains("FIRE"))
        {
            return "화재 감지";
        }

        if (upper.Contains("EVENT_FALL") || upper.Contains("FALL"))
        {
            return "쓰러짐 감지";
        }

        if (upper.Contains("EVENT_HELMET") || upper.Contains("NO_HELMET"))
        {
            return "안전모 미착용";
        }

        return "--";
    }

    private static bool TryFormatAccessOperationalLog(string level, string message, out string category, out string text, out string dedupeKey)
    {
        category = "출입";
        string upper = message.ToUpperInvariant();
        string name = ExtractRawField(message, "name");

        if (level == "ATTENDANCE")
        {
            string action = upper.Contains("CHECK_OUT") ? "직원 퇴근" : "직원 출근";
            text = string.IsNullOrWhiteSpace(name) || name == "-" ? action : $"{name} {action}";
            dedupeKey = $"ACCESS:EMPLOYEE:{ExtractRawField(message, "employee")}:{action}";
            return true;
        }

        string visitorAction = upper.Contains("EXIT") ? "방문자 퇴장" : "방문자 입장";
        text = string.IsNullOrWhiteSpace(name) || name == "-" ? visitorAction : $"{name} {visitorAction}";
        dedupeKey = $"ACCESS:VISITOR:{ExtractRawField(message, "visitor")}:{visitorAction}";
        return true;
    }

    private static string ExtractRawField(string message, string fieldName)
    {
        string prefix = fieldName + "=";
        string[] parts = (message ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return part.Substring(prefix.Length).Trim();
            }
        }

        return string.Empty;
    }

    private static bool TryFormatPatrolOperationalLog(string message, out string category, out string text, out string dedupeKey)
    {
        category = "순찰";
        string robot = ExtractRobotLabel(message);
        string upper = message.ToUpperInvariant();
        string stateText;
        if (upper.Contains("PATROLLING") || upper.Contains("IN_PROGRESS"))
        {
            stateText = "순찰 시작";
        }
        else if (upper.Contains("PAUSED"))
        {
            stateText = "순찰 일시정지";
        }
        else if (upper.Contains("COMPLETED"))
        {
            stateText = "순찰 완료";
        }
        else if (upper.Contains("FAILED"))
        {
            stateText = "순찰 실패";
        }
        else if (upper.Contains("RESUME"))
        {
            stateText = "순찰 재개";
        }
        else
        {
            stateText = "순찰 상태 갱신";
        }

        text = robot == "--" ? stateText : $"{robot} {stateText}";
        dedupeKey = $"PATROL:{robot}:{stateText}";
        return true;
    }

    private static string NormalizeRobotLabel(string robotId)
    {
        string normalized = (robotId ?? string.Empty).Trim().TrimEnd('.', ',');
        return string.IsNullOrWhiteSpace(normalized) ? "--" : normalized.ToUpperInvariant();
    }

    private void RepairEventLogScrollViewLayout()
    {
        // Event Log layout is controlled manually in the Unity Inspector.
    }

    private TMP_Text FindEventLogTextByName(string objectName)
    {
        GameObject canvas = GameObject.Find("Canvas_ControlTower");
        TMP_Text[] texts = canvas != null
            ? canvas.GetComponentsInChildren<TMP_Text>(true)
            : GetComponentsInChildren<TMP_Text>(true);

        foreach (TMP_Text text in texts)
        {
            if (text != null && text.name == objectName)
            {
                return text;
            }
        }

        return null;
    }

    private string GetShortManualCommand(string command)
    {
        return command switch
        {
            "MANUAL_FORWARD" => "FORWARD",
            "MANUAL_BACKWARD" => "BACKWARD",
            "MANUAL_LEFT" => "LEFT",
            "MANUAL_RIGHT" => "RIGHT",
            "MANUAL_STOP" => "STOP",
            _ => command
        };
    }

    private void SetEventMarkerVisible(bool visible)
    {
        if (imageEventMarker != null)
        {
            imageEventMarker.SetActive(visible);
        }

        if (textLabelEvent != null)
        {
            textLabelEvent.SetActive(visible);
        }
    }

    public void ShowAlertPopup(string alertType, string level, int robotId, string location, string detectedBy, string confidence, string recommendedAction, string message)
    {
        EnsurePopupReferences();

        currentPopupAlertType = NormalizePopupValue(alertType, "ALERT").ToUpperInvariant();
        currentPopupLevel = NormalizePopupValue(level, "WARNING");
        currentPopupRobotId = robotId > 0 ? ConvertRobotId(robotId) : selectedRobotId;
        currentPopupLocation = NormalizePopupValue(location, "-");
        currentPopupDetectedBy = NormalizePopupValue(detectedBy, "-");
        currentPopupConfidence = NormalizePopupValue(confidence, "-");
        currentPopupRecommendedAction = NormalizePopupValue(recommendedAction, "ACK and monitor");
        currentPopupLastMessage = NormalizePopupValue(message, currentPopupAlertType);

        if (popupLayerObject != null)
        {
            popupLayerObject.SetActive(true);
        }

        if (popupAlertMessage != null)
        {
            popupAlertMessage.SetActive(true);
        }

        if (textPopupTitle != null)
        {
            textPopupTitle.text = "CONTROL TOWER ALERT";
        }

        string body =
            $"Alert Type         : {currentPopupAlertType}\n" +
            $"Event Level        : {currentPopupLevel}\n" +
            $"Robot ID           : {currentPopupRobotId}\n" +
            $"Location           : {currentPopupLocation}\n" +
            $"Detected By        : {currentPopupDetectedBy}\n" +
            $"Confidence         : {currentPopupConfidence}\n" +
            $"Recommended Action : {currentPopupRecommendedAction}\n" +
            $"Last Message       : {currentPopupLastMessage}";

        if (textPopupAlertBody != null)
        {
            textPopupAlertBody.text = body;
        }

        if (textPopupMessage != null)
        {
            textPopupMessage.text = body;
        }

        if (textPopupSnapshotBody != null)
        {
            textPopupSnapshotBody.text =
                "alert evidence image area\n" +
                $"Photo URL : {NormalizePopupValue(currentPhotoUrl, "-")}";
        }
    }

    public void HideAlertPopup()
    {
        EnsurePopupReferences();
        HidePopup();
    }

    public void ConfirmCurrentAlert()
    {
        int logId = selectedAlertLogId > 0 ? selectedAlertLogId : currentAlertId;
        if (logId > 0)
        {
            acknowledgedAlertLogIds.Add(logId);
            RefreshAlertQueueUi();
            if (activeAlertsByLogId.TryGetValue(logId, out ActiveAlertItem item))
            {
                ApplyAlertItemToDetailPopup(item);
            }
            AddEventLog("ACK", $"{currentPopupAlertType} confirmed");
        }
        else
        {
            AddEventLog("ACK", $"{currentPopupAlertType} confirmed");
        }
    }

    public void ClearCurrentAlert()
    {
        _ = ClearCurrentAlertAsync();
    }

    private async Task ClearCurrentAlertAsync()
    {
        int logId = selectedAlertLogId > 0 ? selectedAlertLogId : currentAlertId;
        if (logId <= 0 || !activeAlertsByLogId.TryGetValue(logId, out ActiveAlertItem item))
        {
            AddEventLog("WARN", "CLEAR_ALERT is not available: no selected active alert");
            return;
        }

        DashboardApiResult result = await PostIncidentClearAsync(logId);
        if (result.Success)
        {
            AddEventLog("ACK", $"{item.IncidentType} action=CLEAR");
            RemoveActiveAlert(logId);
            return;
        }

        string robot = string.IsNullOrWhiteSpace(item.RobotDisplay) ? "--" : item.RobotDisplay;
        AddEventLog("ERROR", $"{FormatIncidentTypeForKoreanDisplay(item.IncidentType)} clear failed for {robot}: {result.Message}");
    }

    private async Task<DashboardApiResult> PostIncidentClearAsync(int logId)
    {
        string baseUrl = string.IsNullOrWhiteSpace(dashboardServerBaseUrl)
            ? "http://127.0.0.1:8000"
            : dashboardServerBaseUrl.TrimEnd('/');
        string url = $"{baseUrl}/api/v1/incidents/{logId}/clear";

        try
        {
            using UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.downloadHandler = new DownloadHandlerBuffer();
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            bool success = request.result == UnityWebRequest.Result.Success && request.responseCode >= 200 && request.responseCode < 300;
            return new DashboardApiResult
            {
                Success = success,
                StatusCode = request.responseCode,
                Body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty,
                Message = success ? "HTTP OK" : $"{request.responseCode} {request.error}"
            };
        }
        catch (Exception exception)
        {
            return new DashboardApiResult
            {
                Success = false,
                StatusCode = 0,
                Body = string.Empty,
                Message = exception.Message
            };
        }
    }

    public void UpdatePopupFromViolationAlert()
    {
        ShowAlertPopup(currentAiEvent, currentSeverity, GetRobotNumberFromSelectedRobotId(), currentCameraLocation, currentObstacleSource, currentConfidence, lastRecommendedAction, $"{currentAiEvent} detected.");
    }

    public void UpdatePopupFromEmergencyAlert()
    {
        ShowAlertPopup(currentAiEvent, currentSeverity, GetRobotNumberFromSelectedRobotId(), currentCameraLocation, currentObstacleSource, currentConfidence, lastRecommendedAction, $"{currentAiEvent} detected.");
    }

    private void ShowPopup(string title, string message)
    {
        string alertType = NormalizePopupTitle(title);
        string level = title != null && title.ToUpperInvariant().Contains("EMERGENCY") ? "CRITICAL" : currentSeverity;
        ShowAlertPopup(alertType, level, GetRobotNumberFromSelectedRobotId(), currentCameraLocation, currentObstacleSource, currentConfidence, lastRecommendedAction, message);
    }

    private void HidePopup()
    {
        if (popupAlertMessage != null)
        {
            popupAlertMessage.SetActive(false);
        }

        if (panelPopupList != null)
        {
            panelPopupList.SetActive(false);
        }

        if (alertListRootObject != null && alertListRootObject != popupLayerObject)
        {
            alertListRootObject.SetActive(false);
        }

        if (popupLayerObject != null)
        {
            popupLayerObject.SetActive(false);
        }
    }

    private void EnsurePopupReferences()
    {
        if (popupLayerObject == null)
        {
            popupLayerObject = FindSceneGameObjectByName("PopupLayer");
        }

        if (popupAlertMessage == null)
        {
            popupAlertMessage = FindSceneGameObjectByName("Panel_AlertPopupRoot");
        }

        if (textPopupTitle == null)
        {
            textPopupTitle = FindSceneTextByName("Text_AlertPopupTitle");
        }

        if (textPopupAlertBody == null)
        {
            textPopupAlertBody = FindSceneTextByName("Text_AlertPopupBody");
        }

        if (textPopupMessage == null)
        {
            textPopupMessage = textPopupAlertBody;
        }

        if (textPopupSnapshotBody == null)
        {
            textPopupSnapshotBody = FindSceneTextByName("Text_PopupSnapshotBody");
        }

        if (buttonPopupAck == null)
        {
            buttonPopupAck = FindSceneButtonByName("Button_PopupAck");
        }

        if (buttonPopupClear == null)
        {
            buttonPopupClear = FindSceneButtonByName("Button_PopupClear");
        }

        if (buttonPopupClose == null)
        {
            buttonPopupClose = FindSceneButtonByName("Button_PopupClose");
        }

        if (buttonPopupConfirm == null || IsPopupListButton(buttonPopupConfirm))
        {
            Button popupConfirm = FindSceneButtonByName("Button_PopupConfirm");
            if (popupConfirm != null)
            {
                buttonPopupConfirm = popupConfirm;
            }
        }

        EnsureAlertQueueReferences();
        BindPopupActionButtons();
    }

    private void EnsureAlertQueueReferences()
    {
        if (popupLayerObject == null)
        {
            popupLayerObject = FindSceneGameObjectByName("PopupLayer");
        }

        if (alertListRootObject == null)
        {
            alertListRootObject = FindSceneGameObjectByName("Popup_AlertList");
            alertListRootObject ??= FindSceneGameObjectByName("AlertList");
            alertListRootObject ??= FindSceneGameObjectByName("Panel_PopupList");
        }

        if (panelPopupList == null)
        {
            panelPopupList = FindSceneGameObjectByName("Panel_PopupList");
            if (panelPopupList == null && alertListRootObject != null && alertListRootObject.name == "Panel_PopupList")
            {
                panelPopupList = alertListRootObject;
            }
        }

        if (textAlertPopupIndex == null)
        {
            textAlertPopupIndex = FindSceneTextByName("Text_AlertPopupIndex");
        }

        if (textPopupPendingCount == null)
        {
            textPopupPendingCount = FindSceneTextByName("Text_PopupPendingCount");
        }

        if (textPopupListMessage == null)
        {
            textPopupListMessage = FindSceneTextByName("Text_PopupMessage");
        }

        if (buttonAlertFilterPending == null)
        {
            buttonAlertFilterPending = FindSceneButtonByName("Button_AlertFilterPending");
        }

        if (buttonAlertFilterCleared == null)
        {
            buttonAlertFilterCleared = FindSceneButtonByName("Button_AlertFilterCleared");
        }

        if (buttonAlertList == null)
        {
            buttonAlertList = FindSceneButtonByName("Button_AlertList");
        }

        if (buttonPopupList == null)
        {
            buttonPopupList = FindPopupDetailListButton();
        }

        if (buttonPopupListClose == null)
        {
            buttonPopupListClose = FindChildComponentByName<Button>(panelPopupList != null ? panelPopupList.transform : null, "Button_PopupList");
        }

        if (alertListContent == null)
        {
            RectTransform content = FindChildComponentByName<RectTransform>(panelPopupList != null ? panelPopupList.transform : null, "Content");
            alertListContent = content != null ? content.transform : null;
        }

        if (scrollRectAlertList == null)
        {
            scrollRectAlertList = FindChildComponentByName<ScrollRect>(panelPopupList != null ? panelPopupList.transform : null, "ScrollView_AlertList");
        }

        if (buttonAlertListItemTemplate == null)
        {
            buttonAlertListItemTemplate = FindSceneButtonByName("Button_AlertListItem_Template");
        }

        if (popupSnapshotPlaceholderImage == null && popupSnapshotPlaceholderRawImage == null)
        {
            Image popupImage = FindChildComponentByName<Image>(popupAlertMessage != null ? popupAlertMessage.transform : null, "Image_PopupSnapshotPlaceholder");
            RawImage popupRawImage = FindChildComponentByName<RawImage>(popupAlertMessage != null ? popupAlertMessage.transform : null, "Image_PopupSnapshotPlaceholder");
            GameObject snapshot = popupImage != null
                ? popupImage.gameObject
                : popupRawImage != null
                    ? popupRawImage.gameObject
                    : FindSceneGameObjectByName("Image_PopupSnapshotPlaceholder");
            popupSnapshotPlaceholderImage = snapshot != null ? snapshot.GetComponent<Image>() : null;
            popupSnapshotPlaceholderRawImage = snapshot != null ? snapshot.GetComponent<RawImage>() : null;
        }

        if (textEventAlertPendingCount == null)
        {
            textEventAlertPendingCount = FindSceneTextByName("Text_EventAlertPendingCount");
        }

        if (buttonEventAlertPrev == null)
        {
            buttonEventAlertPrev = FindSceneButtonByName("Button_EventAlertPrev");
        }

        if (textEventAlertIndex == null)
        {
            textEventAlertIndex = FindSceneTextByName("Text_EventAlertIndex");
        }

        if (buttonEventAlertNext == null)
        {
            buttonEventAlertNext = FindSceneButtonByName("Button_EventAlertNext");
        }

        if (buttonEventAlertDetail == null)
        {
            buttonEventAlertDetail = FindSceneButtonByName("Button_EventAlertDetail");
        }
    }

    private Button FindPopupDetailListButton()
    {
        Button detailListButton = FindChildComponentByName<Button>(popupAlertMessage != null ? popupAlertMessage.transform : null, "Button_PopupList");
        if (detailListButton != null && !IsAlertListScopedObject(detailListButton.gameObject))
        {
            return detailListButton;
        }

        foreach (Button button in Resources.FindObjectsOfTypeAll<Button>())
        {
            if (button == null || button.name != "Button_PopupList" || !button.gameObject.scene.IsValid())
            {
                continue;
            }

            if (IsAlertListScopedObject(button.gameObject))
            {
                continue;
            }

            return button;
        }

        return null;
    }

    private bool IsPopupListButton(Button button)
    {
        return button != null && button.name == "Button_PopupList";
    }

    private bool IsAlertListScopedObject(GameObject item)
    {
        if (item == null)
        {
            return false;
        }

        Transform itemTransform = item.transform;
        return (panelPopupList != null && itemTransform.IsChildOf(panelPopupList.transform)) ||
               (alertListRootObject != null && itemTransform.IsChildOf(alertListRootObject.transform));
    }

    private static bool IsAncestorOrSelf(Transform ancestor, Transform child)
    {
        if (ancestor == null || child == null)
        {
            return false;
        }

        return child == ancestor || child.IsChildOf(ancestor);
    }

    private static void EnsureActiveAncestorChain(Transform target, Transform stopAncestor)
    {
        Transform current = target;
        while (current != null)
        {
            current.gameObject.SetActive(true);
            if (stopAncestor != null && current == stopAncestor)
            {
                break;
            }

            current = current.parent;
        }
    }

    private void LogAlertListActiveState(string label)
    {
        Debug.Log($"[AlertList] {label} layer={FormatActiveState(popupLayerObject)} detail={FormatActiveState(popupAlertMessage)} root={FormatActiveState(alertListRootObject)} panel={FormatActiveState(panelPopupList)}");
    }

    private static string FormatActiveState(GameObject item)
    {
        if (item == null)
        {
            return "null";
        }

        return $"{GetTransformPath(item.transform)} self={item.activeSelf} hierarchy={item.activeInHierarchy}";
    }

    private static string GetTransformPath(Transform transform)
    {
        if (transform == null)
        {
            return "<null>";
        }

        List<string> names = new();
        Transform current = transform;
        while (current != null)
        {
            names.Add(current.name);
            current = current.parent;
        }

        names.Reverse();
        return string.Join("/", names);
    }

    private void EnsureRightControlReferences()
    {
        if (textControlRobot == null)
        {
            textControlRobot = FindSceneTextByName("Text_ControlRobot");
        }

        if (textForkliftHeight == null)
        {
            textForkliftHeight = FindSceneTextByName("Text_ForkliftHeight");
        }

        if (buttonControlSelectTb3_01 == null)
        {
            buttonControlSelectTb3_01 = FindScopedSceneButton("Panel_ManualControl", "Button_Select_Group", "Button_Select_TB3_01") ??
                                       FindSceneButtonByName("Button_ControlSelect_TB3_01");
        }

        if (buttonControlSelectTb3_02 == null)
        {
            buttonControlSelectTb3_02 = FindScopedSceneButton("Panel_ManualControl", "Button_Select_Group", "Button_Select_TB3_02") ??
                                       FindSceneButtonByName("Button_ControlSelect_TB3_02");
        }

        if (buttonControlSelectTb3_03 == null)
        {
            buttonControlSelectTb3_03 = FindScopedSceneButton("Panel_ManualControl", "Button_Select_Group", "Button_Select_TB3_03") ??
                                       FindSceneButtonByName("Button_ControlSelect_TB3_03");
        }

        if (buttonForkliftLiftUp == null)
        {
            buttonForkliftLiftUp = FindFirstSceneButtonByName("Button_Forklift_LiftUp", "Button_Forklift_Up", "Button_ForkliftRaise");
        }

        if (buttonForkliftLiftDown == null)
        {
            buttonForkliftLiftDown = FindFirstSceneButtonByName("Button_Forklift_LiftDown", "Button_Forklift_Down", "Button_ForkliftLower");
        }

    }

    private void RefreshRightControlRobotText()
    {
        EnsureRightControlReferences();
        SetTextValueIfBound(textControlRobot, $"제어 로봇 : {FormatRobotIdUpper(selectedRobotId)}");
    }

    private void RefreshForkliftHeightText()
    {
        EnsureRightControlReferences();
        ResolveForkliftRuntimeController();
        string heightText = forkliftRuntimeController != null
            ? $"{Mathf.RoundToInt(forkliftRuntimeController.HeightPercent * 100f)} %"
            : "-- %";
        SetTextValueIfBound(textForkliftHeight, $"높이 : {heightText}");
    }

    private void RefreshForkliftInteractable()
    {
        EnsureRightControlReferences();
        bool canControl = CanControlForklift(false);
        if (buttonForkliftLiftUp != null)
        {
            buttonForkliftLiftUp.interactable = canControl;
        }

        if (buttonForkliftLiftDown != null)
        {
            buttonForkliftLiftDown.interactable = canControl;
        }
    }

    private void EnsureDashboardReferences()
    {
        if (dashboardReferencesResolved)
        {
            CacheDashboardMapNav2EditModeTemplate();
            return;
        }

        if (panelMainDashboardView == null)
        {
            panelMainDashboardView = FindSceneGameObjectByName("Panel_Main_DashboardView");
        }

        if (buttonBackToDashboardObject == null)
        {
            buttonBackToDashboardObject = FindSceneGameObjectByName("Button_BackToDashboard");
        }

        if (buttonDashboardFactoryCard == null)
        {
            buttonDashboardFactoryCard = FindSceneButtonByName("Button_DashboardFactoryOverviewCard");
        }

        if (buttonDashboardRobotCard == null)
        {
            buttonDashboardRobotCard = FindSceneButtonByName("Button_DashboardRobotStatusCard");
        }

        if (buttonDashboardMapCard == null)
        {
            buttonDashboardMapCard = FindSceneButtonByName("Button_DashboardMapNav2Card");
        }

        if (buttonDashboardCameraCard == null)
        {
            buttonDashboardCameraCard = FindSceneButtonByName("Button_DashboardCameraAiCard");
        }

        if (buttonBackToDashboard == null)
        {
            buttonBackToDashboard = FindSceneButtonByName("Button_BackToDashboard");
        }

        if (textDashboardFactoryOverviewBody == null)
        {
            textDashboardFactoryOverviewBody = FindSceneTextByName("Text_DashboardFactoryOverviewBody");
        }

        if (textDashboardRobotStatusBody == null)
        {
            textDashboardRobotStatusBody = FindSceneTextByName("Text_DashboardRobotStatusBody");
        }

        EnsureDashboardRobotSummaryReferences();

        if (textDashboardMapNav2Body == null)
        {
            textDashboardMapNav2Body = FindSceneTextByName("Text_DashboardMapNav2Body");
        }
        CacheDashboardMapNav2EditModeTemplate();

        if (textDashboardCameraAiBody == null)
        {
            textDashboardCameraAiBody = FindSceneTextByName("Text_DashboardCameraAiBody");
        }

        if (textDashboardSystemHealthBody == null)
        {
            textDashboardSystemHealthBody = FindSceneTextByName("Text_DashboardSystemHealthBody");
        }

        EnsureDashboardSystemHealthValueReferences();

        if (textDashboardRecentTimelineBody == null)
        {
            textDashboardRecentTimelineBody = FindSceneTextByName("Text_DashboardRecentTimelineBody");
        }

        if (textSelectedLogFilter == null)
        {
            textSelectedLogFilter = FindSceneTextByName("Text_SelectedLogFilter");
        }

        BindDashboardButtons();
        dashboardReferencesResolved = true;
    }

    private void ResolveDashboardLogFilterButtons()
    {
        buttonDashboardLogAll = ResolveDashboardLogFilterButton(
            buttonDashboardLogAll,
            "Button_LogFilter_All",
            "buttonDashboardLogAll",
            "Button_DashboardLogAll",
            "Button_DashboardTimelineFilterAll",
            "Button_DashboardLogFilterAll",
            "Button_DashboardRecentTimelineFilterAll",
            "Button_TimelineFilterAll");
        buttonDashboardLogRobot = ResolveDashboardLogFilterButton(
            buttonDashboardLogRobot,
            "Button_LogFilter_Robot",
            "buttonDashboardLogRobot",
            "Button_DashboardLogRobot",
            "Button_DashboardTimelineFilterRobot",
            "Button_DashboardLogFilterRobot",
            "Button_DashboardRecentTimelineFilterRobot",
            "Button_TimelineFilterRobot");
        buttonDashboardLogControl = ResolveDashboardLogFilterButton(
            buttonDashboardLogControl,
            "Button_LogFilter_Control",
            "buttonDashboardLogControl",
            "Button_DashboardLogControl",
            "Button_DashboardTimelineFilterControl",
            "Button_DashboardLogFilterControl",
            "Button_DashboardRecentTimelineFilterControl",
            "Button_TimelineFilterControl");
        buttonDashboardLogCamera = ResolveDashboardLogFilterButton(
            buttonDashboardLogCamera,
            "Button_LogFilter_Camera",
            "buttonDashboardLogCamera",
            "Button_DashboardLogCamera",
            "Button_DashboardTimelineFilterCamera",
            "Button_DashboardLogFilterCamera",
            "Button_DashboardRecentTimelineFilterCamera",
            "Button_TimelineFilterCamera");
        buttonDashboardLogSystem = ResolveDashboardLogFilterButton(
            buttonDashboardLogSystem,
            "Button_LogFilter_System",
            "buttonDashboardLogSystem",
            "Button_DashboardLogSystem",
            "Button_DashboardTimelineFilterSystem",
            "Button_DashboardLogFilterSystem",
            "Button_DashboardRecentTimelineFilterSystem",
            "Button_TimelineFilterSystem");
        buttonDashboardLogError = ResolveDashboardLogFilterButton(
            buttonDashboardLogError,
            "Button_LogFilter_Error",
            "buttonDashboardLogError",
            "Button_DashboardLogError",
            "Button_DashboardTimelineFilterError",
            "Button_DashboardLogFilterError",
            "Button_DashboardRecentTimelineFilterError",
            "Button_TimelineFilterError");
    }

    private Button ResolveDashboardLogFilterButton(Button current, params string[] names)
    {
        if (current != null && current.gameObject.scene.IsValid())
        {
            return current;
        }

        if (names != null && names.Length > 0)
        {
            Button exact = FindFirstSceneButtonByName(names[0]);
            if (exact != null)
            {
                return exact;
            }
        }

        return current != null ? current : FindFirstSceneButtonByName(names);
    }

    private void EnsureDashboardRobotSummaryReferences()
    {
        if (dashboardRobotSummaryReferencesResolved)
        {
            return;
        }

        if (textDashboardRobotReadyCount == null)
        {
            textDashboardRobotReadyCount = FindSceneTextByName("Text_DashboardRobotReadyCount");
        }

        dashboardRobotSummaryReferencesResolved = true;
    }

    private void EnsureDashboardSystemHealthValueReferences()
    {
        if (dashboardSystemHealthValueReferencesResolved)
        {
            return;
        }

        if (textSystemHealthServerValue == null)
        {
            textSystemHealthServerValue = FindSceneTextByName("Text_SystemHealth_ServerValue");
        }

        if (textSystemHealthWebSocketValue == null)
        {
            textSystemHealthWebSocketValue = FindSceneTextByName("Text_SystemHealth_WebSocketValue");
        }

        if (textSystemHealthRos2Value == null)
        {
            textSystemHealthRos2Value = FindSceneTextByName("Text_SystemHealth_Ros2Value");
        }

        if (textSystemHealthAiModelValue == null)
        {
            textSystemHealthAiModelValue = FindSceneTextByName("Text_SystemHealth_AiModelValue");
        }

        if (textSystemHealthDbValue == null)
        {
            textSystemHealthDbValue = FindSceneTextByName("Text_SystemHealth_DbValue");
        }

        if (textSystemHealthHealthPercent == null)
        {
            textSystemHealthHealthPercent = FindSceneTextByName("Text_SystemHealth_HealthPercent");
        }

        dashboardSystemHealthValueReferencesResolved = true;
    }

    private void EnsureFactory3DMapReferences()
    {
        if (rawImageFactory3DMapPreview == null)
        {
            rawImageFactory3DMapPreview = FindSceneGameObjectByName("RawImage_Factory3DMapPreview");
        }
    }

    private void EnsureFactoryMapModeReferences()
    {
        EnsureFactory3DMapReferences();

        if (imageMapAreaBackground == null)
        {
            imageMapAreaBackground = FindSceneGameObjectByName("Image_MapArea_Background");
        }

        if (panelMini2DMap == null)
        {
            panelMini2DMap = FindSceneGameObjectByName("Panel_Mini2DMap");
        }

        if (panelFactory3DViewControls == null)
        {
            panelFactory3DViewControls = FindSceneGameObjectByName("Panel_Factory3DViewControls");
        }

        ResolveFactory2DGlobalCameraReferences();

        if (buttonToggleFactoryMapMode == null)
        {
            buttonToggleFactoryMapMode = FindSceneButtonByName("Button_ToggleFactoryMapMode");
        }

        if (textToggleFactoryMapMode == null)
        {
            textToggleFactoryMapMode = FindSceneTextByName("Text_Button_ToggleFactoryMapMode");
        }

        BindFactoryMapModeButton();
    }

    private void UpdateFactoryMapModeButtonLabel()
    {
        if (textToggleFactoryMapMode != null)
        {
            textToggleFactoryMapMode.text = isFactory3DMapMode ? "2D 공장맵" : "3D 공장뷰";
        }
    }

    private void EnsureFactoryViewRuntimeReferences()
    {
        EnsureFactoryMapModeReferences();
        textFactoryViewTitle ??= FindSceneTextByName("Text_FactoryViewTitle");
        textMini2DMapTitle ??= FindSceneTextByName("Text_Mini2DMapTitle");
        full2DMapController ??= FindSceneComponentByType<scr_FactoryFull2DMapController>();
        mini2DMapController ??= FindSceneComponentByType<scr_FactoryMini2DMapController>();
        factory3DRobotMarkerController ??= FindSceneComponentByType<scr_Factory3DRobotMarkerController>();
        factory2DPeopleMarkerController ??= FindSceneComponentByType<scr_Factory2DPeopleMarkerController>();
    }

    private void RefreshFactory2DPeopleMarkers()
    {
        EnsureFactory2DPeopleMarkerController();
        factory2DPeopleMarkerController?.RefreshPeopleMarkers();
    }

    private void EnsureFactory2DPeopleMarkerController()
    {
        factory2DPeopleMarkerController ??= FindSceneComponentByType<scr_Factory2DPeopleMarkerController>();
    }

    private void ResolveFactory2DGlobalCameraReferences()
    {
        panelFactory2DGlobalCamera ??= FindSceneGameObjectByName("Panel_Factory2DGlobalCamera");
        rawImageFactory2DGlobalCctv ??= FindSceneRawImageByName("RawImage_Factory2DGlobalCctv");
    }

    private void ApplyFactoryViewKoreanLabels()
    {
        EnsureFactoryViewRuntimeReferences();
        UpdateFactoryMapModeButtonLabel();
    }

    private void RefreshFactoryChargingZoneStatus()
    {
        bool charging = false;
        foreach (string robotId in GetPatrolTurtlebotIds())
        {
            if (robotStatesById.TryGetValue(robotId, out RobotStateData state) &&
                IsNormalizedState(state.FsmState, "CHARGING"))
            {
                charging = true;
                break;
            }
        }

        SetChargingTextPair("Text_Charging_Zone_1", charging, "충전 중");
        SetChargingTextPair("Text_Charging_Zone_2", !charging, "대기");
    }

    private static void SetChargingTextPair(string objectName, bool active, string text)
    {
        foreach (TMP_Text item in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (item == null || item.name != objectName || !item.gameObject.scene.IsValid())
            {
                continue;
            }

            item.text = text;
            item.gameObject.SetActive(active);
        }
    }

    private void RefreshFactoryIncidentMarkers()
    {
        EnsureFactoryViewRuntimeReferences();
        RefreshFactoryIncidentMarker("NO_HELMET");
        RefreshFactoryIncidentMarker("FALL");
        RefreshFactoryIncidentMarker("FIRE");
    }

    private void RefreshFactoryIncidentMarker(string incidentType)
    {
        if (TryGetLatestActiveFactoryIncident(incidentType, out ActiveAlertItem item))
        {
            full2DMapController?.SetIncidentMarker(incidentType, item.LocationX, item.LocationY, true);
            mini2DMapController?.SetIncidentMarker(incidentType, item.LocationX, item.LocationY, true);
            factory3DRobotMarkerController?.SetIncidentMarker(incidentType, item.LocationX, item.LocationY, true);
            return;
        }

        full2DMapController?.SetIncidentMarker(incidentType, 0f, 0f, false);
        mini2DMapController?.SetIncidentMarker(incidentType, 0f, 0f, false);
        factory3DRobotMarkerController?.SetIncidentMarker(incidentType, 0f, 0f, false);
    }

    private bool TryGetLatestActiveFactoryIncident(string incidentType, out ActiveAlertItem latest)
    {
        latest = null;
        string normalizedTarget = NormalizeFactoryIncidentType(incidentType);
        foreach (int logId in activeAlertLogIds)
        {
            if (!activeAlertsByLogId.TryGetValue(logId, out ActiveAlertItem item) ||
                item == null ||
                !item.HasLocation ||
                !IsActiveAlertStatus(item.Status) ||
                NormalizeFactoryIncidentType(item.IncidentType) != normalizedTarget)
            {
                continue;
            }

            if (latest == null || CompareAlertItemsByTimestampAscending(latest, item) < 0)
            {
                latest = item;
            }
        }

        return latest != null;
    }

    private static string NormalizeFactoryIncidentType(string incidentType)
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

    private void EnsureFactoryConveyorRuntimeControllers()
    {
        // Conveyor animation components are authored in Edit Mode on the conveyor prefabs.
        // Runtime intentionally does not add components or create conveyor objects.
    }

    private static string NormalizePopupValue(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string NormalizePopupTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "ALERT";
        }

        return title.Trim().ToUpperInvariant().Replace(' ', '_');
    }

    private static GameObject FindSceneGameObjectByName(string objectName)
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

    private void ResolvePreviewCamerasOnce()
    {
        if (previewCamerasResolved)
        {
            return;
        }

        factory3DPreviewCamera = FindSceneCameraByName("Camera_Factory3DMap");
        robotViewPreviewCamera = FindSceneCameraByName("PreviewCamera");
        dashboardPreviewCamera01 = FindSceneCameraByName("DashboardPreviewCamera_01");
        dashboardPreviewCamera02 = FindSceneCameraByName("DashboardPreviewCamera_02");
        dashboardPreviewCamera03 = FindSceneCameraByName("DashboardPreviewCamera_03");
        previewCamerasResolved = true;
    }

    private void UpdatePreviewCameraRenderingState()
    {
        ResolvePreviewCamerasOnce();

        bool factoryCameraActive = IsViewActive(panelMainFactoryView) && isFactory3DMapMode;
        bool robotCameraActive = IsViewActive(panelMainRobotView);
        bool dashboardCamerasActive = IsViewActive(panelMainDashboardView);

        SetCameraEnabledIfChanged(factory3DPreviewCamera, factoryCameraActive);
        SetCameraEnabledIfChanged(robotViewPreviewCamera, robotCameraActive);
        SetCameraEnabledIfChanged(dashboardPreviewCamera01, dashboardCamerasActive);
        SetCameraEnabledIfChanged(dashboardPreviewCamera02, dashboardCamerasActive);
        SetCameraEnabledIfChanged(dashboardPreviewCamera03, dashboardCamerasActive);
    }

    private static Camera FindSceneCameraByName(string objectName)
    {
        GameObject cameraObject = FindSceneGameObjectByName(objectName);
        return cameraObject != null ? cameraObject.GetComponent<Camera>() : null;
    }

    private static void SetCameraEnabledIfChanged(Camera targetCamera, bool enabled)
    {
        if (targetCamera != null && targetCamera.enabled != enabled)
        {
            targetCamera.enabled = enabled;
        }
    }

    private static TMP_Text FindSceneTextByName(string objectName)
    {
        GameObject item = FindSceneGameObjectByName(objectName);
        return item != null ? item.GetComponent<TMP_Text>() : null;
    }

    private static Button FindSceneButtonByName(string objectName)
    {
        GameObject item = FindSceneGameObjectByName(objectName);
        return item != null ? item.GetComponent<Button>() : null;
    }

    private static RawImage FindSceneRawImageByName(string objectName)
    {
        GameObject item = FindSceneGameObjectByName(objectName);
        return item != null ? item.GetComponent<RawImage>() : null;
    }

    private static T FindSceneComponentByType<T>() where T : Component
    {
        foreach (T item in Resources.FindObjectsOfTypeAll<T>())
        {
            if (item != null && item.gameObject.scene.IsValid())
            {
                return item;
            }
        }

        return null;
    }

    private static T FindChildComponentByName<T>(Transform root, string objectName) where T : Component
    {
        if (root == null || string.IsNullOrWhiteSpace(objectName))
        {
            return null;
        }

        T[] components = root.GetComponentsInChildren<T>(true);
        foreach (T component in components)
        {
            if (component != null && component.name == objectName)
            {
                return component;
            }
        }

        return null;
    }

    private static Button FindFirstSceneButtonByName(params string[] objectNames)
    {
        if (objectNames == null)
        {
            return null;
        }

        foreach (string objectName in objectNames)
        {
            Button button = FindSceneButtonByName(objectName);
            if (button != null)
            {
                return button;
            }
        }

        return null;
    }

    public void SendAutoRobotCommand(string command)
    {
        _ = SendRobotCommandV2Async("AUTO", command, 0f, 0f, 0, "NONE", string.Empty);
    }

    public void SendManualRobotCommand(string command, float linearX, float angularZ, int durationMs)
    {
        SyncManualTargetWithSelectedRobot();
        _ = SendRobotCommandV2Async(
            "MANUAL",
            command,
            linearX,
            angularZ,
            durationMs,
            string.IsNullOrWhiteSpace(selectedManualTargetType) ? "NONE" : selectedManualTargetType,
            selectedManualTargetId ?? string.Empty);
    }

    public void EnterManualMode()
    {
        SyncManualTargetWithSelectedRobot();
        _ = SendRobotCommandV2Async("MANUAL", "MANUAL_ENTER", 0f, 0f, 0, selectedManualTargetType, selectedManualTargetId);
    }

    public void ExitManualMode()
    {
        SyncManualTargetWithSelectedRobot();
        _ = SendRobotCommandV2Async("MANUAL", "MANUAL_EXIT", 0f, 0f, 0, selectedManualTargetType, selectedManualTargetId);
    }

    public void SendManualForward()
    {
        SendManualCommand("MANUAL_FORWARD");
    }

    public void SendManualBackward()
    {
        SendManualCommand("MANUAL_BACKWARD");
    }

    public void SendManualLeft()
    {
        SendManualCommand("MANUAL_LEFT");
    }

    public void SendManualRight()
    {
        SendManualCommand("MANUAL_RIGHT");
    }

    public void SendManualStop()
    {
        SendManualCommand("MANUAL_STOP");
    }

    private async Task ExitManualThenResumeAsync()
    {
        SyncManualTargetWithSelectedRobot();
        await SendRobotCommandV2Async("MANUAL", "MANUAL_EXIT", 0f, 0f, 0, selectedManualTargetType, selectedManualTargetId);
        await SendRobotCommandV2Async("AUTO", "RESUME", 0f, 0f, 0, "NONE", string.Empty);
    }

    private void SyncManualTargetWithSelectedRobot()
    {
        selectedManualTargetType = "ROBOT";
        selectedManualTargetId = string.IsNullOrWhiteSpace(selectedRobotId) ? "tb3-01" : selectedRobotId.Trim();
    }

    private async Task<bool> SendRobotCommandV2Async(string controlMode, string command, float linearX, float angularZ, int durationMs, string targetType, string targetId, bool addEventLog = true)
    {
        string normalizedMode = string.IsNullOrWhiteSpace(controlMode) ? "AUTO" : controlMode.Trim().ToUpperInvariant();
        string normalizedCommand = string.IsNullOrWhiteSpace(command) ? "UNKNOWN_COMMAND" : command.Trim().ToUpperInvariant();
        bool isManualRealtimeCommand = IsManualRealtimeCommand(normalizedCommand);
        string logLevel = isManualRealtimeCommand ? "MANUAL" : "CMD";

        lastCommand = GetShortCommand(normalizedCommand);
        lastCommandResult = "Sending";
        lastAck = "--";
        SetRobotCommandViewState(selectedRobotId, normalizedCommand, "SENDING", "--", "--");
        RefreshRobotViewPanel();

        if (normalizedCommand == "CLEAR_ALERT")
        {
            lastCommandResult = "Skipped";
            SetRobotCommandViewState(selectedRobotId, normalizedCommand, "SKIPPED", "--", "--");
            RefreshRobotViewPanel();
            return true;
        }

        if (normalizedCommand == "PAUSE_MISSION")
        {
            const string unsupportedPauseMessage = "PAUSE_MISSION is not supported by current REST command API spec";
            lastCommandResult = "Rejected";
            SetRobotCommandViewState(selectedRobotId, normalizedCommand, "REJECTED", unsupportedPauseMessage, "--");
            RefreshRobotViewPanel();
            Debug.LogWarning(unsupportedPauseMessage);
            AddEventLog("WARN", unsupportedPauseMessage);
            return false;
        }

        ResolveRobotApiClient();
        if (robotApiClient == null)
        {
            lastCommandResult = "FAILED";
            lastAck = "robot api client missing";
            SetRobotCommandViewState(selectedRobotId, normalizedCommand, "FAILED", "robot api client missing", "--");
            RefreshRobotViewPanel();
            Debug.LogWarning("[REST] scr_ControlTowerRobotApiClient not found.");
            AddEventLog("ERROR", "command request failed: robot api client missing");
            return false;
        }

        RobotApiResult result;
        string restCommand = normalizedCommand;
        if (isManualRealtimeCommand)
        {
            if (addEventLog)
            {
                AddEventLog(logLevel, $"{normalizedCommand} sent to {selectedRobotId} vx={linearX:0.00} wz={angularZ:0.00}");
            }
            result = await robotApiClient.SendTeleopAsync(selectedRobotId, linearX, angularZ);
        }
        else if (TryMapRestCommand(normalizedCommand, out restCommand))
        {
            result = await robotApiClient.SendCommandAsync(selectedRobotId, restCommand, operatorId);
        }
        else
        {
            string unsupportedMessage = $"{normalizedCommand} is not supported by current REST command API spec";
            lastCommandResult = "Rejected";
            SetRobotCommandViewState(selectedRobotId, normalizedCommand, "REJECTED", unsupportedMessage, "--");
            RefreshRobotViewPanel();
            Debug.LogWarning(unsupportedMessage);
            AddEventLog("WARN", unsupportedMessage);
            return false;
        }

        if (result.Success)
        {
            lastCommandResult = isManualRealtimeCommand ? "Sent" : "Accepted";
            lastAck = string.IsNullOrWhiteSpace(result.CommandId) ? "--" : result.CommandId;
        }
        else if (result.Rejected)
        {
            lastCommandResult = "Rejected";
            lastAck = result.Message;
        }
        else
        {
            lastCommandResult = "FAILED";
            lastAck = result.Message;
        }

        SetRobotCommandViewState(
            selectedRobotId,
            isManualRealtimeCommand ? normalizedCommand : restCommand,
            lastCommandResult,
            result.Message,
            "--");
        RefreshRobotViewPanel();
        if (result.Success && !isManualRealtimeCommand && addEventLog)
        {
            AddEventLog("CMD", $"{restCommand} accepted for {selectedRobotId}");
        }
        else if (!result.Success)
        {
            string failureMessage = string.IsNullOrWhiteSpace(result.Message) ? "No server message" : result.Message;
            Debug.LogWarning($"[REST] {restCommand} {lastCommandResult}: {failureMessage}");
            if (result.Rejected)
            {
                AddEventLog("WARN", $"{restCommand} rejected for {selectedRobotId}: {failureMessage}");
            }
            else
            {
                AddEventLog("ERROR", $"command request failed: {restCommand} for {selectedRobotId}: {failureMessage}");
            }
        }

        return result.Success;
    }

    private static bool TryMapRestCommand(string command, out string restCommand)
    {
        switch ((command ?? string.Empty).Trim().ToUpperInvariant())
        {
            case "PATROL_START":
            case "START_PATROL":
                restCommand = "PATROL_START";
                return true;
            case "RESUME":
            case "RESUME_MISSION":
                restCommand = "RESUME";
                return true;
            case "MANUAL_ENTER":
            case "ENTER_MANUAL_MODE":
                restCommand = "MANUAL_ENTER";
                return true;
            case "MANUAL_EXIT":
            case "EXIT_MANUAL_MODE":
                restCommand = "MANUAL_EXIT";
                return true;
            case "RETURN_TO_CHARGER":
                restCommand = "RETURN_TO_CHARGER";
                return true;
            case "EMERGENCY_STOP":
                restCommand = "EMERGENCY_STOP";
                return true;
            case "RESET":
                restCommand = "RESET";
                return true;
            default:
                restCommand = string.Empty;
                return false;
        }
    }

    private static bool IsManualRealtimeCommand(string command)
    {
        switch ((command ?? string.Empty).Trim().ToUpperInvariant())
        {
            case "MANUAL_FORWARD":
            case "MANUAL_BACKWARD":
            case "MANUAL_LEFT":
            case "MANUAL_RIGHT":
            case "MANUAL_STOP":
                return true;
            default:
                return false;
        }
    }

    private void LogSelectedRobotJson(string robotId)
    {
        string json =
            "{\n" +
            $"  \"selected_robot_id\": \"{robotId}\",\n" +
            $"  \"operator_id\": \"{operatorId}\",\n" +
            $"  \"timestamp\": \"{GetIsoTimestamp()}\"\n" +
            "}";

        Debug.Log("[Unity -> Server] POST /api/v1/monitor/selected-robot");
        AddEventLog("UI", $"Selected {robotId}");
    }

    private void LogAlertAckJson(int alertId, string action, string memo)
    {
        string alertType = currentAlertType == "NONE" ? "UNKNOWN" : currentAlertType;
        string json =
            "{\n" +
            $"  \"alert_type\": \"{alertType}\",\n" +
            $"  \"alert_id\": {alertId},\n" +
            $"  \"action\": \"{action}\",\n" +
            $"  \"ack_by\": \"{operatorId}\",\n" +
            $"  \"ack_at\": \"{GetIsoTimestamp()}\",\n" +
            $"  \"memo\": \"{memo}\"\n" +
            "}";

        Debug.Log("[Unity -> Server] alert_ack");
        AddEventLog("API", $"{action} {alertType} #{alertId}");

        ResolveWebSocketClient();
        if (webSocketClient != null)
        {
            webSocketClient.SendAlertAck(alertType, alertId, action, operatorId, memo);
        }
    }

    private void LogViewChangeJson(string viewMode)
    {
        string json =
            "{\n" +
            $"  \"operator_id\": \"{operatorId}\",\n" +
            $"  \"view_mode\": \"{viewMode}\",\n" +
            $"  \"selected_robot_id\": \"{selectedRobotId}\",\n" +
            $"  \"timestamp\": \"{GetIsoTimestamp()}\"\n" +
            "}";

        Debug.Log("[Unity -> Server] POST /api/v1/monitor/view-log");
        AddEventLog("UI", $"{GetShortViewMode(viewMode)} selected");
    }

    private void LogDemoAlertWebSocketJson(int alertId, string alertType, string location)
    {
        string json =
            "{\n" +
            "  \"event\": \"violation_alert\",\n" +
            $"  \"sent_at\": \"{GetIsoTimestamp()}\",\n" +
            "  \"payload\": {\n" +
            $"    \"alert_id\": {alertId},\n" +
            $"    \"violation_type\": \"{alertType}\",\n" +
            "    \"title\": \"Safety helmet violation detected\",\n" +
            "    \"severity\": \"medium\",\n" +
            $"    \"robot_id\": \"{selectedRobotId}\",\n" +
            $"    \"robot_location\": \"{location}\",\n" +
            "    \"photo_url\": \"/static/alerts/no_helmet/demo.jpg\",\n" +
            "    \"ai_details\": { \"confidence\": 0.91 }\n" +
            "  }\n" +
            "}";

        Debug.Log("[Server -> Unity Demo WebSocket]");
        AddEventLog("EVENT", $"{alertType} detected");
    }

    private string GetShortViewMode(string viewMode)
    {
        return viewMode switch
        {
            "FACTORY_VIEW" => "Factory View",
            "ROBOT_VIEW" => "Robot View",
            "MAP_STATUS_VIEW" => "Map Status",
            "CAMERA_VIEW" => "Camera View",
            _ => viewMode
        };
    }

    private string GetShortCommand(string command)
    {
        if (command.StartsWith("MANUAL_", StringComparison.Ordinal))
        {
            return GetShortManualCommand(command);
        }

        return command;
    }

    private string BuildTimelineLine(string state)
    {
        bool active = currentFsmState == state || (state == "PAUSED" && currentFsmState.Contains("PAUSED"));
        return active ? $"> {state}" : $"  {state}";
    }

    private static string FormatServerTimestampForDisplay(string timestamp)
    {
        if (string.IsNullOrWhiteSpace(timestamp))
        {
            return "--";
        }

        string trimmed = timestamp.Trim();
        return DateTime.TryParse(trimmed, out DateTime parsed)
            ? parsed.ToString("yyyy-MM-dd HH:mm:ss")
            : trimmed;
    }

    private static string FormatServerStateForDisplay(string state)
    {
        string normalized = string.IsNullOrWhiteSpace(state) ? "UNKNOWN" : state.Trim().ToUpperInvariant();
        return normalized switch
        {
            "IDLE" => "IDLE",
            "LOCALIZING" => "LOCALIZING",
            "PATROLLING" => "PATROLLING",
            "PAUSED" => "PAUSED",
            "CHARGING" => "CHARGING",
            "RETURNING_TO_CHARGER" => "RETURNING_TO_CHARGER",
            "EMERGENCY_STOP" => "EMERGENCY_STOP",
            "COMPLETED" => "COMPLETED",
            "FAILED" => "FAILED",
            "IN_PROGRESS" => "IN_PROGRESS",
            _ => normalized
        };
    }

    private static string FormatPauseReasonForDisplay(string pauseReason)
    {
        string normalized = string.IsNullOrWhiteSpace(pauseReason) ? string.Empty : pauseReason.Trim().ToUpperInvariant();
        return normalized switch
        {
            "" => string.Empty,
            "MANUAL_DONE" => "수동 조작 종료",
            "EVENT_HELMET" or "NO_HELMET" => "안전모 미착용",
            "EVENT_FALL" => "쓰러짐 감지",
            "EVENT_FIRE" => "화재 감지",
            "EMERGENCY" or "EMERGENCY_STOP" => "긴급 정지",
            _ => normalized
        };
    }

    private void EnsureRobotViewTextReferences()
    {
        if (textRobotOverviewBody == null)
        {
            textRobotOverviewBody = FindRobotViewTextByName("Text_RobotOverviewBody");
        }

        if (textRobotTimelineBody == null)
        {
            textRobotTimelineBody = FindRobotViewTextByName("Text_RobotTimelineBody");
        }

        if (textCommandStateBody == null)
        {
            textCommandStateBody = FindRobotViewTextByName("Text_CommandStateBody");
        }

        if (textRobotAlertBody == null)
        {
            textRobotAlertBody = FindRobotViewTextByName("Text_RobotAlertBody");
        }
    }

    private TMP_Text FindRobotViewTextByName(string objectName)
    {
        Transform root = panelMainRobotView != null ? panelMainRobotView.transform : transform.root;
        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text text in texts)
        {
            if (text != null && text.name == objectName)
            {
                return text;
            }
        }

        return null;
    }

    private void EnsureMapStatusViewTextReferences()
    {
        if (textSlamLocalizationBody == null)
        {
            textSlamLocalizationBody = FindMapStatusViewTextByName("Text_SlamLocalizationBody");
        }

        if (textNav2MissionBody == null)
        {
            textNav2MissionBody = FindMapStatusViewTextByName("Text_Nav2MissionBody");
        }

        if (textWaypointRouteBody == null)
        {
            textWaypointRouteBody = FindMapStatusViewTextByName("Text_WaypointRouteBody");
        }

        if (textObstacleRecoveryBody == null)
        {
            textObstacleRecoveryBody = FindMapStatusViewTextByName("Text_ObstacleRecoveryBody");
        }
    }

    private void EnsureMapStatusRouteController()
    {
        if (mapStatusRouteController != null)
        {
            return;
        }

        Transform root = panelMainMapStatusView != null ? panelMainMapStatusView.transform : transform.root;
        mapStatusRouteController = root != null ? root.GetComponentInChildren<scr_MapStatusRouteController>(true) : null;
    }

    private TMP_Text FindMapStatusViewTextByName(string objectName)
    {
        Transform root = panelMainMapStatusView != null ? panelMainMapStatusView.transform : transform.root;
        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text text in texts)
        {
            if (text != null && text.name == objectName)
            {
                return text;
            }
        }

        return null;
    }

    private string BuildWaypointLine(int index)
    {
        int currentIndex = GetCurrentWaypointNumber();
        string prefix = currentIndex == index ? "> " : "  ";
        return $"{prefix}WP{index}";
    }

    private int GetCurrentWaypointNumber()
    {
        if (string.IsNullOrWhiteSpace(currentWaypointIndex))
        {
            return 0;
        }

        string[] parts = currentWaypointIndex.Split('/');
        if (parts.Length > 0 && int.TryParse(parts[0].Trim(), out int waypoint))
        {
            return waypoint;
        }

        return 0;
    }

    private string GetTotalWaypointCount()
    {
        if (string.IsNullOrWhiteSpace(currentWaypointIndex) || currentWaypointIndex.Trim() == "--")
        {
            return "--";
        }

        string[] parts = currentWaypointIndex.Split('/');
        return parts.Length > 1 ? parts[1].Trim() : "--";
    }

    private string GetLocalizationQuality()
    {
        if (string.IsNullOrWhiteSpace(currentLocalization) || currentLocalization.Trim() == "--")
        {
            return "--";
        }

        return currentLocalization.Contains("Stable", StringComparison.OrdinalIgnoreCase) ? "Good" : "Initializing";
    }

    private string GetStuckReason()
    {
        if (currentFsmState == "STUCK")
        {
            return currentPathState;
        }

        return "--";
    }

    private void EnsureCameraViewTextReferences()
    {
        if (textMainCameraFeedSelected == null)
        {
            textMainCameraFeedSelected = FindCameraViewTextByName("Text_MainCameraFeedSelected");
        }

        if (textGlobalCctvBody == null)
        {
            textGlobalCctvBody = FindCameraViewTextByName("Text_GlobalCctvBody");
        }

        if (textTb3CameraBody == null)
        {
            textTb3CameraBody = FindCameraViewTextByName("Text_Tb3CameraBody");
        }

        if (textAiDetectionBody == null)
        {
            textAiDetectionBody = FindCameraViewTextByName("Text_AiDetectionBody");
        }

        if (textCameraAiStatusBody == null)
        {
            textCameraAiStatusBody = FindCameraViewTextByName("Text_CameraAiStatusBody");
        }
    }

    private void CacheCameraAiStatusEditModeTemplate()
    {
        if (textCameraAiStatusBody == null || cameraAiStatusTemplateSource == textCameraAiStatusBody)
        {
            return;
        }

        cameraAiStatusTemplateSource = textCameraAiStatusBody;
        cameraAiStatusEditModeTemplate = textCameraAiStatusBody.text ?? string.Empty;
    }

    private void EnsureCameraViewSnapshotReference()
    {
        if (imageEventSnapshotPlaceholder == null && rawImageEventSnapshotPlaceholder == null)
        {
            Image snapshotImage = FindChildComponentByName<Image>(
                panelMainCameraView != null ? panelMainCameraView.transform : null,
                "Image_EventSnapshotPlaceholder");
            RawImage snapshotRawImage = FindChildComponentByName<RawImage>(
                panelMainCameraView != null ? panelMainCameraView.transform : null,
                "Image_EventSnapshotPlaceholder");
            GameObject snapshot = snapshotImage != null
                ? snapshotImage.gameObject
                : snapshotRawImage != null
                    ? snapshotRawImage.gameObject
                    : FindSceneGameObjectByName("Image_EventSnapshotPlaceholder");
            imageEventSnapshotPlaceholder = snapshot != null ? snapshot.GetComponent<Image>() : null;
            rawImageEventSnapshotPlaceholder = snapshot != null ? snapshot.GetComponent<RawImage>() : null;
        }

        if (textEventSnapshotPlaceholder == null)
        {
            Transform snapshotTransform = imageEventSnapshotPlaceholder != null
                ? imageEventSnapshotPlaceholder.transform
                : rawImageEventSnapshotPlaceholder != null
                    ? rawImageEventSnapshotPlaceholder.transform
                    : null;
            textEventSnapshotPlaceholder = FindChildComponentByName<TMP_Text>(snapshotTransform, "Text_EventSnapshotPlaceholder");
        }
    }

    private void LogCameraSnapshotBindingsOnce()
    {
        if (cameraSnapshotBindingReportWritten)
        {
            return;
        }

        cameraSnapshotBindingReportWritten = true;
        Component eventSnapshotComponent = imageEventSnapshotPlaceholder != null
            ? imageEventSnapshotPlaceholder
            : rawImageEventSnapshotPlaceholder;
        Component popupSnapshotComponent = popupSnapshotPlaceholderImage != null
            ? popupSnapshotPlaceholderImage
            : popupSnapshotPlaceholderRawImage;
        Debug.Log(
            "[CameraBinding]\n" +
            $"EventSnapshotComponent={DescribeRuntimeComponent(eventSnapshotComponent)}\n" +
            $"EventSnapshotText={DescribeRuntimeComponent(textEventSnapshotPlaceholder)}\n" +
            $"PopupImageComponent={DescribeRuntimeComponent(popupSnapshotComponent)}");

        if (eventSnapshotComponent == null || popupSnapshotComponent == null)
        {
            Debug.LogError("[CameraBinding] Event Snapshot or Popup snapshot component is missing.");
        }
    }

    private static string DescribeRuntimeComponent(Component component)
    {
        return component == null
            ? "<null>"
            : $"{component.GetType().Name} {GetTransformPath(component.transform)}#{component.GetInstanceID()}";
    }

    private void EnsureMainCameraFeedSelectionReferences()
    {
        if (textMainCameraFeedSelected == null)
        {
            textMainCameraFeedSelected = FindCameraViewTextByName("Text_MainCameraFeedSelected");
            WarnMissingMainFeedObjectOnce(textMainCameraFeedSelected != null, "Text_MainCameraFeedSelected");
        }

        if (buttonMainFeedGlobalCctv == null)
        {
            buttonMainFeedGlobalCctv = FindSceneButtonByName("Button_MainFeed_GlobalCCTV");
            WarnMissingMainFeedObjectOnce(buttonMainFeedGlobalCctv != null, "Button_MainFeed_GlobalCCTV");
        }

        if (buttonMainFeedTb3_01 == null)
        {
            buttonMainFeedTb3_01 = FindSceneButtonByName("Button_MainFeed_TB3_01");
            WarnMissingMainFeedObjectOnce(buttonMainFeedTb3_01 != null, "Button_MainFeed_TB3_01");
        }

        if (buttonMainFeedTb3_02 == null)
        {
            buttonMainFeedTb3_02 = FindSceneButtonByName("Button_MainFeed_TB3_02");
            WarnMissingMainFeedObjectOnce(buttonMainFeedTb3_02 != null, "Button_MainFeed_TB3_02");
        }

        if (buttonMainFeedTb3_03 == null)
        {
            buttonMainFeedTb3_03 = FindSceneButtonByName("Button_MainFeed_TB3_03");
            WarnMissingMainFeedObjectOnce(buttonMainFeedTb3_03 != null, "Button_MainFeed_TB3_03");
        }
    }

    private void RefreshMainCameraFeedSelectedText()
    {
        EnsureCameraViewTextReferences();
        if (textMainCameraFeedSelected != null)
        {
            textMainCameraFeedSelected.text = $"Main Feed : {currentMainCameraFeedLabel}";
        }
    }

    private bool IsGlobalMainCameraFeedSelected()
    {
        return string.Equals(selectedMainFeedRobotId, "GLOBAL_CCTV", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsTb3NoStreamMainFeedSelected()
    {
        return string.Equals(selectedMainFeedRobotId, "tb3-03", StringComparison.OrdinalIgnoreCase);
    }

    private string GetCameraViewTb3RobotKey()
    {
        if (!IsGlobalMainCameraFeedSelected())
        {
            return NormalizeRobotKey(selectedMainFeedRobotId);
        }

        return NormalizeRobotKey(selectedRobotId);
    }

    private string GetCameraViewTb3RobotDisplay()
    {
        return FormatCameraRobotLabel(GetCameraViewTb3RobotKey());
    }

    private string GetCameraViewTb3ChannelDisplay()
    {
        string robotKey = GetCameraViewTb3RobotKey();
        return robotKey switch
        {
            "tb3-01" => "TB3-01",
            "tb3-02" => "TB3-02",
            _ => "영상 없음"
        };
    }

    private string GetCameraViewTb3ConnectionDisplay()
    {
        if (IsTb3NoStreamMainFeedSelected())
        {
            return "--";
        }

        return FormatCameraConnectionStatusDisplay(currentCameraStatus);
    }

    private string GetCameraViewTb3FrameReceiveDisplay()
    {
        if (IsTb3NoStreamMainFeedSelected())
        {
            return "영상 없음";
        }

        return FormatCameraFrameReceiveDisplay(currentCameraStatus, GetSelectedTb3LastFrameTime());
    }

    private string GetCameraViewTb3FsmDisplay()
    {
        string robotKey = GetCameraViewTb3RobotKey();
        return robotStatesById.TryGetValue(robotKey, out RobotStateData state)
            ? NormalizeDashValue(state.FsmState)
            : "--";
    }

    private string GetCameraViewTb3LastFrameDisplay()
    {
        if (IsTb3NoStreamMainFeedSelected())
        {
            return "--";
        }

        return FormatCameraLastFrameDisplay(GetSelectedTb3LastFrameTime());
    }

    private string GetSelectedTb3LastFrameTime()
    {
        string robotKey = GetCameraViewTb3RobotKey();
        if (!string.Equals(currentSelectedTb3FrameRobotId, robotKey, StringComparison.OrdinalIgnoreCase))
        {
            return "--";
        }

        return currentSelectedTb3LastFrame;
    }

    private static string FormatCameraRobotLabel(string robotId)
    {
        string normalized = NormalizeRobotKey(robotId);
        return normalized.StartsWith("tb3-", StringComparison.OrdinalIgnoreCase)
            ? normalized.ToUpperInvariant()
            : "--";
    }

    private static string FormatCameraConnectionStatusDisplay(string status)
    {
        string normalized = string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim().ToUpperInvariant();
        return normalized switch
        {
            "CONNECTED" => "연결됨",
            "CONNECTING" => "연결 중",
            "DISCONNECTED" => "연결 끊김",
            "CLOSED" => "연결 끊김",
            "WAITING" => "수신 대기",
            "WAITING FOR FRAME" => "수신 대기",
            "NO STREAM" => "영상 없음",
            "ERROR" => "오류",
            _ => string.IsNullOrWhiteSpace(status) ? "--" : status.Trim()
        };
    }

    private static string FormatCameraFrameReceiveDisplay(string status, string lastFrameTime)
    {
        string normalized = string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim().ToUpperInvariant();
        if (normalized == "NO STREAM")
        {
            return "영상 없음";
        }

        if (HasCameraFrameTime(lastFrameTime))
        {
            return "수신 중";
        }

        return normalized switch
        {
            "CONNECTED" => "수신 대기",
            "CONNECTING" => "수신 대기",
            "WAITING" => "수신 대기",
            "WAITING FOR FRAME" => "수신 대기",
            _ => "--"
        };
    }

    private static string FormatCameraLastFrameDisplay(string lastFrameTime)
    {
        return HasCameraFrameTime(lastFrameTime) ? lastFrameTime.Trim() : "--";
    }

    private static bool HasCameraFrameTime(string lastFrameTime)
    {
        return !string.IsNullOrWhiteSpace(lastFrameTime) && lastFrameTime.Trim() != "--" && lastFrameTime.Trim() != "-";
    }

    private string FormatCameraAlertLocationDisplay(string location)
    {
        string normalized = NormalizeDashValue(location);
        if (normalized == "--")
        {
            return "--";
        }

        return TryParseIncidentLocation(normalized, out _, out _)
            ? BuildAlertLocationDisplay(normalized)
            : normalized;
    }

    private string GetMainFeedTb3RobotDisplay()
    {
        return IsGlobalMainCameraFeedSelected() ? "-" : selectedMainFeedRobotId;
    }

    private string GetMainFeedTb3StatusDisplay()
    {
        if (IsGlobalMainCameraFeedSelected())
        {
            return "Not Selected";
        }

        if (string.Equals(selectedMainFeedRobotId, "tb3-03", StringComparison.OrdinalIgnoreCase))
        {
            return "No Stream";
        }

        return string.IsNullOrWhiteSpace(currentCameraStatus) ? "Disconnected" : currentCameraStatus;
    }

    private string GetMainFeedStreamTypeDisplay()
    {
        if (IsGlobalMainCameraFeedSelected() || string.Equals(selectedMainFeedRobotId, "tb3-03", StringComparison.OrdinalIgnoreCase))
        {
            return "-";
        }

        return string.IsNullOrWhiteSpace(currentStreamType) ? "WebSocket JPEG" : currentStreamType;
    }

    private string GetMainFeedLastFrameDisplay()
    {
        if (IsGlobalMainCameraFeedSelected() || !string.Equals(currentCameraStatus, "Connected", StringComparison.OrdinalIgnoreCase))
        {
            return "-";
        }

        return string.IsNullOrWhiteSpace(currentLastFrame) ? "-" : currentLastFrame;
    }

    private string GetMainFeedRotateStateDisplay()
    {
        if (IsGlobalMainCameraFeedSelected() || string.Equals(selectedMainFeedRobotId, "tb3-03", StringComparison.OrdinalIgnoreCase))
        {
            return "-";
        }

        return string.IsNullOrWhiteSpace(currentRotateState) ? "Fixed" : currentRotateState;
    }

    private string GetMainFeedConnectionStateDisplay()
    {
        if (IsGlobalMainCameraFeedSelected() || string.Equals(selectedMainFeedRobotId, "tb3-03", StringComparison.OrdinalIgnoreCase))
        {
            return "-";
        }

        return string.Equals(currentCameraStatus, "Connected", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(currentCameraStatus, "Connecting", StringComparison.OrdinalIgnoreCase)
            ? currentCommStatus
            : "Disconnected";
    }

    private void WarnMissingMainFeedObjectOnce(bool found, string objectName)
    {
        if (found || string.IsNullOrWhiteSpace(objectName) || mainFeedMissingWarnings.Contains(objectName))
        {
            return;
        }

        mainFeedMissingWarnings.Add(objectName);
        Debug.LogWarning($"[CAM] Main feed selection object not found: {objectName}");
    }

    private static string FormatMainCameraFeedLabel(string feedId)
    {
        string normalized = NormalizeMainCameraFeedId(feedId);
        return normalized switch
        {
            "GLOBAL_CCTV" => "GLOBAL CCTV",
            "tb3-01" => "TB3-01",
            "tb3-02" => "TB3-02",
            "tb3-03" => "TB3-03",
            _ => "GLOBAL CCTV"
        };
    }

    private static string NormalizeMainCameraFeedId(string feedId)
    {
        string normalized = string.IsNullOrWhiteSpace(feedId) ? "global" : feedId.Trim().ToLowerInvariant();
        return normalized switch
        {
            "global" => "GLOBAL_CCTV",
            "global cctv" => "GLOBAL_CCTV",
            "global_cctv" => "GLOBAL_CCTV",
            "tb3-1" => "tb3-01",
            "tb3_01" => "tb3-01",
            "tb3-01" => "tb3-01",
            "tb3-2" => "tb3-02",
            "tb3_02" => "tb3-02",
            "tb3-02" => "tb3-02",
            "tb3-3" => "tb3-03",
            "tb3_03" => "tb3-03",
            "tb3-03" => "tb3-03",
            _ => "GLOBAL_CCTV"
        };
    }

    private TMP_Text FindCameraViewTextByName(string objectName)
    {
        Transform root = panelMainCameraView != null ? panelMainCameraView.transform : transform.root;
        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text text in texts)
        {
            if (text != null && text.name == objectName)
            {
                return text;
            }
        }

        return null;
    }

    private void ResolveWebSocketClient()
    {
        if (webSocketClient != null)
        {
            return;
        }

#if UNITY_2023_1_OR_NEWER
        webSocketClient = FindFirstObjectByType<scr_ControlTowerWebSocketClient>();
#else
        webSocketClient = FindObjectOfType<scr_ControlTowerWebSocketClient>();
#endif
    }

    private void ResolveCameraStreamManager()
    {
        if (cameraStreamManager != null)
        {
            return;
        }

#if UNITY_2023_1_OR_NEWER
        cameraStreamManager = FindFirstObjectByType<scr_ControlTowerCameraStreamManager>();
#else
        cameraStreamManager = FindObjectOfType<scr_ControlTowerCameraStreamManager>();
#endif
    }

    private void ResolveRobotApiClient()
    {
        if (robotApiClient != null)
        {
            return;
        }

#if UNITY_2023_1_OR_NEWER
        robotApiClient = FindFirstObjectByType<scr_ControlTowerRobotApiClient>();
#else
        robotApiClient = FindObjectOfType<scr_ControlTowerRobotApiClient>();
#endif
    }

    private void ResolveDashboardRuntimeBinder()
    {
        if (dashboardRuntimeBinder != null)
        {
            return;
        }

#if UNITY_2023_1_OR_NEWER
        dashboardRuntimeBinder = FindFirstObjectByType<scr_ControlTowerDashboardRuntimeBinder>();
#else
        dashboardRuntimeBinder = FindObjectOfType<scr_ControlTowerDashboardRuntimeBinder>();
#endif
    }

    private void ResolveForkliftRuntimeController()
    {
        if (forkliftRuntimeController != null)
        {
            return;
        }

#if UNITY_2023_1_OR_NEWER
        forkliftRuntimeController = FindFirstObjectByType<scr_TB3ForkliftRuntimeController>();
#else
        forkliftRuntimeController = FindObjectOfType<scr_TB3ForkliftRuntimeController>();
#endif
        if (forkliftRuntimeController != null)
        {
            return;
        }

        GameObject forkliftObject = FindSceneGameObjectByName("TB3_Forklift_RackPinion_Final");
        if (forkliftObject != null)
        {
            forkliftRuntimeController = forkliftObject.GetComponent<scr_TB3ForkliftRuntimeController>();
        }
    }

    private void ResolveForkliftPalletCarryController()
    {
        if (forkliftPalletCarryController != null)
        {
            return;
        }

#if UNITY_2023_1_OR_NEWER
        forkliftPalletCarryController =
            FindFirstObjectByType<scr_TB3ForkliftPalletCarryController>(FindObjectsInactive.Include);
#else
        forkliftPalletCarryController = FindObjectOfType<scr_TB3ForkliftPalletCarryController>(true);
#endif
    }

    private void ResolvePersonnelRuntimeReferences()
    {
#if UNITY_2023_1_OR_NEWER
        staffEntranceBarrierController ??= FindFirstObjectByType<scr_StaffEntranceBarrierController>();
        personnel3DMarkerController ??= FindFirstObjectByType<scr_Personnel3DMarkerController>();
#else
        staffEntranceBarrierController ??= FindObjectOfType<scr_StaffEntranceBarrierController>();
        personnel3DMarkerController ??= FindObjectOfType<scr_Personnel3DMarkerController>();
#endif
        textTopAttendanceInCount ??= FindSceneTextByName("Text_TopAttendanceInCount");
        textTopAttendanceOutCount ??= FindSceneTextByName("Text_TopAttendanceOutCount");
        textTopVisitorTodayCount ??= FindSceneTextByName("Text_TopVisitorTodayCount");
        textDashboardAttendanceInValue ??= FindSceneTextByName("Text_DashboardAttendanceInValue");
        textDashboardAttendanceOutValue ??= FindSceneTextByName("Text_DashboardAttendanceOutValue");
        textDashboardVisitorTodayValue ??= FindSceneTextByName("Text_DashboardVisitorTodayValue");
        textDashboardLastAccessEventValue ??= FindSceneTextByName("Text_DashboardLastAccessEventValue");
    }

    private void ResolveTopSummaryCardReferences()
    {
        textTopNoHelmetCount ??= FindSceneTextByName("Card_NoHelmet_Text");
        textTopFallCount ??= FindSceneTextByName("Card_Fall_Text");
        textTopFireCount ??= FindSceneTextByName("Card_Fire_Text");
        textTopLowBatteryCount ??= FindSceneTextByName("Card_LowBattery_Text");
        textTopPatrolCount ??= FindSceneTextByName("Card_PatrolRobots_Text");
        textTopCctvCount ??= FindSceneTextByName("Card_CCTV_Text");
        textTopAttendanceInCount ??= FindSceneTextByName("Text_TopAttendanceInCount");
        textTopAttendanceOutCount ??= FindSceneTextByName("Text_TopAttendanceOutCount");
        textTopVisitorTodayCount ??= FindSceneTextByName("Text_TopVisitorTodayCount");
    }

    private void DisconnectCameraStreams()
    {
        ResolveCameraStreamManager();
        if (cameraStreamManager != null)
        {
            cameraStreamManager.SetCameraViewActive(false);
            cameraStreamManager.DisconnectCameraStreams();
        }
    }

    private void SetCameraViewStreamTargetsActive(bool active)
    {
        ResolveCameraStreamManager();
        if (cameraStreamManager != null)
        {
            cameraStreamManager.SetCameraViewActive(active);
            if (!active)
            {
                cameraStreamManager.SetMainCameraFeedSelection(selectedMainFeedRobotId);
            }
        }
    }

    private void EnsureCameraPreviewStreamsConnected()
    {
        ResolveCameraStreamManager();
        if (cameraStreamManager == null)
        {
            return;
        }

        cameraStreamManager.SetCameraViewActive(IsCameraViewActive());
        cameraStreamManager.SetSelectedRobot(selectedRobotId);
        cameraStreamManager.SetMainCameraFeedSelection(selectedMainFeedRobotId);
        cameraStreamManager.ConnectCameraStreams();
    }

    private void EnsureBottomCameraPreviewVisible()
    {
        GameObject bottomPreview = FindSceneGameObjectByName("Panel_Bottom_CameraPreview");
        if (bottomPreview != null && !bottomPreview.activeSelf)
        {
            bottomPreview.SetActive(true);
        }
    }

    private int GetRobotNumberFromSelectedRobotId()
    {
        if (string.IsNullOrWhiteSpace(selectedRobotId))
        {
            return 1;
        }

        string[] parts = selectedRobotId.Split('-');
        if (parts.Length > 1 && int.TryParse(parts[parts.Length - 1], out int parsed))
        {
            return parsed;
        }

        return 1;
    }

    private string GetIsoTimestamp()
    {
        return DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
    }

    private static bool TryParsePercent(string source, out float percent)
    {
        percent = 0f;
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        string normalized = source.Replace("%", string.Empty).Trim();
        return float.TryParse(normalized, out percent);
    }

    private static bool TryParseProgress(string source, out float value01)
    {
        value01 = 0f;
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        string[] parts = source.Split('/');
        if (parts.Length != 2)
        {
            return false;
        }

        if (!float.TryParse(parts[0].Trim(), out float current) || !float.TryParse(parts[1].Trim(), out float total) || total <= 0f)
        {
            return false;
        }

        value01 = Mathf.Clamp01(current / total);
        return true;
    }

    private static string NormalizeServerValue(string state)
    {
        return IsHealthyState(state) ? "Online" : "Offline";
    }

    private static string NormalizeWebSocketValue(string state)
    {
        return IsHealthyState(state) ? "Connected" : "Disconnected";
    }

    private float EstimateSystemHealthPercent()
    {
        float score = 0f;
        score += IsHealthyState(currentServerStatus) ? 20f : 0f;
        score += IsHealthyState(currentWebSocketStatus) ? 20f : 0f;
        score += IsHealthyState(currentCommStatus) ? 20f : 0f;
        score += IsHealthyState(currentAiModelStatus) ? 20f : 0f;
        score += 0f; // DB is Waiting until a real database health event is wired.
        return Mathf.Clamp(score, 0f, 100f);
    }

    private float EstimateCameraActivityPercent()
    {
        float score = 0f;
        score += IsHealthyState(currentGlobalCamStatus) ? 50f : 0f;
        score += IsHealthyState(currentCameraStatus) ? 50f : 0f;
        return Mathf.Clamp(score, 0f, 100f);
    }

    private bool HasActiveMapGoal()
    {
        string goal = string.IsNullOrWhiteSpace(currentGoal) ? string.Empty : currentGoal.Trim();
        return !string.IsNullOrWhiteSpace(goal) &&
               !goal.Equals("None", StringComparison.OrdinalIgnoreCase) &&
               !goal.Equals("No Data", StringComparison.OrdinalIgnoreCase) &&
               !goal.Equals("Waiting", StringComparison.OrdinalIgnoreCase);
    }

    private bool HasAnyCameraConnectionState()
    {
        return hasCameraStatusFromStream;
    }

    private bool HasAnySystemHealthSignal()
    {
        return hasSystemStatusFromServer;
    }

    private float GetRobotBatteryPercentOrUnknown(string robotId)
    {
        if (robotStatesById.TryGetValue(robotId, out RobotStateData state) && TryParsePercent(state.Battery, out float batteryPercent))
        {
            return batteryPercent;
        }

        return float.NaN;
    }

    private static bool IsHealthyState(string state)
    {
        string normalized = string.IsNullOrWhiteSpace(state) ? string.Empty : state.Trim().ToUpperInvariant();
        return normalized == "READY" || normalized == "ONLINE" || normalized == "CONNECTED" || normalized == "NORMAL" || normalized == "ACCEPTED";
    }

    private static bool IsProblemState(string state)
    {
        string normalized = string.IsNullOrWhiteSpace(state) ? string.Empty : state.Trim().ToUpperInvariant();
        return normalized == "OFFLINE" || normalized == "CLOSED" || normalized == "FAILED" || normalized == "REJECTED" || normalized == "ERROR" || normalized == "EMERGENCY";
    }
}

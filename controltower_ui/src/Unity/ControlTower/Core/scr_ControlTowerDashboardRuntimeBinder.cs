using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class scr_ControlTowerDashboardRuntimeBinder : MonoBehaviour
{
    [Serializable]
    public class DashboardRobotSlotReferences
    {
        public string robotId;
        public TMP_Text textRobotId;
        public TMP_Text textBatteryPercent;
        public GameObject dotSelected;
        public GameObject dotUnselected;
        public GameObject iconBatteryUnknown;
        public GameObject iconBatteryCharging;
        public GameObject iconBatteryFull;
        public GameObject iconBatteryMedium;
        public GameObject iconBatteryLow;
        public GameObject iconBatteryEmpty;
        public RawImage rawImagePreview;
        public Transform previewModelRoot;

        [NonSerialized] public bool HasInitialPreviewRotation;
        [NonSerialized] public Quaternion InitialPreviewLocalRotation;
        [NonSerialized] public Transform InitialPreviewTransform;
    }

    [SerializeField] private DashboardRobotSlotReferences[] dashboardRobotSlots =
    {
        new DashboardRobotSlotReferences { robotId = "tb3-01" },
        new DashboardRobotSlotReferences { robotId = "tb3-02" },
        new DashboardRobotSlotReferences { robotId = "tb3-03" }
    };

    [Header("Dashboard Map Progress References")]
    [SerializeField] private TMP_Text textDashboardMapNavProgressPercent;
    [SerializeField] private Image imageDashboardMapNavProgressFill;

    [Header("Dashboard Camera Dot References")]
    [SerializeField] private GameObject dotDashboardGlobalCctv;
    [SerializeField] private GameObject dotDashboardTb3Camera01;
    [SerializeField] private GameObject dotDashboardTb3Camera02;
    [SerializeField] private GameObject dotDashboardAiModel;

    [Header("Dashboard People Status Pill References")]
    [SerializeField] private GameObject pillDashboardAttendanceInStatus;
    [SerializeField] private GameObject pillDashboardAttendanceOutStatus;
    [SerializeField] private GameObject pillDashboardVisitorTodayStatus;

    [Header("Dashboard Map Status Pill References")]
    [SerializeField] private GameObject pillDashboardSlam;
    [SerializeField] private GameObject pillDashboardNav2;

    [Header("Dashboard System Status Pill References")]
    [SerializeField] private GameObject pillDashboardServerStatus;
    [SerializeField] private GameObject pillDashboardWebSocketStatus;
    [SerializeField] private GameObject pillDashboardRos2Status;
    [SerializeField] private GameObject pillDashboardAiStatus;
    [SerializeField] private GameObject pillDashboardDb;

    private readonly List<Image> barImages = new();
    private readonly List<Image> dotImages = new();
    private readonly List<Image> timelineDotImages = new();
    private readonly List<Image> pillImages = new();
    private readonly List<TMP_Text> dashboardTexts = new();
    private readonly Dictionary<string, Image> peopleStatusImagesByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Transform> peopleStatusTransformsByName = new(StringComparer.Ordinal);
    private readonly List<Image> visitorSlotImagesCache = new();
    private readonly HashSet<string> missingKeywordWarnings = new();
    private bool cachedBarNamesLogged;
    private bool visitorSlotNamesLogged;
    private bool dashboardRobotSlotsResolved;
    private Transform dashboardSystemHealthRoot;
    private Transform dashboardPeopleStatusRoot;
    private Transform cachedPeopleStatusRoot;

    private static readonly Color ReadyColor = new Color32(0x20, 0xD6, 0x6B, 0xFF);
    private static readonly Color WaitingColor = new Color32(0xF2, 0xB0, 0x1E, 0xFF);
    private static readonly Color FailedColor = new Color32(0xF0, 0x44, 0x52, 0xFF);
    private static readonly Color EmergencyColor = new Color32(0xF0, 0x44, 0x52, 0xFF);
    private static readonly Color MutedTextColor = new Color32(0xAE, 0xB8, 0xC2, 0xFF);
    private static readonly Color ChargingColor = new Color32(0x22, 0xC7, 0xF2, 0xFF);
    private static readonly Color BatteryMediumColor = new Color32(0xB8, 0xD6, 0x29, 0xFF);
    private static readonly Color EmptySlotColor = new Color(0.30f, 0.35f, 0.40f, 0.45f);

    private void Awake()
    {
        CacheDashboardObjects();
        ResolveDashboardRobotSlots();
        LogCacheSummary();
        WarnAboutInvisibleOrEmptyTexts();
    }

    private void Start()
    {
        if (barImages.Count == 0 && dotImages.Count == 0 && pillImages.Count == 0 && dashboardTexts.Count == 0)
        {
            CacheDashboardObjects();
            ResolveDashboardRobotSlots();
            LogCacheSummary();
            WarnAboutInvisibleOrEmptyTexts();
        }
    }

    public void RefreshBindings()
    {
        CacheDashboardObjects();
        ResolveDashboardRobotSlots();
        LogCacheSummary();
        WarnAboutInvisibleOrEmptyTexts();
    }

    public void SetBar01(string targetNameKeyword, float value01)
    {
        TrySetBar01(targetNameKeyword, value01, true);
    }

    public void SetPatrolCoverage(float percent)
    {
        SetFactoryPatrolCoverage(percent);
    }

    public void SetFactoryPatrolCoverage(float percent)
    {
        float value01 = PercentTo01(percent);
        if (!TrySetExactFillBar01("Bar_DashboardFactoryPatrolCoverage_Fill", value01, true))
        {
            TrySetBar01("Factory Patrol Coverage", value01, true);
        }

        SetPercentText("Text_DashboardFactoryPatrolCoveragePercent", "Patrol Coverage", value01);
        SetStateVisuals("FactoryPatrolCoverage", PercentToState(percent));
        SetStateVisuals("PatrolCoverage", PercentToState(percent));
    }

    public void SetFactoryPatrolCoverageUnavailable()
    {
        TrySetExactFillBar01("Bar_DashboardFactoryPatrolCoverage_Fill", 0f, false);
        SetExactTextValue("Text_DashboardFactoryPatrolCoveragePercent", "--");
    }

    public void SetMapNavProgress(float percent)
    {
        float value01 = PercentTo01(percent);
        if (!TrySetDashboardMapNavProgressFill(value01, true))
        {
            TrySetBar01("Map Nav Progress", value01, true);
        }

        SetDashboardMapNavProgressText($"Progress {Mathf.RoundToInt(value01 * 100f)}%");
        SetStateVisuals("MapNavProgress", PercentToState(percent));
        SetStateVisuals("NavProgress", PercentToState(percent));
    }

    public void SetMapNavProgressUnavailable()
    {
        TrySetDashboardMapNavProgressFill(0f, false);
        SetDashboardMapNavProgressText("--");
    }

    public void SetMapWaypointCompletionProgress(int completedCount, int totalCount, bool hasRoute)
    {
        int safeTotal = Mathf.Max(1, totalCount);
        int safeCompleted = Mathf.Clamp(completedCount, 0, safeTotal);
        float value01 = hasRoute ? safeCompleted / (float)safeTotal : 0f;

        if (!TrySetDashboardMapNavProgressFill(value01, false))
        {
            TrySetExactFillBar01("Bar_DashboardMapProgress_Fill", value01, false);
        }

        SetDashboardMapNavProgressText(
            hasRoute
                ? $"웨이포인트 완료 : {safeCompleted}/{safeTotal}"
                : $"웨이포인트 완료 : --/{safeTotal}");
    }

    public void SetCameraActivity(float percent)
    {
        float value01 = PercentTo01(percent);
        if (!TrySetExactFillBar01("Bar_DashboardCameraActivity_Fill", value01, true))
        {
            TrySetBar01("Camera Activity", value01, true);
        }

        SetPercentText("Text_DashboardCameraActivityPercent", "Activity", value01);
        SetStateVisuals("CameraActivity", PercentToState(percent));
    }

    public void SetCameraActivityUnavailable()
    {
        TrySetExactFillBar01("Bar_DashboardCameraActivity_Fill", 0f, false);
        SetExactTextValue("Text_DashboardCameraActivityPercent", "연결 카메라 : --");
    }

    public void SetCameraConnectionRatio(int connectedCount, int totalCount)
    {
        int safeTotal = Mathf.Max(1, totalCount);
        int safeConnected = Mathf.Clamp(connectedCount, 0, safeTotal);
        float value01 = safeConnected / (float)safeTotal;
        if (!TrySetExactFillBar01("Bar_DashboardCameraActivity_Fill", value01, true))
        {
            TrySetBar01("Camera Activity", value01, true);
        }

        SetExactTextValue("Text_DashboardCameraActivityPercent", $"연결 카메라 : {safeConnected}/{safeTotal}");
    }

    public void SetSystemHealth(float percent)
    {
        float value01 = PercentTo01(percent);
        if (!TrySetExactFillBar01("Bar_DashboardSystemHealth_Fill", value01, true))
        {
            TrySetBar01("Health Fill", value01, true);
        }

        SetStateVisuals("System", PercentToState(percent));
        SetStateVisuals("Health", PercentToState(percent));
    }

    public void SetSystemHealthUnavailable()
    {
        TrySetExactFillBar01("Bar_DashboardSystemHealth_Fill", 0f, false);
        SetExactTextValue("Text_SystemHealth_HealthPercent", "전체 상태 : --");
    }

    public void SetPeopleStatus(int attendanceIn, int attendanceOut, int visitorsToday, string lastAccessEvent)
    {
        CacheDashboardRoots();
        SetPeopleStatusDecorationsInactive();

        bool hasAttendanceIn = attendanceIn >= 0;
        bool hasAttendanceOut = attendanceOut >= 0;
        bool hasVisitorsToday = visitorsToday >= 0;
        int attendanceInSlots = hasAttendanceIn ? Mathf.Clamp(attendanceIn, 0, 5) : 0;
        int attendanceOutSlots = hasAttendanceOut ? Mathf.Clamp(attendanceOut, 0, 5) : 0;
        int visitorSlots = hasVisitorsToday ? Mathf.Clamp(visitorsToday, 0, 3) : 0;

        SetPeopleSlotGroupActive("Slot_AttendanceIn_", 5, attendanceInSlots);
        SetPeopleSlotGroupActive("Slot_AttendanceOut_", 5, attendanceOutSlots);
        SetVisitorSlotsActive(visitorSlots);
    }

    public void SetPeopleSummaryStatusColors(string todaySummaryRequestState, bool hasAttendanceIn, bool hasAttendanceOut, bool hasVisitorsToday)
    {
        DashboardStatusLevel requestLevel = ResolveRequestLevel(todaySummaryRequestState);
        SetStatusColor(
            pillDashboardAttendanceInStatus,
            new[] { "Pill_DashboardAttendanceInStatus" },
            requestLevel == DashboardStatusLevel.Normal
                ? (hasAttendanceIn ? DashboardStatusLevel.Normal : DashboardStatusLevel.Unknown)
                : requestLevel);
        SetStatusColor(
            pillDashboardAttendanceOutStatus,
            new[] { "Pill_DashboardAttendanceOutStatus" },
            requestLevel == DashboardStatusLevel.Normal
                ? (hasAttendanceOut ? DashboardStatusLevel.Normal : DashboardStatusLevel.Unknown)
                : requestLevel);
        SetStatusColor(
            pillDashboardVisitorTodayStatus,
            new[] { "Pill_DashboardVisitorTodayStatus" },
            requestLevel == DashboardStatusLevel.Normal
                ? (hasVisitorsToday ? DashboardStatusLevel.Normal : DashboardStatusLevel.Unknown)
                : requestLevel);
    }

    public void SetRobotBattery(string robotId, float percent)
    {
        SetRobotBattery(robotId, percent, string.Empty);
    }

    public void SetRobotBattery(string robotId, float percent, string robotStatus)
    {
        SetRobotBatteryGaugeFill(robotId, percent);
        SetRobotBatteryPercentText(robotId, percent);
        SetRobotSlotBatteryState(robotId, percent, robotStatus);
    }

    public void SetServerState(string state)
    {
        SetStateVisuals("Server", state);
    }

    public void SetWebSocketState(string state)
    {
        SetStateVisuals("WebSocket", state);
    }

    public void SetRos2State(string state)
    {
        SetStateVisuals("ROS2", state);
        SetStateVisuals("Ros2", state);
    }

    public void SetAiModelState(string state)
    {
        SetStateVisuals("AI", state);
        SetStateVisuals("AiModel", state);
        SetStateVisuals("Model", state);
    }

    public void SetDbState(string state)
    {
        SetStateVisuals("DB", state);
        SetStateVisuals("Database", state);
    }

    public void SetCameraState(string globalCctvState, string tb3CameraState, string aiModelState)
    {
        SetDashboardCameraDot(dotDashboardGlobalCctv, "Dot_DashboardGlobalCCTV", globalCctvState, SystemStatusKind.WebSocket);
        SetDashboardCameraDot(dotDashboardTb3Camera01, "Dot_DashboardTB3Camera_01", tb3CameraState, SystemStatusKind.WebSocket);
        SetDashboardCameraDot(dotDashboardTb3Camera02, "Dot_DashboardTB3Camera_02", tb3CameraState, SystemStatusKind.WebSocket);
        SetDashboardCameraDot(dotDashboardAiModel, "Dot_DashboardAIModel", aiModelState, SystemStatusKind.AiModel);
    }

    public void SetCameraSourceDotStates(string globalCctvState, string tb3Camera01State, string tb3Camera02State, string aiModelState)
    {
        SetDashboardCameraDot(dotDashboardGlobalCctv, "Dot_DashboardGlobalCCTV", globalCctvState, SystemStatusKind.WebSocket);
        SetDashboardCameraDot(dotDashboardTb3Camera01, "Dot_DashboardTB3Camera_01", tb3Camera01State, SystemStatusKind.WebSocket);
        SetDashboardCameraDot(dotDashboardTb3Camera02, "Dot_DashboardTB3Camera_02", tb3Camera02State, SystemStatusKind.WebSocket);
        SetDashboardCameraDot(dotDashboardAiModel, "Dot_DashboardAIModel", aiModelState, SystemStatusKind.AiModel);
    }

    public void SetMapNavigationStatusColors(string localizationState, string nav2State, bool hasMapNavStatus)
    {
        SetStatusColor(
            pillDashboardSlam,
            new[] { "Pill_DashboardSLAM", "Pill_DashboardSlam", "Dot_DashboardSLAM", "Dot_DashboardSlam" },
            hasMapNavStatus ? ResolveSlamStatusLevel(localizationState) : DashboardStatusLevel.Unknown);
        SetStatusColor(
            pillDashboardNav2,
            new[] { "Pill_DashboardNav2", "Pill_DashboardNAV2", "Dot_DashboardNav2", "Dot_DashboardNAV2" },
            hasMapNavStatus ? ResolveNav2StatusLevel(nav2State) : DashboardStatusLevel.Unknown);
    }

    public void SetSystemStatusColors(string serverState, string webSocketState, string ros2State, string aiModelState, string dbState)
    {
        SetStatusColor(
            pillDashboardServerStatus,
            new[] { "Pill_DashboardServerStatus", "Pill_DashboardServer", "Dot_DashboardServerStatus", "Dot_DashboardServer" },
            ResolveSystemStatusLevel(serverState, SystemStatusKind.Server));
        SetStatusColor(
            pillDashboardWebSocketStatus,
            new[] { "Pill_DashboardWebSocketStatus", "Pill_DashboardWebSocket", "Dot_DashboardWebSocketStatus", "Dot_DashboardWebSocket" },
            ResolveSystemStatusLevel(webSocketState, SystemStatusKind.WebSocket));
        SetStatusColor(
            pillDashboardRos2Status,
            new[] { "Pill_DashboardROS2Status", "Pill_DashboardRos2Status", "Pill_DashboardROS2", "Dot_DashboardROS2Status", "Dot_DashboardROS2" },
            ResolveSystemStatusLevel(ros2State, SystemStatusKind.Ros2));
        SetStatusColor(
            pillDashboardAiStatus,
            new[] { "Pill_DashboardAIStatus", "Pill_DashboardAiStatus", "Pill_DashboardAIModel", "Pill_DashboardAiModel", "Dot_DashboardAIStatus", "Dot_DashboardAIModel" },
            ResolveSystemStatusLevel(aiModelState, SystemStatusKind.AiModel));
        SetStatusColor(
            pillDashboardDb,
            new[] { "Pill_DashboardDB", "Pill_DashboardDb", "Pill_DashboardDatabase", "Dot_DashboardDB", "Dot_DashboardDatabase" },
            ResolveSystemStatusLevel(dbState, SystemStatusKind.Database));
    }

    public void SetSelectedRobot(string robotId)
    {
        ResolveDashboardRobotSlots();
        string selectedRobotKey = NormalizeRobotId(robotId);
        for (int i = 1; i <= 3; i++)
        {
            DashboardRobotSlotReferences slot = GetRobotSlotByNumber(i);
            string slotRobotKey = NormalizeRobotId(slot?.robotId);
            bool selected = !string.IsNullOrWhiteSpace(slotRobotKey) && slotRobotKey == selectedRobotKey;
            if (slot != null && (slot.dotSelected != null || slot.dotUnselected != null))
            {
                ApplyGraphicColor(slot.dotSelected, ReadyColor);
                ApplyGraphicColor(slot.dotUnselected, MutedTextColor);
                SetGameObjectActive(slot.dotSelected, selected);
                SetGameObjectActive(slot.dotUnselected, !selected);
            }
            else
            {
                SetExactImageColor($"Dot_DashboardRobot{i:00}", selected ? ReadyColor : MutedTextColor, $"RobotDotColor:{i:00}");
                SetExactImageActive($"Dot_DashboardRobot{i:00}", selected, $"RobotDot:{i:00}");
            }
        }
    }

    public void SetTimelineMarkerState(int markerIndex, string eventCategory)
    {
        if (markerIndex < 1 || markerIndex > 3)
        {
            return;
        }

        SetTimelineMarkerActive(markerIndex, true);
    }

    public void SetTimelineMarkerActive(int markerIndex, bool active)
    {
        if (markerIndex < 1 || markerIndex > 3)
        {
            return;
        }

        SetExactImageActive($"TimelineDot_{markerIndex:00}", active, $"TimelineDot:{markerIndex:00}");
    }

    public void TickDashboardRobotPreview(string selectedRobotId, bool dashboardActive, float rotationSpeedDegreesPerSecond)
    {
        if (!dashboardActive)
        {
            return;
        }

        for (int i = 0; i < dashboardRobotSlots.Length; i++)
        {
            DashboardRobotSlotReferences slot = dashboardRobotSlots[i];
            if (slot == null || slot.previewModelRoot == null)
            {
                continue;
            }

            CacheInitialPreviewRotation(slot);
            slot.previewModelRoot.Rotate(
                0f,
                rotationSpeedDegreesPerSecond * Time.unscaledDeltaTime,
                0f,
                Space.Self);
        }
    }

    private void ResetPreviewRotation(DashboardRobotSlotReferences slot)
    {
        if (slot?.previewModelRoot == null || !slot.HasInitialPreviewRotation)
        {
            return;
        }

        slot.previewModelRoot.localRotation = slot.InitialPreviewLocalRotation;
    }

    public void AddTimelineEvent(string timeText, string eventText, string level)
    {
        if (string.IsNullOrWhiteSpace(eventText))
        {
            return;
        }

        TMP_Text timelineText = FindTextByKeyword("Timeline") ?? FindTextByKeyword("Recent");
        if (timelineText == null)
        {
            WarnMissingKeywordOnce(false, "Text:Timeline");
            return;
        }

        string safeTime = string.IsNullOrWhiteSpace(timeText) ? DateTime.Now.ToString("HH:mm") : timeText.Trim();
        string line = $"[{safeTime}] {eventText.Trim()}";
        List<string> lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(timelineText.text))
        {
            lines.AddRange(timelineText.text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        lines.Add(line);
        while (lines.Count > 5)
        {
            lines.RemoveAt(0);
        }

        SetTextIfChanged(timelineText, string.Join("\n", lines));
    }

    private void CacheDashboardObjects()
    {
        barImages.Clear();
        dotImages.Clear();
        timelineDotImages.Clear();
        pillImages.Clear();
        dashboardTexts.Clear();
        dashboardRobotSlotsResolved = false;
        cachedPeopleStatusRoot = null;
        peopleStatusImagesByName.Clear();
        peopleStatusTransformsByName.Clear();
        visitorSlotImagesCache.Clear();
        CacheDashboardRoots();

        GameObject[] sceneObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (GameObject sceneObject in sceneObjects)
        {
            if (sceneObject == null || !sceneObject.scene.IsValid())
            {
                continue;
            }

            string objectName = sceneObject.name;
            if (string.IsNullOrWhiteSpace(objectName) || objectName.StartsWith("IconSlot_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (objectName.StartsWith("Bar_", StringComparison.OrdinalIgnoreCase))
            {
                Image image = sceneObject.GetComponent<Image>();
                if (image != null)
                {
                    barImages.Add(image);
                }
            }
            else if (objectName.StartsWith("Dot_", StringComparison.OrdinalIgnoreCase))
            {
                Image image = sceneObject.GetComponent<Image>();
                if (image != null)
                {
                    dotImages.Add(image);
                }
            }
            else if (objectName.StartsWith("TimelineDot_", StringComparison.OrdinalIgnoreCase))
            {
                Image image = sceneObject.GetComponent<Image>();
                if (image != null)
                {
                    timelineDotImages.Add(image);
                }
            }
            else if (objectName.StartsWith("Pill_", StringComparison.OrdinalIgnoreCase))
            {
                Image image = sceneObject.GetComponent<Image>();
                if (image != null)
                {
                    pillImages.Add(image);
                }
            }
            else if (objectName.StartsWith("Text_", StringComparison.OrdinalIgnoreCase))
            {
                TMP_Text text = sceneObject.GetComponent<TMP_Text>();
                if (text != null && IsDashboardLikeText(text))
                {
                    dashboardTexts.Add(text);
                }
            }
        }
    }

    private static bool IsDashboardLikeText(TMP_Text text)
    {
        Transform current = text.transform;
        while (current != null)
        {
            if (current.name.IndexOf("Dashboard", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            current = current.parent;
        }

        return text.name.IndexOf("Dashboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.name.IndexOf("Timeline", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void CacheDashboardRoots()
    {
        dashboardSystemHealthRoot ??= FindSceneTransform("Panel_DashboardSystemHealth");
        dashboardPeopleStatusRoot ??= FindSceneTransform("Panel_DashboardPeopleStatus");
        CachePeopleStatusObjects();
    }

    private void CachePeopleStatusObjects()
    {
        if (dashboardPeopleStatusRoot == null || cachedPeopleStatusRoot == dashboardPeopleStatusRoot)
        {
            return;
        }

        cachedPeopleStatusRoot = dashboardPeopleStatusRoot;
        peopleStatusImagesByName.Clear();
        peopleStatusTransformsByName.Clear();
        visitorSlotImagesCache.Clear();

        Transform[] children = dashboardPeopleStatusRoot.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child == null)
            {
                continue;
            }

            if (!peopleStatusTransformsByName.ContainsKey(child.name))
            {
                peopleStatusTransformsByName.Add(child.name, child);
            }

            Image image = child.GetComponent<Image>();
            if (image != null && !peopleStatusImagesByName.ContainsKey(image.name))
            {
                peopleStatusImagesByName.Add(image.name, image);
            }
        }

        for (int i = 1; i <= 3; i++)
        {
            if (peopleStatusImagesByName.TryGetValue($"Slot_VisitorToday_{i:00}", out Image image))
            {
                visitorSlotImagesCache.Add(image);
            }
        }

        if (visitorSlotImagesCache.Count == 0 &&
            peopleStatusImagesByName.TryGetValue("Slot_VisitorToday", out Image fallback))
        {
            visitorSlotImagesCache.Add(fallback);
        }
    }

    private void ResolveDashboardRobotSlots(bool force = false)
    {
        if (dashboardRobotSlotsResolved && !force)
        {
            return;
        }

        EnsureDashboardRobotSlotArray();
        for (int i = 0; i < dashboardRobotSlots.Length; i++)
        {
            DashboardRobotSlotReferences slot = dashboardRobotSlots[i];
            if (slot == null)
            {
                continue;
            }

            int robotNumber = i + 1;
            string robotId = $"tb3-{robotNumber:00}";
            slot.robotId = robotId;

            Transform slotRoot = FindSceneTransform($"DashboardRobotSlot_TB3_{robotNumber:00}");
            Transform batteryRoot = FindDescendantUnderRoot("BatteryState", slotRoot);
            Transform dotRoot = FindDescendantUnderRoot("DotState", slotRoot);

            slot.textRobotId = ResolveSlotText(slot.textRobotId, "Text_RobotId", slotRoot);
            slot.textBatteryPercent = ResolveSlotText(slot.textBatteryPercent, "Text_BatteryPercent", slotRoot);
            slot.dotSelected = ResolveSlotGameObject(slot.dotSelected, "Dot_Selected", dotRoot != null ? dotRoot : slotRoot);
            slot.dotUnselected = ResolveSlotGameObject(slot.dotUnselected, "Dot_Unselected", dotRoot != null ? dotRoot : slotRoot);
            slot.iconBatteryUnknown = ResolveSlotGameObject(slot.iconBatteryUnknown, "Icon_BatteryUnknown", batteryRoot != null ? batteryRoot : slotRoot);
            slot.iconBatteryCharging = ResolveSlotGameObject(slot.iconBatteryCharging, "Icon_BatteryCharging", batteryRoot != null ? batteryRoot : slotRoot);
            slot.iconBatteryFull = ResolveSlotGameObject(slot.iconBatteryFull, "Icon_BatteryFull", batteryRoot != null ? batteryRoot : slotRoot);
            slot.iconBatteryMedium = ResolveSlotGameObject(slot.iconBatteryMedium, "Icon_BatteryMedium", batteryRoot != null ? batteryRoot : slotRoot);
            slot.iconBatteryLow = ResolveSlotGameObject(slot.iconBatteryLow, "Icon_BatteryLow", batteryRoot != null ? batteryRoot : slotRoot);
            slot.iconBatteryEmpty = ResolveSlotGameObject(slot.iconBatteryEmpty, "Icon_BatteryEmpty", batteryRoot != null ? batteryRoot : slotRoot);
            string rawImageName = $"RawImage_DashboardRobot{robotNumber:00}";
            if (slot.rawImagePreview == null || !string.Equals(slot.rawImagePreview.name, rawImageName, StringComparison.Ordinal))
            {
                slot.rawImagePreview = FindSceneRawImage(rawImageName);
            }

            string previewRootName = $"DashboardPreview_TB3_{robotNumber:00}";
            if (slot.previewModelRoot == null || !string.Equals(slot.previewModelRoot.name, previewRootName, StringComparison.Ordinal))
            {
                slot.previewModelRoot = FindFirstSceneTransform(
                $"DashboardPreview_TB3_{robotNumber:00}",
                $"DashboardPreview_TB3_0{robotNumber}",
                $"Preview_Dashboard_TB3_{robotNumber:00}",
                $"Preview_DashboardRobot{robotNumber:00}");
            }

            slot.previewModelRoot = ResolveDashboardPreviewModelRoot(slot.previewModelRoot, robotNumber);

            if (slot.textRobotId != null)
            {
                SetTextIfChanged(slot.textRobotId, robotId);
            }

            CacheInitialPreviewRotation(slot);
        }

        dashboardRobotSlotsResolved = true;
    }

    private static TMP_Text ResolveSlotText(TMP_Text current, string objectName, Transform slotRoot)
    {
        if (current != null && IsSameOrChildOf(current.transform, slotRoot) && string.Equals(current.name, objectName, StringComparison.Ordinal))
        {
            return current;
        }

        return FindTextUnderRoot(objectName, slotRoot) ?? current;
    }

    private static GameObject ResolveSlotGameObject(GameObject current, string objectName, Transform slotRoot)
    {
        if (current != null && IsSameOrChildOf(current.transform, slotRoot) && string.Equals(current.name, objectName, StringComparison.Ordinal))
        {
            return current;
        }

        return FindGameObjectUnderRoot(objectName, slotRoot) ?? current;
    }

    private static bool IsSameOrChildOf(Transform target, Transform root)
    {
        if (target == null || root == null)
        {
            return false;
        }

        Transform current = target;
        while (current != null)
        {
            if (current == root)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static Transform ResolveDashboardPreviewModelRoot(Transform candidate, int robotNumber)
    {
        if (candidate == null)
        {
            return null;
        }

        Transform modelRoot = FindDescendantUnderRootStatic("ModelRoot", candidate);
        if (IsValidPreviewModelTransform(modelRoot))
        {
            return modelRoot;
        }

        modelRoot = FindDescendantUnderRootStatic("PreviewModelRoot", candidate);
        if (IsValidPreviewModelTransform(modelRoot))
        {
            return modelRoot;
        }

        string robotModelName = $"Preview_TB3_{robotNumber:00}_Model";
        modelRoot = FindDescendantUnderRootStatic(robotModelName, candidate);
        if (IsValidPreviewModelTransform(modelRoot))
        {
            return modelRoot;
        }

        foreach (Transform child in candidate.GetComponentsInChildren<Transform>(true))
        {
            if (child == candidate || HasCameraInHierarchy(child))
            {
                continue;
            }

            if (HasRendererInHierarchy(child))
            {
                return child;
            }
        }

        return HasCameraInHierarchy(candidate) ? null : candidate;
    }

    private static bool IsValidPreviewModelTransform(Transform transform)
    {
        return transform != null && !HasCameraInHierarchy(transform);
    }

    private static bool HasCameraInHierarchy(Transform transform)
    {
        return transform != null && transform.GetComponentInChildren<Camera>(true) != null;
    }

    private static bool HasRendererInHierarchy(Transform transform)
    {
        return transform != null &&
               (transform.GetComponentInChildren<MeshRenderer>(true) != null ||
                transform.GetComponentInChildren<SkinnedMeshRenderer>(true) != null);
    }

    private void EnsureDashboardRobotSlotArray()
    {
        if (dashboardRobotSlots != null && dashboardRobotSlots.Length >= 3)
        {
            for (int i = 0; i < dashboardRobotSlots.Length; i++)
            {
                dashboardRobotSlots[i] ??= new DashboardRobotSlotReferences { robotId = $"tb3-{i + 1:00}" };
            }

            return;
        }

        dashboardRobotSlots = new[]
        {
            new DashboardRobotSlotReferences { robotId = "tb3-01" },
            new DashboardRobotSlotReferences { robotId = "tb3-02" },
            new DashboardRobotSlotReferences { robotId = "tb3-03" }
        };
    }

    private void CacheInitialPreviewRotation(DashboardRobotSlotReferences slot)
    {
        if (slot == null || slot.previewModelRoot == null)
        {
            return;
        }

        if (slot.HasInitialPreviewRotation && slot.InitialPreviewTransform == slot.previewModelRoot)
        {
            return;
        }

        slot.InitialPreviewLocalRotation = slot.previewModelRoot.localRotation;
        slot.InitialPreviewTransform = slot.previewModelRoot;
        slot.HasInitialPreviewRotation = true;
    }

    private void LogCacheSummary()
    {
        Debug.Log($"[DashboardRuntimeBinder] Text={dashboardTexts.Count}, Bar={barImages.Count}, Dot={dotImages.Count}, Pill={pillImages.Count}");
        LogCachedBarNamesOnce();
    }

    private void LogCachedBarNamesOnce()
    {
        if (cachedBarNamesLogged)
        {
            return;
        }

        cachedBarNamesLogged = true;
        if (barImages.Count == 0)
        {
            Debug.Log("[DashboardRuntimeBinder] Cached Bars: none");
            return;
        }

        List<string> barNames = new List<string>();
        foreach (Image bar in barImages)
        {
            if (bar != null)
            {
                barNames.Add($"- {bar.name} type={bar.type}");
            }
        }

        Debug.Log("[DashboardRuntimeBinder] Cached Bars:\n" + string.Join("\n", barNames));
    }

    private void WarnAboutInvisibleOrEmptyTexts()
    {
        foreach (TMP_Text text in dashboardTexts)
        {
            if (text == null)
            {
                continue;
            }

            if (!text.gameObject.activeInHierarchy)
            {
                Debug.LogWarning($"[DashboardRuntimeBinder] Text is inactive: {text.name}");
            }

            if (string.IsNullOrWhiteSpace(text.text))
            {
                Debug.LogWarning($"[DashboardRuntimeBinder] Text is empty: {text.name}");
            }
        }
    }

    private bool TrySetDashboardMapNavProgressFill(float value01, bool warnIfMissing)
    {
        if (imageDashboardMapNavProgressFill != null)
        {
            return TryApplyFillAmount(imageDashboardMapNavProgressFill, value01);
        }

        return TrySetExactFillBar01("Bar_DashboardMapNavProgress_Fill", value01, warnIfMissing);
    }

    private void SetDashboardMapNavProgressText(string value)
    {
        if (textDashboardMapNavProgressPercent != null)
        {
            SetTextIfChanged(textDashboardMapNavProgressPercent, value);
            return;
        }

        SetExactTextValue("Text_DashboardMapNavProgressPercent", value);
    }

    private void SetDashboardCameraDot(GameObject explicitDot, string fallbackName, string state, SystemStatusKind statusKind)
    {
        Color color = ResolveStatusColor(ResolveSystemStatusLevel(state, statusKind));
        if (explicitDot != null)
        {
            ApplyGraphicColor(explicitDot, color);
            SetGameObjectActive(explicitDot, true);
            return;
        }

        SetExactImageColor(fallbackName, color, $"CameraDotColor:{fallbackName}");
        SetExactImageActive(fallbackName, true, $"CameraDot:{fallbackName}");
    }

    private enum DashboardStatusLevel
    {
        Unknown,
        Normal,
        Waiting,
        Error
    }

    private enum SystemStatusKind
    {
        Server,
        WebSocket,
        Ros2,
        AiModel,
        Database
    }

    private static DashboardStatusLevel ResolveRequestLevel(string requestState)
    {
        string normalized = NormalizeStatusToken(requestState);
        return normalized switch
        {
            "SUCCESS" or "OK" or "READY" or "LOADED" => DashboardStatusLevel.Normal,
            "LOADING" or "REQUESTING" or "PENDING" or "WAITING" => DashboardStatusLevel.Waiting,
            "FAILED" or "ERROR" or "TIMEOUT" => DashboardStatusLevel.Error,
            _ => DashboardStatusLevel.Unknown
        };
    }

    private static DashboardStatusLevel ResolveSlamStatusLevel(string state)
    {
        string normalized = NormalizeStatusToken(state);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return DashboardStatusLevel.Unknown;
        }

        if (IsOneOf(normalized, "READY", "ACTIVE", "LOCALIZED", "OK", "GOOD", "TRACKING", "MATCHED"))
        {
            return DashboardStatusLevel.Normal;
        }

        if (IsOneOf(normalized, "WAITING", "INITIALIZING", "STARTING", "IDLE", "UNKNOWN", "RELOCALIZING"))
        {
            return DashboardStatusLevel.Waiting;
        }

        if (IsOneOf(normalized, "ERROR", "FAILED", "LOST", "UNLOCALIZED", "TIMEOUT"))
        {
            return DashboardStatusLevel.Error;
        }

        return DashboardStatusLevel.Unknown;
    }

    private static DashboardStatusLevel ResolveNav2StatusLevel(string state)
    {
        string normalized = NormalizeStatusToken(state);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return DashboardStatusLevel.Unknown;
        }

        if (IsOneOf(normalized, "READY", "ACTIVE", "RUNNING", "NAVIGATING", "SUCCEEDED"))
        {
            return DashboardStatusLevel.Normal;
        }

        if (IsOneOf(normalized, "INACTIVE", "WAITING", "INITIALIZING", "IDLE", "PAUSED", "PLANNING", "CANCELED", "CANCELLED"))
        {
            return DashboardStatusLevel.Waiting;
        }

        if (IsOneOf(normalized, "ERROR", "FAILED", "ABORTED", "TIMEOUT", "EMERGENCY_STOP"))
        {
            return DashboardStatusLevel.Error;
        }

        return DashboardStatusLevel.Unknown;
    }

    private static DashboardStatusLevel ResolveSystemStatusLevel(string state, SystemStatusKind kind)
    {
        string normalized = NormalizeStatusToken(state);
        if (string.IsNullOrWhiteSpace(normalized) || normalized == "--" || normalized == "-")
        {
            return DashboardStatusLevel.Unknown;
        }

        switch (kind)
        {
            case SystemStatusKind.Server:
                if (IsOneOf(normalized, "ONLINE", "CONNECTED", "READY", "OK")) return DashboardStatusLevel.Normal;
                if (IsOneOf(normalized, "CONNECTING", "WAITING", "STARTING")) return DashboardStatusLevel.Waiting;
                if (IsOneOf(normalized, "OFFLINE", "DISCONNECTED", "ERROR", "FAILED", "TIMEOUT")) return DashboardStatusLevel.Error;
                break;
            case SystemStatusKind.WebSocket:
                if (IsOneOf(normalized, "CONNECTED", "OPEN", "ONLINE")) return DashboardStatusLevel.Normal;
                if (IsOneOf(normalized, "CONNECTING", "RECONNECTING", "WAITING")) return DashboardStatusLevel.Waiting;
                if (IsOneOf(normalized, "CLOSED", "DISCONNECTED", "ERROR", "FAILED", "TIMEOUT")) return DashboardStatusLevel.Error;
                break;
            case SystemStatusKind.Ros2:
                if (IsOneOf(normalized, "CONNECTED", "READY", "ACTIVE", "OK", "RECEIVING")) return DashboardStatusLevel.Normal;
                if (IsOneOf(normalized, "WAITING", "CONNECTING", "INITIALIZING", "IDLE")) return DashboardStatusLevel.Waiting;
                if (IsOneOf(normalized, "DISCONNECTED", "ERROR", "FAILED", "TIMEOUT")) return DashboardStatusLevel.Error;
                break;
            case SystemStatusKind.AiModel:
                if (IsOneOf(normalized, "READY", "ACTIVE", "RUNNING", "OK")) return DashboardStatusLevel.Normal;
                if (IsOneOf(normalized, "LOADING", "INITIALIZING", "WAITING", "STARTING")) return DashboardStatusLevel.Waiting;
                if (IsOneOf(normalized, "ERROR", "FAILED", "OFFLINE", "DISCONNECTED")) return DashboardStatusLevel.Error;
                break;
            case SystemStatusKind.Database:
                if (IsOneOf(normalized, "CONNECTED", "ONLINE", "READY", "OK", "HEALTHY")) return DashboardStatusLevel.Normal;
                if (IsOneOf(normalized, "CONNECTING", "WAITING", "INITIALIZING")) return DashboardStatusLevel.Waiting;
                if (IsOneOf(normalized, "DISCONNECTED", "OFFLINE", "ERROR", "FAILED", "UNHEALTHY")) return DashboardStatusLevel.Error;
                break;
        }

        return DashboardStatusLevel.Unknown;
    }

    private void SetStatusColor(GameObject explicitTarget, string[] fallbackNames, DashboardStatusLevel level)
    {
        Color color = ResolveStatusColor(level);
        if (explicitTarget != null)
        {
            ApplyGraphicColor(explicitTarget, color);
            return;
        }

        foreach (string fallbackName in fallbackNames)
        {
            if (SetExactImageColor(fallbackName, color, $"StatusColor:{fallbackName}"))
            {
                return;
            }
        }
    }

    private static Color ResolveStatusColor(DashboardStatusLevel level)
    {
        return level switch
        {
            DashboardStatusLevel.Normal => ReadyColor,
            DashboardStatusLevel.Waiting => WaitingColor,
            DashboardStatusLevel.Error => FailedColor,
            _ => MutedTextColor
        };
    }

    private static void ApplyGraphicColor(GameObject target, Color color)
    {
        if (target == null)
        {
            return;
        }

        Graphic graphic = target.GetComponent<Graphic>();
        if (graphic != null && graphic is not TMP_Text)
        {
            SetGraphicColorIfChanged(graphic, color);
            return;
        }

        Graphic[] childGraphics = target.GetComponentsInChildren<Graphic>(true);
        foreach (Graphic childGraphic in childGraphics)
        {
            if (childGraphic != null && childGraphic is not TMP_Text)
            {
                SetGraphicColorIfChanged(childGraphic, color);
                return;
            }
        }
    }

    private static void SetGraphicColorIfChanged(Graphic graphic, Color color)
    {
        if (graphic != null && graphic.color != color)
        {
            graphic.color = color;
        }
    }

    private static void SetTextIfChanged(TMP_Text text, string value)
    {
        if (text != null && text.text != value)
        {
            text.text = value;
        }
    }

    private static string NormalizeStatusToken(string state)
    {
        return string.IsNullOrWhiteSpace(state) ? string.Empty : state.Trim().ToUpperInvariant();
    }

    private static string NormalizeRobotId(string robotId)
    {
        if (string.IsNullOrWhiteSpace(robotId))
        {
            return string.Empty;
        }

        string normalized = robotId.Trim().ToLowerInvariant().Replace("_", "-");
        if (normalized.StartsWith("tb3-", StringComparison.Ordinal))
        {
            string suffix = normalized.Substring("tb3-".Length);
            if (int.TryParse(suffix, out int parsed))
            {
                return $"tb3-{parsed:00}";
            }
        }

        if (normalized.StartsWith("tb3", StringComparison.Ordinal))
        {
            string suffix = normalized.Substring("tb3".Length).TrimStart('-');
            if (int.TryParse(suffix, out int parsed))
            {
                return $"tb3-{parsed:00}";
            }
        }

        return normalized;
    }

    private static bool IsOneOf(string normalized, params string[] values)
    {
        foreach (string value in values)
        {
            if (string.Equals(normalized, value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void SetStateVisuals(string targetNameKeyword, string state)
    {
        string keyword = NormalizeKeyword(targetNameKeyword);
        Color color = ResolveStateColor(state, MutedTextColor);
        bool applied = false;

        foreach (Image dot in dotImages)
        {
            if (IsUnderDashboardPeopleStatus(dot.transform))
            {
                continue;
            }

            if (ShouldLimitToSystemHealthRoot(keyword) && !IsUnderDashboardSystemHealth(dot.transform))
            {
                continue;
            }

            if (MatchesKeyword(dot.name, keyword))
            {
                SetGraphicColorIfChanged(dot, color);
                applied = true;
            }
        }

        foreach (Image pill in pillImages)
        {
            if (IsUnderDashboardPeopleStatus(pill.transform))
            {
                continue;
            }

            if (ShouldLimitToSystemHealthRoot(keyword) && !IsUnderDashboardSystemHealth(pill.transform))
            {
                continue;
            }

            if (MatchesKeyword(pill.name, keyword))
            {
                SetGraphicColorIfChanged(pill, color);
                applied = true;
            }
        }

        foreach (Image bar in barImages)
        {
            if (IsUnderDashboardPeopleStatus(bar.transform))
            {
                continue;
            }

            if (ShouldLimitToSystemHealthRoot(keyword) && !IsUnderDashboardSystemHealth(bar.transform))
            {
                continue;
            }

            if (IsFillBarImage(bar) && MatchesKeyword(bar.name, keyword))
            {
                SetGraphicColorIfChanged(bar, color);
                applied = true;
            }
        }

        WarnMissingKeywordOnce(applied, $"State:{targetNameKeyword}");
    }

    private bool SetExactImageColor(string imageName, Color color, string warningKey)
    {
        foreach (Image dot in dotImages)
        {
            if (dot != null && string.Equals(dot.name, imageName, StringComparison.Ordinal))
            {
                SetGraphicColorIfChanged(dot, color);
                return true;
            }
        }

        foreach (Image pill in pillImages)
        {
            if (pill != null && string.Equals(pill.name, imageName, StringComparison.Ordinal))
            {
                SetGraphicColorIfChanged(pill, color);
                return true;
            }
        }

        foreach (Image timelineDot in timelineDotImages)
        {
            if (timelineDot != null && string.Equals(timelineDot.name, imageName, StringComparison.Ordinal))
            {
                SetGraphicColorIfChanged(timelineDot, color);
                return true;
            }
        }

        WarnMissingKeywordOnce(false, warningKey);
        return false;
    }

    private bool SetExactImageActive(string imageName, bool active, string warningKey)
    {
        foreach (Image dot in dotImages)
        {
            if (dot != null && string.Equals(dot.name, imageName, StringComparison.Ordinal))
            {
                SetGameObjectActive(dot.gameObject, active);
                return true;
            }
        }

        foreach (Image pill in pillImages)
        {
            if (pill != null && string.Equals(pill.name, imageName, StringComparison.Ordinal))
            {
                SetGameObjectActive(pill.gameObject, active);
                return true;
            }
        }

        foreach (Image timelineDot in timelineDotImages)
        {
            if (timelineDot != null && string.Equals(timelineDot.name, imageName, StringComparison.Ordinal))
            {
                SetGameObjectActive(timelineDot.gameObject, active);
                return true;
            }
        }

        WarnMissingKeywordOnce(false, warningKey);
        return false;
    }

    private static void SetGameObjectActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }

    private DashboardRobotSlotReferences GetRobotSlot(string robotId)
    {
        int robotNumber = ExtractRobotNumber(robotId);
        return GetRobotSlotByNumber(robotNumber);
    }

    private DashboardRobotSlotReferences GetRobotSlotByNumber(int robotNumber)
    {
        if (dashboardRobotSlots == null || robotNumber < 1 || robotNumber > dashboardRobotSlots.Length)
        {
            return null;
        }

        return dashboardRobotSlots[robotNumber - 1];
    }

    private bool TrySetRobotBatteryBar(string robotId, float value01)
    {
        int robotNumber = ExtractRobotNumber(robotId);
        string twoDigitNumber = robotNumber.ToString("00");
        string[] preferredKeywords =
        {
            $"Battery{twoDigitNumber}",
            $"RobotBattery{twoDigitNumber}",
            $"tb3-{twoDigitNumber}",
            $"TB3_{twoDigitNumber}"
        };

        foreach (string keyword in preferredKeywords)
        {
            if (TrySetBar01(keyword, value01, false))
            {
                return true;
            }
        }

        List<Image> batteryBars = FindProgressBarCandidates(Tokenize("Battery"));
        if (batteryBars.Count >= robotNumber && robotNumber > 0)
        {
            return TryApplyFillAmount(batteryBars[robotNumber - 1], value01);
        }

        return false;
    }

    private void SetRobotBatteryPercentText(string robotId, float percent)
    {
        int robotNumber = ExtractRobotNumber(robotId);
        if (robotNumber < 1 || robotNumber > 3)
        {
            return;
        }

        string textName = $"Text_DashboardRobotBattery{robotNumber:00}Percent";
        TMP_Text text = FindExactText(textName);
        if (text != null)
        {
            SetTextIfChanged(text, FormatPercentOnly(percent));
        }

        DashboardRobotSlotReferences slot = GetRobotSlot(robotId);
        if (slot?.textBatteryPercent != null)
        {
            SetTextIfChanged(slot.textBatteryPercent, FormatPercentOnly(percent));
        }
    }

    private void SetRobotSlotBatteryState(string robotId, float percent, string robotStatus)
    {
        ResolveDashboardRobotSlots();
        DashboardRobotSlotReferences slot = GetRobotSlot(robotId);
        if (slot == null)
        {
            WarnMissingKeywordOnce(false, $"RobotSlot:{robotId}");
            return;
        }

        DashboardBatteryIconState iconState = ResolveDashboardBatteryIconState(percent, robotStatus);
        Color stateColor = ResolveDashboardBatteryColor(iconState);
        SetGameObjectActive(slot.iconBatteryUnknown, iconState == DashboardBatteryIconState.Unknown);
        SetGameObjectActive(slot.iconBatteryCharging, iconState == DashboardBatteryIconState.Charging);
        SetGameObjectActive(slot.iconBatteryFull, iconState == DashboardBatteryIconState.Full);
        SetGameObjectActive(slot.iconBatteryMedium, iconState == DashboardBatteryIconState.Medium);
        SetGameObjectActive(slot.iconBatteryLow, iconState == DashboardBatteryIconState.Low);
        SetGameObjectActive(slot.iconBatteryEmpty, iconState == DashboardBatteryIconState.Empty);
        ApplyGraphicColor(GetBatteryIconObject(slot, iconState), stateColor);

        if (slot.textBatteryPercent != null)
        {
            SetGraphicColorIfChanged(slot.textBatteryPercent, stateColor);
        }
    }

    private enum DashboardBatteryIconState
    {
        Unknown,
        Charging,
        Full,
        Medium,
        Low,
        Empty
    }

    private static DashboardBatteryIconState ResolveDashboardBatteryIconState(float percent, string robotStatus)
    {
        if (float.IsNaN(percent) || float.IsInfinity(percent) || percent < 0f)
        {
            return DashboardBatteryIconState.Unknown;
        }

        if (!string.IsNullOrWhiteSpace(robotStatus) &&
            robotStatus.Trim().Equals("CHARGING", StringComparison.OrdinalIgnoreCase))
        {
            return DashboardBatteryIconState.Charging;
        }

        float normalizedPercent = Mathf.Clamp(percent > 1f ? percent : percent * 100f, 0f, 100f);
        if (normalizedPercent >= 76f)
        {
            return DashboardBatteryIconState.Full;
        }

        if (normalizedPercent >= 51f)
        {
            return DashboardBatteryIconState.Medium;
        }

        if (normalizedPercent >= 21f)
        {
            return DashboardBatteryIconState.Low;
        }

        return DashboardBatteryIconState.Empty;
    }

    private static GameObject GetBatteryIconObject(DashboardRobotSlotReferences slot, DashboardBatteryIconState iconState)
    {
        if (slot == null)
        {
            return null;
        }

        return iconState switch
        {
            DashboardBatteryIconState.Charging => slot.iconBatteryCharging,
            DashboardBatteryIconState.Full => slot.iconBatteryFull,
            DashboardBatteryIconState.Medium => slot.iconBatteryMedium,
            DashboardBatteryIconState.Low => slot.iconBatteryLow,
            DashboardBatteryIconState.Empty => slot.iconBatteryEmpty,
            _ => slot.iconBatteryUnknown
        };
    }

    private static Color ResolveDashboardBatteryColor(DashboardBatteryIconState iconState)
    {
        return iconState switch
        {
            DashboardBatteryIconState.Charging => ChargingColor,
            DashboardBatteryIconState.Full => ReadyColor,
            DashboardBatteryIconState.Medium => BatteryMediumColor,
            DashboardBatteryIconState.Low => WaitingColor,
            DashboardBatteryIconState.Empty => FailedColor,
            _ => MutedTextColor
        };
    }

    private void SetRobotBatteryGaugeFill(string robotId, float percent)
    {
        int robotNumber = ExtractRobotNumber(robotId);
        if (robotNumber < 1 || robotNumber > 3)
        {
            return;
        }

        float value01 = PercentTo01(percent);
        TrySetExactRobotBatteryFillAmount($"Bar_DashboardRobotBattery{robotNumber:00}_Fill", value01);
    }

    private void SetRobotBatteryFillColor(string robotId, Color color)
    {
        int robotNumber = ExtractRobotNumber(robotId);
        if (robotNumber < 1 || robotNumber > 3)
        {
            return;
        }

        SetExactFillBarColor($"Bar_DashboardRobotBattery{robotNumber:00}_Fill", color);
    }

    private bool TrySetExactRobotBatteryFillAmount(string exactBarName, float value01)
    {
        foreach (Image bar in barImages)
        {
            if (bar == null || !string.Equals(bar.name, exactBarName, StringComparison.Ordinal))
            {
                continue;
            }

            if (IsUnderDashboardPeopleStatus(bar.transform))
            {
                continue;
            }

            if (!IsFillBarImage(bar))
            {
                WarnMissingKeywordOnce(false, $"BatteryFill:{exactBarName}");
                return false;
            }

            if (bar.type != Image.Type.Filled)
            {
                WarnMissingKeywordOnce(false, $"BatteryFilledType:{exactBarName}");
                return false;
            }

            if (bar.fillMethod != Image.FillMethod.Radial360)
            {
                WarnMissingKeywordOnce(false, $"BatteryRadial360:{exactBarName}");
            }

            bar.fillAmount = Mathf.Clamp01(value01);
            return true;
        }

        WarnMissingKeywordOnce(false, $"BatteryFill:{exactBarName}");
        return false;
    }

    private bool SetExactFillBarColor(string exactBarName, Color color)
    {
        foreach (Image bar in barImages)
        {
            if (bar != null && string.Equals(bar.name, exactBarName, StringComparison.Ordinal) && IsFillBarImage(bar))
            {
                if (IsUnderDashboardPeopleStatus(bar.transform))
                {
                    continue;
                }

                SetGraphicColorIfChanged(bar, color);
                return true;
            }
        }

        WarnMissingKeywordOnce(false, $"FillColor:{exactBarName}");
        return false;
    }

    private static string FormatPercentOnly(float percent)
    {
        if (float.IsNaN(percent) || float.IsInfinity(percent) || percent < 0f)
        {
            return "--%";
        }

        float normalizedPercent = percent > 1f ? percent : percent * 100f;
        return $"{Mathf.RoundToInt(Mathf.Clamp(normalizedPercent, 0f, 100f))}%";
    }

    private bool TrySetBar01(string targetNameKeyword, float value01, bool warnIfMissing)
    {
        List<Image> candidates = FindProgressBarCandidates(Tokenize(targetNameKeyword));
        float clamped = Mathf.Clamp01(value01);
        bool foundCandidate = false;

        foreach (Image candidate in candidates)
        {
            foundCandidate = true;
            if (TryApplyFillAmount(candidate, clamped))
            {
                return true;
            }
        }

        if (warnIfMissing)
        {
            WarnMissingKeywordOnce(foundCandidate, $"Bar:{targetNameKeyword}");
        }

        return false;
    }

    private bool TrySetExactFillBar01(string exactBarName, float value01, bool warnIfMissing)
    {
        foreach (Image bar in barImages)
        {
            if (bar == null || !string.Equals(bar.name, exactBarName, StringComparison.Ordinal))
            {
                continue;
            }

            if (IsUnderDashboardPeopleStatus(bar.transform))
            {
                continue;
            }

            if (!IsFillBarImage(bar))
            {
                WarnMissingKeywordOnce(false, $"FillBar:{exactBarName}");
                return false;
            }

            return TryApplyFillAmount(bar, value01);
        }

        if (warnIfMissing)
        {
            WarnMissingKeywordOnce(false, $"Bar:{exactBarName}");
        }

        return false;
    }

    private void SetPercentText(string textObjectName, string label, float value01)
    {
        TMP_Text text = FindExactText(textObjectName);
        if (text == null)
        {
            WarnMissingKeywordOnce(false, $"Text:{textObjectName}");
            return;
        }

        SetTextIfChanged(text, $"{label} {Mathf.RoundToInt(Mathf.Clamp01(value01) * 100f)}%");
    }

    private void SetExactTextValue(string textObjectName, string value)
    {
        TMP_Text text = FindExactText(textObjectName);
        if (text == null)
        {
            WarnMissingKeywordOnce(false, $"Text:{textObjectName}");
            return;
        }

        SetTextIfChanged(text, value);
    }

    private TMP_Text FindExactText(string textObjectName)
    {
        foreach (TMP_Text text in dashboardTexts)
        {
            if (text != null && string.Equals(text.name, textObjectName, StringComparison.Ordinal))
            {
                return text;
            }
        }

        return null;
    }

    private TMP_Text FindExactTextUnderRoot(string textObjectName, Transform root)
    {
        if (root == null)
        {
            return null;
        }

        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text text in texts)
        {
            if (text != null && string.Equals(text.name, textObjectName, StringComparison.Ordinal))
            {
                return text;
            }
        }

        return null;
    }

    private static TMP_Text FindTextUnderRoot(string textObjectName, Transform root)
    {
        if (root == null)
        {
            return null;
        }

        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text text in texts)
        {
            if (text != null && string.Equals(text.name, textObjectName, StringComparison.Ordinal))
            {
                return text;
            }
        }

        return null;
    }

    private static GameObject FindGameObjectUnderRoot(string objectName, Transform root)
    {
        Transform transform = FindDescendantUnderRootStatic(objectName, root);
        return transform != null ? transform.gameObject : null;
    }

    private static Transform FindDescendantUnderRootStatic(string objectName, Transform root)
    {
        if (root == null)
        {
            return null;
        }

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child != null && string.Equals(child.name, objectName, StringComparison.Ordinal))
            {
                return child;
            }
        }

        return null;
    }

    private static RawImage FindSceneRawImage(string objectName)
    {
        Transform transform = FindSceneTransform(objectName);
        return transform != null ? transform.GetComponent<RawImage>() : null;
    }

    private static Transform FindFirstSceneTransform(params string[] objectNames)
    {
        if (objectNames == null)
        {
            return null;
        }

        foreach (string objectName in objectNames)
        {
            Transform transform = FindSceneTransform(objectName);
            if (transform != null)
            {
                return transform;
            }
        }

        return null;
    }

    private void SetPeopleStatusPill(string exactName, string state)
    {
        Image image = FindExactImageUnderRoot(exactName, dashboardPeopleStatusRoot);
        if (image == null)
        {
            WarnMissingKeywordOnce(false, $"PeoplePill:{exactName}");
            return;
        }

        SetGraphicColorIfChanged(image, ResolveStateColor(state, MutedTextColor));
    }

    private void SetPeopleSlotGroupAlpha(string prefix, int slotCount, int filledCount)
    {
        for (int i = 1; i <= slotCount; i++)
        {
            string slotName = slotCount == 1 ? prefix : $"{prefix}{i:00}";
            Image image = FindExactImageUnderRoot(slotName, dashboardPeopleStatusRoot);
            if (image == null)
            {
                WarnMissingKeywordOnce(false, $"PeopleSlot:{slotName}");
                continue;
            }

            Color color = image.color;
            color.a = i <= filledCount ? 1f : 0.25f;
            SetGraphicColorIfChanged(image, color);
        }
    }

    private void SetPeopleSlotGroupActive(string prefix, int slotCount, int filledCount)
    {
        for (int i = 1; i <= slotCount; i++)
        {
            string slotName = slotCount == 1 ? prefix : $"{prefix}{i:00}";
            Image image = FindExactImageUnderRoot(slotName, dashboardPeopleStatusRoot);
            if (image == null)
            {
                WarnMissingKeywordOnce(false, $"PeopleSlot:{slotName}");
                continue;
            }

            SetGameObjectActive(image.gameObject, i <= filledCount);
        }
    }

    private void SetVisitorSlotsAlpha(int visitorCount)
    {
        List<Image> visitorSlots = ResolveVisitorSlotImages();
        for (int i = 0; i < visitorSlots.Count; i++)
        {
            Image image = visitorSlots[i];
            if (image == null)
            {
                continue;
            }

            Color color = image.color;
            color.a = i < visitorCount ? 1f : 0.25f;
            SetGraphicColorIfChanged(image, color);
        }
    }

    private void SetVisitorSlotsActive(int visitorCount)
    {
        List<Image> visitorSlots = ResolveVisitorSlotImages();
        for (int i = 0; i < visitorSlots.Count; i++)
        {
            Image image = visitorSlots[i];
            if (image == null)
            {
                continue;
            }

            SetGameObjectActive(image.gameObject, i < visitorCount);
        }
    }

    private List<Image> ResolveVisitorSlotImages()
    {
        CacheDashboardRoots();
        LogVisitorSlotsOnce(visitorSlotImagesCache);
        return visitorSlotImagesCache;
    }

    private void LogVisitorSlotsOnce(List<Image> slots)
    {
        if (visitorSlotNamesLogged)
        {
            return;
        }

        visitorSlotNamesLogged = true;
        Debug.Log($"[DashboardRuntimeBinder] Visitor slots resolved count={slots.Count}");
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] != null)
            {
                Debug.Log($"[DashboardRuntimeBinder] Visitor slot {i}={slots[i].name}");
            }
        }
    }

    private void SetPeopleSlotGroup(string prefix, int slotCount, int filledCount, Color filledColor)
    {
        for (int i = 1; i <= slotCount; i++)
        {
            string slotName = slotCount == 1 ? prefix : $"{prefix}{i:00}";
            Image image = FindExactImageUnderRoot(slotName, dashboardPeopleStatusRoot);
            if (image == null)
            {
                WarnMissingKeywordOnce(false, $"PeopleSlot:{slotName}");
                continue;
            }

            SetGraphicColorIfChanged(image, i <= filledCount ? filledColor : EmptySlotColor);
        }
    }

    private void SetPeopleStatusDecorationsInactive()
    {
        SetPeopleStatusObjectInactive("Pill_DashboardPeopleStatusExtra");
        SetPeopleStatusObjectInactive("Pill_DashboardLastAccessStatus");
        SetPeopleStatusObjectInactive("Text_DashboardPeopleStatusPercent");
        SetPeopleStatusObjectInactive("Bar_DashboardPeopleStatus_Background");
        SetPeopleStatusObjectInactive("Bar_DashboardPeopleStatus_Fill");
    }

    private void SetPeopleStatusObjectInactive(string objectName)
    {
        Transform target = FindDescendantUnderRoot(objectName, dashboardPeopleStatusRoot);
        if (target != null && target.gameObject.activeSelf)
        {
            target.gameObject.SetActive(false);
        }
    }

    private void SetPeopleStatusObjectActive(string objectName, bool active)
    {
        Transform target = FindDescendantUnderRoot(objectName, dashboardPeopleStatusRoot);
        if (target != null && target.gameObject.activeSelf != active)
        {
            target.gameObject.SetActive(active);
        }
    }

    private bool TrySetPeopleFillBar01(string exactName, float value01)
    {
        Image image = FindExactImageUnderRoot(exactName, dashboardPeopleStatusRoot);
        if (image == null)
        {
            WarnMissingKeywordOnce(false, $"PeopleFill:{exactName}");
            return false;
        }

        if (!IsFillBarImage(image))
        {
            WarnMissingKeywordOnce(false, $"PeopleFillName:{exactName}");
            return false;
        }

        SetGraphicColorIfChanged(image, ResolveStateColor(PercentToState(value01 * 100f), MutedTextColor));
        return TryApplyFillAmount(image, value01);
    }

    private static Color ResolveAttendanceInColor(int attendanceIn)
    {
        if (attendanceIn <= 0)
        {
            return FailedColor;
        }

        if (attendanceIn <= 2)
        {
            return WaitingColor;
        }

        if (attendanceIn <= 4)
        {
            return new Color(0.95f, 0.88f, 0.26f, 1f);
        }

        return ReadyColor;
    }

    private static Color ResolveAttendanceOutColor(int attendanceOut)
    {
        if (attendanceOut <= 0)
        {
            return ReadyColor;
        }

        if (attendanceOut <= 2)
        {
            return WaitingColor;
        }

        return FailedColor;
    }

    private Image FindExactImageUnderRoot(string imageName, Transform root)
    {
        if (root == null)
        {
            return null;
        }

        if (root == dashboardPeopleStatusRoot)
        {
            CachePeopleStatusObjects();
            return peopleStatusImagesByName.TryGetValue(imageName, out Image cachedImage) ? cachedImage : null;
        }

        Image[] images = root.GetComponentsInChildren<Image>(true);
        foreach (Image image in images)
        {
            if (image != null && string.Equals(image.name, imageName, StringComparison.Ordinal))
            {
                return image;
            }
        }

        return null;
    }

    private Transform FindDescendantUnderRoot(string objectName, Transform root)
    {
        if (root == null)
        {
            return null;
        }

        if (root == dashboardPeopleStatusRoot)
        {
            CachePeopleStatusObjects();
            return peopleStatusTransformsByName.TryGetValue(objectName, out Transform cachedTransform) ? cachedTransform : null;
        }

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child != null && string.Equals(child.name, objectName, StringComparison.Ordinal))
            {
                return child;
            }
        }

        return null;
    }

    private bool IsUnderDashboardPeopleStatus(Transform transform)
    {
        if (transform == null)
        {
            return false;
        }

        if (dashboardPeopleStatusRoot == null)
        {
            CacheDashboardRoots();
        }

        return IsUnderRoot(transform, dashboardPeopleStatusRoot);
    }

    private bool IsUnderDashboardSystemHealth(Transform transform)
    {
        if (transform == null)
        {
            return false;
        }

        if (dashboardSystemHealthRoot == null)
        {
            CacheDashboardRoots();
        }

        return IsUnderRoot(transform, dashboardSystemHealthRoot);
    }

    private static bool ShouldLimitToSystemHealthRoot(string normalizedKeyword)
    {
        switch (normalizedKeyword)
        {
            case "SERVER":
            case "WEBSOCKET":
            case "ROS2":
            case "AI":
            case "AIMODEL":
            case "MODEL":
            case "DB":
            case "DATABASE":
            case "SYSTEM":
            case "HEALTH":
                return true;
            default:
                return false;
        }
    }

    private static bool IsUnderRoot(Transform transform, Transform root)
    {
        if (transform == null || root == null)
        {
            return false;
        }

        Transform current = transform;
        while (current != null)
        {
            if (current == root)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static Transform FindSceneTransform(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return null;
        }

        GameObject[] sceneObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (GameObject sceneObject in sceneObjects)
        {
            if (sceneObject != null && sceneObject.scene.IsValid() && sceneObject.name == objectName)
            {
                return sceneObject.transform;
            }
        }

        return null;
    }

    private bool TryApplyFillAmount(Image bar, float value01)
    {
        if (bar == null)
        {
            return false;
        }

        if (bar.type != Image.Type.Filled)
        {
            WarnMissingKeywordOnce(false, $"FilledBar:{bar.name}");
            return false;
        }

        float clampedValue = Mathf.Clamp01(value01);
        if (!Mathf.Approximately(bar.fillAmount, clampedValue))
        {
            bar.fillAmount = clampedValue;
        }

        return true;
    }

    private List<Image> FindProgressBarCandidates(List<string> tokens)
    {
        List<Image> candidates = new List<Image>();
        if (tokens.Count == 0)
        {
            return candidates;
        }

        foreach (Image bar in barImages)
        {
            if (bar == null)
            {
                continue;
            }

            if (IsUnderDashboardPeopleStatus(bar.transform))
            {
                continue;
            }

            string normalizedName = NormalizeKeyword(bar.name);
            if (!IsFillBarName(normalizedName) ||
                IsStatusOnlyKeyword(normalizedName) ||
                !IsProgressBarKeyword(normalizedName) ||
                !MatchesAllTokens(normalizedName, tokens))
            {
                continue;
            }

            candidates.Add(bar);
        }

        if (candidates.Count == 0 && tokens.Count > 1)
        {
            foreach (Image bar in barImages)
            {
                if (bar == null)
                {
                    continue;
                }

                if (IsUnderDashboardPeopleStatus(bar.transform))
                {
                    continue;
                }

                string normalizedName = NormalizeKeyword(bar.name);
                if (!IsFillBarName(normalizedName) ||
                    IsStatusOnlyKeyword(normalizedName) ||
                    !IsProgressBarKeyword(normalizedName) ||
                    !MatchesAnyToken(normalizedName, tokens))
                {
                    continue;
                }

                candidates.Add(bar);
            }
        }

        candidates.Sort(CompareProgressBarCandidate);
        return candidates;
    }

    private static int CompareProgressBarCandidate(Image left, Image right)
    {
        int leftScore = GetCandidateScore(left);
        int rightScore = GetCandidateScore(right);
        int scoreCompare = rightScore.CompareTo(leftScore);
        if (scoreCompare != 0)
        {
            return scoreCompare;
        }

        return string.Compare(left != null ? left.name : string.Empty, right != null ? right.name : string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetCandidateScore(Image image)
    {
        if (image == null)
        {
            return 0;
        }

        string normalizedName = NormalizeKeyword(image.name);
        int score = 0;
        if (normalizedName.Contains("FILL"))
        {
            score += 100;
        }

        if (image.type == Image.Type.Filled)
        {
            score += 50;
        }

        if (normalizedName.Contains("PROGRESS") || normalizedName.Contains("COVERAGE") || normalizedName.Contains("BATTERY"))
        {
            score += 20;
        }

        return score;
    }

    private static bool IsFillBarImage(Image image)
    {
        return image != null && IsFillBarName(NormalizeKeyword(image.name));
    }

    private static bool IsFillBarName(string normalizedName)
    {
        return !string.IsNullOrWhiteSpace(normalizedName) &&
               normalizedName.Contains("FILL") &&
               !normalizedName.Contains("BACKGROUND");
    }

    private TMP_Text FindTextByKeyword(string targetNameKeyword)
    {
        string keyword = NormalizeKeyword(targetNameKeyword);
        foreach (TMP_Text text in dashboardTexts)
        {
            if (text != null && (MatchesKeyword(text.name, keyword) || MatchesKeyword(text.text, keyword)))
            {
                return text;
            }
        }

        return null;
    }

    private static Color ResolveStateColor(string state, Color fallback)
    {
        string normalized = string.IsNullOrWhiteSpace(state) ? string.Empty : state.Trim().ToUpperInvariant();
        switch (normalized)
        {
            case "READY":
            case "ONLINE":
            case "CONNECTED":
            case "NORMAL":
            case "ACCEPTED":
            case "OK":
                return ReadyColor;
            case "WAITING":
            case "STANDBY":
            case "PENDING":
                return WaitingColor;
            case "OFFLINE":
            case "CLOSED":
            case "DISCONNECTED":
            case "FAILED":
            case "REJECTED":
                return FailedColor;
            case "ERROR":
            case "EMERGENCY":
            case "EMERGENCY_STOP":
                return EmergencyColor;
            default:
                return fallback;
        }
    }

    private static Color ResolveTimelineColor(string eventCategory)
    {
        string normalized = NormalizeKeyword(eventCategory);
        switch (normalized)
        {
            case "WARNING":
            case "NO_HELMET":
            case "LOW_BATTERY":
            case "WAITING":
                return WaitingColor;
            case "ERROR":
            case "EMERGENCY":
            case "FALL":
            case "FIRE":
            case "STOP":
            case "FAILED":
            case "DISCONNECTED":
                return FailedColor;
            case "ROBOT":
            case "CHARGING":
            case "SYSTEM":
            case "NAV":
            case "CAMERA":
                return new Color(0.24f, 0.55f, 1f, 1f);
            default:
                return ReadyColor;
        }
    }

    private static string PercentToState(float percent)
    {
        if (float.IsNaN(percent) || float.IsInfinity(percent) || percent < 0f)
        {
            return string.Empty;
        }

        if (percent >= 70f)
        {
            return "READY";
        }

        if (percent >= 35f)
        {
            return "WAITING";
        }

        return "FAILED";
    }

    private static Color ResolveBatteryColor(float percent)
    {
        if (float.IsNaN(percent) || float.IsInfinity(percent) || percent < 0f)
        {
            return MutedTextColor;
        }

        float normalizedPercent = percent > 1f ? percent : percent * 100f;
        if (normalizedPercent <= 20f)
        {
            return FailedColor;
        }

        if (normalizedPercent <= 50f)
        {
            return WaitingColor;
        }

        return ReadyColor;
    }

    private static float PercentTo01(float percent)
    {
        if (float.IsNaN(percent) || float.IsInfinity(percent) || percent < 0f)
        {
            return 0f;
        }

        return Mathf.Clamp01(percent > 1f ? percent / 100f : percent);
    }

    private static bool IsProgressBarKeyword(string source)
    {
        string normalized = NormalizeKeyword(source);
        return normalized.Contains("COVERAGE") ||
               normalized.Contains("PROGRESS") ||
               normalized.Contains("HEALTH") ||
               normalized.Contains("ACTIVITY");
    }

    private static bool IsStatusOnlyKeyword(string source)
    {
        string normalized = NormalizeKeyword(source);
        return normalized.Contains("SERVER") ||
               normalized.Contains("WEBSOCKET") ||
               normalized.Contains("ROS2") ||
               normalized.Contains("AI") ||
               normalized.Contains("DB") ||
               normalized.Contains("DATABASE");
    }

    private static List<string> Tokenize(string source)
    {
        List<string> tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(source))
        {
            return tokens;
        }

        string normalized = NormalizeKeyword(source);
        string current = string.Empty;
        for (int i = 0; i < normalized.Length; i++)
        {
            char character = normalized[i];
            if (char.IsLetterOrDigit(character))
            {
                current += character;
            }
            else if (!string.IsNullOrWhiteSpace(current))
            {
                tokens.Add(current);
                current = string.Empty;
            }
        }

        if (!string.IsNullOrWhiteSpace(current))
        {
            tokens.Add(current);
        }

        return tokens;
    }

    private static bool MatchesAllTokens(string normalizedSource, List<string> tokens)
    {
        foreach (string token in tokens)
        {
            if (!normalizedSource.Contains(token))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesAnyToken(string normalizedSource, List<string> tokens)
    {
        foreach (string token in tokens)
        {
            if (normalizedSource.Contains(token))
            {
                return true;
            }
        }

        return false;
    }

    private static int ExtractRobotNumber(string robotId)
    {
        if (string.IsNullOrWhiteSpace(robotId))
        {
            return 1;
        }

        List<string> tokens = Tokenize(robotId);
        for (int i = tokens.Count - 1; i >= 0; i--)
        {
            if (int.TryParse(tokens[i], out int parsed) && parsed > 0)
            {
                return parsed;
            }
        }

        return 1;
    }

    private static string NormalizeKeyword(string keyword)
    {
        return string.IsNullOrWhiteSpace(keyword) ? string.Empty : keyword.Trim().ToUpperInvariant();
    }

    private static bool MatchesKeyword(string source, string normalizedKeyword)
    {
        return !string.IsNullOrWhiteSpace(source) &&
               !string.IsNullOrWhiteSpace(normalizedKeyword) &&
               source.ToUpperInvariant().Contains(normalizedKeyword);
    }

    private void WarnMissingKeywordOnce(bool applied, string warningKey)
    {
        if (applied || missingKeywordWarnings.Contains(warningKey))
        {
            return;
        }

        missingKeywordWarnings.Add(warningKey);
        Debug.LogWarning($"[DashboardRuntimeBinder] Target not found for {warningKey}");
    }
}

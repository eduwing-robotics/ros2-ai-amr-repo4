using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class scr_ControlTowerCameraStreamManager : MonoBehaviour
{
    private const string GlobalSourceKey = "global";
    private const string Tb3_01SourceKey = "tb3-01";
    private const string Tb3_02SourceKey = "tb3-02";

    [Header("References")]
    [SerializeField] private scr_ControlTowerUIManager uiManager;
    [SerializeField] private scr_CameraJpegWebSocketClient globalCctvClient;
    [SerializeField] private scr_CameraJpegWebSocketClient tb3CameraClient;
    [SerializeField] private scr_CameraJpegWebSocketClient tb3_01FixedClient;
    [SerializeField] private scr_CameraJpegWebSocketClient tb3_02FixedClient;
    [SerializeField] private RawImage rawImageMainCameraFeedStream;
    [SerializeField] private RawImage rawImageGlobalCctvStream;
    [SerializeField] private RawImage rawImageFactory2DGlobalCctv;
    [SerializeField] private RawImage rawImageTb3CameraStream;
    [SerializeField] private RawImage rawImageBottomGlobalCctvStream;
    [SerializeField] private RawImage rawImageBottomTb3_01CameraStream;
    [SerializeField] private RawImage rawImageBottomTb3_02CameraStream;
    [SerializeField] private TMPro.TMP_Text textBottomGlobalCctvStatus;
    [SerializeField] private TMPro.TMP_Text textBottomGlobalCctvLastDetection;
    [SerializeField] private TMPro.TMP_Text textBottomTb3_01StreamState;
    [SerializeField] private TMPro.TMP_Text textBottomTb3_01LastDetection;
    [SerializeField] private TMPro.TMP_Text textBottomTb3_02StreamState;
    [SerializeField] private TMPro.TMP_Text textBottomTb3_02LastDetection;
    [SerializeField] private GameObject imageMainCameraFeedPlaceholder;

    [Header("Video WebSocket URIs")]
    [SerializeField] private string globalCctvUri = "ws://127.0.0.1:8000/ws/video/global";
    [SerializeField] private string tb3_01Uri = "ws://127.0.0.1:8000/ws/video/1";
    [SerializeField] private string tb3_02Uri = "ws://127.0.0.1:8000/ws/video/2";

    private string selectedRobotId = "tb3-01";
    private string selectedMainFeedId = "tb3-01";
    private string globalStatus = "Waiting";
    private string tb3Status = "Waiting";
    private string tb3_01Status = "Waiting";
    private string tb3_02Status = "Waiting";
    private string globalLastFrameTime = "--";
    private string selectedTb3LastFrameTime = "--";
    private string tb3_01LastFrameTime = "--";
    private string tb3_02LastFrameTime = "--";
    private string activeTb3RobotId = "None";
    private bool streamsRequested;
    private bool isCameraViewActive;
    private bool globalConnectedLogged;
    private bool tb3ConnectedLogged;
    private bool noStreamLogged;
    private bool mainCameraFeedPlaceholderWarningShown;
    private bool mainFeedRawImageWarningShown;
    private bool tb3PreviewRawImageWarningShown;
    private bool cameraBindingReportWritten;
    private bool cameraBindingErrorWritten;
    private bool fixedTextureErrorWritten;
    private bool referencesResolved;
    private int tb3SwitchRequestId;

    private void Awake()
    {
        ResolveReferences();
        BindClientEvents();
        LogCameraBindingsOnce();
    }

    public void ConnectCameraStreams()
    {
        ResolveReferences();
        BindClientEvents();
        streamsRequested = true;

        if (globalCctvClient != null)
        {
            globalCctvClient.SetDiagnosticLabel("GlobalCctvVideoClient");
            globalCctvClient.SetDiagnosticRobotId(GlobalSourceKey);
            globalCctvClient.SetSourceKey(GlobalSourceKey);
            globalCctvClient.SetStreamUri(globalCctvUri);
            ApplyStreamTargets();
            globalCctvClient.Connect();
        }

        ConnectFixedTb3PreviewClients();
        if (isCameraViewActive)
        {
            SwitchTb3CameraStream(selectedRobotId);
        }
        PublishStatus();
    }

    public void DisconnectCameraStreams()
    {
        bool shouldLogDisconnect = streamsRequested || globalConnectedLogged || tb3ConnectedLogged;
        isCameraViewActive = false;
        streamsRequested = false;
        tb3SwitchRequestId++;

        if (globalCctvClient != null)
        {
            globalCctvClient.Disconnect();
        }

        if (tb3CameraClient != null)
        {
            tb3CameraClient.ClearTargetFrames();
            tb3CameraClient.Disconnect();
        }

        if (tb3_01FixedClient != null)
        {
            tb3_01FixedClient.ClearTargetFrames();
            tb3_01FixedClient.Disconnect();
        }

        if (tb3_02FixedClient != null && tb3_02FixedClient != tb3_01FixedClient)
        {
            tb3_02FixedClient.ClearTargetFrames();
            tb3_02FixedClient.Disconnect();
        }

        globalConnectedLogged = false;
        tb3ConnectedLogged = false;
        globalStatus = "Disconnected";
        tb3Status = "Disconnected";
        tb3_01Status = "Disconnected";
        tb3_02Status = "Disconnected";
        PublishSourceStatus("global", globalStatus);
        PublishSourceStatus("tb3-01", tb3_01Status);
        PublishSourceStatus("tb3-02", tb3_02Status);
        RefreshBottomPreviewTextNow();
        PublishStatus();

        if (shouldLogDisconnect)
        {
            LogCamera("Stream disconnected");
        }
    }

    public void SetSelectedRobot(string robotId)
    {
        string nextRobotId = string.IsNullOrWhiteSpace(robotId) ? "tb3-01" : robotId.Trim().ToLowerInvariant();
        if (selectedRobotId == nextRobotId)
        {
            return;
        }

        selectedRobotId = nextRobotId;
        noStreamLogged = false;

        if (streamsRequested && isCameraViewActive)
        {
            SwitchTb3CameraStream(selectedRobotId);
        }
        else
        {
            PublishStatus();
        }
    }

    public bool SelectMainCameraFeed(string feedId)
    {
        ResolveReferences();
        string nextFeedId = NormalizeMainFeedId(feedId);
        selectedMainFeedId = nextFeedId;
        ClearMainFeedTexture();
        ApplyStreamTargets();

        if (nextFeedId == "global")
        {
            if (!IsCameraStatusConnected(globalStatus))
            {
                SetMainCameraFeedPlaceholderVisible(true);
                LogCamera("GLOBAL CCTV feed selected but stream is not connected");
                return false;
            }

            SetMainCameraFeedPlaceholderVisible(!HasReceivedFrame(globalLastFrameTime));
            LogCamera("Main feed selected: GLOBAL CCTV");
            return true;
        }

        ClearTb3PreviewTexture();
        if (nextFeedId == "tb3-03")
        {
            tb3SwitchRequestId++;
            if (tb3CameraClient != null)
            {
                tb3CameraClient.ClearTargetFrames();
                tb3CameraClient.Disconnect();
            }

            tb3Status = "No Stream";
            selectedTb3LastFrameTime = "--";
            PublishSelectedTb3FrameApplied(nextFeedId, selectedTb3LastFrameTime);
            PublishStatus();
            SetMainCameraFeedPlaceholderVisible(true);
            LogCamera("TB3-03 feed selected but stream is not connected");
            return false;
        }

        if (nextFeedId != selectedRobotId)
        {
            SetSelectedRobot(nextFeedId);
        }
        else if (isCameraViewActive)
        {
            SwitchTb3CameraStream(nextFeedId);
        }

        if (!IsCameraStatusConnected(tb3Status))
        {
            SetMainCameraFeedPlaceholderVisible(true);
            LogCamera($"{FormatMainFeedLabel(nextFeedId)} feed selected but stream is not connected");
            return false;
        }

        SetMainCameraFeedPlaceholderVisible(!HasReceivedFrame(selectedTb3LastFrameTime));
        LogCamera($"Main feed selected: {FormatMainFeedLabel(nextFeedId)}");
        return true;
    }

    public void SetMainCameraFeedSelection(string feedId)
    {
        ResolveReferences();
        selectedMainFeedId = NormalizeMainFeedId(feedId);
        ApplyStreamTargets();

        if (selectedMainFeedId == "tb3-03")
        {
            ClearMainFeedTexture();
            ClearTb3PreviewTexture();
            selectedTb3LastFrameTime = "--";
            PublishSelectedTb3FrameApplied(selectedMainFeedId, selectedTb3LastFrameTime);
            SetMainCameraFeedPlaceholderVisible(true);
        }
    }

    public void SetCameraViewActive(bool active)
    {
        isCameraViewActive = active;
        ApplyStreamTargets();
    }

    public void SwitchTb3CameraStream(string robotId)
    {
        selectedRobotId = string.IsNullOrWhiteSpace(robotId) ? "tb3-01" : robotId.Trim().ToLowerInvariant();
        _ = SwitchTb3CameraStreamAsync(selectedRobotId, ++tb3SwitchRequestId);
    }

    private async Task SwitchTb3CameraStreamAsync(string robotId, int requestId)
    {
        if (tb3CameraClient == null)
        {
            return;
        }

        string previousRobotId = activeTb3RobotId;
        string streamUri = GetSelectedTb3StreamUri();
        LogCamera($"TB3 stream switching: {previousRobotId} -> {robotId}");
        tb3CameraClient.SetDiagnosticLabel("Tb3CameraVideoClient");
        tb3CameraClient.SetDiagnosticRobotId(robotId);
        tb3CameraClient.SetSourceKey(NormalizeCameraSourceKey(robotId));
        selectedTb3LastFrameTime = "--";
        PublishSelectedTb3FrameApplied(robotId, selectedTb3LastFrameTime);
        ClearMainFeedTexture();
        ClearTb3PreviewTexture();
        ApplyStreamTargets();
        tb3CameraClient.ClearTargetFrames();
        LogCamera("Clear previous TB3 camera frame");
        tb3ConnectedLogged = false;
        await tb3CameraClient.DisconnectAsync();

        if (requestId != tb3SwitchRequestId || !streamsRequested)
        {
            return;
        }

        activeTb3RobotId = robotId;
        if (string.IsNullOrEmpty(streamUri))
        {
            tb3Status = "No Stream";
            PublishStatus();
            SetMainCameraFeedPlaceholderVisible(true);
            if (!noStreamLogged)
            {
                LogCamera($"No stream for {robotId}");
                noStreamLogged = true;
            }

            return;
        }

        noStreamLogged = false;
        tb3CameraClient.SetStreamUri(streamUri);
        tb3Status = "Connecting";
        PublishStatus();
        LogCamera($"Connecting TB3 camera uri={streamUri}");
        await tb3CameraClient.ConnectAsync();
    }

    private string GetSelectedTb3StreamUri()
    {
        return selectedRobotId switch
        {
            "tb3-01" => tb3_01Uri,
            "tb3-02" => tb3_02Uri,
            _ => string.Empty
        };
    }

    private void ConnectFixedTb3PreviewClients()
    {
        if (tb3_01FixedClient != null)
        {
            tb3_01FixedClient.SetDiagnosticLabel("Tb3_01BottomPreviewClient");
            tb3_01FixedClient.SetDiagnosticRobotId(Tb3_01SourceKey);
            tb3_01FixedClient.SetSourceKey(Tb3_01SourceKey);
            tb3_01FixedClient.SetStreamUri(tb3_01Uri);
            tb3_01Status = "Connecting";
            tb3_01FixedClient.Connect();
            PublishSourceStatus("tb3-01", tb3_01Status);
        }

        if (tb3_02FixedClient != null && tb3_02FixedClient != tb3_01FixedClient)
        {
            tb3_02FixedClient.SetDiagnosticLabel("Tb3_02BottomPreviewClient");
            tb3_02FixedClient.SetDiagnosticRobotId(Tb3_02SourceKey);
            tb3_02FixedClient.SetSourceKey(Tb3_02SourceKey);
            tb3_02FixedClient.SetStreamUri(tb3_02Uri);
            tb3_02Status = "Connecting";
            tb3_02FixedClient.Connect();
            PublishSourceStatus("tb3-02", tb3_02Status);
        }

        RefreshBottomPreviewTextNow();
    }

    private void HandleGlobalStatus(string status)
    {
        globalStatus = status;
        PublishSourceStatus("global", status);
        if (selectedMainFeedId == "global")
        {
            SetMainCameraFeedPlaceholderVisible(!(status == "Connected" && HasReceivedFrame(globalLastFrameTime)));
        }

        if (status == "Connected" && !globalConnectedLogged)
        {
            LogCamera("Global CCTV connected");
            globalConnectedLogged = true;
        }

        PublishStatus();
        RefreshBottomPreviewTextNow();
    }

    private void HandleTb3Status(string status)
    {
        if (tb3Status == "No Stream" && status == "Disconnected")
        {
            return;
        }

        tb3Status = status;
        if ((status != "Disconnected" || activeTb3RobotId == selectedRobotId) &&
            !HasFixedPreviewSource(activeTb3RobotId))
        {
            PublishSourceStatus(activeTb3RobotId, status);
        }

        if ((selectedMainFeedId == "tb3-01" || selectedMainFeedId == "tb3-02") &&
            selectedMainFeedId == activeTb3RobotId)
        {
            SetMainCameraFeedPlaceholderVisible(!(status == "Connected" && HasReceivedFrame(selectedTb3LastFrameTime)));
        }

        if (status == "Connected" && !tb3ConnectedLogged)
        {
            LogCamera($"TB3 camera connected: {activeTb3RobotId}");
            tb3ConnectedLogged = true;
        }

        PublishStatus();
    }

    private void HandleTb3_01FixedStatus(string status)
    {
        tb3_01Status = status;
        PublishSourceStatus("tb3-01", status);
        RefreshBottomPreviewTextNow();
    }

    private void HandleTb3_02FixedStatus(string status)
    {
        tb3_02Status = status;
        PublishSourceStatus("tb3-02", status);
        RefreshBottomPreviewTextNow();
    }

    private void HandleGlobalFrameApplied(string timeText)
    {
        globalLastFrameTime = string.IsNullOrWhiteSpace(timeText) ? globalLastFrameTime : timeText;
        PublishSourceStatus(GlobalSourceKey, globalStatus);
        PublishFrameApplied("global", globalLastFrameTime);
        if (selectedMainFeedId == "global" && HasReceivedFrame(globalLastFrameTime))
        {
            SetMainCameraFeedPlaceholderVisible(false);
        }

        RefreshBottomPreviewTextNow();
    }

    private void HandleSelectedTb3FrameApplied(string timeText)
    {
        selectedTb3LastFrameTime = string.IsNullOrWhiteSpace(timeText) ? selectedTb3LastFrameTime : timeText;
        PublishSelectedTb3FrameApplied(activeTb3RobotId, selectedTb3LastFrameTime);
        if (selectedMainFeedId == activeTb3RobotId && HasReceivedFrame(selectedTb3LastFrameTime))
        {
            SetMainCameraFeedPlaceholderVisible(false);
        }
    }

    private void HandleTb3_01FrameApplied(string timeText)
    {
        tb3_01LastFrameTime = string.IsNullOrWhiteSpace(timeText) ? tb3_01LastFrameTime : timeText;
        PublishSourceStatus(Tb3_01SourceKey, tb3_01Status);
        PublishFrameApplied(Tb3_01SourceKey, tb3_01LastFrameTime);
        ValidateFixedDecodeTextureSeparation();
        RefreshBottomPreviewTextNow();
    }

    private void HandleTb3_02FrameApplied(string timeText)
    {
        tb3_02LastFrameTime = string.IsNullOrWhiteSpace(timeText) ? tb3_02LastFrameTime : timeText;
        PublishSourceStatus(Tb3_02SourceKey, tb3_02Status);
        PublishFrameApplied(Tb3_02SourceKey, tb3_02LastFrameTime);
        ValidateFixedDecodeTextureSeparation();
        RefreshBottomPreviewTextNow();
    }

    private void HandleGlobalLog(string message)
    {
        LogCamera(message);
    }

    private void HandleTb3Log(string message)
    {
        LogCamera(message);
    }

    private void PublishStatus()
    {
        ResolveUiManager();
        if (uiManager != null)
        {
            uiManager.ApplyCameraStreamStatus(globalStatus, tb3Status);
        }
    }

    private void PublishSourceStatus(string sourceId, string status)
    {
        ResolveUiManager();
        if (uiManager != null)
        {
            uiManager.ApplyCameraSourceStatus(sourceId, GetPreviewSourceState(sourceId, status));
        }
    }

    private bool HasFixedPreviewSource(string sourceId)
    {
        string source = NormalizeCameraSourceKey(sourceId);
        return source switch
        {
            Tb3_01SourceKey => tb3_01FixedClient != null,
            Tb3_02SourceKey => tb3_02FixedClient != null,
            _ => false
        };
    }

    private string GetPreviewSourceState(string sourceId, string status)
    {
        string source = NormalizeCameraSourceKey(sourceId);
        string lastFrameTime = source switch
        {
            GlobalSourceKey => globalLastFrameTime,
            Tb3_01SourceKey => tb3_01LastFrameTime,
            Tb3_02SourceKey => tb3_02LastFrameTime,
            _ => "--"
        };
        string normalized = string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim().ToUpperInvariant();
        return normalized switch
        {
            "CONNECTED" => HasReceivedFrame(lastFrameTime) ? "Connected" : "Video Waiting",
            "CONNECTING" or "INITIALIZING" => "Connecting",
            "WAITING" => "Video Waiting",
            "DISCONNECTED" or "NO STREAM" or "CLOSED" or "ERROR" or "FAILED" => "Disconnected",
            _ => "UNKNOWN"
        };
    }

    private void PublishFrameApplied(string sourceId, string lastFrameTime)
    {
        ResolveUiManager();
        if (uiManager != null)
        {
            uiManager.ApplyCameraFrameApplied(sourceId, lastFrameTime);
        }
    }

    private void PublishSelectedTb3FrameApplied(string robotId, string lastFrameTime)
    {
        ResolveUiManager();
        if (uiManager != null)
        {
            uiManager.ApplySelectedTb3CameraFrameApplied(robotId, lastFrameTime);
        }
    }

    private void LogCamera(string message)
    {
        ResolveUiManager();
        if (uiManager != null)
        {
            uiManager.AddExternalEventLog("CAM", message);
        }
        else
        {
            Debug.Log($"[CAM] {message}");
        }
    }

    public void RefreshBottomPreviewTextNow()
    {
        ResolveReferences();
        SetText(textBottomGlobalCctvStatus, $"상태 : {FormatPreviewStatus(globalStatus, globalLastFrameTime)}");
        SetText(textBottomGlobalCctvLastDetection, $"마지막 수신 : {FormatLastFrameTime(globalLastFrameTime)}");
        SetText(textBottomTb3_01StreamState, $"상태 : {FormatPreviewStatus(tb3_01Status, tb3_01LastFrameTime)}");
        SetText(textBottomTb3_01LastDetection, $"마지막 수신 : {FormatLastFrameTime(tb3_01LastFrameTime)}");
        SetText(textBottomTb3_02StreamState, $"상태 : {FormatPreviewStatus(tb3_02Status, tb3_02LastFrameTime)}");
        SetText(textBottomTb3_02LastDetection, $"마지막 수신 : {FormatLastFrameTime(tb3_02LastFrameTime)}");
    }

    private static void SetText(TMPro.TMP_Text text, string value)
    {
        if (text != null && text.text != value)
        {
            text.text = value;
        }
    }

    private static string FormatPreviewStatus(string status, string lastFrameTime)
    {
        string normalized = string.IsNullOrWhiteSpace(status) ? "--" : status.Trim().ToUpperInvariant();
        return normalized switch
        {
            "CONNECTING" or "WAITING" => "연결 중",
            "CONNECTED" => HasReceivedFrame(lastFrameTime) ? "연결됨" : "영상 대기",
            "DISCONNECTED" => "연결 끊김",
            "NO STREAM" => "연결 끊김",
            "ERROR" => "오류",
            _ => "--"
        };
    }

    private static bool HasReceivedFrame(string timeText)
    {
        return !string.IsNullOrWhiteSpace(timeText) && timeText.Trim() != "--";
    }

    private static string FormatLastFrameTime(string timeText)
    {
        return string.IsNullOrWhiteSpace(timeText) || timeText == "--" ? "--" : timeText.Trim();
    }

    private void ResolveReferences()
    {
        if (referencesResolved)
        {
            return;
        }

        ResolveUiManager();

        globalCctvClient = ResolveCameraClient(globalCctvClient, "GlobalCctvVideoClient");
        tb3CameraClient = ResolveCameraClient(tb3CameraClient, "Tb3VideoClient");
        tb3_01FixedClient = ResolveCameraClient(tb3_01FixedClient, "Tb3_01FixedVideoClient");
        tb3_02FixedClient = ResolveCameraClient(tb3_02FixedClient, "Tb3_02FixedVideoClient");
        ConfigureCanonicalSourceKeys();

        rawImageMainCameraFeedStream ??= FindRawImage("RawImage_MainCameraFeedStream");
        rawImageGlobalCctvStream ??= FindRawImage("RawImage_GlobalCctvStream");
        rawImageFactory2DGlobalCctv ??= FindRawImage("RawImage_Factory2DGlobalCctv");
        rawImageTb3CameraStream ??= FindRawImage("RawImage_Tb3CameraStream");
        rawImageBottomGlobalCctvStream ??= FindScopedRawImage("Panel_BottomPreview_GlobalCctv", "RawImage_BottomGlobalCctvStream") ??
                                           FindRawImage("RawImage_BottomGlobalCctvStream");
        if (rawImageBottomTb3_01CameraStream == null)
        {
            rawImageBottomTb3_01CameraStream = FindScopedRawImage(
                "Panel_Bottom_CameraPreview",
                "Panel_BottomPreview_Tb3_01_Camera",
                "RawImage_BottomTb3CameraStream");
        }

        if (rawImageBottomTb3_02CameraStream == null)
        {
            rawImageBottomTb3_02CameraStream = FindScopedRawImage(
                "Panel_Bottom_CameraPreview",
                "Panel_BottomPreview_Tb3_02_Camera",
                "RawImage_BottomTb3CameraStream");
        }

        textBottomGlobalCctvStatus ??= FindScopedText("Panel_BottomPreview_GlobalCctv", "Text_BottomGlobalCctvStatus");
        textBottomGlobalCctvLastDetection ??= FindScopedText("Panel_BottomPreview_GlobalCctv", "Text_BottomGlobalCctvLastDetection");
        textBottomTb3_01StreamState ??= FindScopedText("Panel_BottomPreview_Tb3_01_Camera", "Text_BottomTb3CameraStreamState");
        textBottomTb3_01LastDetection ??= FindScopedText("Panel_BottomPreview_Tb3_01_Camera", "Text_BottomTb3CameraLastDetection");
        textBottomTb3_02StreamState ??= FindScopedText("Panel_BottomPreview_Tb3_02_Camera", "Text_BottomTb3CameraStreamState");
        textBottomTb3_02LastDetection ??= FindScopedText("Panel_BottomPreview_Tb3_02_Camera", "Text_BottomTb3CameraLastDetection");
        imageMainCameraFeedPlaceholder ??= FindSceneGameObject("Image_MainCameraFeedPlaceholder");
        ApplyStreamTargets();
        referencesResolved = true;
    }

    private scr_CameraJpegWebSocketClient ResolveCameraClient(
        scr_CameraJpegWebSocketClient current,
        string expectedObjectName)
    {
        if (current != null && current.gameObject.scene.IsValid() && current.gameObject.name == expectedObjectName)
        {
            return current;
        }

        scr_CameraJpegWebSocketClient[] localClients = transform.root.GetComponentsInChildren<scr_CameraJpegWebSocketClient>(true);
        foreach (scr_CameraJpegWebSocketClient client in localClients)
        {
            if (client != null && client.gameObject.scene.IsValid() && client.gameObject.name == expectedObjectName)
            {
                return client;
            }
        }

        foreach (scr_CameraJpegWebSocketClient client in Resources.FindObjectsOfTypeAll<scr_CameraJpegWebSocketClient>())
        {
            if (client != null && client.gameObject.scene.IsValid() && client.gameObject.name == expectedObjectName)
            {
                return client;
            }
        }

        return current != null && current.gameObject.scene.IsValid() ? current : null;
    }

    private void ConfigureCanonicalSourceKeys()
    {
        globalCctvClient?.SetSourceKey(GlobalSourceKey);
        tb3CameraClient?.SetSourceKey(NormalizeCameraSourceKey(selectedRobotId));
        tb3_01FixedClient?.SetSourceKey(Tb3_01SourceKey);
        tb3_02FixedClient?.SetSourceKey(Tb3_02SourceKey);
    }

    private void ValidateFixedPreviewBindings()
    {
        bool missingReference = tb3_01FixedClient == null || tb3_02FixedClient == null ||
                                rawImageBottomTb3_01CameraStream == null || rawImageBottomTb3_02CameraStream == null;
        bool duplicateClient = tb3_01FixedClient != null && tb3_01FixedClient == tb3_02FixedClient;
        bool duplicateRawImage = rawImageBottomTb3_01CameraStream != null &&
                                 rawImageBottomTb3_01CameraStream == rawImageBottomTb3_02CameraStream;
        bool duplicateSourceKey = tb3_01FixedClient != null && tb3_02FixedClient != null &&
                                  !string.IsNullOrEmpty(tb3_01FixedClient.SourceKey) &&
                                  tb3_01FixedClient.SourceKey == tb3_02FixedClient.SourceKey;

        if ((missingReference || duplicateClient || duplicateRawImage || duplicateSourceKey) && !cameraBindingErrorWritten)
        {
            cameraBindingErrorWritten = true;
            Debug.LogError(
                "[CameraBinding] Invalid fixed camera binding. " +
                $"missing={missingReference} duplicateClient={duplicateClient} " +
                $"duplicateRawImage={duplicateRawImage} duplicateSourceKey={duplicateSourceKey}");
        }
    }

    private void ValidateFixedDecodeTextureSeparation()
    {
        if (fixedTextureErrorWritten || tb3_01FixedClient == null || tb3_02FixedClient == null)
        {
            return;
        }

        Texture2D tb3_01Texture = tb3_01FixedClient.CurrentFrameTexture;
        Texture2D tb3_02Texture = tb3_02FixedClient.CurrentFrameTexture;
        if (tb3_01Texture != null && tb3_02Texture != null && tb3_01Texture == tb3_02Texture)
        {
            fixedTextureErrorWritten = true;
            Debug.LogError("[CameraBinding] TB3-01 and TB3-02 fixed clients share the same decode Texture2D.");
        }
    }

    private void LogCameraBindingsOnce()
    {
        if (cameraBindingReportWritten)
        {
            return;
        }

        cameraBindingReportWritten = true;
        Debug.Log(
            "[CameraBinding]\n" +
            $"GlobalClient={DescribeClient(globalCctvClient, globalCctvUri)}\n" +
            $"GlobalRawImage={DescribeComponent(rawImageBottomGlobalCctvStream)}\n" +
            $"TB3_01Client={DescribeClient(tb3_01FixedClient, tb3_01Uri)}\n" +
            $"TB3_01RawImage={DescribeComponent(rawImageBottomTb3_01CameraStream)}\n" +
            $"TB3_02Client={DescribeClient(tb3_02FixedClient, tb3_02Uri)}\n" +
            $"TB3_02RawImage={DescribeComponent(rawImageBottomTb3_02CameraStream)}");
        ValidateFixedPreviewBindings();
    }

    private static string DescribeClient(scr_CameraJpegWebSocketClient client, string configuredUri)
    {
        return client == null
            ? $"<null> source=<none> uri={configuredUri}"
            : $"{GetHierarchyPath(client.transform)}#{client.GetInstanceID()} source={client.SourceKey} uri={configuredUri}";
    }

    private static string DescribeComponent(Component component)
    {
        return component == null
            ? "<null>"
            : $"{GetHierarchyPath(component.transform)}#{component.GetInstanceID()}";
    }

    private static string GetHierarchyPath(Transform item)
    {
        if (item == null)
        {
            return "<null>";
        }

        string path = item.name;
        while (item.parent != null)
        {
            item = item.parent;
            path = $"{item.name}/{path}";
        }

        return path;
    }

    private void ClearMainFeedTexture()
    {
        if (rawImageMainCameraFeedStream != null)
        {
            rawImageMainCameraFeedStream.texture = null;
            return;
        }

        if (!mainFeedRawImageWarningShown)
        {
            mainFeedRawImageWarningShown = true;
            Debug.LogWarning("[CAM] RawImage not found: RawImage_MainCameraFeedStream");
        }
    }

    private void ClearTb3PreviewTexture()
    {
        if (rawImageTb3CameraStream != null)
        {
            rawImageTb3CameraStream.texture = null;
        }
        else if (!tb3PreviewRawImageWarningShown)
        {
            tb3PreviewRawImageWarningShown = true;
            Debug.LogWarning("[CAM] RawImage not found: RawImage_Tb3CameraStream");
        }

        // Bottom TB3-01/TB3-02 previews are fixed independent streams and are not cleared
        // by selected main feed changes.
    }

    private void SetMainCameraFeedPlaceholderVisible(bool visible)
    {
        if (imageMainCameraFeedPlaceholder != null)
        {
            if (imageMainCameraFeedPlaceholder.activeSelf != visible)
            {
                imageMainCameraFeedPlaceholder.SetActive(visible);
            }
            return;
        }

        if (visible && !mainCameraFeedPlaceholderWarningShown)
        {
            mainCameraFeedPlaceholderWarningShown = true;
            Debug.LogWarning("[CAM] Main feed placeholder not found: Image_MainCameraFeedPlaceholder");
        }
    }

    private void ApplyStreamTargets()
    {
        if (globalCctvClient != null)
        {
            bool mainUsesGlobal = isCameraViewActive && selectedMainFeedId == "global";
            globalCctvClient.SetTargets(BuildRawImageTargets(
                mainUsesGlobal ? rawImageMainCameraFeedStream : null,
                isCameraViewActive ? rawImageGlobalCctvStream : null,
                rawImageFactory2DGlobalCctv,
                rawImageBottomGlobalCctvStream));
        }

        if (tb3CameraClient != null)
        {
            bool mainUsesTb3 = isCameraViewActive &&
                (selectedMainFeedId == "tb3-01" || selectedMainFeedId == "tb3-02" || selectedMainFeedId == "tb3-03");
            tb3CameraClient.SetTargets(BuildRawImageTargets(
                mainUsesTb3 ? rawImageMainCameraFeedStream : null,
                isCameraViewActive ? rawImageTb3CameraStream : null));
        }

        if (tb3_01FixedClient != null)
        {
            tb3_01FixedClient.SetTargets(BuildRawImageTargets(rawImageBottomTb3_01CameraStream));
        }

        bool fixedClientsAreDistinct = tb3_01FixedClient == null || tb3_02FixedClient == null || tb3_01FixedClient != tb3_02FixedClient;
        bool fixedRawImagesAreDistinct = rawImageBottomTb3_01CameraStream == null ||
                                         rawImageBottomTb3_02CameraStream == null ||
                                         rawImageBottomTb3_01CameraStream != rawImageBottomTb3_02CameraStream;
        if (tb3_02FixedClient != null && fixedClientsAreDistinct && fixedRawImagesAreDistinct)
        {
            tb3_02FixedClient.SetTargets(BuildRawImageTargets(rawImageBottomTb3_02CameraStream));
        }
        else if (tb3_02FixedClient != null)
        {
            tb3_02FixedClient.SetTargets(Array.Empty<RawImage>());
        }

        ValidateFixedPreviewBindings();
    }

    private static RawImage[] BuildRawImageTargets(params RawImage[] candidates)
    {
        int count = 0;
        foreach (RawImage candidate in candidates)
        {
            if (candidate != null)
            {
                count++;
            }
        }

        RawImage[] targets = new RawImage[count];
        int index = 0;
        foreach (RawImage candidate in candidates)
        {
            if (candidate != null)
            {
                targets[index++] = candidate;
            }
        }

        return targets;
    }

    private static string NormalizeMainFeedId(string feedId)
    {
        string normalized = string.IsNullOrWhiteSpace(feedId) ? "global" : feedId.Trim().ToLowerInvariant();
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
            "tb3-3" => "tb3-03",
            "tb3_03" => "tb3-03",
            "tb3-03" => "tb3-03",
            _ => "global"
        };
    }

    private static string NormalizeCameraSourceKey(string sourceId)
    {
        string normalized = string.IsNullOrWhiteSpace(sourceId)
            ? string.Empty
            : sourceId.Trim().ToLowerInvariant().Replace('_', '-');
        return normalized switch
        {
            "global" or "global-cctv" => GlobalSourceKey,
            "1" or "01" or "tb3-1" or "tb3-01" => Tb3_01SourceKey,
            "2" or "02" or "tb3-2" or "tb3-02" => Tb3_02SourceKey,
            _ => string.Empty
        };
    }

    private static bool IsCameraStatusConnected(string status)
    {
        return !string.IsNullOrWhiteSpace(status) && status.Trim().Equals("Connected", StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatMainFeedLabel(string feedId)
    {
        string normalized = NormalizeMainFeedId(feedId);
        return normalized == "global" ? "GLOBAL CCTV" : normalized.ToUpperInvariant();
    }

    private void BindClientEvents()
    {
        if (globalCctvClient != null)
        {
            globalCctvClient.onStatusChanged.RemoveListener(HandleGlobalStatus);
            globalCctvClient.onStatusChanged.AddListener(HandleGlobalStatus);
            globalCctvClient.onStreamLog.RemoveListener(HandleGlobalLog);
            globalCctvClient.onStreamLog.AddListener(HandleGlobalLog);
            globalCctvClient.onFrameApplied.RemoveListener(HandleGlobalFrameApplied);
            globalCctvClient.onFrameApplied.AddListener(HandleGlobalFrameApplied);
        }

        if (tb3CameraClient != null)
        {
            tb3CameraClient.onStatusChanged.RemoveListener(HandleTb3Status);
            tb3CameraClient.onStatusChanged.AddListener(HandleTb3Status);
            tb3CameraClient.onStreamLog.RemoveListener(HandleTb3Log);
            tb3CameraClient.onStreamLog.AddListener(HandleTb3Log);
            tb3CameraClient.onFrameApplied.RemoveListener(HandleSelectedTb3FrameApplied);
            tb3CameraClient.onFrameApplied.AddListener(HandleSelectedTb3FrameApplied);
        }

        if (tb3_01FixedClient != null)
        {
            tb3_01FixedClient.onStatusChanged.RemoveListener(HandleTb3_01FixedStatus);
            tb3_01FixedClient.onStatusChanged.AddListener(HandleTb3_01FixedStatus);
            tb3_01FixedClient.onStreamLog.RemoveListener(HandleTb3Log);
            tb3_01FixedClient.onStreamLog.AddListener(HandleTb3Log);
            tb3_01FixedClient.onFrameApplied.RemoveListener(HandleTb3_01FrameApplied);
            tb3_01FixedClient.onFrameApplied.AddListener(HandleTb3_01FrameApplied);
        }

        if (tb3_02FixedClient != null && tb3_02FixedClient != tb3_01FixedClient)
        {
            tb3_02FixedClient.onStatusChanged.RemoveListener(HandleTb3_02FixedStatus);
            tb3_02FixedClient.onStatusChanged.AddListener(HandleTb3_02FixedStatus);
            tb3_02FixedClient.onStreamLog.RemoveListener(HandleTb3Log);
            tb3_02FixedClient.onStreamLog.AddListener(HandleTb3Log);
            tb3_02FixedClient.onFrameApplied.RemoveListener(HandleTb3_02FrameApplied);
            tb3_02FixedClient.onFrameApplied.AddListener(HandleTb3_02FrameApplied);
        }
    }

    private void ResolveUiManager()
    {
        if (uiManager != null)
        {
            return;
        }

#if UNITY_2023_1_OR_NEWER
        uiManager = FindFirstObjectByType<scr_ControlTowerUIManager>();
#else
        uiManager = FindObjectOfType<scr_ControlTowerUIManager>();
#endif
    }

    private static RawImage FindRawImage(string objectName)
    {
        foreach (GameObject item in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (item.name == objectName && item.scene.IsValid())
            {
                return item.GetComponent<RawImage>();
            }
        }

        return null;
    }

    private static RawImage FindScopedRawImage(string parentName, string rawImageName)
    {
        GameObject parent = FindSceneGameObject(parentName);
        if (parent == null)
        {
            return null;
        }

        RawImage[] rawImages = parent.GetComponentsInChildren<RawImage>(true);
        foreach (RawImage rawImage in rawImages)
        {
            if (rawImage != null && rawImage.name == rawImageName)
            {
                return rawImage;
            }
        }

        return null;
    }

    private static RawImage FindScopedRawImage(string rootName, string parentName, string rawImageName)
    {
        GameObject root = FindSceneGameObject(rootName);
        Transform parent = root != null ? FindDescendantByExactName(root.transform, parentName) : null;
        if (parent == null)
        {
            return null;
        }

        RawImage[] rawImages = parent.GetComponentsInChildren<RawImage>(true);
        foreach (RawImage rawImage in rawImages)
        {
            if (rawImage != null && rawImage.name == rawImageName)
            {
                return rawImage;
            }
        }

        return null;
    }

    private static Transform FindDescendantByExactName(Transform root, string objectName)
    {
        if (root == null)
        {
            return null;
        }

        if (root.name == objectName)
        {
            return root;
        }

        foreach (Transform child in root)
        {
            Transform found = FindDescendantByExactName(child, objectName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static TMPro.TMP_Text FindScopedText(string parentName, string textName)
    {
        GameObject parent = FindSceneGameObject(parentName);
        if (parent == null)
        {
            return null;
        }

        TMPro.TMP_Text[] texts = parent.GetComponentsInChildren<TMPro.TMP_Text>(true);
        foreach (TMPro.TMP_Text text in texts)
        {
            if (text != null && text.name == textName)
            {
                return text;
            }
        }

        return null;
    }

    private static GameObject FindSceneGameObject(string objectName)
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

    private void OnDisable()
    {
        DisconnectCameraStreams();
    }

    private void OnDestroy()
    {
        DisconnectCameraStreams();
    }
}

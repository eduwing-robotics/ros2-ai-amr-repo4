using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net.WebSockets;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
public class WsEventEnvelope
{
    public string type;
    public string event_type;
    public string event_name;
    public string @event;
}

[Serializable]
public class RobotStatusWsEnvelope
{
    public string type;
    public string event_type;
    public RobotStatusWsData data;
}

[Serializable]
public class RobotStatusWsData
{
    public int robot_id;
    public float x;
    public float y;
    public float yaw;
    public string status;
    public float battery;
    public float linear_vel;
    public float angular_vel;
    public string pause_reason;
    public int current_target_wp;
    public string map_id;
    public string localization_state;
    public string amcl_state;
    public bool initial_pose_set;
    public string localization_quality;
    public string scan_match_state;
    public string nav2_state;
    public string planner_state;
    public string controller_state;
    public string goal_result;
    public int replan_count;
    public int current_wp_index;
    public int total_waypoints;
    public string route_state;
    public string route_id;
    public string route_name;
    public ControlTowerWaypointData[] waypoints;
    public string obstacle_state;
    public string obstacle_type;
    public float obstacle_distance;
    public float obstacle_x;
    public float obstacle_y;
    public string recovery_state;
    public string recovery_behavior;
    public int recovery_retry_count;
    public string detected_at;
    public string message;
    public string updated_at;
}

[Serializable]
public class CameraAiStatusWsEnvelope
{
    public string type;
    public string event_type;
    public CameraAiStatusWsData data;
}

[Serializable]
public class CameraAiStatusWsData
{
    public string event_type;
    public string updated_at;
    public CameraAiStreamWsData[] streams;
    public CameraAiModelWsData ai;
}

[Serializable]
public class CameraAiStreamWsData
{
    public string camera_id;
    public string source_type;
    public int robot_id;
    public string channel;
    public bool connected;
    public string stream_status;
    public bool frame_received;
    public float fps;
    public float stream_latency_ms;
    public string resolution;
    public string last_frame_at;
    public string error_message;
    [NonSerialized] public bool has_connected;
    [NonSerialized] public bool has_frame_received;
    [NonSerialized] public bool has_fps;
    [NonSerialized] public bool has_stream_latency_ms;
}

[Serializable]
public class CameraAiModelWsData
{
    public string model_status;
    public string model_name;
    public string model_version;
    public string inference_status;
    public float inference_fps;
    public float inference_latency_ms;
    public bool detection_enabled;
    public string last_inference_at;
    public string last_detection_at;
    public string error_message;
    [NonSerialized] public bool has_inference_fps;
    [NonSerialized] public bool has_inference_latency_ms;
    [NonSerialized] public bool has_detection_enabled;
}

[Serializable]
public class NewAlertWsEnvelope
{
    public string type;
    public NewAlertWsData data;
}

[Serializable]
public class NewAlertWsData
{
    public int alert_id;
    public int log_id;
    public string detected_at;
    public string timestamp;
    public string incident_type;
    public int robot_id;
    public string camera_id;
    public string detected_by;
    public string employee_id;
    public float location_x;
    public float location_y;
    public string photo_url;
    public float confidence;
    public string status;
    public string cleared_at;
    public NewAlertAiDetails ai_details;
    public string message;
}

[Serializable]
public class NewAlertAiDetails
{
    public float confidence;
}

[Serializable]
public class EmployeeAttendanceWsEnvelope
{
    public string type;
    public EmployeeAttendanceWsData data;
}

[Serializable]
public class EmployeeAttendanceWsData
{
    public string employee_id;
    public string name;
    public string action_type;
    public string timestamp;
}

[Serializable]
public class VisitorAttendanceWsEnvelope
{
    public string type;
    public VisitorAttendanceWsData data;
}

[Serializable]
public class VisitorAttendanceWsData
{
    public string visitor_id;
    public string name;
    public string action_type;
    public string timestamp;
}

[Serializable]
public class RobotStateWsMessage
{
    public string event_type;
    public int robot_id;
    public float x;
    public float y;
    public float yaw;
    public float theta;
    public string status;
    public float battery;
    public float linear_vel;
    public float angular_vel;
    public string pause_reason;
}

[Serializable]
public class AiDetailsWsMessage
{
    public float confidence;
    public int[] bbox;
    public string label;
    public string pose;
}

[Serializable]
public class ViolationAlertWsMessage
{
    public string event_type;
    public int violation_id;
    public string violation_type;
    public string employee_id;
    public string detected_by;
    public int robot_id;
    public string robot_location;
    public string photo_url;
    public AiDetailsWsMessage ai_details;
    public string timestamp;
}

[Serializable]
public class EmergencyAlertWsMessage
{
    public string event_type;
    public int emergency_id;
    public string emergency_type;
    public string detected_by;
    public int robot_id;
    public string robot_location;
    public string photo_url;
    public AiDetailsWsMessage ai_details;
    public string timestamp;
}

[Serializable]
public class PatrolTimelineEventWsMessage
{
    public string event_type;
    public int timeline_id;
    public int log_id;
    public int robot_id;
    public string state;
    public string pause_reason;
    public string changed_at;
}

[Serializable]
public class PatrolLogUpdateWsMessage
{
    public string event_type;
    public int log_id;
    public int robot_id;
    public string start_time;
    public string end_time;
    public string status;
}

[Serializable]
public class SystemStatusWsMessage
{
    public string event_type;
    public string server_status;
    public string websocket_status;
    public string ros2_status;
    public string ai_model_status;
}

[Serializable]
public class CommandAckWsMessage
{
    public string event_type;
    public int robot_id;
    public string command;
    public string result_status;
    public string response_message;
    public string requested_by;
}

[Serializable]
public class AlertAckResultWsMessage
{
    public string event_type;
    public string alert_type;
    public int alert_id;
    public string action;
    public string result_status;
    public string response_message;
}

public class scr_ControlTowerWebSocketClient : MonoBehaviour
{
    private const string DefaultControlTowerWebSocketUri = "ws://127.0.0.1:8000/ws/control-tower";
    private const string LegacyControlTowerWebSocketUri = "ws://127.0.0.1:8000/ws/control-tower";

    [SerializeField] private scr_ControlTowerUIManager uiManager;
    [SerializeField] private string webSocketUri = DefaultControlTowerWebSocketUri;
    [SerializeField] private string serverBaseUrl = "http://127.0.0.1:8000";
    [SerializeField] private bool connectOnStart = true;
    [SerializeField] private bool logRawJsonToConsole;

    private readonly ConcurrentQueue<Action> mainThreadActions = new();
    private ClientWebSocket webSocket;
    private CancellationTokenSource cancellation;
    private Task receiveTask;
    private bool isClosing;

    public bool IsConnected => webSocket != null && webSocket.State == WebSocketState.Open;

    private async void Start()
    {
        ResolveUiManager();

        if (connectOnStart)
        {
            await ConnectAsync();
        }
    }

    private void Update()
    {
        while (mainThreadActions.TryDequeue(out Action action))
        {
            action?.Invoke();
        }
    }

    public async void SendRobotCommand(int robotId, string command, string requestedBy)
    {
        string safeCommand = string.IsNullOrWhiteSpace(command) ? "UNKNOWN_COMMAND" : command.Trim().ToUpperInvariant();
        string safeRequestedBy = string.IsNullOrWhiteSpace(requestedBy) ? "control_tower" : requestedBy.Trim();
        string json =
            "{\n" +
            "  \"event_type\": \"robot_command\",\n" +
            $"  \"robot_id\": {robotId},\n" +
            $"  \"command\": \"{EscapeJson(safeCommand)}\",\n" +
            $"  \"requested_by\": \"{EscapeJson(safeRequestedBy)}\",\n" +
            $"  \"requested_at\": \"{DateTime.Now:yyyy-MM-ddTHH:mm:sszzz}\"\n" +
            "}";

        await SendJsonAsync(json, $"robot_command/{safeCommand}");
    }

    public async void SendAlertAck(string alertType, int alertId, string action, string ackBy, string memo)
    {
        string safeAlertType = string.IsNullOrWhiteSpace(alertType) ? "UNKNOWN" : alertType.Trim().ToUpperInvariant();
        string safeAction = string.IsNullOrWhiteSpace(action) ? "ACK" : action.Trim().ToUpperInvariant();
        string safeAckBy = string.IsNullOrWhiteSpace(ackBy) ? "control_tower" : ackBy.Trim();
        string safeMemo = string.IsNullOrWhiteSpace(memo) ? string.Empty : memo.Trim();
        string json =
            "{\n" +
            "  \"event_type\": \"alert_ack\",\n" +
            $"  \"alert_type\": \"{EscapeJson(safeAlertType)}\",\n" +
            $"  \"alert_id\": {alertId},\n" +
            $"  \"action\": \"{EscapeJson(safeAction)}\",\n" +
            $"  \"ack_by\": \"{EscapeJson(safeAckBy)}\",\n" +
            $"  \"ack_at\": \"{DateTime.Now:yyyy-MM-ddTHH:mm:sszzz}\",\n" +
            $"  \"memo\": \"{EscapeJson(safeMemo)}\"\n" +
            "}";

        await SendJsonAsync(json, $"alert_ack/{safeAction}");
    }

    public async Task<bool> SendRobotCommandV2Async(int robotId, string operatorId, string controlMode, string command, float linearX, float angularZ, int durationMs, string targetType, string targetId)
    {
        string safeOperatorId = string.IsNullOrWhiteSpace(operatorId) ? "OPERATOR_01" : operatorId.Trim();
        string safeControlMode = string.IsNullOrWhiteSpace(controlMode) ? "AUTO" : controlMode.Trim().ToUpperInvariant();
        string safeCommand = string.IsNullOrWhiteSpace(command) ? "UNKNOWN_COMMAND" : command.Trim().ToUpperInvariant();
        string safeTargetType = string.IsNullOrWhiteSpace(targetType) ? "NONE" : targetType.Trim().ToUpperInvariant();
        string safeTargetId = targetId ?? string.Empty;
        bool dbLog = ShouldWriteRobotCommandLog(safeCommand);
        string payload = dbLog
            ? "{\n    \"db_log\": true\n  }"
            : "{\n" +
              $"    \"linear_x\": {linearX.ToString("0.###", CultureInfo.InvariantCulture)},\n" +
              $"    \"angular_z\": {angularZ.ToString("0.###", CultureInfo.InvariantCulture)},\n" +
              $"    \"duration_ms\": {Mathf.Max(0, durationMs)},\n" +
              $"    \"target_type\": \"{EscapeJson(safeTargetType)}\",\n" +
              $"    \"target_id\": \"{EscapeJson(safeTargetId)}\",\n" +
              "    \"db_log\": false\n" +
              "  }";
        string json =
            "{\n" +
            "  \"type\": \"robot_command\",\n" +
            $"  \"robot_id\": {robotId},\n" +
            $"  \"operator_id\": \"{EscapeJson(safeOperatorId)}\",\n" +
            $"  \"control_mode\": \"{EscapeJson(safeControlMode)}\",\n" +
            $"  \"command\": \"{EscapeJson(safeCommand)}\",\n" +
            $"  \"payload\": {payload}\n" +
            "}";

        return await SendJsonAsync(json, $"robot_command_v2/{safeCommand}");
    }

    public static bool ShouldWriteRobotCommandLog(string command)
    {
        switch ((command ?? string.Empty).Trim().ToUpperInvariant())
        {
            case "START_PATROL":
            case "PAUSE_MISSION":
            case "RESUME_MISSION":
            case "RETURN_TO_CHARGER":
            case "EMERGENCY_STOP":
            case "ENTER_MANUAL_MODE":
            case "EXIT_MANUAL_MODE":
                return true;
            default:
                return false;
        }
    }

    private async Task<bool> SendJsonAsync(string json, string label)
    {
        if (!IsConnected)
        {
            EnqueueUiLog("API", $"Not sent {label}: WebSocket disconnected");
            Debug.LogWarning($"[ControlTowerWS] Not sent {label}. WebSocket is not connected.");
            return false;
        }

        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellation.Token);
            Debug.Log($"[ControlTowerWS] Sent {label}");
            return true;
        }
        catch (Exception ex)
        {
            string message = $"Send failed {label}: {ex.Message}";
            Debug.LogWarning($"[ControlTowerWS] {message}");
            EnqueueUiLog("API", message);
            return false;
        }
    }

    private string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    public async Task ConnectAsync()
    {
        if (webSocket != null &&
            (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.Connecting))
        {
            return;
        }

        isClosing = false;
        cancellation = new CancellationTokenSource();
        webSocket = new ClientWebSocket();

        try
        {
            Uri uri = new Uri(GetEffectiveWebSocketUri());
            Debug.Log($"[ControlTowerWS] Connecting to {uri}");
            await webSocket.ConnectAsync(uri, cancellation.Token);

            EnqueueConnectionState(true, "Connected");
            Debug.Log("[ControlTowerWS] Connected to control tower server");

            receiveTask = ReceiveLoopAsync(cancellation.Token);
        }
        catch (Exception ex)
        {
            string message = $"Connection failed: {ex.Message}";
            Debug.LogWarning($"[ControlTowerWS] {message}");
            EnqueueConnectionState(false, message);
            CleanupSocket();
        }
    }

    public async Task DisconnectAsync()
    {
        isClosing = true;

        try
        {
            cancellation?.Cancel();

            if (webSocket != null &&
                (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived))
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Unity client closing", CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ControlTowerWS] Disconnect error: {ex.Message}");
        }
        finally
        {
            CleanupSocket();
            EnqueueConnectionState(false, "Disconnected");
            Debug.Log("[ControlTowerWS] Disconnected");
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        byte[] buffer = new byte[4096];
        StringBuilder messageBuilder = new StringBuilder();

        try
        {
            while (!token.IsCancellationRequested && webSocket != null && webSocket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        isClosing = true;
                        EnqueueConnectionState(false, "Disconnected");
                        Debug.Log("[ControlTowerWS] Server closed the connection.");
                        return;
                    }

                    string chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    messageBuilder.Append(chunk);
                }
                while (!result.EndOfMessage);

                string json = messageBuilder.ToString();
                messageBuilder.Clear();
                HandleReceivedJson(json);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            string message = $"Receive failed: {ex.Message}";
            Debug.LogWarning($"[ControlTowerWS] {message}");
            EnqueueConnectionState(false, message);
        }
        finally
        {
            if (!isClosing)
            {
                EnqueueConnectionState(false, "Disconnected");
            }
        }
    }

    private void HandleReceivedJson(string json)
    {
        string eventType = "robot_state_update";
        try
        {
            WsEventEnvelope envelope = JsonUtility.FromJson<WsEventEnvelope>(json);
            if (envelope != null)
            {
                eventType = FirstNonEmpty(envelope.type, envelope.event_type, envelope.event_name, envelope.@event, "robot_state_update");
            }
        }
        catch (Exception ex)
        {
            EnqueueUiLog("WS", $"JSON envelope parse failed: {ex.Message}");
            return;
        }

        switch (eventType.Trim().ToLowerInvariant())
        {
            case "camera_ai_status":
                HandleCameraAiStatusJson(json);
                break;

            case "robot_status":
                HandleRobotStatusEnvelopeJson(json);
                break;

            case "new_alert":
                HandleNewAlertEnvelopeJson(json);
                break;

            case "employee_attendance":
                HandleEmployeeAttendanceEnvelopeJson(json);
                break;

            case "visitor_attendance":
                HandleVisitorAttendanceEnvelopeJson(json);
                break;

            case "robot_state_update":
            case "robot_state":
                HandleRobotStateJson(json);
                break;

            case "violation_alert":
                HandleViolationAlertJson(json);
                break;

            case "emergency_alert":
                HandleEmergencyAlertJson(json);
                break;

            case "patrol_timeline_event":
                HandlePatrolTimelineEventJson(json);
                break;

            case "patrol_log_update":
                HandlePatrolLogUpdateJson(json);
                break;

            case "system_status":
                HandleSystemStatusJson(json);
                break;

            case "command_ack":
                HandleCommandAckJson(json);
                break;

            case "alert_ack_result":
                HandleAlertAckResultJson(json);
                break;

            default:
                EnqueueUiLog("WS", $"Unhandled event_type: {eventType}");
                break;
        }
    }

    private void HandleEmployeeAttendanceEnvelopeJson(string json)
    {
        EmployeeAttendanceWsEnvelope envelope = ParseJsonOrLog<EmployeeAttendanceWsEnvelope>(json, "EMPLOYEE_ATTENDANCE");
        if (envelope?.data == null)
        {
            EnqueueUiLog("WS", "EMPLOYEE_ATTENDANCE parse failed: missing data");
            return;
        }

        EmployeeAttendanceWsData data = envelope.data;
        string employeeId = data.employee_id ?? "-";
        string name = data.name ?? "-";
        string action = data.action_type ?? "unknown";
        Debug.Log($"[WS] EMPLOYEE_ATTENDANCE action={action}");

        EnqueueUiAction(() => uiManager.ApplyEmployeeAttendanceFromServer(
            employeeId,
            name,
            action,
            data.timestamp));
    }

    private void HandleVisitorAttendanceEnvelopeJson(string json)
    {
        VisitorAttendanceWsEnvelope envelope = ParseJsonOrLog<VisitorAttendanceWsEnvelope>(json, "VISITOR_ATTENDANCE");
        if (envelope?.data == null)
        {
            EnqueueUiLog("WS", "VISITOR_ATTENDANCE parse failed: missing data");
            return;
        }

        VisitorAttendanceWsData data = envelope.data;
        string visitorId = data.visitor_id ?? "-";
        string name = data.name ?? "-";
        string action = data.action_type ?? "unknown";
        Debug.Log($"[WS] VISITOR_ATTENDANCE action={action}");

        EnqueueUiAction(() => uiManager.ApplyVisitorAttendanceFromServer(
            visitorId,
            name,
            action,
            data.timestamp));
    }

    private void HandleRobotStatusEnvelopeJson(string json)
    {
        RobotStatusWsEnvelope envelope = ParseJsonOrLog<RobotStatusWsEnvelope>(json, "ROBOT_STATUS");
        RobotStatusWsData data = envelope?.data ?? ParseJsonOrLog<RobotStatusWsData>(json, "ROBOT_STATUS_DIRECT");
        if (data == null)
        {
            EnqueueUiLog("WS", "ROBOT_STATUS parse failed: missing data");
            return;
        }

        int robotNumber = ResolveRobotNumber(json, data.robot_id);
        string robotLabel = ConvertRobotId(robotNumber);
        string status = string.IsNullOrWhiteSpace(data.status) ? "UNKNOWN" : data.status.Trim().ToUpperInvariant();
        string pauseReason = data.pause_reason ?? string.Empty;

        if (logRawJsonToConsole)
        {
            Debug.Log($"[WS] ROBOT_STATUS {robotLabel} x={data.x:0.###} y={data.y:0.###} yawRad={data.yaw:0.###} status={status} battery={data.battery:0.#}");
        }

        EnqueueUiAction(() => uiManager.ApplyRobotStateFromServer(
            robotNumber,
            data.x,
            data.y,
            status,
            data.battery,
            data.linear_vel,
            data.angular_vel,
            pauseReason,
            true,
            data.yaw,
            data.current_target_wp));

        if (HasAnyMapNavStatusField(json, data))
        {
            ControlTowerMapNavStatusData mapNavStatus = BuildMapNavStatus(robotNumber, data, json);
            EnqueueUiAction(() => uiManager.ApplyMapNavStatusFromServer(mapNavStatus));
        }

        if (data.waypoints != null)
        {
            ControlTowerWaypointRouteData route = BuildWaypointRoute(robotNumber, data, json);
            EnqueueUiAction(() => uiManager.ApplyWaypointRouteFromServer(route));
        }

        if (HasAnyObstacleRecoveryField(json, data))
        {
            ControlTowerObstacleRecoveryData recovery = BuildObstacleRecovery(robotNumber, data, json);
            EnqueueUiAction(() => uiManager.ApplyObstacleRecoveryFromServer(recovery));
        }

        if (!string.IsNullOrWhiteSpace(pauseReason))
        {
            EnqueueUiLog("WS", $"{robotLabel} pause_reason={pauseReason}");
        }
    }

    private void HandleCameraAiStatusJson(string json)
    {
        CameraAiStatusWsEnvelope envelope = ParseJsonOrLog<CameraAiStatusWsEnvelope>(json, "CAMERA_AI_STATUS");
        CameraAiStatusWsData data = envelope?.data ?? ParseJsonOrLog<CameraAiStatusWsData>(json, "CAMERA_AI_STATUS_DIRECT");
        if (data == null)
        {
            EnqueueUiLog("WS", "CAMERA_AI_STATUS parse failed: missing payload");
            return;
        }

        MarkCameraAiOptionalFields(data, json);
        int streamCount = data.streams != null ? data.streams.Length : 0;
        Debug.Log($"[WS] CAMERA_AI_STATUS streams={streamCount} ai={data.ai?.model_status ?? "--"} updated_at={data.updated_at ?? "--"}");
        EnqueueUiAction(() => uiManager.ApplyCameraAiStatusFromServer(data));
    }

    private void HandleNewAlertEnvelopeJson(string json)
    {
        NewAlertWsEnvelope envelope = ParseJsonOrLog<NewAlertWsEnvelope>(json, "NEW_ALERT");
        if (envelope?.data == null)
        {
            EnqueueUiLog("WS", "NEW_ALERT parse failed: missing data");
            return;
        }

        NewAlertWsData data = envelope.data;
        string incidentType = NormalizeIncidentType(data.incident_type);
        int alertKey = ResolveAlertKey(json, data.alert_id, data.log_id);
        int robotNumber = ResolveRobotNumber(json, data.robot_id);
        string detectedAt = FirstNonEmpty(data.detected_at, data.timestamp);
        string robotLabel = robotNumber > 0 ? ConvertRobotId(robotNumber) : "GLOBAL_CAM";
        string detectedBy = string.IsNullOrWhiteSpace(data.detected_by) ? "UNKNOWN" : data.detected_by.Trim().ToUpperInvariant();
        bool hasLocation = JsonFieldHasNonNullValue(json, "location_x") && JsonFieldHasNonNullValue(json, "location_y");
        string location = hasLocation ? $"X {data.location_x:0.00}, Y {data.location_y:0.00}" : string.Empty;
        string photoUrl = BuildAbsolutePhotoUrl(data.photo_url);
        float confidenceValue = data.ai_details != null ? data.ai_details.confidence : data.confidence;
        string confidence = JsonFieldHasNonNullValue(json, "confidence") ? FormatConfidence01(confidenceValue) : string.Empty;
        string message = string.IsNullOrWhiteSpace(data.message) ? "--" : data.message.Trim();

        Debug.Log($"[WS] NEW_ALERT type={incidentType} robot={robotLabel} location=({data.location_x:0.###},{data.location_y:0.###}) confidence={confidence}");

        EnqueueUiAction(() =>
        {
            if (IsEmergencyIncident(incidentType))
            {
                uiManager.ApplyEmergencyAlertFromServer(
                    alertKey,
                    incidentType,
                    detectedBy,
                    robotNumber,
                    location,
                    photoUrl,
                    confidence,
                    "-",
                    message,
                    detectedAt,
                    data.status,
                    data.camera_id);
            }
            else
            {
                uiManager.ApplyViolationAlertFromServer(
                    alertKey,
                    incidentType,
                    data.employee_id,
                    detectedBy,
                    robotNumber,
                    location,
                    photoUrl,
                    confidence,
                    "-",
                    message,
                    detectedAt,
                    data.status,
                    data.camera_id);
            }

            uiManager.AddExternalEventLog("ALERT", $"{incidentType} {robotLabel} {location} confidence={confidence}");
        });
    }

    private void HandleRobotStateJson(string json)
    {
        RobotStateWsMessage message = ParseJsonOrLog<RobotStateWsMessage>(json, "robot_state_update");
        if (message == null) return;

        bool hasHeading = TryReadOptionalFloat(json, "yaw", out float heading);
        if (!hasHeading)
        {
            hasHeading = TryReadOptionalFloat(json, "theta", out heading);
        }

        EnqueueUiAction(() => uiManager.ApplyRobotStateFromServer(
            message.robot_id,
            message.x,
            message.y,
            message.status,
            message.battery,
            message.linear_vel,
            message.angular_vel,
            message.pause_reason,
            hasHeading,
            heading));
    }

    private static bool TryReadOptionalFloat(string json, string fieldName, out float value)
    {
        value = 0f;
        Match match = Regex.Match(
            json,
            $"\\\"{Regex.Escape(fieldName)}\\\"\\s*:\\s*(?<value>-?(?:\\d+(?:\\.\\d*)?|\\.\\d+)(?:[eE][+-]?\\d+)?)");

        return match.Success && float.TryParse(
            match.Groups["value"].Value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static bool TryReadOptionalInt(string json, string fieldName, out int value)
    {
        value = 0;
        Match match = Regex.Match(
            json,
            $"\\\"{Regex.Escape(fieldName)}\\\"\\s*:\\s*(?<value>-?\\d+)");

        return match.Success && int.TryParse(
            match.Groups["value"].Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static bool TryReadOptionalBool(string json, string fieldName, out bool value)
    {
        value = false;
        Match match = Regex.Match(
            json,
            $"\\\"{Regex.Escape(fieldName)}\\\"\\s*:\\s*(?<value>true|false)",
            RegexOptions.IgnoreCase);

        return match.Success && bool.TryParse(match.Groups["value"].Value, out value);
    }

    private static bool TryReadOptionalString(string json, string fieldName, out string value)
    {
        value = string.Empty;
        Match match = Regex.Match(
            json,
            $"\\\"{Regex.Escape(fieldName)}\\\"\\s*:\\s*(?:\\\"(?<quoted>(?:\\\\.|[^\\\"])*)\\\"|(?<bare>[^,}}\\]]+))");
        if (!match.Success)
        {
            return false;
        }

        string rawValue = match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["bare"].Value;
        if (string.IsNullOrWhiteSpace(rawValue) || rawValue.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        value = rawValue.Trim();
        return true;
    }

    private static bool JsonFieldHasNonNullValue(string json, string fieldName)
    {
        return TryReadOptionalString(json, fieldName, out _) ||
               TryReadOptionalFloat(json, fieldName, out _) ||
               TryReadOptionalBool(json, fieldName, out _);
    }

    private static int ResolveRobotNumber(string json, int parsedRobotId)
    {
        if (parsedRobotId > 0)
        {
            return parsedRobotId;
        }

        return TryReadOptionalString(json, "robot_id", out string rawRobotId)
            ? ConvertRobotKeyToNumber(rawRobotId)
            : 0;
    }

    private static int ConvertRobotKeyToNumber(string rawRobotId)
    {
        if (string.IsNullOrWhiteSpace(rawRobotId))
        {
            return 0;
        }

        string normalized = rawRobotId.Trim().ToLowerInvariant();
        if (int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numericId))
        {
            return numericId;
        }

        Match match = Regex.Match(normalized, @"(?:tb3|cam|robot)[-_]?(?<num>\d+)");
        if (match.Success && int.TryParse(match.Groups["num"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static int ResolveAlertKey(string json, int alertId, int logId)
    {
        if (alertId > 0)
        {
            return alertId;
        }

        if (TryReadOptionalInt(json, "alert_id", out int parsedAlertId) && parsedAlertId > 0)
        {
            return parsedAlertId;
        }

        return logId;
    }

    private static string ReadOptionalDisplayValue(string json, string fieldName, string parsedValue = null)
    {
        if (!string.IsNullOrWhiteSpace(parsedValue))
        {
            return parsedValue.Trim();
        }

        if (TryReadOptionalString(json, fieldName, out string stringValue))
        {
            return stringValue;
        }

        return TryReadOptionalFloat(json, fieldName, out float floatValue)
            ? floatValue.ToString("0.###", CultureInfo.InvariantCulture)
            : null;
    }

    private static bool HasAnyMapNavStatusField(string json, RobotStatusWsData data)
    {
        return data != null && (
            !string.IsNullOrWhiteSpace(data.map_id) ||
            !string.IsNullOrWhiteSpace(data.localization_state) ||
            !string.IsNullOrWhiteSpace(data.amcl_state) ||
            !string.IsNullOrWhiteSpace(data.scan_match_state) ||
            !string.IsNullOrWhiteSpace(data.nav2_state) ||
            !string.IsNullOrWhiteSpace(data.planner_state) ||
            !string.IsNullOrWhiteSpace(data.controller_state) ||
            !string.IsNullOrWhiteSpace(data.goal_result) ||
            !string.IsNullOrWhiteSpace(data.route_state) ||
            JsonFieldHasNonNullValue(json, "initial_pose_set") ||
            JsonFieldHasNonNullValue(json, "localization_quality") ||
            JsonFieldHasNonNullValue(json, "current_wp_index") ||
            JsonFieldHasNonNullValue(json, "total_waypoints") ||
            JsonFieldHasNonNullValue(json, "current_target_wp") ||
            JsonFieldHasNonNullValue(json, "replan_count") ||
            JsonFieldHasNonNullValue(json, "updated_at"));
    }

    private static ControlTowerMapNavStatusData BuildMapNavStatus(int robotNumber, RobotStatusWsData data, string json)
    {
        return new ControlTowerMapNavStatusData
        {
            robot_id = robotNumber,
            map_id = data.map_id,
            localization_state = data.localization_state,
            amcl_state = data.amcl_state,
            initial_pose_set = TryReadOptionalBool(json, "initial_pose_set", out bool initialPoseSet) ? initialPoseSet : data.initial_pose_set,
            localization_quality = ReadOptionalDisplayValue(json, "localization_quality", data.localization_quality),
            scan_match_state = data.scan_match_state,
            nav2_state = data.nav2_state,
            planner_state = data.planner_state,
            controller_state = data.controller_state,
            current_target_wp = data.current_target_wp,
            current_wp_index = data.current_wp_index,
            total_waypoints = data.total_waypoints,
            route_state = data.route_state,
            goal_result = data.goal_result,
            replan_count = data.replan_count,
            updated_at = data.updated_at,
            has_initial_pose_set = JsonFieldHasNonNullValue(json, "initial_pose_set"),
            has_current_target_wp = JsonFieldHasNonNullValue(json, "current_target_wp"),
            has_current_wp_index = JsonFieldHasNonNullValue(json, "current_wp_index"),
            has_total_waypoints = JsonFieldHasNonNullValue(json, "total_waypoints"),
            has_replan_count = JsonFieldHasNonNullValue(json, "replan_count")
        };
    }

    private static ControlTowerWaypointRouteData BuildWaypointRoute(int robotNumber, RobotStatusWsData data, string json)
    {
        return new ControlTowerWaypointRouteData
        {
            robot_id = robotNumber,
            route_id = data.route_id,
            route_name = data.route_name,
            current_wp_index = data.current_wp_index,
            total_waypoints = data.total_waypoints,
            route_state = data.route_state,
            waypoints = data.waypoints,
            has_current_wp_index = JsonFieldHasNonNullValue(json, "current_wp_index"),
            has_total_waypoints = JsonFieldHasNonNullValue(json, "total_waypoints")
        };
    }

    private static bool HasAnyObstacleRecoveryField(string json, RobotStatusWsData data)
    {
        return data != null && (
            !string.IsNullOrWhiteSpace(data.obstacle_state) ||
            !string.IsNullOrWhiteSpace(data.obstacle_type) ||
            !string.IsNullOrWhiteSpace(data.recovery_state) ||
            !string.IsNullOrWhiteSpace(data.recovery_behavior) ||
            !string.IsNullOrWhiteSpace(data.detected_at) ||
            !string.IsNullOrWhiteSpace(data.message) ||
            JsonFieldHasNonNullValue(json, "obstacle_distance") ||
            JsonFieldHasNonNullValue(json, "obstacle_x") ||
            JsonFieldHasNonNullValue(json, "obstacle_y") ||
            JsonFieldHasNonNullValue(json, "recovery_retry_count"));
    }

    private static ControlTowerObstacleRecoveryData BuildObstacleRecovery(int robotNumber, RobotStatusWsData data, string json)
    {
        return new ControlTowerObstacleRecoveryData
        {
            robot_id = robotNumber,
            obstacle_state = data.obstacle_state,
            obstacle_type = data.obstacle_type,
            obstacle_distance = data.obstacle_distance,
            obstacle_x = data.obstacle_x,
            obstacle_y = data.obstacle_y,
            recovery_state = data.recovery_state,
            recovery_behavior = data.recovery_behavior,
            recovery_retry_count = data.recovery_retry_count,
            detected_at = data.detected_at,
            updated_at = data.updated_at,
            message = data.message,
            has_obstacle_distance = JsonFieldHasNonNullValue(json, "obstacle_distance"),
            has_obstacle_x = JsonFieldHasNonNullValue(json, "obstacle_x"),
            has_obstacle_y = JsonFieldHasNonNullValue(json, "obstacle_y"),
            has_recovery_retry_count = JsonFieldHasNonNullValue(json, "recovery_retry_count")
        };
    }

    private static void MarkCameraAiOptionalFields(CameraAiStatusWsData data, string json)
    {
        if (data?.streams != null)
        {
            List<string> streamPayloads = ExtractJsonObjectArrayItems(json, "streams");
            for (int i = 0; i < data.streams.Length; i++)
            {
                CameraAiStreamWsData stream = data.streams[i];
                if (stream == null)
                {
                    continue;
                }

                string streamJson = i < streamPayloads.Count ? streamPayloads[i] : null;
                if (stream.robot_id <= 0 && TryReadOptionalString(streamJson, "robot_id", out string rawRobotId))
                {
                    stream.robot_id = ConvertRobotKeyToNumber(rawRobotId);
                }

                if (string.IsNullOrWhiteSpace(stream.channel) && TryReadOptionalString(streamJson, "channel", out string rawChannel))
                {
                    stream.channel = rawChannel;
                }

                stream.has_connected = JsonFieldHasNonNullValue(streamJson, "connected");
                stream.has_frame_received = JsonFieldHasNonNullValue(streamJson, "frame_received");
                stream.has_fps = JsonFieldHasNonNullValue(streamJson, "fps");
                stream.has_stream_latency_ms = JsonFieldHasNonNullValue(streamJson, "stream_latency_ms");
            }
        }

        if (data?.ai != null)
        {
            data.ai.has_inference_fps = JsonFieldHasNonNullValue(json, "inference_fps");
            data.ai.has_inference_latency_ms = JsonFieldHasNonNullValue(json, "inference_latency_ms");
            data.ai.has_detection_enabled = JsonFieldHasNonNullValue(json, "detection_enabled");
        }
    }

    private static List<string> ExtractJsonObjectArrayItems(string json, string fieldName)
    {
        List<string> items = new List<string>();
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(fieldName))
        {
            return items;
        }

        Match fieldMatch = Regex.Match(
            json,
            $"\\\"{Regex.Escape(fieldName)}\\\"\\s*:",
            RegexOptions.CultureInvariant);
        if (!fieldMatch.Success)
        {
            return items;
        }

        int arrayStart = json.IndexOf('[', fieldMatch.Index + fieldMatch.Length);
        if (arrayStart < 0)
        {
            return items;
        }

        bool inString = false;
        bool escaped = false;
        int objectDepth = 0;
        int objectStart = -1;
        for (int i = arrayStart + 1; i < json.Length; i++)
        {
            char current = json[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (current == '\\')
                {
                    escaped = true;
                }
                else if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current == '{')
            {
                if (objectDepth == 0)
                {
                    objectStart = i;
                }

                objectDepth++;
                continue;
            }

            if (current == '}' && objectDepth > 0)
            {
                objectDepth--;
                if (objectDepth == 0 && objectStart >= 0)
                {
                    items.Add(json.Substring(objectStart, i - objectStart + 1));
                    objectStart = -1;
                }

                continue;
            }

            if (current == ']' && objectDepth == 0)
            {
                break;
            }
        }

        return items;
    }

    private void HandleViolationAlertJson(string json)
    {
        ViolationAlertWsMessage message = ParseJsonOrLog<ViolationAlertWsMessage>(json, "violation_alert");
        if (message == null) return;

        EnqueueUiAction(() => uiManager.ApplyViolationAlertFromServer(
            message.violation_id,
            message.violation_type,
            message.employee_id,
            message.detected_by,
            message.robot_id,
            message.robot_location,
            message.photo_url,
            FormatConfidence(message.ai_details),
            FormatBbox(message.ai_details)));
    }

    private void HandleEmergencyAlertJson(string json)
    {
        EmergencyAlertWsMessage message = ParseJsonOrLog<EmergencyAlertWsMessage>(json, "emergency_alert");
        if (message == null) return;

        EnqueueUiAction(() => uiManager.ApplyEmergencyAlertFromServer(
            message.emergency_id,
            message.emergency_type,
            message.detected_by,
            message.robot_id,
            message.robot_location,
            message.photo_url,
            FormatConfidence(message.ai_details),
            FormatBbox(message.ai_details)));
    }

    private void HandlePatrolTimelineEventJson(string json)
    {
        PatrolTimelineEventWsMessage message = ParseJsonOrLog<PatrolTimelineEventWsMessage>(json, "patrol_timeline_event");
        if (message == null) return;

        EnqueueUiAction(() => uiManager.ApplyPatrolTimelineEventFromServer(
            message.timeline_id,
            message.log_id,
            message.robot_id,
            message.state,
            message.pause_reason,
            message.changed_at));
    }

    private void HandlePatrolLogUpdateJson(string json)
    {
        PatrolLogUpdateWsMessage message = ParseJsonOrLog<PatrolLogUpdateWsMessage>(json, "patrol_log_update");
        if (message == null) return;

        EnqueueUiAction(() => uiManager.ApplyPatrolLogUpdateFromServer(
            message.log_id,
            message.robot_id,
            message.start_time,
            message.end_time,
            message.status));
    }

    private void HandleSystemStatusJson(string json)
    {
        SystemStatusWsMessage message = ParseJsonOrLog<SystemStatusWsMessage>(json, "system_status");
        if (message == null) return;

        EnqueueUiAction(() => uiManager.ApplySystemStatusFromServer(
            message.server_status,
            message.websocket_status,
            message.ros2_status,
            message.ai_model_status));
    }

    private void HandleCommandAckJson(string json)
    {
        CommandAckWsMessage message = ParseJsonOrLog<CommandAckWsMessage>(json, "command_ack");
        if (message == null) return;

        EnqueueUiAction(() => uiManager.ApplyCommandAckFromServer(
            message.robot_id,
            message.command,
            message.result_status,
            message.response_message));
    }

    private void HandleAlertAckResultJson(string json)
    {
        AlertAckResultWsMessage message = ParseJsonOrLog<AlertAckResultWsMessage>(json, "alert_ack_result");
        if (message == null) return;

        EnqueueUiAction(() => uiManager.ApplyAlertAckResultFromServer(
            message.alert_type,
            message.alert_id,
            message.action,
            message.result_status,
            message.response_message));
    }

    private T ParseJsonOrLog<T>(string json, string eventType) where T : class
    {
        try
        {
            T message = JsonUtility.FromJson<T>(json);
            if (message == null)
            {
                EnqueueUiLog("WS", $"{eventType} parse failed: empty message");
            }

            return message;
        }
        catch (Exception ex)
        {
            EnqueueUiLog("WS", $"{eventType} parse failed: {ex.Message}");
            return null;
        }
    }

    private void EnqueueUiAction(Action uiAction)
    {
        mainThreadActions.Enqueue(() =>
        {
            ResolveUiManager();
            if (uiManager == null)
            {
                Debug.LogWarning("[ControlTowerWS] scr_ControlTowerUIManager was not found.");
                return;
            }

            uiAction?.Invoke();
        });
    }

    private string FirstNonEmpty(params string[] values)
    {
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string ConvertRobotId(int robotId)
    {
        return robotId switch
        {
            1 => "tb3-01",
            2 => "tb3-02",
            3 => "tb3-03",
            _ => robotId > 0 ? $"tb3-{robotId:00}" : "GLOBAL_CAM"
        };
    }

    private static string NormalizeIncidentType(string incidentType)
    {
        string value = string.IsNullOrWhiteSpace(incidentType)
            ? "UNKNOWN_ALERT"
            : incidentType.Trim().ToUpperInvariant();

        return value switch
        {
            "EVENT_HELMET" => "NO_HELMET",
            "EVENT_FALL" => "FALL",
            "EVENT_FIRE" => "FIRE",
            _ => value
        };
    }

    private static bool IsEmergencyIncident(string incidentType)
    {
        string value = string.IsNullOrWhiteSpace(incidentType) ? string.Empty : incidentType.Trim().ToUpperInvariant();
        return value == "FALL" || value == "FIRE" || value == "EMERGENCY_STOP";
    }

    private static string FormatConfidence01(float confidence)
    {
        float percent = confidence <= 1f ? confidence * 100f : confidence;
        return $"{percent:0}%";
    }

    private string BuildAbsolutePhotoUrl(string photoUrl)
    {
        if (string.IsNullOrWhiteSpace(photoUrl))
        {
            return "-";
        }

        string trimmedPhotoUrl = photoUrl.Trim();
        if (trimmedPhotoUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmedPhotoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmedPhotoUrl;
        }

        string baseUrl = string.IsNullOrWhiteSpace(serverBaseUrl) ? "http://127.0.0.1:8000" : serverBaseUrl.Trim().TrimEnd('/');
        string relativeUrl = trimmedPhotoUrl.StartsWith("/") ? trimmedPhotoUrl : "/" + trimmedPhotoUrl;
        return baseUrl + relativeUrl;
    }

    private string GetEffectiveWebSocketUri()
    {
        if (string.IsNullOrWhiteSpace(webSocketUri) ||
            string.Equals(webSocketUri.Trim(), LegacyControlTowerWebSocketUri, StringComparison.OrdinalIgnoreCase))
        {
            return DefaultControlTowerWebSocketUri;
        }

        return webSocketUri.Trim();
    }

    private string FormatConfidence(AiDetailsWsMessage aiDetails)
    {
        if (aiDetails == null || aiDetails.confidence <= 0f)
        {
            return "-";
        }

        return aiDetails.confidence.ToString("0.00");
    }

    private string FormatBbox(AiDetailsWsMessage aiDetails)
    {
        if (aiDetails == null || aiDetails.bbox == null || aiDetails.bbox.Length < 4)
        {
            return "-";
        }

        return $"x={aiDetails.bbox[0]} y={aiDetails.bbox[1]} w={aiDetails.bbox[2]} h={aiDetails.bbox[3]}";
    }

    private void EnqueueConnectionState(bool connected, string message)
    {
        mainThreadActions.Enqueue(() =>
        {
            ResolveUiManager();
            if (uiManager != null)
            {
                uiManager.SetWebSocketConnectionState(connected, message);
            }
        });
    }

    private void EnqueueUiLog(string level, string message)
    {
        mainThreadActions.Enqueue(() =>
        {
            ResolveUiManager();
            if (uiManager != null)
            {
                uiManager.AddExternalEventLog(level, message);
            }
        });
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

    private async void OnDestroy()
    {
        await DisconnectAsync();
    }

    private async void OnApplicationQuit()
    {
        await DisconnectAsync();
    }

    private void CleanupSocket()
    {
        cancellation?.Dispose();
        cancellation = null;
        webSocket?.Dispose();
        webSocket = null;
        receiveTask = null;
    }
}

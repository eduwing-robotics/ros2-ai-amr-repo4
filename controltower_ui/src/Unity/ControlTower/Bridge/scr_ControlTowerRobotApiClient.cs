using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class scr_ControlTowerRobotApiClient : MonoBehaviour
{
    [SerializeField] private string serverBaseUrl = "http://127.0.0.1:8000";

    [Serializable]
    private class RobotCommandRequest
    {
        public string command;
        public string operator_id;
        public string timestamp;
    }

    [Serializable]
    private class TeleopRequest
    {
        public float linear_x;
        public float angular_z;
    }

    [Serializable]
    private class LiftTeleopRequest
    {
        public float linear_x;
        public float angular_z;
        public float lift;
    }

    [Serializable]
    private class RobotCommandResponse
    {
        public bool ok = false;
        public RobotCommandResponseData data = null;
        public string message = string.Empty;
    }

    [Serializable]
    private class RobotCommandResponseData
    {
        public string command_id = string.Empty;
        public string robot_id = string.Empty;
        public string command = string.Empty;
        public string status = string.Empty;
        public string message = string.Empty;
    }

    public async Task<RobotApiResult> SendCommandAsync(string robotId, string command, string operatorId)
    {
        string safeRobotId = NormalizeRobotId(robotId);
        string endpoint = $"{GetBaseUrl()}/api/v1/robots/{safeRobotId}/commands";
        RobotCommandRequest requestBody = new RobotCommandRequest
        {
            command = command,
            operator_id = string.IsNullOrWhiteSpace(operatorId) ? "OPERATOR_01" : operatorId,
            timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        string json = JsonUtility.ToJson(requestBody);
        Debug.Log($"[REST -> Server] POST {endpoint}");

        RobotApiResult result = await PostJsonAsync(endpoint, json);
        Debug.Log($"[REST <- Server] POST {endpoint} http={result.HttpStatusCode}");

        if (!result.TransportSuccess)
        {
            return result;
        }

        return EvaluateCommandResponse(result);
    }

    public async Task<RobotApiResult> SendTeleopAsync(string robotId, float linearX, float angularZ)
    {
        string safeRobotId = NormalizeRobotId(robotId);
        string endpoint = $"{GetBaseUrl()}/api/v1/robots/{safeRobotId}/teleop";
        string json = string.Equals(safeRobotId, "tb3-03", StringComparison.OrdinalIgnoreCase)
            ? JsonUtility.ToJson(new LiftTeleopRequest
            {
                linear_x = linearX,
                angular_z = angularZ,
                lift = 0f
            })
            : JsonUtility.ToJson(new TeleopRequest
            {
                linear_x = linearX,
                angular_z = angularZ
            });
        Debug.Log($"[REST -> Server] POST {endpoint}");

        RobotApiResult result = await PostJsonAsync(endpoint, json);
        Debug.Log($"[REST <- Server] POST {endpoint} http={result.HttpStatusCode}");
        return result;
    }

    public async Task<RobotApiResult> SendLiftTeleopAsync(string robotId, float lift)
    {
        string safeRobotId = NormalizeRobotId(robotId);
        if (!string.Equals(safeRobotId, "tb3-03", StringComparison.OrdinalIgnoreCase))
        {
            return new RobotApiResult
            {
                Success = false,
                TransportSuccess = true,
                Rejected = true,
                Message = "TB3-03에서만 사용할 수 있습니다.",
                RobotId = safeRobotId,
                Command = "LIFT"
            };
        }

        string endpoint = $"{GetBaseUrl()}/api/v1/robots/{safeRobotId}/teleop";
        LiftTeleopRequest requestBody = new LiftTeleopRequest
        {
            linear_x = 0f,
            angular_z = 0f,
            lift = Mathf.Clamp(lift, -1f, 1f)
        };

        string json = JsonUtility.ToJson(requestBody);
        Debug.Log($"[REST -> Server] POST {endpoint}");

        RobotApiResult result = await PostJsonAsync(endpoint, json);
        Debug.Log($"[REST <- Server] POST {endpoint} http={result.HttpStatusCode}");
        return result.TransportSuccess ? EvaluateLiftTeleopResponse(result) : result;
    }

    private async Task<RobotApiResult> PostJsonAsync(string endpoint, string json)
    {
        using UnityWebRequest request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        UnityWebRequestAsyncOperation operation = request.SendWebRequest();
        while (!operation.isDone)
        {
            await Task.Yield();
        }

        string responseBody = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
        bool httpSuccess = request.result == UnityWebRequest.Result.Success && request.responseCode >= 200 && request.responseCode < 300;
        return new RobotApiResult
        {
            Success = httpSuccess,
            TransportSuccess = httpSuccess,
            Rejected = false,
            HttpStatusCode = request.responseCode,
            Message = httpSuccess ? "HTTP OK" : request.error,
            ResponseBody = responseBody
        };
    }

    private RobotApiResult EvaluateCommandResponse(RobotApiResult transportResult)
    {
        if (string.IsNullOrWhiteSpace(transportResult.ResponseBody))
        {
            transportResult.Success = false;
            transportResult.Message = "Empty response body";
            return transportResult;
        }

        RobotCommandResponse response;
        try
        {
            response = JsonUtility.FromJson<RobotCommandResponse>(transportResult.ResponseBody);
        }
        catch (Exception exception)
        {
            transportResult.Success = false;
            transportResult.Message = $"Response parse failed: {exception.Message}";
            return transportResult;
        }

        string status = response != null && response.data != null ? response.data.status : string.Empty;
        string commandId = response != null && response.data != null ? response.data.command_id : string.Empty;
        string robotId = response != null && response.data != null ? response.data.robot_id : string.Empty;
        string command = response != null && response.data != null ? response.data.command : string.Empty;
        string dataMessage = response != null && response.data != null ? response.data.message : string.Empty;
        string message = !string.IsNullOrWhiteSpace(dataMessage)
            ? dataMessage
            : response != null && !string.IsNullOrWhiteSpace(response.message)
                ? response.message
                : "No message";

        bool accepted = response != null && response.ok && string.Equals(status, "accepted", StringComparison.OrdinalIgnoreCase);
        bool rejected = response == null || !response.ok || string.Equals(status, "rejected", StringComparison.OrdinalIgnoreCase);

        transportResult.Success = accepted;
        transportResult.Rejected = rejected;
        transportResult.Message = message;
        transportResult.CommandId = commandId;
        transportResult.RobotId = robotId;
        transportResult.Command = command;
        transportResult.Status = status;
        return transportResult;
    }

    private static RobotApiResult EvaluateLiftTeleopResponse(RobotApiResult transportResult)
    {
        if (string.IsNullOrWhiteSpace(transportResult.ResponseBody))
        {
            transportResult.Success = false;
            transportResult.Message = "서버 응답이 비어 있습니다.";
            return transportResult;
        }

        RobotCommandResponse response;
        try
        {
            response = JsonUtility.FromJson<RobotCommandResponse>(transportResult.ResponseBody);
        }
        catch (Exception)
        {
            transportResult.Success = false;
            transportResult.Message = "서버 응답을 해석할 수 없습니다.";
            return transportResult;
        }

        string dataMessage = response != null && response.data != null ? response.data.message : string.Empty;
        string message = !string.IsNullOrWhiteSpace(dataMessage)
            ? dataMessage
            : response != null && !string.IsNullOrWhiteSpace(response.message)
                ? response.message
                : response != null && response.ok
                    ? "수동 조작 전송 완료"
                    : "리프트 명령을 거부하였습니다.";

        transportResult.Success = response != null && response.ok;
        transportResult.Rejected = response == null || !response.ok;
        transportResult.Message = message;
        transportResult.Command = "LIFT";
        transportResult.Status = transportResult.Success ? "accepted" : "rejected";
        return transportResult;
    }

    private string GetBaseUrl()
    {
        return string.IsNullOrWhiteSpace(serverBaseUrl)
            ? "http://127.0.0.1:8000"
            : serverBaseUrl.TrimEnd('/');
    }

    private static string NormalizeRobotId(string robotId)
    {
        if (string.IsNullOrWhiteSpace(robotId))
        {
            return "tb3-01";
        }

        string trimmed = robotId.Trim().ToLowerInvariant().Replace("_", "-");

        if (int.TryParse(trimmed, out int robotNumber))
        {
            return $"tb3-{robotNumber:00}";
        }

        return trimmed;
    }
}

public struct RobotApiResult
{
    public bool Success;
    public bool TransportSuccess;
    public bool Rejected;
    public long HttpStatusCode;
    public string Message;
    public string ResponseBody;
    public string CommandId;
    public string RobotId;
    public string Command;
    public string Status;
}

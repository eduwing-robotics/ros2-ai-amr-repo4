using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class scr_CameraJpegWebSocketClient : MonoBehaviour
{
    [SerializeField] private string streamUri;
    [SerializeField] private RawImage[] targetRawImages;
    [SerializeField] private string diagnosticLabel = "CameraVideoClient";
    [SerializeField] private string diagnosticRobotId;
    [SerializeField, Min(1f)] private float maxDecodeFps = 15f;

    public bool logFrameDiagnostics = true;

    public UnityEvent<string> onStatusChanged = new UnityEvent<string>();
    public UnityEvent<string> onStreamLog = new UnityEvent<string>();
    public UnityEvent<string> onFrameApplied = new UnityEvent<string>();

    private readonly ConcurrentQueue<Action> mainThreadActions = new();
    private readonly object latestFrameLock = new();
    private ClientWebSocket webSocket;
    private CancellationTokenSource cancellation;
    private Texture2D frameTexture;
    private FramePacket latestFrame;
    private bool hasLatestFrame;
    private int generationId;
    private int receivedFrameCount;
    private long lastQueuedFrameTimestamp;
    private long lastFrameAppliedSecond = -1;
    private float nextDecodeTime;
    private string sourceKey = string.Empty;
    private bool isClosing;
    private bool targetRawImagesWarningShown;

    public bool IsConnected => webSocket != null && webSocket.State == WebSocketState.Open;
    public string StreamUri => streamUri;
    public string SourceKey => sourceKey;
    public Texture2D CurrentFrameTexture => frameTexture;
    public int ReceivedFrameCount => Volatile.Read(ref receivedFrameCount);

    private static readonly ProfilerMarker CameraReceiveMarker = new("ControlTower.Camera.Receive");
    private static readonly ProfilerMarker CameraDecodeMarker = new("ControlTower.Camera.Decode");
    private static readonly ProfilerMarker CameraAssignTextureMarker = new("ControlTower.Camera.AssignTexture");

    private struct FramePacket
    {
        public byte[] Bytes;
        public int GenerationId;
        public int FrameNumber;
        public string Uri;
    }

    public void SetStreamUri(string uri)
    {
        streamUri = uri;
    }

    public void SetTargets(params RawImage[] rawImages)
    {
        targetRawImages = rawImages;
        targetRawImagesWarningShown = false;
    }

    public void SetDiagnosticLabel(string label)
    {
        diagnosticLabel = string.IsNullOrWhiteSpace(label) ? "CameraVideoClient" : label.Trim();
    }

    public void SetDiagnosticRobotId(string robotId)
    {
        diagnosticRobotId = string.IsNullOrWhiteSpace(robotId) ? string.Empty : robotId.Trim().ToLowerInvariant();
    }

    public void SetSourceKey(string value)
    {
        sourceKey = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }

    public void ClearTargetFrames()
    {
        lock (latestFrameLock)
        {
            latestFrame = default;
            hasLatestFrame = false;
        }

        if (targetRawImages == null)
        {
            return;
        }

        foreach (RawImage rawImage in targetRawImages)
        {
            if (rawImage != null)
            {
                rawImage.texture = null;
                Color color = rawImage.color;
                color.a = 0f;
                rawImage.color = color;
            }
        }
    }

    public void Connect()
    {
        _ = ConnectAsync();
    }

    public void Disconnect()
    {
        _ = DisconnectAsync();
    }

    public async Task ReconnectAsync(string uri)
    {
        SetStreamUri(uri);
        await DisconnectAsync();
        await ConnectAsync();
    }

    private void Update()
    {
        while (mainThreadActions.TryDequeue(out Action action))
        {
            action?.Invoke();
        }

        if (maxDecodeFps > 0f && Time.unscaledTime < nextDecodeTime)
        {
            return;
        }

        FramePacket framePacket = default;
        bool hasFramePacket = false;
        lock (latestFrameLock)
        {
            if (hasLatestFrame)
            {
                framePacket = latestFrame;
                latestFrame = default;
                hasLatestFrame = false;
                hasFramePacket = true;
            }
        }

        if (hasFramePacket && framePacket.GenerationId == Volatile.Read(ref generationId))
        {
            nextDecodeTime = maxDecodeFps > 0f
                ? Time.unscaledTime + 1f / Mathf.Max(1f, maxDecodeFps)
                : Time.unscaledTime;
            DecodeAndApplyFrame(framePacket);
        }
    }

    public async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(streamUri))
        {
            EnqueueStatus("No Stream");
            return;
        }

        if (IsConnected || (webSocket != null && webSocket.State == WebSocketState.Connecting))
        {
            return;
        }

        int generation = Interlocked.Increment(ref generationId);
        isClosing = false;
        Interlocked.Exchange(ref receivedFrameCount, 0);
        Interlocked.Exchange(ref lastQueuedFrameTimestamp, 0L);
        Interlocked.Exchange(ref lastFrameAppliedSecond, -1L);
        nextDecodeTime = 0f;
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = new CancellationTokenSource();
        ClientWebSocket socket = new ClientWebSocket();
        webSocket = socket;
        EnqueueStatus("Connecting");

        try
        {
            await socket.ConnectAsync(new Uri(streamUri), cancellation.Token);
            if (generation != Volatile.Read(ref generationId) || isClosing)
            {
                await CloseSocketAsync(socket);
                return;
            }

            EnqueueStatus("Connected");
            _ = ReceiveLoopAsync(socket, cancellation.Token, generation, streamUri);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (generation == Volatile.Read(ref generationId))
            {
                EnqueueLog($"Connection failed: {ex.Message}");
                EnqueueStatus("Disconnected");
            }
        }
    }

    public async Task DisconnectAsync()
    {
        isClosing = true;
        Interlocked.Increment(ref generationId);
        ClientWebSocket socket = webSocket;
        webSocket = null;

        try
        {
            cancellation?.Cancel();
            if (socket != null && (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived))
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Camera stream closed", CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            EnqueueLog($"Disconnect error: {ex.Message}");
        }
        finally
        {
            socket?.Dispose();
            cancellation?.Dispose();
            cancellation = null;
            lock (latestFrameLock)
            {
                latestFrame = default;
                hasLatestFrame = false;
            }

            EnqueueStatus("Disconnected");
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token, int generation, string receiveUri)
    {
        byte[] buffer = new byte[16 * 1024];
        using MemoryStream frameStream = new MemoryStream();

        try
        {
            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open && generation == Volatile.Read(ref generationId))
            {
                frameStream.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        EnqueueStatus("Disconnected");
                        return;
                    }

                    if (result.MessageType == WebSocketMessageType.Binary && result.Count > 0)
                    {
                        frameStream.Write(buffer, 0, result.Count);
                    }
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Binary && frameStream.Length > 0)
                {
                    int frameNumber = Interlocked.Increment(ref receivedFrameCount);
                    if (!ShouldQueueFrameForDecode())
                    {
                        continue;
                    }

                    using (CameraReceiveMarker.Auto())
                    {
                        byte[] newestFrame = frameStream.ToArray();
                        if (ShouldLogFrame(frameNumber))
                        {
                            EnqueueFrameDiagnostic($"binary frame received bytes={newestFrame.Length} {GetJpegSignature(newestFrame)}");
                        }

                        lock (latestFrameLock)
                        {
                            latestFrame = new FramePacket
                            {
                                Bytes = newestFrame,
                                GenerationId = generation,
                                FrameNumber = frameNumber,
                                Uri = receiveUri
                            };
                            hasLatestFrame = true;
                        }
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    EnqueueUnexpectedTextWarning();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!isClosing && generation == Volatile.Read(ref generationId))
            {
                EnqueueLog($"Receive failed: {ex.Message}");
                EnqueueStatus("Disconnected");
            }
        }
    }

    private void DecodeAndApplyFrame(FramePacket framePacket)
    {
        byte[] frameBytes = framePacket.Bytes;
        try
        {
            if (frameTexture == null)
            {
                frameTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            }

            bool decoded;
            using (CameraDecodeMarker.Auto())
            {
                decoded = frameTexture.LoadImage(frameBytes, false);
            }
            if (!decoded)
            {
                if (ShouldLogFrame(framePacket.FrameNumber))
                {
                    EnqueueFrameDiagnostic($"frame decode failed bytes={frameBytes.Length} uri={framePacket.Uri}", true);
                }

                EnqueueLog("Frame decode failed");
                return;
            }

            if (ShouldLogFrame(framePacket.FrameNumber))
            {
                EnqueueFrameDiagnostic($"frame decoded {frameTexture.width}x{frameTexture.height} bytes={frameBytes.Length} uri={framePacket.Uri}");
            }

            if (targetRawImages == null || targetRawImages.Length == 0)
            {
                WarnTargetRawImagesEmpty();
                return;
            }

            int validTargetCount = 0;
            using (CameraAssignTextureMarker.Auto())
            {
                foreach (RawImage rawImage in targetRawImages)
                {
                    if (rawImage == null)
                    {
                        continue;
                    }

                    if (rawImage.texture != frameTexture)
                    {
                        rawImage.texture = frameTexture;
                    }

                    Color color = rawImage.color;
                    if (!Mathf.Approximately(color.a, 1f))
                    {
                        color.a = 1f;
                        rawImage.color = color;
                    }

                    validTargetCount++;
                }
            }

            if (validTargetCount == 0)
            {
                WarnTargetRawImagesEmpty();
                return;
            }

            DateTime now = DateTime.Now;
            long currentSecond = now.Ticks / TimeSpan.TicksPerSecond;
            if (Interlocked.Exchange(ref lastFrameAppliedSecond, currentSecond) != currentSecond)
            {
                onFrameApplied?.Invoke(now.ToString("HH:mm:ss"));
            }
        }
        catch (Exception ex)
        {
            if (ShouldLogFrame(framePacket.FrameNumber))
            {
                EnqueueFrameDiagnostic($"frame decode failed bytes={frameBytes.Length} uri={framePacket.Uri}", true);
            }

            EnqueueLog($"Frame decode failed: {ex.Message}");
        }
    }

    private bool ShouldLogFrame(int frameNumber)
    {
        return logFrameDiagnostics && frameNumber > 0 && (frameNumber == 1 || frameNumber % 30 == 0);
    }

    private bool ShouldQueueFrameForDecode()
    {
        float decodeFps = maxDecodeFps;
        if (decodeFps <= 0f)
        {
            return true;
        }

        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        long minimumInterval = Math.Max(1L, (long)(System.Diagnostics.Stopwatch.Frequency / Math.Max(1f, decodeFps)));
        while (true)
        {
            long previous = Interlocked.Read(ref lastQueuedFrameTimestamp);
            if (previous > 0L && now - previous < minimumInterval)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref lastQueuedFrameTimestamp, now, previous) == previous)
            {
                return true;
            }
        }
    }

    private string GetJpegSignature(byte[] frameBytes)
    {
        if (frameBytes == null || frameBytes.Length < 2)
        {
            return "first=-- -- last=-- --";
        }

        int lastIndex = frameBytes.Length - 2;
        return $"first={frameBytes[0]:X2} {frameBytes[1]:X2} last={frameBytes[lastIndex]:X2} {frameBytes[lastIndex + 1]:X2}";
    }

    private void WarnTargetRawImagesEmpty()
    {
        if (targetRawImagesWarningShown)
        {
            return;
        }

        targetRawImagesWarningShown = true;
        mainThreadActions.Enqueue(() => Debug.LogWarning("[CAM] target RawImages is empty"));
    }

    private void EnqueueUnexpectedTextWarning()
    {
        mainThreadActions.Enqueue(() => Debug.LogWarning("[CAM] unexpected text message received"));
    }

    private void EnqueueFrameDiagnostic(string message, bool warning = false)
    {
        mainThreadActions.Enqueue(() =>
        {
            string robotContext = string.IsNullOrEmpty(diagnosticRobotId) ? string.Empty : $"[{diagnosticRobotId}]";
            string formatted = $"[CAM][{diagnosticLabel}]{robotContext} {message}";
            if (warning)
            {
                Debug.LogWarning(formatted);
            }
            else
            {
                Debug.Log(formatted);
            }
        });
    }

    private async Task CloseSocketAsync(ClientWebSocket socket)
    {
        try
        {
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Camera stream replaced", CancellationToken.None);
            }
        }
        catch
        {
        }
        finally
        {
            socket.Dispose();
        }
    }

    private void EnqueueStatus(string status)
    {
        mainThreadActions.Enqueue(() => onStatusChanged?.Invoke(status));
    }

    private void EnqueueLog(string message)
    {
        mainThreadActions.Enqueue(() => onStreamLog?.Invoke(message));
    }

    private void OnDisable()
    {
        Disconnect();
    }

    private void OnDestroy()
    {
        Disconnect();
        if (frameTexture != null)
        {
            Destroy(frameTexture);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

public class scr_FactoryConveyorRuntimeController : MonoBehaviour
{
    [SerializeField] private bool runOnStart = true;
    [SerializeField] private float speed = 0.35f;
    [SerializeField] private float travelDistance = 0.12f;
    [SerializeField] private float respawnDelay = 0.10f;
    [SerializeField] private float spawnInterval = 2.50f;
    [SerializeField] private bool loop = true;
    [SerializeField] private Vector3 localAxis = Vector3.right;
    [SerializeField] private Transform[] movingParts;
    [SerializeField] private Transform startPoint;
    [SerializeField] private Transform endPoint;
    [SerializeField] private bool useBeltBoundsForRoute = true;

    private readonly List<MovingPartState> partStates = new();
    private readonly Queue<MovingPartState> spawnQueue = new();
    private bool running;
    private float commonStartProgress;
    private float commonEndProgress;
    private float startClearDistance;
    private float nextAllowedSpawnTime;

    private sealed class MovingPartState
    {
        public Transform Part;
        public float RespawnTimer;
        public bool Respawning;
        public bool Queued;
    }

    public void SetRunning(bool isRunning)
    {
        running = isRunning;
        if (running)
        {
            Vector3 axis = localAxis.sqrMagnitude > 0.0001f ? localAxis.normalized : Vector3.right;
            TrySpawnNextQueuedPart(axis, Time.time, true);
        }
    }

    private void Awake()
    {
        ResolveRoutePoints();
        ResolveMovingParts();
    }

    private void OnEnable()
    {
        ResolveRoutePoints();
        ResolveMovingParts();
        running = runOnStart;
        CachePartStates();
    }

    private void ResolveRoutePoints()
    {
        if (startPoint == null)
        {
            startPoint = FindChildByName("Conveyor_StartPoint");
        }

        if (endPoint == null)
        {
            endPoint = FindChildByName("Conveyor_EndPoint");
        }
    }

    private Transform FindChildByName(string objectName)
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child != transform && child.name == objectName)
            {
                return child;
            }
        }

        return null;
    }

    private void Update()
    {
        if (!running || partStates.Count == 0)
        {
            return;
        }

        Vector3 axis = localAxis.sqrMagnitude > 0.0001f ? localAxis.normalized : Vector3.right;
        float moveSpeed = Mathf.Max(0f, speed);
        float delay = Mathf.Max(0f, respawnDelay);
        float now = Time.time;
        foreach (MovingPartState state in partStates)
        {
            if (state.Part == null)
            {
                continue;
            }

            if (state.Respawning)
            {
                state.RespawnTimer -= Time.deltaTime;
                if (state.RespawnTimer <= 0f && loop)
                {
                    state.Respawning = false;
                    EnqueueSpawn(state);
                }

                continue;
            }

            if (moveSpeed <= 0f || !state.Part.gameObject.activeSelf)
            {
                continue;
            }

            Vector3 nextPosition = state.Part.localPosition + axis * moveSpeed * Time.deltaTime;
            if (HasReachedEnd(nextPosition, axis))
            {
                state.Part.gameObject.SetActive(false);
                state.Part.localPosition = SetAxisProgress(state.Part.localPosition, axis, commonStartProgress);
                state.Respawning = loop;
                state.RespawnTimer = delay;
                continue;
            }

            state.Part.localPosition = nextPosition;
        }

        TrySpawnNextQueuedPart(axis, now, false);
    }

    private void ResolveMovingParts()
    {
        if (movingParts != null && movingParts.Length > 0)
        {
            movingParts = FilterMovingParts(movingParts);
            return;
        }

        List<Transform> resolved = new();
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child == transform)
            {
                continue;
            }

            string name = child.name;
            if (IsMovableCargoName(name))
            {
                resolved.Add(child);
            }
        }

        movingParts = resolved.ToArray();
    }

    private void CachePartStates()
    {
        partStates.Clear();
        spawnQueue.Clear();
        if (movingParts == null)
        {
            return;
        }

        Vector3 axis = localAxis.sqrMagnitude > 0.0001f ? localAxis.normalized : Vector3.right;
        CalculateCommonRoute(axis);
        foreach (Transform part in movingParts)
        {
            if (part != null)
            {
                MovingPartState state = new MovingPartState
                {
                    Part = part,
                    RespawnTimer = 0f,
                    Respawning = false,
                    Queued = false
                };
                part.localPosition = SetAxisProgress(part.localPosition, axis, commonStartProgress);
                part.gameObject.SetActive(false);
                partStates.Add(state);
                EnqueueSpawn(state);
            }
        }

        nextAllowedSpawnTime = Time.time;
        if (running)
        {
            TrySpawnNextQueuedPart(axis, Time.time, true);
        }
    }

    private void CalculateCommonRoute(Vector3 axis)
    {
        if (TryGetPointRouteProgress(axis, out float pointStart, out float pointEnd))
        {
            commonStartProgress = pointStart;
            commonEndProgress = pointEnd;
            float cargoHalfExtent = GetMaxCargoHalfExtent(axis);
            startClearDistance = Mathf.Max(cargoHalfExtent * 2.2f, 0.001f);
            return;
        }

        if (useBeltBoundsForRoute && TryGetBeltRouteProgress(axis, out float routeMin, out float routeMax))
        {
            float cargoHalfExtent = GetMaxCargoHalfExtent(axis);
            startClearDistance = Mathf.Max(cargoHalfExtent * 2.2f, 0.001f);
            commonStartProgress = routeMin + cargoHalfExtent;
            commonEndProgress = routeMax - cargoHalfExtent;
            if (commonEndProgress <= commonStartProgress)
            {
                commonStartProgress = routeMin;
                commonEndProgress = routeMax;
                startClearDistance = Mathf.Max((commonEndProgress - commonStartProgress) * 0.15f, 0.001f);
            }

            return;
        }

        CalculateFallbackRouteProgress(axis);
    }

    private bool TryGetPointRouteProgress(Vector3 axis, out float startProgress, out float endProgress)
    {
        startProgress = 0f;
        endProgress = 0f;
        if (startPoint == null || endPoint == null)
        {
            return false;
        }

        Vector3 startLocal = transform.InverseTransformPoint(startPoint.position);
        Vector3 endLocal = transform.InverseTransformPoint(endPoint.position);
        startProgress = Vector3.Dot(startLocal, axis);
        endProgress = Vector3.Dot(endLocal, axis);
        return !Mathf.Approximately(startProgress, endProgress);
    }

    private bool TryGetBeltRouteProgress(Vector3 axis, out float routeMin, out float routeMax)
    {
        routeMin = float.PositiveInfinity;
        routeMax = float.NegativeInfinity;
        Renderer bestRenderer = null;
        float bestSpan = 0f;

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || renderer.transform == transform || IsMovingPart(renderer.transform) || IsIgnoredRouteRendererName(renderer.name))
            {
                continue;
            }

            if (!IsPreferredRouteRendererName(renderer.name))
            {
                continue;
            }

            GetRendererProgressRange(renderer, axis, out float min, out float max);
            float span = max - min;
            if (span > bestSpan)
            {
                bestSpan = span;
                bestRenderer = renderer;
                routeMin = min;
                routeMax = max;
            }
        }

        return bestRenderer != null && routeMax > routeMin;
    }

    private void CalculateFallbackRouteProgress(Vector3 axis)
    {
        float minProgress = float.PositiveInfinity;
        float maxProgress = float.NegativeInfinity;
        if (movingParts != null)
        {
            foreach (Transform part in movingParts)
            {
                if (part == null)
                {
                    continue;
                }

                float progress = Vector3.Dot(part.localPosition, axis);
                minProgress = Mathf.Min(minProgress, progress);
                maxProgress = Mathf.Max(maxProgress, progress);
            }
        }

        if (!float.IsFinite(minProgress) || !float.IsFinite(maxProgress))
        {
            minProgress = 0f;
            maxProgress = Mathf.Max(0.001f, travelDistance);
        }

        commonStartProgress = minProgress;
        commonEndProgress = Mathf.Max(maxProgress + Mathf.Abs(travelDistance), commonStartProgress + 0.001f);
        startClearDistance = Mathf.Max(Mathf.Abs(travelDistance) * 0.1f, 0.001f);
    }

    private void EnqueueSpawn(MovingPartState state)
    {
        if (state == null || state.Part == null || state.Queued)
        {
            return;
        }

        state.Queued = true;
        spawnQueue.Enqueue(state);
    }

    private bool TrySpawnNextQueuedPart(Vector3 axis, float now, bool ignoreInterval)
    {
        while (spawnQueue.Count > 0 && (spawnQueue.Peek() == null || spawnQueue.Peek().Part == null))
        {
            spawnQueue.Dequeue();
        }

        if (spawnQueue.Count == 0 || (!ignoreInterval && now < nextAllowedSpawnTime))
        {
            return false;
        }

        MovingPartState next = spawnQueue.Peek();
        if (!IsStartClear(axis, next))
        {
            return false;
        }

        spawnQueue.Dequeue();
        next.Queued = false;
        next.Respawning = false;
        next.RespawnTimer = 0f;
        next.Part.localPosition = SetAxisProgress(next.Part.localPosition, axis, commonStartProgress);
        next.Part.gameObject.SetActive(true);
        nextAllowedSpawnTime = now + Mathf.Max(0f, spawnInterval);
        return true;
    }

    private bool IsStartClear(Vector3 axis, MovingPartState candidate)
    {
        foreach (MovingPartState state in partStates)
        {
            if (state == null || state == candidate || state.Part == null || !state.Part.gameObject.activeSelf)
            {
                continue;
            }

            float progress = Vector3.Dot(state.Part.localPosition, axis);
            if (Mathf.Abs(progress - commonStartProgress) < startClearDistance)
            {
                return false;
            }
        }

        return true;
    }

    private float GetMaxCargoHalfExtent(Vector3 axis)
    {
        float maxHalfExtent = 0f;
        if (movingParts == null)
        {
            return maxHalfExtent;
        }

        foreach (Transform part in movingParts)
        {
            if (part == null)
            {
                continue;
            }

            Renderer[] renderers = part.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                GetRendererProgressRange(renderer, axis, out float min, out float max);
                maxHalfExtent = Mathf.Max(maxHalfExtent, (max - min) * 0.5f);
            }
        }

        return maxHalfExtent;
    }

    private void GetRendererProgressRange(Renderer renderer, Vector3 axis, out float min, out float max)
    {
        Bounds bounds = renderer.bounds;
        min = float.PositiveInfinity;
        max = float.NegativeInfinity;
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;

        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 worldCorner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                    Vector3 localCorner = transform.InverseTransformPoint(worldCorner);
                    float progress = Vector3.Dot(localCorner, axis);
                    min = Mathf.Min(min, progress);
                    max = Mathf.Max(max, progress);
                }
            }
        }
    }

    private bool IsMovingPart(Transform candidate)
    {
        if (candidate == null || movingParts == null)
        {
            return false;
        }

        foreach (Transform part in movingParts)
        {
            if (part != null && (candidate == part || candidate.IsChildOf(part)))
            {
                return true;
            }
        }

        return false;
    }

    private static Transform[] FilterMovingParts(Transform[] source)
    {
        List<Transform> filtered = new();
        foreach (Transform item in source)
        {
            if (item != null && IsMovableCargoName(item.name))
            {
                filtered.Add(item);
            }
        }

        return filtered.ToArray();
    }

    private static bool IsMovableCargoName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return false;
        }

        string value = objectName.ToLowerInvariant();
        return value.Contains("box") || value.Contains("cargo");
    }

    private static bool IsPreferredRouteRendererName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return false;
        }

        string value = objectName.ToLowerInvariant();
        return value.Contains("belt") || value.Contains("conveyorzone") || value.Contains("conveyor_zone") || value.Contains("conveyor");
    }

    private static bool IsIgnoredRouteRendererName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return false;
        }

        string value = objectName.ToLowerInvariant();
        return value.Contains("text") ||
               value.Contains("sign") ||
               value.Contains("wall") ||
               value.Contains("door") ||
               value.Contains("box") ||
               value.Contains("cargo");
    }

    private bool HasReachedEnd(Vector3 position, Vector3 axis)
    {
        float currentProgress = Vector3.Dot(position, axis);
        return currentProgress >= commonEndProgress;
    }

    private static Vector3 SetAxisProgress(Vector3 currentPosition, Vector3 axis, float targetProgress)
    {
        float currentProgress = Vector3.Dot(currentPosition, axis);
        return currentPosition + axis * (targetProgress - currentProgress);
    }
}

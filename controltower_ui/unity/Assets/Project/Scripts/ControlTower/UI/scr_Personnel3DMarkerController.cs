using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class scr_Personnel3DMarkerController : MonoBehaviour
{
    public event System.Action Person2DStateChanged;

    [Header("Worker Markers")]
    [SerializeField] private Transform[] employeeMarkers;
    [SerializeField] private Transform[] employeeInsidePositions;
    [SerializeField] private Transform[] employeeExitPositions;

    [Header("Visitor Markers")]
    [SerializeField] private Transform[] visitorMarkers;
    [SerializeField] private Transform[] visitorInsidePositions;
    [SerializeField] private Transform[] visitorExitPositions;

    [Header("Route Points")]
    [SerializeField] private Transform[] workerRoutes;
    [SerializeField] private Transform[] visitorRoutes;
    [SerializeField] private Transform staffEntranceOutside;
    [SerializeField] private Transform staffEntranceGateFront;
    [SerializeField] private Transform staffEntranceInside;
    [SerializeField] private Transform workerRouteHub;
    [SerializeField] private Transform visitorRouteHub;
    [SerializeField] private scr_StaffEntranceBarrierController barrierController;

    [Header("Motion")]
    [SerializeField] private float moveDuration = 0.7f;
    [SerializeField] private bool useMoveSpeedMode = true;
    [SerializeField] private float workerMoveSpeed = 1.2f;
    [SerializeField] private float visitorMoveSpeed = 1.0f;
    [SerializeField] private float rotationSpeedDeg = 360f;
    [SerializeField] private float waypointArriveDistance = 0.03f;
    [SerializeField] private float waitAtGateSeconds = 0.2f;
    [SerializeField] private float waitAfterBarrierOpenSeconds = 0.4f;
    [SerializeField] private bool faceMoveDirection = true;
    [SerializeField] private bool useWalkAnimation = true;
    [SerializeField] private bool useRouteContainerAsPointWhenNoChildren = true;
    [SerializeField] private bool logRouteDebug;
    [SerializeField] private bool logAnimationDebug;
    [SerializeField] private AnimationClip walkClip;
    [SerializeField] private AnimationClip standClip;
    [SerializeField] private string walkStateName = "Walk";
    [SerializeField] private string standStateName = "Stand";
    [SerializeField] private bool logDebug;

    private readonly Dictionary<string, int> workerIndexById = new();
    private readonly Dictionary<string, int> visitorIndexById = new();
    private readonly Dictionary<Transform, Coroutine> moveRoutines = new();
    private readonly Dictionary<Transform, bool> visibleOn2DByMarker = new();
    private int nextFallbackWorkerIndex;
    private int nextFallbackVisitorIndex;
    private bool resolvedLogPrinted;

    private void Awake()
    {
        ResolveSceneReferences();
        ResolveAnimationClipsIfNeeded();
    }

    public void ApplyEmployeeAttendance(string employeeId, string employeeName, string actionType)
    {
        ResolveSceneReferences();
        string id = string.IsNullOrWhiteSpace(employeeId) ? "WORKER-UNKNOWN" : employeeId.Trim();
        string action = string.IsNullOrWhiteSpace(actionType) ? string.Empty : actionType.Trim().ToLowerInvariant();
        int index = ResolveWorkerIndex(id);
        Transform marker = GetArrayItem(employeeMarkers, index);
        if (marker == null)
        {
            Debug.LogWarning("[People3D] Worker marker is missing.");
            return;
        }

        marker.gameObject.SetActive(true);
        if (action == "check_out")
        {
            SetPerson2DVisible(marker, true);
            Transform destination = GetArrayItem(employeeExitPositions, index);
            MoveAlongRoute(marker, BuildWorkerCheckOutRoute(index, destination), $"{id} check_out", false, workerMoveSpeed, false);
            if (logDebug) Debug.Log($"[People3D] Worker check_out route started speed={workerMoveSpeed:0.##}");
            return;
        }

        SetPerson2DVisible(marker, true);
        Transform inside = GetArrayItem(employeeInsidePositions, index);
        MoveAlongRoute(marker, BuildWorkerCheckInRoute(index, inside), $"{id} check_in", false, workerMoveSpeed, true);
        if (logDebug) Debug.Log($"[People3D] Worker check_in route started speed={workerMoveSpeed:0.##}");
    }

    public void ApplyVisitorAttendance(string visitorId, string visitorName, string actionType)
    {
        ResolveSceneReferences();
        string id = string.IsNullOrWhiteSpace(visitorId) ? "VISITOR-001" : visitorId.Trim();
        string action = string.IsNullOrWhiteSpace(actionType) ? string.Empty : actionType.Trim().ToLowerInvariant();
        int index = ResolveVisitorIndex(id);
        Transform marker = GetArrayItem(visitorMarkers, index);
        if (marker == null)
        {
            Debug.LogWarning("[People3D] Visitor marker is missing.");
            return;
        }

        marker.gameObject.SetActive(true);
        if (action == "exit")
        {
            SetPerson2DVisible(marker, true);
            Transform destination = GetArrayItem(visitorExitPositions, index);
            MoveAlongRoute(marker, BuildVisitorExitRoute(index, destination), $"{id} exit", false, visitorMoveSpeed, false);
            if (logDebug) Debug.Log($"[People3D] Visitor exit route started speed={visitorMoveSpeed:0.##}");
            return;
        }

        SetPerson2DVisible(marker, true);
        Transform inside = GetArrayItem(visitorInsidePositions, index);
        MoveAlongRoute(marker, BuildVisitorEntryRoute(index, inside), $"{id} entry", false, visitorMoveSpeed, true);
        if (logDebug) Debug.Log($"[People3D] Visitor entry route started speed={visitorMoveSpeed:0.##}");
    }

    private List<Transform> BuildWorkerCheckInRoute(int index, Transform destination)
    {
        List<Transform> route = BuildRoute(staffEntranceOutside, staffEntranceGateFront, staffEntranceInside, workerRouteHub);
        List<Transform> specificRoute = BuildRouteFromContainer(GetArrayItem(workerRoutes, index), false, $"WORKER-{index + 1:000}", $"WorkerRoute_{index + 1:00}");
        route.AddRange(specificRoute);
        AddIfNotNull(route, destination);
        return route;
    }

    private List<Transform> BuildWorkerCheckOutRoute(int index, Transform destination)
    {
        List<Transform> route = BuildRouteFromContainer(GetArrayItem(workerRoutes, index), true, $"WORKER-{index + 1:000}", $"WorkerRoute_{index + 1:00}");
        route.AddRange(BuildRoute(workerRouteHub, staffEntranceInside, staffEntranceGateFront, staffEntranceOutside));
        AddIfNotNull(route, destination);
        return route;
    }

    private List<Transform> BuildVisitorEntryRoute(int index, Transform destination)
    {
        List<Transform> route = BuildRoute(staffEntranceOutside, staffEntranceGateFront, staffEntranceInside, visitorRouteHub);
        List<Transform> specificRoute = BuildRouteFromContainer(GetArrayItem(visitorRoutes, index), false, $"VISITOR-{index + 1:000}", $"VisitorRoute_{index + 1:00}");
        route.AddRange(specificRoute);
        AddIfNotNull(route, destination);
        return route;
    }

    private List<Transform> BuildVisitorExitRoute(int index, Transform destination)
    {
        List<Transform> route = BuildRouteFromContainer(GetArrayItem(visitorRoutes, index), true, $"VISITOR-{index + 1:000}", $"VisitorRoute_{index + 1:00}");
        route.AddRange(BuildRoute(visitorRouteHub, staffEntranceInside, staffEntranceGateFront, staffEntranceOutside));
        AddIfNotNull(route, destination);
        return route;
    }

    private static List<Transform> BuildRoute(params Transform[] routePoints)
    {
        List<Transform> route = new List<Transform>();
        foreach (Transform point in routePoints)
        {
            if (point != null)
            {
                route.Add(point);
            }
        }

        return route;
    }

    private static void AddIfNotNull(List<Transform> route, Transform point)
    {
        if (route != null && point != null)
        {
            route.Add(point);
        }
    }

    private List<Transform> BuildRouteFromContainer(Transform routeRoot, bool reverse, string actorId, string routeName)
    {
        List<Transform> points = new List<Transform>();
        if (routeRoot == null)
        {
            if (logRouteDebug) Debug.LogWarning("[People3D] Route missing. Fallback common route used.");
            return points;
        }

        if (logRouteDebug) Debug.Log($"[People3D] Selected route={routeRoot.name} childCount={routeRoot.childCount}");
        for (int i = 0; i < routeRoot.childCount; i++)
        {
            Transform child = routeRoot.GetChild(i);
            if (child != null)
            {
                points.Add(child);
                if (logRouteDebug) Debug.Log($"[People3D] Route point {i}={child.name} pos={child.position}");
            }
        }

        if (points.Count == 0 && useRouteContainerAsPointWhenNoChildren)
        {
            points.Add(routeRoot);
            if (logRouteDebug) Debug.Log($"[People3D] {routeRoot.name} has no child points. Parent transform used as route point.");
        }

        if (points.Count == 0 && logRouteDebug)
        {
            Debug.LogWarning($"[People3D] {routeName} has no child points. Fallback common route used.");
        }

        if (reverse)
        {
            points.Reverse();
        }

        if (logRouteDebug) Debug.Log($"[People3D] {routeRoot.name} resolved points={points.Count}");
        return points;
    }

    private void MoveAlongRoute(Transform marker, IList<Transform> route, string reason, bool hideAfterMove, float moveSpeed, bool finalVisibleOn2D = true)
    {
        if (marker == null || route == null || route.Count == 0)
        {
            Debug.LogWarning("[People3D] Movement route is missing.");
            SetPerson2DVisible(marker, finalVisibleOn2D);
            return;
        }

        if (moveRoutines.TryGetValue(marker, out Coroutine previousRoutine) && previousRoutine != null)
        {
            StopCoroutine(previousRoutine);
        }

        moveRoutines[marker] = StartCoroutine(MoveAlongRouteRoutine(marker, route, reason, hideAfterMove, moveSpeed, finalVisibleOn2D));
    }

    private IEnumerator MoveAlongRouteRoutine(Transform marker, IList<Transform> route, string reason, bool hideAfterMove, float moveSpeed, bool finalVisibleOn2D)
    {
        bool barrierTriggered = false;
        PlayWalk(marker, reason);
        foreach (Transform point in route)
        {
            if (point == null)
            {
                continue;
            }

            yield return MoveToPoint(marker, point.position, moveSpeed);
            if (logDebug) Debug.Log($"[People3D] Waypoint {point.name} reached");

            if (!barrierTriggered && IsGatePoint(point))
            {
                barrierTriggered = true;
                if (waitAtGateSeconds > 0f)
                {
                    yield return new WaitForSeconds(waitAtGateSeconds);
                }

                barrierController?.OpenThenClose(reason);
                if (waitAfterBarrierOpenSeconds > 0f)
                {
                    yield return new WaitForSeconds(waitAfterBarrierOpenSeconds);
                }
            }
        }

        if (hideAfterMove)
        {
            marker.gameObject.SetActive(false);
        }

        Transform finalPoint = route[route.Count - 1];
        if (logDebug && finalPoint != null)
        {
            Debug.Log($"[People3D] Final point {finalPoint.name} reached");
        }

        PlayStand(marker, reason);
        moveRoutines.Remove(marker);
        SetPerson2DVisible(marker, finalVisibleOn2D);
    }

    public bool HasActive2DPersonMotion => moveRoutines.Count > 0;

    public bool TryGetPerson2DState(string markerName, out Transform marker, out bool visibleOn2D, out bool isMoving)
    {
        ResolveSceneReferences();
        marker = FindPersonMarkerByName(markerName);
        visibleOn2D = false;
        isMoving = false;
        if (marker == null)
        {
            return false;
        }

        visibleOn2D = marker.gameObject.activeInHierarchy &&
            visibleOn2DByMarker.TryGetValue(marker, out bool storedVisible) &&
            storedVisible;
        isMoving = moveRoutines.ContainsKey(marker);
        return true;
    }

    private void SetPerson2DVisible(Transform marker, bool visible)
    {
        if (marker == null)
        {
            return;
        }

        visibleOn2DByMarker[marker] = visible;
        Person2DStateChanged?.Invoke();
    }

    private Transform FindPersonMarkerByName(string markerName)
    {
        if (string.IsNullOrWhiteSpace(markerName))
        {
            return null;
        }

        Transform marker = FindInArrayByName(employeeMarkers, markerName);
        if (marker != null)
        {
            return marker;
        }

        return FindInArrayByName(visitorMarkers, markerName);
    }

    private static Transform FindInArrayByName(Transform[] markers, string markerName)
    {
        if (markers == null)
        {
            return null;
        }

        foreach (Transform marker in markers)
        {
            if (marker != null && marker.name == markerName)
            {
                return marker;
            }
        }

        return null;
    }

    private IEnumerator MoveToPoint(Transform marker, Vector3 targetPosition, float moveSpeed)
    {
        if (useMoveSpeedMode)
        {
            float safeSpeed = Mathf.Max(0.001f, moveSpeed);
            float safeArriveDistance = Mathf.Max(0.001f, waypointArriveDistance);
            while (Vector3.Distance(marker.position, targetPosition) > safeArriveDistance)
            {
                Vector3 direction = targetPosition - marker.position;
                FaceDirection(marker, direction);
                marker.position = Vector3.MoveTowards(marker.position, targetPosition, safeSpeed * Time.deltaTime);
                yield return null;
            }

            marker.position = targetPosition;
            yield break;
        }

        Vector3 start = marker.position;
        float safeDuration = Mathf.Max(0.001f, moveDuration);
        float elapsed = 0f;
        while (elapsed < safeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / safeDuration);
            marker.position = Vector3.Lerp(start, targetPosition, t);
            FaceDirection(marker, targetPosition - marker.position);
            yield return null;
        }

        marker.position = targetPosition;
    }

    private void FaceDirection(Transform marker, Vector3 direction)
    {
        if (!faceMoveDirection || marker == null)
        {
            return;
        }

        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        marker.rotation = Quaternion.RotateTowards(marker.rotation, targetRotation, rotationSpeedDeg * Time.deltaTime);
    }

    private static bool IsGatePoint(Transform point)
    {
        if (point == null)
        {
            return false;
        }

        string name = point.name;
        return name.Contains("GateFront") || name.Contains("StaffEntrance_GateFront") || name.Contains("Entrance") || name.Contains("Gate");
    }

    private int ResolveWorkerIndex(string workerId)
    {
        if (employeeMarkers == null || employeeMarkers.Length == 0) return -1;
        if (workerIndexById.TryGetValue(workerId, out int existingIndex)) return existingIndex;

        int numericIndex = ExtractOneBasedIndex(workerId) - 1;
        if (numericIndex >= 0 && numericIndex < employeeMarkers.Length)
        {
            workerIndexById[workerId] = numericIndex;
            return numericIndex;
        }

        int fallback = nextFallbackWorkerIndex % employeeMarkers.Length;
        nextFallbackWorkerIndex++;
        workerIndexById[workerId] = fallback;
        return fallback;
    }

    private int ResolveVisitorIndex(string visitorId)
    {
        if (visitorMarkers == null || visitorMarkers.Length == 0) return -1;
        if (visitorIndexById.TryGetValue(visitorId, out int existingIndex)) return existingIndex;

        int numericIndex = ExtractOneBasedIndex(visitorId) - 1;
        if (numericIndex >= 0 && numericIndex < visitorMarkers.Length)
        {
            visitorIndexById[visitorId] = numericIndex;
            return numericIndex;
        }

        int fallback = nextFallbackVisitorIndex % visitorMarkers.Length;
        nextFallbackVisitorIndex++;
        visitorIndexById[visitorId] = fallback;
        return fallback;
    }

    private static int ExtractOneBasedIndex(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return -1;
        }

        int dashIndex = id.LastIndexOf('-');
        string suffix = dashIndex >= 0 && dashIndex + 1 < id.Length ? id.Substring(dashIndex + 1) : id;
        return int.TryParse(suffix, out int result) ? result : -1;
    }

    private void ResolveSceneReferences()
    {
        employeeMarkers = EnsureArray(employeeMarkers, 5, "BoxHuman_");
        employeeInsidePositions = EnsureArray(employeeInsidePositions, 5, "EmployeeInside_");
        employeeExitPositions = EnsureArray(employeeExitPositions, 5, "EmployeeExit_");
        visitorMarkers = EnsureArray(visitorMarkers, 3, "BoxHuman_visitor_");
        visitorInsidePositions = EnsureArray(visitorInsidePositions, 3, "VisitorInside_");
        visitorExitPositions = EnsureArray(visitorExitPositions, 3, "VisitorExit_");
        workerRoutes = EnsureArray(workerRoutes, 5, "WorkerRoute_");
        visitorRoutes = EnsureArray(visitorRoutes, 3, "VisitorRoute_");

        staffEntranceOutside ??= FindSceneTransform("StaffEntrance_Outside");
        staffEntranceGateFront ??= FindSceneTransform("StaffEntrance_GateFront");
        staffEntranceInside ??= FindSceneTransform("StaffEntrance_Inside");
        workerRouteHub ??= FindSceneTransform("WorkerRouteHub");
        visitorRouteHub ??= FindSceneTransform("VisitorRouteHub");
        barrierController ??= FindBarrierController();

        if (!resolvedLogPrinted && logDebug)
        {
            resolvedLogPrinted = true;
            Debug.Log($"[People3D] resolved worker markers={CountNonNull(employeeMarkers)} visitor markers={CountNonNull(visitorMarkers)}");
            Debug.Log("[People3D] resolved personnel routes");
        }
    }

    private void PlayWalk(Transform marker, string reason)
    {
        if (!useWalkAnimation || marker == null)
        {
            return;
        }

        if (TryPlayAnimation(marker, walkClip, walkStateName))
        {
            if (logDebug) Debug.Log("[PeopleAnim] Walk");
        }
    }

    private void PlayStand(Transform marker, string reason)
    {
        if (!useWalkAnimation || marker == null)
        {
            return;
        }

        if (TryPlayAnimation(marker, standClip, standStateName))
        {
            if (logDebug) Debug.Log("[PeopleAnim] Stand");
        }
    }

    private bool TryPlayAnimation(Transform marker, AnimationClip clip, string stateName)
    {
        ResolveAnimationClipsIfNeeded();
        Animator animator = marker.GetComponentInChildren<Animator>();
        if (animator != null && !string.IsNullOrWhiteSpace(stateName))
        {
            if (animator.runtimeAnimatorController != null)
            {
                animator.CrossFade(stateName, 0.08f);
                return true;
            }

            if (logAnimationDebug) Debug.LogWarning($"[PeopleAnim] {marker.name} Animator has no RuntimeAnimatorController. Trying Animation fallback.");
        }

        if (clip == null)
        {
            if (logAnimationDebug) Debug.LogWarning($"[PeopleAnim] {marker.name} {(stateName == walkStateName ? "walkClip" : "standClip")} is null");
            return false;
        }

        Animation animation = marker.GetComponentInChildren<Animation>();
        if (animation == null)
        {
            animation = marker.gameObject.AddComponent<Animation>();
            if (logAnimationDebug) Debug.Log($"[PeopleAnim] {marker.name} Animation component added");
        }

        if (animation.GetClip(clip.name) == null)
        {
            animation.AddClip(clip, clip.name);
            if (logAnimationDebug) Debug.Log($"[PeopleAnim] {marker.name} {clip.name} clip assigned");
        }

        animation.clip = clip;
        bool played = animation.Play(clip.name);
        if (logAnimationDebug)
        {
            Debug.Log(played
                ? $"[PeopleAnim] {marker.name} Play {stateName}"
                : $"[PeopleAnim] {marker.name} animation playback failed, movement continues.");
        }

        return played;
    }

    private void ResolveAnimationClipsIfNeeded()
    {
#if UNITY_EDITOR
        walkClip ??= FindAnimationClipByName("BoxHuman@Walk");
        standClip ??= FindAnimationClipByName("BoxHuman@Stand");
#endif
    }

#if UNITY_EDITOR
    private static AnimationClip FindAnimationClipByName(string clipName)
    {
        string[] guids = AssetDatabase.FindAssets($"{clipName} t:AnimationClip");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip != null && clip.name == clipName)
            {
                return clip;
            }
        }

        return null;
    }
#endif

    private static Transform[] EnsureArray(Transform[] current, int count, string prefix)
    {
        bool needsResolve = current == null || current.Length < count;
        if (!needsResolve)
        {
            for (int i = 0; i < count; i++)
            {
                if (current[i] == null)
                {
                    needsResolve = true;
                    break;
                }
            }
        }

        if (!needsResolve)
        {
            return current;
        }

        Transform[] resolved = new Transform[count];
        for (int i = 0; i < count; i++)
        {
            resolved[i] = FindSceneTransform($"{prefix}{i + 1:00}");
            if (resolved[i] == null)
            {
                resolved[i] = FindSceneTransform($"{prefix}{i + 1}");
            }
        }

        return resolved;
    }

    private static int CountNonNull(Transform[] transforms)
    {
        if (transforms == null) return 0;
        int count = 0;
        foreach (Transform item in transforms)
        {
            if (item != null) count++;
        }

        return count;
    }

    private static Transform GetArrayItem(Transform[] items, int index)
    {
        if (items == null || index < 0 || index >= items.Length)
        {
            return null;
        }

        return items[index];
    }

    [ContextMenu("Test Employee 1 Check In")]
    private void TestEmployee1CheckIn() => SimulateEmployee(1, "check_in");

    [ContextMenu("Test Employee 2 Check In")]
    private void TestEmployee2CheckIn() => SimulateEmployee(2, "check_in");

    [ContextMenu("Test Employee 3 Check In")]
    private void TestEmployee3CheckIn() => SimulateEmployee(3, "check_in");

    [ContextMenu("Test Employee 4 Check In")]
    private void TestEmployee4CheckIn() => SimulateEmployee(4, "check_in");

    [ContextMenu("Test Employee 5 Check In")]
    private void TestEmployee5CheckIn() => SimulateEmployee(5, "check_in");

    [ContextMenu("Test Employee 1 Check Out")]
    private void TestEmployee1CheckOut() => SimulateEmployee(1, "check_out");

    [ContextMenu("Test Employee 2 Check Out")]
    private void TestEmployee2CheckOut() => SimulateEmployee(2, "check_out");

    [ContextMenu("Test Employee 3 Check Out")]
    private void TestEmployee3CheckOut() => SimulateEmployee(3, "check_out");

    [ContextMenu("Test Employee 4 Check Out")]
    private void TestEmployee4CheckOut() => SimulateEmployee(4, "check_out");

    [ContextMenu("Test Employee 5 Check Out")]
    private void TestEmployee5CheckOut() => SimulateEmployee(5, "check_out");

    [ContextMenu("Test All Employees Check In")]
    private void TestAllEmployeesCheckIn()
    {
        for (int i = 1; i <= 5; i++) SimulateEmployee(i, "check_in");
    }

    [ContextMenu("Test All Employees Check Out")]
    private void TestAllEmployeesCheckOut()
    {
        for (int i = 1; i <= 5; i++) SimulateEmployee(i, "check_out");
    }

    [ContextMenu("Test Visitor Entry")]
    private void TestVisitorEntry() => SimulateVisitor(1, "entry");

    [ContextMenu("Test Visitor Exit")]
    private void TestVisitorExit() => SimulateVisitor(1, "exit");

    [ContextMenu("Test Visitor 1 Entry")]
    private void TestVisitor1Entry() => SimulateVisitor(1, "entry");

    [ContextMenu("Test Visitor 2 Entry")]
    private void TestVisitor2Entry() => SimulateVisitor(2, "entry");

    [ContextMenu("Test Visitor 3 Entry")]
    private void TestVisitor3Entry() => SimulateVisitor(3, "entry");

    [ContextMenu("Test Visitor 1 Exit")]
    private void TestVisitor1Exit() => SimulateVisitor(1, "exit");

    [ContextMenu("Test Visitor 2 Exit")]
    private void TestVisitor2Exit() => SimulateVisitor(2, "exit");

    [ContextMenu("Test Visitor 3 Exit")]
    private void TestVisitor3Exit() => SimulateVisitor(3, "exit");

    [ContextMenu("Test All Visitors Entry")]
    private void TestAllVisitorsEntry()
    {
        for (int i = 1; i <= 3; i++) SimulateVisitor(i, "entry");
    }

    [ContextMenu("Test All Visitors Exit")]
    private void TestAllVisitorsExit()
    {
        for (int i = 1; i <= 3; i++) SimulateVisitor(i, "exit");
    }

    [ContextMenu("Test Worker 1 Route Preview")]
    private void TestWorker1RoutePreview()
    {
        ResolveSceneReferences();
        Transform marker = GetArrayItem(employeeMarkers, 0);
        MoveAlongRoute(marker, BuildWorkerCheckInRoute(0, GetArrayItem(employeeInsidePositions, 0)), "WORKER-001 route_preview", false, workerMoveSpeed);
    }

    [ContextMenu("Test Worker 2 Route Preview")]
    private void TestWorker2RoutePreview()
    {
        ResolveSceneReferences();
        Transform marker = GetArrayItem(employeeMarkers, 1);
        MoveAlongRoute(marker, BuildWorkerCheckInRoute(1, GetArrayItem(employeeInsidePositions, 1)), "WORKER-002 route_preview", false, workerMoveSpeed);
    }

    [ContextMenu("Test Visitor 1 Route Preview")]
    private void TestVisitor1RoutePreview()
    {
        ResolveSceneReferences();
        Transform marker = GetArrayItem(visitorMarkers, 0);
        MoveAlongRoute(marker, BuildVisitorEntryRoute(0, GetArrayItem(visitorInsidePositions, 0)), "VISITOR-001 route_preview", false, visitorMoveSpeed);
    }

    [ContextMenu("Reset Personnel Test State")]
    private void ResetPersonnelTestState()
    {
        workerIndexById.Clear();
        visitorIndexById.Clear();
        nextFallbackWorkerIndex = 0;
        nextFallbackVisitorIndex = 0;
        scr_ControlTowerUIManager uiManager = FindUiManager();
        if (uiManager != null)
        {
            uiManager.ResetPersonnelStatusForTest();
        }
    }

    private void SimulateEmployee(int employeeNumber, string actionType)
    {
        string id = $"WORKER-{employeeNumber:000}";
        scr_ControlTowerUIManager uiManager = FindUiManager();
        if (uiManager != null)
        {
            uiManager.SimulateEmployeeAttendance(id, $"Worker {employeeNumber}", actionType);
            return;
        }

        ApplyEmployeeAttendance(id, $"Worker {employeeNumber}", actionType);
    }

    private void SimulateVisitor(int visitorNumber, string actionType)
    {
        string id = $"VISITOR-{visitorNumber:000}";
        scr_ControlTowerUIManager uiManager = FindUiManager();
        if (uiManager != null)
        {
            uiManager.SimulateVisitorAttendance(id, $"Visitor {visitorNumber}", actionType);
            return;
        }

        ApplyVisitorAttendance(id, $"Visitor {visitorNumber}", actionType);
    }

    private static Transform FindSceneTransform(string objectName)
    {
        foreach (GameObject item in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (item.name == objectName && item.scene.IsValid()) return item.transform;
        }

        return null;
    }

    private static scr_StaffEntranceBarrierController FindBarrierController()
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindFirstObjectByType<scr_StaffEntranceBarrierController>();
#else
        return Object.FindObjectOfType<scr_StaffEntranceBarrierController>();
#endif
    }

    private static scr_ControlTowerUIManager FindUiManager()
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindFirstObjectByType<scr_ControlTowerUIManager>();
#else
        return Object.FindObjectOfType<scr_ControlTowerUIManager>();
#endif
    }
}

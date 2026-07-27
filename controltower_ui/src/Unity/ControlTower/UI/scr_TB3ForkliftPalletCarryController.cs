using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class scr_TB3ForkliftPalletCarryController : MonoBehaviour
{
    private static readonly HashSet<int> ClaimedPalletInstanceIds = new();
    private static readonly HashSet<int> WarnedChildRigidbodyInstanceIds = new();
    private static readonly WaitForFixedUpdate FixedUpdateWait = new();
    private const int ReleaseCollisionRestoreFixedFrames = 2;

    [SerializeField] private Transform palletCarryPoint;
    [SerializeField] private Transform carriageLift;
    [SerializeField] private Transform palletGroupRoot;
    [SerializeField] private scr_TB3ForkliftRuntimeController liftController;
    [SerializeField] private bool autoPickupOnLiftUp = true;
    [SerializeField] private bool autoReleaseAtBottom = true;
    [SerializeField] private float attachAlignSeconds = 0.15f;
    [SerializeField] private float liftMovementEpsilon = 0.0005f;
    [SerializeField] private float releaseBottomTolerance = 0.01f;
    [SerializeField] private float releaseArmHeight = 0.05f;
    [SerializeField] private float maximumPickupDistance = 1f;
    [SerializeField] private bool preservePickupRelativePose = true;
    [SerializeField] private Vector3 carryLocalPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 carryLocalEulerOffset = Vector3.zero;
    [SerializeField] private bool enableDebugLogs;

    private readonly List<Rigidbody> detectedPallets = new();
    private Rigidbody pickupCandidate;
    private Rigidbody carriedPallet;
    private Rigidbody releasingPallet;
    private Transform carriedOriginalParent;
    private Vector3 carriedOriginalLocalScale;
    private Vector3 pickupWorldPosition;
    private Quaternion pickupWorldRotation;
    private Vector3 pickupRelativePosition;
    private Quaternion pickupRelativeRotation;
    private RigidbodyInterpolation carriedOriginalInterpolation;
    private CollisionDetectionMode carriedOriginalCollisionDetectionMode;
    private bool carriedOriginalDetectCollisions;
    private bool carriedOriginalIsKinematic;
    private bool carriedOriginalUseGravity;
    private bool carriedPhysicsStateCached;
    private readonly List<Collider> carriedColliders = new();
    private readonly List<bool> carriedColliderEnabledStates = new();
    private readonly List<Rigidbody> rigidbodyInspectionBuffer = new();
    private bool hasLiftedSincePickup;
    private Coroutine attachAlignRoutine;
    private Coroutine releaseRestoreRoutine;
    private float lastLiftHeight;
    private bool hasLastLiftHeight;
    private bool pickupParentFailureWarningShown;
    private int lastLoggedPickupAttemptInstanceId;
    private int lastLoggedCarriedExitInstanceId;

    public bool HasPickupCandidate => pickupCandidate != null;
    public bool IsCarryingPallet => carriedPallet != null;
    public string CandidatePalletName => pickupCandidate != null ? pickupCandidate.name : string.Empty;
    public string CarriedPalletName => carriedPallet != null ? carriedPallet.name : string.Empty;

    private void Awake()
    {
        Collider sensor = GetComponent<Collider>();
        if (sensor != null && !sensor.isTrigger)
        {
            Debug.LogWarning("[TB3-03 Pallet] ForkPickupSensor collider must be a trigger.", this);
        }
    }

    private void Update()
    {
        if (liftController == null)
        {
            return;
        }

        float liftHeight = liftController.CurrentLiftHeight;
        bool movingUp = liftController.IsLiftMovingUp ||
                        (hasLastLiftHeight && liftHeight - lastLiftHeight > liftMovementEpsilon);
        bool movingDown = liftController.IsLiftMovingDown ||
                          (hasLastLiftHeight && lastLiftHeight - liftHeight > liftMovementEpsilon);
        hasLastLiftHeight = true;
        lastLiftHeight = liftHeight;

        if (!IsCarryingPallet)
        {
            if (autoPickupOnLiftUp && movingUp && HasPickupCandidate)
            {
                TryPickupCurrentCandidate();
            }

            return;
        }

        if (liftHeight >= releaseArmHeight)
        {
            hasLiftedSincePickup = true;
        }

        bool atBottom = liftController.IsLiftAtBottom || liftHeight <= releaseBottomTolerance;
        if (autoReleaseAtBottom && hasLiftedSincePickup && movingDown && atBottom)
        {
            ReleaseCarriedPallet();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TrackPallet(other, true);
    }

    private void OnTriggerStay(Collider other)
    {
        TrackPallet(other, true);
    }

    private void OnTriggerExit(Collider other)
    {
        Rigidbody pallet = other != null ? other.attachedRigidbody : null;
        if (pallet != null && pallet == carriedPallet)
        {
            int palletInstanceId = pallet.GetInstanceID();
            if (lastLoggedCarriedExitInstanceId != palletInstanceId)
            {
                lastLoggedCarriedExitInstanceId = palletInstanceId;
                Log($"candidate trigger exit: {pallet.name}, carrying retained=true");
            }

            return;
        }

        TrackPallet(other, false);
    }

    public bool TryPickupCurrentCandidate()
    {
        if (pickupCandidate != null && lastLoggedPickupAttemptInstanceId != pickupCandidate.GetInstanceID())
        {
            lastLoggedPickupAttemptInstanceId = pickupCandidate.GetInstanceID();
            Log(
                $"pickup attempt: candidate={pickupCandidate.name}, " +
                $"hasCandidate={HasPickupCandidate}, isCarrying={IsCarryingPallet}");
        }

        if (IsCarryingPallet || releaseRestoreRoutine != null || releasingPallet != null ||
            pickupCandidate == null || palletCarryPoint == null)
        {
            return false;
        }

        if (!IsValidPallet(pickupCandidate) ||
            Vector3.Distance(pickupCandidate.worldCenterOfMass, palletCarryPoint.position) > Mathf.Max(0f, maximumPickupDistance))
        {
            RefreshPickupCandidate();
            return false;
        }

        carriedPallet = pickupCandidate;
        carriedOriginalParent = carriedPallet.transform.parent;
        carriedOriginalLocalScale = carriedPallet.transform.localScale;
        pickupWorldPosition = carriedPallet.transform.position;
        pickupWorldRotation = carriedPallet.transform.rotation;
        hasLiftedSincePickup = liftController != null && liftController.CurrentLiftHeight >= releaseArmHeight;

        CacheCarriedPhysicsState(carriedPallet);
        PreparePalletForCarry(carriedPallet);
        carriedPallet.transform.SetParent(palletCarryPoint, true);
        if (carriedPallet.transform.parent != palletCarryPoint)
        {
            if (!pickupParentFailureWarningShown)
            {
                pickupParentFailureWarningShown = true;
                Debug.LogWarning(
                    "[TB3-03 팔레트] PalletCarryPoint 연결에 실패하여 Pickup 상태를 복원했습니다.",
                    this);
            }

            RollbackFailedPickup();
            return false;
        }

        pickupRelativePosition = carriedPallet.transform.localPosition;
        pickupRelativeRotation = carriedPallet.transform.localRotation;
        ClaimedPalletInstanceIds.Add(carriedPallet.GetInstanceID());
        Log($"pallet parent attached: {carriedPallet.name} -> {palletCarryPoint.name}");

        if (attachAlignRoutine != null)
        {
            StopCoroutine(attachAlignRoutine);
            attachAlignRoutine = null;
        }

        Vector3 alignTargetPosition = preservePickupRelativePose
            ? pickupRelativePosition
            : carryLocalPositionOffset;
        Quaternion alignTargetRotation = preservePickupRelativePose
            ? pickupRelativeRotation
            : Quaternion.Euler(carryLocalEulerOffset);

        if (!preservePickupRelativePose)
        {
            attachAlignRoutine = StartCoroutine(AlignCarriedPalletRoutine(
                carriedPallet.transform,
                alignTargetPosition,
                alignTargetRotation));
        }

        Log(
            $"pickup pose: {carriedPallet.name} " +
            $"worldPosition={pickupWorldPosition} worldRotation={pickupWorldRotation.eulerAngles} " +
            $"localPosition={pickupRelativePosition} localRotation={pickupRelativeRotation.eulerAngles} " +
            $"alignTargetPosition={alignTargetPosition} alignTargetRotation={alignTargetRotation.eulerAngles} " +
            $"preservePickupRelativePose={preservePickupRelativePose}");
        Log($"pickup success: carried={carriedPallet.name}, parent={carriedPallet.transform.parent.name}");
        lastLoggedCarriedExitInstanceId = 0;
        detectedPallets.Remove(carriedPallet);
        pickupCandidate = null;
        return true;
    }

    private void RollbackFailedPickup()
    {
        Rigidbody failedPallet = carriedPallet;
        if (failedPallet != null)
        {
            Transform failedTransform = failedPallet.transform;
            failedTransform.SetParent(carriedOriginalParent, true);
            failedTransform.SetPositionAndRotation(pickupWorldPosition, pickupWorldRotation);
            failedTransform.localScale = carriedOriginalLocalScale;
            RestoreCachedPalletPhysicsState(failedPallet);
        }

        carriedPallet = null;
        carriedOriginalParent = null;
        hasLiftedSincePickup = false;
        ClearCarriedPhysicsStateCache();
        RefreshPickupCandidate();
    }

    public bool ReleaseCarriedPallet()
    {
        if (!IsCarryingPallet || releaseRestoreRoutine != null || releasingPallet != null)
        {
            return false;
        }

        if (attachAlignRoutine != null)
        {
            StopCoroutine(attachAlignRoutine);
            attachAlignRoutine = null;
        }

        Rigidbody palletToRelease = carriedPallet;
        Transform palletTransform = palletToRelease.transform;
        Vector3 worldPosition = palletTransform.position;
        Quaternion worldRotation = palletTransform.rotation;
        Transform restoreParent = carriedOriginalParent != null ? carriedOriginalParent : palletGroupRoot;
        palletTransform.SetParent(restoreParent, true);
        palletTransform.SetPositionAndRotation(worldPosition, worldRotation);
        palletTransform.localScale = carriedOriginalLocalScale;
        PreparePalletForReleaseDelay(palletToRelease);

        string releasedName = palletToRelease.name;
        Log($"release parent restored: {releasedName} -> {(restoreParent != null ? restoreParent.name : "<root>")}");
        releasingPallet = palletToRelease;
        carriedPallet = null;
        carriedOriginalParent = null;
        hasLiftedSincePickup = false;
        releaseRestoreRoutine = StartCoroutine(RestoreReleasedPalletPhysicsRoutine(
            palletToRelease,
            palletToRelease.GetInstanceID(),
            releasedName));
        return true;
    }

    private void CacheCarriedPhysicsState(Rigidbody pallet)
    {
        carriedOriginalInterpolation = pallet.interpolation;
        carriedOriginalCollisionDetectionMode = pallet.collisionDetectionMode;
        carriedOriginalDetectCollisions = pallet.detectCollisions;
        carriedOriginalIsKinematic = pallet.isKinematic;
        carriedOriginalUseGravity = pallet.useGravity;
        carriedPhysicsStateCached = true;

        carriedColliders.Clear();
        carriedColliderEnabledStates.Clear();
        pallet.GetComponentsInChildren(true, carriedColliders);
        for (int index = 0; index < carriedColliders.Count; index++)
        {
            Collider palletCollider = carriedColliders[index];
            carriedColliderEnabledStates.Add(palletCollider != null && palletCollider.enabled);
        }

        InspectChildRigidbodies(pallet);
    }

    private void PreparePalletForCarry(Rigidbody pallet)
    {
        pallet.linearVelocity = Vector3.zero;
        pallet.angularVelocity = Vector3.zero;
        pallet.detectCollisions = false;
        pallet.interpolation = RigidbodyInterpolation.None;
        pallet.collisionDetectionMode = CollisionDetectionMode.Discrete;
        pallet.isKinematic = true;
        pallet.useGravity = false;
        SetCachedPalletCollidersEnabled(false);
        Log($"carry collisions disabled: {pallet.name}");
    }

    private void PreparePalletForReleaseDelay(Rigidbody pallet)
    {
        pallet.linearVelocity = Vector3.zero;
        pallet.angularVelocity = Vector3.zero;
        pallet.detectCollisions = false;
        pallet.interpolation = RigidbodyInterpolation.None;
        pallet.collisionDetectionMode = CollisionDetectionMode.Discrete;
        pallet.isKinematic = true;
        pallet.useGravity = false;
        SetCachedPalletCollidersEnabled(false);
    }

    private IEnumerator RestoreReleasedPalletPhysicsRoutine(
        Rigidbody pallet,
        int palletInstanceId,
        string palletName)
    {
        for (int frame = 0; frame < ReleaseCollisionRestoreFixedFrames; frame++)
        {
            yield return FixedUpdateWait;
        }

        CompleteReleasedPalletPhysicsRestore(pallet, palletInstanceId, palletName);
    }

    private void CompleteReleasedPalletPhysicsRestore(
        Rigidbody pallet,
        int palletInstanceId,
        string palletName)
    {
        if (pallet != null)
        {
            pallet.linearVelocity = Vector3.zero;
            pallet.angularVelocity = Vector3.zero;
            RestoreCachedPalletPhysicsState(pallet);
        }

        ClaimedPalletInstanceIds.Remove(palletInstanceId);
        releasingPallet = null;
        releaseRestoreRoutine = null;
        lastLoggedPickupAttemptInstanceId = 0;
        lastLoggedCarriedExitInstanceId = 0;
        ClearCarriedPhysicsStateCache();
        RefreshPickupCandidate();
        Log($"release collisions restored: {palletName}");
    }

    private void RestoreCachedPalletPhysicsState(Rigidbody pallet)
    {
        if (pallet == null || !carriedPhysicsStateCached)
        {
            return;
        }

        pallet.detectCollisions = false;
        pallet.useGravity = false;
        pallet.isKinematic = carriedOriginalIsKinematic;
        pallet.collisionDetectionMode = carriedOriginalCollisionDetectionMode;
        pallet.interpolation = carriedOriginalInterpolation;
        pallet.useGravity = carriedOriginalUseGravity;
        pallet.detectCollisions = carriedOriginalDetectCollisions;
        RestoreCachedPalletColliderStates();
    }

    private void SetCachedPalletCollidersEnabled(bool enabled)
    {
        for (int index = 0; index < carriedColliders.Count; index++)
        {
            Collider palletCollider = carriedColliders[index];
            if (palletCollider != null)
            {
                palletCollider.enabled = enabled;
            }
        }
    }

    private void RestoreCachedPalletColliderStates()
    {
        int restoreCount = Mathf.Min(carriedColliders.Count, carriedColliderEnabledStates.Count);
        for (int index = 0; index < restoreCount; index++)
        {
            Collider palletCollider = carriedColliders[index];
            if (palletCollider != null)
            {
                palletCollider.enabled = carriedColliderEnabledStates[index];
            }
        }
    }

    private void ClearCarriedPhysicsStateCache()
    {
        carriedPhysicsStateCached = false;
        carriedColliders.Clear();
        carriedColliderEnabledStates.Clear();
    }

    private void InspectChildRigidbodies(Rigidbody pallet)
    {
        rigidbodyInspectionBuffer.Clear();
        pallet.GetComponentsInChildren(true, rigidbodyInspectionBuffer);
        for (int index = 0; index < rigidbodyInspectionBuffer.Count; index++)
        {
            Rigidbody childRigidbody = rigidbodyInspectionBuffer[index];
            if (childRigidbody == null || childRigidbody == pallet ||
                !WarnedChildRigidbodyInstanceIds.Add(childRigidbody.GetInstanceID()))
            {
                continue;
            }

            Debug.LogWarning(
                $"[TB3-03 Pallet] {pallet.name} has a separate child Rigidbody: {childRigidbody.name}",
                childRigidbody);
        }

        rigidbodyInspectionBuffer.Clear();
    }

    private void OnDisable()
    {
        if (releaseRestoreRoutine == null || releasingPallet == null)
        {
            return;
        }

        Rigidbody pallet = releasingPallet;
        int palletInstanceId = pallet.GetInstanceID();
        string palletName = pallet.name;
        StopCoroutine(releaseRestoreRoutine);
        releaseRestoreRoutine = null;
        CompleteReleasedPalletPhysicsRestore(pallet, palletInstanceId, palletName);
    }

    private IEnumerator AlignCarriedPalletRoutine(
        Transform palletTransform,
        Vector3 targetPosition,
        Quaternion targetRotation)
    {
        Vector3 startPosition = palletTransform.localPosition;
        Quaternion startRotation = palletTransform.localRotation;
        float duration = Mathf.Max(0f, attachAlignSeconds);
        if (duration <= 0f)
        {
            palletTransform.SetLocalPositionAndRotation(targetPosition, targetRotation);
            attachAlignRoutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (palletTransform != null && palletTransform == (carriedPallet != null ? carriedPallet.transform : null) && elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            palletTransform.SetLocalPositionAndRotation(
                Vector3.Lerp(startPosition, targetPosition, t),
                Quaternion.Slerp(startRotation, targetRotation, t));
            yield return null;
        }

        if (palletTransform != null && carriedPallet != null && palletTransform == carriedPallet.transform)
        {
            palletTransform.SetLocalPositionAndRotation(targetPosition, targetRotation);
        }

        attachAlignRoutine = null;
    }

    private void TrackPallet(Collider other, bool entered)
    {
        Rigidbody pallet = other != null ? other.attachedRigidbody : null;
        if (!IsValidPallet(pallet))
        {
            return;
        }

        if (entered)
        {
            if (!detectedPallets.Contains(pallet) && pallet != carriedPallet)
            {
                detectedPallets.Add(pallet);
                RefreshPickupCandidate();
                if (autoPickupOnLiftUp && liftController != null && liftController.IsLiftMovingUp)
                {
                    TryPickupCurrentCandidate();
                }
            }
        }
        else if (pallet != carriedPallet && detectedPallets.Remove(pallet))
        {
            RefreshPickupCandidate();
        }
    }

    private bool IsValidPallet(Rigidbody pallet)
    {
        if (pallet == null || pallet == carriedPallet || palletGroupRoot == null ||
            ClaimedPalletInstanceIds.Contains(pallet.GetInstanceID()))
        {
            return false;
        }

        Transform palletTransform = pallet.transform;
        return palletTransform.name.StartsWith("Pallet_Cargo_", System.StringComparison.Ordinal) &&
               palletTransform.IsChildOf(palletGroupRoot);
    }

    private void RefreshPickupCandidate()
    {
        pickupCandidate = null;
        if (palletCarryPoint == null || IsCarryingPallet)
        {
            return;
        }

        float nearestDistanceSquared = float.PositiveInfinity;
        for (int index = detectedPallets.Count - 1; index >= 0; index--)
        {
            Rigidbody pallet = detectedPallets[index];
            if (!IsValidPallet(pallet))
            {
                detectedPallets.RemoveAt(index);
                continue;
            }

            float distanceSquared = (pallet.worldCenterOfMass - palletCarryPoint.position).sqrMagnitude;
            if (distanceSquared <= Mathf.Max(0f, maximumPickupDistance) * Mathf.Max(0f, maximumPickupDistance) &&
                distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                pickupCandidate = pallet;
            }
        }

        if (pickupCandidate != null)
        {
            Log($"pallet pickup candidate: {pickupCandidate.name}");
        }
    }

    private void Log(string message)
    {
        if (enableDebugLogs)
        {
            Debug.Log($"[TB3-03 Pallet] {message}", this);
        }
    }
}

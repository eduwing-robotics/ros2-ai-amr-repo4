using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class PalletDropSlot
{
    public string slotId;
    public Transform dropTarget;
    [Min(0f)] public float horizontalDropRadius = 0.35f;
}

[DisallowMultipleComponent]
public sealed class scr_TB3PalletCarryController : MonoBehaviour
{
    public enum PalletCarryState
    {
        Grounded,
        Attaching,
        Carried,
        Dropping,
        Placed
    }

    [Header("Scene References")]
    [SerializeField] private Transform forkliftRobotRoot;
    [SerializeField] private Transform carriageLift;
    [SerializeField] private Transform carryPoint;
    [SerializeField] private Transform palletRoot;
    [SerializeField] private Transform pickupTarget;
    [SerializeField] private PalletDropSlot[] dropSlots = Array.Empty<PalletDropSlot>();
    [SerializeField] private scr_Factory2DPalletMarkerController palletMarkerController;
    [SerializeField] private scr_TB3ForkliftRuntimeController liftController;

    [Header("Pickup / Drop")]
    [SerializeField, Min(0f)] private float pickupHorizontalRadius = 0.25f;
    [SerializeField] private float attachLiftThreshold = 0.05f;
    [SerializeField] private float releaseLiftThreshold = 0.02f;
    [SerializeField, Min(0f)] private float attachDuration = 0.25f;
    [SerializeField, Min(0f)] private float dropDuration = 0.35f;
    [SerializeField] private AnimationCurve attachCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve dropCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private Vector3 carryPositionOffset;
    [SerializeField] private Vector3 carryRotationOffset;
    [SerializeField] private bool resetOnEnable = true;
    [SerializeField] private bool logDebug = true;

    private readonly List<RigidbodyState> rigidbodyStates = new();
    private readonly List<Rigidbody> palletRigidbodies = new();

    private PalletCarryState state = PalletCarryState.Grounded;
    private Coroutine transitionRoutine;
    private PalletDropSlot placedSlot;
    private Vector3 initialWorldPosition;
    private Quaternion initialWorldRotation;
    private Vector3 initialWorldScale;
    private Vector3 initialCarriageLocalPosition;
    private float previousLiftHeight;
    private bool initialPoseCaptured;
    private bool hasLiftSample;
    private bool missingReferenceWarningShown;
    private bool pickupDistanceRejectedLogged;
    private bool dropSlotRejectedLogged;

    public PalletCarryState State => state;
    public Vector3 InitialWorldScale => initialWorldScale;

    private readonly struct RigidbodyState
    {
        public RigidbodyState(Rigidbody body)
        {
            Body = body;
            UseGravity = body.useGravity;
            IsKinematic = body.isKinematic;
        }

        public Rigidbody Body { get; }
        public bool UseGravity { get; }
        public bool IsKinematic { get; }
    }

    private void Awake()
    {
        CaptureInitialPose();
        CachePalletRigidbodies();
    }

    private void OnEnable()
    {
        CaptureInitialPose();
        CachePalletRigidbodies();
        ApplyControlledRigidbodyState();
        ResetLiftSample();

        if (resetOnEnable)
        {
            ResetPalletToPickup();
        }
        else
        {
            SetAllSlotIcons(false);
        }

        ValidateReferencesOnce();
    }

    private void OnDisable()
    {
        StopTransition();
        RestoreRigidbodyState();
    }

    private void Update()
    {
        if (!HasCarryReferences())
        {
            return;
        }

        float currentLiftHeight = ReadLiftHeight();
        if (!hasLiftSample)
        {
            previousLiftHeight = currentLiftHeight;
            hasLiftSample = true;
            return;
        }

        bool crossedAttachThreshold =
            previousLiftHeight < attachLiftThreshold &&
            currentLiftHeight >= attachLiftThreshold &&
            currentLiftHeight > previousLiftHeight;

        bool crossedReleaseThreshold =
            previousLiftHeight > releaseLiftThreshold &&
            currentLiftHeight <= releaseLiftThreshold &&
            currentLiftHeight < previousLiftHeight;

        if (currentLiftHeight < attachLiftThreshold)
        {
            pickupDistanceRejectedLogged = false;
        }

        if (currentLiftHeight > releaseLiftThreshold)
        {
            dropSlotRejectedLogged = false;
        }

        if ((state == PalletCarryState.Grounded || state == PalletCarryState.Placed) &&
            crossedAttachThreshold)
        {
            TryBeginPickup();
        }
        else if (state == PalletCarryState.Carried && crossedReleaseThreshold)
        {
            TryBeginDrop();
        }

        previousLiftHeight = currentLiftHeight;
    }

    private void LateUpdate()
    {
        if (state != PalletCarryState.Carried || palletRoot == null || carryPoint == null)
        {
            return;
        }

        GetCarryWorldPose(out Vector3 targetPosition, out Quaternion targetRotation);
        palletRoot.SetPositionAndRotation(targetPosition, targetRotation);
    }

    private void FixedUpdate()
    {
        for (int index = 0; index < palletRigidbodies.Count; index++)
        {
            Rigidbody body = palletRigidbodies[index];
            if (body == null)
            {
                continue;
            }

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    public void ResetPalletToPickup()
    {
        StopTransition();
        placedSlot = null;
        state = PalletCarryState.Grounded;

        if (palletRoot != null && initialPoseCaptured)
        {
            Vector3 targetPosition = pickupTarget != null ? pickupTarget.position : initialWorldPosition;
            Quaternion targetRotation = pickupTarget != null ? pickupTarget.rotation : initialWorldRotation;
            palletRoot.SetPositionAndRotation(targetPosition, targetRotation);
        }

        ApplyControlledRigidbodyState();
        SetAllSlotIcons(false);
        pickupDistanceRejectedLogged = false;
        dropSlotRejectedLogged = false;
        ResetLiftSample();
    }

    private void TryBeginPickup()
    {
        GetCarryWorldPose(out Vector3 targetPosition, out _);
        float distance = HorizontalDistance(palletRoot.position, targetPosition);
        if (distance > Mathf.Max(0f, pickupHorizontalRadius))
        {
            if (logDebug && !pickupDistanceRejectedLogged)
            {
                pickupDistanceRejectedLogged = true;
                Debug.Log($"[PalletCarry] Pickup rejected distance={distance:F3}", this);
            }

            return;
        }

        PalletCarryState previousState = state;
        string previousSlotId = placedSlot != null ? placedSlot.slotId : string.Empty;
        if (placedSlot != null)
        {
            SetSlotOccupied(placedSlot, false);
            placedSlot = null;
        }

        SetAllSlotIcons(false);
        SetState(PalletCarryState.Attaching, previousState == PalletCarryState.Placed ? previousSlotId : null);
        transitionRoutine = StartCoroutine(AttachRoutine());
    }

    private void TryBeginDrop()
    {
        PalletDropSlot nearestSlot = FindNearestDropSlot();
        if (nearestSlot == null)
        {
            if (logDebug && !dropSlotRejectedLogged)
            {
                dropSlotRejectedLogged = true;
                Debug.Log("[PalletCarry] Drop rejected no valid slot", this);
            }

            return;
        }

        SetAllSlotIcons(false);
        SetState(PalletCarryState.Dropping, nearestSlot.slotId);
        transitionRoutine = StartCoroutine(DropRoutine(nearestSlot));
    }

    private IEnumerator AttachRoutine()
    {
        Vector3 startPosition = palletRoot.position;
        Quaternion startRotation = palletRoot.rotation;
        float duration = Mathf.Max(0f, attachDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float normalizedTime = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
            float curveTime = EvaluateCurve(attachCurve, normalizedTime);
            GetCarryWorldPose(out Vector3 targetPosition, out Quaternion targetRotation);
            palletRoot.SetPositionAndRotation(
                Vector3.LerpUnclamped(startPosition, targetPosition, curveTime),
                Quaternion.SlerpUnclamped(startRotation, targetRotation, curveTime));
            yield return null;
        }

        GetCarryWorldPose(out Vector3 finalPosition, out Quaternion finalRotation);
        palletRoot.SetPositionAndRotation(finalPosition, finalRotation);
        transitionRoutine = null;
        SetState(PalletCarryState.Carried);
    }

    private IEnumerator DropRoutine(PalletDropSlot slot)
    {
        Vector3 startPosition = palletRoot.position;
        Quaternion startRotation = palletRoot.rotation;
        Vector3 targetPosition = slot.dropTarget.position;
        Quaternion targetRotation = slot.dropTarget.rotation;
        float duration = Mathf.Max(0f, dropDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float normalizedTime = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
            float curveTime = EvaluateCurve(dropCurve, normalizedTime);
            palletRoot.SetPositionAndRotation(
                Vector3.LerpUnclamped(startPosition, targetPosition, curveTime),
                Quaternion.SlerpUnclamped(startRotation, targetRotation, curveTime));
            yield return null;
        }

        palletRoot.SetPositionAndRotation(targetPosition, targetRotation);
        placedSlot = slot;
        transitionRoutine = null;
        SetSlotOccupied(slot, true);
        SetState(PalletCarryState.Placed, slot.slotId);
    }

    private PalletDropSlot FindNearestDropSlot()
    {
        float nearestDistance = float.PositiveInfinity;
        PalletDropSlot nearestSlot = null;
        Vector3 dropOrigin = carryPoint != null
            ? carryPoint.position
            : forkliftRobotRoot != null ? forkliftRobotRoot.position : palletRoot.position;

        if (dropSlots == null)
        {
            return null;
        }

        for (int index = 0; index < dropSlots.Length; index++)
        {
            PalletDropSlot slot = dropSlots[index];
            if (slot == null || slot.dropTarget == null || string.IsNullOrWhiteSpace(slot.slotId))
            {
                continue;
            }

            float distance = HorizontalDistance(dropOrigin, slot.dropTarget.position);
            if (distance <= Mathf.Max(0f, slot.horizontalDropRadius) && distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestSlot = slot;
            }
        }

        return nearestSlot;
    }

    private void GetCarryWorldPose(out Vector3 position, out Quaternion rotation)
    {
        rotation = carryPoint.rotation * Quaternion.Euler(carryRotationOffset);
        position = carryPoint.position + carryPoint.rotation * carryPositionOffset;
    }

    private float ReadLiftHeight()
    {
        if (liftController != null)
        {
            return liftController.CurrentLiftHeight;
        }

        return carriageLift != null
            ? carriageLift.localPosition.z - initialCarriageLocalPosition.z
            : 0f;
    }

    private void CaptureInitialPose()
    {
        if (!initialPoseCaptured && palletRoot != null)
        {
            initialWorldPosition = palletRoot.position;
            initialWorldRotation = palletRoot.rotation;
            initialWorldScale = palletRoot.lossyScale;
            initialPoseCaptured = true;
        }

        if (carriageLift != null)
        {
            initialCarriageLocalPosition = carriageLift.localPosition;
        }
    }

    private void CachePalletRigidbodies()
    {
        if (palletRoot == null || rigidbodyStates.Count > 0)
        {
            return;
        }

        palletRigidbodies.Clear();
        palletRoot.GetComponentsInChildren(true, palletRigidbodies);
        for (int index = 0; index < palletRigidbodies.Count; index++)
        {
            Rigidbody body = palletRigidbodies[index];
            if (body != null)
            {
                rigidbodyStates.Add(new RigidbodyState(body));
            }
        }
    }

    private void ApplyControlledRigidbodyState()
    {
        for (int index = 0; index < palletRigidbodies.Count; index++)
        {
            Rigidbody body = palletRigidbodies[index];
            if (body == null)
            {
                continue;
            }

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.isKinematic = true;
        }
    }

    private void RestoreRigidbodyState()
    {
        for (int index = 0; index < rigidbodyStates.Count; index++)
        {
            RigidbodyState savedState = rigidbodyStates[index];
            if (savedState.Body == null)
            {
                continue;
            }

            savedState.Body.linearVelocity = Vector3.zero;
            savedState.Body.angularVelocity = Vector3.zero;
            savedState.Body.isKinematic = savedState.IsKinematic;
            savedState.Body.useGravity = savedState.UseGravity;
        }
    }

    private void SetAllSlotIcons(bool occupied)
    {
        if (dropSlots == null || palletMarkerController == null)
        {
            return;
        }

        for (int index = 0; index < dropSlots.Length; index++)
        {
            SetSlotOccupied(dropSlots[index], occupied);
        }
    }

    private void SetSlotOccupied(PalletDropSlot slot, bool occupied)
    {
        if (slot == null || palletMarkerController == null || string.IsNullOrWhiteSpace(slot.slotId))
        {
            return;
        }

        palletMarkerController.ApplyPalletSlotState(slot.slotId, occupied);
    }

    private void SetState(PalletCarryState nextState, string slotId = null)
    {
        if (state == nextState)
        {
            return;
        }

        PalletCarryState previousState = state;
        state = nextState;
        if (!logDebug)
        {
            return;
        }

        string slotSuffix = string.IsNullOrWhiteSpace(slotId) ? string.Empty : $" slot={slotId}";
        Debug.Log($"[PalletCarry] {previousState} -> {nextState}{slotSuffix}", this);
    }

    private void StopTransition()
    {
        if (transitionRoutine == null)
        {
            return;
        }

        StopCoroutine(transitionRoutine);
        transitionRoutine = null;
    }

    private void ResetLiftSample()
    {
        previousLiftHeight = ReadLiftHeight();
        hasLiftSample = true;
    }

    private bool HasCarryReferences()
    {
        return palletRoot != null && carryPoint != null && carriageLift != null;
    }

    private void ValidateReferencesOnce()
    {
        if (missingReferenceWarningShown)
        {
            return;
        }

        List<string> missing = new();
        if (forkliftRobotRoot == null) missing.Add(nameof(forkliftRobotRoot));
        if (carriageLift == null) missing.Add(nameof(carriageLift));
        if (carryPoint == null) missing.Add(nameof(carryPoint));
        if (palletRoot == null) missing.Add(nameof(palletRoot));
        if (liftController == null) missing.Add(nameof(liftController));
        if (palletMarkerController == null) missing.Add(nameof(palletMarkerController));

        if (missing.Count > 0)
        {
            missingReferenceWarningShown = true;
            Debug.LogWarning($"[PalletCarry] Missing Inspector reference: {string.Join(", ", missing)}", this);
        }
    }

    private static float HorizontalDistance(Vector3 first, Vector3 second)
    {
        float deltaX = first.x - second.x;
        float deltaZ = first.z - second.z;
        return Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
    }

    private static float EvaluateCurve(AnimationCurve curve, float normalizedTime)
    {
        return curve != null ? curve.Evaluate(normalizedTime) : normalizedTime;
    }
}

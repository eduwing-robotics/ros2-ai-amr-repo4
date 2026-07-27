using UnityEngine;

public class scr_TB3ForkliftRuntimeController : MonoBehaviour
{
    [SerializeField] private Transform forkliftRoot;
    [SerializeField] private Transform carriageTarget;
    [SerializeField] private Transform pinionTarget;
    [SerializeField] private Vector3 liftLocalAxis = Vector3.forward;
    [SerializeField] private Vector3 pinionLocalAxis = Vector3.up;
    [SerializeField] private float minLiftHeight;
    [SerializeField] private float maxLiftHeight = 0.37f;
    [SerializeField] private float liftSpeed = 0.15f;
    [SerializeField] private float pinionDegreesPerUnit = 720f;
    [SerializeField] private bool logDebug = true;

    private Vector3 carriageBaseLocalPosition;
    private Quaternion pinionBaseLocalRotation;
    private float currentLiftHeight;
    private int liftDirection;
    private bool basePoseCaptured;
    private bool missingTargetWarningShown;

    public float HeightPercent => Mathf.Approximately(maxLiftHeight, minLiftHeight)
        ? 0f
        : Mathf.InverseLerp(minLiftHeight, maxLiftHeight, currentLiftHeight);
    public float CurrentNormalizedLiftHeight => HeightPercent;
    public float CurrentLiftHeight => currentLiftHeight;
    public bool IsLiftMovingUp => liftDirection > 0;
    public bool IsLiftMovingDown => liftDirection < 0;
    public bool IsLiftAtBottom => currentLiftHeight <= Mathf.Min(minLiftHeight, maxLiftHeight) + 0.0001f;

    private void Awake()
    {
        ResolveReferences();
        CaptureBasePoseIfNeeded();
        ApplyLiftHeight(currentLiftHeight);
    }

    private void Update()
    {
        if (liftDirection == 0)
        {
            return;
        }

        float nextHeight = currentLiftHeight + liftDirection * Mathf.Max(0f, liftSpeed) * Time.deltaTime;
        ApplyLiftHeight(nextHeight);
    }

    public void LiftUp()
    {
        ResolveReferences();
        CaptureBasePoseIfNeeded();
        if (!HasRequiredTargets()) return;
        liftDirection = 1;
        if (logDebug) Debug.Log("[Forklift] lift up");
    }

    public void LiftDown()
    {
        ResolveReferences();
        CaptureBasePoseIfNeeded();
        if (!HasRequiredTargets()) return;
        liftDirection = -1;
        if (logDebug) Debug.Log("[Forklift] lift down");
    }

    public void StopLift()
    {
        liftDirection = 0;
        if (logDebug) Debug.Log("[Forklift] lift stop");
    }

    public void SetHeightPercent(float percent)
    {
        ResolveReferences();
        CaptureBasePoseIfNeeded();
        if (!HasRequiredTargets()) return;
        float clampedPercent = Mathf.Clamp01(percent);
        ApplyLiftHeight(Mathf.Lerp(minLiftHeight, maxLiftHeight, clampedPercent));
    }

    public string GetHeightPercentText()
    {
        return $"{Mathf.RoundToInt(HeightPercent * 100f)}%";
    }

    [ContextMenu("Lift Up")]
    private void ContextLiftUp()
    {
        LiftUp();
    }

    [ContextMenu("Lift Down")]
    private void ContextLiftDown()
    {
        LiftDown();
    }

    [ContextMenu("Stop Lift")]
    private void ContextStopLift()
    {
        StopLift();
    }

    [ContextMenu("Set Height 0%")]
    private void ContextSetHeightZero()
    {
        SetHeightPercent(0f);
    }

    [ContextMenu("Set Height 100%")]
    private void ContextSetHeightFull()
    {
        SetHeightPercent(1f);
    }

    [ContextMenu("Capture Current As Base Pose")]
    private void CaptureCurrentAsBasePose()
    {
        ResolveReferences();
        if (carriageTarget != null)
        {
            carriageBaseLocalPosition = carriageTarget.localPosition;
        }

        if (pinionTarget != null)
        {
            pinionBaseLocalRotation = pinionTarget.localRotation;
        }

        currentLiftHeight = minLiftHeight;
        basePoseCaptured = true;
        if (logDebug) Debug.Log("[Forklift] captured current carriage/pinion pose as base");
    }

    private void ApplyLiftHeight(float height)
    {
        if (!HasRequiredTargets())
        {
            return;
        }

        float min = Mathf.Min(minLiftHeight, maxLiftHeight);
        float max = Mathf.Max(minLiftHeight, maxLiftHeight);
        currentLiftHeight = Mathf.Clamp(height, min, max);

        Vector3 liftAxis = liftLocalAxis.sqrMagnitude < 0.0001f ? Vector3.up : liftLocalAxis.normalized;
        carriageTarget.localPosition = carriageBaseLocalPosition + liftAxis * currentLiftHeight;

        if (pinionTarget != null)
        {
            Vector3 rotateAxis = pinionLocalAxis.sqrMagnitude < 0.0001f ? Vector3.right : pinionLocalAxis.normalized;
            pinionTarget.localRotation = pinionBaseLocalRotation * Quaternion.AngleAxis(currentLiftHeight * pinionDegreesPerUnit, rotateAxis);
        }
    }

    private bool HasRequiredTargets()
    {
        bool ok = carriageTarget != null;
        if (!ok && !missingTargetWarningShown)
        {
            missingTargetWarningShown = true;
            Debug.LogWarning("[Forklift] Carriage target not found. Assign carriageTarget in the Inspector.");
        }

        return ok;
    }

    private void CaptureBasePoseIfNeeded()
    {
        if (basePoseCaptured)
        {
            return;
        }

        if (carriageTarget != null)
        {
            carriageBaseLocalPosition = carriageTarget.localPosition;
        }

        if (pinionTarget != null)
        {
            pinionBaseLocalRotation = pinionTarget.localRotation;
        }

        currentLiftHeight = Mathf.Clamp(currentLiftHeight, Mathf.Min(minLiftHeight, maxLiftHeight), Mathf.Max(minLiftHeight, maxLiftHeight));
        basePoseCaptured = true;
    }

    private void ResolveReferences()
    {
        if (forkliftRoot == null)
        {
            GameObject rootObject = FindSceneObject("TB3_Forklift_RackPinion_Final");
            if (rootObject != null)
            {
                forkliftRoot = rootObject.transform;
            }
        }

        Transform searchRoot = forkliftRoot != null ? forkliftRoot : transform;
        if (carriageTarget == null)
        {
            carriageTarget = FindDescendantByKeywords(searchRoot, "Carriage", "Lift", "Fork");
        }

        if (pinionTarget == null)
        {
            pinionTarget = FindDescendantByKeywords(searchRoot, "Pinion", "Gear", "Rotate");
        }
    }

    private static Transform FindDescendantByKeywords(Transform root, params string[] keywords)
    {
        if (root == null || keywords == null)
        {
            return null;
        }

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child == root)
            {
                continue;
            }

            string childName = child.name;
            foreach (string keyword in keywords)
            {
                if (!string.IsNullOrWhiteSpace(keyword) &&
                    childName.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return child;
                }
            }
        }

        return null;
    }

    private static GameObject FindSceneObject(string objectName)
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
}

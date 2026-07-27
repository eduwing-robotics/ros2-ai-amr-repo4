using System.Collections;
using UnityEngine;

public class scr_StaffEntranceBarrierController : MonoBehaviour
{
    [SerializeField] private Transform targetPivot;
    [SerializeField] private Vector3 closedEuler;
    [SerializeField] private Vector3 openEuler = new Vector3(0f, 0f, -65f);
    [SerializeField] private float openDuration = 0.5f;
    [SerializeField] private float holdOpenSeconds = 1.5f;
    [SerializeField] private float closeDuration = 0.5f;
    [SerializeField] private bool autoClose = true;
    [SerializeField] private bool logDebug = true;

    private Coroutine motionRoutine;
    private float holdUntilTime;

    private void Awake()
    {
        ResolveTargetPivot();
    }

    public void Open()
    {
        ResolveTargetPivot();
        if (targetPivot == null) return;
        StartMotion(openEuler, openDuration, "open");
    }

    public void Close()
    {
        ResolveTargetPivot();
        if (targetPivot == null) return;
        StartMotion(closedEuler, closeDuration, "close");
    }

    public void OpenThenClose(string reason)
    {
        ResolveTargetPivot();
        if (targetPivot == null) return;

        holdUntilTime = Time.time + Mathf.Max(0f, openDuration) + Mathf.Max(0f, holdOpenSeconds);
        if (motionRoutine != null)
        {
            StopCoroutine(motionRoutine);
        }

        motionRoutine = StartCoroutine(OpenHoldCloseRoutine(reason));
    }

    private IEnumerator OpenHoldCloseRoutine(string reason)
    {
        if (logDebug)
        {
            Debug.Log($"[Barrier] OpenThenClose reason={reason}");
        }

        yield return RotateTo(openEuler, openDuration);

        while (autoClose && Time.time < holdUntilTime)
        {
            yield return null;
        }

        if (autoClose)
        {
            yield return RotateTo(closedEuler, closeDuration);
        }

        motionRoutine = null;
    }

    private void StartMotion(Vector3 targetEuler, float duration, string label)
    {
        if (motionRoutine != null)
        {
            StopCoroutine(motionRoutine);
        }

        if (logDebug)
        {
            Debug.Log($"[Barrier] {label}");
        }

        motionRoutine = StartCoroutine(RotateTo(targetEuler, duration));
    }

    private IEnumerator RotateTo(Vector3 targetEuler, float duration)
    {
        Quaternion start = targetPivot.localRotation;
        Quaternion end = Quaternion.Euler(targetEuler);
        float safeDuration = Mathf.Max(0.001f, duration);
        float elapsed = 0f;

        while (elapsed < safeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / safeDuration);
            targetPivot.localRotation = Quaternion.Slerp(start, end, t);
            yield return null;
        }

        targetPivot.localRotation = end;
    }

    private void ResolveTargetPivot()
    {
        if (targetPivot != null) return;

        GameObject pivotObject = FindSceneObject("BarrierArm_Pivot");
        if (pivotObject != null)
        {
            targetPivot = pivotObject.transform;
        }
    }

    [ContextMenu("Test Open Then Close")]
    private void TestOpenThenClose()
    {
        OpenThenClose("context menu test");
    }

    [ContextMenu("Capture Current As Closed")]
    private void CaptureCurrentAsClosed()
    {
        ResolveTargetPivot();
        if (targetPivot != null)
        {
            closedEuler = targetPivot.localEulerAngles;
        }
    }

    [ContextMenu("Set Example Open Rotation")]
    private void SetExampleOpenRotation()
    {
        openEuler = closedEuler + new Vector3(0f, 0f, -65f);
    }

    private static GameObject FindSceneObject(string objectName)
    {
        foreach (GameObject item in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (item.name == objectName && item.scene.IsValid()) return item;
        }

        return null;
    }
}

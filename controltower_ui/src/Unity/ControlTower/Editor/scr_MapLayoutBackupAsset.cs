#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class scr_MapLayoutBackupAsset : ScriptableObject
{
    private const int CurrentSchemaVersion = 1;

    [SerializeField] private int schemaVersion = CurrentSchemaVersion;
    [SerializeField] private string scenePath = string.Empty;
    [SerializeField] private string createdAt = string.Empty;
    [SerializeField] private long createdUtcTicks;
    [SerializeField] private List<scr_MapLayoutObjectSnapshot> objectSnapshots =
        new List<scr_MapLayoutObjectSnapshot>();

    public int SchemaVersion => schemaVersion;
    public string ScenePath => scenePath;
    public string CreatedAt => createdAt;
    public long CreatedUtcTicks => createdUtcTicks;
    public IReadOnlyList<scr_MapLayoutObjectSnapshot> ObjectSnapshots => objectSnapshots;

    internal void Initialize(
        string sourceScenePath,
        string createdAtValue,
        long utcTicks,
        List<scr_MapLayoutObjectSnapshot> snapshots)
    {
        schemaVersion = CurrentSchemaVersion;
        scenePath = sourceScenePath ?? string.Empty;
        createdAt = createdAtValue ?? string.Empty;
        createdUtcTicks = utcTicks;
        objectSnapshots = snapshots ?? new List<scr_MapLayoutObjectSnapshot>();
    }
}

[Serializable]
public sealed class scr_MapLayoutObjectSnapshot
{
    [SerializeField] private string groupName = string.Empty;
    [SerializeField] private string scenePath = string.Empty;
    [SerializeField] private string hierarchyPath = string.Empty;
    [SerializeField] private string parentPath = string.Empty;
    [SerializeField] private bool activeSelf;
    [SerializeField] private int layer;
    [SerializeField] private bool isRectTransform;

    [SerializeField] private Vector2 anchoredPosition;
    [SerializeField] private Vector2 sizeDelta;
    [SerializeField] private Vector2 anchorMin;
    [SerializeField] private Vector2 anchorMax;
    [SerializeField] private Vector2 pivot;

    [SerializeField] private Vector3 localPosition;
    [SerializeField] private Quaternion localRotation = Quaternion.identity;
    [SerializeField] private Vector3 localScale = Vector3.one;

    public string GroupName => groupName;
    public string ScenePath => scenePath;
    public string HierarchyPath => hierarchyPath;
    public string ParentPath => parentPath;
    public bool ActiveSelf => activeSelf;
    public int Layer => layer;
    public bool IsRectTransform => isRectTransform;
    public Vector2 AnchoredPosition => anchoredPosition;
    public Vector2 SizeDelta => sizeDelta;
    public Vector2 AnchorMin => anchorMin;
    public Vector2 AnchorMax => anchorMax;
    public Vector2 Pivot => pivot;
    public Vector3 LocalPosition => localPosition;
    public Quaternion LocalRotation => localRotation;
    public Vector3 LocalScale => localScale;

    internal void Capture(
        string sourceGroupName,
        string sourceScenePath,
        string sourceHierarchyPath,
        string sourceParentPath,
        Transform target)
    {
        groupName = sourceGroupName ?? string.Empty;
        scenePath = sourceScenePath ?? string.Empty;
        hierarchyPath = sourceHierarchyPath ?? string.Empty;
        parentPath = sourceParentPath ?? string.Empty;
        activeSelf = target.gameObject.activeSelf;
        layer = target.gameObject.layer;
        localPosition = target.localPosition;
        localRotation = target.localRotation;
        localScale = target.localScale;

        RectTransform rectTransform = target as RectTransform;
        isRectTransform = rectTransform != null;
        if (rectTransform == null)
        {
            return;
        }

        anchoredPosition = rectTransform.anchoredPosition;
        sizeDelta = rectTransform.sizeDelta;
        anchorMin = rectTransform.anchorMin;
        anchorMax = rectTransform.anchorMax;
        pivot = rectTransform.pivot;
    }
}
#endif

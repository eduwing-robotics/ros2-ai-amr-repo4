#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

[InitializeOnLoad]
public static class scr_MapLayoutEditorTool
{
    private const string BackupMenuPath = "Tools/ControlTower/Map/Backup Current Layout";
    private const string RestoreMenuPath = "Tools/ControlTower/Map/Restore Previous Layout";
    private const string ValidateMenuPath = "Tools/ControlTower/Map/Validate / Compare Layout";
    private const string PreviewFull2DMenuPath = "Tools/ControlTower/Map/Preview Full 2D Layout";
    private const string PreviewMiniMapMenuPath = "Tools/ControlTower/Map/Preview Mini Map Layout";
    private const string PreviewMapStatusMenuPath = "Tools/ControlTower/Map/Preview Map Status Layout";
    private const string PreviewAllLayoutsMenuPath = "Tools/ControlTower/Map/Preview All Layouts";
    private const string ClearLayoutPreviewMenuPath = "Tools/ControlTower/Map/Clear Layout Preview";
    private const string CalibrateFull2DInteriorMenuPath =
        "Tools/ControlTower/Map/Calibrate Full 2D Interior Bounds";
    private const string CalibrateMapStatusInteriorMenuPath =
        "Tools/ControlTower/Map/Calibrate Map Status Interior Bounds";
    private const string SaveMeasuredInteriorMenuPath =
        "Tools/ControlTower/Map/Save Measured Interior Bounds";
    private const string ClearInteriorCalibrationMenuPath =
        "Tools/ControlTower/Map/Clear Interior Calibration Preview";
    private const string ApplyMeasured2DMenuPath = "Tools/ControlTower/Map/Apply Measured 2D Layout";
    private const string CalibrateFactory3DInteriorMenuPath =
        "Tools/ControlTower/Map/Calibrate Factory 3D Interior Bounds";
    private const string SaveFactory3DInteriorMenuPath =
        "Tools/ControlTower/Map/Save Factory 3D Interior Bounds";
    private const string PreviewMeasured3DMenuPath =
        "Tools/ControlTower/Map/Preview Measured 3D Layout";
    private const string ApplyMeasured3DPositionsMenuPath =
        "Tools/ControlTower/Map/Apply Measured 3D Positions";
    private const string ApplyMeasured3DFootprintsMenuPath =
        "Tools/ControlTower/Map/Apply Measured 3D Footprints";
    private const string ClearMeasured3DPreviewMenuPath =
        "Tools/ControlTower/Map/Clear 3D Layout Preview";
    private const string BackupFolderPath = "Assets/Project/Settings/MapLayoutBackups";
    private const string MeasuredLayoutConfigFolderPath = "Assets/Project/Settings/MapLayout";
    private const string MeasuredLayoutConfigAssetPath =
        MeasuredLayoutConfigFolderPath + "/MapMeasuredLayoutConfig.asset";
    private const string BackupFilePrefix = "MapLayoutBackup_";
    private const string ControlTowerScenePath = "Assets/Project/Scenes/ControlTowerScene.unity";
    private const float InteriorWidthCm = 176f;
    private const float InteriorHeightCm = 174f;
    private const float BoundsTolerance = 0.01f;
    private const float Factory3DPositionTolerance = 0.001f;
    private const float Factory3DFootprintTolerance = 0.01f;
    private const string Measured3DMappingVersion = "PALLET_GROUP_PLACEMENT_V2";
    private const string PlayModeBaselineSessionKey =
        "ControlTower.MapLayout.Measured2D.PlayModeBaseline";

    // Measured placement remains preview-only until every visual and wall bound is approved.
    private static readonly bool Measured2DApplyEnabled = false;

    private static readonly string[] FullMapFacilityNames =
    {
        "B_ConveyorZone_01",
        "B_ConveyorZone_02",
        "C_PalletArea",
        "A_ChargingZone",
        "D_EntryZone"
    };

    private static readonly string[] MiniMapFacilityNames =
    {
        "Image_Mini2DMapConveyor01",
        "Image_Mini2DMapConveyor02",
        "Image_Mini2DMapPallets",
        "Image_Mini2DMapCharging",
        "Image_Mini2DMapEntry"
    };

    private static readonly string[] InteriorWallNames =
    {
        "Wall_2D_Left",
        "Wall_2D_Right",
        "Wall_2D_Bottom",
        "Wall_2D_Top"
    };

    private static readonly string[] Factory3DFacilityNames =
    {
        "B_ConveyorFloor_01_3D",
        "B_ConveyorFloor_02_3D",
        "C_PalletArea_3D",
        "Pallet_Group_3D",
        "A_ChargingZone_3D",
        "D_EntryZone_3D"
    };

    private static readonly string[] MeasuredFactory3DLogicalZoneNames =
    {
        "B_ConveyorFloor_01_3D",
        "B_ConveyorFloor_02_3D",
        "C_PalletArea_3D",
        "A_ChargingZone_3D"
    };

    private static readonly string[] MeasuredFactory3DPlacementRootNames =
    {
        "B_ConveyorFloor_01_3D",
        "B_ConveyorFloor_02_3D",
        "Pallet_Group_3D",
        "A_ChargingZone_3D"
    };

    private static readonly string[] MeasuredFactory3DShortLabels =
    {
        "C1",
        "C2",
        "PALLET",
        "CHARGE"
    };

    private static readonly string[] Factory3DReferenceNames =
    {
        "Floor_3DMap",
        "Wall_3D_Left",
        "Wall_3D_Right",
        "Wall_3D_Top",
        "Wall_3D_Bottom"
    };

    private static readonly MeasuredFacilityDefinition[] MeasuredFacilityDefinitions =
    {
        new MeasuredFacilityDefinition("Conveyor01", 50f, 63f, 130f, 174f),
        new MeasuredFacilityDefinition("Conveyor02", 113f, 126f, 130f, 174f),
        new MeasuredFacilityDefinition("Pallet", 55f, 85f, 45f, 75f),
        new MeasuredFacilityDefinition("Charging", 119f, 176f, 0f, 30f)
    };

    private static List<Measured2DResult> activeMeasuredPreview = new List<Measured2DResult>();
    private static GUIStyle measuredPreviewLabelStyle;
    private static readonly Dictionary<int, SpriteAlphaBoundsInfo> spriteAlphaBoundsCache =
        new Dictionary<int, SpriteAlphaBoundsInfo>();
    private static readonly HashSet<string> reportedPlayModeDifferences = new HashSet<string>();
    private static InteriorCalibrationPreviewState activeInteriorCalibration;
    private static Factory3DInteriorCalibrationState activeFactory3DInteriorCalibration;
    private static Factory3DPreviewState activeMeasured3DPreview;
    private static double nextPlayModeValidationTime;

    static scr_MapLayoutEditorTool()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        SceneView.duringSceneGui -= DrawInteriorCalibrationPreview;
        if (EditorApplication.isPlaying)
        {
            EditorApplication.update -= ValidatePlayModeStaticLayoutOnInterval;
            EditorApplication.update += ValidatePlayModeStaticLayoutOnInterval;
        }
    }

    private readonly struct LayoutTarget
    {
        public LayoutTarget(string groupName, Transform transform)
        {
            GroupName = groupName;
            Transform = transform;
        }

        public string GroupName { get; }
        public Transform Transform { get; }
    }

    private readonly struct MeasuredFacilityDefinition
    {
        public MeasuredFacilityDefinition(
            string id,
            float leftCm,
            float rightCm,
            float bottomCm,
            float topCm)
        {
            Id = id;
            LeftCm = leftCm;
            RightCm = rightCm;
            BottomCm = bottomCm;
            TopCm = topCm;
        }

        public string Id { get; }
        public float LeftCm { get; }
        public float RightCm { get; }
        public float BottomCm { get; }
        public float TopCm { get; }
    }

    private sealed class Measured2DViewContext
    {
        public string ViewName;
        public RectTransform CoordinateRoot;
        public RectTransform[] Facilities;
        public RectTransform Entry;
        public Measured2DResult EntryImageAnalysis;
        public RectTransform[] InteriorWalls;
        public Rect[] InteriorWallBounds;
        public bool HasDiagnosticWallInteriorBounds;
        public Rect DiagnosticWallInteriorBounds;
        public Rect InteriorBounds;
        public Rect NormalizedInteriorBounds;
        public string InteriorBoundsSource;
        public bool HasSavedInteriorBounds;
        public float UnitsPerCmX;
        public float UnitsPerCmY;
    }

    private sealed class Measured2DResult
    {
        public string ViewName;
        public string FacilityId;
        public RectTransform CoordinateRoot;
        public RectTransform Target;
        public Rect CurrentBounds;
        public Rect CurrentImageDrawBounds;
        public Rect TargetBounds;
        public Rect CompensatedTargetRectBounds;
        public Rect InteriorBounds;
        public bool InteriorBoundsSaved;
        public Vector2 TargetAnchoredPosition;
        public Vector2 TargetSizeDelta;
        public Vector2 CompensatedTargetAnchoredPosition;
        public Vector2 CompensatedTargetSizeDelta;
        public Vector2 AnchoredPositionCorrection;
        public bool HasPhysicalTarget;
        public bool HasCompensatedTargetRect;
        public bool HasImage;
        public string ImageType;
        public bool PreserveAspect;
        public bool UseSpriteMesh;
        public Rect SpriteRect;
        public Vector2 SpriteRectSize;
        public Vector3 SpriteBoundsSize;
        public Vector2 TextureSize;
        public float SpriteAspect;
        public float RectTransformAspect;
        public bool HasSpriteAlphaBounds;
        public Rect SpriteAlphaPixelBounds;
        public Rect SpriteAlphaNormalizedBounds;
        public Vector2 SpriteAlphaCenterOffsetNormalized;
        public string SpriteAlphaDetail;
        public Rect CompositeNormalizedBounds;
        public int CompositeVisualCount;
        public bool CompositeVisibleBoundsValid;
        public string CompositeVisibleDetail;
        public bool ImageDrawValidationPassed;
        public Vector2 SuggestedRectTransformSizeInRoot;
        public string ApplyPropertyProposal;

        public Vector2 CurrentSize => CurrentBounds.size;
        public Vector2 TargetSize => TargetBounds.size;
        public Vector2 CurrentCenter => CurrentBounds.center;
        public Vector2 TargetCenter => TargetBounds.center;
        public Vector2 PositionDelta => TargetCenter - CurrentCenter;
        public Vector2 PhysicalSizeDelta => TargetSize - CurrentSize;
    }

    private readonly struct SpriteAlphaBoundsInfo
    {
        public SpriteAlphaBoundsInfo(
            bool available,
            Rect pixelBounds,
            Rect normalizedBounds,
            Vector2 textureSize,
            string detail)
        {
            Available = available;
            PixelBounds = pixelBounds;
            NormalizedBounds = normalizedBounds;
            TextureSize = textureSize;
            Detail = detail;
        }

        public bool Available { get; }
        public Rect PixelBounds { get; }
        public Rect NormalizedBounds { get; }
        public Vector2 TextureSize { get; }
        public string Detail { get; }
    }

    private readonly struct MeasuredPreviewLabel
    {
        public MeasuredPreviewLabel(string text, Vector3 worldAnchor, bool placeRight)
        {
            Text = text;
            WorldAnchor = worldAnchor;
            PlaceRight = placeRight;
        }

        public string Text { get; }
        public Vector3 WorldAnchor { get; }
        public bool PlaceRight { get; }
    }

    private sealed class InteriorCalibrationPreviewState
    {
        public string ViewName;
        public RectTransform CoordinateRoot;
        public Rect Bounds;
        public bool InitializedFromSavedConfig;
    }

    private sealed class Factory3DInteriorCalibrationState
    {
        public Transform Stage;
        public Transform FloorRoot;
        public Rect FloorBounds;
        public float PreviewY;
        public Rect Bounds;
        public bool InitializedFromSavedConfig;
    }

    private sealed class Factory3DPreviewState
    {
        public Transform Stage;
        public Rect FloorBounds;
        public Rect InteriorBounds;
        public float PreviewY;
        public readonly List<Measured3DResult> Results = new List<Measured3DResult>();
    }

    private sealed class Measured3DResult
    {
        public string FacilityId;
        public string ShortLabel;
        public Transform Stage;
        public Transform LogicalZoneRoot;
        public Transform PlacementRoot;
        public Transform SafeVisualRoot;
        public Rect SourceVisibleBounds;
        public Rect SourceClampedBounds;
        public Rect SourceNormalizedBounds;
        public Rect OrientedNormalizedBounds;
        public Rect CurrentFootprint;
        public Rect TargetFootprint;
        public Rect PositionOnlyFootprint;
        public Rect PredictedFootprint;
        public Vector2 MoveXZ;
        public Vector2 ScaleXZ;
        public float CurrentMinY;
        public float CurrentMaxY;
        public int RendererCount;
        public bool SourceValid;
        public bool CurrentFootprintValid;
        public bool TargetValid;
        public bool PositionApplyReady;
        public bool FootprintApplyReady;
        public bool VisualAxesAligned;
        public bool HasScript;
        public bool HasCollider;
        public bool HasAnimator;
        public bool HasRigidbody;
        public bool HasCamera;
        public string RendererSummary;
        public string ScaleBlockReason;
    }

    private sealed class Factory3DTransformProtectionState
    {
        public Transform Target;
        public Transform Parent;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public Vector3 LocalScale;
        public int Layer;
        public bool ActiveSelf;
    }

    private sealed class CompositeBoundsAccumulator
    {
        public bool HasBounds;
        public Rect Bounds;
        public int VisualCount;
        public bool ValidationPassed = true;
        public int SpriteImageCount;
        public int SolidColorImageCount;
        public int RawImageCount;
        public int SpriteRendererCount;
        public int MeshRendererCount;
        public int SkinnedMeshRendererCount;
        public int SkippedSpriteLessImageCount;
        public int UnverifiedVisualCount;
        public readonly List<string> VisualDetails = new List<string>();

        public void Add(Rect bounds, string detail)
        {
            if (!HasBounds)
            {
                Bounds = bounds;
                HasBounds = true;
            }
            else
            {
                Bounds = Rect.MinMaxRect(
                    Mathf.Min(Bounds.xMin, bounds.xMin),
                    Mathf.Min(Bounds.yMin, bounds.yMin),
                    Mathf.Max(Bounds.xMax, bounds.xMax),
                    Mathf.Max(Bounds.yMax, bounds.yMax));
            }

            VisualCount++;
            if (!string.IsNullOrEmpty(detail))
            {
                VisualDetails.Add(detail);
            }
        }
    }

    private sealed class ApplyProtectionState
    {
        public Transform Target;
        public scr_MapLayoutObjectSnapshot Snapshot;
        public bool AllowsMeasuredRectChange;
    }

    private sealed class ComponentSignature
    {
        public UnityEngine.Object Target;
        public string Name;
        public string Json;
    }

    [Serializable]
    private sealed class PlayModeLayoutBaseline
    {
        public string ScenePath = string.Empty;
        public List<scr_MapLayoutObjectSnapshot> Snapshots =
            new List<scr_MapLayoutObjectSnapshot>();
    }

    private readonly struct ResolvedSnapshot
    {
        public ResolvedSnapshot(scr_MapLayoutObjectSnapshot snapshot, Transform transform)
        {
            Snapshot = snapshot;
            Transform = transform;
        }

        public scr_MapLayoutObjectSnapshot Snapshot { get; }
        public Transform Transform { get; }
    }

    private sealed class ComparisonSummary
    {
        public int ObjectCount;
        public int PropertyCount;
        public int MatchingPropertyCount;
        public int DifferenceCount;
        public int MissingObjectCount;
        public int AllowedMeasuredDifferenceCount;
        public int ProtectedDifferenceCount;

        public bool IsMatch => DifferenceCount == 0 && MissingObjectCount == 0;
        public bool HasOnlyAllowedMeasuredChanges =>
            DifferenceCount > 0 && ProtectedDifferenceCount == 0 && MissingObjectCount == 0;
    }

    [MenuItem(BackupMenuPath)]
    public static void BackupCurrentLayout()
    {
        if (!TryGetEditableActiveScene(out Scene scene))
        {
            return;
        }

        if (!TryCollectLayoutTargets(scene, out List<LayoutTarget> targets))
        {
            Debug.LogError("[MapLayoutBackup] Required layout objects were not found. Backup was not created.");
            return;
        }

        targets.Sort((left, right) => string.CompareOrdinal(
            BuildHierarchyPath(left.Transform),
            BuildHierarchyPath(right.Transform)));

        List<scr_MapLayoutObjectSnapshot> snapshots =
            new List<scr_MapLayoutObjectSnapshot>(targets.Count);
        foreach (LayoutTarget target in targets)
        {
            Transform transform = target.Transform;
            scr_MapLayoutObjectSnapshot snapshot = new scr_MapLayoutObjectSnapshot();
            snapshot.Capture(
                target.GroupName,
                scene.path,
                BuildHierarchyPath(transform),
                transform.parent != null ? BuildHierarchyPath(transform.parent) : string.Empty,
                transform);
            snapshots.Add(snapshot);
        }

        EnsureAssetFolder(BackupFolderPath);
        DateTime localNow = DateTime.Now;
        string assetPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{BackupFolderPath}/{BackupFilePrefix}{localNow:yyyyMMdd_HHmmss}.asset");

        scr_MapLayoutBackupAsset backup = ScriptableObject.CreateInstance<scr_MapLayoutBackupAsset>();
        backup.Initialize(
            scene.path,
            localNow.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            DateTime.UtcNow.Ticks,
            snapshots);

        AssetDatabase.CreateAsset(backup, assetPath);
        EditorUtility.SetDirty(backup);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = backup;
        EditorGUIUtility.PingObject(backup);
        Debug.Log(
            $"[MapLayoutBackup] Saved | Asset={assetPath} | Scene={scene.path} | Objects={snapshots.Count}");
    }

    [MenuItem(RestoreMenuPath)]
    public static void RestorePreviousLayout()
    {
        if (!TryGetEditableActiveScene(out Scene scene) ||
            !TryResolveBackupAsset(out scr_MapLayoutBackupAsset backup, out string backupPath))
        {
            return;
        }

        if (!ValidateBackupHeader(backup, scene, backupPath))
        {
            return;
        }

        if (!TryResolveAllSnapshots(backup, scene, out List<ResolvedSnapshot> resolvedSnapshots))
        {
            Debug.LogError("[MapLayoutRestore] Scene hierarchy does not match the backup. Nothing was changed.");
            return;
        }

        List<UnityEngine.Object> undoTargets = new List<UnityEngine.Object>(resolvedSnapshots.Count * 2);
        HashSet<int> registeredInstanceIds = new HashSet<int>();
        foreach (ResolvedSnapshot resolved in resolvedSnapshots)
        {
            AddUndoTarget(resolved.Transform.gameObject, registeredInstanceIds, undoTargets);
            AddUndoTarget(resolved.Transform, registeredInstanceIds, undoTargets);
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Restore Map Layout Backup");
        Undo.RegisterCompleteObjectUndo(undoTargets.ToArray(), "Restore Map Layout Backup");

        foreach (ResolvedSnapshot resolved in resolvedSnapshots)
        {
            ApplySnapshot(resolved.Snapshot, resolved.Transform);
        }

        ComparisonSummary validation = CompareLayout(backup, scene, true);
        if (!validation.IsMatch)
        {
            Undo.RevertAllDownToGroup(undoGroup);
            Debug.LogError("[MapLayoutRestore] Validation failed. All in-memory Restore changes were reverted.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
        {
            Undo.RevertAllDownToGroup(undoGroup);
            Debug.LogError("[MapLayoutRestore] Scene save failed. Restore changes were reverted.");
            return;
        }

        Undo.CollapseUndoOperations(undoGroup);
        SceneView.RepaintAll();
        Debug.Log(
            $"[MapLayoutRestore] Restored and verified | Asset={backupPath} | Objects={resolvedSnapshots.Count} | Undo=available");
    }

    [MenuItem(ValidateMenuPath)]
    public static void ValidateCompareLayout()
    {
        if (!TryGetLoadedActiveScene(out Scene scene) ||
            !TryResolveBackupAsset(out scr_MapLayoutBackupAsset backup, out string backupPath))
        {
            return;
        }

        if (!ValidateBackupHeader(backup, scene, backupPath))
        {
            return;
        }

        Debug.Log(
            $"[MapLayoutCompare] Object Path | Property | Backup Value | Current Value | Difference\n" +
            $"[MapLayoutCompare] Backup={backupPath}");
        ComparisonSummary summary = CompareLayout(backup, scene, true);
        if (summary.IsMatch)
        {
            Debug.Log(
                $"[MapLayoutCompare] MATCH | Objects={summary.ObjectCount} | " +
                $"Properties={summary.PropertyCount} | Differences=0");
        }
        else if (summary.HasOnlyAllowedMeasuredChanges)
        {
            Debug.Log(
                $"[MapLayoutCompare] MEASURED_LAYOUT_ONLY | Objects={summary.ObjectCount} | " +
                $"AllowedDifferences={summary.AllowedMeasuredDifferenceCount} | " +
                "ProtectedDifferences=0");
        }
        else
        {
            Debug.Log(
                $"[MapLayoutCompare] DIFFERENT | Objects={summary.ObjectCount} | " +
                $"MatchingProperties={summary.MatchingPropertyCount}/{summary.PropertyCount} | " +
                $"Differences={summary.DifferenceCount} | " +
                $"AllowedMeasured={summary.AllowedMeasuredDifferenceCount} | " +
                $"Protected={summary.ProtectedDifferenceCount} | " +
                $"MissingObjects={summary.MissingObjectCount}");
        }
    }

    [MenuItem(PreviewFull2DMenuPath)]
    public static void PreviewFull2DLayout()
    {
        PreviewMeasured2DLayoutForView("Full2D");
    }

    [MenuItem(PreviewMiniMapMenuPath)]
    public static void PreviewMiniMapLayout()
    {
        PreviewMeasured2DLayoutForView("MiniMap");
    }

    [MenuItem(PreviewMapStatusMenuPath)]
    public static void PreviewMapStatusLayout()
    {
        PreviewMeasured2DLayoutForView("MapStatus");
    }

    [MenuItem(CalibrateFull2DInteriorMenuPath)]
    public static void CalibrateFull2DInteriorBounds()
    {
        BeginInteriorCalibration("Full2D");
    }

    [MenuItem(CalibrateMapStatusInteriorMenuPath)]
    public static void CalibrateMapStatusInteriorBounds()
    {
        BeginInteriorCalibration("MapStatus");
    }

    [MenuItem(SaveMeasuredInteriorMenuPath)]
    public static void SaveMeasuredInteriorBounds()
    {
        if (activeInteriorCalibration == null ||
            activeInteriorCalibration.CoordinateRoot == null)
        {
            Debug.LogWarning(
                "[MeasuredInterior] No active calibration preview. " +
                "Run a Calibrate Interior Bounds menu first.");
            return;
        }

        Rect rootBounds = activeInteriorCalibration.CoordinateRoot.rect;
        Rect measuredBounds = activeInteriorCalibration.Bounds;
        if (!ValidateMeasuredInteriorBounds(
                activeInteriorCalibration.ViewName,
                rootBounds,
                measuredBounds,
                true))
        {
            return;
        }

        scr_MapMeasuredLayoutConfig config = LoadOrCreateMeasuredLayoutConfig();
        if (config == null)
        {
            return;
        }

        Undo.RecordObject(config, "Save Measured Interior Bounds");
        if (string.Equals(
                activeInteriorCalibration.ViewName,
                "Full2D",
                StringComparison.Ordinal))
        {
            config.SetFull2DInteriorBounds(measuredBounds);
        }
        else
        {
            config.SetMapStatusInteriorBounds(measuredBounds);
        }

        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        LogMeasuredInteriorBounds(
            "MeasuredInteriorSave",
            activeInteriorCalibration.ViewName,
            rootBounds,
            measuredBounds);
        Debug.Log(
            $"[MeasuredInteriorSave] Saved numeric bounds only | " +
            $"Asset={MeasuredLayoutConfigAssetPath} | " +
            $"View={activeInteriorCalibration.ViewName} | Scene unchanged");
    }

    [MenuItem(ClearInteriorCalibrationMenuPath)]
    public static void ClearInteriorCalibrationPreview()
    {
        string clearedView = activeInteriorCalibration != null
            ? activeInteriorCalibration.ViewName
            : "None";
        activeInteriorCalibration = null;
        SceneView.duringSceneGui -= DrawInteriorCalibrationPreview;
        SceneView.RepaintAll();
        Debug.Log(
            $"[MeasuredInterior] Calibration preview cleared | " +
            $"View={clearedView} | Scene unchanged");
    }

    [MenuItem(PreviewAllLayoutsMenuPath)]
    public static void PreviewAllLayouts()
    {
        PreviewMeasured2DLayoutForView(string.Empty);
    }

    [MenuItem(ClearLayoutPreviewMenuPath)]
    public static void ClearLayoutPreview()
    {
        int clearedObjectCount = activeMeasuredPreview != null
            ? activeMeasuredPreview.Count
            : 0;
        SetMeasuredPreview(null);
        Debug.Log(
            $"[Measured2DPreview] Cleared | Objects={clearedObjectCount} | Scene values unchanged");
    }

    [MenuItem(CalibrateFactory3DInteriorMenuPath)]
    public static void CalibrateFactory3DInteriorBounds()
    {
        BeginFactory3DInteriorCalibration();
    }

    [MenuItem(SaveFactory3DInteriorMenuPath)]
    public static void SaveFactory3DInteriorBounds()
    {
        SaveActiveFactory3DInteriorCalibration();
    }

    [MenuItem(PreviewMeasured3DMenuPath)]
    public static void PreviewMeasured3DLayout()
    {
        PreviewFactory3DMeasuredLayout();
    }

    [MenuItem(ApplyMeasured3DPositionsMenuPath)]
    public static void ApplyMeasured3DPositions()
    {
        ApplyFactory3DPositions();
    }

    [MenuItem(ApplyMeasured3DFootprintsMenuPath)]
    public static void ApplyMeasured3DFootprints()
    {
        ApplyFactory3DFootprints();
    }

    [MenuItem(ClearMeasured3DPreviewMenuPath)]
    public static void ClearMeasured3DLayoutPreview()
    {
        int resultCount = activeMeasured3DPreview != null
            ? activeMeasured3DPreview.Results.Count
            : 0;
        activeMeasured3DPreview = null;
        activeFactory3DInteriorCalibration = null;
        SceneView.duringSceneGui -= DrawFactory3DInteriorCalibration;
        SceneView.duringSceneGui -= DrawMeasured3DPreview;
        SceneView.RepaintAll();
        Debug.Log(
            $"[Measured3DPreview] Cleared | Objects={resultCount} | Scene values unchanged");
    }

    private static void BeginInteriorCalibration(string viewName)
    {
        SetMeasuredPreview(null);
        activeMeasured3DPreview = null;
        activeFactory3DInteriorCalibration = null;
        SceneView.duringSceneGui -= DrawMeasured3DPreview;
        SceneView.duringSceneGui -= DrawFactory3DInteriorCalibration;
        if (!TryGetEditableControlTowerScene(out Scene scene) ||
            !TryCollectMeasured2DViewObjects(scene, out List<Measured2DViewContext> views))
        {
            return;
        }

        Measured2DViewContext selectedView = null;
        foreach (Measured2DViewContext view in views)
        {
            if (string.Equals(view.ViewName, viewName, StringComparison.Ordinal))
            {
                selectedView = view;
                break;
            }
        }

        if (selectedView == null || selectedView.CoordinateRoot == null)
        {
            Debug.LogError($"[MeasuredInterior] View not found: {viewName}");
            return;
        }

        if (TryResolveWallInteriorBoundsDiagnostic(selectedView))
        {
            LogWallInnerBounds("MeasuredInteriorCalibration", selectedView);
        }

        scr_MapMeasuredLayoutConfig config = LoadMeasuredLayoutConfig();
        bool hasSavedBounds = TryGetSavedInteriorBounds(
            config,
            viewName,
            out Rect initialBounds);
        string initialSource = "Saved config";
        if (!hasSavedBounds &&
            !TryGetInitialInteriorBoundsFromFloor(selectedView, out initialBounds))
        {
            initialBounds = selectedView.CoordinateRoot.rect;
            initialSource = "Coordinate Root fallback";
        }
        else if (!hasSavedBounds)
        {
            initialSource = "Current floor Image preview";
        }

        activeInteriorCalibration = new InteriorCalibrationPreviewState
        {
            ViewName = viewName,
            CoordinateRoot = selectedView.CoordinateRoot,
            Bounds = initialBounds,
            InitializedFromSavedConfig = hasSavedBounds
        };

        SceneView.duringSceneGui -= DrawInteriorCalibrationPreview;
        SceneView.duringSceneGui += DrawInteriorCalibrationPreview;
        SceneView.RepaintAll();
        LogMeasuredInteriorBounds(
            "MeasuredInteriorCalibration",
            viewName,
            selectedView.CoordinateRoot.rect,
            initialBounds);
        Debug.Log(
            $"[MeasuredInteriorCalibration] Started | View={viewName} | " +
            $"InitialSource={initialSource} | Scene values unchanged");
    }

    private static void DrawInteriorCalibrationPreview(SceneView sceneView)
    {
        if (sceneView == null || activeInteriorCalibration == null ||
            activeInteriorCalibration.CoordinateRoot == null)
        {
            SceneView.duringSceneGui -= DrawInteriorCalibrationPreview;
            return;
        }

        RectTransform root = activeInteriorCalibration.CoordinateRoot;
        Rect bounds = activeInteriorCalibration.Bounds;
        bool valid = ValidateMeasuredInteriorBounds(
            activeInteriorCalibration.ViewName,
            root.rect,
            bounds,
            false);
        Color previousColor = Handles.color;
        Handles.color = valid
            ? new Color(0.2f, 1f, 0.35f, 1f)
            : new Color(1f, 0.25f, 0.2f, 1f);
        DrawClosedOutline(GetWorldCorners(root, bounds), Handles.color, 4f);

        Vector3 rootRight = root.TransformVector(Vector3.right).normalized;
        Vector3 rootUp = root.TransformVector(Vector3.up).normalized;
        Vector3 leftWorld = root.TransformPoint(
            new Vector3(bounds.xMin, bounds.center.y, 0f));
        Vector3 rightWorld = root.TransformPoint(
            new Vector3(bounds.xMax, bounds.center.y, 0f));
        Vector3 bottomWorld = root.TransformPoint(
            new Vector3(bounds.center.x, bounds.yMin, 0f));
        Vector3 topWorld = root.TransformPoint(
            new Vector3(bounds.center.x, bounds.yMax, 0f));

        EditorGUI.BeginChangeCheck();
        Vector3 movedLeft = Handles.Slider(
            leftWorld,
            rootRight,
            HandleUtility.GetHandleSize(leftWorld) * 0.08f,
            Handles.SphereHandleCap,
            0f);
        Vector3 movedRight = Handles.Slider(
            rightWorld,
            rootRight,
            HandleUtility.GetHandleSize(rightWorld) * 0.08f,
            Handles.SphereHandleCap,
            0f);
        Vector3 movedBottom = Handles.Slider(
            bottomWorld,
            rootUp,
            HandleUtility.GetHandleSize(bottomWorld) * 0.08f,
            Handles.SphereHandleCap,
            0f);
        Vector3 movedTop = Handles.Slider(
            topWorld,
            rootUp,
            HandleUtility.GetHandleSize(topWorld) * 0.08f,
            Handles.SphereHandleCap,
            0f);
        if (EditorGUI.EndChangeCheck())
        {
            float left = root.InverseTransformPoint(movedLeft).x;
            float right = root.InverseTransformPoint(movedRight).x;
            float bottom = root.InverseTransformPoint(movedBottom).y;
            float top = root.InverseTransformPoint(movedTop).y;
            if (right > left && top > bottom &&
                IsFinite(left) && IsFinite(right) && IsFinite(bottom) && IsFinite(top))
            {
                activeInteriorCalibration.Bounds = Rect.MinMaxRect(
                    left,
                    bottom,
                    right,
                    top);
            }

            SceneView.RepaintAll();
        }

        Handles.Label(leftWorld, "Left");
        Handles.Label(rightWorld, "Right");
        Handles.Label(bottomWorld, "Bottom");
        Handles.Label(topWorld, "Top");
        Handles.color = previousColor;
    }

    private static bool TryGetInitialInteriorBoundsFromFloor(
        Measured2DViewContext view,
        out Rect bounds)
    {
        bounds = default;
        if (view == null || view.CoordinateRoot == null)
        {
            return false;
        }

        Transform current = view.CoordinateRoot;
        while (current != null &&
               !string.Equals(current.name, "Image_FactoryFloor", StringComparison.Ordinal))
        {
            current = current.parent;
        }

        RectTransform floor = current as RectTransform;
        return floor != null &&
               TryGetRectBoundsInCoordinateRoot(floor, view.CoordinateRoot, out bounds) &&
               IsFinite(bounds) && bounds.width > 0f && bounds.height > 0f;
    }

    private static scr_MapMeasuredLayoutConfig LoadMeasuredLayoutConfig()
    {
        return AssetDatabase.LoadAssetAtPath<scr_MapMeasuredLayoutConfig>(
            MeasuredLayoutConfigAssetPath);
    }

    private static scr_MapMeasuredLayoutConfig LoadOrCreateMeasuredLayoutConfig()
    {
        scr_MapMeasuredLayoutConfig config = LoadMeasuredLayoutConfig();
        if (config != null)
        {
            return config;
        }

        EnsureAssetFolder(MeasuredLayoutConfigFolderPath);
        config = ScriptableObject.CreateInstance<scr_MapMeasuredLayoutConfig>();
        AssetDatabase.CreateAsset(config, MeasuredLayoutConfigAssetPath);
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        return config;
    }

    private static bool TryGetSavedInteriorBounds(
        scr_MapMeasuredLayoutConfig config,
        string viewName,
        out Rect bounds)
    {
        bounds = default;
        if (config == null)
        {
            return false;
        }

        if (string.Equals(viewName, "Full2D", StringComparison.Ordinal))
        {
            bounds = config.Full2DInteriorBounds;
            return config.HasFull2DInteriorBounds;
        }

        if (string.Equals(viewName, "MapStatus", StringComparison.Ordinal))
        {
            bounds = config.MapStatusInteriorBounds;
            return config.HasMapStatusInteriorBounds;
        }

        return false;
    }

    private static bool ValidateMeasuredInteriorBounds(
        string viewName,
        Rect rootBounds,
        Rect measuredBounds,
        bool logFailure)
    {
        bool dimensionsValid = IsFinite(measuredBounds) &&
                               measuredBounds.xMax > measuredBounds.xMin &&
                               measuredBounds.yMax > measuredBounds.yMin &&
                               measuredBounds.width > 0f &&
                               measuredBounds.height > 0f;
        bool insideRoot = dimensionsValid &&
                          RectContainsRect(rootBounds, measuredBounds, BoundsTolerance);
        if (logFailure && !dimensionsValid)
        {
            Debug.LogWarning(
                $"[MeasuredInterior] Save blocked: invalid bounds | " +
                $"View={viewName} | Bounds={FormatRect(measuredBounds)}");
        }
        else if (logFailure && !insideRoot)
        {
            Debug.LogWarning(
                $"[MeasuredInterior] Save blocked: bounds exceed Coordinate Root | " +
                $"View={viewName} | Root={FormatRect(rootBounds)} | " +
                $"Measured={FormatRect(measuredBounds)}");
        }

        return dimensionsValid && insideRoot;
    }

    private static void LogMeasuredInteriorBounds(
        string category,
        string viewName,
        Rect rootBounds,
        Rect measuredBounds)
    {
        float unitsPerCmX = measuredBounds.width / InteriorWidthCm;
        float unitsPerCmY = measuredBounds.height / InteriorHeightCm;
        Debug.Log(
            $"[{category}] View={viewName} | " +
            $"CoordinateRootBounds={FormatRect(rootBounds)} | " +
            $"MeasuredInteriorBounds={FormatRect(measuredBounds)} | " +
            $"InteriorUISize={FormatVector2(measuredBounds.size)} | " +
            $"UnitsPerCm=({FormatFloat(unitsPerCmX)},{FormatFloat(unitsPerCmY)})");
    }

    private static void PreviewMeasured2DLayoutForView(string requestedViewName)
    {
        activeInteriorCalibration = null;
        SceneView.duringSceneGui -= DrawInteriorCalibrationPreview;
        activeMeasured3DPreview = null;
        activeFactory3DInteriorCalibration = null;
        SceneView.duringSceneGui -= DrawMeasured3DPreview;
        SceneView.duringSceneGui -= DrawFactory3DInteriorCalibration;
        SetMeasuredPreview(null);
        if (!TryGetEditableControlTowerScene(out Scene scene) ||
            !TryBuildMeasured2DLayout(scene, out List<Measured2DViewContext> views,
                out List<Measured2DResult> results))
        {
            return;
        }

        bool previewAll = string.IsNullOrEmpty(requestedViewName);
        List<Measured2DViewContext> previewViews = new List<Measured2DViewContext>();
        foreach (Measured2DViewContext view in views)
        {
            if (previewAll || string.Equals(
                    view.ViewName,
                    requestedViewName,
                    StringComparison.Ordinal))
            {
                previewViews.Add(view);
            }
        }

        List<Measured2DResult> previewResults = new List<Measured2DResult>();
        foreach (Measured2DResult result in results)
        {
            if (previewAll || string.Equals(
                    result.ViewName,
                    requestedViewName,
                    StringComparison.Ordinal))
            {
                previewResults.Add(result);
            }
        }

        int expectedCount = previewAll ? 12 : 4;
        if (previewViews.Count != (previewAll ? 3 : 1) ||
            previewResults.Count != expectedCount)
        {
            Debug.LogError(
                $"[Measured2DPreview] View filter failed | " +
                $"Requested={(previewAll ? "All" : requestedViewName)} | " +
                $"Views={previewViews.Count} | Objects={previewResults.Count}");
            return;
        }

        // SceneView handles stay single-view only. "All" remains a Console-wide audit.
        SetMeasuredPreview(previewAll ? null : previewResults);
        LogMeasured2DResults("Measured2DPreview", previewViews, previewResults);
        LogEntryWarnings("Measured2DPreview", previewViews);
        bool applyReady = ValidateMeasured2DApplyReadiness(
            previewViews,
            previewResults,
            false);
        Debug.Log(
            $"[Measured2DPreview] Scene values unchanged | " +
            $"View={(previewAll ? "All" : requestedViewName)} | Objects={previewResults.Count} | " +
            "CurrentRect=orange-dotted | ActualImage=yellow | Target=cyan | " +
            $"CompensatedRect=purple-dotted | Interior=green(saved)/red(unsaved) | " +
            $"SceneHandles={(previewAll ? "none (single-view only)" : "enabled")} | " +
            $"ApplyReady={applyReady}");
    }

    [MenuItem(ApplyMeasured2DMenuPath)]
    public static void ApplyMeasured2DLayout()
    {
        if (!Measured2DApplyEnabled)
        {
            Debug.LogError(
                "[Measured2DApply] BLOCKED: measured-interior Composite layout is preview-only. " +
                "No Scene or RectTransform value was changed.");
            return;
        }

        if (!TryGetEditableControlTowerScene(out Scene scene))
        {
            return;
        }

        if (!TryResolveLatestBackupAsset(out scr_MapLayoutBackupAsset backup, out string backupPath) ||
            !ValidateBackupHeader(backup, scene, backupPath))
        {
            Debug.LogError("[Measured2DApply] A current-Scene backup is required. Nothing was changed.");
            return;
        }

        if (!TryCollectLayoutTargets(scene, out List<LayoutTarget> allLayoutTargets) ||
            !TryBuildMeasured2DLayout(scene, out List<Measured2DViewContext> views,
                out List<Measured2DResult> results))
        {
            Debug.LogError("[Measured2DApply] Target validation failed. Nothing was changed.");
            return;
        }

        if (!ValidateMeasured2DApplyReadiness(views, results, true))
        {
            Debug.LogError(
                "[Measured2DApply] Readiness checks failed. Nothing was changed.");
            return;
        }

        HashSet<int> modifiedTargetIds = new HashSet<int>();
        foreach (Measured2DResult result in results)
        {
            if (result.Target == null || !modifiedTargetIds.Add(result.Target.GetInstanceID()))
            {
                Debug.LogError("[Measured2DApply] Missing or duplicate measured target. Nothing was changed.");
                return;
            }
        }

        if (modifiedTargetIds.Count != 12 ||
            !TryCaptureApplyProtectionStates(
                scene,
                allLayoutTargets,
                views,
                modifiedTargetIds,
                out List<ApplyProtectionState> protectionStates) ||
            !TryCaptureMapControllerSignatures(scene, out List<ComponentSignature> controllerSignatures))
        {
            Debug.LogError("[Measured2DApply] Protection snapshot failed. Nothing was changed.");
            return;
        }

        UnityEngine.Object[] undoTargets = new UnityEngine.Object[results.Count];
        for (int index = 0; index < results.Count; index++)
        {
            undoTargets[index] = results[index].Target;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Apply Measured 2D Map Layout");
        Undo.RegisterCompleteObjectUndo(undoTargets, "Apply Measured 2D Map Layout");

        foreach (Measured2DResult result in results)
        {
            result.Target.sizeDelta = result.TargetSizeDelta;
            result.Target.anchoredPosition = result.TargetAnchoredPosition;
            EditorUtility.SetDirty(result.Target);
            PrefabUtility.RecordPrefabInstancePropertyModifications(result.Target);
        }

        bool boundsOk = ValidateAppliedMeasuredBounds(results);
        bool protectedPropertiesOk = ValidateApplyProtectionStates(protectionStates);
        bool calibrationOk = ValidateMapControllerSignatures(controllerSignatures);
        if (!boundsOk || !protectedPropertiesOk || !calibrationOk)
        {
            Undo.RevertAllDownToGroup(undoGroup);
            Debug.LogError(
                "[Measured2DApply] Validation failed. All Apply changes were reverted; Scene was not saved.");
            return;
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        LogMeasured2DResults("Measured2DApply", views, results);
        LogEntryWarnings("Measured2DApply", views);
        if (TryBuildMeasured2DLayout(
                scene,
                out _,
                out List<Measured2DResult> appliedPreviewResults))
        {
            SetMeasuredPreview(appliedPreviewResults);
        }
        else
        {
            SetMeasuredPreview(results);
        }

        Debug.Log(
            "[Measured2DApply] Modified RectTransforms = 12\n" +
            "[Measured2DApply] Modified Properties = anchoredPosition, sizeDelta only\n" +
            "[Measured2DApply] Preserved Anchors = OK\n" +
            "[Measured2DApply] Preserved Pivots = OK\n" +
            "[Measured2DApply] Preserved Scale = OK\n" +
            "[Measured2DApply] Preserved Rotation = OK\n" +
            "[Measured2DApply] Entry unchanged = OK\n" +
            "[Measured2DApply] Calibration unchanged = OK\n" +
            "[Measured2DApply] Factory 3D unchanged = OK\n" +
            "[Measured2DApply] Scene saved = NO (review the Scene, then press Ctrl+S)");
        SceneView.RepaintAll();
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        switch (state)
        {
            case PlayModeStateChange.ExitingEditMode:
                CapturePlayModeStaticLayoutBaseline();
                break;

            case PlayModeStateChange.EnteredPlayMode:
                reportedPlayModeDifferences.Clear();
                nextPlayModeValidationTime = EditorApplication.timeSinceStartup + 0.5d;
                EditorApplication.update -= ValidatePlayModeStaticLayoutOnInterval;
                EditorApplication.update += ValidatePlayModeStaticLayoutOnInterval;
                break;

            case PlayModeStateChange.ExitingPlayMode:
                ValidatePlayModeStaticLayout();
                EditorApplication.update -= ValidatePlayModeStaticLayoutOnInterval;
                break;

            case PlayModeStateChange.EnteredEditMode:
                EditorApplication.update -= ValidatePlayModeStaticLayoutOnInterval;
                SessionState.EraseString(PlayModeBaselineSessionKey);
                reportedPlayModeDifferences.Clear();
                break;
        }
    }

    private static void CapturePlayModeStaticLayoutBaseline()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded ||
            !string.Equals(scene.path, ControlTowerScenePath, StringComparison.Ordinal) ||
            !TryCollectMeasured2DViewObjects(scene, out List<Measured2DViewContext> views))
        {
            SessionState.EraseString(PlayModeBaselineSessionKey);
            return;
        }

        PlayModeLayoutBaseline baseline = new PlayModeLayoutBaseline
        {
            ScenePath = scene.path
        };
        foreach (Measured2DViewContext view in views)
        {
            for (int index = 0; index < view.Facilities.Length; index++)
            {
                AddPlayModeBaselineSnapshot(scene, view.ViewName, view.Facilities[index], baseline);
            }

            AddPlayModeBaselineSnapshot(scene, view.ViewName, view.Entry, baseline);
        }

        SessionState.SetString(PlayModeBaselineSessionKey, JsonUtility.ToJson(baseline));
    }

    private static void AddPlayModeBaselineSnapshot(
        Scene scene,
        string viewName,
        RectTransform target,
        PlayModeLayoutBaseline baseline)
    {
        scr_MapLayoutObjectSnapshot snapshot = new scr_MapLayoutObjectSnapshot();
        snapshot.Capture(
            "PlayMode/" + viewName,
            scene.path,
            BuildHierarchyPath(target),
            target.parent != null ? BuildHierarchyPath(target.parent) : string.Empty,
            target);
        baseline.Snapshots.Add(snapshot);
    }

    private static void ValidatePlayModeStaticLayoutOnInterval()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorApplication.update -= ValidatePlayModeStaticLayoutOnInterval;
            return;
        }

        if (EditorApplication.timeSinceStartup < nextPlayModeValidationTime)
        {
            return;
        }

        nextPlayModeValidationTime = EditorApplication.timeSinceStartup + 1d;
        ValidatePlayModeStaticLayout();
    }

    private static void ValidatePlayModeStaticLayout()
    {
        string json = SessionState.GetString(PlayModeBaselineSessionKey, string.Empty);
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        PlayModeLayoutBaseline baseline = JsonUtility.FromJson<PlayModeLayoutBaseline>(json);
        Scene scene = SceneManager.GetActiveScene();
        if (baseline == null || !scene.IsValid() || !scene.isLoaded ||
            !string.Equals(scene.path, baseline.ScenePath, StringComparison.Ordinal))
        {
            return;
        }

        foreach (scr_MapLayoutObjectSnapshot snapshot in baseline.Snapshots)
        {
            RectTransform target = ResolveHierarchyPath(scene, snapshot.HierarchyPath) as RectTransform;
            if (target == null)
            {
                WarnPlayModeDifference(snapshot.HierarchyPath, "Object", "Present", "Missing");
                continue;
            }

            WarnPlayModeDifferenceIfChanged(
                snapshot.HierarchyPath,
                "Parent Path",
                snapshot.ParentPath,
                target.parent != null ? BuildHierarchyPath(target.parent) : string.Empty);
            WarnPlayModeDifferenceIfChanged(
                snapshot.HierarchyPath,
                "anchoredPosition",
                snapshot.AnchoredPosition,
                target.anchoredPosition);
            WarnPlayModeDifferenceIfChanged(
                snapshot.HierarchyPath,
                "sizeDelta",
                snapshot.SizeDelta,
                target.sizeDelta);
            WarnPlayModeDifferenceIfChanged(
                snapshot.HierarchyPath,
                "anchorMin",
                snapshot.AnchorMin,
                target.anchorMin);
            WarnPlayModeDifferenceIfChanged(
                snapshot.HierarchyPath,
                "anchorMax",
                snapshot.AnchorMax,
                target.anchorMax);
            WarnPlayModeDifferenceIfChanged(
                snapshot.HierarchyPath,
                "pivot",
                snapshot.Pivot,
                target.pivot);
            WarnPlayModeDifferenceIfChanged(
                snapshot.HierarchyPath,
                "localScale",
                snapshot.LocalScale,
                target.localScale);
            WarnPlayModeDifferenceIfChanged(
                snapshot.HierarchyPath,
                "localRotation",
                snapshot.LocalRotation,
                target.localRotation);
        }
    }

    private static void WarnPlayModeDifferenceIfChanged(
        string path,
        string property,
        string expected,
        string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            WarnPlayModeDifference(path, property, expected, actual);
        }
    }

    private static void WarnPlayModeDifferenceIfChanged(
        string path,
        string property,
        Vector2 expected,
        Vector2 actual)
    {
        if (!ExactEquals(expected, actual))
        {
            WarnPlayModeDifference(path, property, FormatVector2(expected), FormatVector2(actual));
        }
    }

    private static void WarnPlayModeDifferenceIfChanged(
        string path,
        string property,
        Vector3 expected,
        Vector3 actual)
    {
        if (!ExactEquals(expected, actual))
        {
            WarnPlayModeDifference(path, property, FormatVector3(expected), FormatVector3(actual));
        }
    }

    private static void WarnPlayModeDifferenceIfChanged(
        string path,
        string property,
        Quaternion expected,
        Quaternion actual)
    {
        if (!ExactEquals(expected, actual))
        {
            WarnPlayModeDifference(
                path,
                property,
                FormatQuaternion(expected),
                FormatQuaternion(actual));
        }
    }

    private static void WarnPlayModeDifference(
        string path,
        string property,
        string editModeValue,
        string playModeValue)
    {
        string warningKey = path + "|" + property;
        if (!reportedPlayModeDifferences.Add(warningKey))
        {
            return;
        }

        Debug.LogWarning(
            $"[Measured2DPlayGuard] Edit/Play static layout mismatch | Object={path} | " +
            $"Property={property} | Edit={editModeValue} | Play={playModeValue}");
    }

    private static bool TryGetEditableControlTowerScene(out Scene scene)
    {
        if (!TryGetEditableActiveScene(out scene))
        {
            return false;
        }

        if (!string.Equals(scene.path, ControlTowerScenePath, StringComparison.Ordinal))
        {
            Debug.LogError(
                $"[Measured2D] Open {ControlTowerScenePath} before using the measured-layout tool. " +
                $"Current={scene.path}");
            return false;
        }

        return true;
    }

    private static bool TryBuildMeasured2DLayout(
        Scene scene,
        out List<Measured2DViewContext> views,
        out List<Measured2DResult> results)
    {
        results = new List<Measured2DResult>(12);
        if (!TryCollectMeasured2DViews(scene, out views))
        {
            return false;
        }

        foreach (Measured2DViewContext view in views)
        {
            Rect interiorBounds = view.InteriorBounds;
            if (!IsFinite(interiorBounds) ||
                interiorBounds.width <= 0f || interiorBounds.height <= 0f)
            {
                Debug.LogError(
                    $"[Measured2D] Invalid interior bounds: " +
                    $"{BuildHierarchyPath(view.CoordinateRoot)} " +
                    $"Bounds={FormatRect(interiorBounds)}");
                return false;
            }

            view.UnitsPerCmX = interiorBounds.width / InteriorWidthCm;
            view.UnitsPerCmY = interiorBounds.height / InteriorHeightCm;

            for (int facilityIndex = 0;
                 facilityIndex < MeasuredFacilityDefinitions.Length;
                 facilityIndex++)
            {
                MeasuredFacilityDefinition definition =
                    MeasuredFacilityDefinitions[facilityIndex];
                RectTransform target = view.Facilities[facilityIndex];
                Rect targetBounds = Rect.MinMaxRect(
                    interiorBounds.xMin + definition.LeftCm * view.UnitsPerCmX,
                    interiorBounds.yMin + definition.BottomCm * view.UnitsPerCmY,
                    interiorBounds.xMin + definition.RightCm * view.UnitsPerCmX,
                    interiorBounds.yMin + definition.TopCm * view.UnitsPerCmY);

                if (!TryGetRectBoundsInCoordinateRoot(
                        target,
                        view.CoordinateRoot,
                        out Rect currentBounds) ||
                    !TryCalculateRectTransformValues(
                        target,
                        view.CoordinateRoot,
                        targetBounds,
                        out Vector2 targetAnchoredPosition,
                        out Vector2 targetSizeDelta))
                {
                    Debug.LogError(
                        $"[Measured2D] Cannot calculate target Rect: " +
                        $"{view.ViewName}/{BuildHierarchyPath(target)}");
                    return false;
                }

                if (!IsFinite(targetBounds) || !IsFinite(targetAnchoredPosition) ||
                    !IsFinite(targetSizeDelta))
                {
                    Debug.LogError(
                        $"[Measured2D] NaN or Infinity detected: {BuildHierarchyPath(target)}");
                    return false;
                }

                Measured2DResult result = new Measured2DResult
                {
                    ViewName = view.ViewName,
                    FacilityId = definition.Id,
                    CoordinateRoot = view.CoordinateRoot,
                    Target = target,
                    CurrentBounds = currentBounds,
                    CurrentImageDrawBounds = currentBounds,
                    TargetBounds = targetBounds,
                    InteriorBounds = interiorBounds,
                    InteriorBoundsSaved = view.HasSavedInteriorBounds,
                    TargetAnchoredPosition = targetAnchoredPosition,
                    TargetSizeDelta = targetSizeDelta,
                    HasPhysicalTarget = true
                };

                if (!TryAnalyzeMeasuredImage(result))
                {
                    Debug.LogError(
                        $"[Measured2D] Image draw analysis failed: " +
                        $"{view.ViewName}/{BuildHierarchyPath(target)}");
                    return false;
                }

                results.Add(result);
            }

            if (!TryAnalyzeEntryImage(view))
            {
                return false;
            }
        }

        return results.Count == 12;
    }

    private static bool TryCollectMeasured2DViews(
        Scene scene,
        out List<Measured2DViewContext> views)
    {
        if (!TryCollectMeasured2DViewObjects(scene, out views))
        {
            return false;
        }

        Measured2DViewContext full2D = views[0];
        Measured2DViewContext miniMap = views[1];
        Measured2DViewContext mapStatus = views[2];

        // Wall geometry is retained as diagnostics only and never supplies layout units.
        TryResolveWallInteriorBoundsDiagnostic(full2D);
        TryResolveWallInteriorBoundsDiagnostic(mapStatus);

        scr_MapMeasuredLayoutConfig config = LoadMeasuredLayoutConfig();
        bool fullSaved = config != null && config.HasFull2DInteriorBounds;
        bool mapStatusSaved = config != null && config.HasMapStatusInteriorBounds;
        Rect fullBounds;
        Rect mapStatusBounds;
        if (fullSaved)
        {
            fullBounds = config.Full2DInteriorBounds;
        }
        else if (!TryGetInitialInteriorBoundsFromFloor(full2D, out fullBounds))
        {
            fullBounds = full2D.CoordinateRoot.rect;
        }

        if (mapStatusSaved)
        {
            mapStatusBounds = config.MapStatusInteriorBounds;
        }
        else if (!TryGetInitialInteriorBoundsFromFloor(mapStatus, out mapStatusBounds))
        {
            mapStatusBounds = mapStatus.CoordinateRoot.rect;
        }

        if (!fullSaved || !mapStatusSaved)
        {
            Debug.LogWarning(
                $"[Measured2D] Unsaved Interior Bounds use floor/root Preview fallback only. " +
                $"Asset={MeasuredLayoutConfigAssetPath} | " +
                $"Full2D={fullSaved} | MapStatus={mapStatusSaved} | Apply remains blocked");
        }

        if (!TryAssignInteriorBounds(
                full2D,
                fullBounds,
                fullSaved ? "Measured layout config" : "Unsaved floor preview",
                fullSaved) ||
            !TryAssignInteriorBounds(
                mapStatus,
                mapStatusBounds,
                mapStatusSaved ? "Measured layout config" : "Unsaved floor preview",
                mapStatusSaved))
        {
            return false;
        }

        return TryProjectInteriorBounds(full2D, miniMap);
    }

    private static bool TryCollectMeasured2DViewObjects(
        Scene scene,
        out List<Measured2DViewContext> views)
    {
        views = new List<Measured2DViewContext>(3);
        bool valid = true;

        Transform factoryView = FindUniqueSceneTransform(scene, "Panel_Main_FactoryView");
        Transform fullMapRoot = FindUniqueDescendant(factoryView, "RealMapLayoutRoot", "Full2D");
        valid &= TryCreateMeasured2DView(
            scene,
            "Full2D",
            fullMapRoot,
            FullMapFacilityNames,
            views);

        Transform miniMapPanel = FindUniqueSceneTransform(scene, "Panel_Mini2DMap");
        Transform miniMapRoot = FindUniqueDescendant(
            miniMapPanel,
            "Image_Mini2DMapArea",
            "MiniMap");
        valid &= TryCreateMeasured2DView(
            scene,
            "MiniMap",
            miniMapRoot,
            MiniMapFacilityNames,
            views);

        Transform mapStatusPanel = FindUniqueSceneTransform(scene, "Panel_MapPreview2DContent");
        Transform mapStatusRoot = FindUniqueDescendant(
            mapStatusPanel,
            "RealMapLayoutRoot",
            "MapStatus");
        valid &= TryCreateMeasured2DView(
            scene,
            "MapStatus",
            mapStatusRoot,
            FullMapFacilityNames,
            views);

        if (!valid || views.Count != 3)
        {
            return false;
        }

        return true;
    }

    private static bool TryCreateMeasured2DView(
        Scene scene,
        string viewName,
        Transform coordinateRootTransform,
        IReadOnlyList<string> facilityNames,
        ICollection<Measured2DViewContext> views)
    {
        RectTransform coordinateRoot = coordinateRootTransform as RectTransform;
        if (coordinateRoot == null || coordinateRoot.gameObject.scene != scene ||
            facilityNames.Count < 5)
        {
            Debug.LogError($"[Measured2D] Coordinate Root is invalid: {viewName}");
            return false;
        }

        RectTransform[] facilities = new RectTransform[4];
        for (int index = 0; index < facilities.Length; index++)
        {
            facilities[index] =
                FindUniqueDescendant(coordinateRoot, facilityNames[index], viewName) as RectTransform;
            if (facilities[index] == null)
            {
                Debug.LogError(
                    $"[Measured2D] Required RectTransform is missing: {viewName}/{facilityNames[index]}");
                return false;
            }
        }

        RectTransform entry =
            FindUniqueDescendant(coordinateRoot, facilityNames[4], viewName) as RectTransform;
        if (entry == null)
        {
            Debug.LogError($"[Measured2D] Entry RectTransform is missing: {viewName}/{facilityNames[4]}");
            return false;
        }

        views.Add(new Measured2DViewContext
        {
            ViewName = viewName,
            CoordinateRoot = coordinateRoot,
            Facilities = facilities,
            Entry = entry,
            InteriorWalls = Array.Empty<RectTransform>(),
            InteriorWallBounds = Array.Empty<Rect>(),
            HasDiagnosticWallInteriorBounds = false,
            HasSavedInteriorBounds = false,
            InteriorBoundsSource = string.Empty
        });
        return true;
    }

    private static bool TryResolveWallInteriorBoundsDiagnostic(Measured2DViewContext view)
    {
        if (view == null || view.CoordinateRoot == null)
        {
            return false;
        }

        RectTransform[] walls = new RectTransform[InteriorWallNames.Length];
        Rect[] wallBounds = new Rect[InteriorWallNames.Length];
        for (int index = 0; index < InteriorWallNames.Length; index++)
        {
            walls[index] = FindUniqueDescendant(
                               view.CoordinateRoot,
                               InteriorWallNames[index],
                               view.ViewName) as RectTransform;
            if (walls[index] == null ||
                !TryGetRectBoundsInCoordinateRoot(
                    walls[index],
                    view.CoordinateRoot,
                    out wallBounds[index]))
            {
                Debug.LogWarning(
                    $"[Measured2DWallDiagnostic] Interior wall is missing or invalid: " +
                    $"{view.ViewName}/{InteriorWallNames[index]}");
                return false;
            }
        }

        float leftInner = wallBounds[0].xMax;
        float rightInner = wallBounds[1].xMin;
        float bottomInner = wallBounds[2].yMax;
        float topInner = wallBounds[3].yMin;
        Rect interiorBounds = Rect.MinMaxRect(
            leftInner,
            bottomInner,
            rightInner,
            topInner);
        if (!IsFinite(interiorBounds) ||
            interiorBounds.width <= 0f || interiorBounds.height <= 0f)
        {
            Debug.LogWarning(
                $"[Measured2DWallDiagnostic] Wall inner faces do not form a valid interior: " +
                $"View={view.ViewName} Bounds={FormatRect(interiorBounds)}");
            return false;
        }

        Rect rootRect = view.CoordinateRoot.rect;
        if (!IsFinite(rootRect) || rootRect.width <= 0f || rootRect.height <= 0f)
        {
            Debug.LogError($"[Measured2D] Invalid Coordinate Root: {view.ViewName}");
            return false;
        }

        view.InteriorWalls = walls;
        view.InteriorWallBounds = wallBounds;
        view.HasDiagnosticWallInteriorBounds = true;
        view.DiagnosticWallInteriorBounds = interiorBounds;
        return true;
    }

    private static bool TryAssignInteriorBounds(
        Measured2DViewContext view,
        Rect measuredBounds,
        string source,
        bool hasSavedBounds)
    {
        if (view == null || view.CoordinateRoot == null ||
            !ValidateMeasuredInteriorBounds(
                view.ViewName,
                view.CoordinateRoot.rect,
                measuredBounds,
                true))
        {
            return false;
        }

        Rect rootBounds = view.CoordinateRoot.rect;
        view.InteriorBounds = measuredBounds;
        view.NormalizedInteriorBounds = Rect.MinMaxRect(
            Mathf.InverseLerp(rootBounds.xMin, rootBounds.xMax, measuredBounds.xMin),
            Mathf.InverseLerp(rootBounds.yMin, rootBounds.yMax, measuredBounds.yMin),
            Mathf.InverseLerp(rootBounds.xMin, rootBounds.xMax, measuredBounds.xMax),
            Mathf.InverseLerp(rootBounds.yMin, rootBounds.yMax, measuredBounds.yMax));
        view.InteriorBoundsSource = source;
        view.HasSavedInteriorBounds = hasSavedBounds;
        LogMeasuredInteriorBounds(
            "MeasuredInteriorLoad",
            view.ViewName,
            rootBounds,
            measuredBounds);
        return IsFinite(view.NormalizedInteriorBounds);
    }

    private static bool TryProjectInteriorBounds(
        Measured2DViewContext source,
        Measured2DViewContext destination)
    {
        if (source == null || destination == null ||
            source.CoordinateRoot == null || destination.CoordinateRoot == null ||
            !IsFinite(source.NormalizedInteriorBounds))
        {
            return false;
        }

        Rect destinationRoot = destination.CoordinateRoot.rect;
        Rect normalized = source.NormalizedInteriorBounds;
        Rect projected = Rect.MinMaxRect(
            Mathf.Lerp(destinationRoot.xMin, destinationRoot.xMax, normalized.xMin),
            Mathf.Lerp(destinationRoot.yMin, destinationRoot.yMax, normalized.yMin),
            Mathf.Lerp(destinationRoot.xMin, destinationRoot.xMax, normalized.xMax),
            Mathf.Lerp(destinationRoot.yMin, destinationRoot.yMax, normalized.yMax));
        if (!IsFinite(projected) || projected.width <= 0f || projected.height <= 0f)
        {
            Debug.LogError(
                $"[Measured2D] MiniMap interior projection failed: {FormatRect(projected)}");
            return false;
        }

        destination.InteriorWalls = Array.Empty<RectTransform>();
        destination.InteriorWallBounds = Array.Empty<Rect>();
        destination.InteriorBounds = projected;
        destination.NormalizedInteriorBounds = normalized;
        destination.InteriorBoundsSource = "Full2D measured normalized bounds";
        destination.HasSavedInteriorBounds = source.HasSavedInteriorBounds;
        return true;
    }

    private static bool TryAnalyzeEntryImage(Measured2DViewContext view)
    {
        if (view == null || view.Entry == null || view.CoordinateRoot == null ||
            !TryGetRectBoundsInCoordinateRoot(
                view.Entry,
                view.CoordinateRoot,
                out Rect currentBounds))
        {
            Debug.LogError($"[Measured2D] Entry Image analysis failed: {view?.ViewName}");
            return false;
        }

        Measured2DResult result = new Measured2DResult
        {
            ViewName = view.ViewName,
            FacilityId = "Entry",
            CoordinateRoot = view.CoordinateRoot,
            Target = view.Entry,
            CurrentBounds = currentBounds,
            CurrentImageDrawBounds = currentBounds,
            InteriorBounds = view.InteriorBounds,
            InteriorBoundsSaved = view.HasSavedInteriorBounds,
            HasPhysicalTarget = false,
            ApplyPropertyProposal = "UNCHANGED: Entry physical reference is unresolved"
        };

        if (!TryAnalyzeMeasuredImage(result))
        {
            Debug.LogError(
                $"[Measured2D] Entry Image draw analysis failed: " +
                $"{view.ViewName}/{BuildHierarchyPath(view.Entry)}");
            return false;
        }

        view.EntryImageAnalysis = result;
        return true;
    }

    private static bool TryAnalyzeMeasuredImage(Measured2DResult result)
    {
        if (result == null || result.Target == null || result.CoordinateRoot == null)
        {
            return false;
        }

        Image primaryImage = result.Target.GetComponent<Image>();
        result.HasImage = primaryImage != null;
        result.ImageType = primaryImage != null ? primaryImage.type.ToString() : "Missing";
        result.PreserveAspect = primaryImage != null && primaryImage.preserveAspect;
        result.UseSpriteMesh = primaryImage != null && primaryImage.useSpriteMesh;
        result.SpriteRect = default;
        result.SpriteRectSize = Vector2.zero;
        result.SpriteBoundsSize = Vector3.zero;
        result.TextureSize = Vector2.zero;
        result.SpriteAspect = 0f;
        result.RectTransformAspect = result.Target != null &&
                                     Mathf.Abs(result.Target.rect.height) > 0.000001f
            ? Mathf.Abs(result.Target.rect.width / result.Target.rect.height)
            : 0f;
        result.SpriteAlphaPixelBounds = default;
        result.SpriteAlphaNormalizedBounds = new Rect(0f, 0f, 1f, 1f);
        result.SpriteAlphaCenterOffsetNormalized = Vector2.zero;
        result.SpriteAlphaDetail = "No Sprite";
        result.SuggestedRectTransformSizeInRoot = result.HasPhysicalTarget
            ? result.TargetSize
            : Vector2.zero;
        result.HasCompensatedTargetRect = false;
        CapturePrimaryImageDiagnostics(primaryImage, result);

        if (!TryCalculateCompositeVisibleBounds(
                result.Target,
                result.CoordinateRoot,
                out Rect compositeBounds,
                out CompositeBoundsAccumulator composite))
        {
            result.CurrentImageDrawBounds = result.CurrentBounds;
            result.CompositeVisibleBoundsValid = false;
            result.ImageDrawValidationPassed = false;
            result.CompositeVisibleDetail =
                "No validated Image, RawImage, SpriteRenderer, MeshRenderer, " +
                "or SkinnedMeshRenderer was found";
            result.ApplyPropertyProposal =
                "BLOCKED: facility Composite Visible Bounds are unavailable";
            return true;
        }

        result.CurrentImageDrawBounds = compositeBounds;
        result.CompositeVisualCount = composite.VisualCount;
        result.CompositeVisibleBoundsValid = composite.ValidationPassed;
        result.ImageDrawValidationPassed = composite.ValidationPassed;
        result.CompositeVisibleDetail =
            $"SpriteImages={composite.SpriteImageCount}, " +
            $"SolidColorImages={composite.SolidColorImageCount}, " +
            $"RawImages={composite.RawImageCount}, " +
            $"SpriteRenderers={composite.SpriteRendererCount}, " +
            $"MeshRenderers={composite.MeshRendererCount}, " +
            $"SkinnedMeshRenderers={composite.SkinnedMeshRendererCount}, " +
            $"SkippedSpriteLessImages={composite.SkippedSpriteLessImageCount}, " +
            $"Unverified={composite.UnverifiedVisualCount}, " +
            $"Visuals=[{string.Join("; ", composite.VisualDetails)}]";

        if (!result.HasPhysicalTarget)
        {
            result.ApplyPropertyProposal = result.ImageDrawValidationPassed
                ? "UNCHANGED: Entry Composite Bounds analyzed; physical target unresolved"
                : "UNCHANGED: Entry Composite Bounds require review; physical target unresolved";
            return true;
        }

        if (!result.ImageDrawValidationPassed)
        {
            result.ApplyPropertyProposal =
                "BLOCKED: one or more Composite visuals could not be alpha/bounds verified";
            return true;
        }

        Rect currentRootBounds = result.CurrentBounds;
        if (!IsFinite(currentRootBounds) ||
            currentRootBounds.width <= 0.000001f ||
            currentRootBounds.height <= 0.000001f)
        {
            result.ImageDrawValidationPassed = false;
            result.ApplyPropertyProposal =
                "BLOCKED: current facility Root bounds are invalid";
            return true;
        }

        Rect normalizedComposite = Rect.MinMaxRect(
            (compositeBounds.xMin - currentRootBounds.xMin) / currentRootBounds.width,
            (compositeBounds.yMin - currentRootBounds.yMin) / currentRootBounds.height,
            (compositeBounds.xMax - currentRootBounds.xMin) / currentRootBounds.width,
            (compositeBounds.yMax - currentRootBounds.yMin) / currentRootBounds.height);
        result.CompositeNormalizedBounds = normalizedComposite;
        if (!IsFinite(normalizedComposite) ||
            normalizedComposite.width <= 0.000001f ||
            normalizedComposite.height <= 0.000001f)
        {
            result.ImageDrawValidationPassed = false;
            result.ApplyPropertyProposal =
                "BLOCKED: Composite Visible Bounds cannot be normalized to the facility Root";
            return true;
        }

        float compensatedWidth =
            result.TargetBounds.width / normalizedComposite.width;
        float compensatedHeight =
            result.TargetBounds.height / normalizedComposite.height;
        Rect compensatedBounds = new Rect(
            result.TargetBounds.xMin -
            normalizedComposite.xMin * compensatedWidth,
            result.TargetBounds.yMin -
            normalizedComposite.yMin * compensatedHeight,
            compensatedWidth,
            compensatedHeight);

        Rect predictedVisibleBounds = RemapNormalizedRect(
            compensatedBounds,
            normalizedComposite);

        if (!IsFinite(compensatedBounds) ||
            !RectApproximately(
                predictedVisibleBounds,
                result.TargetBounds,
                BoundsTolerance) ||
            !TryCalculateRectTransformValues(
                result.Target,
                result.CoordinateRoot,
                compensatedBounds,
                out Vector2 compensatedAnchoredPosition,
                out Vector2 compensatedSizeDelta))
        {
            result.ImageDrawValidationPassed = false;
            result.ApplyPropertyProposal =
                "BLOCKED: compensated RectTransform values are NaN, Infinity, or unsupported";
            return true;
        }

        result.CompensatedTargetRectBounds = compensatedBounds;
        result.CompensatedTargetAnchoredPosition = compensatedAnchoredPosition;
        result.CompensatedTargetSizeDelta = compensatedSizeDelta;
        result.AnchoredPositionCorrection =
            compensatedAnchoredPosition - result.Target.anchoredPosition;
        result.SuggestedRectTransformSizeInRoot = compensatedBounds.size;
        result.HasCompensatedTargetRect = true;
        result.ApplyPropertyProposal =
            "PREVIEW ONLY: Composite-normalized anchoredPosition + sizeDelta";

        return true;
    }

    private static void CapturePrimaryImageDiagnostics(Image image, Measured2DResult result)
    {
        if (image == null)
        {
            result.SpriteAlphaDetail = "Facility Root has no Image component";
            return;
        }

        Sprite sprite = image.overrideSprite != null ? image.overrideSprite : image.sprite;
        if (sprite == null)
        {
            result.SpriteAlphaDetail =
                "Facility Root Image has no Sprite; eligible child visuals supply Composite Bounds";
            return;
        }

        result.SpriteRect = sprite.rect;
        result.SpriteRectSize = sprite.rect.size;
        result.SpriteBoundsSize = sprite.bounds.size;
        result.SpriteAspect = Mathf.Abs(sprite.rect.height) > 0.000001f
            ? Mathf.Abs(sprite.rect.width / sprite.rect.height)
            : 0f;
        if (sprite.texture != null)
        {
            result.TextureSize = new Vector2(sprite.texture.width, sprite.texture.height);
        }

        SpriteAlphaBoundsInfo alphaInfo = GetSpriteAlphaBounds(sprite);
        result.HasSpriteAlphaBounds = alphaInfo.Available;
        result.SpriteAlphaDetail = alphaInfo.Detail;
        if (!alphaInfo.Available)
        {
            return;
        }

        result.TextureSize = alphaInfo.TextureSize;
        result.SpriteAlphaPixelBounds = alphaInfo.PixelBounds;
        result.SpriteAlphaNormalizedBounds = alphaInfo.NormalizedBounds;
        result.SpriteAlphaCenterOffsetNormalized =
            alphaInfo.NormalizedBounds.center - new Vector2(0.5f, 0.5f);
    }

    private static bool TryCalculateCompositeVisibleBounds(
        RectTransform facilityRoot,
        RectTransform coordinateRoot,
        out Rect compositeBounds,
        out CompositeBoundsAccumulator accumulator)
    {
        compositeBounds = default;
        accumulator = new CompositeBoundsAccumulator();
        if (facilityRoot == null || coordinateRoot == null)
        {
            return false;
        }

        Image[] images = facilityRoot.GetComponentsInChildren<Image>(true);
        foreach (Image image in images)
        {
            if (!IsEligibleCompositeVisual(image.transform, facilityRoot) ||
                !image.enabled || image.color.a <= 0.000001f)
            {
                continue;
            }

            Sprite sprite = image.overrideSprite != null ? image.overrideSprite : image.sprite;
            if (sprite == null)
            {
                Rect solidLocalRect = image.rectTransform.rect;
                if (!IsFinite(solidLocalRect) ||
                    solidLocalRect.width <= 0.000001f ||
                    solidLocalRect.height <= 0.000001f)
                {
                    accumulator.SkippedSpriteLessImageCount++;
                    continue;
                }

                if (!TryGetTransformLocalRectBoundsInCoordinateRoot(
                        image.rectTransform,
                        coordinateRoot,
                        solidLocalRect,
                        out Rect solidImageBounds))
                {
                    accumulator.ValidationPassed = false;
                    accumulator.UnverifiedVisualCount++;
                    continue;
                }

                accumulator.Add(
                    solidImageBounds,
                    $"Image:{BuildHierarchyPath(image.transform)}:" +
                    $"Bounds={FormatRect(solidImageBounds)}:" +
                    "Sprite=None:Method=FullRect:" +
                    $"Color={FormatColor(image.color)}:" +
                    $"GraphicEnabled={image.enabled}:CanvasCull={image.canvasRenderer.cull}");
                accumulator.SolidColorImageCount++;
                continue;
            }

            Rect localDrawRect = CalculateImageDrawRect(image, image.rectTransform.rect, sprite);
            SpriteAlphaBoundsInfo alphaInfo = GetSpriteAlphaBounds(sprite);
            bool supported = image.type == Image.Type.Simple && !image.useSpriteMesh;
            Rect visibleLocalRect = localDrawRect;
            if (alphaInfo.Available)
            {
                visibleLocalRect = RemapNormalizedRect(
                    localDrawRect,
                    alphaInfo.NormalizedBounds);
            }
            else
            {
                supported = false;
            }

            if (!TryGetTransformLocalRectBoundsInCoordinateRoot(
                    image.rectTransform,
                    coordinateRoot,
                    visibleLocalRect,
                    out Rect imageBounds))
            {
                supported = false;
                continue;
            }

            accumulator.Add(
                imageBounds,
                $"Image:{BuildHierarchyPath(image.transform)}:" +
                $"Bounds={FormatRect(imageBounds)}:Sprite={sprite.name}:" +
                $"Method={(alphaInfo.Available ? "SpriteAlphaBounds" : "RectFallback")}:" +
                $"Alpha={(alphaInfo.Available ? FormatRect(alphaInfo.NormalizedBounds) : "unverified")}:" +
                $"Color={FormatColor(image.color)}:" +
                $"GraphicEnabled={image.enabled}:CanvasCull={image.canvasRenderer.cull}");
            accumulator.SpriteImageCount++;
            if (!supported)
            {
                accumulator.ValidationPassed = false;
                accumulator.UnverifiedVisualCount++;
            }
        }

        RawImage[] rawImages = facilityRoot.GetComponentsInChildren<RawImage>(true);
        foreach (RawImage rawImage in rawImages)
        {
            if (!IsEligibleCompositeVisual(rawImage.transform, facilityRoot) ||
                !rawImage.enabled || rawImage.color.a <= 0.000001f ||
                rawImage.texture == null)
            {
                continue;
            }

            if (!TryGetTransformLocalRectBoundsInCoordinateRoot(
                    rawImage.rectTransform,
                    coordinateRoot,
                    rawImage.rectTransform.rect,
                    out Rect rawImageBounds))
            {
                accumulator.ValidationPassed = false;
                accumulator.UnverifiedVisualCount++;
                continue;
            }

            accumulator.Add(
                rawImageBounds,
                $"RawImage:{BuildHierarchyPath(rawImage.transform)}:{FormatRect(rawImageBounds)}");
            accumulator.RawImageCount++;
        }

        SpriteRenderer[] spriteRenderers =
            facilityRoot.GetComponentsInChildren<SpriteRenderer>(true);
        foreach (SpriteRenderer spriteRenderer in spriteRenderers)
        {
            if (!IsEligibleCompositeVisual(spriteRenderer.transform, facilityRoot) ||
                !spriteRenderer.enabled || spriteRenderer.color.a <= 0.000001f ||
                spriteRenderer.sprite == null)
            {
                continue;
            }

            Sprite sprite = spriteRenderer.sprite;
            SpriteAlphaBoundsInfo alphaInfo = GetSpriteAlphaBounds(sprite);
            Rect normalized = alphaInfo.Available
                ? alphaInfo.NormalizedBounds
                : new Rect(0f, 0f, 1f, 1f);
            if (spriteRenderer.flipX)
            {
                normalized.x = 1f - normalized.xMax;
            }

            if (spriteRenderer.flipY)
            {
                normalized.y = 1f - normalized.yMax;
            }

            Rect spriteLocalBounds = Rect.MinMaxRect(
                sprite.bounds.min.x,
                sprite.bounds.min.y,
                sprite.bounds.max.x,
                sprite.bounds.max.y);
            Rect visibleLocalBounds = RemapNormalizedRect(spriteLocalBounds, normalized);
            bool supported = alphaInfo.Available &&
                             spriteRenderer.drawMode == SpriteDrawMode.Simple;
            if (!TryGetTransformLocalRectBoundsInCoordinateRoot(
                    spriteRenderer.transform,
                    coordinateRoot,
                    visibleLocalBounds,
                    out Rect spriteBounds))
            {
                accumulator.ValidationPassed = false;
                accumulator.UnverifiedVisualCount++;
                continue;
            }

            accumulator.Add(
                spriteBounds,
                $"SpriteRenderer:{BuildHierarchyPath(spriteRenderer.transform)}:" +
                $"{FormatRect(spriteBounds)}");
            accumulator.SpriteRendererCount++;
            if (!supported)
            {
                accumulator.ValidationPassed = false;
                accumulator.UnverifiedVisualCount++;
            }
        }

        MeshRenderer[] meshRenderers =
            facilityRoot.GetComponentsInChildren<MeshRenderer>(true);
        foreach (MeshRenderer meshRenderer in meshRenderers)
        {
            if (!IsEligibleCompositeVisual(meshRenderer.transform, facilityRoot) ||
                !meshRenderer.enabled)
            {
                continue;
            }

            if (!TryGetRendererBoundsInCoordinateRoot(
                    meshRenderer,
                    coordinateRoot,
                    out Rect meshBounds))
            {
                accumulator.ValidationPassed = false;
                accumulator.UnverifiedVisualCount++;
                continue;
            }

            accumulator.Add(
                meshBounds,
                $"MeshRenderer:{BuildHierarchyPath(meshRenderer.transform)}:" +
                $"{FormatRect(meshBounds)}");
            accumulator.MeshRendererCount++;
        }

        SkinnedMeshRenderer[] skinnedMeshRenderers =
            facilityRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (SkinnedMeshRenderer skinnedMeshRenderer in skinnedMeshRenderers)
        {
            if (!IsEligibleCompositeVisual(skinnedMeshRenderer.transform, facilityRoot) ||
                !skinnedMeshRenderer.enabled)
            {
                continue;
            }

            if (!TryGetRendererBoundsInCoordinateRoot(
                    skinnedMeshRenderer,
                    coordinateRoot,
                    out Rect skinnedMeshBounds))
            {
                accumulator.ValidationPassed = false;
                accumulator.UnverifiedVisualCount++;
                continue;
            }

            accumulator.Add(
                skinnedMeshBounds,
                $"SkinnedMeshRenderer:{BuildHierarchyPath(skinnedMeshRenderer.transform)}:" +
                $"Bounds={FormatRect(skinnedMeshBounds)}");
            accumulator.SkinnedMeshRendererCount++;
        }

        if (!accumulator.HasBounds)
        {
            accumulator.ValidationPassed = false;
            return false;
        }

        compositeBounds = accumulator.Bounds;
        return IsFinite(compositeBounds) &&
               compositeBounds.width > 0f && compositeBounds.height > 0f;
    }

    private static bool TryGetOpaqueNormalizedBoundsInCoordinateRoot(
        RectTransform target,
        RectTransform coordinateRoot,
        Rect localNormalizedOpaqueBounds,
        out Rect rootNormalizedOpaqueBounds)
    {
        rootNormalizedOpaqueBounds = default;
        if (target == null || coordinateRoot == null ||
            !IsFinite(localNormalizedOpaqueBounds))
        {
            return false;
        }

        Vector3 rootOrigin = coordinateRoot.InverseTransformPoint(
            target.TransformPoint(Vector3.zero));
        Vector3 rootXPoint = coordinateRoot.InverseTransformPoint(
            target.TransformPoint(Vector3.right));
        Vector3 rootYPoint = coordinateRoot.InverseTransformPoint(
            target.TransformPoint(Vector3.up));
        Vector2 basisX = new Vector2(
            rootXPoint.x - rootOrigin.x,
            rootXPoint.y - rootOrigin.y);
        Vector2 basisY = new Vector2(
            rootYPoint.x - rootOrigin.x,
            rootYPoint.y - rootOrigin.y);
        const float axisTolerance = 0.0001f;

        bool localXMapsToRootX = Mathf.Abs(basisX.x) > axisTolerance &&
                                 Mathf.Abs(basisX.y) <= axisTolerance;
        bool localYMapsToRootY = Mathf.Abs(basisY.y) > axisTolerance &&
                                 Mathf.Abs(basisY.x) <= axisTolerance;
        bool localXMapsToRootY = Mathf.Abs(basisX.y) > axisTolerance &&
                                 Mathf.Abs(basisX.x) <= axisTolerance;
        bool localYMapsToRootX = Mathf.Abs(basisY.x) > axisTolerance &&
                                 Mathf.Abs(basisY.y) <= axisTolerance;

        if (localXMapsToRootX && localYMapsToRootY)
        {
            Vector2 xRange = OrientNormalizedRange(
                localNormalizedOpaqueBounds.xMin,
                localNormalizedOpaqueBounds.xMax,
                basisX.x);
            Vector2 yRange = OrientNormalizedRange(
                localNormalizedOpaqueBounds.yMin,
                localNormalizedOpaqueBounds.yMax,
                basisY.y);
            rootNormalizedOpaqueBounds = Rect.MinMaxRect(
                xRange.x,
                yRange.x,
                xRange.y,
                yRange.y);
            return true;
        }

        if (localXMapsToRootY && localYMapsToRootX)
        {
            Vector2 xRange = OrientNormalizedRange(
                localNormalizedOpaqueBounds.yMin,
                localNormalizedOpaqueBounds.yMax,
                basisY.x);
            Vector2 yRange = OrientNormalizedRange(
                localNormalizedOpaqueBounds.xMin,
                localNormalizedOpaqueBounds.xMax,
                basisX.y);
            rootNormalizedOpaqueBounds = Rect.MinMaxRect(
                xRange.x,
                yRange.x,
                xRange.y,
                yRange.y);
            return true;
        }

        return false;
    }

    private static Vector2 OrientNormalizedRange(float minimum, float maximum, float axisSign)
    {
        return axisSign >= 0f
            ? new Vector2(minimum, maximum)
            : new Vector2(1f - maximum, 1f - minimum);
    }

    private static Rect CalculateImageDrawRect(Image image, Rect rect, Sprite sprite)
    {
        if (image == null || sprite == null ||
            !image.preserveAspect || image.type != Image.Type.Simple ||
            sprite.rect.width <= 0f || sprite.rect.height <= 0f ||
            rect.width <= 0f || rect.height <= 0f)
        {
            return rect;
        }

        float spriteAspect = sprite.rect.width / sprite.rect.height;
        float rectAspect = rect.width / rect.height;
        if (spriteAspect > rectAspect)
        {
            float previousHeight = rect.height;
            rect.height = rect.width / spriteAspect;
            rect.y += (previousHeight - rect.height) * image.rectTransform.pivot.y;
        }
        else
        {
            float previousWidth = rect.width;
            rect.width = rect.height * spriteAspect;
            rect.x += (previousWidth - rect.width) * image.rectTransform.pivot.x;
        }

        return rect;
    }

    private static Rect RemapNormalizedRect(Rect destination, Rect normalized)
    {
        return Rect.MinMaxRect(
            destination.xMin + destination.width * normalized.xMin,
            destination.yMin + destination.height * normalized.yMin,
            destination.xMin + destination.width * normalized.xMax,
               destination.yMin + destination.height * normalized.yMax);
    }

    private static bool IsEligibleCompositeVisual(
        Transform visual,
        Transform facilityRoot)
    {
        if (visual == null || facilityRoot == null)
        {
            return false;
        }

        if (visual.GetComponent<TMP_Text>() != null)
        {
            return false;
        }

        Transform current = visual;
        while (current != null)
        {
            if (!current.gameObject.activeSelf)
            {
                return false;
            }

            if (current != facilityRoot && IsExcludedCompositeName(current.name))
            {
                return false;
            }

            CanvasGroup canvasGroup = current.GetComponent<CanvasGroup>();
            if (canvasGroup != null && canvasGroup.alpha <= 0.000001f)
            {
                return false;
            }

            if (current == facilityRoot)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static bool IsExcludedCompositeName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
        {
            return false;
        }

        return objectName.IndexOf("RobotMarker", StringComparison.OrdinalIgnoreCase) >= 0 ||
               objectName.IndexOf("EventMarker", StringComparison.OrdinalIgnoreCase) >= 0 ||
               objectName.IndexOf("Waypoint", StringComparison.OrdinalIgnoreCase) >= 0 ||
               objectName.IndexOf("PatrolPath", StringComparison.OrdinalIgnoreCase) >= 0 ||
               objectName.IndexOf("PeopleMarker", StringComparison.OrdinalIgnoreCase) >= 0 ||
               objectName.IndexOf("HeadingArrow", StringComparison.OrdinalIgnoreCase) >= 0 ||
               objectName.IndexOf("AxisOverlay", StringComparison.OrdinalIgnoreCase) >= 0 ||
               objectName.IndexOf("Debug", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool TryGetTransformLocalRectBoundsInCoordinateRoot(
        Transform target,
        RectTransform coordinateRoot,
        Rect localRect,
        out Rect bounds)
    {
        bounds = default;
        if (target == null || coordinateRoot == null || !IsFinite(localRect))
        {
            return false;
        }

        Vector3[] localCorners =
        {
            new Vector3(localRect.xMin, localRect.yMin, 0f),
            new Vector3(localRect.xMin, localRect.yMax, 0f),
            new Vector3(localRect.xMax, localRect.yMax, 0f),
            new Vector3(localRect.xMax, localRect.yMin, 0f)
        };
        Vector3 first = coordinateRoot.InverseTransformPoint(
            target.TransformPoint(localCorners[0]));
        float minX = first.x;
        float maxX = first.x;
        float minY = first.y;
        float maxY = first.y;
        for (int index = 1; index < localCorners.Length; index++)
        {
            Vector3 point = coordinateRoot.InverseTransformPoint(
                target.TransformPoint(localCorners[index]));
            minX = Mathf.Min(minX, point.x);
            maxX = Mathf.Max(maxX, point.x);
            minY = Mathf.Min(minY, point.y);
            maxY = Mathf.Max(maxY, point.y);
        }

        bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return IsFinite(bounds);
    }

    private static bool TryGetRendererBoundsInCoordinateRoot(
        Renderer renderer,
        RectTransform coordinateRoot,
        out Rect bounds)
    {
        bounds = default;
        if (renderer == null || coordinateRoot == null)
        {
            return false;
        }

        Bounds worldBounds = renderer.bounds;
        Vector3 minimum = worldBounds.min;
        Vector3 maximum = worldBounds.max;
        Vector3[] worldCorners =
        {
            new Vector3(minimum.x, minimum.y, minimum.z),
            new Vector3(minimum.x, minimum.y, maximum.z),
            new Vector3(minimum.x, maximum.y, minimum.z),
            new Vector3(minimum.x, maximum.y, maximum.z),
            new Vector3(maximum.x, minimum.y, minimum.z),
            new Vector3(maximum.x, minimum.y, maximum.z),
            new Vector3(maximum.x, maximum.y, minimum.z),
            new Vector3(maximum.x, maximum.y, maximum.z)
        };

        Vector3 first = coordinateRoot.InverseTransformPoint(worldCorners[0]);
        float minX = first.x;
        float maxX = first.x;
        float minY = first.y;
        float maxY = first.y;
        for (int index = 1; index < worldCorners.Length; index++)
        {
            Vector3 point = coordinateRoot.InverseTransformPoint(worldCorners[index]);
            minX = Mathf.Min(minX, point.x);
            maxX = Mathf.Max(maxX, point.x);
            minY = Mathf.Min(minY, point.y);
            maxY = Mathf.Max(maxY, point.y);
        }

        bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return IsFinite(bounds) && bounds.width > 0f && bounds.height > 0f;
    }

    private static bool TryGetLocalRectBoundsInCoordinateRoot(
        RectTransform target,
        RectTransform coordinateRoot,
        Rect localRect,
        out Rect bounds)
    {
        return TryGetTransformLocalRectBoundsInCoordinateRoot(
            target,
            coordinateRoot,
            localRect,
            out bounds);
    }

    private static SpriteAlphaBoundsInfo GetSpriteAlphaBounds(Sprite sprite)
    {
        if (sprite == null)
        {
            return new SpriteAlphaBoundsInfo(
                false,
                default,
                default,
                Vector2.zero,
                "Sprite missing");
        }

        int instanceId = sprite.GetInstanceID();
        if (spriteAlphaBoundsCache.TryGetValue(
                instanceId,
                out SpriteAlphaBoundsInfo cached))
        {
            return cached;
        }

        SpriteAlphaBoundsInfo result = ReadSpriteAlphaBounds(sprite);
        spriteAlphaBoundsCache[instanceId] = result;
        return result;
    }

    private static SpriteAlphaBoundsInfo ReadSpriteAlphaBounds(Sprite sprite)
    {
        string assetPath = AssetDatabase.GetAssetPath(sprite);
        if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath) || sprite.packed)
        {
            return new SpriteAlphaBoundsInfo(
                false,
                default,
                default,
                Vector2.zero,
                $"Alpha scan unavailable: Path={assetPath}, Packed={sprite.packed}");
        }

        Texture2D readableTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
        try
        {
            byte[] bytes = File.ReadAllBytes(assetPath);
            if (!ImageConversion.LoadImage(readableTexture, bytes, false) ||
                readableTexture.width <= 0 || readableTexture.height <= 0)
            {
                return new SpriteAlphaBoundsInfo(
                    false,
                    default,
                    default,
                    Vector2.zero,
                    $"Alpha scan decode failed: {assetPath}");
            }

            Texture2D importedTexture = sprite.texture;
            if (importedTexture == null || importedTexture.width <= 0 || importedTexture.height <= 0)
            {
                return new SpriteAlphaBoundsInfo(
                    false,
                    default,
                    default,
                    Vector2.zero,
                    $"Imported texture missing: {assetPath}");
            }

            float sourceScaleX = readableTexture.width / (float)importedTexture.width;
            float sourceScaleY = readableTexture.height / (float)importedTexture.height;
            Rect spriteRect = sprite.rect;
            int startX = Mathf.Clamp(
                Mathf.FloorToInt(spriteRect.xMin * sourceScaleX),
                0,
                readableTexture.width - 1);
            int startY = Mathf.Clamp(
                Mathf.FloorToInt(spriteRect.yMin * sourceScaleY),
                0,
                readableTexture.height - 1);
            int endX = Mathf.Clamp(
                Mathf.CeilToInt(spriteRect.xMax * sourceScaleX),
                startX + 1,
                readableTexture.width);
            int endY = Mathf.Clamp(
                Mathf.CeilToInt(spriteRect.yMax * sourceScaleY),
                startY + 1,
                readableTexture.height);

            Color32[] pixels = readableTexture.GetPixels32();
            int minX = endX;
            int minY = endY;
            int maxXExclusive = startX;
            int maxYExclusive = startY;
            for (int y = startY; y < endY; y++)
            {
                int row = y * readableTexture.width;
                for (int x = startX; x < endX; x++)
                {
                    if (pixels[row + x].a == 0)
                    {
                        continue;
                    }

                    minX = Mathf.Min(minX, x);
                    minY = Mathf.Min(minY, y);
                    maxXExclusive = Mathf.Max(maxXExclusive, x + 1);
                    maxYExclusive = Mathf.Max(maxYExclusive, y + 1);
                }
            }

            if (maxXExclusive <= minX || maxYExclusive <= minY)
            {
                return new SpriteAlphaBoundsInfo(
                    false,
                    default,
                    default,
                    new Vector2(readableTexture.width, readableTexture.height),
                    $"No non-transparent pixels: {assetPath}");
            }

            float spriteSourceWidth = endX - startX;
            float spriteSourceHeight = endY - startY;
            Rect normalized = Rect.MinMaxRect(
                (minX - startX) / spriteSourceWidth,
                (minY - startY) / spriteSourceHeight,
                (maxXExclusive - startX) / spriteSourceWidth,
                (maxYExclusive - startY) / spriteSourceHeight);
            return new SpriteAlphaBoundsInfo(
                true,
                Rect.MinMaxRect(minX, minY, maxXExclusive, maxYExclusive),
                normalized,
                new Vector2(readableTexture.width, readableTexture.height),
                $"AlphaPixels={maxXExclusive - minX}x{maxYExclusive - minY} " +
                $"Texture={readableTexture.width}x{readableTexture.height}");
        }
        catch (Exception exception)
        {
            return new SpriteAlphaBoundsInfo(
                false,
                default,
                default,
                Vector2.zero,
                $"Alpha scan exception: {exception.GetType().Name}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(readableTexture);
        }
    }

    private static bool RectApproximately(Rect left, Rect right, float tolerance)
    {
        return Mathf.Abs(left.xMin - right.xMin) <= tolerance &&
               Mathf.Abs(left.xMax - right.xMax) <= tolerance &&
               Mathf.Abs(left.yMin - right.yMin) <= tolerance &&
               Mathf.Abs(left.yMax - right.yMax) <= tolerance;
    }

    private static bool RectContainsRect(Rect container, Rect contained, float tolerance)
    {
        return contained.xMin >= container.xMin - tolerance &&
               contained.xMax <= container.xMax + tolerance &&
               contained.yMin >= container.yMin - tolerance &&
               contained.yMax <= container.yMax + tolerance;
    }

    private static bool TryGetRectBoundsInCoordinateRoot(
        RectTransform target,
        RectTransform coordinateRoot,
        out Rect bounds)
    {
        bounds = default;
        if (target == null || coordinateRoot == null)
        {
            return false;
        }

        Vector3[] worldCorners = new Vector3[4];
        target.GetWorldCorners(worldCorners);
        Vector3 first = coordinateRoot.InverseTransformPoint(worldCorners[0]);
        float minX = first.x;
        float maxX = first.x;
        float minY = first.y;
        float maxY = first.y;
        for (int index = 1; index < worldCorners.Length; index++)
        {
            Vector3 local = coordinateRoot.InverseTransformPoint(worldCorners[index]);
            minX = Mathf.Min(minX, local.x);
            maxX = Mathf.Max(maxX, local.x);
            minY = Mathf.Min(minY, local.y);
            maxY = Mathf.Max(maxY, local.y);
        }

        bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return IsFinite(bounds);
    }

    private static bool TryCalculateRectTransformValues(
        RectTransform target,
        RectTransform coordinateRoot,
        Rect targetBounds,
        out Vector2 anchoredPosition,
        out Vector2 sizeDelta)
    {
        anchoredPosition = default;
        sizeDelta = default;
        RectTransform parent = target != null ? target.parent as RectTransform : null;
        if (target == null || coordinateRoot == null || parent == null ||
            targetBounds.width <= 0f || targetBounds.height <= 0f)
        {
            return false;
        }

        Vector3 targetWorldOrigin = target.TransformPoint(Vector3.zero);
        Vector3 rootOrigin = coordinateRoot.InverseTransformPoint(targetWorldOrigin);
        Vector3 rootXPoint = coordinateRoot.InverseTransformPoint(
            target.TransformPoint(Vector3.right));
        Vector3 rootYPoint = coordinateRoot.InverseTransformPoint(
            target.TransformPoint(Vector3.up));
        Vector2 basisX = new Vector2(rootXPoint.x - rootOrigin.x, rootXPoint.y - rootOrigin.y);
        Vector2 basisY = new Vector2(rootYPoint.x - rootOrigin.x, rootYPoint.y - rootOrigin.y);

        float coefficientXX = Mathf.Abs(basisX.x);
        float coefficientXY = Mathf.Abs(basisY.x);
        float coefficientYX = Mathf.Abs(basisX.y);
        float coefficientYY = Mathf.Abs(basisY.y);
        float determinant = coefficientXX * coefficientYY - coefficientXY * coefficientYX;
        if (Mathf.Abs(determinant) <= 0.000001f)
        {
            Debug.LogError(
                $"[Measured2D] Unsupported Rect rotation/basis: {BuildHierarchyPath(target)}");
            return false;
        }

        float targetRectWidth =
            (targetBounds.width * coefficientYY - coefficientXY * targetBounds.height) /
            determinant;
        float targetRectHeight =
            (coefficientXX * targetBounds.height - targetBounds.width * coefficientYX) /
            determinant;
        if (!IsFinite(targetRectWidth) || !IsFinite(targetRectHeight) ||
            targetRectWidth <= 0f || targetRectHeight <= 0f)
        {
            return false;
        }

        Vector2 desiredRectSize = new Vector2(targetRectWidth, targetRectHeight);
        Vector2 anchorSpan = target.anchorMax - target.anchorMin;
        sizeDelta = desiredRectSize - Vector2.Scale(parent.rect.size, anchorSpan);

        Vector2 centerFromPivot = new Vector2(
            (0.5f - target.pivot.x) * targetRectWidth,
            (0.5f - target.pivot.y) * targetRectHeight);
        Vector2 rootCenterOffset = basisX * centerFromPivot.x + basisY * centerFromPivot.y;
        Vector2 desiredPivotRoot = targetBounds.center - rootCenterOffset;
        float currentPivotRootZ = coordinateRoot.InverseTransformPoint(target.position).z;
        Vector3 desiredPivotWorld = coordinateRoot.TransformPoint(
            new Vector3(desiredPivotRoot.x, desiredPivotRoot.y, currentPivotRootZ));
        Vector3 desiredPivotParent = parent.InverseTransformPoint(desiredPivotWorld);

        Rect parentRect = parent.rect;
        Vector2 anchorMinPoint = parentRect.min +
                                 Vector2.Scale(parentRect.size, target.anchorMin);
        Vector2 anchorMaxPoint = parentRect.min +
                                 Vector2.Scale(parentRect.size, target.anchorMax);
        Vector2 anchorReference = anchorMinPoint +
                                  Vector2.Scale(anchorMaxPoint - anchorMinPoint, target.pivot);
        anchoredPosition = new Vector2(desiredPivotParent.x, desiredPivotParent.y) - anchorReference;
        return IsFinite(anchoredPosition) && IsFinite(sizeDelta);
    }

    private static void SetMeasuredPreview(List<Measured2DResult> results)
    {
        activeMeasuredPreview = results ?? new List<Measured2DResult>();
        SceneView.duringSceneGui -= DrawMeasured2DPreview;
        if (activeMeasuredPreview.Count > 0)
        {
            SceneView.duringSceneGui += DrawMeasured2DPreview;
        }

        SceneView.RepaintAll();
    }

    private static void DrawMeasured2DPreview(SceneView sceneView)
    {
        if (activeMeasuredPreview == null || activeMeasuredPreview.Count == 0)
        {
            SceneView.duringSceneGui -= DrawMeasured2DPreview;
            return;
        }

        Color previousColor = Handles.color;
        List<MeasuredPreviewLabel> labels =
            new List<MeasuredPreviewLabel>(activeMeasuredPreview.Count);
        HashSet<int> drawnInteriorRoots = new HashSet<int>();
        foreach (Measured2DResult result in activeMeasuredPreview)
        {
            if (result.Target == null || result.CoordinateRoot == null)
            {
                continue;
            }

            if (drawnInteriorRoots.Add(result.CoordinateRoot.GetInstanceID()))
            {
                Vector3[] interiorCorners = GetWorldCorners(
                    result.CoordinateRoot,
                    result.InteriorBounds);
                Color interiorColor = result.InteriorBoundsSaved
                    ? new Color(0.2f, 1f, 0.35f, 1f)
                    : new Color(1f, 0.25f, 0.2f, 1f);
                DrawClosedOutline(interiorCorners, interiorColor, 4f);
            }

            Vector3[] imageDrawCorners = GetWorldCorners(
                result.CoordinateRoot,
                result.CurrentImageDrawBounds);
            DrawClosedOutline(imageDrawCorners, new Color(1f, 0.9f, 0.1f, 1f), 2f);

            Vector3[] currentCorners = new Vector3[4];
            result.Target.GetWorldCorners(currentCorners);
            DrawDottedOutline(currentCorners, new Color(1f, 0.5f, 0.08f, 1f));

            Vector3[] targetCorners = GetWorldCorners(
                result.CoordinateRoot,
                result.TargetBounds);
            DrawClosedOutline(targetCorners, new Color(0.15f, 0.95f, 1f, 1f), 3f);
            DrawChargingAnchor(result, targetCorners);

            if (result.HasCompensatedTargetRect)
            {
                Vector3[] compensatedCorners = GetWorldCorners(
                    result.CoordinateRoot,
                    result.CompensatedTargetRectBounds);
                DrawDottedOutline(compensatedCorners, new Color(0.85f, 0.35f, 1f, 1f));
            }

            bool placeRight = string.Equals(
                                  result.FacilityId,
                                  "Conveyor02",
                                  StringComparison.Ordinal) ||
                              string.Equals(
                                  result.FacilityId,
                                  "Charging",
                                  StringComparison.Ordinal);
            labels.Add(new MeasuredPreviewLabel(
                BuildMeasuredPreviewLabel(result),
                placeRight ? targetCorners[2] : targetCorners[1],
                placeRight));
        }

        Handles.color = previousColor;
        DrawMeasuredPreviewLabels(sceneView, labels);
    }

    private static void DrawChargingAnchor(
        Measured2DResult result,
        IReadOnlyList<Vector3> targetCorners)
    {
        if (!string.Equals(result.FacilityId, "Charging", StringComparison.Ordinal) ||
            targetCorners == null || targetCorners.Count != 4)
        {
            return;
        }

        Vector3 anchor = targetCorners[3];
        Vector3 towardLeft = (targetCorners[0] - anchor).normalized;
        Vector3 towardTop = (targetCorners[2] - anchor).normalized;
        float markerSize = HandleUtility.GetHandleSize(anchor) * 0.08f;

        Handles.color = new Color(0.2f, 1f, 0.35f, 1f);
        Handles.DrawAAPolyLine(4f, anchor, anchor + towardLeft * markerSize);
        Handles.DrawAAPolyLine(4f, anchor, anchor + towardTop * markerSize);
    }

    private static void DrawMeasuredPreviewLabels(
        SceneView sceneView,
        IReadOnlyList<MeasuredPreviewLabel> labels)
    {
        if (sceneView == null || labels == null || labels.Count == 0)
        {
            return;
        }

        GUIStyle labelStyle = GetMeasuredPreviewLabelStyle();
        List<Rect> occupiedRects = new List<Rect>(labels.Count);
        Rect sceneRect = new Rect(
            0f,
            0f,
            sceneView.position.width,
            sceneView.position.height);

        Handles.BeginGUI();
        try
        {
            foreach (MeasuredPreviewLabel label in labels)
            {
                GUIContent content = new GUIContent(label.Text);
                Vector2 labelSize = CalculateMeasuredPreviewLabelSize(labelStyle, label.Text);
                Vector2 anchor = HandleUtility.WorldToGUIPoint(label.WorldAnchor);
                Rect desiredRect = new Rect(
                    label.PlaceRight ? anchor.x + 8f : anchor.x - labelSize.x - 8f,
                    anchor.y - labelSize.y * 0.5f,
                    labelSize.x,
                    labelSize.y);
                Rect labelRect = PlacePreviewLabel(
                    desiredRect,
                    sceneRect,
                    occupiedRects);

                GUI.Box(labelRect, content, labelStyle);
                occupiedRects.Add(labelRect);
            }
        }
        finally
        {
            Handles.EndGUI();
        }
    }

    private static Vector2 CalculateMeasuredPreviewLabelSize(GUIStyle style, string text)
    {
        string[] lines = (text ?? string.Empty).Split('\n');
        float width = 0f;
        foreach (string line in lines)
        {
            width = Mathf.Max(width, style.CalcSize(new GUIContent(line)).x);
        }

        float lineHeight = Mathf.Max(style.lineHeight, EditorGUIUtility.singleLineHeight);
        float height = lineHeight * Mathf.Max(1, lines.Length) +
                       style.padding.top + style.padding.bottom;
        return new Vector2(width, height);
    }

    private static Rect PlacePreviewLabel(
        Rect desiredRect,
        Rect sceneRect,
        IReadOnlyList<Rect> occupiedRects)
    {
        const float margin = 6f;
        const float gap = 4f;
        Rect candidate = desiredRect;
        candidate.x = Mathf.Clamp(
            candidate.x,
            margin,
            Mathf.Max(margin, sceneRect.width - candidate.width - margin));
        candidate.y = Mathf.Clamp(
            candidate.y,
            margin,
            Mathf.Max(margin, sceneRect.height - candidate.height - margin));

        for (int attempt = 0; attempt <= occupiedRects.Count; attempt++)
        {
            bool overlaps = false;
            foreach (Rect occupied in occupiedRects)
            {
                if (!candidate.Overlaps(occupied))
                {
                    continue;
                }

                overlaps = true;
                candidate.y = occupied.yMax + gap;
                if (candidate.yMax > sceneRect.height - margin)
                {
                    candidate.y = margin;
                    candidate.x = Mathf.Clamp(
                        occupied.xMax + gap,
                        margin,
                        Mathf.Max(margin, sceneRect.width - candidate.width - margin));
                }

                break;
            }

            if (!overlaps)
            {
                break;
            }
        }

        return candidate;
    }

    private static GUIStyle GetMeasuredPreviewLabelStyle()
    {
        if (measuredPreviewLabelStyle != null)
        {
            return measuredPreviewLabelStyle;
        }

        measuredPreviewLabelStyle = new GUIStyle(EditorStyles.helpBox)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 11,
            wordWrap = false,
            padding = new RectOffset(6, 6, 3, 3)
        };
        measuredPreviewLabelStyle.normal.textColor = Color.white;
        return measuredPreviewLabelStyle;
    }

    private static string BuildMeasuredPreviewLabel(Measured2DResult result)
    {
        string name = GetMeasuredPreviewFacilityName(result.FacilityId);
        string compensatedSize = result.HasCompensatedTargetRect
            ? FormatMeasuredPreviewSize(result.CompensatedTargetRectBounds.size)
            : "--";
        return $"{name} Visible " +
               $"{FormatMeasuredPreviewSize(result.CurrentImageDrawBounds.size)} \u2192 Target " +
               $"{FormatMeasuredPreviewSize(result.TargetSize)}\n" +
               $"Root {FormatMeasuredPreviewSize(result.CurrentSize)} \u2192 Proposed " +
               compensatedSize;
    }

    private static string GetMeasuredPreviewFacilityName(string facilityId)
    {
        switch (facilityId)
        {
            case "Conveyor01":
                return "C1";
            case "Conveyor02":
                return "C2";
            case "Pallet":
                return "PALLET";
            case "Charging":
                return "CHARGE";
            default:
                return facilityId;
        }
    }

    private static string FormatMeasuredPreviewSize(Vector2 size)
    {
        return $"{FormatMeasuredPreviewDimension(Mathf.Abs(size.x))}\u00D7" +
               FormatMeasuredPreviewDimension(Mathf.Abs(size.y));
    }

    private static string FormatMeasuredPreviewDimension(float value)
    {
        return value.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static void DrawDottedOutline(Vector3[] corners, Color color)
    {
        if (corners == null || corners.Length != 4)
        {
            return;
        }

        Handles.color = color;
        for (int index = 0; index < corners.Length; index++)
        {
            Handles.DrawDottedLine(
                corners[index],
                corners[(index + 1) % corners.Length],
                4f);
        }
    }

    private static void DrawClosedOutline(Vector3[] corners, Color color, float width)
    {
        if (corners == null || corners.Length != 4)
        {
            return;
        }

        Handles.color = color;
        Handles.DrawAAPolyLine(
            width,
            corners[0],
            corners[1],
            corners[2],
            corners[3],
            corners[0]);
    }

    private static Vector3[] GetWorldCorners(RectTransform coordinateRoot, Rect bounds)
    {
        return new[]
        {
            coordinateRoot.TransformPoint(new Vector3(bounds.xMin, bounds.yMin, 0f)),
            coordinateRoot.TransformPoint(new Vector3(bounds.xMin, bounds.yMax, 0f)),
            coordinateRoot.TransformPoint(new Vector3(bounds.xMax, bounds.yMax, 0f)),
            coordinateRoot.TransformPoint(new Vector3(bounds.xMax, bounds.yMin, 0f))
        };
    }

    private static void LogMeasured2DResults(
        string category,
        IReadOnlyList<Measured2DViewContext> views,
        IReadOnlyList<Measured2DResult> results)
    {
        foreach (Measured2DViewContext view in views)
        {
            Rect rootBounds = view.CoordinateRoot.rect;
            bool interiorInsideRoot = RectContainsRect(
                rootBounds,
                view.InteriorBounds,
                BoundsTolerance);
            Debug.Log(
                $"[{category}] View={view.ViewName} " +
                $"CoordinateRoot={BuildHierarchyPath(view.CoordinateRoot)} " +
                $"RootRect={FormatRect(rootBounds)} " +
                $"InteriorSource={view.InteriorBoundsSource} " +
                $"MeasuredLeft={FormatFloat(view.InteriorBounds.xMin)} " +
                $"MeasuredRight={FormatFloat(view.InteriorBounds.xMax)} " +
                $"MeasuredBottom={FormatFloat(view.InteriorBounds.yMin)} " +
                $"MeasuredTop={FormatFloat(view.InteriorBounds.yMax)} " +
                $"InteriorSize={FormatVector2(view.InteriorBounds.size)} " +
                $"NormalizedInterior={FormatRect(view.NormalizedInteriorBounds)} " +
                $"UnitsPerCm=({FormatFloat(view.UnitsPerCmX)},{FormatFloat(view.UnitsPerCmY)}) " +
                $"Saved={view.HasSavedInteriorBounds} " +
                $"InteriorInsideRoot={interiorInsideRoot}");

            LogWallInnerBounds(category, view);
            if (!interiorInsideRoot)
            {
                Debug.LogWarning(
                    $"[{category}] APPLY BLOCKED | View={view.ViewName} | " +
                    $"Interior={FormatRect(view.InteriorBounds)} exceeds " +
                    $"CoordinateRoot={FormatRect(rootBounds)}. " +
                    "Saved Measured Interior Bounds must remain inside the Coordinate Root.");
            }

            if (view.EntryImageAnalysis != null)
            {
                LogMeasuredImageAnalysis(category, view.EntryImageAnalysis);
            }
        }

        foreach (Measured2DResult result in results)
        {
            Debug.Log(
                $"[{category}] View={result.ViewName} " +
                $"Object={BuildHierarchyPath(result.Target)} " +
                $"CurrentBounds={FormatRect(result.CurrentBounds)} " +
                $"CurrentImageDrawBounds={FormatRect(result.CurrentImageDrawBounds)} " +
                $"TargetBounds={FormatRect(result.TargetBounds)} " +
                $"CurrentSize={FormatVector2(result.CurrentSize)} " +
                $"TargetSize={FormatVector2(result.TargetSize)} " +
                $"CurrentCenter={FormatVector2(result.CurrentCenter)} " +
                $"TargetCenter={FormatVector2(result.TargetCenter)} " +
                $"PositionDelta={FormatVector2(result.PositionDelta)} " +
                $"SizeDelta={FormatVector2(result.PhysicalSizeDelta)} " +
                $"TargetAnchoredPosition={FormatVector2(result.TargetAnchoredPosition)} " +
                $"TargetSizeDelta={FormatVector2(result.TargetSizeDelta)}");

            LogMeasuredImageAnalysis(category, result);
        }
    }

    private static void LogWallInnerBounds(string category, Measured2DViewContext view)
    {
        if (view.InteriorWalls == null || view.InteriorWallBounds == null ||
            view.InteriorWalls.Length != InteriorWallNames.Length ||
            view.InteriorWallBounds.Length != InteriorWallNames.Length)
        {
            Debug.Log(
                $"[{category}Wall] View={view.ViewName} | " +
                $"Source={view.InteriorBoundsSource} | " +
                $"ProjectedInterior={FormatRect(view.InteriorBounds)} | " +
                "No local wall objects are used for this View.");
            return;
        }

        for (int index = 0; index < InteriorWallNames.Length; index++)
        {
            RectTransform wall = view.InteriorWalls[index];
            Rect wallBounds = view.InteriorWallBounds[index];
            string selectedInnerEdge;
            switch (index)
            {
                case 0:
                    selectedInnerEdge = $"Right x={FormatFloat(wallBounds.xMax)}";
                    break;
                case 1:
                    selectedInnerEdge = $"Left x={FormatFloat(wallBounds.xMin)}";
                    break;
                case 2:
                    selectedInnerEdge = $"Top y={FormatFloat(wallBounds.yMax)}";
                    break;
                default:
                    selectedInnerEdge = $"Bottom y={FormatFloat(wallBounds.yMin)}";
                    break;
            }

            Debug.Log(
                $"[{category}Wall] View={view.ViewName} " +
                $"Wall={BuildHierarchyPath(wall)} " +
                $"RootLocalBounds={FormatRect(wallBounds)} " +
                $"SelectedInnerEdge={selectedInnerEdge} " +
                $"AnchoredPosition={FormatVector2(wall.anchoredPosition)} " +
                $"SizeDelta={FormatVector2(wall.sizeDelta)} " +
                $"LocalPosition={FormatVector3(wall.localPosition)} " +
                $"LocalRotation={FormatVector3(wall.localEulerAngles)} " +
                $"LocalScale={FormatVector3(wall.localScale)}");
        }

        if (view.HasDiagnosticWallInteriorBounds)
        {
            bool diagnosticInsideRoot = RectContainsRect(
                view.CoordinateRoot.rect,
                view.DiagnosticWallInteriorBounds,
                BoundsTolerance);
            Debug.LogWarning(
                $"[{category}Wall] DIAGNOSTIC ONLY | View={view.ViewName} | " +
                $"WallInnerBounds={FormatRect(view.DiagnosticWallInteriorBounds)} | " +
                $"CoordinateRoot={FormatRect(view.CoordinateRoot.rect)} | " +
                $"InsideRoot={diagnosticInsideRoot} | " +
                "Wall bounds are not used for unitsPerCm or target placement.");
        }
    }

    private static void LogMeasuredImageAnalysis(string category, Measured2DResult result)
    {
        string targetVisibleSize = result.HasPhysicalTarget
            ? FormatVector2(result.TargetSize)
            : "--";
        string compensatedBounds = result.HasCompensatedTargetRect
            ? FormatRect(result.CompensatedTargetRectBounds)
            : "--";
        string compensatedSize = result.HasCompensatedTargetRect
            ? FormatVector2(result.CompensatedTargetRectBounds.size)
            : "--";
        string compensatedAnchored = result.HasCompensatedTargetRect
            ? FormatVector2(result.CompensatedTargetAnchoredPosition)
            : "--";
        string compensatedSizeDelta = result.HasCompensatedTargetRect
            ? FormatVector2(result.CompensatedTargetSizeDelta)
            : "--";
        string anchoredCorrection = result.HasCompensatedTargetRect
            ? FormatVector2(result.AnchoredPositionCorrection)
            : "--";

        Debug.Log(
            $"[{category}Image] View={result.ViewName} " +
            $"Facility={result.FacilityId} " +
            $"Object={BuildHierarchyPath(result.Target)} " +
            $"Texture={FormatVector2(result.TextureSize)} " +
            $"SpriteRect={FormatRect(result.SpriteRect)} " +
            $"ImageType={result.ImageType} " +
            $"PreserveAspect={result.PreserveAspect} " +
            $"UseSpriteMesh={result.UseSpriteMesh} " +
            $"OpaquePixelBounds={FormatRect(result.SpriteAlphaPixelBounds)} " +
            $"OpaquePixelSize={FormatVector2(result.SpriteAlphaPixelBounds.size)} " +
            $"OpaqueNormalizedBounds={FormatRect(result.SpriteAlphaNormalizedBounds)} " +
            $"OpaqueNormalizedSize={FormatVector2(result.SpriteAlphaNormalizedBounds.size)} " +
            $"OpaqueCenterOffsetNormalized=" +
            $"{FormatVector2(result.SpriteAlphaCenterOffsetNormalized)} " +
            $"CurrentRectSize={FormatVector2(result.CurrentSize)} " +
            $"CurrentVisibleSize={FormatVector2(result.CurrentImageDrawBounds.size)} " +
            $"CompositeNormalizedBounds={FormatRect(result.CompositeNormalizedBounds)} " +
            $"CompositeVisualCount={result.CompositeVisualCount} " +
            $"CompositeValid={result.CompositeVisibleBoundsValid} " +
            $"CompositeDetail={result.CompositeVisibleDetail} " +
            $"TargetVisibleSize={targetVisibleSize} " +
            $"CompensatedRectBounds={compensatedBounds} " +
            $"CompensatedRectSize={compensatedSize} " +
            $"CompensatedAnchoredPosition={compensatedAnchored} " +
            $"CompensatedSizeDelta={compensatedSizeDelta} " +
            $"AnchoredPositionCorrection={anchoredCorrection} " +
            $"SpriteBounds={FormatVector3(result.SpriteBoundsSize)} " +
            $"SpriteAspect={FormatFloat(result.SpriteAspect)} " +
            $"RectTransformAspect={FormatFloat(result.RectTransformAspect)} " +
            $"AlphaDetail={result.SpriteAlphaDetail} " +
            $"Validation={(result.ImageDrawValidationPassed ? "PASS" : "BLOCKED")} " +
            $"Proposal={result.ApplyPropertyProposal}");
    }

    private static void LogEntryWarnings(
        string category,
        IReadOnlyList<Measured2DViewContext> views)
    {
        foreach (Measured2DViewContext view in views)
        {
            Debug.LogWarning(
                $"[{category}] Entry unchanged | View={view.ViewName} | " +
                $"Object={BuildHierarchyPath(view.Entry)} | " +
                "10cm reference is unresolved (no Barrier pivot, edge, column center, or guide-line assumption used).");
        }
    }

    private static bool ValidateMeasured2DApplyReadiness(
        IReadOnlyList<Measured2DViewContext> views,
        IReadOnlyList<Measured2DResult> results,
        bool applyAttempt)
    {
        bool valid = views != null && results != null &&
                     views.Count > 0 && results.Count == views.Count * 4;
        if (!valid)
        {
            LogMeasuredReadinessFailure(
                applyAttempt,
                "View/result count is incomplete.");
            return false;
        }

        foreach (Measured2DViewContext view in views)
        {
            bool measuredSourceValid = string.Equals(
                                           view.ViewName,
                                           "MiniMap",
                                           StringComparison.Ordinal)
                ? string.Equals(
                    view.InteriorBoundsSource,
                    "Full2D measured normalized bounds",
                    StringComparison.Ordinal) &&
                  view.HasSavedInteriorBounds
                : view.HasSavedInteriorBounds &&
                  string.Equals(
                      view.InteriorBoundsSource,
                      "Measured layout config",
                      StringComparison.Ordinal);
            if (!measuredSourceValid || !IsFinite(view.InteriorBounds) ||
                view.InteriorBounds.width <= 0f || view.InteriorBounds.height <= 0f)
            {
                valid = false;
                LogMeasuredReadinessFailure(
                    applyAttempt,
                    $"Saved Measured Interior Bounds invalid or missing: {view.ViewName}");
            }

            if (view.CoordinateRoot == null ||
                !RectContainsRect(
                    view.CoordinateRoot.rect,
                    view.InteriorBounds,
                    BoundsTolerance))
            {
                valid = false;
                LogMeasuredReadinessFailure(
                    applyAttempt,
                    $"Wall inner bounds exceed Coordinate Root: {view.ViewName} | " +
                    $"Root={FormatRect(view.CoordinateRoot != null ? view.CoordinateRoot.rect : default)} | " +
                    $"Interior={FormatRect(view.InteriorBounds)}");
            }

            if (view.Entry == null)
            {
                valid = false;
                LogMeasuredReadinessFailure(
                    applyAttempt,
                    $"Entry reference missing: {view.ViewName}");
            }
        }

        foreach (Measured2DResult result in results)
        {
            Rect interior = result.InteriorBounds;
            Rect target = result.TargetBounds;
            bool inside = target.xMin >= interior.xMin - BoundsTolerance &&
                          target.xMax <= interior.xMax + BoundsTolerance &&
                          target.yMin >= interior.yMin - BoundsTolerance &&
                          target.yMax <= interior.yMax + BoundsTolerance;
            if (!inside)
            {
                valid = false;
                LogMeasuredReadinessFailure(
                    applyAttempt,
                    $"Target outside interior: {result.ViewName}/{result.FacilityId}");
            }

            if ((string.Equals(result.FacilityId, "Conveyor01", StringComparison.Ordinal) ||
                 string.Equals(result.FacilityId, "Conveyor02", StringComparison.Ordinal)) &&
                Mathf.Abs(target.yMax - interior.yMax) > BoundsTolerance)
            {
                valid = false;
                LogMeasuredReadinessFailure(
                    applyAttempt,
                    $"Conveyor does not touch top wall: " +
                    $"{result.ViewName}/{result.FacilityId}");
            }

            if (string.Equals(result.FacilityId, "Charging", StringComparison.Ordinal) &&
                (Mathf.Abs(target.xMax - interior.xMax) > BoundsTolerance ||
                 Mathf.Abs(target.yMin - interior.yMin) > BoundsTolerance))
            {
                valid = false;
                LogMeasuredReadinessFailure(
                    applyAttempt,
                    $"Charging does not touch right/bottom walls: {result.ViewName}");
            }

            if (string.Equals(result.FacilityId, "Pallet", StringComparison.Ordinal))
            {
                float normalizedCenterX =
                    (target.center.x - interior.xMin) / interior.width;
                float normalizedCenterY =
                    (target.center.y - interior.yMin) / interior.height;
                if (normalizedCenterX < 0.25f || normalizedCenterX > 0.75f ||
                    normalizedCenterY < 0.2f || normalizedCenterY > 0.5f)
                {
                    valid = false;
                    LogMeasuredReadinessFailure(
                        applyAttempt,
                        $"Pallet is not in the lower central interior: {result.ViewName}");
                }
            }

            if (!result.ImageDrawValidationPassed ||
                !result.CompositeVisibleBoundsValid ||
                result.CompositeVisualCount <= 0 ||
                !result.HasCompensatedTargetRect ||
                !IsFinite(result.CompensatedTargetRectBounds) ||
                !IsFinite(result.CompensatedTargetAnchoredPosition) ||
                !IsFinite(result.CompensatedTargetSizeDelta))
            {
                valid = false;
                LogMeasuredReadinessFailure(
                    applyAttempt,
                    $"Image alpha compensation is incomplete: " +
                    $"{result.ViewName}/{result.FacilityId} | " +
                    $"SuggestedRectSize={FormatVector2(result.SuggestedRectTransformSizeInRoot)}");
            }

            else if (!IsFinite(result.Target.anchorMin) ||
                     !IsFinite(result.Target.anchorMax) ||
                     !IsFinite(result.Target.pivot) ||
                     !IsFinite(result.Target.localScale) ||
                     !IsFinite(result.Target.localEulerAngles))
            {
                valid = false;
                LogMeasuredReadinessFailure(
                    applyAttempt,
                    $"Protected Anchor/Pivot/Scale/Rotation values are invalid: " +
                    $"{result.ViewName}/{result.FacilityId}");
            }
        }

        if (!Measured2DApplyEnabled)
        {
            valid = false;
            LogMeasuredReadinessFailure(
                applyAttempt,
                "Preview-only safety lock is active; Apply remains disabled pending approval.");
        }

        if (valid)
        {
            Debug.Log(
                $"[Measured2DReadiness] READY | Views={views.Count} | Objects={results.Count} | " +
                "Entry unchanged | Protected Transform checks remain enabled");
        }

        return valid;
    }

    private static void LogMeasuredReadinessFailure(bool applyAttempt, string message)
    {
        if (applyAttempt)
        {
            Debug.LogError($"[Measured2DReadiness] BLOCKED | {message}");
        }
        else
        {
            Debug.LogWarning($"[Measured2DReadiness] NOT READY | {message}");
        }
    }

    private static bool TryCaptureApplyProtectionStates(
        Scene scene,
        IReadOnlyList<LayoutTarget> layoutTargets,
        IReadOnlyList<Measured2DViewContext> views,
        ISet<int> modifiedTargetIds,
        out List<ApplyProtectionState> states)
    {
        states = new List<ApplyProtectionState>();
        Dictionary<int, ApplyProtectionState> statesByInstanceId =
            new Dictionary<int, ApplyProtectionState>();

        foreach (LayoutTarget layoutTarget in layoutTargets)
        {
            if (!TryAddApplyProtectionState(
                    scene,
                    layoutTarget.Transform,
                    modifiedTargetIds.Contains(layoutTarget.Transform.GetInstanceID()),
                    statesByInstanceId,
                    states))
            {
                return false;
            }
        }

        foreach (Measured2DViewContext view in views)
        {
            if (!TryAddApplyProtectionState(
                    scene,
                    view.CoordinateRoot,
                    false,
                    statesByInstanceId,
                    states) ||
                !TryAddApplyProtectionState(
                    scene,
                    view.CoordinateRoot.parent,
                    false,
                    statesByInstanceId,
                    states))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAddApplyProtectionState(
        Scene scene,
        Transform target,
        bool allowsMeasuredRectChange,
        IDictionary<int, ApplyProtectionState> statesByInstanceId,
        ICollection<ApplyProtectionState> states)
    {
        if (target == null || target.gameObject.scene != scene)
        {
            return false;
        }

        int instanceId = target.GetInstanceID();
        if (statesByInstanceId.TryGetValue(instanceId, out ApplyProtectionState existing))
        {
            existing.AllowsMeasuredRectChange |= allowsMeasuredRectChange;
            return true;
        }

        scr_MapLayoutObjectSnapshot snapshot = new scr_MapLayoutObjectSnapshot();
        snapshot.Capture(
            "Measured2DApplyProtection",
            scene.path,
            BuildHierarchyPath(target),
            target.parent != null ? BuildHierarchyPath(target.parent) : string.Empty,
            target);
        ApplyProtectionState state = new ApplyProtectionState
        {
            Target = target,
            Snapshot = snapshot,
            AllowsMeasuredRectChange = allowsMeasuredRectChange
        };
        statesByInstanceId.Add(instanceId, state);
        states.Add(state);
        return true;
    }

    private static bool ValidateApplyProtectionStates(
        IReadOnlyList<ApplyProtectionState> states)
    {
        bool valid = true;
        foreach (ApplyProtectionState state in states)
        {
            Transform target = state.Target;
            scr_MapLayoutObjectSnapshot snapshot = state.Snapshot;
            if (target == null)
            {
                Debug.LogError(
                    $"[Measured2DApply] Protected object disappeared: {snapshot.HierarchyPath}");
                valid = false;
                continue;
            }

            valid &= ValidateProtectedValue(
                snapshot.HierarchyPath,
                "Scene Path",
                snapshot.ScenePath,
                target.gameObject.scene.path);
            valid &= ValidateProtectedValue(
                snapshot.HierarchyPath,
                "Hierarchy Path",
                snapshot.HierarchyPath,
                BuildHierarchyPath(target));
            valid &= ValidateProtectedValue(
                snapshot.HierarchyPath,
                "Parent Path",
                snapshot.ParentPath,
                target.parent != null ? BuildHierarchyPath(target.parent) : string.Empty);
            valid &= ValidateProtectedValue(
                snapshot.HierarchyPath,
                "activeSelf",
                snapshot.ActiveSelf,
                target.gameObject.activeSelf);
            valid &= ValidateProtectedValue(
                snapshot.HierarchyPath,
                "layer",
                snapshot.Layer,
                target.gameObject.layer);
            valid &= ValidateProtectedValue(
                snapshot.HierarchyPath,
                "localRotation",
                snapshot.LocalRotation,
                target.localRotation);
            valid &= ValidateProtectedValue(
                snapshot.HierarchyPath,
                "localScale",
                snapshot.LocalScale,
                target.localScale);

            RectTransform rectTransform = target as RectTransform;
            valid &= ValidateProtectedValue(
                snapshot.HierarchyPath,
                "isRectTransform",
                snapshot.IsRectTransform,
                rectTransform != null);
            if (snapshot.IsRectTransform && rectTransform != null)
            {
                valid &= ValidateProtectedValue(
                    snapshot.HierarchyPath,
                    "anchorMin",
                    snapshot.AnchorMin,
                    rectTransform.anchorMin);
                valid &= ValidateProtectedValue(
                    snapshot.HierarchyPath,
                    "anchorMax",
                    snapshot.AnchorMax,
                    rectTransform.anchorMax);
                valid &= ValidateProtectedValue(
                    snapshot.HierarchyPath,
                    "pivot",
                    snapshot.Pivot,
                    rectTransform.pivot);

                if (state.AllowsMeasuredRectChange)
                {
                    valid &= ValidateProtectedValue(
                        snapshot.HierarchyPath,
                        "localPosition.z",
                        snapshot.LocalPosition.z,
                        rectTransform.localPosition.z);
                }
                else
                {
                    valid &= ValidateProtectedValue(
                        snapshot.HierarchyPath,
                        "anchoredPosition",
                        snapshot.AnchoredPosition,
                        rectTransform.anchoredPosition);
                    valid &= ValidateProtectedValue(
                        snapshot.HierarchyPath,
                        "sizeDelta",
                        snapshot.SizeDelta,
                        rectTransform.sizeDelta);
                    valid &= ValidateProtectedValue(
                        snapshot.HierarchyPath,
                        "localPosition",
                        snapshot.LocalPosition,
                        rectTransform.localPosition);
                }
            }
            else if (!snapshot.IsRectTransform)
            {
                valid &= ValidateProtectedValue(
                    snapshot.HierarchyPath,
                    "localPosition",
                    snapshot.LocalPosition,
                    target.localPosition);
            }
        }

        return valid;
    }

    private static bool ValidateAppliedMeasuredBounds(
        IReadOnlyList<Measured2DResult> results)
    {
        bool valid = true;
        foreach (Measured2DResult result in results)
        {
            if (!TryGetRectBoundsInCoordinateRoot(
                    result.Target,
                    result.CoordinateRoot,
                    out Rect currentBounds))
            {
                Debug.LogError(
                    $"[Measured2DApply] Cannot read applied bounds: {BuildHierarchyPath(result.Target)}");
                valid = false;
                continue;
            }

            valid &= ValidateWithinTolerance(
                result,
                "Bounds.xMin",
                result.TargetBounds.xMin,
                currentBounds.xMin);
            valid &= ValidateWithinTolerance(
                result,
                "Bounds.xMax",
                result.TargetBounds.xMax,
                currentBounds.xMax);
            valid &= ValidateWithinTolerance(
                result,
                "Bounds.yMin",
                result.TargetBounds.yMin,
                currentBounds.yMin);
            valid &= ValidateWithinTolerance(
                result,
                "Bounds.yMax",
                result.TargetBounds.yMax,
                currentBounds.yMax);
            valid &= ValidateWithinTolerance(
                result,
                "anchoredPosition.x",
                result.TargetAnchoredPosition.x,
                result.Target.anchoredPosition.x);
            valid &= ValidateWithinTolerance(
                result,
                "anchoredPosition.y",
                result.TargetAnchoredPosition.y,
                result.Target.anchoredPosition.y);
            valid &= ValidateWithinTolerance(
                result,
                "sizeDelta.x",
                result.TargetSizeDelta.x,
                result.Target.sizeDelta.x);
            valid &= ValidateWithinTolerance(
                result,
                "sizeDelta.y",
                result.TargetSizeDelta.y,
                result.Target.sizeDelta.y);
        }

        return valid;
    }

    private static bool ValidateWithinTolerance(
        Measured2DResult result,
        string property,
        float expected,
        float actual)
    {
        float difference = actual - expected;
        if (IsFinite(actual) && Mathf.Abs(difference) <= BoundsTolerance)
        {
            return true;
        }

        Debug.LogError(
            $"[Measured2DApply] Validation failed | Object={BuildHierarchyPath(result.Target)} | " +
            $"Property={property} | Target={FormatFloat(expected)} | " +
            $"Current={FormatFloat(actual)} | Difference={FormatFloat(difference)}");
        return false;
    }

    private static bool TryCaptureMapControllerSignatures(
        Scene scene,
        out List<ComponentSignature> signatures)
    {
        signatures = new List<ComponentSignature>();
        int fullControllerCount = 0;
        int miniControllerCount = 0;

        scr_FactoryFull2DMapController[] fullControllers =
            UnityEngine.Object.FindObjectsByType<scr_FactoryFull2DMapController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        foreach (scr_FactoryFull2DMapController controller in fullControllers)
        {
            if (controller == null || controller.gameObject.scene != scene)
            {
                continue;
            }

            fullControllerCount++;
            AddComponentSignature(controller, signatures);
        }

        scr_FactoryMini2DMapController[] miniControllers =
            UnityEngine.Object.FindObjectsByType<scr_FactoryMini2DMapController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        foreach (scr_FactoryMini2DMapController controller in miniControllers)
        {
            if (controller == null || controller.gameObject.scene != scene)
            {
                continue;
            }

            miniControllerCount++;
            AddComponentSignature(controller, signatures);
        }

        if (fullControllerCount < 2 || miniControllerCount < 1)
        {
            Debug.LogError(
                $"[Measured2DApply] Map Controller references are incomplete. " +
                $"Full={fullControllerCount}, Mini={miniControllerCount}");
            return false;
        }

        return true;
    }

    private static void AddComponentSignature(
        Component component,
        ICollection<ComponentSignature> signatures)
    {
        signatures.Add(new ComponentSignature
        {
            Target = component,
            Name = $"{component.GetType().Name}@{BuildHierarchyPath(component.transform)}" +
                   $"#{component.GetInstanceID()}",
            Json = EditorJsonUtility.ToJson(component, false)
        });
    }

    private static bool ValidateMapControllerSignatures(
        IReadOnlyList<ComponentSignature> signatures)
    {
        bool valid = true;
        foreach (ComponentSignature signature in signatures)
        {
            if (signature.Target == null)
            {
                Debug.LogError($"[Measured2DApply] Map Controller disappeared: {signature.Name}");
                valid = false;
                continue;
            }

            string currentJson = EditorJsonUtility.ToJson(signature.Target, false);
            if (string.Equals(signature.Json, currentJson, StringComparison.Ordinal))
            {
                continue;
            }

            Debug.LogError(
                $"[Measured2DApply] Map Controller/Calibration changed unexpectedly: {signature.Name}");
            valid = false;
        }

        return valid;
    }

    private static bool ValidateProtectedValue(
        string path,
        string property,
        string expected,
        string actual)
    {
        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            return true;
        }

        LogProtectedDifference(path, property, expected, actual);
        return false;
    }

    private static bool ValidateProtectedValue(
        string path,
        string property,
        bool expected,
        bool actual)
    {
        if (expected == actual)
        {
            return true;
        }

        LogProtectedDifference(path, property, expected.ToString(), actual.ToString());
        return false;
    }

    private static bool ValidateProtectedValue(
        string path,
        string property,
        int expected,
        int actual)
    {
        if (expected == actual)
        {
            return true;
        }

        LogProtectedDifference(
            path,
            property,
            expected.ToString(CultureInfo.InvariantCulture),
            actual.ToString(CultureInfo.InvariantCulture));
        return false;
    }

    private static bool ValidateProtectedValue(
        string path,
        string property,
        float expected,
        float actual)
    {
        if (expected.Equals(actual))
        {
            return true;
        }

        LogProtectedDifference(path, property, FormatFloat(expected), FormatFloat(actual));
        return false;
    }

    private static bool ValidateProtectedValue(
        string path,
        string property,
        Vector2 expected,
        Vector2 actual)
    {
        if (ExactEquals(expected, actual))
        {
            return true;
        }

        LogProtectedDifference(path, property, FormatVector2(expected), FormatVector2(actual));
        return false;
    }

    private static bool ValidateProtectedValue(
        string path,
        string property,
        Vector3 expected,
        Vector3 actual)
    {
        if (ExactEquals(expected, actual))
        {
            return true;
        }

        LogProtectedDifference(path, property, FormatVector3(expected), FormatVector3(actual));
        return false;
    }

    private static bool ValidateProtectedValue(
        string path,
        string property,
        Quaternion expected,
        Quaternion actual)
    {
        if (ExactEquals(expected, actual))
        {
            return true;
        }

        LogProtectedDifference(path, property, FormatQuaternion(expected), FormatQuaternion(actual));
        return false;
    }

    private static void LogProtectedDifference(
        string path,
        string property,
        string expected,
        string actual)
    {
        Debug.LogError(
            $"[Measured2DApply] Protected property changed | Object={path} | " +
            $"Property={property} | Before={expected} | After={actual}");
    }

    private static void BeginFactory3DInteriorCalibration()
    {
        SetMeasuredPreview(null);
        activeMeasured3DPreview = null;
        SceneView.duringSceneGui -= DrawMeasured3DPreview;
        activeInteriorCalibration = null;
        SceneView.duringSceneGui -= DrawInteriorCalibrationPreview;

        if (!TryGetEditableControlTowerScene(out Scene scene) ||
            !TryResolveFactory3DStageAndFloor(
                scene,
                out Transform stage,
                out Transform floorRoot,
                out Rect floorBounds,
                out float floorMinY,
                out float floorMaxY))
        {
            return;
        }

        scr_MapMeasuredLayoutConfig config = LoadMeasuredLayoutConfig();
        bool hasSavedBounds = config != null && config.Factory3DInteriorSaved;
        Rect initialBounds = hasSavedBounds
            ? config.Factory3DInteriorBounds
            : floorBounds;
        if (!ValidateFactory3DInteriorBounds(initialBounds, floorBounds, false))
        {
            initialBounds = floorBounds;
            hasSavedBounds = false;
        }

        activeFactory3DInteriorCalibration = new Factory3DInteriorCalibrationState
        {
            Stage = stage,
            FloorRoot = floorRoot,
            FloorBounds = floorBounds,
            PreviewY = floorMaxY + Mathf.Max(0.02f, floorMaxY - floorMinY),
            Bounds = initialBounds,
            InitializedFromSavedConfig = hasSavedBounds
        };

        SceneView.duringSceneGui -= DrawFactory3DInteriorCalibration;
        SceneView.duringSceneGui += DrawFactory3DInteriorCalibration;
        SceneView.RepaintAll();
        LogFactory3DInterior(
            "Measured3DCalibration",
            stage,
            floorRoot,
            floorBounds,
            initialBounds,
            config);
        Debug.Log(
            $"[Measured3DCalibration] Started | " +
            $"InitialSource={(hasSavedBounds ? "Saved config" : "Floor Renderer bounds")} | " +
            "Handles change numeric preview values only; Scene unchanged");
    }

    private static void DrawFactory3DInteriorCalibration(SceneView sceneView)
    {
        Factory3DInteriorCalibrationState state = activeFactory3DInteriorCalibration;
        if (sceneView == null || state == null || state.Stage == null)
        {
            SceneView.duringSceneGui -= DrawFactory3DInteriorCalibration;
            return;
        }

        Transform stage = state.Stage;
        Rect bounds = state.Bounds;
        bool valid = ValidateFactory3DInteriorBounds(bounds, state.FloorBounds, false);
        Color previousColor = Handles.color;
        Color outlineColor = valid
            ? new Color(0.2f, 1f, 0.35f, 1f)
            : new Color(1f, 0.25f, 0.2f, 1f);
        DrawClosedOutline(GetFactory3DWorldCorners(stage, bounds, state.PreviewY), outlineColor, 4f);

        Vector3 stageRight = stage.TransformDirection(Vector3.right).normalized;
        Vector3 stageForward = stage.TransformDirection(Vector3.forward).normalized;
        Vector3 leftWorld = stage.TransformPoint(
            new Vector3(bounds.xMin, state.PreviewY, bounds.center.y));
        Vector3 rightWorld = stage.TransformPoint(
            new Vector3(bounds.xMax, state.PreviewY, bounds.center.y));
        Vector3 bottomWorld = stage.TransformPoint(
            new Vector3(bounds.center.x, state.PreviewY, bounds.yMin));
        Vector3 topWorld = stage.TransformPoint(
            new Vector3(bounds.center.x, state.PreviewY, bounds.yMax));

        EditorGUI.BeginChangeCheck();
        Vector3 movedLeft = Handles.Slider(
            leftWorld,
            stageRight,
            HandleUtility.GetHandleSize(leftWorld) * 0.08f,
            Handles.SphereHandleCap,
            0f);
        Vector3 movedRight = Handles.Slider(
            rightWorld,
            stageRight,
            HandleUtility.GetHandleSize(rightWorld) * 0.08f,
            Handles.SphereHandleCap,
            0f);
        Vector3 movedBottom = Handles.Slider(
            bottomWorld,
            stageForward,
            HandleUtility.GetHandleSize(bottomWorld) * 0.08f,
            Handles.SphereHandleCap,
            0f);
        Vector3 movedTop = Handles.Slider(
            topWorld,
            stageForward,
            HandleUtility.GetHandleSize(topWorld) * 0.08f,
            Handles.SphereHandleCap,
            0f);
        if (EditorGUI.EndChangeCheck())
        {
            float left = stage.InverseTransformPoint(movedLeft).x;
            float right = stage.InverseTransformPoint(movedRight).x;
            float bottom = stage.InverseTransformPoint(movedBottom).z;
            float top = stage.InverseTransformPoint(movedTop).z;
            if (IsFinite(left) && IsFinite(right) && IsFinite(bottom) && IsFinite(top) &&
                right > left && top > bottom)
            {
                state.Bounds = Rect.MinMaxRect(left, bottom, right, top);
            }

            SceneView.RepaintAll();
        }

        Handles.Label(leftWorld, "Left");
        Handles.Label(rightWorld, "Right");
        Handles.Label(bottomWorld, "Bottom / Front");
        Handles.Label(topWorld, "Top / Back");
        Handles.color = previousColor;
    }

    private static void SaveActiveFactory3DInteriorCalibration()
    {
        Factory3DInteriorCalibrationState state = activeFactory3DInteriorCalibration;
        if (state == null || state.Stage == null || state.FloorRoot == null)
        {
            Debug.LogWarning(
                "[Measured3DInteriorSave] No active 3D calibration. " +
                "Run Calibrate Factory 3D Interior Bounds first.");
            return;
        }

        if (!ValidateFactory3DInteriorBounds(state.Bounds, state.FloorBounds, true))
        {
            return;
        }

        scr_MapMeasuredLayoutConfig config = LoadOrCreateMeasuredLayoutConfig();
        if (config == null)
        {
            return;
        }

        Undo.RecordObject(config, "Save Factory 3D Interior Bounds");
        config.SetFactory3DInteriorBounds(state.Bounds);
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        state.InitializedFromSavedConfig = true;
        LogFactory3DInterior(
            "Measured3DInteriorSave",
            state.Stage,
            state.FloorRoot,
            state.FloorBounds,
            state.Bounds,
            config);
        Debug.Log(
            $"[Measured3DInteriorSave] Saved numeric X/Z bounds only | " +
            $"Asset={MeasuredLayoutConfigAssetPath} | Scene unchanged");
    }

    private static bool ValidateFactory3DInteriorBounds(
        Rect bounds,
        Rect floorBounds,
        bool logFailure)
    {
        bool finiteAndPositive = IsFinite(bounds) && IsFinite(floorBounds) &&
                                 bounds.width > 0f && bounds.height > 0f &&
                                 floorBounds.width > 0f && floorBounds.height > 0f;
        float floorSpan = Mathf.Max(floorBounds.width, floorBounds.height);
        bool overlapsFloor = finiteAndPositive && bounds.Overlaps(floorBounds, true);
        bool reasonableSize = finiteAndPositive &&
                              bounds.width <= floorSpan * 4f &&
                              bounds.height <= floorSpan * 4f;
        bool reasonableDistance = finiteAndPositive &&
                                  Vector2.Distance(bounds.center, floorBounds.center) <= floorSpan * 1.5f;
        bool valid = finiteAndPositive && overlapsFloor && reasonableSize && reasonableDistance;
        if (logFailure && !valid)
        {
            Debug.LogError(
                $"[Measured3DInteriorSave] BLOCKED | Bounds={FormatRect(bounds)} | " +
                $"Floor={FormatRect(floorBounds)} | FinitePositive={finiteAndPositive} | " +
                $"OverlapsFloor={overlapsFloor} | ReasonableSize={reasonableSize} | " +
                $"ReasonableDistance={reasonableDistance}");
        }

        return valid;
    }

    private static void PreviewFactory3DMeasuredLayout()
    {
        activeFactory3DInteriorCalibration = null;
        SceneView.duringSceneGui -= DrawFactory3DInteriorCalibration;
        SetMeasuredPreview(null);
        if (!TryGetEditableControlTowerScene(out Scene scene) ||
            !TryBuildMeasured3DLayout(scene, out Factory3DPreviewState preview))
        {
            activeMeasured3DPreview = null;
            SceneView.duringSceneGui -= DrawMeasured3DPreview;
            return;
        }

        activeMeasured3DPreview = preview;
        SceneView.duringSceneGui -= DrawMeasured3DPreview;
        SceneView.duringSceneGui += DrawMeasured3DPreview;
        SceneView.RepaintAll();
        LogMeasured3DResults("Measured3DPreview", preview);
        Debug.Log(
            $"[Measured3DPreview] Scene values unchanged | Objects={preview.Results.Count}");
    }

    private static void DrawMeasured3DPreview(SceneView sceneView)
    {
        Factory3DPreviewState preview = activeMeasured3DPreview;
        if (sceneView == null || preview == null || preview.Stage == null)
        {
            SceneView.duringSceneGui -= DrawMeasured3DPreview;
            return;
        }

        Transform stage = preview.Stage;
        DrawClosedOutline(
            GetFactory3DWorldCorners(stage, preview.InteriorBounds, preview.PreviewY),
            new Color(0.2f, 1f, 0.35f, 1f),
            4f);
        GUIStyle labelStyle = GetMeasuredPreviewLabelStyle();
        int labelIndex = 0;
        foreach (Measured3DResult result in preview.Results)
        {
            DrawClosedOutline(
                GetFactory3DWorldCorners(stage, result.CurrentFootprint, preview.PreviewY),
                new Color(1f, 0.9f, 0.1f, 1f),
                2f);
            DrawClosedOutline(
                GetFactory3DWorldCorners(stage, result.TargetFootprint, preview.PreviewY),
                new Color(0.15f, 0.95f, 1f, 1f),
                3f);
            DrawClosedOutline(
                GetFactory3DWorldCorners(stage, result.PredictedFootprint, preview.PreviewY),
                new Color(0.8f, 0.3f, 1f, 1f),
                2f);

            Vector3 labelAnchor = stage.TransformPoint(new Vector3(
                result.TargetFootprint.xMax,
                preview.PreviewY,
                result.TargetFootprint.yMax));
            Vector3 labelOffset = sceneView.camera != null
                ? sceneView.camera.transform.up * HandleUtility.GetHandleSize(labelAnchor) *
                  (0.08f + labelIndex * 0.035f)
                : Vector3.up * (0.1f + labelIndex * 0.04f);
            Handles.Label(
                labelAnchor + labelOffset,
                BuildMeasured3DPreviewLabel(result),
                labelStyle);
            labelIndex++;
        }
    }

    private static Vector3[] GetFactory3DWorldCorners(
        Transform stage,
        Rect bounds,
        float localY)
    {
        return new[]
        {
            stage.TransformPoint(new Vector3(bounds.xMin, localY, bounds.yMin)),
            stage.TransformPoint(new Vector3(bounds.xMin, localY, bounds.yMax)),
            stage.TransformPoint(new Vector3(bounds.xMax, localY, bounds.yMax)),
            stage.TransformPoint(new Vector3(bounds.xMax, localY, bounds.yMin))
        };
    }

    private static string BuildMeasured3DPreviewLabel(Measured3DResult result)
    {
        string scaleLabel = result.FootprintApplyReady
            ? $"{FormatFloat(result.ScaleXZ.x)}/{FormatFloat(result.ScaleXZ.y)}"
            : "BLOCKED";
        return
            $"{result.ShortLabel} Current {FormatFloat(result.CurrentFootprint.width)}x" +
            $"{FormatFloat(result.CurrentFootprint.height)} -> Target " +
            $"{FormatFloat(result.TargetFootprint.width)}x{FormatFloat(result.TargetFootprint.height)}\n" +
            $"Move X/Z {FormatFloat(result.MoveXZ.x)}/{FormatFloat(result.MoveXZ.y)} | " +
            $"Scale X/Z {scaleLabel}";
    }

    private static bool TryBuildMeasured3DLayout(
        Scene scene,
        out Factory3DPreviewState preview)
    {
        preview = null;
        Debug.Log(
            $"[Measured3D] MappingVersion={Measured3DMappingVersion} | " +
            "PalletLogicalZone=C_PalletArea_3D | PalletPlacementRoot=Pallet_Group_3D");
        scr_MapMeasuredLayoutConfig config = LoadMeasuredLayoutConfig();
        if (config == null || !config.HasFull2DInteriorBounds)
        {
            Debug.LogError(
                "[Measured3D] BLOCKED: saved Full 2D Interior Bounds are required. " +
                "Calibrate and save the finalized Full 2D interior first.");
            return false;
        }

        if (!config.Factory3DInteriorSaved)
        {
            Debug.LogError(
                "[Measured3D] BLOCKED: saved Factory 3D Interior Bounds are required. " +
                "Run Calibrate Factory 3D Interior Bounds, then Save Factory 3D Interior Bounds.");
            return false;
        }

        if (!TryResolveFactory3DStageAndFloor(
                scene,
                out Transform stage,
                out Transform floorRoot,
                out Rect floorBounds,
                out float floorMinY,
                out float floorMaxY))
        {
            return false;
        }

        Rect full2DInterior = config.Full2DInteriorBounds;
        Rect factory3DInterior = config.Factory3DInteriorBounds;
        if (!IsFinite(full2DInterior) || full2DInterior.width <= 0f ||
            full2DInterior.height <= 0f ||
            !ValidateFactory3DInteriorBounds(factory3DInterior, floorBounds, true))
        {
            Debug.LogError("[Measured3D] BLOCKED: saved interior bounds are invalid.");
            return false;
        }

        Transform factoryView = FindUniqueSceneTransform(scene, "Panel_Main_FactoryView");
        Transform fullMapRootTransform = FindUniqueDescendant(
            factoryView,
            "RealMapLayoutRoot",
            "Measured3D/Full2D");
        RectTransform fullMapRoot = fullMapRootTransform as RectTransform;
        if (fullMapRoot == null)
        {
            Debug.LogError("[Measured3D] Full 2D coordinate root was not found.");
            return false;
        }

        Factory3DPreviewState built = new Factory3DPreviewState
        {
            Stage = stage,
            FloorBounds = floorBounds,
            InteriorBounds = factory3DInterior,
            PreviewY = floorMaxY + Mathf.Max(0.02f, floorMaxY - floorMinY)
        };

        for (int index = 0; index < MeasuredFactory3DPlacementRootNames.Length; index++)
        {
            RectTransform sourceRoot = FindUniqueDescendant(
                fullMapRoot,
                FullMapFacilityNames[index],
                "Measured3D/Full2DSource") as RectTransform;
            Transform logicalZoneRoot = FindExactFactory3DStageRoot(
                stage,
                MeasuredFactory3DLogicalZoneNames[index],
                "Measured3D/LogicalZone");
            Transform placementRoot = FindExactFactory3DStageRoot(
                stage,
                MeasuredFactory3DPlacementRootNames[index],
                "Measured3D/PlacementRoot");
            if (sourceRoot == null || logicalZoneRoot == null || placementRoot == null)
            {
                Debug.LogError(
                    $"[Measured3D] Missing source, logical zone, or placement root | " +
                    $"Source={FullMapFacilityNames[index]} | " +
                    $"LogicalZone={MeasuredFactory3DLogicalZoneNames[index]} | " +
                    $"Placement={MeasuredFactory3DPlacementRootNames[index]}");
                return false;
            }

            if (!TryGetRectBoundsInCoordinateRoot(sourceRoot, fullMapRoot, out Rect sourceRootBounds))
            {
                Debug.LogError(
                    $"[Measured3D] Cannot read Full 2D source bounds: " +
                    $"{BuildHierarchyPath(sourceRoot)}");
                return false;
            }

            bool isChargingSource = string.Equals(
                sourceRoot.name,
                "A_ChargingZone",
                StringComparison.Ordinal);
            if (isChargingSource)
            {
                LogMeasured3DChargingSourceHierarchy(
                    sourceRoot,
                    fullMapRoot,
                    full2DInterior);
            }

            Measured2DResult sourceAnalysis = new Measured2DResult
            {
                ViewName = "Full2D",
                FacilityId = MeasuredFacilityDefinitions[index].Id,
                CoordinateRoot = fullMapRoot,
                Target = sourceRoot,
                CurrentBounds = sourceRootBounds,
                InteriorBounds = full2DInterior,
                InteriorBoundsSaved = true,
                HasPhysicalTarget = false
            };
            bool compositeBoundsAvailable = TryAnalyzeMeasuredImage(sourceAnalysis) &&
                                            sourceAnalysis.CompositeVisibleBoundsValid &&
                                            sourceAnalysis.ImageDrawValidationPassed;
            Rect sourceVisibleBounds;
            if (compositeBoundsAvailable)
            {
                sourceVisibleBounds = sourceAnalysis.CurrentImageDrawBounds;
            }
            else if (TryGetSolidImageBoundsForMeasured3D(
                         sourceRoot,
                         fullMapRoot,
                         out sourceVisibleBounds,
                         out int solidImageCount))
            {
                Debug.Log(
                    $"[Measured3D] Full 2D solid Image bounds fallback | " +
                    $"Object={BuildHierarchyPath(sourceRoot)} | " +
                    $"EligibleSolidImages={solidImageCount} | " +
                    $"VisibleBounds={FormatRect(sourceVisibleBounds)} | " +
                    "Reason=enabled sprite-less Image renders its full RectTransform bounds");
            }
            else
            {
                Debug.LogError(
                    $"[Measured3D] Full 2D Composite Visible Bounds are unavailable: " +
                    $"{BuildHierarchyPath(sourceRoot)}");
                return false;
            }

            Vector2 sourceCenter = sourceVisibleBounds.center;
            bool sourceCenterInside =
                sourceCenter.x >= full2DInterior.xMin &&
                sourceCenter.x <= full2DInterior.xMax &&
                sourceCenter.y >= full2DInterior.yMin &&
                sourceCenter.y <= full2DInterior.yMax;
            if (!IsFinite(sourceVisibleBounds) ||
                sourceVisibleBounds.width <= 0f ||
                sourceVisibleBounds.height <= 0f ||
                !sourceCenterInside)
            {
                Debug.LogError(
                    $"[Measured3D] BLOCKED: Full 2D source bounds are invalid or their center is outside " +
                    $"the saved interior | " +
                    $"Object={BuildHierarchyPath(sourceRoot)} | " +
                    $"OriginalFull2DVisibleBounds={FormatRect(sourceVisibleBounds)} | " +
                    $"SavedFull2DInteriorBounds={FormatRect(full2DInterior)} | " +
                    $"SourceCenter={FormatVector2(sourceCenter)} | CenterInside={sourceCenterInside}");
                return false;
            }

            Vector4 clampExcess = new Vector4(
                Mathf.Max(0f, full2DInterior.xMin - sourceVisibleBounds.xMin),
                Mathf.Max(0f, sourceVisibleBounds.xMax - full2DInterior.xMax),
                Mathf.Max(0f, full2DInterior.yMin - sourceVisibleBounds.yMin),
                Mathf.Max(0f, sourceVisibleBounds.yMax - full2DInterior.yMax));
            Rect clampedSourceBounds = Rect.MinMaxRect(
                Mathf.Clamp(sourceVisibleBounds.xMin, full2DInterior.xMin, full2DInterior.xMax),
                Mathf.Clamp(sourceVisibleBounds.yMin, full2DInterior.yMin, full2DInterior.yMax),
                Mathf.Clamp(sourceVisibleBounds.xMax, full2DInterior.xMin, full2DInterior.xMax),
                Mathf.Clamp(sourceVisibleBounds.yMax, full2DInterior.yMin, full2DInterior.yMax));
            if (!IsFinite(clampedSourceBounds) ||
                clampedSourceBounds.width <= 0f ||
                clampedSourceBounds.height <= 0f)
            {
                Debug.LogError(
                    $"[Measured3D] BLOCKED: clamped Full 2D source bounds are degenerate | " +
                    $"Object={BuildHierarchyPath(sourceRoot)} | " +
                    $"OriginalFull2DVisibleBounds={FormatRect(sourceVisibleBounds)} | " +
                    $"SavedFull2DInteriorBounds={FormatRect(full2DInterior)} | " +
                    $"ClampedSourceBounds={FormatRect(clampedSourceBounds)}");
                return false;
            }

            Rect normalized = NormalizeRectUnclamped(clampedSourceBounds, full2DInterior);
            if (!IsFinite(normalized) || normalized.width <= 0f || normalized.height <= 0f)
            {
                Debug.LogError(
                    $"[Measured3D] BLOCKED: clamped Full 2D source bounds cannot be normalized | " +
                    $"Object={BuildHierarchyPath(sourceRoot)} | " +
                    $"ClampedSourceBounds={FormatRect(clampedSourceBounds)} | " +
                    $"SavedFull2DInteriorBounds={FormatRect(full2DInterior)}");
                return false;
            }

            Rect orientedNormalized = ApplyFactory3DOrientation(
                normalized,
                config.Factory3DSwapXZ,
                config.Factory3DFlipX,
                config.Factory3DFlipZ);
            Rect targetFootprint = MapNormalizedRectToBounds(
                orientedNormalized,
                factory3DInterior);
            Debug.Log(
                $"[Measured3D] Source projection | Object={BuildHierarchyPath(sourceRoot)} | " +
                $"OriginalFull2DVisibleBounds={FormatRect(sourceVisibleBounds)} | " +
                $"SavedFull2DInteriorBounds={FormatRect(full2DInterior)} | " +
                $"ClampedSourceBounds={FormatRect(clampedSourceBounds)} | " +
                $"NormalizedBounds={FormatRect(normalized)} | " +
                $"ClampDirections={BuildClampDirectionLabel(clampExcess)} | " +
                $"ClampExcess=(Left={FormatFloat(clampExcess.x)}," +
                $"Right={FormatFloat(clampExcess.y)}," +
                $"Bottom={FormatFloat(clampExcess.z)}," +
                $"Top={FormatFloat(clampExcess.w)}) | " +
                $"Final3DTargetFootprint={FormatRect(targetFootprint)}");
            if (isChargingSource)
            {
                Debug.Log(
                    $"[Measured3D][Charging] Projection ready | " +
                    $"LogicalRoot={BuildHierarchyPath(sourceRoot)} | " +
                    $"SavedFull2DInterior={FormatRect(full2DInterior)} | " +
                    $"ClampedSourceBounds={FormatRect(clampedSourceBounds)} | " +
                    $"NormalizedBounds={FormatRect(normalized)} | " +
                    $"Final3DTargetFootprint={FormatRect(targetFootprint)}");
            }
            if (!TryCalculateFactory3DRendererFootprint(
                    placementRoot,
                    stage,
                    out Rect currentFootprint,
                    out float currentMinY,
                    out float currentMaxY,
                    out List<Renderer> renderers,
                    out string rendererSummary))
            {
                Debug.LogError(
                    $"[Measured3D] No eligible active Renderer for " +
                    $"PlacementRoot={BuildHierarchyPath(placementRoot)} | " +
                    $"LogicalZoneRoot={BuildHierarchyPath(logicalZoneRoot)}");
                return false;
            }

            Debug.Log(
                $"[Measured3D] Facility Renderer Footprint | " +
                $"LogicalZoneRoot={BuildHierarchyPath(logicalZoneRoot)} | " +
                $"PlacementRoot={BuildHierarchyPath(placementRoot)} | " +
                $"RendererCount={renderers.Count} | " +
                $"Renderers=[{rendererSummary}] | " +
                $"CompositeFootprintXZ={FormatRect(currentFootprint)}");

            Measured3DResult result = new Measured3DResult
            {
                FacilityId = MeasuredFacilityDefinitions[index].Id,
                ShortLabel = MeasuredFactory3DShortLabels[index],
                Stage = stage,
                LogicalZoneRoot = logicalZoneRoot,
                PlacementRoot = placementRoot,
                SourceVisibleBounds = sourceVisibleBounds,
                SourceClampedBounds = clampedSourceBounds,
                SourceNormalizedBounds = normalized,
                OrientedNormalizedBounds = orientedNormalized,
                CurrentFootprint = currentFootprint,
                TargetFootprint = targetFootprint,
                CurrentMinY = currentMinY,
                CurrentMaxY = currentMaxY,
                RendererCount = renderers.Count,
                RendererSummary = rendererSummary,
                SourceValid = true,
                CurrentFootprintValid = true,
                TargetValid = IsFinite(targetFootprint) &&
                              targetFootprint.width > 0f &&
                              targetFootprint.height > 0f
            };
            AnalyzeFactory3DFacilitySafety(result, renderers);

            result.MoveXZ = result.TargetFootprint.center - result.CurrentFootprint.center;
            result.PositionOnlyFootprint = TranslateRect(result.CurrentFootprint, result.MoveXZ);
            result.PositionApplyReady = result.TargetValid &&
                                        placementRoot.parent == stage &&
                                        IsFinite(result.MoveXZ);
            result.ScaleXZ = new Vector2(
                result.TargetFootprint.width / result.CurrentFootprint.width,
                result.TargetFootprint.height / result.CurrentFootprint.height);
            bool scaleFinite = IsFinite(result.ScaleXZ) &&
                               result.ScaleXZ.x > 0f && result.ScaleXZ.y > 0f;
            result.FootprintApplyReady = result.PositionApplyReady &&
                                         result.SafeVisualRoot != null &&
                                         result.VisualAxesAligned &&
                                         scaleFinite;
            if (result.FootprintApplyReady)
            {
                Vector3 visualPivot = stage.InverseTransformPoint(result.SafeVisualRoot.position);
                Vector2 movedPivot = new Vector2(visualPivot.x, visualPivot.z) + result.MoveXZ;
                result.PredictedFootprint = ScaleRectAroundPivot(
                    result.PositionOnlyFootprint,
                    movedPivot,
                    result.ScaleXZ);
            }
            else
            {
                result.PredictedFootprint = result.PositionOnlyFootprint;
            }

            if (!result.PositionApplyReady)
            {
                Debug.LogError(
                    $"[Measured3D] Position Apply blocked for {BuildHierarchyPath(placementRoot)} | " +
                    "Placement Root must be a direct Factory3DStage child and all bounds must be finite.");
                return false;
            }

            built.Results.Add(result);
        }

        preview = built;
        return true;
    }

    private static Rect NormalizeRectUnclamped(Rect value, Rect bounds)
    {
        return Rect.MinMaxRect(
            (value.xMin - bounds.xMin) / bounds.width,
            (value.yMin - bounds.yMin) / bounds.height,
            (value.xMax - bounds.xMin) / bounds.width,
            (value.yMax - bounds.yMin) / bounds.height);
    }

    private static Transform FindExactFactory3DStageRoot(
        Transform stage,
        string exactName,
        string context)
    {
        if (stage == null || string.IsNullOrEmpty(exactName))
        {
            Debug.LogError($"[Measured3D] Missing Stage or exact root name: {context}/{exactName}");
            return null;
        }

        Transform directChild = stage.Find(exactName);
        if (directChild != null && directChild.parent == stage &&
            string.Equals(directChild.name, exactName, StringComparison.Ordinal))
        {
            return directChild;
        }

        return FindUniqueDescendant(stage, exactName, context);
    }

    private static bool TryGetSolidImageBoundsForMeasured3D(
        RectTransform sourceRoot,
        RectTransform coordinateRoot,
        out Rect visibleBounds,
        out int eligibleImageCount)
    {
        visibleBounds = default;
        eligibleImageCount = 0;
        if (sourceRoot == null || coordinateRoot == null)
        {
            return false;
        }

        bool hasBounds = false;
        Image[] images = sourceRoot.GetComponentsInChildren<Image>(true);
        foreach (Image image in images)
        {
            if (!IsEligibleCompositeVisual(image.transform, sourceRoot) ||
                !image.enabled || image.color.a <= 0.000001f)
            {
                continue;
            }

            Sprite sprite = image.overrideSprite != null ? image.overrideSprite : image.sprite;
            Rect localRect = image.rectTransform.rect;
            if (sprite != null || !IsFinite(localRect) ||
                localRect.width <= 0.000001f || localRect.height <= 0.000001f ||
                !TryGetTransformLocalRectBoundsInCoordinateRoot(
                    image.rectTransform,
                    coordinateRoot,
                    localRect,
                    out Rect imageBounds))
            {
                continue;
            }

            visibleBounds = hasBounds
                ? Rect.MinMaxRect(
                    Mathf.Min(visibleBounds.xMin, imageBounds.xMin),
                    Mathf.Min(visibleBounds.yMin, imageBounds.yMin),
                    Mathf.Max(visibleBounds.xMax, imageBounds.xMax),
                    Mathf.Max(visibleBounds.yMax, imageBounds.yMax))
                : imageBounds;
            hasBounds = true;
            eligibleImageCount++;
        }

        return hasBounds && IsFinite(visibleBounds) &&
               visibleBounds.width > 0f && visibleBounds.height > 0f;
    }

    private static void LogMeasured3DChargingSourceHierarchy(
        RectTransform logicalRoot,
        RectTransform coordinateRoot,
        Rect savedInteriorBounds)
    {
        if (logicalRoot == null || coordinateRoot == null)
        {
            return;
        }

        StringBuilder report = new StringBuilder(8192);
        report.AppendLine("[Measured3D][Charging] Full 2D source hierarchy diagnostics");
        report.AppendLine($"LogicalRoot={BuildHierarchyPath(logicalRoot)}");
        report.AppendLine($"CoordinateRoot={BuildHierarchyPath(coordinateRoot)}");
        report.AppendLine($"SavedFull2DInterior={FormatRect(savedInteriorBounds)}");

        Transform[] hierarchy = logicalRoot.GetComponentsInChildren<Transform>(true);
        foreach (Transform item in hierarchy)
        {
            Component[] components = item.GetComponents<Component>();
            List<string> componentNames = new List<string>(components.Length);
            foreach (Component component in components)
            {
                componentNames.Add(component != null
                    ? component.GetType().Name
                    : "MissingScript");
            }

            report.AppendLine($"Object={BuildHierarchyPath(item)}");
            report.AppendLine(
                $"  ActiveSelf={item.gameObject.activeSelf} | " +
                $"ActiveInHierarchy={item.gameObject.activeInHierarchy} | " +
                $"Components=[{string.Join(",", componentNames)}]");

            RectTransform rectTransform = item as RectTransform;
            if (rectTransform != null)
            {
                Vector3[] worldCorners = new Vector3[4];
                rectTransform.GetWorldCorners(worldCorners);
                bool hasRootLocalBounds = TryGetTransformLocalRectBoundsInCoordinateRoot(
                    rectTransform,
                    coordinateRoot,
                    rectTransform.rect,
                    out Rect rootLocalBounds);
                report.AppendLine(
                    $"  Rect={FormatRect(rectTransform.rect)} | " +
                    $"WorldCorners=[{FormatVector3(worldCorners[0])}," +
                    $"{FormatVector3(worldCorners[1])}," +
                    $"{FormatVector3(worldCorners[2])}," +
                    $"{FormatVector3(worldCorners[3])}] | " +
                    $"CoordinateRootLocalBounds=" +
                    $"{(hasRootLocalBounds ? FormatRect(rootLocalBounds) : "Unavailable")}");
            }

            Image image = item.GetComponent<Image>();
            if (image != null)
            {
                Sprite sprite = image.overrideSprite != null
                    ? image.overrideSprite
                    : image.sprite;
                report.AppendLine(
                    $"  ImageEnabled={image.enabled} | " +
                    $"Sprite={(sprite != null ? sprite.name : "None")} | " +
                    $"Color={FormatColor(image.color)} | Alpha={FormatFloat(image.color.a)} | " +
                    $"Type={image.type} | PreserveAspect={image.preserveAspect}");
            }

            RawImage rawImage = item.GetComponent<RawImage>();
            if (rawImage != null)
            {
                report.AppendLine(
                    $"  RawImageEnabled={rawImage.enabled} | " +
                    $"Texture={(rawImage.texture != null ? rawImage.texture.name : "None")} | " +
                    $"Color={FormatColor(rawImage.color)} | Alpha={FormatFloat(rawImage.color.a)}");
            }

            Graphic graphic = item.GetComponent<Graphic>();
            CanvasRenderer canvasRenderer = item.GetComponent<CanvasRenderer>();
            if (graphic != null || canvasRenderer != null)
            {
                report.AppendLine(
                    $"  GraphicEnabled={(graphic != null ? graphic.enabled.ToString() : "N/A")} | " +
                    $"CanvasRendererCull=" +
                    $"{(canvasRenderer != null ? canvasRenderer.cull.ToString() : "N/A")}");
            }
        }

        bool compositeAvailable = TryCalculateCompositeVisibleBounds(
            logicalRoot,
            coordinateRoot,
            out Rect compositeBounds,
            out CompositeBoundsAccumulator composite);
        int eligibleGraphicCount = composite.SpriteImageCount +
                                   composite.SolidColorImageCount +
                                   composite.RawImageCount;
        report.AppendLine(
            $"EligibleGraphicCount={eligibleGraphicCount} | " +
            $"EligibleVisualCount={composite.VisualCount} | " +
            $"SpriteImages={composite.SpriteImageCount} | " +
            $"SolidColorImages={composite.SolidColorImageCount} | " +
            $"RawImages={composite.RawImageCount} | " +
            $"SpriteRenderers={composite.SpriteRendererCount} | " +
            $"MeshRenderers={composite.MeshRendererCount} | " +
            $"SkinnedMeshRenderers={composite.SkinnedMeshRendererCount}");
        foreach (string visualDetail in composite.VisualDetails)
        {
            report.AppendLine($"  EligibleVisual={visualDetail}");
        }

        report.AppendLine(
            $"CompositeAvailable={compositeAvailable} | " +
            $"ValidationPassed={composite.ValidationPassed} | " +
            $"CompositeBounds=" +
            $"{(compositeAvailable ? FormatRect(compositeBounds) : "Unavailable")}");
        Debug.Log(report.ToString());
    }

    private static string BuildClampDirectionLabel(Vector4 clampExcess)
    {
        List<string> directions = new List<string>(4);
        if (clampExcess.x > 0f)
        {
            directions.Add("LEFT");
        }

        if (clampExcess.y > 0f)
        {
            directions.Add("RIGHT");
        }

        if (clampExcess.z > 0f)
        {
            directions.Add("BOTTOM");
        }

        if (clampExcess.w > 0f)
        {
            directions.Add("TOP");
        }

        return directions.Count > 0 ? string.Join(",", directions) : "NONE";
    }

    private static Rect ApplyFactory3DOrientation(
        Rect normalized,
        bool swapXZ,
        bool flipX,
        bool flipZ)
    {
        float xMin = swapXZ ? normalized.yMin : normalized.xMin;
        float xMax = swapXZ ? normalized.yMax : normalized.xMax;
        float zMin = swapXZ ? normalized.xMin : normalized.yMin;
        float zMax = swapXZ ? normalized.xMax : normalized.yMax;
        if (flipX)
        {
            float previousMin = xMin;
            xMin = 1f - xMax;
            xMax = 1f - previousMin;
        }

        if (flipZ)
        {
            float previousMin = zMin;
            zMin = 1f - zMax;
            zMax = 1f - previousMin;
        }

        return Rect.MinMaxRect(xMin, zMin, xMax, zMax);
    }

    private static Rect MapNormalizedRectToBounds(Rect normalized, Rect bounds)
    {
        return Rect.MinMaxRect(
            Mathf.LerpUnclamped(bounds.xMin, bounds.xMax, normalized.xMin),
            Mathf.LerpUnclamped(bounds.yMin, bounds.yMax, normalized.yMin),
            Mathf.LerpUnclamped(bounds.xMin, bounds.xMax, normalized.xMax),
            Mathf.LerpUnclamped(bounds.yMin, bounds.yMax, normalized.yMax));
    }

    private static Rect TranslateRect(Rect value, Vector2 delta)
    {
        return new Rect(value.position + delta, value.size);
    }

    private static Rect ScaleRectAroundPivot(Rect value, Vector2 pivot, Vector2 scale)
    {
        float firstX = pivot.x + (value.xMin - pivot.x) * scale.x;
        float secondX = pivot.x + (value.xMax - pivot.x) * scale.x;
        float firstZ = pivot.y + (value.yMin - pivot.y) * scale.y;
        float secondZ = pivot.y + (value.yMax - pivot.y) * scale.y;
        return Rect.MinMaxRect(
            Mathf.Min(firstX, secondX),
            Mathf.Min(firstZ, secondZ),
            Mathf.Max(firstX, secondX),
            Mathf.Max(firstZ, secondZ));
    }

    private static bool TryResolveFactory3DStageAndFloor(
        Scene scene,
        out Transform stage,
        out Transform floorRoot,
        out Rect floorBounds,
        out float floorMinY,
        out float floorMaxY)
    {
        stage = FindUniqueSceneTransform(scene, "Factory3DStage");
        floorRoot = FindUniqueDescendant(stage, "Floor_3DMap", "Measured3D/Floor");
        floorBounds = default;
        floorMinY = 0f;
        floorMaxY = 0f;
        if (stage == null || floorRoot == null)
        {
            Debug.LogError("[Measured3D] Factory3DStage or Floor_3DMap was not found.");
            return false;
        }

        if (!TryCalculateFactory3DRendererFootprint(
                floorRoot,
                stage,
                out floorBounds,
                out floorMinY,
                out floorMaxY,
                out List<Renderer> floorRenderers,
                out string rendererSummary))
        {
            Debug.LogError(
                $"[Measured3D] Floor Renderer bounds are unavailable: " +
                $"{BuildHierarchyPath(floorRoot)}");
            return false;
        }

        Debug.Log(
            $"[Measured3D] Factory3DStage={BuildHierarchyPath(stage)} | " +
            $"Floor={BuildHierarchyPath(floorRoot)} | FloorBoundsXZ={FormatRect(floorBounds)} | " +
            $"FloorY={FormatFloat(floorMinY)}..{FormatFloat(floorMaxY)} | " +
            $"FloorRenderers={floorRenderers.Count} [{rendererSummary}]");
        return true;
    }

    private static bool TryCalculateFactory3DRendererFootprint(
        Transform facilityRoot,
        Transform stage,
        out Rect footprint,
        out float minY,
        out float maxY,
        out List<Renderer> includedRenderers,
        out string rendererSummary)
    {
        footprint = default;
        minY = float.PositiveInfinity;
        maxY = float.NegativeInfinity;
        includedRenderers = new List<Renderer>();
        rendererSummary = string.Empty;
        if (facilityRoot == null || stage == null)
        {
            return false;
        }

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        Renderer[] renderers = facilityRoot.GetComponentsInChildren<Renderer>(true);
        List<string> rendererPaths = new List<string>();
        foreach (Renderer renderer in renderers)
        {
            if (!IsEligibleFactory3DRenderer(renderer, facilityRoot))
            {
                continue;
            }

            Bounds worldBounds = renderer.bounds;
            Vector3 worldMin = worldBounds.min;
            Vector3 worldMax = worldBounds.max;
            float rendererMinX = float.PositiveInfinity;
            float rendererMaxX = float.NegativeInfinity;
            float rendererMinZ = float.PositiveInfinity;
            float rendererMaxZ = float.NegativeInfinity;
            for (int xIndex = 0; xIndex < 2; xIndex++)
            {
                for (int yIndex = 0; yIndex < 2; yIndex++)
                {
                    for (int zIndex = 0; zIndex < 2; zIndex++)
                    {
                        Vector3 worldCorner = new Vector3(
                            xIndex == 0 ? worldMin.x : worldMax.x,
                            yIndex == 0 ? worldMin.y : worldMax.y,
                            zIndex == 0 ? worldMin.z : worldMax.z);
                        Vector3 localCorner = stage.InverseTransformPoint(worldCorner);
                        minX = Mathf.Min(minX, localCorner.x);
                        maxX = Mathf.Max(maxX, localCorner.x);
                        minY = Mathf.Min(minY, localCorner.y);
                        maxY = Mathf.Max(maxY, localCorner.y);
                        minZ = Mathf.Min(minZ, localCorner.z);
                        maxZ = Mathf.Max(maxZ, localCorner.z);
                        rendererMinX = Mathf.Min(rendererMinX, localCorner.x);
                        rendererMaxX = Mathf.Max(rendererMaxX, localCorner.x);
                        rendererMinZ = Mathf.Min(rendererMinZ, localCorner.z);
                        rendererMaxZ = Mathf.Max(rendererMaxZ, localCorner.z);
                    }
                }
            }

            includedRenderers.Add(renderer);
            Rect rendererFootprint = Rect.MinMaxRect(
                rendererMinX,
                rendererMinZ,
                rendererMaxX,
                rendererMaxZ);
            rendererPaths.Add(
                $"Path={BuildHierarchyPath(renderer.transform)}," +
                $"StageLocalBoundsXZ={FormatRect(rendererFootprint)}");
        }

        if (includedRenderers.Count == 0 ||
            !IsFinite(minX) || !IsFinite(maxX) ||
            !IsFinite(minY) || !IsFinite(maxY) ||
            !IsFinite(minZ) || !IsFinite(maxZ) ||
            maxX <= minX || maxZ <= minZ)
        {
            includedRenderers.Clear();
            return false;
        }

        footprint = Rect.MinMaxRect(minX, minZ, maxX, maxZ);
        rendererSummary = string.Join("; ", rendererPaths);
        return true;
    }

    private static bool IsEligibleFactory3DRenderer(Renderer renderer, Transform facilityRoot)
    {
        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
            (!(renderer is MeshRenderer) &&
             !(renderer is SkinnedMeshRenderer) &&
             !(renderer is SpriteRenderer)))
        {
            return false;
        }

        if (renderer.GetComponent<TMP_Text>() != null)
        {
            return false;
        }

        Transform current = renderer.transform;
        while (current != null)
        {
            if (IsExcludedFactory3DVisualName(current.name))
            {
                return false;
            }

            if (current == facilityRoot)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static bool IsExcludedFactory3DVisualName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
        {
            return false;
        }

        string normalized = objectName.ToLowerInvariant();
        return normalized.Contains("robot") ||
               normalized.Contains("marker") ||
               normalized.Contains("event") ||
               normalized.Contains("debug") ||
               normalized.Contains("axis") ||
               normalized.StartsWith("text_") ||
               normalized.Contains("tmp");
    }

    private static void AnalyzeFactory3DFacilitySafety(
        Measured3DResult result,
        IReadOnlyList<Renderer> renderers)
    {
        Transform root = result.PlacementRoot;
        result.HasScript = HasFactory3DRuntimeController(root);
        result.HasCollider = root.GetComponentInChildren<Collider>(true) != null;
        result.HasAnimator = root.GetComponentInChildren<Animator>(true) != null;
        result.HasRigidbody = root.GetComponentInChildren<Rigidbody>(true) != null;
        result.HasCamera = root.GetComponentInChildren<Camera>(true) != null;

        Transform visualRoot = FindLowestCommonAncestor(root, renderers);
        if (visualRoot == null)
        {
            result.ScaleBlockReason = "No common Visual Mesh Root";
            return;
        }

        if (!IsSafeFactory3DVisualRoot(visualRoot, root, out string reason))
        {
            result.ScaleBlockReason = reason;
            return;
        }

        result.SafeVisualRoot = visualRoot;
        result.VisualAxesAligned = AreFactory3DVisualAxesAligned(visualRoot, result.Stage);
        if (!result.VisualAxesAligned)
        {
            result.ScaleBlockReason = "Visual Root local X/Z axes do not align with Factory3DStage X/Z";
        }
    }

    private static Transform FindLowestCommonAncestor(
        Transform facilityRoot,
        IReadOnlyList<Renderer> renderers)
    {
        if (facilityRoot == null || renderers == null || renderers.Count == 0)
        {
            return null;
        }

        Transform candidate = renderers[0].transform;
        while (candidate != null && IsDescendantOrSelf(candidate, facilityRoot))
        {
            bool containsAll = true;
            for (int index = 1; index < renderers.Count; index++)
            {
                if (!IsDescendantOrSelf(renderers[index].transform, candidate))
                {
                    containsAll = false;
                    break;
                }
            }

            if (containsAll)
            {
                return candidate;
            }

            if (candidate == facilityRoot)
            {
                break;
            }

            candidate = candidate.parent;
        }

        return null;
    }

    private static bool IsDescendantOrSelf(Transform candidate, Transform ancestor)
    {
        Transform current = candidate;
        while (current != null)
        {
            if (current == ancestor)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static bool IsSafeFactory3DVisualRoot(
        Transform visualRoot,
        Transform placementRoot,
        out string reason)
    {
        reason = string.Empty;
        if (visualRoot == null || placementRoot == null ||
            !IsDescendantOrSelf(visualRoot, placementRoot))
        {
            reason = "Visual Root is outside the Placement Root";
            return false;
        }

        if (visualRoot.GetComponentInChildren<Camera>(true) != null)
        {
            reason = "Visual Root contains a Camera";
            return false;
        }

        if (visualRoot.GetComponentInChildren<Rigidbody>(true) != null)
        {
            reason = "Visual Root contains a Rigidbody";
            return false;
        }

        if (HasFactory3DRuntimeController(visualRoot))
        {
            reason = "Visual Root contains a Runtime Controller";
            return false;
        }

        Transform[] descendants = visualRoot.GetComponentsInChildren<Transform>(true);
        foreach (Transform descendant in descendants)
        {
            if (descendant != visualRoot && IsMeasuredFactory3DFacilityName(descendant.name))
            {
                reason = $"Visual Root contains another facility: {descendant.name}";
                return false;
            }

            string lowerName = descendant.name.ToLowerInvariant();
            if (lowerName.Contains("robot") || lowerName.Contains("eventmarker") ||
                lowerName.Contains("robotmarker"))
            {
                reason = $"Visual Root contains a Robot/Event Marker: {descendant.name}";
                return false;
            }
        }

        return true;
    }

    private static bool HasFactory3DRuntimeController(Transform root)
    {
        if (root == null)
        {
            return false;
        }

        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null || behaviour is TMP_Text)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool AreFactory3DVisualAxesAligned(Transform visualRoot, Transform stage)
    {
        if (visualRoot == null || stage == null)
        {
            return false;
        }

        Vector3 xInStage = stage.InverseTransformVector(
            visualRoot.TransformVector(Vector3.right)).normalized;
        Vector3 zInStage = stage.InverseTransformVector(
            visualRoot.TransformVector(Vector3.forward)).normalized;
        const float alignmentTolerance = 0.001f;
        bool xAligned = Mathf.Abs(xInStage.y) <= alignmentTolerance &&
                        Mathf.Abs(xInStage.z) <= alignmentTolerance &&
                        Mathf.Abs(Mathf.Abs(xInStage.x) - 1f) <= alignmentTolerance;
        bool zAligned = Mathf.Abs(zInStage.y) <= alignmentTolerance &&
                        Mathf.Abs(zInStage.x) <= alignmentTolerance &&
                        Mathf.Abs(Mathf.Abs(zInStage.z) - 1f) <= alignmentTolerance;
        return xAligned && zAligned;
    }

    private static bool IsMeasuredFactory3DFacilityName(string objectName)
    {
        foreach (string facilityName in Factory3DFacilityNames)
        {
            if (string.Equals(objectName, facilityName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void LogFactory3DInterior(
        string category,
        Transform stage,
        Transform floorRoot,
        Rect floorBounds,
        Rect interiorBounds,
        scr_MapMeasuredLayoutConfig config)
    {
        Debug.Log(
            $"[{category}] Factory3DStage={BuildHierarchyPath(stage)} | " +
            $"Floor={BuildHierarchyPath(floorRoot)} | FloorBoundsXZ={FormatRect(floorBounds)} | " +
            $"MeasuredInteriorXZ={FormatRect(interiorBounds)} | " +
            $"Width={FormatFloat(interiorBounds.width)} | Depth={FormatFloat(interiorBounds.height)} | " +
            $"FlipX={config != null && config.Factory3DFlipX} | " +
            $"FlipZ={config != null && config.Factory3DFlipZ} | " +
            $"SwapXZ={config != null && config.Factory3DSwapXZ}");
    }

    private static void LogMeasured3DResults(
        string category,
        Factory3DPreviewState preview)
    {
        scr_MapMeasuredLayoutConfig config = LoadMeasuredLayoutConfig();
        LogFactory3DInterior(
            category,
            preview.Stage,
            FindUniqueDescendant(preview.Stage, "Floor_3DMap", category),
            preview.FloorBounds,
            preview.InteriorBounds,
            config);
        foreach (Measured3DResult result in preview.Results)
        {
            string scripts = result.HasScript ? "YES" : "NO";
            string colliders = result.HasCollider ? "YES" : "NO";
            string animator = result.HasAnimator ? "YES" : "NO";
            string rigidbody = result.HasRigidbody ? "YES" : "NO";
            string camera = result.HasCamera ? "YES" : "NO";
            Debug.Log(
                $"[{category}] {result.ShortLabel} | " +
                $"LogicalZoneRoot={BuildHierarchyPath(result.LogicalZoneRoot)} | " +
                $"PlacementRoot={BuildHierarchyPath(result.PlacementRoot)} | " +
                $"VisualMeshRoot={(result.SafeVisualRoot != null ? BuildHierarchyPath(result.SafeVisualRoot) : "BLOCKED")} | " +
                $"Scripts={scripts} | Colliders={colliders} | Animator={animator} | " +
                $"Rigidbody={rigidbody} | Camera={camera} | " +
                $"LocalPosition={FormatVector3(result.PlacementRoot.localPosition)} | " +
                $"LocalRotation={FormatQuaternion(result.PlacementRoot.localRotation)} | " +
                $"LocalScale={FormatVector3(result.PlacementRoot.localScale)} | " +
                $"Full2DVisible={FormatRect(result.SourceVisibleBounds)} | " +
                $"Full2DClamped={FormatRect(result.SourceClampedBounds)} | " +
                $"Normalized={FormatRect(result.SourceNormalizedBounds)} | " +
                $"OrientedNormalized={FormatRect(result.OrientedNormalizedBounds)} | " +
                $"CurrentFootprintXZ={FormatRect(result.CurrentFootprint)} | " +
                $"TargetFootprintXZ={FormatRect(result.TargetFootprint)} | " +
                $"MoveXZ={FormatVector2(result.MoveXZ)} | ScaleXZ={FormatVector2(result.ScaleXZ)} | " +
                $"PositionReady={result.PositionApplyReady} | " +
                $"FootprintReady={result.FootprintApplyReady} | " +
                $"ScaleBlock={result.ScaleBlockReason} | Renderers={result.RendererCount} " +
                $"[{result.RendererSummary}]");
        }
    }

    private static void ApplyFactory3DPositions()
    {
        if (!TryGetEditableControlTowerScene(out Scene scene) ||
            !TryBuildMeasured3DLayout(scene, out Factory3DPreviewState before))
        {
            return;
        }

        foreach (Measured3DResult result in before.Results)
        {
            if (!result.PositionApplyReady)
            {
                Debug.LogError(
                    $"[Measured3DPositionApply] BLOCKED: {result.ShortLabel} is not position-safe.");
                return;
            }
        }

        if (!TryResolveLatestBackupAsset(
                out scr_MapLayoutBackupAsset backup,
                out string backupPath) ||
            !ValidateBackupHeader(backup, scene, backupPath) ||
            !ValidateFactory3DBackupState(backup, scene, false, before.Results))
        {
            Debug.LogError(
                "[Measured3DPositionApply] BLOCKED: latest backup must exactly match the current Scene. " +
                "Run Backup Current Layout after reviewing the current Edit Mode layout.");
            return;
        }

        List<Factory3DTransformProtectionState> protectionStates =
            CaptureFactory3DTransformStates(before.Results, false);
        UnityEngine.Object[] undoTargets = BuildFactory3DUndoTargets(before.Results, false);
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Apply Measured Factory 3D Positions");
        Undo.RegisterCompleteObjectUndo(undoTargets, "Apply Measured Factory 3D Positions");

        foreach (Measured3DResult result in before.Results)
        {
            Transform root = result.PlacementRoot;
            Vector3 position = root.localPosition;
            position.x += result.MoveXZ.x;
            position.z += result.MoveXZ.y;
            root.localPosition = position;
            RecordFactory3DTransformModification(root);
        }

        Factory3DPreviewState after = null;
        bool validationPassed = ValidateFactory3DTransformProtection(
            protectionStates,
            true,
            false) &&
            TryBuildMeasured3DLayout(scene, out after) &&
            ValidateFactory3DPositionTargets(after.Results);
        if (!validationPassed)
        {
            Undo.RevertAllDownToGroup(undoGroup);
            Debug.LogError(
                "[Measured3DPositionApply] Validation failed. All position changes were reverted; " +
                "Scene was not saved.");
            return;
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        activeMeasured3DPreview = after;
        SceneView.duringSceneGui -= DrawMeasured3DPreview;
        SceneView.duringSceneGui += DrawMeasured3DPreview;
        SceneView.RepaintAll();
        LogMeasured3DResults("Measured3DPositionApply", after);
        Debug.Log(
            "[Measured3DPositionApply] SUCCESS | Modified=localPosition.x,localPosition.z only | " +
            "Undo=available | Scene saved=NO (review, then press Ctrl+S)");
    }

    private static void ApplyFactory3DFootprints()
    {
        if (!TryGetEditableControlTowerScene(out Scene scene) ||
            !TryBuildMeasured3DLayout(scene, out Factory3DPreviewState before))
        {
            return;
        }

        if (!ValidateFactory3DPositionTargets(before.Results))
        {
            Debug.LogError(
                "[Measured3DFootprintApply] BLOCKED: verified Position Apply is required first.");
            return;
        }

        foreach (Measured3DResult result in before.Results)
        {
            if (!result.FootprintApplyReady)
            {
                Debug.LogError(
                    $"[Measured3DFootprintApply] BLOCKED: {result.ShortLabel} has no safe Scale Root | " +
                    $"Reason={result.ScaleBlockReason}");
                return;
            }

            if (!RectApproximately(
                    result.PredictedFootprint,
                    result.TargetFootprint,
                    Factory3DFootprintTolerance))
            {
                Debug.LogError(
                    $"[Measured3DFootprintApply] BLOCKED: predicted footprint does not match target | " +
                    $"Facility={result.ShortLabel} | Predicted={FormatRect(result.PredictedFootprint)} | " +
                    $"Target={FormatRect(result.TargetFootprint)}");
                return;
            }
        }

        if (!TryResolveLatestBackupAsset(
                out scr_MapLayoutBackupAsset backup,
                out string backupPath) ||
            !ValidateBackupHeader(backup, scene, backupPath) ||
            !ValidateFactory3DBackupState(backup, scene, true, before.Results) ||
            !BackupContainsFactory3DVisualRoots(backup, before.Results))
        {
            Debug.LogError(
                "[Measured3DFootprintApply] BLOCKED: latest backup must cover every Scale Root and " +
                "match all protected Scene values.");
            return;
        }

        List<Factory3DTransformProtectionState> protectionStates =
            CaptureFactory3DTransformStates(before.Results, true);
        UnityEngine.Object[] undoTargets = BuildFactory3DUndoTargets(before.Results, true);
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Apply Measured Factory 3D Footprints");
        Undo.RegisterCompleteObjectUndo(undoTargets, "Apply Measured Factory 3D Footprints");

        foreach (Measured3DResult result in before.Results)
        {
            Transform visualRoot = result.SafeVisualRoot;
            Vector3 scale = visualRoot.localScale;
            scale.x *= result.ScaleXZ.x;
            scale.z *= result.ScaleXZ.y;
            visualRoot.localScale = scale;
            RecordFactory3DTransformModification(visualRoot);
        }

        Factory3DPreviewState after = null;
        bool validationPassed = ValidateFactory3DTransformProtection(
            protectionStates,
            false,
            true) &&
            TryBuildMeasured3DLayout(scene, out after) &&
            ValidateFactory3DFootprintTargets(after.Results);
        if (!validationPassed)
        {
            Undo.RevertAllDownToGroup(undoGroup);
            Debug.LogError(
                "[Measured3DFootprintApply] Validation failed. All Scale changes were reverted; " +
                "Scene was not saved.");
            return;
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        activeMeasured3DPreview = after;
        SceneView.duringSceneGui -= DrawMeasured3DPreview;
        SceneView.duringSceneGui += DrawMeasured3DPreview;
        SceneView.RepaintAll();
        LogMeasured3DResults("Measured3DFootprintApply", after);
        Debug.Log(
            "[Measured3DFootprintApply] SUCCESS | Modified=SafeVisualRoot.localScale.x,z only | " +
            "Undo=available | Scene saved=NO (review, then press Ctrl+S)");
    }

    private static bool ValidateFactory3DPositionTargets(
        IReadOnlyList<Measured3DResult> results)
    {
        bool valid = true;
        foreach (Measured3DResult result in results)
        {
            Vector2 difference = result.CurrentFootprint.center - result.TargetFootprint.center;
            if (Mathf.Abs(difference.x) > Factory3DPositionTolerance ||
                Mathf.Abs(difference.y) > Factory3DPositionTolerance)
            {
                Debug.LogError(
                    $"[Measured3DPositionApply] Target center mismatch | " +
                    $"Facility={result.ShortLabel} | Current={FormatVector2(result.CurrentFootprint.center)} | " +
                    $"Target={FormatVector2(result.TargetFootprint.center)} | " +
                    $"Difference={FormatVector2(difference)}");
                valid = false;
            }
        }

        return valid;
    }

    private static bool ValidateFactory3DFootprintTargets(
        IReadOnlyList<Measured3DResult> results)
    {
        bool valid = true;
        foreach (Measured3DResult result in results)
        {
            if (!RectApproximately(
                    result.CurrentFootprint,
                    result.TargetFootprint,
                    Factory3DFootprintTolerance))
            {
                Debug.LogError(
                    $"[Measured3DFootprintApply] Footprint mismatch | Facility={result.ShortLabel} | " +
                    $"Current={FormatRect(result.CurrentFootprint)} | " +
                    $"Target={FormatRect(result.TargetFootprint)}");
                valid = false;
            }
        }

        return valid;
    }

    private static List<Factory3DTransformProtectionState> CaptureFactory3DTransformStates(
        IReadOnlyList<Measured3DResult> results,
        bool visualRoots)
    {
        List<Factory3DTransformProtectionState> states =
            new List<Factory3DTransformProtectionState>();
        HashSet<int> instanceIds = new HashSet<int>();
        foreach (Measured3DResult result in results)
        {
            Transform target = visualRoots ? result.SafeVisualRoot : result.PlacementRoot;
            if (target == null || !instanceIds.Add(target.GetInstanceID()))
            {
                continue;
            }

            states.Add(new Factory3DTransformProtectionState
            {
                Target = target,
                Parent = target.parent,
                LocalPosition = target.localPosition,
                LocalRotation = target.localRotation,
                LocalScale = target.localScale,
                Layer = target.gameObject.layer,
                ActiveSelf = target.gameObject.activeSelf
            });
        }

        return states;
    }

    private static UnityEngine.Object[] BuildFactory3DUndoTargets(
        IReadOnlyList<Measured3DResult> results,
        bool visualRoots)
    {
        List<UnityEngine.Object> targets = new List<UnityEngine.Object>();
        HashSet<int> instanceIds = new HashSet<int>();
        foreach (Measured3DResult result in results)
        {
            Transform target = visualRoots ? result.SafeVisualRoot : result.PlacementRoot;
            AddUndoTarget(target, instanceIds, targets);
        }

        return targets.ToArray();
    }

    private static bool ValidateFactory3DTransformProtection(
        IReadOnlyList<Factory3DTransformProtectionState> states,
        bool allowPositionXZ,
        bool allowScaleXZ)
    {
        bool valid = true;
        foreach (Factory3DTransformProtectionState state in states)
        {
            Transform target = state.Target;
            if (target == null || target.parent != state.Parent ||
                target.gameObject.layer != state.Layer ||
                target.gameObject.activeSelf != state.ActiveSelf ||
                !ExactEquals(target.localRotation, state.LocalRotation))
            {
                Debug.LogError(
                    $"[Measured3DApply] Protected hierarchy/rotation/active/layer changed: " +
                    $"{(target != null ? BuildHierarchyPath(target) : "Missing")}");
                valid = false;
                continue;
            }

            Vector3 position = target.localPosition;
            bool positionValid = allowPositionXZ
                ? position.y.Equals(state.LocalPosition.y)
                : ExactEquals(position, state.LocalPosition);
            Vector3 scale = target.localScale;
            bool scaleValid = allowScaleXZ
                ? scale.y.Equals(state.LocalScale.y)
                : ExactEquals(scale, state.LocalScale);
            if (!positionValid || !scaleValid || !IsFinite(position) || !IsFinite(scale))
            {
                Debug.LogError(
                    $"[Measured3DApply] Protected Transform value changed | " +
                    $"Object={BuildHierarchyPath(target)} | " +
                    $"PositionBefore={FormatVector3(state.LocalPosition)} | " +
                    $"PositionAfter={FormatVector3(position)} | " +
                    $"ScaleBefore={FormatVector3(state.LocalScale)} | " +
                    $"ScaleAfter={FormatVector3(scale)}");
                valid = false;
            }
        }

        return valid;
    }

    private static void RecordFactory3DTransformModification(Transform target)
    {
        EditorUtility.SetDirty(target);
        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }

    private static bool BackupContainsFactory3DVisualRoots(
        scr_MapLayoutBackupAsset backup,
        IReadOnlyList<Measured3DResult> results)
    {
        foreach (Measured3DResult result in results)
        {
            string visualPath = BuildHierarchyPath(result.SafeVisualRoot);
            bool found = false;
            foreach (scr_MapLayoutObjectSnapshot snapshot in backup.ObjectSnapshots)
            {
                if (string.Equals(snapshot.HierarchyPath, visualPath, StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                Debug.LogError(
                    $"[Measured3DFootprintApply] Backup has no Visual Root snapshot: {visualPath}");
                return false;
            }
        }

        return true;
    }

    private static bool ValidateFactory3DBackupState(
        scr_MapLayoutBackupAsset backup,
        Scene scene,
        bool allowMeasured3DPositionXZ,
        IReadOnlyList<Measured3DResult> results)
    {
        if (!TryResolveAllSnapshots(backup, scene, out List<ResolvedSnapshot> resolvedSnapshots))
        {
            return false;
        }

        HashSet<string> requiredPlacementPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (Measured3DResult result in results)
        {
            requiredPlacementPaths.Add(BuildHierarchyPath(result.PlacementRoot));
        }

        HashSet<string> foundPlacementPaths = new HashSet<string>(StringComparer.Ordinal);
        bool valid = true;
        foreach (ResolvedSnapshot resolved in resolvedSnapshots)
        {
            scr_MapLayoutObjectSnapshot snapshot = resolved.Snapshot;
            Transform target = resolved.Transform;
            bool measured3DPlacement = requiredPlacementPaths.Contains(snapshot.HierarchyPath);
            if (measured3DPlacement)
            {
                foundPlacementPaths.Add(snapshot.HierarchyPath);
            }

            bool positionMatches = measured3DPlacement && allowMeasured3DPositionXZ
                ? target.localPosition.y.Equals(snapshot.LocalPosition.y)
                : ExactEquals(target.localPosition, snapshot.LocalPosition);
            bool commonMatches = positionMatches &&
                                 ExactEquals(target.localRotation, snapshot.LocalRotation) &&
                                 ExactEquals(target.localScale, snapshot.LocalScale) &&
                                 target.gameObject.activeSelf == snapshot.ActiveSelf &&
                                 target.gameObject.layer == snapshot.Layer &&
                                 string.Equals(
                                     target.parent != null ? BuildHierarchyPath(target.parent) : string.Empty,
                                     snapshot.ParentPath,
                                     StringComparison.Ordinal);
            RectTransform rectTransform = target as RectTransform;
            bool rectMatches = snapshot.IsRectTransform == (rectTransform != null);
            if (snapshot.IsRectTransform && rectTransform != null)
            {
                rectMatches &= ExactEquals(rectTransform.anchoredPosition, snapshot.AnchoredPosition) &&
                               ExactEquals(rectTransform.sizeDelta, snapshot.SizeDelta) &&
                               ExactEquals(rectTransform.anchorMin, snapshot.AnchorMin) &&
                               ExactEquals(rectTransform.anchorMax, snapshot.AnchorMax) &&
                               ExactEquals(rectTransform.pivot, snapshot.Pivot);
            }

            if (!commonMatches || !rectMatches)
            {
                Debug.LogError(
                    $"[Measured3DApply] Backup mismatch | Object={snapshot.HierarchyPath} | " +
                    $"AllowPositionXZ={measured3DPlacement && allowMeasured3DPositionXZ}");
                valid = false;
            }
        }

        if (foundPlacementPaths.Count != requiredPlacementPaths.Count)
        {
            Debug.LogError(
                $"[Measured3DApply] Backup is missing measured Factory 3D Placement Roots | " +
                $"Expected={requiredPlacementPaths.Count} | Found={foundPlacementPaths.Count}");
            valid = false;
        }

        return valid;
    }

    private static bool TryCollectLayoutTargets(Scene scene, out List<LayoutTarget> targets)
    {
        targets = new List<LayoutTarget>();
        HashSet<int> targetInstanceIds = new HashSet<int>();
        bool valid = true;

        Transform factoryView = FindUniqueSceneTransform(scene, "Panel_Main_FactoryView");
        Transform fullMapRoot = FindUniqueDescendant(factoryView, "RealMapLayoutRoot", "Full2D");
        valid &= TryAddNamedTargets(
            scene,
            fullMapRoot,
            "Full2D",
            FullMapFacilityNames,
            targetInstanceIds,
            targets);

        Transform miniMapPanel = FindUniqueSceneTransform(scene, "Panel_Mini2DMap");
        Transform miniMapRoot = FindUniqueDescendant(miniMapPanel, "Image_Mini2DMapArea", "MiniMap");
        valid &= TryAddNamedTargets(
            scene,
            miniMapRoot,
            "MiniMap",
            MiniMapFacilityNames,
            targetInstanceIds,
            targets);

        Transform mapStatusPanel = FindUniqueSceneTransform(scene, "Panel_MapPreview2DContent");
        Transform mapStatusRoot = FindUniqueDescendant(mapStatusPanel, "RealMapLayoutRoot", "MapStatus");
        valid &= TryAddNamedTargets(
            scene,
            mapStatusRoot,
            "MapStatus",
            FullMapFacilityNames,
            targetInstanceIds,
            targets);

        Transform factory3DStage = FindUniqueSceneTransform(scene, "Factory3DStage");
        if (factory3DStage == null)
        {
            valid = false;
        }
        else
        {
            valid &= TryAddTarget(
                scene,
                "Factory3D/Reference",
                factory3DStage,
                targetInstanceIds,
                targets);
            valid &= TryAddNamedTargets(
                scene,
                factory3DStage,
                "Factory3D/Facility",
                Factory3DFacilityNames,
                targetInstanceIds,
                targets);
            valid &= TryAddNamedTargets(
                scene,
                factory3DStage,
                "Factory3D/Reference",
                Factory3DReferenceNames,
                targetInstanceIds,
                targets);
        }

        return valid;
    }

    private static bool TryAddNamedTargets(
        Scene scene,
        Transform searchRoot,
        string groupName,
        IReadOnlyList<string> objectNames,
        ISet<int> targetInstanceIds,
        ICollection<LayoutTarget> targets)
    {
        if (searchRoot == null)
        {
            return false;
        }

        bool valid = true;
        foreach (string objectName in objectNames)
        {
            Transform target = FindUniqueDescendant(searchRoot, objectName, groupName);
            valid &= TryAddTarget(scene, groupName, target, targetInstanceIds, targets);
        }

        return valid;
    }

    private static bool TryAddTarget(
        Scene scene,
        string groupName,
        Transform target,
        ISet<int> targetInstanceIds,
        ICollection<LayoutTarget> targets)
    {
        if (target == null || target.gameObject.scene != scene)
        {
            return false;
        }

        if (!targetInstanceIds.Add(target.GetInstanceID()))
        {
            Debug.LogError($"[MapLayoutBackup] Duplicate target: {BuildHierarchyPath(target)}");
            return false;
        }

        targets.Add(new LayoutTarget(groupName, target));
        return true;
    }

    private static void ApplySnapshot(scr_MapLayoutObjectSnapshot snapshot, Transform target)
    {
        GameObject gameObject = target.gameObject;
        gameObject.layer = snapshot.Layer;

        RectTransform rectTransform = target as RectTransform;
        if (snapshot.IsRectTransform && rectTransform != null)
        {
            rectTransform.anchorMin = snapshot.AnchorMin;
            rectTransform.anchorMax = snapshot.AnchorMax;
            rectTransform.pivot = snapshot.Pivot;
            rectTransform.sizeDelta = snapshot.SizeDelta;
            rectTransform.localRotation = snapshot.LocalRotation;
            rectTransform.localScale = snapshot.LocalScale;
            rectTransform.localPosition = snapshot.LocalPosition;
            rectTransform.anchoredPosition = snapshot.AnchoredPosition;
        }
        else
        {
            target.localPosition = snapshot.LocalPosition;
            target.localRotation = snapshot.LocalRotation;
            target.localScale = snapshot.LocalScale;
        }

        if (gameObject.activeSelf != snapshot.ActiveSelf)
        {
            gameObject.SetActive(snapshot.ActiveSelf);
        }

        EditorUtility.SetDirty(gameObject);
        EditorUtility.SetDirty(target);
        PrefabUtility.RecordPrefabInstancePropertyModifications(gameObject);
        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }

    private static ComparisonSummary CompareLayout(
        scr_MapLayoutBackupAsset backup,
        Scene scene,
        bool logDifferences)
    {
        ComparisonSummary summary = new ComparisonSummary();
        foreach (scr_MapLayoutObjectSnapshot snapshot in backup.ObjectSnapshots)
        {
            summary.ObjectCount++;
            Transform target = ResolveHierarchyPath(scene, snapshot.HierarchyPath);
            if (target == null)
            {
                summary.MissingObjectCount++;
                summary.DifferenceCount++;
                summary.ProtectedDifferenceCount++;
                if (logDifferences)
                {
                    LogDifference(snapshot.HierarchyPath, "Object", "Present", "Missing", "Missing");
                }

                continue;
            }

            RectTransform rectTransform = target as RectTransform;
            bool isMeasuredApplyTarget = IsMeasuredApplySnapshot(snapshot);
            bool measuredRectValueChanged = isMeasuredApplyTarget && rectTransform != null &&
                                            (!ExactEquals(
                                                 snapshot.AnchoredPosition,
                                                 rectTransform.anchoredPosition) ||
                                             !ExactEquals(snapshot.SizeDelta, rectTransform.sizeDelta));

            CompareString(summary, snapshot, "Scene Path", snapshot.ScenePath,
                target.gameObject.scene.path, logDifferences);
            CompareString(summary, snapshot, "Hierarchy Path", snapshot.HierarchyPath,
                BuildHierarchyPath(target), logDifferences);
            CompareString(summary, snapshot, "Parent Path", snapshot.ParentPath,
                target.parent != null ? BuildHierarchyPath(target.parent) : string.Empty, logDifferences);
            CompareBool(summary, snapshot, "activeSelf", snapshot.ActiveSelf,
                target.gameObject.activeSelf, logDifferences);
            CompareInt(summary, snapshot, "layer", snapshot.Layer,
                target.gameObject.layer, logDifferences);
            CompareVector3(summary, snapshot, "localPosition", snapshot.LocalPosition,
                target.localPosition, logDifferences, measuredRectValueChanged);
            CompareQuaternion(summary, snapshot, "localRotation", snapshot.LocalRotation,
                target.localRotation, logDifferences);
            CompareVector3(summary, snapshot, "localScale", snapshot.LocalScale,
                target.localScale, logDifferences);

            bool currentIsRectTransform = rectTransform != null;
            CompareBool(summary, snapshot, "isRectTransform", snapshot.IsRectTransform,
                currentIsRectTransform, logDifferences);
            if (!snapshot.IsRectTransform || rectTransform == null)
            {
                continue;
            }

            CompareVector2(summary, snapshot, "anchoredPosition", snapshot.AnchoredPosition,
                rectTransform.anchoredPosition, logDifferences, isMeasuredApplyTarget);
            CompareVector2(summary, snapshot, "sizeDelta", snapshot.SizeDelta,
                rectTransform.sizeDelta, logDifferences, isMeasuredApplyTarget);
            CompareVector2(summary, snapshot, "anchorMin", snapshot.AnchorMin,
                rectTransform.anchorMin, logDifferences);
            CompareVector2(summary, snapshot, "anchorMax", snapshot.AnchorMax,
                rectTransform.anchorMax, logDifferences);
            CompareVector2(summary, snapshot, "pivot", snapshot.Pivot,
                rectTransform.pivot, logDifferences);
        }

        return summary;
    }

    private static bool IsMeasuredApplySnapshot(scr_MapLayoutObjectSnapshot snapshot)
    {
        if (snapshot == null || !snapshot.IsRectTransform ||
            (!string.Equals(snapshot.GroupName, "Full2D", StringComparison.Ordinal) &&
             !string.Equals(snapshot.GroupName, "MiniMap", StringComparison.Ordinal) &&
             !string.Equals(snapshot.GroupName, "MapStatus", StringComparison.Ordinal)))
        {
            return false;
        }

        string objectName = snapshot.HierarchyPath;
        int separatorIndex = objectName.LastIndexOf('/');
        if (separatorIndex >= 0)
        {
            objectName = objectName.Substring(separatorIndex + 1);
        }

        return !string.Equals(objectName, "D_EntryZone", StringComparison.Ordinal) &&
               !string.Equals(objectName, "Image_Mini2DMapEntry", StringComparison.Ordinal);
    }

    private static bool TryResolveAllSnapshots(
        scr_MapLayoutBackupAsset backup,
        Scene scene,
        out List<ResolvedSnapshot> resolvedSnapshots)
    {
        resolvedSnapshots = new List<ResolvedSnapshot>(backup.ObjectSnapshots.Count);
        HashSet<int> resolvedInstanceIds = new HashSet<int>();
        bool valid = true;

        foreach (scr_MapLayoutObjectSnapshot snapshot in backup.ObjectSnapshots)
        {
            if (!string.Equals(snapshot.ScenePath, scene.path, StringComparison.Ordinal))
            {
                Debug.LogError(
                    $"[MapLayoutRestore] Snapshot Scene mismatch: {snapshot.HierarchyPath} | " +
                    $"Backup={snapshot.ScenePath} | Current={scene.path}");
                valid = false;
                continue;
            }

            Transform target = ResolveHierarchyPath(scene, snapshot.HierarchyPath);
            if (target == null)
            {
                Debug.LogError($"[MapLayoutRestore] Missing object: {snapshot.HierarchyPath}");
                valid = false;
                continue;
            }

            string currentParentPath = target.parent != null
                ? BuildHierarchyPath(target.parent)
                : string.Empty;
            if (!string.Equals(snapshot.ParentPath, currentParentPath, StringComparison.Ordinal))
            {
                Debug.LogError(
                    $"[MapLayoutRestore] Parent mismatch: {snapshot.HierarchyPath} | " +
                    $"Backup={snapshot.ParentPath} | Current={currentParentPath}");
                valid = false;
                continue;
            }

            if (snapshot.IsRectTransform != (target is RectTransform))
            {
                Debug.LogError($"[MapLayoutRestore] Transform type mismatch: {snapshot.HierarchyPath}");
                valid = false;
                continue;
            }

            if (!resolvedInstanceIds.Add(target.GetInstanceID()))
            {
                Debug.LogError($"[MapLayoutRestore] Duplicate resolved object: {snapshot.HierarchyPath}");
                valid = false;
                continue;
            }

            resolvedSnapshots.Add(new ResolvedSnapshot(snapshot, target));
        }

        return valid && resolvedSnapshots.Count == backup.ObjectSnapshots.Count;
    }

    private static bool ValidateBackupHeader(
        scr_MapLayoutBackupAsset backup,
        Scene scene,
        string backupPath)
    {
        if (backup == null || backup.ObjectSnapshots == null || backup.ObjectSnapshots.Count == 0)
        {
            Debug.LogError($"[MapLayout] Backup is empty or invalid: {backupPath}");
            return false;
        }

        if (!string.Equals(backup.ScenePath, scene.path, StringComparison.Ordinal))
        {
            Debug.LogError(
                $"[MapLayout] Open the backup Scene before restoring or comparing. " +
                $"Backup={backup.ScenePath} | Current={scene.path}");
            return false;
        }

        return true;
    }

    private static bool TryResolveBackupAsset(
        out scr_MapLayoutBackupAsset backup,
        out string assetPath)
    {
        backup = Selection.activeObject as scr_MapLayoutBackupAsset;
        assetPath = backup != null ? AssetDatabase.GetAssetPath(backup) : string.Empty;
        if (backup != null && IsBackupFolderAsset(assetPath))
        {
            Debug.Log($"[MapLayout] Using selected backup: {assetPath}");
            return true;
        }

        if (!TryFindLatestBackupAsset(out backup, out assetPath))
        {
            return false;
        }

        Debug.Log($"[MapLayout] Using latest backup: {assetPath}");
        return true;
    }

    private static bool TryResolveLatestBackupAsset(
        out scr_MapLayoutBackupAsset backup,
        out string assetPath)
    {
        if (!TryFindLatestBackupAsset(out backup, out assetPath))
        {
            return false;
        }

        Debug.Log($"[MapLayout] Using latest backup for measured Apply: {assetPath}");
        return true;
    }

    private static bool TryFindLatestBackupAsset(
        out scr_MapLayoutBackupAsset backup,
        out string assetPath)
    {
        backup = null;
        assetPath = string.Empty;
        string[] guids = AssetDatabase.FindAssets(
            $"t:{nameof(scr_MapLayoutBackupAsset)}",
            new[] { BackupFolderPath });
        long latestTicks = long.MinValue;
        foreach (string guid in guids)
        {
            string candidatePath = AssetDatabase.GUIDToAssetPath(guid);
            scr_MapLayoutBackupAsset candidate =
                AssetDatabase.LoadAssetAtPath<scr_MapLayoutBackupAsset>(candidatePath);
            if (candidate == null || candidate.CreatedUtcTicks < latestTicks)
            {
                continue;
            }

            if (candidate.CreatedUtcTicks == latestTicks &&
                string.CompareOrdinal(candidatePath, assetPath) <= 0)
            {
                continue;
            }

            backup = candidate;
            assetPath = candidatePath;
            latestTicks = candidate.CreatedUtcTicks;
        }

        if (backup == null)
        {
            Debug.LogError(
                $"[MapLayout] No backup found in {BackupFolderPath}. Run Backup Current Layout first.");
            return false;
        }

        return true;
    }

    private static bool IsBackupFolderAsset(string assetPath)
    {
        return !string.IsNullOrEmpty(assetPath) &&
               assetPath.StartsWith(BackupFolderPath + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetEditableActiveScene(out Scene scene)
    {
        scene = default;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[MapLayout] Exit Play Mode before backing up or restoring a layout.");
            return false;
        }

        return TryGetLoadedActiveScene(out scene);
    }

    private static bool TryGetLoadedActiveScene(out Scene scene)
    {
        scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path))
        {
            Debug.LogError("[MapLayout] Open and save the target Scene before using this tool.");
            return false;
        }

        return true;
    }

    private static Transform FindUniqueSceneTransform(Scene scene, string objectName)
    {
        Transform result = null;
        Transform[] transforms = UnityEngine.Object.FindObjectsByType<Transform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (Transform candidate in transforms)
        {
            if (candidate == null || candidate.gameObject.scene != scene ||
                !string.Equals(candidate.name, objectName, StringComparison.Ordinal))
            {
                continue;
            }

            if (result != null)
            {
                Debug.LogError($"[MapLayout] Duplicate Scene object name: {objectName}");
                return null;
            }

            result = candidate;
        }

        if (result == null)
        {
            Debug.LogError($"[MapLayout] Scene object not found: {objectName}");
        }

        return result;
    }

    private static Transform FindUniqueDescendant(
        Transform root,
        string objectName,
        string context)
    {
        if (root == null)
        {
            Debug.LogError($"[MapLayout] Search root is missing: {context}/{objectName}");
            return null;
        }

        Transform result = null;
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(candidate.name, objectName, StringComparison.Ordinal))
            {
                continue;
            }

            if (result != null)
            {
                Debug.LogError(
                    $"[MapLayout] Duplicate object under {BuildHierarchyPath(root)}: {objectName}");
                return null;
            }

            result = candidate;
        }

        if (result == null)
        {
            Debug.LogError($"[MapLayout] Object not found: {context}/{objectName}");
        }

        return result;
    }

    private static string BuildHierarchyPath(Transform transform)
    {
        if (transform == null)
        {
            return string.Empty;
        }

        Stack<string> names = new Stack<string>();
        Transform current = transform;
        while (current != null)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names);
    }

    private static Transform ResolveHierarchyPath(Scene scene, string hierarchyPath)
    {
        if (string.IsNullOrEmpty(hierarchyPath))
        {
            return null;
        }

        string[] segments = hierarchyPath.Split('/');
        Transform current = null;
        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            if (!string.Equals(rootObject.name, segments[0], StringComparison.Ordinal))
            {
                continue;
            }

            if (current != null)
            {
                return null;
            }

            current = rootObject.transform;
        }

        if (current == null)
        {
            return null;
        }

        for (int segmentIndex = 1; segmentIndex < segments.Length; segmentIndex++)
        {
            Transform next = null;
            for (int childIndex = 0; childIndex < current.childCount; childIndex++)
            {
                Transform child = current.GetChild(childIndex);
                if (!string.Equals(child.name, segments[segmentIndex], StringComparison.Ordinal))
                {
                    continue;
                }

                if (next != null)
                {
                    return null;
                }

                next = child;
            }

            if (next == null)
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private static void EnsureAssetFolder(string folderPath)
    {
        string[] segments = folderPath.Split('/');
        string currentPath = segments[0];
        for (int index = 1; index < segments.Length; index++)
        {
            string nextPath = currentPath + "/" + segments[index];
            if (!AssetDatabase.IsValidFolder(nextPath))
            {
                AssetDatabase.CreateFolder(currentPath, segments[index]);
            }

            currentPath = nextPath;
        }
    }

    private static void AddUndoTarget(
        UnityEngine.Object target,
        ISet<int> registeredInstanceIds,
        ICollection<UnityEngine.Object> undoTargets)
    {
        if (target != null && registeredInstanceIds.Add(target.GetInstanceID()))
        {
            undoTargets.Add(target);
        }
    }

    private static void CompareString(
        ComparisonSummary summary,
        scr_MapLayoutObjectSnapshot snapshot,
        string property,
        string backupValue,
        string currentValue,
        bool logDifferences)
    {
        RecordComparison(
            summary,
            snapshot,
            property,
            backupValue ?? string.Empty,
            currentValue ?? string.Empty,
            string.Equals(backupValue, currentValue, StringComparison.Ordinal),
            "Changed",
            logDifferences);
    }

    private static void CompareBool(
        ComparisonSummary summary,
        scr_MapLayoutObjectSnapshot snapshot,
        string property,
        bool backupValue,
        bool currentValue,
        bool logDifferences)
    {
        RecordComparison(
            summary,
            snapshot,
            property,
            backupValue.ToString(),
            currentValue.ToString(),
            backupValue == currentValue,
            backupValue == currentValue ? "0" : "Changed",
            logDifferences);
    }

    private static void CompareInt(
        ComparisonSummary summary,
        scr_MapLayoutObjectSnapshot snapshot,
        string property,
        int backupValue,
        int currentValue,
        bool logDifferences)
    {
        RecordComparison(
            summary,
            snapshot,
            property,
            backupValue.ToString(CultureInfo.InvariantCulture),
            currentValue.ToString(CultureInfo.InvariantCulture),
            backupValue == currentValue,
            (currentValue - backupValue).ToString(CultureInfo.InvariantCulture),
            logDifferences);
    }

    private static void CompareVector2(
        ComparisonSummary summary,
        scr_MapLayoutObjectSnapshot snapshot,
        string property,
        Vector2 backupValue,
        Vector2 currentValue,
        bool logDifferences,
        bool allowMeasuredDifference = false)
    {
        RecordComparison(
            summary,
            snapshot,
            property,
            FormatVector2(backupValue),
            FormatVector2(currentValue),
            ExactEquals(backupValue, currentValue),
            FormatVector2(currentValue - backupValue),
            logDifferences,
            allowMeasuredDifference);
    }

    private static void CompareVector3(
        ComparisonSummary summary,
        scr_MapLayoutObjectSnapshot snapshot,
        string property,
        Vector3 backupValue,
        Vector3 currentValue,
        bool logDifferences,
        bool allowMeasuredDifference = false)
    {
        RecordComparison(
            summary,
            snapshot,
            property,
            FormatVector3(backupValue),
            FormatVector3(currentValue),
            ExactEquals(backupValue, currentValue),
            FormatVector3(currentValue - backupValue),
            logDifferences,
            allowMeasuredDifference);
    }

    private static void CompareQuaternion(
        ComparisonSummary summary,
        scr_MapLayoutObjectSnapshot snapshot,
        string property,
        Quaternion backupValue,
        Quaternion currentValue,
        bool logDifferences)
    {
        Quaternion difference = new Quaternion(
            currentValue.x - backupValue.x,
            currentValue.y - backupValue.y,
            currentValue.z - backupValue.z,
            currentValue.w - backupValue.w);
        RecordComparison(
            summary,
            snapshot,
            property,
            FormatQuaternion(backupValue),
            FormatQuaternion(currentValue),
            ExactEquals(backupValue, currentValue),
            FormatQuaternion(difference),
            logDifferences);
    }

    private static void RecordComparison(
        ComparisonSummary summary,
        scr_MapLayoutObjectSnapshot snapshot,
        string property,
        string backupValue,
        string currentValue,
        bool isMatch,
        string difference,
        bool logDifferences,
        bool allowMeasuredDifference = false)
    {
        summary.PropertyCount++;
        if (isMatch)
        {
            summary.MatchingPropertyCount++;
            return;
        }

        summary.DifferenceCount++;
        if (allowMeasuredDifference)
        {
            summary.AllowedMeasuredDifferenceCount++;
        }
        else
        {
            summary.ProtectedDifferenceCount++;
        }

        if (logDifferences)
        {
            LogDifference(
                snapshot.HierarchyPath,
                property,
                backupValue,
                currentValue,
                difference,
                allowMeasuredDifference);
        }
    }

    private static void LogDifference(
        string hierarchyPath,
        string property,
        string backupValue,
        string currentValue,
        string difference,
        bool allowMeasuredDifference = false)
    {
        Debug.LogWarning(
            $"[MapLayoutCompare] {(allowMeasuredDifference ? "ALLOWED_MEASURED" : "PROTECTED")} | " +
            $"{hierarchyPath} | {property} | {backupValue} | {currentValue} | {difference}");
    }

    private static bool ExactEquals(Vector2 left, Vector2 right)
    {
        return left.x.Equals(right.x) && left.y.Equals(right.y);
    }

    private static bool ExactEquals(Vector3 left, Vector3 right)
    {
        return left.x.Equals(right.x) && left.y.Equals(right.y) && left.z.Equals(right.z);
    }

    private static bool ExactEquals(Quaternion left, Quaternion right)
    {
        return left.x.Equals(right.x) && left.y.Equals(right.y) &&
               left.z.Equals(right.z) && left.w.Equals(right.w);
    }

    private static string FormatVector2(Vector2 value)
    {
        return $"({FormatFloat(value.x)},{FormatFloat(value.y)})";
    }

    private static string FormatVector3(Vector3 value)
    {
        return $"({FormatFloat(value.x)},{FormatFloat(value.y)},{FormatFloat(value.z)})";
    }

    private static string FormatColor(Color value)
    {
        return $"({FormatFloat(value.r)},{FormatFloat(value.g)}," +
               $"{FormatFloat(value.b)},{FormatFloat(value.a)})";
    }

    private static string FormatQuaternion(Quaternion value)
    {
        return $"({FormatFloat(value.x)},{FormatFloat(value.y)},{FormatFloat(value.z)},{FormatFloat(value.w)})";
    }

    private static string FormatRect(Rect value)
    {
        return $"(xMin={FormatFloat(value.xMin)},yMin={FormatFloat(value.yMin)}," +
               $"xMax={FormatFloat(value.xMax)},yMax={FormatFloat(value.yMax)})";
    }

    private static bool IsFinite(Rect value)
    {
        return IsFinite(value.xMin) && IsFinite(value.yMin) &&
               IsFinite(value.xMax) && IsFinite(value.yMax);
    }

    private static bool IsFinite(Vector2 value)
    {
        return IsFinite(value.x) && IsFinite(value.y);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
#endif

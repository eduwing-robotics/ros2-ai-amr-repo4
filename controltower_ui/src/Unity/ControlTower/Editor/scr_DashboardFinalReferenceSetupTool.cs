#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class scr_DashboardFinalReferenceSetupTool
{
    private const string MenuPath = "Tools/ControlTower/Dashboard/Apply Final Dashboard References";
    private const string LatestCarriageAssetPath =
        "Assets/Project/Models/TB3_Forklift_RackPinion/Carriage_0715.fbx";

    private readonly struct ReferenceBinding
    {
        public ReferenceBinding(string propertyName, string objectName)
        {
            PropertyName = propertyName;
            ObjectName = objectName;
        }

        public string PropertyName { get; }
        public string ObjectName { get; }
    }

    private static readonly ReferenceBinding[] PeoplePillBindings =
    {
        new ReferenceBinding("pillDashboardAttendanceInStatus", "Pill_DashboardAttendanceInStatus"),
        new ReferenceBinding("pillDashboardAttendanceOutStatus", "Pill_DashboardAttendanceOutStatus"),
        new ReferenceBinding("pillDashboardVisitorTodayStatus", "Pill_DashboardVisitorTodayStatus")
    };

    private static readonly ReferenceBinding[] MapPillBindings =
    {
        new ReferenceBinding("pillDashboardSlam", "Pill_DashboardSLAM"),
        new ReferenceBinding("pillDashboardNav2", "Pill_DashboardNav2")
    };

    private static readonly ReferenceBinding[] SystemPillBindings =
    {
        new ReferenceBinding("pillDashboardServerStatus", "Pill_DashboardServerStatus"),
        new ReferenceBinding("pillDashboardWebSocketStatus", "Pill_DashboardWebSocketStatus"),
        new ReferenceBinding("pillDashboardRos2Status", "Pill_DashboardROS2Status"),
        new ReferenceBinding("pillDashboardAiStatus", "Pill_DashboardAIStatus"),
        new ReferenceBinding("pillDashboardDb", "Pill_DashboardDB")
    };

    private static readonly string[] RequiredBinderReferences =
    {
        "textDashboardMapNavProgressPercent",
        "imageDashboardMapNavProgressFill",
        "dotDashboardGlobalCctv",
        "dotDashboardTb3Camera01",
        "dotDashboardTb3Camera02",
        "dotDashboardAiModel"
    };

    private static readonly string[] RequiredLogFilterReferences =
    {
        "buttonDashboardLogAll",
        "buttonDashboardLogRobot",
        "buttonDashboardLogControl",
        "buttonDashboardLogCamera",
        "buttonDashboardLogSystem",
        "buttonDashboardLogError"
    };

    [MenuItem(MenuPath)]
    public static void ApplyFinalDashboardReferences()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[DashboardFinalReferenceSetup] Exit Play Mode before applying references.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[DashboardFinalReferenceSetup] No loaded active Scene.");
            return;
        }

        Transform dashboardRoot = FindUniqueSceneTransform(scene, "Panel_Main_DashboardView");
        scr_ControlTowerUIManager uiManager = FindUniqueSceneComponent<scr_ControlTowerUIManager>(scene);
        scr_ControlTowerDashboardRuntimeBinder binder = ResolveDashboardBinder(uiManager);
        if (dashboardRoot == null || uiManager == null || binder == null)
        {
            Debug.LogError("[DashboardFinalReferenceSetup] Required Dashboard root, UIManager, or linked RuntimeBinder was not found.");
            return;
        }

        SerializedObject binderSerialized = new SerializedObject(binder);
        SerializedObject uiManagerSerialized = new SerializedObject(uiManager);
        binderSerialized.Update();
        uiManagerSerialized.Update();

        Undo.RecordObject(binder, "Apply Final Dashboard References");
        bool changed = false;
        bool peopleOk = ApplyBindings(binderSerialized, dashboardRoot, PeoplePillBindings, ref changed);
        bool mapOk = ApplyBindings(binderSerialized, dashboardRoot, MapPillBindings, ref changed);
        bool systemOk = ApplyBindings(binderSerialized, dashboardRoot, SystemPillBindings, ref changed);
        bool previewOk = ApplyAndValidateRobotPreviewRoots(binderSerialized, scene, ref changed);
        bool forkliftRepairOk = RepairTb3ForkliftPreviewBranches(scene, ref changed);

        binderSerialized.ApplyModifiedProperties();
        if (changed)
        {
            EditorUtility.SetDirty(binder);
            PrefabUtility.RecordPrefabInstancePropertyModifications(binder);
        }

        bool binderReferencesOk = ValidateObjectReferences(binderSerialized, RequiredBinderReferences, "Dashboard RuntimeBinder");
        bool slotsOk = ValidateRobotSlots(binderSerialized);
        bool filtersOk = ValidateObjectReferences(uiManagerSerialized, RequiredLogFilterReferences, "Dashboard log filters");
        bool forkliftOk = ValidateTb3ForkliftPreviews(scene, binderSerialized);
        forkliftOk &= forkliftRepairOk;
        bool factoryForkliftOk = ValidateFactoryForkliftControllerIsolation(scene);
        bool distinctPills = ValidateDistinctBoundObjects(binderSerialized);

        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                Debug.LogError("[DashboardFinalReferenceSetup] Scene save failed.");
                return;
            }
        }

        Debug.Log(peopleOk ?
            "[DashboardFinalReferenceSetup] People Pill references OK" :
            "[DashboardFinalReferenceSetup] People Pill references FAILED");
        Debug.Log(mapOk ?
            "[DashboardFinalReferenceSetup] Map Pill references OK" :
            "[DashboardFinalReferenceSetup] Map Pill references FAILED");
        Debug.Log(systemOk ?
            "[DashboardFinalReferenceSetup] System Pill references OK" :
            "[DashboardFinalReferenceSetup] System Pill references FAILED");
        Debug.Log(slotsOk && previewOk ?
            "[DashboardFinalReferenceSetup] Robot slots OK" :
            "[DashboardFinalReferenceSetup] Robot slots FAILED");
        Debug.Log(forkliftOk ?
            "[DashboardFinalReferenceSetup] Dashboard/Robot TB3-03 Carriage, Fork, and Mast renderers OK" :
            "[DashboardFinalReferenceSetup] Dashboard/Robot TB3-03 Carriage, Fork, or Mast validation FAILED");
        Debug.Log(factoryForkliftOk ?
            "[DashboardFinalReferenceSetup] Factory forklift controller references are isolated from Preview models" :
            "[DashboardFinalReferenceSetup] Factory forklift controller reference isolation FAILED");

        bool allOk = peopleOk && mapOk && systemOk && previewOk && binderReferencesOk &&
                     slotsOk && filtersOk && forkliftOk && factoryForkliftOk && distinctPills;
        Debug.Log(allOk ?
            "[DashboardFinalReferenceSetup] Reference validation OK" :
            "[DashboardFinalReferenceSetup] Reference validation FAILED");
        Debug.Log("[DashboardFinalReferenceSetup] Completed");
    }

    private static bool ApplyBindings(
        SerializedObject serializedObject,
        Transform dashboardRoot,
        IReadOnlyList<ReferenceBinding> bindings,
        ref bool changed)
    {
        bool valid = true;
        foreach (ReferenceBinding binding in bindings)
        {
            SerializedProperty property = serializedObject.FindProperty(binding.PropertyName);
            Transform target = FindUniqueDescendant(dashboardRoot, binding.ObjectName);
            if (property == null || target == null || !target.IsChildOf(dashboardRoot))
            {
                Debug.LogError($"[DashboardFinalReferenceSetup] Cannot bind {binding.PropertyName} -> {binding.ObjectName}.");
                valid = false;
                continue;
            }

            if (property.objectReferenceValue != target.gameObject)
            {
                property.objectReferenceValue = target.gameObject;
                changed = true;
            }
        }

        return valid;
    }

    private static bool ApplyAndValidateRobotPreviewRoots(
        SerializedObject binderSerialized,
        Scene scene,
        ref bool changed)
    {
        SerializedProperty slots = binderSerialized.FindProperty("dashboardRobotSlots");
        if (slots == null || !slots.isArray || slots.arraySize < 3)
        {
            Debug.LogError("[DashboardFinalReferenceSetup] Dashboard robot slot array is missing or incomplete.");
            return false;
        }

        bool valid = true;
        for (int index = 0; index < 3; index++)
        {
            int robotNumber = index + 1;
            Transform previewWrapper = FindUniqueSceneTransform(scene, $"DashboardPreview_TB3_{robotNumber:00}");
            Transform modelRoot = ResolveWholePreviewModelRoot(previewWrapper, robotNumber);
            SerializedProperty slot = slots.GetArrayElementAtIndex(index);
            SerializedProperty previewProperty = slot.FindPropertyRelative("previewModelRoot");
            if (previewWrapper == null || modelRoot == null || previewProperty == null)
            {
                Debug.LogError($"[DashboardFinalReferenceSetup] TB3-{robotNumber:00} complete preview model root was not found.");
                valid = false;
                continue;
            }

            if (modelRoot.GetComponentInChildren<Camera>(true) != null ||
                modelRoot.GetComponentsInChildren<Renderer>(true).Length == 0)
            {
                Debug.LogError($"[DashboardFinalReferenceSetup] TB3-{robotNumber:00} preview target is not a camera-free complete model root.");
                valid = false;
                continue;
            }

            if (previewProperty.objectReferenceValue != modelRoot)
            {
                previewProperty.objectReferenceValue = modelRoot;
                changed = true;
            }
        }

        return valid;
    }

    private static Transform ResolveWholePreviewModelRoot(Transform previewWrapper, int robotNumber)
    {
        if (previewWrapper == null)
        {
            return null;
        }

        Transform previewModel = FindUniqueDescendant(previewWrapper, $"Preview_TB3_{robotNumber:00}_Model");
        Transform searchRoot = previewModel != null ? previewModel : previewWrapper;
        Transform modelRoot = FindUniqueDescendant(searchRoot, "ModelRoot");
        return modelRoot != null ? modelRoot : searchRoot;
    }

    private static bool ValidateRobotSlots(SerializedObject binderSerialized)
    {
        SerializedProperty slots = binderSerialized.FindProperty("dashboardRobotSlots");
        if (slots == null || !slots.isArray || slots.arraySize < 3)
        {
            return false;
        }

        string[] required =
        {
            "textRobotId",
            "textBatteryPercent",
            "dotSelected",
            "dotUnselected",
            "iconBatteryUnknown",
            "iconBatteryCharging",
            "iconBatteryFull",
            "iconBatteryMedium",
            "iconBatteryLow",
            "iconBatteryEmpty",
            "rawImagePreview",
            "previewModelRoot"
        };

        bool valid = true;
        for (int i = 0; i < 3; i++)
        {
            SerializedProperty slot = slots.GetArrayElementAtIndex(i);
            foreach (string relativeName in required)
            {
                SerializedProperty property = slot.FindPropertyRelative(relativeName);
                if (property == null || property.objectReferenceValue == null)
                {
                    Debug.LogError($"[DashboardFinalReferenceSetup] Robot slot {i + 1} missing {relativeName}.");
                    valid = false;
                }
            }
        }

        return valid;
    }

    private static bool RepairTb3ForkliftPreviewBranches(Scene scene, ref bool changed)
    {
        bool dashboardOk = RepairTb3ForkliftPreviewBranch(scene, "DashboardPreview_TB3_03", ref changed);
        bool robotOk = RepairTb3ForkliftPreviewBranch(scene, "Preview_TB3_03", ref changed);
        return dashboardOk && robotOk;
    }

    private static bool RepairTb3ForkliftPreviewBranch(
        Scene scene,
        string wrapperName,
        ref bool changed)
    {
        Transform wrapper = FindUniqueSceneTransform(scene, wrapperName);
        Transform modelRoot = ResolveWholePreviewModelRoot(wrapper, 3);
        Transform carriageLift = FindUniqueDescendant(modelRoot, "Carriage_Lift");
        if (wrapper == null || modelRoot == null || carriageLift == null)
        {
            Debug.LogError(
                $"[DashboardFinalReferenceSetup] TB3-03 Carriage hierarchy is missing under {wrapperName}.");
            return false;
        }

        int expectedLayer = modelRoot.gameObject.layer;
        foreach (Transform child in carriageLift.GetComponentsInChildren<Transform>(true))
        {
            if (child == null)
            {
                continue;
            }

            if (child.gameObject.layer != expectedLayer)
            {
                Undo.RecordObject(child.gameObject, "Fix TB3-03 Preview Carriage Layer");
                child.gameObject.layer = expectedLayer;
                EditorUtility.SetDirty(child.gameObject);
                PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
                changed = true;
            }

            if (!child.gameObject.activeSelf)
            {
                Undo.RecordObject(child.gameObject, "Enable TB3-03 Preview Carriage");
                child.gameObject.SetActive(true);
                EditorUtility.SetDirty(child.gameObject);
                PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
                changed = true;
            }
        }

        Renderer[] renderers = carriageLift.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogError(
                $"[DashboardFinalReferenceSetup] TB3-03 Carriage renderer missing under: {GetHierarchyPath(carriageLift)}");
            return false;
        }

        foreach (Renderer renderer in renderers)
        {
            if (renderer != null && !renderer.enabled)
            {
                Undo.RecordObject(renderer, "Enable TB3-03 Preview Carriage Renderer");
                renderer.enabled = true;
                EditorUtility.SetDirty(renderer);
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                changed = true;
            }
        }

        return true;
    }

    private static bool ValidateTb3ForkliftPreviews(Scene scene, SerializedObject binderSerialized)
    {
        bool dashboardOk = ValidateTb3ForkliftPreview(
            scene,
            "DashboardPreview_TB3_03",
            "DashboardPreviewCamera_03",
            binderSerialized,
            true);
        bool robotOk = ValidateTb3ForkliftPreview(
            scene,
            "Preview_TB3_03",
            "PreviewCamera",
            null,
            false);
        return dashboardOk && robotOk;
    }

    private static bool ValidateTb3ForkliftPreview(
        Scene scene,
        string wrapperName,
        string cameraName,
        SerializedObject binderSerialized,
        bool validateDashboardBinding)
    {
        Transform wrapper = FindUniqueSceneTransform(scene, wrapperName);
        Transform modelRoot = ResolveWholePreviewModelRoot(wrapper, 3);
        Transform forkliftRoot = FindUniqueDescendant(modelRoot, "TB3_Forklift_RackPinion_Final");
        Transform carriageLift = FindUniqueDescendant(forkliftRoot, "Carriage_Lift");
        Transform mastAssembly = FindUniqueDescendant(forkliftRoot, "Mast_Assembly");
        Camera previewCamera = FindUniqueSceneTransform(scene, cameraName)?.GetComponent<Camera>();
        if (wrapper == null || modelRoot == null || forkliftRoot == null || carriageLift == null ||
            mastAssembly == null || previewCamera == null)
        {
            Debug.LogError(
                $"[DashboardFinalReferenceSetup] TB3-03 hierarchy/camera missing for {wrapperName}. " +
                $"ModelRoot={GetHierarchyPath(modelRoot)}, Carriage={GetHierarchyPath(carriageLift)}, " +
                $"Mast={GetHierarchyPath(mastAssembly)}, Camera={GetHierarchyPath(previewCamera?.transform)}");
            return false;
        }

        bool valid = true;
        int expectedLayer = modelRoot.gameObject.layer;
        if (modelRoot.GetComponentInChildren<Camera>(true) != null)
        {
            Debug.LogError(
                $"[DashboardFinalReferenceSetup] Preview Camera must not be under rotating ModelRoot: {GetHierarchyPath(modelRoot)}");
            valid = false;
        }

        Renderer[] latestCarriageRenderers = FindLatestCarriageRenderers(carriageLift);
        if (latestCarriageRenderers.Length == 0)
        {
            Debug.LogError(
                $"[DashboardFinalReferenceSetup] Latest Carriage_0715 renderer missing under: {GetHierarchyPath(carriageLift)}");
            valid = false;
        }
        else
        {
            valid &= ValidateRendererGroup(
                "Carriage",
                carriageLift,
                latestCarriageRenderers,
                expectedLayer,
                previewCamera);

            // Carriage_0715 is one combined mesh containing both the carriage and fork geometry.
            valid &= ValidateRendererGroup(
                "Fork (integrated Carriage_0715 mesh)",
                carriageLift,
                latestCarriageRenderers,
                expectedLayer,
                previewCamera);
        }

        Renderer[] mastRenderers = mastAssembly.GetComponentsInChildren<Renderer>(true);
        valid &= ValidateRendererGroup("Mast", mastAssembly, mastRenderers, expectedLayer, previewCamera);

        foreach (Renderer renderer in forkliftRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled || HasMissingRenderAsset(renderer))
            {
                Debug.LogError(
                    $"[DashboardFinalReferenceSetup] Invalid TB3-03 renderer: {GetHierarchyPath(renderer?.transform)}");
                valid = false;
            }
        }

        if (validateDashboardBinding && !ValidateDashboardTb3PreviewBinding(binderSerialized, modelRoot))
        {
            valid = false;
        }

        if (valid)
        {
            Debug.Log(
                $"[DashboardFinalReferenceSetup] TB3-03 preview branch OK: {GetHierarchyPath(modelRoot)}");
        }

        return valid;
    }

    private static Renderer[] FindLatestCarriageRenderers(Transform carriageLift)
    {
        List<Renderer> matches = new List<Renderer>();
        if (carriageLift == null)
        {
            return matches.ToArray();
        }

        foreach (Renderer renderer in carriageLift.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
            {
                continue;
            }

            Mesh mesh = GetSharedMesh(renderer);
            string meshAssetPath = mesh != null ? AssetDatabase.GetAssetPath(mesh) : string.Empty;
            string instanceAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(renderer.gameObject);
            if (string.Equals(meshAssetPath, LatestCarriageAssetPath, StringComparison.Ordinal) ||
                string.Equals(instanceAssetPath, LatestCarriageAssetPath, StringComparison.Ordinal))
            {
                matches.Add(renderer);
            }
        }

        return matches.ToArray();
    }

    private static bool ValidateRendererGroup(
        string role,
        Transform expectedRoot,
        IReadOnlyList<Renderer> renderers,
        int expectedLayer,
        Camera previewCamera)
    {
        if (renderers == null || renderers.Count == 0)
        {
            Debug.LogError(
                $"[DashboardFinalReferenceSetup] TB3-03 {role} renderer missing under: {GetHierarchyPath(expectedRoot)}");
            return false;
        }

        bool valid = true;
        foreach (Renderer renderer in renderers)
        {
            string path = GetHierarchyPath(renderer?.transform);
            if (renderer == null || !renderer.enabled || HasMissingRenderAsset(renderer))
            {
                Debug.LogError(
                    $"[DashboardFinalReferenceSetup] TB3-03 {role} renderer/mesh/material invalid: {path}");
                valid = false;
                continue;
            }

            if (!renderer.gameObject.activeSelf)
            {
                Debug.LogError($"[DashboardFinalReferenceSetup] TB3-03 {role} inactive: {path}");
                valid = false;
            }

            if (renderer.gameObject.layer != expectedLayer)
            {
                Debug.LogError(
                    $"[DashboardFinalReferenceSetup] TB3-03 {role} layer mismatch: " +
                    $"{path} / current={renderer.gameObject.layer} / expected={expectedLayer}");
                valid = false;
            }

            if ((previewCamera.cullingMask & (1 << renderer.gameObject.layer)) == 0)
            {
                Debug.LogError(
                    $"[DashboardFinalReferenceSetup] TB3-03 {role} excluded by Preview Camera culling mask: " +
                    $"{path} / layer={renderer.gameObject.layer} / mask={previewCamera.cullingMask}");
                valid = false;
            }
        }

        return valid;
    }

    private static bool ValidateDashboardTb3PreviewBinding(
        SerializedObject binderSerialized,
        Transform expectedModelRoot)
    {
        SerializedProperty slots = binderSerialized?.FindProperty("dashboardRobotSlots");
        if (slots == null || !slots.isArray || slots.arraySize < 3)
        {
            Debug.LogError("[DashboardFinalReferenceSetup] Dashboard TB3-03 preview slot is missing.");
            return false;
        }

        SerializedProperty previewProperty = slots.GetArrayElementAtIndex(2).FindPropertyRelative("previewModelRoot");
        Transform actual = previewProperty?.objectReferenceValue as Transform;
        if (actual == expectedModelRoot)
        {
            return true;
        }

        Debug.LogError(
            $"[DashboardFinalReferenceSetup] Dashboard TB3-03 previewModelRoot mismatch: " +
            $"actual={GetHierarchyPath(actual)} / expected={GetHierarchyPath(expectedModelRoot)}");
        return false;
    }

    private static bool ValidateFactoryForkliftControllerIsolation(Scene scene)
    {
        scr_TB3ForkliftRuntimeController controller =
            FindUniqueSceneComponent<scr_TB3ForkliftRuntimeController>(scene);
        if (controller == null)
        {
            Debug.LogError("[DashboardFinalReferenceSetup] Factory forklift runtime controller is missing.");
            return false;
        }

        SerializedObject serialized = new SerializedObject(controller);
        Transform carriageTarget = serialized.FindProperty("carriageTarget")?.objectReferenceValue as Transform;
        Transform pinionTarget = serialized.FindProperty("pinionTarget")?.objectReferenceValue as Transform;
        if (carriageTarget == null || pinionTarget == null ||
            IsPreviewHierarchy(carriageTarget) || IsPreviewHierarchy(pinionTarget))
        {
            Debug.LogError(
                $"[DashboardFinalReferenceSetup] Factory forklift controller target invalid: " +
                $"Carriage={GetHierarchyPath(carriageTarget)}, Pinion={GetHierarchyPath(pinionTarget)}");
            return false;
        }

        Debug.Log(
            $"[DashboardFinalReferenceSetup] Factory lift targets: " +
            $"Carriage={GetHierarchyPath(carriageTarget)}, Pinion={GetHierarchyPath(pinionTarget)}");
        return true;
    }

    private static bool IsPreviewHierarchy(Transform target)
    {
        Transform current = target;
        while (current != null)
        {
            if (current.name.IndexOf("Preview", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static Mesh GetSharedMesh(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinned)
        {
            return skinned.sharedMesh;
        }

        return renderer != null ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;
    }

    private static string GetHierarchyPath(Transform target)
    {
        if (target == null)
        {
            return "<missing>";
        }

        string path = target.name;
        Transform current = target.parent;
        while (current != null)
        {
            path = $"{current.name}/{path}";
            current = current.parent;
        }

        return path;
    }

    private static bool HasMissingRenderAsset(Renderer renderer)
    {
        Material[] materials = renderer.sharedMaterials;
        if (materials == null || materials.Length == 0)
        {
            return true;
        }

        foreach (Material material in materials)
        {
            if (material == null)
            {
                return true;
            }
        }

        if (renderer is SkinnedMeshRenderer skinned)
        {
            return skinned.sharedMesh == null;
        }

        MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
        return meshFilter != null && meshFilter.sharedMesh == null;
    }

    private static bool ValidateObjectReferences(
        SerializedObject serializedObject,
        IReadOnlyList<string> propertyNames,
        string label)
    {
        serializedObject.Update();
        bool valid = true;
        foreach (string propertyName in propertyNames)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null || property.objectReferenceValue == null)
            {
                Debug.LogError($"[DashboardFinalReferenceSetup] {label} missing {propertyName}.");
                valid = false;
            }
        }

        return valid;
    }

    private static bool ValidateDistinctBoundObjects(SerializedObject binderSerialized)
    {
        HashSet<UnityEngine.Object> targets = new HashSet<UnityEngine.Object>();
        foreach (ReferenceBinding binding in PeoplePillBindings)
        {
            AddDistinctReference(binderSerialized, binding.PropertyName, targets);
        }

        foreach (ReferenceBinding binding in MapPillBindings)
        {
            AddDistinctReference(binderSerialized, binding.PropertyName, targets);
        }

        foreach (ReferenceBinding binding in SystemPillBindings)
        {
            AddDistinctReference(binderSerialized, binding.PropertyName, targets);
        }

        if (targets.Count == 10)
        {
            return true;
        }

        Debug.LogError("[DashboardFinalReferenceSetup] The 10 status references are not distinct Scene objects.");
        return false;
    }

    private static void AddDistinctReference(
        SerializedObject serializedObject,
        string propertyName,
        ISet<UnityEngine.Object> targets)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property?.objectReferenceValue != null)
        {
            targets.Add(property.objectReferenceValue);
        }
    }

    private static scr_ControlTowerDashboardRuntimeBinder ResolveDashboardBinder(scr_ControlTowerUIManager uiManager)
    {
        if (uiManager == null)
        {
            return null;
        }

        SerializedObject serialized = new SerializedObject(uiManager);
        SerializedProperty property = serialized.FindProperty("dashboardRuntimeBinder");
        return property?.objectReferenceValue as scr_ControlTowerDashboardRuntimeBinder;
    }

    private static T FindUniqueSceneComponent<T>(Scene scene) where T : Component
    {
        T result = null;
        T[] components = UnityEngine.Object.FindObjectsByType<T>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (T component in components)
        {
            if (component == null || component.gameObject.scene != scene)
            {
                continue;
            }

            if (result != null)
            {
                Debug.LogError($"[DashboardFinalReferenceSetup] Multiple Scene components found: {typeof(T).Name}.");
                return null;
            }

            result = component;
        }

        return result;
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
                Debug.LogError($"[DashboardFinalReferenceSetup] Duplicate Scene object name: {objectName}.");
                return null;
            }

            result = candidate;
        }

        return result;
    }

    private static Transform FindUniqueDescendant(Transform root, string objectName)
    {
        if (root == null)
        {
            return null;
        }

        Transform result = null;
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
        {
            if (candidate == null || !string.Equals(candidate.name, objectName, StringComparison.Ordinal))
            {
                continue;
            }

            if (result != null)
            {
                Debug.LogError($"[DashboardFinalReferenceSetup] Duplicate object under {root.name}: {objectName}.");
                return null;
            }

            result = candidate;
        }

        return result;
    }
}
#endif

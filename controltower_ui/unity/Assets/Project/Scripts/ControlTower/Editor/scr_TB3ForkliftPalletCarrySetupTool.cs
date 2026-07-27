using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class scr_TB3ForkliftPalletCarrySetupTool
{
    private const string MenuPath = "Tools/ControlTower/TB3-03/Setup Pallet Carry Controller";

    [MenuItem(MenuPath)]
    public static void SetupPalletCarryController()
    {
        Transform sensor = FindUniqueSceneTransform("ForkPickupSensor");
        Transform palletGroup = FindUniqueSceneTransform("Pallet_Group_3D");
        if (sensor == null || palletGroup == null)
        {
            Debug.LogError("[TB3-03 Pallet Setup] Missing ForkPickupSensor or Pallet_Group_3D. Nothing was changed.");
            return;
        }

        Transform carriageLift = FindParentByName(sensor, "Carriage_Lift");
        Transform carryPoint = FindDescendantByName(carriageLift, "PalletCarryPoint") ?? FindUniqueSceneTransform("PalletCarryPoint");
        scr_TB3ForkliftRuntimeController liftController = FindUniqueLiftController();
        if (carriageLift == null || carryPoint == null || liftController == null)
        {
            Debug.LogError(
                $"[TB3-03 Pallet Setup] Missing reference. Carriage_Lift={carriageLift != null}, " +
                $"PalletCarryPoint={carryPoint != null}, LiftController={liftController != null}. Nothing was changed.");
            return;
        }

        scr_TB3ForkliftPalletCarryController controller = sensor.GetComponent<scr_TB3ForkliftPalletCarryController>();
        if (controller == null)
        {
            controller = Undo.AddComponent<scr_TB3ForkliftPalletCarryController>(sensor.gameObject);
        }
        else
        {
            Undo.RecordObject(controller, "Configure TB3-03 Pallet Carry Controller");
        }

        SerializedObject serializedController = new(controller);
        serializedController.FindProperty("palletCarryPoint").objectReferenceValue = carryPoint;
        serializedController.FindProperty("carriageLift").objectReferenceValue = carriageLift;
        serializedController.FindProperty("palletGroupRoot").objectReferenceValue = palletGroup;
        serializedController.FindProperty("liftController").objectReferenceValue = liftController;
        serializedController.ApplyModifiedProperties();
        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(sensor.gameObject.scene);

        Debug.Log(
            "[TB3-03 Pallet Setup] Complete\n" +
            $"Sensor={GetPath(sensor)}\n" +
            $"PalletCarryPoint={GetPath(carryPoint)}\n" +
            $"Carriage_Lift={GetPath(carriageLift)}\n" +
            $"Pallet_Group_3D={GetPath(palletGroup)}\n" +
            $"LiftController={GetPath(liftController.transform)}");
    }

    private static scr_TB3ForkliftRuntimeController FindUniqueLiftController()
    {
        scr_TB3ForkliftRuntimeController found = null;
        foreach (scr_TB3ForkliftRuntimeController controller in
                 Object.FindObjectsByType<scr_TB3ForkliftRuntimeController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (controller == null || !controller.gameObject.scene.IsValid())
            {
                continue;
            }

            if (found != null)
            {
                Debug.LogError("[TB3-03 Pallet Setup] More than one scene forklift lift controller was found. Nothing was changed.");
                return null;
            }

            found = controller;
        }

        return found;
    }

    private static Transform FindUniqueSceneTransform(string objectName)
    {
        Transform found = null;
        foreach (Transform candidate in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (candidate == null || !candidate.gameObject.scene.IsValid() || candidate.name != objectName)
            {
                continue;
            }

            if (found != null)
            {
                Debug.LogError($"[TB3-03 Pallet Setup] More than one scene object named {objectName} was found. Nothing was changed.");
                return null;
            }

            found = candidate;
        }

        return found;
    }

    private static Transform FindParentByName(Transform start, string objectName)
    {
        for (Transform current = start; current != null; current = current.parent)
        {
            if (current.name == objectName)
            {
                return current;
            }
        }

        return null;
    }

    private static Transform FindDescendantByName(Transform root, string objectName)
    {
        if (root == null)
        {
            return null;
        }

        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name == objectName)
            {
                return candidate;
            }
        }

        return null;
    }

    private static string GetPath(Transform target)
    {
        if (target == null)
        {
            return "<null>";
        }

        string path = target.name;
        for (Transform parent = target.parent; parent != null; parent = parent.parent)
        {
            path = parent.name + "/" + path;
        }

        return path;
    }
}

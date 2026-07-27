using System.Collections.Generic;
using UnityEngine;

public class scr_Factory2DPeopleMarkerController : MonoBehaviour
{
    [SerializeField] private scr_Personnel3DMarkerController personnel3DMarkerController;
    [SerializeField] private RectTransform fullPeopleMarkerParent;
    [SerializeField] private RectTransform miniPeopleMarkerParent;
    [SerializeField] private bool refreshOnStart = true;

    private const int EmployeeCount = 5;
    private const int VisitorCount = 3;
    private readonly HashSet<string> missingReferenceWarnings = new();
    private scr_Personnel3DMarkerController subscribedPersonnelController;

    private void OnEnable()
    {
        ResolveReferences();
        SubscribePersonnelController();
        if (refreshOnStart)
        {
            RefreshPeopleMarkers();
        }
    }

    private void OnDisable()
    {
        UnsubscribePersonnelController();
    }

    private void Start()
    {
        if (refreshOnStart)
        {
            RefreshPeopleMarkers();
        }
    }

    public void RefreshPeopleMarkers()
    {
        ResolveReferences();
        SubscribePersonnelController();
        for (int i = 1; i <= EmployeeCount; i++)
        {
            RefreshPersonMarker($"BoxHuman_{i}", $"Marker_Worker_{i:00}", $"Image_Mini_Marker_Worker_{i:00}");
        }

        for (int i = 1; i <= VisitorCount; i++)
        {
            RefreshPersonMarker($"BoxHuman_visitor_{i}", $"Marker_Visitor_{i:00}", $"Image_Mini_Marker_Visitor_{i:00}");
        }
    }

    private void RefreshPersonMarker(string personName, string fullMarkerName, string miniMarkerName)
    {
        RectTransform fullMarker = FindMarker(fullPeopleMarkerParent, fullMarkerName);
        RectTransform miniMarker = FindMarker(miniPeopleMarkerParent, miniMarkerName);
        bool active = TryGetPerson2DVisibility(personName, out bool visibleOn2D) && visibleOn2D;

        SetMarkerActive(fullMarker, active);
        SetMarkerActive(miniMarker, active);
    }

    private bool TryGetPerson2DVisibility(string personName, out bool visibleOn2D)
    {
        visibleOn2D = false;
        if (personnel3DMarkerController != null &&
            personnel3DMarkerController.TryGetPerson2DState(personName, out _, out visibleOn2D, out _))
        {
            return true;
        }

        WarnMissingOnce($"person-state:{personName}", $"[Factory2DPeople] Missing 3D personnel state for '{personName}'. 2D marker remains hidden.");
        return false;
    }

    private RectTransform FindMarker(RectTransform parent, string markerName)
    {
        if (parent == null)
        {
            return null;
        }

        Transform existing = parent.Find(markerName);
        RectTransform marker = existing != null ? existing.GetComponent<RectTransform>() : null;
        if (marker != null)
        {
            return marker;
        }

        WarnMissingOnce($"marker:{markerName}", $"[Factory2DPeople] Missing Edit Mode people marker '{markerName}'. Runtime will not create it.");
        return null;
    }

    private void ResolveReferences()
    {
        personnel3DMarkerController ??= FindSceneComponentByType<scr_Personnel3DMarkerController>();
        fullPeopleMarkerParent ??= FindGroup("Image_MapArea_Background", "PeopleMarker_Group");
        miniPeopleMarkerParent ??= FindRectTransformByName("Image_Mini2DMapArea");
    }

    private void SubscribePersonnelController()
    {
        if (personnel3DMarkerController == subscribedPersonnelController)
        {
            return;
        }

        UnsubscribePersonnelController();
        subscribedPersonnelController = personnel3DMarkerController;
        if (subscribedPersonnelController != null)
        {
            subscribedPersonnelController.Person2DStateChanged += RefreshPeopleMarkers;
        }
    }

    private void UnsubscribePersonnelController()
    {
        if (subscribedPersonnelController != null)
        {
            subscribedPersonnelController.Person2DStateChanged -= RefreshPeopleMarkers;
            subscribedPersonnelController = null;
        }
    }

    private static void SetMarkerActive(RectTransform marker, bool active)
    {
        if (marker != null && marker.gameObject.activeSelf != active)
        {
            marker.gameObject.SetActive(active);
        }
    }

    private RectTransform FindGroup(string parentName, string groupName)
    {
        GameObject parent = FindSceneGameObjectByName(parentName);
        if (parent == null)
        {
            WarnMissingOnce($"parent:{parentName}", $"[Factory2DPeople] Missing parent '{parentName}'. Runtime will not create people marker groups.");
            return null;
        }

        Transform existing = parent.transform.Find(groupName);
        if (existing != null)
        {
            return existing.GetComponent<RectTransform>();
        }

        WarnMissingOnce($"group:{parentName}/{groupName}", $"[Factory2DPeople] Missing Edit Mode group '{parentName}/{groupName}'. Runtime will not create it.");
        return null;
    }

    private RectTransform FindRectTransformByName(string objectName)
    {
        GameObject target = FindSceneGameObjectByName(objectName);
        if (target != null && target.TryGetComponent(out RectTransform rectTransform))
        {
            return rectTransform;
        }

        WarnMissingOnce($"rect:{objectName}", $"[Factory2DPeople] Missing Edit Mode RectTransform '{objectName}'. Runtime will not create it.");
        return null;
    }

    private void WarnMissingOnce(string key, string message)
    {
        if (missingReferenceWarnings.Add(key))
        {
            Debug.LogWarning(message);
        }
    }

    private static GameObject FindSceneGameObjectByName(string objectName)
    {
        foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (candidate == null ||
                candidate.name != objectName ||
                !candidate.scene.IsValid())
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private static T FindSceneComponentByType<T>() where T : Component
    {
        foreach (T component in Resources.FindObjectsOfTypeAll<T>())
        {
            if (component != null && component.gameObject.scene.IsValid())
            {
                return component;
            }
        }

        return null;
    }
}

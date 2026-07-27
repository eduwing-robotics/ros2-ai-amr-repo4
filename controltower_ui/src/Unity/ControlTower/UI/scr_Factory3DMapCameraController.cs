using UnityEngine;
using UnityEngine.UI;

public class scr_Factory3DMapCameraController : MonoBehaviour
{
    private const string ButtonTopViewName = "Button_Factory3DView_Top";
    private const string ButtonIsoViewName = "Button_Factory3DView_Iso";
    private const string ButtonFrontViewName = "Button_Factory3DView_Front";
    private const string ButtonResetViewName = "Button_Factory3DView_Reset";

    [SerializeField] private Camera factory3DMapCamera;
    [SerializeField] private Transform factory3DStage;
    [SerializeField] private Vector3 topPosition = new Vector3(0f, 12f, 8f);
    [SerializeField] private Vector3 topRotation = new Vector3(90f, 0f, 0f);
    [SerializeField] private Vector3 isoPosition = new Vector3(-1.892542f, 7.461749f, 3.776397f);
    [SerializeField] private Vector3 isoRotation = new Vector3(61.858f, 20.549f, 0.002f);
    [SerializeField] private Vector3 frontPosition = new Vector3(0f, 4f, -8f);
    [SerializeField] private Vector3 frontRotation = new Vector3(55f, 0f, 0f);
    [SerializeField] private bool useExplicitPresetRotation = true;
    [SerializeField] private float isoFieldOfView = 50f;
    [SerializeField] private float frontFieldOfView = 45f;

    private Button buttonTopView;
    private Button buttonIsoView;
    private Button buttonFrontView;
    private Button buttonResetView;
    private bool hasWarnedMissingViewButtons;

    private void OnEnable()
    {
        ResolveViewControlButtons();
        BindViewControlButtons();
    }

    private void Start()
    {
        ResolveViewControlButtons();
        BindViewControlButtons();
    }

    private void OnDisable()
    {
        UnbindViewControlButtons();
    }

    private void OnDestroy()
    {
        UnbindViewControlButtons();
    }

    public void ShowTopView()
    {
        if (!ResolveReferences()) return;
        factory3DMapCamera.orthographic = true;
        factory3DMapCamera.transform.position = topPosition;
        factory3DMapCamera.transform.rotation = Quaternion.Euler(topRotation);
    }

    public void ShowIsoView()
    {
        if (!ResolveReferences()) return;
        factory3DMapCamera.orthographic = false;
        factory3DMapCamera.fieldOfView = isoFieldOfView;
        factory3DMapCamera.transform.position = isoPosition;
        ApplyPresetRotation(isoRotation);
    }

    public void ShowFrontView()
    {
        if (!ResolveReferences()) return;
        factory3DMapCamera.orthographic = false;
        factory3DMapCamera.fieldOfView = frontFieldOfView;
        factory3DMapCamera.transform.position = frontPosition;
        ApplyPresetRotation(frontRotation);
    }

    public void ResetView()
    {
        ShowIsoView();
    }

    private void ApplyPresetRotation(Vector3 presetRotation)
    {
        if (useExplicitPresetRotation)
        {
            factory3DMapCamera.transform.eulerAngles = presetRotation;
            return;
        }

        factory3DMapCamera.transform.LookAt(factory3DStage.position);
    }

    private void ResolveViewControlButtons()
    {
        if (buttonTopView == null)
        {
            buttonTopView = FindSceneButton(ButtonTopViewName);
        }

        if (buttonIsoView == null)
        {
            buttonIsoView = FindSceneButton(ButtonIsoViewName);
        }

        if (buttonFrontView == null)
        {
            buttonFrontView = FindSceneButton(ButtonFrontViewName);
        }

        if (buttonResetView == null)
        {
            buttonResetView = FindSceneButton(ButtonResetViewName);
        }

        if (!hasWarnedMissingViewButtons &&
            (buttonTopView == null || buttonIsoView == null || buttonFrontView == null || buttonResetView == null))
        {
            Debug.LogWarning(
                "[Factory3DMapCamera] One or more 3D view control buttons were not found. " +
                $"Top={buttonTopView != null}, Iso={buttonIsoView != null}, Front={buttonFrontView != null}, Reset={buttonResetView != null}");
            hasWarnedMissingViewButtons = true;
        }
    }

    private void BindViewControlButtons()
    {
        if (buttonTopView != null)
        {
            buttonTopView.onClick.RemoveListener(ShowTopView);
            buttonTopView.onClick.AddListener(ShowTopView);
        }

        if (buttonIsoView != null)
        {
            buttonIsoView.onClick.RemoveListener(ShowIsoView);
            buttonIsoView.onClick.AddListener(ShowIsoView);
        }

        if (buttonFrontView != null)
        {
            buttonFrontView.onClick.RemoveListener(ShowFrontView);
            buttonFrontView.onClick.AddListener(ShowFrontView);
        }

        if (buttonResetView != null)
        {
            buttonResetView.onClick.RemoveListener(ResetView);
            buttonResetView.onClick.AddListener(ResetView);
        }
    }

    private void UnbindViewControlButtons()
    {
        if (buttonTopView != null)
        {
            buttonTopView.onClick.RemoveListener(ShowTopView);
        }

        if (buttonIsoView != null)
        {
            buttonIsoView.onClick.RemoveListener(ShowIsoView);
        }

        if (buttonFrontView != null)
        {
            buttonFrontView.onClick.RemoveListener(ShowFrontView);
        }

        if (buttonResetView != null)
        {
            buttonResetView.onClick.RemoveListener(ResetView);
        }
    }

    private static Button FindSceneButton(string buttonName)
    {
        foreach (Button button in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (button.gameObject.name == buttonName && button.gameObject.scene.IsValid())
            {
                return button;
            }
        }

        return null;
    }

    private bool ResolveReferences()
    {
        if (factory3DMapCamera == null)
        {
            factory3DMapCamera = FindSceneObject("Camera_Factory3DMap")?.GetComponent<Camera>();
        }

        if (factory3DStage == null)
        {
            factory3DStage = FindSceneObject("Factory3DStage")?.transform;
        }

        if (factory3DMapCamera == null || factory3DStage == null)
        {
            Debug.LogWarning("[Factory3DMapCamera] Camera_Factory3DMap or Factory3DStage was not found.");
            return false;
        }

        return true;
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

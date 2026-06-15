using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

/// <summary>
/// Tap-based tooth/bone info display for AR image tracking.
/// Shows Display Name + Description only when the user taps a visible tracked model.
/// </summary>
[DisallowMultipleComponent]
public class BoneNameDisplayScript : MonoBehaviour
{
    [System.Serializable]
    public class BoneNameMapping
    {
        [Tooltip("Must match Reference Image name in the image library.")]
        public string imageReferenceName;

        [Tooltip("Title shown in the info panel.")]
        public string displayName;

        [Tooltip("Body text shown below the title.")]
        [TextArea(2, 8)]
        public string description;
    }

    [Header("Tooth / bone data (Inspector)")]
    [SerializeField] private List<BoneNameMapping> boneNames = new();

    [Header("References")]
    [SerializeField] private ARTrackedImageManager trackedImageManager;
    [SerializeField] private Camera arCamera;

    [Header("Info panel UI (auto-created if empty)")]
    [SerializeField] private CanvasGroup topNameCanvasGroup;
    [SerializeField] private Text topNameText;
    [SerializeField] private CanvasGroup infoPanelCanvasGroup;
    [SerializeField] private Text descriptionText;

    [Header("Layout")]
    [SerializeField] private Vector2 topNamePadding = new(32f, 48f);
    [SerializeField] private Vector2 bottomPanelPadding = new(0f, 48f);

    [Header("Tap settings")]
    [SerializeField] private float tapMaxMovementPixels = 25f;
    [SerializeField] private float raycastDistance = 100f;

    [Header("Appearance")]
    [SerializeField] private float fadeDuration = 0.25f;
    [SerializeField] private int titleFontSize = 38;
    [SerializeField] private int descriptionFontSize = 26;
    [SerializeField] private Color titleColor = Color.white;
    [SerializeField] private Color descriptionColor = new(0.92f, 0.92f, 0.92f, 1f);

    private readonly Dictionary<string, BoneNameMapping> _mappingLookup = new();
    private BoneInfoComponent _selectedBone;
    private Vector2 _touchStartPosition;
    private float _uiAlpha;
    private float _uiAlphaTarget;

    private void Awake()
    {
        if (trackedImageManager == null)
        {
            trackedImageManager = GetComponent<ARTrackedImageManager>();
        }

        if (arCamera == null)
        {
            arCamera = Camera.main;
        }

        BuildMappingLookup();
        EnsureInfoPanelUiExists();
        HidePanelImmediate();
    }

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();

        if (trackedImageManager != null)
        {
            trackedImageManager.trackablesChanged.AddListener(OnTrackablesChanged);
        }
    }

    private void OnDisable()
    {
        if (trackedImageManager != null)
        {
            trackedImageManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
        }
    }

    private void Update()
    {
        HandleTapInput();
        UpdateUiFade();
        HidePanelIfSelectionLost();
    }

    /// <summary>
    /// Called by MultipleImagesTrackingManager when a model is spawned.
    /// Attaches and configures BoneInfoComponent on the model root.
    /// </summary>
    public void RegisterModel(GameObject modelRoot, string imageReferenceName)
    {
        if (modelRoot == null || string.IsNullOrWhiteSpace(imageReferenceName))
        {
            return;
        }

        var key = imageReferenceName.Trim();
        var boneInfo = modelRoot.GetComponent<BoneInfoComponent>();
        if (boneInfo == null)
        {
            boneInfo = modelRoot.AddComponent<BoneInfoComponent>();
        }

        if (_mappingLookup.TryGetValue(key, out var mapping))
        {
            boneInfo.Initialize(key, mapping.displayName, mapping.description);
        }
        else
        {
            boneInfo.Initialize(key, FormatFallbackName(key), string.Empty);
        }
    }

    private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> eventArgs)
    {
        HidePanelIfSelectionLost();
    }

    private void BuildMappingLookup()
    {
        _mappingLookup.Clear();

        foreach (var mapping in boneNames)
        {
            if (mapping == null || string.IsNullOrWhiteSpace(mapping.imageReferenceName))
            {
                continue;
            }

            _mappingLookup[mapping.imageReferenceName.Trim()] = mapping;
        }
    }

    private void HandleTapInput()
    {
        if (Touch.activeTouches.Count != 1)
        {
            return;
        }

        var touch = Touch.activeTouches[0];

        if (touch.phase == TouchPhase.Began)
        {
            _touchStartPosition = touch.screenPosition;
            return;
        }

        if (touch.phase != TouchPhase.Ended)
        {
            return;
        }

        var movement = Vector2.Distance(_touchStartPosition, touch.screenPosition);
        if (movement > tapMaxMovementPixels)
        {
            return;
        }

        ProcessTap(touch.screenPosition);
    }

    private void ProcessTap(Vector2 screenPosition)
    {
        if (arCamera == null)
        {
            return;
        }

        var ray = arCamera.ScreenPointToRay(screenPosition);
        var hits = Physics.RaycastAll(ray, raycastDistance);
        if (hits.Length == 0)
        {
            ClearSelection();
            return;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (var i = 0; i < hits.Length; i++)
        {
            var boneInfo = hits[i].transform.GetComponentInParent<BoneInfoComponent>();
            if (boneInfo == null || !IsBoneVisibleAndTracked(boneInfo))
            {
                continue;
            }

            SelectBone(boneInfo);
            return;
        }

        ClearSelection();
    }

    private bool IsBoneVisibleAndTracked(BoneInfoComponent boneInfo)
    {
        if (boneInfo == null || !boneInfo.gameObject.activeInHierarchy)
        {
            return false;
        }

        if (trackedImageManager == null)
        {
            return true;
        }

        foreach (var trackedImage in trackedImageManager.trackables)
        {
            if (trackedImage.trackingState != TrackingState.Tracking)
            {
                continue;
            }

            if (trackedImage.referenceImage.name.Trim() != boneInfo.ImageReferenceName)
            {
                continue;
            }

            return boneInfo.transform.IsChildOf(trackedImage.transform)
                   || boneInfo.transform == trackedImage.transform;
        }

        return false;
    }

    private void SelectBone(BoneInfoComponent boneInfo)
    {
        _selectedBone = boneInfo;
        ShowPanel(boneInfo.DisplayName, boneInfo.Description);
    }

    private void ClearSelection()
    {
        _selectedBone = null;
        _uiAlphaTarget = 0f;
    }

    private void HidePanelIfSelectionLost()
    {
        if (_selectedBone == null)
        {
            return;
        }

        if (!IsBoneVisibleAndTracked(_selectedBone))
        {
            ClearSelection();
        }
    }

    private void ShowPanel(string displayName, string description)
    {
        if (topNameText != null)
        {
            topNameText.text = displayName;
        }

        if (descriptionText != null)
        {
            descriptionText.text = description;
        }

        _uiAlphaTarget = 1f;
    }

    private void HidePanelImmediate()
    {
        _uiAlpha = 0f;
        _uiAlphaTarget = 0f;
        ApplyUiAlpha(0f);
    }

    private void UpdateUiFade()
    {
        if (topNameCanvasGroup == null && infoPanelCanvasGroup == null)
        {
            return;
        }

        if (Mathf.Approximately(_uiAlpha, _uiAlphaTarget))
        {
            ApplyUiAlpha(_uiAlphaTarget);
            return;
        }

        var step = fadeDuration <= 0f ? 1f : Time.deltaTime / fadeDuration;
        _uiAlpha = Mathf.MoveTowards(_uiAlpha, _uiAlphaTarget, step);
        ApplyUiAlpha(_uiAlpha);
    }

    private void ApplyUiAlpha(float alpha)
    {
        if (topNameCanvasGroup != null)
        {
            topNameCanvasGroup.alpha = alpha;
            topNameCanvasGroup.blocksRaycasts = false;
        }

        if (infoPanelCanvasGroup != null)
        {
            infoPanelCanvasGroup.alpha = alpha;
            infoPanelCanvasGroup.blocksRaycasts = alpha > 0.01f;
        }
    }

    private static string FormatFallbackName(string imageName)
    {
        return imageName.Replace('_', ' ');
    }

    private void EnsureInfoPanelUiExists()
    {
        if (topNameCanvasGroup != null && topNameText != null
            && infoPanelCanvasGroup != null && descriptionText != null)
        {
            return;
        }

        var canvasObject = new GameObject("BoneInfoCanvas");
        canvasObject.transform.SetParent(transform, false);

        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;

        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObject.AddComponent<GraphicRaycaster>();

        CreateTopNameBar(canvasObject.transform);
        CreateDescriptionPanel(canvasObject.transform);
    }

    private void CreateTopNameBar(Transform canvasTransform)
    {
        var topBarObject = new GameObject("TopNameBar", typeof(RectTransform));
        topBarObject.transform.SetParent(canvasTransform, false);

        var topBarRect = topBarObject.GetComponent<RectTransform>();
        topBarRect.anchorMin = new Vector2(0f, 1f);
        topBarRect.anchorMax = new Vector2(1f, 1f);
        topBarRect.pivot = new Vector2(0.5f, 1f);
        topBarRect.anchoredPosition = new Vector2(0f, -topNamePadding.y);
        topBarRect.sizeDelta = new Vector2(-topNamePadding.x * 2f, 100f);

        var topBarImage = topBarObject.AddComponent<Image>();
        topBarImage.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
        topBarImage.type = Image.Type.Sliced;
        topBarImage.color = new Color(0f, 0f, 0f, 0.55f);
        topBarImage.raycastTarget = false;

        topNameCanvasGroup = topBarObject.AddComponent<CanvasGroup>();
        topNameCanvasGroup.alpha = 0f;
        topNameCanvasGroup.interactable = false;
        topNameCanvasGroup.blocksRaycasts = false;

        topNameText = CreatePanelText(
            topBarObject.transform,
            "TopNameText",
            Vector2.zero,
            Vector2.one,
            new Vector2(24f, 8f),
            new Vector2(-24f, -8f),
            titleFontSize,
            titleColor,
            TextAnchor.MiddleCenter,
            FontStyle.Bold);
    }

    private void CreateDescriptionPanel(Transform canvasTransform)
    {
        var panelObject = new GameObject("DescriptionPanel", typeof(RectTransform));
        panelObject.transform.SetParent(canvasTransform, false);

        var panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0f);
        panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, bottomPanelPadding.y);
        panelRect.sizeDelta = new Vector2(920f, 260f);

        var panelImage = panelObject.AddComponent<Image>();
        panelImage.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
        panelImage.type = Image.Type.Sliced;
        panelImage.color = new Color(0f, 0f, 0f, 0.72f);
        panelImage.raycastTarget = false;

        infoPanelCanvasGroup = panelObject.AddComponent<CanvasGroup>();
        infoPanelCanvasGroup.alpha = 0f;
        infoPanelCanvasGroup.interactable = false;
        infoPanelCanvasGroup.blocksRaycasts = false;

        descriptionText = CreatePanelText(
            panelObject.transform,
            "DescriptionText",
            Vector2.zero,
            Vector2.one,
            new Vector2(24f, 20f),
            new Vector2(-24f, -20f),
            descriptionFontSize,
            descriptionColor,
            TextAnchor.UpperLeft,
            FontStyle.Normal);
        descriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
        descriptionText.verticalOverflow = VerticalWrapMode.Overflow;
    }

    private static Text CreatePanelText(
        Transform parent,
        string objectName,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax,
        int fontSize,
        Color color,
        TextAnchor alignment,
        FontStyle fontStyle)
    {
        var textObject = new GameObject(objectName, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);

        var rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.offsetMin = offsetMin;
        rectTransform.offsetMax = offsetMax;

        var text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.alignment = alignment;
        text.supportRichText = true;
        text.raycastTarget = false;

        return text;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        BuildMappingLookup();
    }
#endif
}

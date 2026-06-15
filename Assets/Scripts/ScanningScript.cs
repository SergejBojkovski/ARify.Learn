using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Scan feedback for AR tooth marker tracking:
/// - Animated center indicator while searching
/// - One-shot Android haptic when a marker is first detected (and again after loss)
/// </summary>
[DisallowMultipleComponent]
public class ScanningScript : MonoBehaviour
{
    private enum ScanPhase
    {
        Searching,
        Detected
    }

    [Header("AR")]
    [SerializeField] private ARTrackedImageManager trackedImageManager;

    [Header("Scan Indicator UI")]
    [SerializeField] private CanvasGroup scanIndicatorCanvasGroup;
    [SerializeField] private RectTransform scanRingTransform;
    [SerializeField] private RectTransform scanPulseTransform;

    [Header("Indicator Animation")]
    [SerializeField] private float fadeDuration = 0.3f;
    [SerializeField] private float ringRotateSpeed = 90f;
    [SerializeField] private float pulseSpeed = 2.5f;
    [SerializeField] private float pulseMinScale = 0.85f;
    [SerializeField] private float pulseMaxScale = 1.15f;

    [Header("Haptic Feedback (Android)")]
    [SerializeField] private long androidVibrationMilliseconds = 45;
    [SerializeField] private int androidVibrationAmplitude = 160;

    private ScanPhase _scanPhase = ScanPhase.Searching;
    private float _indicatorAlpha;
    private float _indicatorAlphaTarget = 1f;
    private float _pulseTimer;

    private void Awake()
    {
        if (trackedImageManager == null)
        {
            trackedImageManager = GetComponent<ARTrackedImageManager>();
        }

        EnsureScanIndicatorUi();
        SetIndicatorImmediate(1f);
    }

    private void OnEnable()
    {
        if (trackedImageManager != null)
        {
            trackedImageManager.trackablesChanged.AddListener(OnTrackablesChanged);
        }

        RefreshScanPhase();
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
        UpdateIndicatorFade();
        UpdateIndicatorAnimation();
    }

    private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> eventArgs)
    {
        RefreshScanPhase();
    }

    /// <summary>
    /// Detects transitions: Searching → Detected → Lost → Searching.
    /// </summary>
    private void RefreshScanPhase()
    {
        var isTracking = IsAnyToothMarkerTracked();
        var newPhase = isTracking ? ScanPhase.Detected : ScanPhase.Searching;

        if (newPhase == ScanPhase.Detected && _scanPhase == ScanPhase.Searching)
        {
            // Successful detection event (includes re-detection after loss).
            _indicatorAlphaTarget = 0f;
            TriggerDetectionHaptic();
        }
        else if (newPhase == ScanPhase.Searching && _scanPhase == ScanPhase.Detected)
        {
            // Tracking lost — show indicator again.
            _indicatorAlphaTarget = 1f;
        }
        else
        {
            _indicatorAlphaTarget = isTracking ? 0f : 1f;
        }

        _scanPhase = newPhase;

        if (scanIndicatorCanvasGroup != null)
        {
            scanIndicatorCanvasGroup.blocksRaycasts = _indicatorAlphaTarget > 0.01f;
        }
    }

    private bool IsAnyToothMarkerTracked()
    {
        if (trackedImageManager == null)
        {
            return false;
        }

        foreach (var trackedImage in trackedImageManager.trackables)
        {
            if (trackedImage.trackingState == TrackingState.Tracking)
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateIndicatorFade()
    {
        if (scanIndicatorCanvasGroup == null)
        {
            return;
        }

        if (Mathf.Approximately(_indicatorAlpha, _indicatorAlphaTarget))
        {
            scanIndicatorCanvasGroup.alpha = _indicatorAlphaTarget;
            return;
        }

        var step = fadeDuration <= 0f ? 1f : Time.deltaTime / fadeDuration;
        _indicatorAlpha = Mathf.MoveTowards(_indicatorAlpha, _indicatorAlphaTarget, step);
        scanIndicatorCanvasGroup.alpha = _indicatorAlpha;
    }

    private void UpdateIndicatorAnimation()
    {
        if (scanIndicatorCanvasGroup == null || _indicatorAlpha <= 0.01f)
        {
            return;
        }

        _pulseTimer += Time.deltaTime * pulseSpeed;

        if (scanRingTransform != null)
        {
            scanRingTransform.Rotate(0f, 0f, -ringRotateSpeed * Time.deltaTime);
        }

        if (scanPulseTransform != null)
        {
            var t = (Mathf.Sin(_pulseTimer) + 1f) * 0.5f;
            var scale = Mathf.Lerp(pulseMinScale, pulseMaxScale, t);
            scanPulseTransform.localScale = Vector3.one * scale;
        }
    }

    private void SetIndicatorImmediate(float alpha)
    {
        _indicatorAlpha = alpha;
        _indicatorAlphaTarget = alpha;

        if (scanIndicatorCanvasGroup != null)
        {
            scanIndicatorCanvasGroup.alpha = alpha;
            scanIndicatorCanvasGroup.blocksRaycasts = alpha > 0.01f;
        }
    }

    /// <summary>
    /// Short one-shot vibration on Android. Safe no-op in Unity Editor.
    /// </summary>
    private void TriggerDetectionHaptic()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");

            if (vibrator == null)
            {
                return;
            }

            var sdkInt = new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");
            if (sdkInt >= 26)
            {
                using var vibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect");
                var amplitude = Mathf.Clamp(androidVibrationAmplitude, 1, 255);
                using var effect = vibrationEffectClass.CallStatic<AndroidJavaObject>(
                    "createOneShot",
                    androidVibrationMilliseconds,
                    amplitude);

                vibrator.Call("vibrate", effect);
            }
            else
            {
                vibrator.Call("vibrate", androidVibrationMilliseconds);
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[ScanningScript] Haptic failed, using fallback. {exception.Message}");
            Handheld.Vibrate();
        }
#else
        Debug.Log("[ScanningScript] Haptic feedback (Editor simulation — no vibration on desktop).");
#endif
    }

    /// <summary>
    /// Builds a lightweight overlay UI if references are not assigned in the Inspector.
    /// </summary>
    private void EnsureScanIndicatorUi()
    {
        if (scanIndicatorCanvasGroup != null)
        {
            return;
        }

        var canvasObject = new GameObject("ScanIndicatorCanvas");
        canvasObject.transform.SetParent(transform, false);

        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObject.AddComponent<GraphicRaycaster>();

        scanIndicatorCanvasGroup = canvasObject.AddComponent<CanvasGroup>();
        scanIndicatorCanvasGroup.alpha = 1f;
        scanIndicatorCanvasGroup.interactable = false;
        scanIndicatorCanvasGroup.blocksRaycasts = false;

        var ringObject = CreateUiImage(canvasObject.transform, "ScanRing", 220f, new Color(1f, 1f, 1f, 0.35f));
        scanRingTransform = ringObject.GetComponent<RectTransform>();

        var pulseObject = CreateUiImage(canvasObject.transform, "ScanPulse", 140f, new Color(0.4f, 0.85f, 1f, 0.55f));
        scanPulseTransform = pulseObject.GetComponent<RectTransform>();

        var centerDotObject = CreateUiImage(canvasObject.transform, "ScanCenterDot", 18f, new Color(1f, 1f, 1f, 0.9f));
        centerDotObject.GetComponent<RectTransform>().SetAsLastSibling();
    }

    private static GameObject CreateUiImage(Transform parent, string objectName, float size, Color color)
    {
        var imageObject = new GameObject(objectName, typeof(RectTransform));
        imageObject.transform.SetParent(parent, false);

        var rectTransform = imageObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = new Vector2(size, size);
        rectTransform.anchoredPosition = Vector2.zero;

        var image = imageObject.AddComponent<Image>();
        image.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = Image.Type.Simple;
        image.raycastTarget = false;
        image.color = color;

        return imageObject;
    }
}

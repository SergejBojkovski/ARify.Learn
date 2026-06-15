using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

[DisallowMultipleComponent]
public class ModelInteractionScript : MonoBehaviour
{
    [Header("Rotation (1 finger drag)")]
    [SerializeField] private float rotationSpeed = 0.25f;

    [Header("Scale (2 finger pinch)")]
    [SerializeField] private float pinchSensitivity = 0.005f;
    [SerializeField] private float minScaleMultiplier = 0.25f;
    [SerializeField] private float maxScaleMultiplier = 4f;

    [Header("Touch targeting")]
    [SerializeField] private Camera interactionCamera;

    private static ModelInteractionScript _lockedInteractor;

    private Vector3 _baseLocalPosition;
    private Quaternion _baseLocalRotation = Quaternion.identity;
    private float _baseUniformScale = 1f;
    private Vector2 _userEuler;
    private float _scaleMultiplier = 1f;
    private float _lastPinchDistance;
    private bool _isPlaced;

    public bool IsPlaced => _isPlaced;

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
    }

    private void Awake()
    {
        if (interactionCamera == null)
        {
            interactionCamera = Camera.main;
        }
    }

    private void OnDisable()
    {
        if (_lockedInteractor == this)
        {
            _lockedInteractor = null;
        }
    }

    public void Initialize(Vector3 localPosition, Quaternion localRotation, float baseUniformScale)
    {
        _baseLocalPosition = localPosition;
        _baseLocalRotation = localRotation;
        _baseUniformScale = Mathf.Max(baseUniformScale, 0.0001f);
        _userEuler = Vector2.zero;
        _scaleMultiplier = 1f;
        _isPlaced = true;
        ApplyTransform();
    }

    public void ResetPlacement()
    {
        if (_lockedInteractor == this)
        {
            _lockedInteractor = null;
        }

        _isPlaced = false;
        _userEuler = Vector2.zero;
        _scaleMultiplier = 1f;
    }

    private void Update()
    {
        if (!_isPlaced || !isActiveAndEnabled)
        {
            return;
        }

        var activeTouches = Touch.activeTouches;
        if (activeTouches.Count == 0)
        {
            if (_lockedInteractor == this)
            {
                _lockedInteractor = null;
            }

            return;
        }

        if (_lockedInteractor == null)
        {
            if (!TryAcquireInteractionLock(activeTouches))
            {
                return;
            }
        }

        if (_lockedInteractor != this)
        {
            return;
        }

        if (activeTouches.Count == 1)
        {
            HandleSingleTouchRotation(activeTouches[0]);
        }
        else if (activeTouches.Count >= 2)
        {
            HandlePinchScale(activeTouches[0], activeTouches[1]);
        }
    }

    private bool TryAcquireInteractionLock(IReadOnlyList<Touch> activeTouches)
    {
        for (var i = 0; i < activeTouches.Count; i++)
        {
            var touch = activeTouches[i];
            if (touch.phase != TouchPhase.Began && touch.phase != TouchPhase.Moved && touch.phase != TouchPhase.Stationary)
            {
                continue;
            }

            var interactor = FindInteractorUnderScreenPoint(touch.screenPosition);
            if (interactor == null)
            {
                continue;
            }

            _lockedInteractor = interactor;
            return interactor == this;
        }

        return false;
    }

    private static ModelInteractionScript FindInteractorUnderScreenPoint(Vector2 screenPosition)
    {
        var camera = Camera.main;
        if (camera == null)
        {
            return null;
        }

        var ray = camera.ScreenPointToRay(screenPosition);
        var hits = Physics.RaycastAll(ray, 100f);
        if (hits.Length == 0)
        {
            return null;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (var i = 0; i < hits.Length; i++)
        {
            var interactor = hits[i].transform.GetComponentInParent<ModelInteractionScript>();
            if (interactor != null && interactor._isPlaced && interactor.isActiveAndEnabled)
            {
                return interactor;
            }
        }

        return null;
    }

    private void HandleSingleTouchRotation(Touch touch)
    {
        if (touch.phase != TouchPhase.Moved)
        {
            return;
        }

        var delta = touch.delta;
        _userEuler.y += delta.x * rotationSpeed;
        _userEuler.x -= delta.y * rotationSpeed;
        ApplyTransform();
    }

    private void HandlePinchScale(Touch touch0, Touch touch1)
    {
        var distance = Vector2.Distance(touch0.screenPosition, touch1.screenPosition);

        if (touch0.phase == TouchPhase.Began || touch1.phase == TouchPhase.Began)
        {
            _lastPinchDistance = distance;
            return;
        }

        var pinchDelta = distance - _lastPinchDistance;
        _lastPinchDistance = distance;

        _scaleMultiplier = Mathf.Clamp(
            _scaleMultiplier + pinchDelta * pinchSensitivity,
            minScaleMultiplier,
            maxScaleMultiplier);

        ApplyTransform();
    }

    private void ApplyTransform()
    {
        transform.localPosition = _baseLocalPosition;
        transform.localRotation = _baseLocalRotation * Quaternion.Euler(_userEuler.x, _userEuler.y, 0f);

        var scale = _baseUniformScale * _scaleMultiplier;
        transform.localScale = Vector3.one * scale;
    }
}

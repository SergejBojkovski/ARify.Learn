using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARTrackedImageManager))]
public class MultipleImagesTrackingManager : MonoBehaviour
{
    [Header("Model prefabs (names must match Reference Image names)")]
    [FormerlySerializedAs("prefabsToSpawn")]
    [SerializeField] private List<GameObject> modelPrefabs = new();

    [Header("Placement")]
    [SerializeField] private bool parentToTrackedImage = true;
    [SerializeField] private bool autoFitModelToMarker = true;
    [SerializeField] private float modelFillRatio = 0.85f;
    [SerializeField] private Vector3 localPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 localEulerOffset = Vector3.zero;
    [SerializeField] private float uniformScale = 1f;
    [Tooltip("Debug cubes only — turn OFF when testing models.")]
    [SerializeField] private bool showDebugCubeOnMarker = false;
    [SerializeField] private bool fixUrpMaterialsOnSpawn = true;

    private ARTrackedImageManager _trackedImageManager;
    private BoneNameDisplayScript _boneNameDisplay;
    private readonly Dictionary<string, GameObject> _spawnedModelsByImageName = new();
    private readonly Dictionary<string, GameObject> _debugCubesByImageName = new();
    private static Material _fallbackUrpMaterial;

    private void Awake()
    {
        _trackedImageManager = GetComponent<ARTrackedImageManager>();
        _boneNameDisplay = GetComponent<BoneNameDisplayScript>();
    }

    private void Start()
    {
        if (_trackedImageManager == null)
        {
            Debug.LogError("ARTrackedImageManager component not found.");
            return;
        }

        SpawnAndRegisterModels();
        Debug.Log($"[AR] Registered {_spawnedModelsByImageName.Count} model(s) for image tracking.");
    }

    private void OnEnable()
    {
        if (_trackedImageManager == null)
        {
            _trackedImageManager = GetComponent<ARTrackedImageManager>();
        }

        if (_trackedImageManager != null)
        {
            _trackedImageManager.trackablesChanged.AddListener(OnTrackablesChanged);
        }
    }

    private void OnDisable()
    {
        if (_trackedImageManager != null)
        {
            _trackedImageManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
        }
    }

    private void SpawnAndRegisterModels()
    {
        _spawnedModelsByImageName.Clear();

        foreach (var prefab in modelPrefabs)
        {
            if (prefab == null)
            {
                continue;
            }

            var modelName = prefab.name.Trim();
            if (_spawnedModelsByImageName.ContainsKey(modelName))
            {
                Debug.LogWarning($"Duplicate model name '{modelName}' in list. Skipping duplicate.");
                continue;
            }

            var spawnedModel = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            spawnedModel.name = modelName;
            spawnedModel.SetActive(false);

            EnsureRenderersEnabled(spawnedModel);

            if (fixUrpMaterialsOnSpawn)
            {
                FixUrpMaterials(spawnedModel);
            }

            EnsureInteractionCollider(spawnedModel);

            if (spawnedModel.GetComponent<ModelInteractionScript>() == null)
            {
                spawnedModel.AddComponent<ModelInteractionScript>();
            }

            _boneNameDisplay?.RegisterModel(spawnedModel, modelName);

            var rendererCount = spawnedModel.GetComponentsInChildren<Renderer>(true).Length;
            if (rendererCount == 0)
            {
                Debug.LogError($"[AR] Model '{modelName}' has NO mesh renderers. It will never be visible on phone.");
            }
            else
            {
                var bounds = CalculateLocalBounds(spawnedModel.transform);
                Debug.Log($"[AR] Spawned '{modelName}' — {rendererCount} renderer(s), local size {bounds.size}");
            }

            _spawnedModelsByImageName.Add(modelName, spawnedModel);
        }
    }

    private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> eventArgs)
    {
        foreach (var trackedImage in eventArgs.added)
        {
            UpdateSingleTrackedImage(trackedImage);
        }

        foreach (var trackedImage in eventArgs.updated)
        {
            UpdateSingleTrackedImage(trackedImage);
        }

        foreach (var removedPair in eventArgs.removed)
        {
            HideTrackedImageModel(removedPair.Value);
        }
    }

    private void UpdateSingleTrackedImage(ARTrackedImage trackedImage)
    {
        if (trackedImage == null)
        {
            return;
        }

        var imageName = trackedImage.referenceImage.name.Trim();
        if (!_spawnedModelsByImageName.TryGetValue(imageName, out var model))
        {
            Debug.LogWarning(
                $"No model prefab with matching name for reference image '{imageName}'. " +
                "Add a prefab with the exact same name to Model Prefabs.");
            return;
        }

        if (trackedImage.trackingState == TrackingState.Tracking)
        {
            model.SetActive(true);
            EnsureRenderersEnabled(model);
            ApplyPlacement(model.transform, trackedImage);

            if (showDebugCubeOnMarker)
            {
                EnsureDebugCube(imageName, trackedImage.transform);
            }
        }
        else
        {
            var interaction = model.GetComponent<ModelInteractionScript>();
            interaction?.ResetPlacement();
            model.SetActive(false);
        }
    }

    private void ApplyPlacement(Transform modelTransform, ARTrackedImage trackedImage)
    {
        var trackedTransform = trackedImage.transform;
        var interaction = modelTransform.GetComponent<ModelInteractionScript>();

        if (parentToTrackedImage)
        {
            if (modelTransform.parent != trackedTransform)
            {
                modelTransform.SetParent(trackedTransform, worldPositionStays: false);
            }
        }
        else
        {
            if (modelTransform.parent != null)
            {
                modelTransform.SetParent(null, worldPositionStays: true);
            }

            modelTransform.SetPositionAndRotation(trackedTransform.position, trackedTransform.rotation);
        }

        if (interaction != null && interaction.IsPlaced)
        {
            return;
        }

        var baseRotation = Quaternion.Euler(localEulerOffset);
        var localPosition = localPositionOffset;
        var fitScale = uniformScale;

        if (autoFitModelToMarker)
        {
            modelTransform.localRotation = baseRotation;
            modelTransform.localScale = Vector3.one;

            var bounds = CalculateLocalBounds(modelTransform);
            var markerWidth = trackedImage.referenceImage.size.x;
            if (markerWidth <= 0.001f)
            {
                markerWidth = 0.1f;
            }

            var modelSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (modelSize > 0.0001f)
            {
                fitScale = (markerWidth * modelFillRatio) / modelSize * uniformScale;
            }

            localPosition = localPositionOffset - bounds.center;
        }

        if (interaction != null)
        {
            interaction.Initialize(localPosition, baseRotation, fitScale);
        }
        else
        {
            modelTransform.localRotation = baseRotation;
            modelTransform.localPosition = localPosition;
            modelTransform.localScale = Vector3.one * fitScale;
        }
    }

    private static void EnsureInteractionCollider(GameObject root)
    {
        if (root.GetComponentInChildren<Collider>() != null)
        {
            return;
        }

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        var boxCollider = root.AddComponent<BoxCollider>();
        boxCollider.center = root.transform.InverseTransformPoint(bounds.center);
        boxCollider.size = bounds.size;
    }

    private static Bounds CalculateLocalBounds(Transform root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(Vector3.zero, Vector3.one * 0.01f);
        }

        var bounds = TransformBoundsToLocal(root, renderers[0].bounds);
        for (var i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(TransformBoundsToLocal(root, renderers[i].bounds));
        }

        return bounds;
    }

    private static Bounds TransformBoundsToLocal(Transform root, Bounds worldBounds)
    {
        var localCenter = root.InverseTransformPoint(worldBounds.center);
        var localBounds = new Bounds(localCenter, Vector3.zero);
        var extents = worldBounds.extents;

        localBounds.Encapsulate(root.InverseTransformPoint(worldBounds.center + new Vector3(extents.x, extents.y, extents.z)));
        localBounds.Encapsulate(root.InverseTransformPoint(worldBounds.center + new Vector3(extents.x, extents.y, -extents.z)));
        localBounds.Encapsulate(root.InverseTransformPoint(worldBounds.center + new Vector3(extents.x, -extents.y, extents.z)));
        localBounds.Encapsulate(root.InverseTransformPoint(worldBounds.center + new Vector3(extents.x, -extents.y, -extents.z)));
        localBounds.Encapsulate(root.InverseTransformPoint(worldBounds.center + new Vector3(-extents.x, extents.y, extents.z)));
        localBounds.Encapsulate(root.InverseTransformPoint(worldBounds.center + new Vector3(-extents.x, extents.y, -extents.z)));
        localBounds.Encapsulate(root.InverseTransformPoint(worldBounds.center + new Vector3(-extents.x, -extents.y, extents.z)));
        localBounds.Encapsulate(root.InverseTransformPoint(worldBounds.center + new Vector3(-extents.x, -extents.y, -extents.z)));

        return localBounds;
    }

    private static void EnsureRenderersEnabled(GameObject root)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
            renderer.gameObject.SetActive(true);
        }
    }

    private void EnsureDebugCube(string imageName, Transform trackedTransform)
    {
        if (!_debugCubesByImageName.TryGetValue(imageName, out var cube) || cube == null)
        {
            cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"DebugCube_{imageName}";
            var renderer = cube.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = CreateColoredUrpMaterial(Color.red);
            }

            Object.Destroy(cube.GetComponent<Collider>());
            _debugCubesByImageName[imageName] = cube;
        }

        cube.SetActive(true);
        cube.transform.SetParent(trackedTransform, worldPositionStays: false);
        cube.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        cube.transform.localRotation = Quaternion.identity;
        cube.transform.localScale = Vector3.one * 0.05f;
    }

    private void HideTrackedImageModel(ARTrackedImage trackedImage)
    {
        if (trackedImage == null)
        {
            return;
        }

        var imageName = trackedImage.referenceImage.name.Trim();
        if (_spawnedModelsByImageName.TryGetValue(imageName, out var model))
        {
            model.GetComponent<ModelInteractionScript>()?.ResetPlacement();
            model.SetActive(false);
        }

        if (_debugCubesByImageName.TryGetValue(imageName, out var cube) && cube != null)
        {
            cube.SetActive(false);
        }
    }

    private static void FixUrpMaterials(GameObject root)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var sharedMaterials = renderer.sharedMaterials;
            var materials = new Material[sharedMaterials.Length];

            for (var i = 0; i < sharedMaterials.Length; i++)
            {
                materials[i] = NeedsUrpFix(sharedMaterials[i])
                    ? GetFallbackUrpMaterial()
                    : sharedMaterials[i];
            }

            renderer.sharedMaterials = materials;
        }
    }

    private static bool NeedsUrpFix(Material material)
    {
        if (material == null || material.shader == null)
        {
            return true;
        }

        var shaderName = material.shader.name;
        return shaderName == "Hidden/InternalErrorShader"
               || shaderName.StartsWith("Standard")
               || shaderName.StartsWith("Legacy Shaders/");
    }

    private static Material GetFallbackUrpMaterial()
    {
        if (_fallbackUrpMaterial != null)
        {
            return _fallbackUrpMaterial;
        }

        var shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                     ?? Shader.Find("Unlit/Color");

        _fallbackUrpMaterial = new Material(shader)
        {
            color = new Color(0.9f, 0.82f, 0.72f)
        };

        return _fallbackUrpMaterial;
    }

    private static Material CreateColoredUrpMaterial(Color color)
    {
        var material = new Material(GetFallbackUrpMaterial());
        material.color = color;
        return material;
    }
}

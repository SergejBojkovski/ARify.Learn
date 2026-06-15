using UnityEngine;

/// <summary>
/// Per-tooth data attached to each spawned AR model.
/// Populated by BoneNameDisplayScript from Inspector mappings.
/// </summary>
[DisallowMultipleComponent]
public class BoneInfoComponent : MonoBehaviour
{
    [SerializeField] private string imageReferenceName;
    [SerializeField] private string displayName;
    [SerializeField, TextArea(2, 6)] private string description;

    public string ImageReferenceName => imageReferenceName;
    public string DisplayName => displayName;
    public string Description => description;

    public void Initialize(string referenceName, string name, string desc)
    {
        imageReferenceName = referenceName.Trim();
        displayName = string.IsNullOrWhiteSpace(name) ? imageReferenceName : name.Trim();
        description = desc ?? string.Empty;
    }
}

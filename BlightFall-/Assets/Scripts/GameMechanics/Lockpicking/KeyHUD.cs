using UnityEngine;

public class KeyHUD : MonoBehaviour
{
    public static KeyHUD Instance { get; private set; }

    [Header("UI Element")]
    [Tooltip("Drag the Key Icon / Image panel child here")]
    [SerializeField] private GameObject keyDisplay;

    private void Awake()
    {
        Instance = this;
        SetKeyVisible(false);
    }

    public void SetKeyVisible(bool visible)
    {
        if (keyDisplay != null)
        {
            keyDisplay.SetActive(visible);
            Debug.Log($"[KeyHUD] Key UI set to: {visible}");
        }
        else
        {
            Debug.LogError("[KeyHUD] 'Key Display' is NOT assigned in the Inspector on KeyHUD!", this);
        }
    }
}
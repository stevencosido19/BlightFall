using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class DamageFlashUI : MonoBehaviour
{
    public static DamageFlashUI Instance { get; private set; }

    [Header("UI Element")]
    [Tooltip("Drag the full-screen red Image or CanvasGroup here")]
    [SerializeField] private Image damageImage;

    [Header("Flash Settings")]
    [Range(0f, 1f)]
    [SerializeField] private float maxAlpha = 0.45f;    // Maximum redness intensity
    [SerializeField] private float fadeInTime = 0.05f;   // Snap in fast
    [SerializeField] private float fadeOutTime = 0.35f;  // Fade out smoothly

    private Coroutine flashRoutine;

    private void Awake()
    {
        Instance = this;

        if (damageImage != null)
        {
            // Ensure alpha starts completely transparent (0)
            SetAlpha(0f);
        }
    }

    public void Flash()
    {
        if (damageImage == null) return;

        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
        }

        flashRoutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        // 1. Fast Fade In (Quick hit impact)
        float elapsed = 0f;
        float startAlpha = damageImage.color.a;

        while (elapsed < fadeInTime)
        {
            elapsed += Time.deltaTime;
            SetAlpha(Mathf.Lerp(startAlpha, maxAlpha, elapsed / fadeInTime));
            yield return null;
        }

        SetAlpha(maxAlpha);

        // 2. Smooth Fade Out
        elapsed = 0f;
        while (elapsed < fadeOutTime)
        {
            elapsed += Time.deltaTime;
            SetAlpha(Mathf.Lerp(maxAlpha, 0f, elapsed / fadeOutTime));
            yield return null;
        }

        SetAlpha(0f);
        flashRoutine = null;
    }

    private void SetAlpha(float alpha)
    {
        if (damageImage != null)
        {
            Color c = damageImage.color;
            c.a = alpha;
            damageImage.color = c;
        }
    }
}
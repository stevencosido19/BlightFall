using UnityEngine;

public class HideInDarkness : MonoBehaviour
{
    [Header("Settings")]
    public LayerMask wallLayer;             // Set to your "Walls" layer
    public float fadeSpeed = 8f;
    public float visibleRangeOffset = -0.5f;

    [Header("Mobile Optimization")]
    [Tooltip("How often (in seconds) to check distance & walls. 0.08 = ~12 checks/sec.")]
    public float checkInterval = 0.08f;

    private SpriteRenderer[] spriteRenderers;
    private float currentAlpha = 0f;
    private float targetAlpha = 0f;
    private float nextCheckTime;

    private void Awake()
    {
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        ResetToHidden();
    }

    private void OnEnable()
    {
        ResetToHidden();
        nextCheckTime = Time.time + Random.Range(0f, checkInterval);
    }

    private void Start()
    {
        // Runs after other scripts' Awake/OnEnable in case a spawner enabled the sprite
        UpdateTargetVisibility();
        if (targetAlpha <= 0.005f)
            ResetToHidden();
    }

    private void Update()
    {
        if (Time.time >= nextCheckTime)
        {
            nextCheckTime = Time.time + checkInterval;
            UpdateTargetVisibility();

            // Guarantee hidden zombies stay disabled even if an Animator/script re-enables them
            if (targetAlpha <= 0.005f && currentAlpha <= 0.005f)
                EnsureDisabled();
        }

        if (!Mathf.Approximately(currentAlpha, targetAlpha))
        {
            currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, fadeSpeed * Time.deltaTime);
            ApplyVisuals(currentAlpha);
        }
    }

    private void ResetToHidden()
    {
        currentAlpha = 0f;
        targetAlpha = 0f;
        ApplyVisuals(0f);
    }

    private void UpdateTargetVisibility()
    {
        targetAlpha = 0f;
        Vector3 zombieCenter = transform.position + Vector3.up * 0.5f;

        for (int i = 0; i < TorchFlicker.ActiveTorches.Count; i++)
        {
            TorchFlicker torchSource = TorchFlicker.ActiveTorches[i];
            if (torchSource == null || torchSource.TorchLight == null || !torchSource.TorchLight.enabled)
                continue;

            Light torch = torchSource.TorchLight;
            float maxRange = torch.range + visibleRangeOffset;
            if (maxRange <= 0f) continue;

            Vector3 lightPos = torch.transform.position;
            Vector3 toZombie = zombieCenter - lightPos;
            float sqrDist = toZombie.sqrMagnitude;

            if (sqrDist <= maxRange * maxRange)
            {
                float dist = Mathf.Sqrt(sqrDist);
                bool inCone = true;

                if (torch.type == LightType.Spot && dist > 0.001f)
                {
                    Vector3 dirToZombie = toZombie / dist;
                    if (Vector3.Angle(torch.transform.forward, dirToZombie) > torch.spotAngle * 0.5f)
                        inCone = false;
                }

                if (inCone && !Physics.Linecast(lightPos, zombieCenter, wallLayer))
                {
                    float alpha = Mathf.Clamp01(1f - (dist / maxRange) * 0.6f);
                    if (alpha > targetAlpha)
                        targetAlpha = alpha;
                }
            }
        }
    }

    private void ApplyVisuals(float alpha)
    {
        if (spriteRenderers == null) return;

        bool shouldEnable = alpha > 0.005f;

        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            SpriteRenderer sr = spriteRenderers[i];
            if (sr == null) continue;

            Color c = sr.color;
            c.a = shouldEnable ? alpha : 0f;
            c.r = c.g = c.b = Mathf.Lerp(0.2f, 1f, alpha);
            sr.color = c;

            if (sr.enabled != shouldEnable)
                sr.enabled = shouldEnable;
        }
    }

    private void EnsureDisabled()
    {
        if (spriteRenderers == null) return;

        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] != null && spriteRenderers[i].enabled)
                spriteRenderers[i].enabled = false;
        }
    }
}
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class TransparentWallManager : MonoBehaviour
{
    public enum FadeDetectionMode
    {
        NearPlayer,      // Fades any TransparentWall within radius of the local player
        BlockingCamera,  // Fades only TransparentWalls between Camera and local player
        Both             // Combines both
    }

    [Header("Detection Settings")]
    [SerializeField] private FadeDetectionMode detectionMode = FadeDetectionMode.NearPlayer;
    [SerializeField] private string wallTag = "TransparentWall";
    [SerializeField] private LayerMask wallLayerMask = ~0; // Default: Everything

    [Header("Near Player Mode")]
    [SerializeField] private float nearPlayerRadius = 3.0f;

    [Header("Blocking Camera Mode")]
    [SerializeField] private float cameraCastRadius = 0.5f;

    [Header("Fade Settings")]
    [Range(0f, 1f)]
    [SerializeField] private float transparentAlpha = 0.25f;
    [SerializeField] private float fadeSpeed = 8f;

    private class WallFadeData
    {
        public Renderer Renderer;
        public Material[] OriginalMaterials;
        public Material[] FadedMaterials;
        public float CurrentAlpha = 1f;
        public bool IsTargetedThisFrame = false;
    }

    private readonly Dictionary<Renderer, WallFadeData> wallDataMap = new Dictionary<Renderer, WallFadeData>();
    private readonly List<Renderer> activeFadingRenderers = new List<Renderer>();
    private readonly Collider[] overlapBuffer = new Collider[64];

    private Transform localPlayer;
    private Camera mainCam;
    private float nextPlayerSearchTime = 0f;
    private bool missingTagWarned = false;

    private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProp = Shader.PropertyToID("_Color");
    private static readonly int SurfaceProp = Shader.PropertyToID("_Surface");
    private static readonly int SrcBlendProp = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendProp = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteProp = Shader.PropertyToID("_ZWrite");

    private void Awake()
    {
        mainCam = GetComponent<Camera>();
        if (mainCam == null) mainCam = Camera.main;
    }

    private void OnDestroy()
    {
        foreach (var data in wallDataMap.Values)
        {
            if (data.FadedMaterials == null) continue;
            for (int i = 0; i < data.FadedMaterials.Length; i++)
            {
                if (data.FadedMaterials[i] != null)
                {
                    Destroy(data.FadedMaterials[i]);
                }
            }
        }
        wallDataMap.Clear();
        activeFadingRenderers.Clear();
    }

    private void Update()
    {
        FindLocalPlayerIfNeeded();
        if (localPlayer == null) return;

        // 1. Reset frame flags on currently active fading walls
        for (int i = 0; i < activeFadingRenderers.Count; i++)
        {
            if (wallDataMap.TryGetValue(activeFadingRenderers[i], out WallFadeData data))
            {
                data.IsTargetedThisFrame = false;
            }
        }

        // 2. Detect walls to fade
        Vector3 playerCenter = localPlayer.position + Vector3.up;

        if (detectionMode == FadeDetectionMode.NearPlayer || detectionMode == FadeDetectionMode.Both)
        {
            int hitCount = Physics.OverlapSphereNonAlloc(playerCenter, nearPlayerRadius, overlapBuffer, wallLayerMask);
            for (int i = 0; i < hitCount; i++)
            {
                MarkColliderIfWall(overlapBuffer[i]);
            }
        }

        if ((detectionMode == FadeDetectionMode.BlockingCamera || detectionMode == FadeDetectionMode.Both) && mainCam != null)
        {
            Vector3 camPos = mainCam.transform.position;
            Vector3 toPlayer = playerCenter - camPos;
            float dist = toPlayer.magnitude;

            if (dist > 0.1f)
            {
                RaycastHit[] hits = Physics.SphereCastAll(camPos, cameraCastRadius, toPlayer.normalized, dist, wallLayerMask);
                for (int i = 0; i < hits.Length; i++)
                {
                    MarkColliderIfWall(hits[i].collider);
                }
            }
        }

        // 3. Smoothly fade targeted walls, and restore original materials when fully solid again
        for (int i = activeFadingRenderers.Count - 1; i >= 0; i--)
        {
            Renderer rend = activeFadingRenderers[i];
            if (rend == null || !wallDataMap.TryGetValue(rend, out WallFadeData data))
            {
                activeFadingRenderers.RemoveAt(i);
                continue;
            }

            float targetAlpha = data.IsTargetedThisFrame ? transparentAlpha : 1f;
            data.CurrentAlpha = Mathf.MoveTowards(data.CurrentAlpha, targetAlpha, Time.deltaTime * fadeSpeed);

            if (data.CurrentAlpha < 0.999f)
            {
                if (rend.sharedMaterials != data.FadedMaterials)
                {
                    rend.sharedMaterials = data.FadedMaterials;
                }
                SetMaterialsAlpha(data.FadedMaterials, data.CurrentAlpha);
            }
            else if (!data.IsTargetedThisFrame)
            {
                // Back to 100% solid -> restore original opaque materials & remove from active list!
                data.CurrentAlpha = 1f;
                rend.sharedMaterials = data.OriginalMaterials;
                activeFadingRenderers.RemoveAt(i);
            }
        }
    }

    private void FindLocalPlayerIfNeeded()
    {
        if (localPlayer != null) return;

        if (PlayerHealth.LocalPlayerInstance != null)
        {
            localPlayer = PlayerHealth.LocalPlayerInstance.transform;
            return;
        }

        if (Time.time < nextPlayerSearchTime) return;
        nextPlayerSearchTime = Time.time + 0.5f;

        PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        foreach (var p in players)
        {
            if (p != null && p.IsOwner)
            {
                localPlayer = p.transform;
                break;
            }
        }
    }

    private void MarkColliderIfWall(Collider col)
    {
        if (col == null) return;

        bool matchesTag = false;
        try
        {
            matchesTag = col.CompareTag(wallTag);
        }
        catch (UnityException)
        {
            if (!missingTagWarned)
            {
                missingTagWarned = true;
                Debug.LogError($"[TransparentWallManager] Tag '{wallTag}' does not exist in Unity! Go to Tag -> Add Tag... and create '{wallTag}'.");
            }
            return;
        }

        if (!matchesTag) return;

        Renderer[] renderers = col.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer rend = renderers[i];
            if (rend == null) continue;

            if (!wallDataMap.TryGetValue(rend, out WallFadeData data))
            {
                data = CreateFadeData(rend);
                wallDataMap[rend] = data;
            }

            data.IsTargetedThisFrame = true;
            if (!activeFadingRenderers.Contains(rend))
            {
                activeFadingRenderers.Add(rend);
            }
        }
    }

    private WallFadeData CreateFadeData(Renderer rend)
    {
        Material[] originals = rend.sharedMaterials;
        Material[] faded = new Material[originals.Length];

        for (int i = 0; i < originals.Length; i++)
        {
            if (originals[i] == null) continue;
            Material clone = new Material(originals[i]);
            clone.name = $"{originals[i].name}_FadeClone";
            MakeMaterialTransparent(clone);
            faded[i] = clone;
        }

        return new WallFadeData
        {
            Renderer = rend,
            OriginalMaterials = originals,
            FadedMaterials = faded,
            CurrentAlpha = 1f
        };
    }

    private void MakeMaterialTransparent(Material mat)
    {
        if (mat.HasProperty(SurfaceProp))
        {
            mat.SetFloat(SurfaceProp, 1f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt(SrcBlendProp, (int)BlendMode.SrcAlpha);
            mat.SetInt(DstBlendProp, (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt(ZWriteProp, 0);
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
    }

    private void SetMaterialsAlpha(Material[] mats, float alpha)
    {
        for (int i = 0; i < mats.Length; i++)
        {
            Material mat = mats[i];
            if (mat == null) continue;

            if (mat.HasProperty(BaseColorProp))
            {
                Color c = mat.GetColor(BaseColorProp);
                c.a = alpha;
                mat.SetColor(BaseColorProp, c);
            }
            else if (mat.HasProperty(ColorProp))
            {
                Color c = mat.GetColor(ColorProp);
                c.a = alpha;
                mat.SetColor(ColorProp, c);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (localPlayer != null && (detectionMode == FadeDetectionMode.NearPlayer || detectionMode == FadeDetectionMode.Both))
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
            Gizmos.DrawWireSphere(localPlayer.position + Vector3.up, nearPlayerRadius);
        }
    }
}
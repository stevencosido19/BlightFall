using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class WallFader : MonoBehaviour
{
    [Header("Fade Settings")]
    [Range(0f, 1f)]
    [SerializeField] private float transparentAlpha = 0.25f;
    [SerializeField] private float fadeSpeed = 7f;

    [Header("Group Collider Settings")]
    [SerializeField] private bool combineIntoOneMeshCollider = true;
    [SerializeField] private bool disableChildColliders = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private Material[] clonedMaterials;
    private float currentAlpha = 1f;
    private bool isObstructing = false;
    private bool isCurrentlyTransparent = false;

    private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProp = Shader.PropertyToID("_Color");
    private static readonly int SurfaceProp = Shader.PropertyToID("_Surface");
    private static readonly int SrcBlendProp = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendProp = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteProp = Shader.PropertyToID("_ZWrite");

    private void Awake()
    {
        if (combineIntoOneMeshCollider)
        {
            CombineChildMeshesIntoCollider();
        }
        else if (disableChildColliders)
        {
            DisableChildCollidersOnly();
        }

        // Share 1 cloned material per unique material inside this group (saves memory & draw calls!)
        Renderer[] wallRenderers = GetComponentsInChildren<Renderer>();
        Dictionary<Material, Material> sharedGroupClones = new Dictionary<Material, Material>();
        List<Material> uniqueClones = new List<Material>();

        foreach (Renderer rend in wallRenderers)
        {
            Material[] mats = rend.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < mats.Length; i++)
            {
                Material original = mats[i];
                if (original == null) continue;

                if (!sharedGroupClones.TryGetValue(original, out Material clone))
                {
                    clone = new Material(original);
                    clone.name = $"{original.name}_FadeInstance";
                    sharedGroupClones[original] = clone;
                    uniqueClones.Add(clone);
                }

                mats[i] = clone;
                changed = true;
            }

            if (changed)
            {
                rend.sharedMaterials = mats;
            }
        }

        clonedMaterials = uniqueClones.ToArray();

        if (debugLogs)
        {
            Debug.Log($"[WallFader] '{gameObject.name}' initialized with {wallRenderers.Length} renderers sharing {clonedMaterials.Length} unique cloned material(s).");
        }

        // Sleep until the camera actually hits this wall group!
        enabled = false;
    }

    private void OnDestroy()
    {
        if (clonedMaterials == null) return;
        for (int i = 0; i < clonedMaterials.Length; i++)
        {
            if (clonedMaterials[i] != null)
            {
                Destroy(clonedMaterials[i]);
            }
        }
    }

    private void CombineChildMeshesIntoCollider()
    {
        MeshFilter[] childFilters = GetComponentsInChildren<MeshFilter>();
        List<CombineInstance> combineList = new List<CombineInstance>();
        Matrix4x4 parentWorldToLocal = transform.worldToLocalMatrix;

        foreach (MeshFilter mf in childFilters)
        {
            if (mf.gameObject == gameObject || mf.sharedMesh == null) continue;

            if (!mf.sharedMesh.isReadable)
            {
                Debug.LogWarning($"[WallFader] Mesh '{mf.sharedMesh.name}' on '{mf.gameObject.name}' is NOT Read/Write enabled!");
                continue;
            }

            combineList.Add(new CombineInstance
            {
                mesh = mf.sharedMesh,
                transform = parentWorldToLocal * mf.transform.localToWorldMatrix
            });
        }

        if (combineList.Count > 0)
        {
            Mesh combinedMesh = new Mesh();
            combinedMesh.name = $"{gameObject.name}_CombinedWallCollider";
            combinedMesh.indexFormat = IndexFormat.UInt32;
            combinedMesh.CombineMeshes(combineList.ToArray(), true, true);

            MeshCollider meshCol = GetComponent<MeshCollider>();
            if (meshCol == null)
            {
                meshCol = gameObject.AddComponent<MeshCollider>();
            }
            meshCol.sharedMesh = null;
            meshCol.sharedMesh = combinedMesh;
        }

        if (disableChildColliders)
        {
            DisableChildCollidersOnly();
        }
    }

    private void DisableChildCollidersOnly()
    {
        foreach (Collider col in GetComponentsInChildren<Collider>())
        {
            if (col.gameObject == gameObject) continue;
            col.enabled = false;
        }
    }

    // Wakes up Update() only while obstructing the player
    public void NotifyObstructing()
    {
        if (debugLogs && !isObstructing)
        {
            Debug.Log($"<color=cyan>[WallFader]</color> '{gameObject.name}' hit by camera raycast!");
        }

        isObstructing = true;
        if (!enabled)
        {
            enabled = true;
        }
    }

    private void Update()
    {
        float targetAlpha = isObstructing ? transparentAlpha : 1f;
        currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, Time.deltaTime * fadeSpeed);

        bool shouldBeTransparent = currentAlpha < 0.99f;
        if (shouldBeTransparent != isCurrentlyTransparent)
        {
            isCurrentlyTransparent = shouldBeTransparent;
            SetMaterialsRenderMode(shouldBeTransparent);
        }

        ApplyAlphaToMaterials(currentAlpha);

        // Once fully solid again and not obstructed, go back to sleep (0 CPU cost)
        if (!isObstructing && currentAlpha >= 0.999f)
        {
            currentAlpha = 1f;
            ApplyAlphaToMaterials(1f);
            isCurrentlyTransparent = false;
            SetMaterialsRenderMode(false);
            enabled = false;
        }

        isObstructing = false;
    }

    private void ApplyAlphaToMaterials(float alpha)
    {
        for (int i = 0; i < clonedMaterials.Length; i++)
        {
            Material mat = clonedMaterials[i];
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

    private void SetMaterialsRenderMode(bool transparent)
    {
        for (int i = 0; i < clonedMaterials.Length; i++)
        {
            Material mat = clonedMaterials[i];
            if (mat == null || !mat.HasProperty(SurfaceProp)) continue;

            if (transparent)
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
            else
            {
                mat.SetFloat(SurfaceProp, 0f);
                mat.SetOverrideTag("RenderType", "Opaque");
                mat.SetInt(SrcBlendProp, (int)BlendMode.One);
                mat.SetInt(DstBlendProp, (int)BlendMode.Zero);
                mat.SetInt(ZWriteProp, 1);
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)RenderQueue.Geometry;
            }
        }
    }
}
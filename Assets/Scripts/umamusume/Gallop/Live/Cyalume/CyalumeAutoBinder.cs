using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Gallop.Live;
using UnityEngine;
using UnityEngine.Rendering;

public class CyalumeAutoBinder : MonoBehaviour
{
    [Header("Optional. Leave 0 to resolve from Director.instance.live.MusicId.")]
    public int musicId = 0;

    [Header("Only touch renderers whose name contains cyalume.")]
    public bool onlyNameContainsCyalume = true;

    [Header("Optional shader name filter. Leave empty to accept any shader.")]
    public string shaderNameContains = "Cyalume";

    [Header("Wait a little for the live stage to instantiate.")]
    public float waitForStageSeconds = 0.2f;

    [Header("Re-apply for a short period so later writes do not wipe the fix.")]
    public float rebindDuration = 2.0f;
    public float rebindInterval = 0.1f;

    [Header("Animate the glowstick flipbook on every bound material.")]
    public bool animateAllBoundMaterials = true;
    public string scrollPropertyName = "_MainTex";
    public float scrollInterval = 1f / 30f;
    public float scrollStep = 1f / 32f;

    [Header("Repair dark *_front renderers.")]
    public bool fixFrontRenderers = true;
    public bool copyBackPropertyBlockToFront = true;
    public bool overrideFrontVertexColor = true;
    public bool overrideFrontUv2 = true;

    private sealed class BoundMaterial
    {
        public Renderer Renderer;
        public int MaterialIndex;
        public Material Material;
    }

    private readonly Dictionary<int, Mesh> _frontStreamCache = new Dictionary<int, Mesh>();
    private readonly List<BoundMaterial> _boundMaterials = new List<BoundMaterial>();

    private Dictionary<string, Texture2D> _texSet;
    private MaterialPropertyBlock _mpb;
    private float _scrollElapsed;
    private float _scrollOffset;
    private int _scrollStPropertyId;

    private IEnumerator Start()
    {
        _mpb = new MaterialPropertyBlock();
        _scrollStPropertyId = string.IsNullOrEmpty(scrollPropertyName)
            ? -1
            : Shader.PropertyToID(scrollPropertyName + "_ST");

        Debug.Log("[CyalumeAutoBinder] Start()");

        if (musicId <= 0 && Director.instance != null && Director.instance.live != null)
        {
            musicId = Director.instance.live.MusicId;
        }

        if (musicId <= 0)
        {
            Debug.LogWarning("[CyalumeAutoBinder] musicId is unavailable. Make sure Director is initialized in LiveScene.");
            yield break;
        }

        string liveKey = $"m{musicId}";
        Debug.Log($"[CyalumeAutoBinder] musicId={musicId}, liveKey={liveKey}");

        TryLoadTextureBundlesFromIndex(liveKey);
        yield return new WaitForSeconds(waitForStageSeconds);

        _texSet = ResolveTexSetFromLoadedBundles(liveKey);
        if (_texSet.Count == 0)
        {
            Debug.LogWarning($"[CyalumeAutoBinder] No tex_live_cyalume_{liveKey}_*** texture set was found.");
            yield break;
        }

        float timeout = 5f;
        while (timeout > 0f && CountTargetRenderers() == 0)
        {
            yield return null;
            timeout -= Time.deltaTime;
        }

        Debug.Log($"[CyalumeAutoBinder] targetRenderers={CountTargetRenderers()}");

        int lastBound = ApplyToScene(_texSet);
        float elapsed = 0f;
        while (elapsed < rebindDuration)
        {
            yield return new WaitForSeconds(rebindInterval);
            elapsed += rebindInterval;
            lastBound = ApplyToScene(_texSet);
        }

        Debug.Log($"[CyalumeAutoBinder] Done. boundSlots={lastBound}, animatedMaterials={_boundMaterials.Count}");
    }

    private void Update()
    {
        if (!animateAllBoundMaterials || _boundMaterials.Count == 0 || string.IsNullOrEmpty(scrollPropertyName))
        {
            return;
        }

        _scrollElapsed += Time.deltaTime;
        if (_scrollElapsed < scrollInterval)
        {
            return;
        }

        while (_scrollElapsed >= scrollInterval)
        {
            _scrollElapsed -= scrollInterval;
            _scrollOffset += scrollStep;
        }

        _scrollOffset -= Mathf.Floor(_scrollOffset);
        ApplyScrollOffset();
    }

    private void TryLoadTextureBundlesFromIndex(string liveKey)
    {
        var main = FindObjectOfType<UmaViewerMain>();
        if (main == null || main.AbList == null)
        {
            return;
        }

        string needle = $"tex_live_cyalume_{liveKey}_";
        var entries = main.AbList.Values
            .Where(e => e != null &&
                        !string.IsNullOrEmpty(e.Name) &&
                        e.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        Debug.Log($"[CyalumeAutoBinder] textureEntriesInIndex={entries.Count}");

        foreach (var entry in entries)
        {
            UmaAssetManager.LoadAssetBundle(entry, neverUnload: true, isRecursive: true);
        }
    }

    private Dictionary<string, Texture2D> ResolveTexSetFromLoadedBundles(string liveKey)
    {
        var result = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        foreach (var assetBundle in AssetBundle.GetAllLoadedAssetBundles())
        {
            if (!assetBundle)
            {
                continue;
            }

            string[] names;
            try
            {
                names = assetBundle.GetAllAssetNames();
            }
            catch
            {
                continue;
            }

            foreach (var path in names)
            {
                var file = path.Substring(path.LastIndexOf('/') + 1);
                if (!file.StartsWith("tex_live_cyalume_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = file.Split('_');
                if (parts.Length < 5)
                {
                    continue;
                }

                var key = parts[3];
                var suffix = parts[4];
                if (!string.Equals(key, liveKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (result.ContainsKey(suffix))
                {
                    continue;
                }

                var texture = assetBundle.LoadAsset<Texture2D>(path);
                if (texture)
                {
                    result[suffix] = texture;
                }
            }
        }

        Debug.Log($"[CyalumeAutoBinder] foundSuffix=[{string.Join(",", result.Keys.OrderBy(x => x))}]");
        return result;
    }

    private int CountTargetRenderers()
    {
        int count = 0;
        foreach (var renderer in FindObjectsOfType<Renderer>(true))
        {
            if (IsTargetRenderer(renderer))
            {
                count++;
            }
        }

        return count;
    }

    private static string ExtractSuffix(string name)
    {
        var match = Regex.Match(name, @"cyalume_[dr](\d{3})", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "000";
    }

    private int ApplyToScene(Dictionary<string, Texture2D> texSet)
    {
        _boundMaterials.Clear();

        int boundSlots = 0;
        var renderers = FindObjectsOfType<Renderer>(true);
        foreach (var renderer in renderers)
        {
            if (!IsTargetRenderer(renderer))
            {
                continue;
            }

            var texture = ResolveTextureForRenderer(renderer, texSet);
            if (!texture)
            {
                continue;
            }

            boundSlots += BindRenderer(renderer, texture);
        }

        if (fixFrontRenderers)
        {
            RepairFrontRenderers(renderers);
        }

        if (animateAllBoundMaterials)
        {
            ApplyScrollOffset();
        }

        return boundSlots;
    }

    private Texture2D ResolveTextureForRenderer(Renderer renderer, Dictionary<string, Texture2D> texSet)
    {
        var suffix = ExtractSuffix(renderer.gameObject.name);
        if (texSet.TryGetValue(suffix, out var exact) && exact)
        {
            return exact;
        }

        if (texSet.TryGetValue("000", out var fallback) && fallback)
        {
            return fallback;
        }

        return texSet.Values.FirstOrDefault(x => x);
    }

    private int BindRenderer(Renderer renderer, Texture2D texture)
    {
        int boundSlots = 0;
        var materials = renderer.materials;

        for (int i = 0; i < materials.Length; i++)
        {
            var material = materials[i];
            if (!IsTargetMaterial(material))
            {
                continue;
            }

            var textureProperties = material.GetTexturePropertyNames();
            if (textureProperties == null || textureProperties.Length == 0)
            {
                continue;
            }

            foreach (var propertyName in textureProperties)
            {
                material.SetTexture(propertyName, texture);
            }

#if UNITY_2021_2_OR_NEWER
            renderer.GetPropertyBlock(_mpb, i);
            foreach (var propertyName in textureProperties)
            {
                _mpb.SetTexture(propertyName, texture);
            }
            renderer.SetPropertyBlock(_mpb, i);
#else
            renderer.GetPropertyBlock(_mpb);
            foreach (var propertyName in textureProperties)
            {
                _mpb.SetTexture(propertyName, texture);
            }
            renderer.SetPropertyBlock(_mpb);
#endif

            if (animateAllBoundMaterials &&
                !string.IsNullOrEmpty(scrollPropertyName) &&
                material.HasProperty(scrollPropertyName))
            {
                _boundMaterials.Add(new BoundMaterial
                {
                    Renderer = renderer,
                    MaterialIndex = i,
                    Material = material,
                });
            }

            boundSlots++;
        }

        return boundSlots;
    }

    private void RepairFrontRenderers(Renderer[] renderers)
    {
        foreach (var renderer in renderers)
        {
            var frontRenderer = renderer as MeshRenderer;
            if (frontRenderer == null || !IsTargetRenderer(frontRenderer))
            {
                continue;
            }

            if (!frontRenderer.name.EndsWith("_front", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (copyBackPropertyBlockToFront)
            {
                var backRenderer = FindMatchingBackRenderer(frontRenderer);
                if (backRenderer != null)
                {
                    CopyPropertyBlocks(backRenderer, frontRenderer);
                }
            }

            ApplyFrontAdditionalVertexStreams(frontRenderer);
        }
    }

    private MeshRenderer FindMatchingBackRenderer(MeshRenderer frontRenderer)
    {
        var parent = frontRenderer.transform.parent;
        if (!parent)
        {
            return null;
        }

        var backName = frontRenderer.name.Substring(0, frontRenderer.name.Length - "_front".Length) + "_back";
        var backTransform = parent.Find(backName);
        if (!backTransform)
        {
            return null;
        }

        var backRenderer = backTransform.GetComponent<MeshRenderer>();
        return backRenderer != null && IsTargetRenderer(backRenderer) ? backRenderer : null;
    }

    private void CopyPropertyBlocks(Renderer source, Renderer destination)
    {
        int slotCount = Mathf.Min(source.sharedMaterials.Length, destination.sharedMaterials.Length);
        if (slotCount <= 0)
        {
            return;
        }

        for (int i = 0; i < slotCount; i++)
        {
#if UNITY_2021_2_OR_NEWER
            source.GetPropertyBlock(_mpb, i);
            destination.SetPropertyBlock(_mpb, i);
#else
            source.GetPropertyBlock(_mpb);
            destination.SetPropertyBlock(_mpb);
            break;
#endif
        }
    }

    private void ApplyFrontAdditionalVertexStreams(MeshRenderer frontRenderer)
    {
        if (!overrideFrontVertexColor && !overrideFrontUv2)
        {
            return;
        }

        var meshFilter = frontRenderer.GetComponent<MeshFilter>();
        if (!meshFilter || !meshFilter.sharedMesh)
        {
            return;
        }

        int vertexCount = meshFilter.sharedMesh.vertexCount;
        if (vertexCount <= 0)
        {
            return;
        }

        if (!_frontStreamCache.TryGetValue(vertexCount, out var stream) || !stream)
        {
            stream = BuildFrontStream(vertexCount);
            if (!stream)
            {
                return;
            }

            _frontStreamCache[vertexCount] = stream;
        }

        if (frontRenderer.additionalVertexStreams != stream)
        {
            frontRenderer.additionalVertexStreams = stream;
        }
    }

    private Mesh BuildFrontStream(int vertexCount)
    {
        var descriptors = new List<VertexAttributeDescriptor>();
        int streamIndex = 0;
        int colorStream = -1;
        int uv2Stream = -1;

        if (overrideFrontVertexColor)
        {
            colorStream = streamIndex++;
            descriptors.Add(new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, colorStream));
        }

        if (overrideFrontUv2)
        {
            uv2Stream = streamIndex++;
            descriptors.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2, uv2Stream));
        }

        if (descriptors.Count == 0)
        {
            return null;
        }

        var stream = new Mesh
        {
            name = $"CyalumeFrontStream_vc{vertexCount}"
        };

        stream.SetVertexBufferParams(vertexCount, descriptors.ToArray());

        if (overrideFrontVertexColor)
        {
            var colors = new Color32[vertexCount];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = new Color32(255, 255, 255, 255);
            }

            stream.SetVertexBufferData(colors, 0, 0, vertexCount, colorStream, MeshUpdateFlags.DontRecalculateBounds);
        }

        if (overrideFrontUv2)
        {
            var uv2 = new Vector2[vertexCount];
            for (int i = 0; i < uv2.Length; i++)
            {
                uv2[i] = Vector2.one;
            }

            stream.SetVertexBufferData(uv2, 0, 0, vertexCount, uv2Stream, MeshUpdateFlags.DontRecalculateBounds);
        }

        return stream;
    }

    private void ApplyScrollOffset()
    {
        if (string.IsNullOrEmpty(scrollPropertyName))
        {
            return;
        }

        for (int i = 0; i < _boundMaterials.Count; i++)
        {
            var bound = _boundMaterials[i];
            if (bound == null || !bound.Renderer || !bound.Material || !bound.Material.HasProperty(scrollPropertyName))
            {
                continue;
            }

            bound.Material.SetTextureOffset(scrollPropertyName, new Vector2(0f, _scrollOffset));

#if UNITY_2021_2_OR_NEWER
            if (_scrollStPropertyId >= 0)
            {
                var scale = bound.Material.GetTextureScale(scrollPropertyName);
                bound.Renderer.GetPropertyBlock(_mpb, bound.MaterialIndex);
                _mpb.SetVector(_scrollStPropertyId, new Vector4(scale.x, scale.y, 0f, _scrollOffset));
                bound.Renderer.SetPropertyBlock(_mpb, bound.MaterialIndex);
            }
#endif
        }
    }

    private bool IsTargetRenderer(Renderer renderer)
    {
        if (!renderer)
        {
            return false;
        }

        if (onlyNameContainsCyalume &&
            renderer.gameObject.name.IndexOf("cyalume", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        foreach (var material in renderer.sharedMaterials)
        {
            if (IsTargetMaterial(material))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsTargetMaterial(Material material)
    {
        if (!material || !material.shader)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(shaderNameContains) &&
            material.shader.name.IndexOf(shaderNameContains, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        return true;
    }
}

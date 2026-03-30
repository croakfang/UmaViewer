using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Gallop.Live;
using Gallop.Live.Cutt;
using Gallop.Live.Cyalume;
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

    [Header("Re-apply for a short period to catch late-spawned renderers. Keep this small.")]
    public float rebindDuration = 0.0f;
    public float rebindInterval = 0.25f;

    [Header("Animate the glowstick flipbook on every bound material.")]
    public bool animateAllBoundMaterials = true;
    public string scrollPropertyName = "_MainTex";
    public float legacyScrollInterval = 1f / 30f;
    public float legacyScrollStep = 1f / 32f;
    public float recoveredFrameTime = 40f;
    public int recoveredAnimationFrameCount = 32;
    public bool useGlobalPatternFromPlayback = true;
    public bool verboseTextureLog = false;

    [Header("Repair dark *_front renderers.")]
    public bool fixFrontRenderers = true;
    public bool copyBackPropertyBlockToFront = true;

    [Header("Mirror back mesh vertex colors onto *_front runtime meshes.")]
    public bool mirrorBackVertexColorsToFront = true;
    public bool cloneFrontMeshBeforeWritingColors = true;
    public bool fallbackFrontToSolidWhiteVertexColor = true;
    public bool verboseFrontRepairLog = false;

    public bool overrideFrontVertexColor = false;
    public bool overrideFrontUv2 = false;

    [Header("Optional timeline transform control recovered from mob/cyalume keys.")]
    public bool applyTimelineTransformControl = true;
    public bool applyMobControlList = true;
    public bool applyCyalumeControlList = true;
    public float transformMapRefreshInterval = 1f;
    public bool verboseTimelineLog = false;

    [Header("Optional recovered playback data source.")]
    public Gallop.Live.Cyalume.CyalumePlaybackProvider playbackProvider;
    public bool useRecoveredPlaybackState = true;
    private sealed class BoundMaterial
    {
        public Renderer Renderer;
        public int MaterialIndex;
        public Material Material;
        public string TexturePropertyName;
        public int DefaultPatternId;
        public int LastAppliedPatternId = -1;
    }

    private static readonly string[] kPreferredTextureProps = { "_MainTex", "_BaseMap" };

    private readonly Dictionary<string, Mesh> _frontStreamCache = new Dictionary<string, Mesh>();
    private readonly List<BoundMaterial> _boundMaterials = new List<BoundMaterial>();
    private readonly Dictionary<string, BoundMaterial> _boundMaterialsByKey = new Dictionary<string, BoundMaterial>();
    private readonly Dictionary<string, List<Transform>> _sceneTransformsByName = new Dictionary<string, List<Transform>>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missingTimelineTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<MeshFilter, Mesh> _runtimeFrontMeshCache = new Dictionary<MeshFilter, Mesh>();
    private int _lastAppliedGlobalPatternId = -1;

    private Dictionary<int, Texture2D> _texSet;
    private MaterialPropertyBlock _mpb;
    private float _scrollElapsed;
    private float _scrollOffset;
    private int _scrollStPropertyId;
    private float _nextTransformMapRefreshTime = -1f;
    private bool _texturesReady;
    private CyalumePlaybackProvider _playbackProvider;

    private bool _hasManualPlaybackState;
    private int _manualPatternId;
    private float _manualPatternStartTime;
    private float _manualPlaySpeed = 1f;
    private int _manualChoreographyType;

    public void SetManualPlaybackState(int patternId, float patternStartTime, float playSpeed, int choreographyType)
    {
        _hasManualPlaybackState = true;
        _manualPatternId = Mathf.Max(0, patternId);
        _manualPatternStartTime = patternStartTime;
        _manualPlaySpeed = playSpeed > 0f ? playSpeed : 1f;
        _manualChoreographyType = choreographyType;
    }

    public void ClearManualPlaybackState()
    {
        _hasManualPlaybackState = false;
    }

    private IEnumerator Start()
    {
        _mpb = new MaterialPropertyBlock();
        _scrollStPropertyId = string.IsNullOrEmpty(scrollPropertyName)
            ? -1
            : Shader.PropertyToID(scrollPropertyName + "_ST");

        _playbackProvider = playbackProvider;
        if (_playbackProvider == null)
        {
            _playbackProvider = GetComponent<CyalumePlaybackProvider>();
        }
        if (_playbackProvider == null)
        {
            _playbackProvider = FindObjectOfType<CyalumePlaybackProvider>(true);
        }
        if (_playbackProvider == null)
        {
            _playbackProvider = gameObject.AddComponent<CyalumePlaybackProvider>();
        }

        Debug.Log("[CyalumeAutoBinder] Start()");

        float resolveMusicTimeout = 5f;
        while (musicId <= 0 && resolveMusicTimeout > 0f)
        {
            if (Director.instance != null && Director.instance.live != null)
            {
                musicId = Director.instance.live.MusicId;
                if (musicId > 0)
                {
                    break;
                }
            }

            resolveMusicTimeout -= Time.deltaTime > 0f ? Time.deltaTime : 0.016f;
            yield return null;
        }

        if (musicId <= 0)
        {
            Debug.LogWarning("[CyalumeAutoBinder] musicId is unavailable. Make sure Director is initialized in LiveScene.");
            yield break;
        }

        string liveKey = $"m{musicId}";
        Debug.Log($"[CyalumeAutoBinder] musicId={musicId}, liveKey={liveKey}");

        if (_playbackProvider != null)
        {
            _playbackProvider.InitializeForMusicId(musicId);
        }

        TryLoadTextureBundlesFromIndex(liveKey);
        yield return new WaitForSeconds(waitForStageSeconds);

        _texSet = ResolveTexSetFromLoadedBundles(liveKey);
        if (_texSet.Count == 0)
        {
            Debug.LogWarning($"[CyalumeAutoBinder] No tex_live_cyalume_{liveKey}_*** texture set was found.");
            yield break;
        }

        _texturesReady = true;

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
            yield return new WaitForSeconds(Mathf.Max(0.05f, rebindInterval));
            elapsed += Mathf.Max(0.05f, rebindInterval);
            lastBound = ApplyToScene(_texSet);
        }

        Debug.Log($"[CyalumeAutoBinder] Done. boundSlots={lastBound}, animatedMaterials={_boundMaterials.Count}");
    }

    private void Update()
    {
        if (_boundMaterials.Count == 0 || _texSet == null || _texSet.Count == 0)
            return;

        CleanupDeadBoundMaterials();
        if (_boundMaterials.Count == 0)
            return;

        if (useRecoveredPlaybackState &&
            TryGetRecoveredPlaybackState(out int patternId, out float patternStartTime, out float playSpeed, out int choreographyType, out float liveTime))
        {
            if (useGlobalPatternFromPlayback)
            {
                ApplyPatternTextureToAll(patternId);
            }
            else
            {
                for (int i = 0; i < _boundMaterials.Count; i++)
                {
                    var bound = _boundMaterials[i];
                    if (bound == null)
                        continue;

                    int patternToUse = bound.DefaultPatternId;
                    var texture = ResolveTextureForPatternId(patternToUse);
                    if (texture != null && bound.LastAppliedPatternId != patternToUse)
                    {
                        ApplyTexture(bound, texture, patternToUse);
                    }
                }
            }

            float yOffset = ComputeRecoveredYOffset(liveTime, patternStartTime, playSpeed, choreographyType);
            _scrollOffset = yOffset - Mathf.Floor(yOffset);
            ApplyScrollOffset(_scrollOffset);
            return;
        }

        if (!animateAllBoundMaterials || string.IsNullOrEmpty(scrollPropertyName))
            return;

        _scrollElapsed += Time.deltaTime;
        if (_scrollElapsed < legacyScrollInterval)
            return;

        while (_scrollElapsed >= legacyScrollInterval)
        {
            _scrollElapsed -= legacyScrollInterval;
            _scrollOffset += legacyScrollStep;
        }

        _scrollOffset -= Mathf.Floor(_scrollOffset);
        ApplyScrollOffset(_scrollOffset);
    }

    private void LateUpdate()
    {
        ApplyTimelineTransformControls();
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

    private Dictionary<int, Texture2D> ResolveTexSetFromLoadedBundles(string liveKey)
    {
        var result = new Dictionary<int, Texture2D>();

        foreach (var assetBundle in AssetBundle.GetAllLoadedAssetBundles())
        {
            if (!assetBundle)
                continue;

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
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                var match = Regex.Match(file, @"^tex_live_cyalume_(m\d+)_(\d{3})$", RegexOptions.IgnoreCase);
                if (!match.Success)
                    continue;

                string key = match.Groups[1].Value;
                if (!string.Equals(key, liveKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                int patternId = int.Parse(match.Groups[2].Value);
                if (result.ContainsKey(patternId))
                    continue;

                var tex = assetBundle.LoadAsset<Texture2D>(path);
                if (tex)
                    result[patternId] = tex;
            }
        }

        Debug.Log($"[CyalumeAutoBinder] foundPatternIds=[{string.Join(",", result.Keys.OrderBy(x => x))}]");
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

    private static int ExtractPatternId(string name)
    {
        var match = Regex.Match(name ?? string.Empty, @"cyalume_[dr](\d{3})", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out int value))
        {
            return value;
        }

        return 0;
    }

    private int ApplyToScene(Dictionary<int, Texture2D> texSet)
    {
        int boundSlots = 0;
        var renderers = FindObjectsOfType<Renderer>(true);
        foreach (var renderer in renderers)
        {
            if (!IsTargetRenderer(renderer))
            {
                continue;
            }

            boundSlots += BindRenderer(renderer, texSet);
        }

        if (fixFrontRenderers)
        {
            RepairFrontRenderers(renderers);
        }

        if (animateAllBoundMaterials)
        {
            ApplyScrollOffset(_scrollOffset);
        }

        return boundSlots;
    }

    private int BindRenderer(Renderer renderer, Dictionary<int, Texture2D> texSet)
    {
        int boundSlots = 0;
        var sharedMaterials = renderer.sharedMaterials;
        if (sharedMaterials == null || sharedMaterials.Length == 0)
        {
            return 0;
        }

        int defaultPatternId = ExtractPatternId(renderer.gameObject.name);
        Texture2D defaultTexture = ResolveTextureForPatternId(defaultPatternId, texSet);

        for (int i = 0; i < sharedMaterials.Length; i++)
        {
            string key = renderer.GetInstanceID() + ":" + i;
            if (_boundMaterialsByKey.TryGetValue(key, out var existing) && existing != null && existing.Renderer && existing.Material)
            {
                existing.DefaultPatternId = defaultPatternId;
                if (existing.LastAppliedPatternId < 0 && defaultTexture)
                {
                    ApplyTexture(existing, defaultTexture, defaultPatternId);
                    boundSlots++;
                }
                continue;
            }

            var materials = renderer.materials;
            if (i >= materials.Length)
            {
                continue;
            }

            var material = materials[i];
            if (!IsTargetMaterial(material))
            {
                continue;
            }

            string propertyName = ResolveFlipbookProperty(material);
            if (string.IsNullOrEmpty(propertyName))
            {
                continue;
            }

            var bound = new BoundMaterial
            {
                Renderer = renderer,
                MaterialIndex = i,
                Material = material,
                TexturePropertyName = propertyName,
                DefaultPatternId = defaultPatternId,
                LastAppliedPatternId = -1,
            };

            _boundMaterialsByKey[key] = bound;
            _boundMaterials.Add(bound);

            if (defaultTexture)
            {
                ApplyTexture(bound, defaultTexture, defaultPatternId);
            }

            boundSlots++;
        }

        return boundSlots;
    }

    private string ResolveFlipbookProperty(Material material)
    {
        if (!material)
        {
            return null;
        }

        for (int i = 0; i < kPreferredTextureProps.Length; i++)
        {
            string prop = kPreferredTextureProps[i];
            if (material.HasProperty(prop))
            {
                return prop;
            }
        }

        var props = material.GetTexturePropertyNames();
        if (props != null && props.Length > 0)
        {
            return props[0];
        }

        return null;
    }

    private Texture2D ResolveTextureForPatternId(int patternId, Dictionary<int, Texture2D> texSet = null)
    {
        var source = texSet ?? _texSet;
        if (source == null || source.Count == 0)
        {
            return null;
        }

        if (source.TryGetValue(patternId, out var exact) && exact)
        {
            return exact;
        }

        if (source.TryGetValue(0, out var fallbackZero) && fallbackZero)
        {
            return fallbackZero;
        }

        return source.Values.FirstOrDefault(x => x);
    }

    private void ApplyTexture(BoundMaterial bound, Texture2D texture, int patternId)
    {
        if (bound == null || !bound.Renderer || !bound.Material || texture == null)
        {
            return;
        }

        string prop = bound.TexturePropertyName;
        if (string.IsNullOrEmpty(prop) || !bound.Material.HasProperty(prop))
        {
            return;
        }

        bound.Material.SetTexture(prop, texture);

#if UNITY_2021_2_OR_NEWER
        bound.Renderer.GetPropertyBlock(_mpb, bound.MaterialIndex);
        _mpb.SetTexture(prop, texture);
        bound.Renderer.SetPropertyBlock(_mpb, bound.MaterialIndex);
#else
        bound.Renderer.GetPropertyBlock(_mpb);
        _mpb.SetTexture(prop, texture);
        bound.Renderer.SetPropertyBlock(_mpb);
#endif

        bound.LastAppliedPatternId = patternId;
    }

    private void ApplyPatternTextureToAll(int patternId)
    {
        var texture = ResolveTextureForPatternId(patternId);
        if (!texture)
        {
            if (verboseTextureLog)
            {
                Debug.LogWarning($"[CyalumeAutoBinder] Pattern texture missing for patternId={patternId}");
            }
            return;
        }

        for (int i = 0; i < _boundMaterials.Count; i++)
        {
            var bound = _boundMaterials[i];
            if (bound == null)
            {
                continue;
            }

            if (bound.LastAppliedPatternId == patternId)
            {
                continue;
            }

            ApplyTexture(bound, texture, patternId);
        }
    }

    private bool TryGetRecoveredPlaybackState(out int patternId, out float patternStartTime, out float playSpeed, out int choreographyType, out float liveTime)
    {
        liveTime = GetCurrentLiveTime();

        if (!useRecoveredPlaybackState)
        {
            patternId = 0;
            patternStartTime = 0f;
            playSpeed = 1f;
            choreographyType = 0;
            return false;
        }

        if (_playbackProvider != null)
        {
            if (_playbackProvider.TryGetCurrent(liveTime, out var current) && current != null)
            {
                patternId = Mathf.Max(0, current.PatternId);
                patternStartTime = current.StartTime;
                playSpeed = current.PlaySpeed > 0f ? current.PlaySpeed : 1f;
                choreographyType = current.ChoreographyType;
                return true;
            }
        }

        if (_hasManualPlaybackState)
        {
            patternId = Mathf.Max(0, _manualPatternId);
            patternStartTime = _manualPatternStartTime;
            playSpeed = _manualPlaySpeed > 0f ? _manualPlaySpeed : 1f;
            choreographyType = _manualChoreographyType;
            return true;
        }

        patternId = 0;
        patternStartTime = 0f;
        playSpeed = 1f;
        choreographyType = 0;
        return false;
    }

    private float GetCurrentLiveTime()
    {
        if (Director.instance != null && Director.instance._liveTimelineControl != null)
        {
            return Director.instance._liveTimelineControl.currentLiveTime;
        }

        return Time.time;
    }

    private float ComputeRecoveredYOffset(float liveTime, float patternStartTime, float playSpeed, int choreographyType)
    {
        int frameNo;
        int frameCount = Mathf.Max(1, recoveredAnimationFrameCount);

        if (choreographyType >= 8)
        {
            frameNo = 0;
        }
        else
        {
            float delta = Mathf.Max(0f, liveTime - patternStartTime);
            int raw = (int)((((delta * recoveredFrameTime) / 40.0f) * playSpeed) * frameCount) % frameCount;
            frameNo = raw >= 0 ? raw : 0;
        }

        return 1.0f - ((float)frameNo / frameCount);
    }

    private void CleanupDeadBoundMaterials()
    {
        for (int i = _boundMaterials.Count - 1; i >= 0; i--)
        {
            var bound = _boundMaterials[i];
            if (bound != null && bound.Renderer && bound.Material)
            {
                continue;
            }

            _boundMaterials.RemoveAt(i);
        }

        var deadKeys = new List<string>();
        foreach (var pair in _boundMaterialsByKey)
        {
            var bound = pair.Value;
            if (bound == null || !bound.Renderer || !bound.Material)
            {
                deadKeys.Add(pair.Key);
            }
        }

        for (int i = 0; i < deadKeys.Count; i++)
        {
            _boundMaterialsByKey.Remove(deadKeys[i]);
        }
    }

    private void ApplyScrollOffset(float yOffset)
    {
        if (string.IsNullOrEmpty(scrollPropertyName))
            return;

        for (int i = 0; i < _boundMaterials.Count; i++)
        {
            var bound = _boundMaterials[i];
            if (bound == null || !bound.Renderer || !bound.Material || !bound.Material.HasProperty(scrollPropertyName))
                continue;

            bound.Material.SetTextureOffset(scrollPropertyName, new Vector2(0f, yOffset));

#if UNITY_2021_2_OR_NEWER
        if (_scrollStPropertyId >= 0)
        {
            var scale = bound.Material.GetTextureScale(scrollPropertyName);
            bound.Renderer.GetPropertyBlock(_mpb, bound.MaterialIndex);
            _mpb.SetVector(_scrollStPropertyId, new Vector4(scale.x, scale.y, 0f, yOffset));
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

            var backRenderer = FindMatchingBackRenderer(frontRenderer);
            if (copyBackPropertyBlockToFront && backRenderer != null)
            {
                CopyPropertyBlocks(backRenderer, frontRenderer);
            }

            bool mirrored = false;
            if (mirrorBackVertexColorsToFront)
            {
                mirrored = TryMirrorBackVertexColorsToFront(frontRenderer, backRenderer);
            }

            bool forceSolidWhiteVertexColor = !mirrored && fallbackFrontToSolidWhiteVertexColor;
            ApplyFrontAdditionalVertexStreams(frontRenderer, forceSolidWhiteVertexColor);
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

    private void ApplyFrontAdditionalVertexStreams(MeshRenderer frontRenderer, bool forceVertexColor)
    {
        bool writeVertexColor = forceVertexColor || overrideFrontVertexColor;
        bool writeUv2 = overrideFrontUv2;
        if (!writeVertexColor && !writeUv2)
        {
            if (frontRenderer.additionalVertexStreams != null)
            {
                frontRenderer.additionalVertexStreams = null;
            }
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

        string cacheKey = vertexCount + ":" + (writeVertexColor ? 1 : 0) + ":" + (writeUv2 ? 1 : 0);
        if (!_frontStreamCache.TryGetValue(cacheKey, out var stream) || !stream)
        {
            stream = BuildFrontStream(vertexCount, writeVertexColor, writeUv2);
            if (!stream)
            {
                return;
            }

            _frontStreamCache[cacheKey] = stream;
        }

        if (frontRenderer.additionalVertexStreams != stream)
        {
            frontRenderer.additionalVertexStreams = stream;
        }
    }

    private Mesh BuildFrontStream(int vertexCount, bool writeVertexColor, bool writeUv2)
    {
        var descriptors = new List<VertexAttributeDescriptor>();
        int streamIndex = 0;
        int colorStream = -1;
        int uv2Stream = -1;

        if (writeVertexColor)
        {
            colorStream = streamIndex++;
            descriptors.Add(new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, colorStream));
        }

        if (writeUv2)
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
            name = $"CyalumeFrontStream_vc{vertexCount}_c{(writeVertexColor ? 1 : 0)}_u{(writeUv2 ? 1 : 0)}"
        };

        stream.SetVertexBufferParams(vertexCount, descriptors.ToArray());

        if (writeVertexColor)
        {
            var colors = new Color32[vertexCount];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = new Color32(255, 255, 255, 255);
            }

            stream.SetVertexBufferData(colors, 0, 0, vertexCount, colorStream, MeshUpdateFlags.DontRecalculateBounds);
        }

        if (writeUv2)
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

    private bool TryMirrorBackVertexColorsToFront(MeshRenderer frontRenderer, MeshRenderer backRenderer)
    {
        if (frontRenderer == null || backRenderer == null)
        {
            return false;
        }

        var frontFilter = frontRenderer.GetComponent<MeshFilter>();
        var backFilter = backRenderer.GetComponent<MeshFilter>();
        if (!frontFilter || !backFilter || !frontFilter.sharedMesh || !backFilter.sharedMesh)
        {
            return false;
        }

        Mesh backMesh = backFilter.sharedMesh;
        if (!TryGetMeshColors(backMesh, out var backColors))
        {
            if (verboseFrontRepairLog)
            {
                Debug.LogWarning($"[CyalumeAutoBinder] Back colors unreadable: {backRenderer.name}, mesh={backMesh.name}");
            }
            return false;
        }

        int frontVertexCount = frontFilter.sharedMesh.vertexCount;
        if (backColors == null || backColors.Length != frontVertexCount)
        {
            if (verboseFrontRepairLog)
            {
                Debug.LogWarning($"[CyalumeAutoBinder] Vertex count mismatch. front={frontRenderer.name} vc={frontVertexCount}, back={backRenderer.name} colorCount={(backColors == null ? 0 : backColors.Length)}");
            }
            return false;
        }

        var runtimeFrontMesh = GetOrCreateWritableFrontMesh(frontFilter);
        if (!runtimeFrontMesh)
        {
            return false;
        }

        try
        {
            runtimeFrontMesh.colors32 = backColors;
            if (verboseFrontRepairLog)
            {
                Debug.Log($"[CyalumeAutoBinder] Mirrored back vertex colors: {backRenderer.name} -> {frontRenderer.name}, vc={backColors.Length}");
            }
            return true;
        }
        catch (Exception ex)
        {
            if (verboseFrontRepairLog)
            {
                Debug.LogWarning($"[CyalumeAutoBinder] Failed to write front colors for {frontRenderer.name}: {ex.Message}");
            }
            return false;
        }
    }

    private Mesh GetOrCreateWritableFrontMesh(MeshFilter frontFilter)
    {
        if (!frontFilter || !frontFilter.sharedMesh)
        {
            return null;
        }

        if (_runtimeFrontMeshCache.TryGetValue(frontFilter, out var cached) && cached)
        {
            return cached;
        }

        Mesh runtimeMesh = null;
        try
        {
            if (cloneFrontMeshBeforeWritingColors)
            {
                runtimeMesh = Instantiate(frontFilter.sharedMesh);
                if (runtimeMesh)
                {
                    runtimeMesh.name = frontFilter.sharedMesh.name + "_FrontRuntimeClone";
                    frontFilter.sharedMesh = runtimeMesh;
                }
            }
            else
            {
                runtimeMesh = frontFilter.mesh;
            }
        }
        catch (Exception ex)
        {
            if (verboseFrontRepairLog)
            {
                Debug.LogWarning($"[CyalumeAutoBinder] Failed to create writable front mesh for {frontFilter.name}: {ex.Message}");
            }
            runtimeMesh = null;
        }

        if (runtimeMesh)
        {
            _runtimeFrontMeshCache[frontFilter] = runtimeMesh;
        }

        return runtimeMesh;
    }

    private bool TryGetMeshColors(Mesh mesh, out Color32[] colors)
    {
        colors = null;
        if (!mesh)
        {
            return false;
        }

        try
        {
            colors = mesh.colors32;
            return colors != null && colors.Length == mesh.vertexCount && colors.Length > 0;
        }
        catch (Exception ex)
        {
            if (verboseFrontRepairLog)
            {
                Debug.LogWarning($"[CyalumeAutoBinder] Failed to read colors from mesh {mesh.name}: {ex.Message}");
            }
            return false;
        }
    }

    private void ApplyTimelineTransformControls()
    {
        if (!applyTimelineTransformControl || Director.instance == null)
        {
            return;
        }

        var timelineControl = Director.instance._liveTimelineControl;
        if (timelineControl == null || timelineControl.data == null || timelineControl.data.worksheetList == null)
        {
            return;
        }

        if (Time.time >= _nextTransformMapRefreshTime)
        {
            RebuildSceneTransformMap();
        }

        float currentLiveTime = timelineControl.currentLiveTime;
        float currentFrame = currentLiveTime * LiveTimelineControl.kTargetFpsF;

        var worksheetList = timelineControl.data.worksheetList;
        for (int i = 0; i < worksheetList.Count; i++)
        {
            var worksheet = worksheetList[i];
            if (worksheet == null)
            {
                continue;
            }

            if (applyMobControlList)
            {
                ApplyMobCyalumeControlList(worksheet.mobControlList, currentFrame, currentLiveTime, "mob");
            }

            if (applyCyalumeControlList)
            {
                ApplyMobCyalumeControlList(worksheet.cyalumeControlList, currentFrame, currentLiveTime, "cyalume");
            }
        }
    }

    private void ApplyMobCyalumeControlList(
        List<LiveTimelineMobCyalumeControlData> controlList,
        float currentFrame,
        float currentLiveTime,
        string sourceTag)
    {
        if (controlList == null || controlList.Count == 0)
        {
            return;
        }

        for (int i = 0; i < controlList.Count; i++)
        {
            var controlData = controlList[i];
            if (!TryPopulateMobCyalumeUpdate(controlData, currentFrame, currentLiveTime, out var updateInfo))
            {
                continue;
            }

            ApplyMobCyalumeUpdate(ref updateInfo, sourceTag);
        }
    }

    private bool TryPopulateMobCyalumeUpdate(
        LiveTimelineMobCyalumeControlData controlData,
        float currentFrame,
        float currentLiveTime,
        out MobCyalumeUpdateInfo updateInfo)
    {
        updateInfo = default;
        if (controlData == null || controlData.keys == null || controlData.keys.Count <= 0)
        {
            return false;
        }

        var keys = controlData.keys;
        if (keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable))
        {
            return false;
        }

        if (!keys.EnablePlayModeTimeline(Director.instance._liveTimelineControl.PlayMode))
        {
            return false;
        }

        LiveTimelineControl.FindTimelineKey(out var curKeyBase, out var nextKeyBase, keys, currentFrame);
        var currentKey = curKeyBase as LiveTimelineKeyMobCyalumeControlData;
        if (currentKey == null)
        {
            return false;
        }

        var nextKey = nextKeyBase as LiveTimelineKeyMobCyalumeControlData;

        updateInfo.data = controlData;
        updateInfo.unk0 = keys.unk48;
        updateInfo.currentFrame = currentFrame;
        updateInfo.currentLiveTime = currentLiveTime;

        if (nextKey != null && nextKey.interpolateType != LiveCameraInterpolateType.None)
        {
            float t = LiveTimelineControl.CalculateInterpolationValue(currentKey, nextKey, currentFrame);
            updateInfo.position = LerpWithoutClamp(currentKey.position, nextKey.position, t);
            updateInfo.scale = LerpWithoutClamp(currentKey.scale, nextKey.scale, t);
            updateInfo.rotation = Quaternion.Lerp(currentKey.GetRotation(), nextKey.GetRotation(), t);
        }
        else
        {
            updateInfo.position = currentKey.position;
            updateInfo.rotation = currentKey.GetRotation();
            updateInfo.scale = currentKey.scale;
        }

        return true;
    }

    private void ApplyMobCyalumeUpdate(ref MobCyalumeUpdateInfo updateInfo, string sourceTag)
    {
        var targets = ResolveTimelineTargets(updateInfo.data?.name);
        if (targets == null || targets.Count == 0)
        {
            return;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (!target)
            {
                continue;
            }

            target.localPosition = updateInfo.position;
            target.localRotation = updateInfo.rotation;
            target.localScale = updateInfo.scale;
        }

        if (verboseTimelineLog)
        {
            Debug.Log($"[CyalumeAutoBinder] Applied {sourceTag} control '{updateInfo.data.name}' to {targets.Count} target(s). unk48={updateInfo.unk0}");
        }
    }

    private List<Transform> ResolveTimelineTargets(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        string normalized = NormalizeSceneName(name);
        if (_sceneTransformsByName.TryGetValue(normalized, out var targets))
        {
            return targets;
        }

        if (verboseTimelineLog && _missingTimelineTargets.Add(normalized))
        {
            Debug.LogWarning($"[CyalumeAutoBinder] Timeline control target not found: {name}");
        }

        return null;
    }

    private void RebuildSceneTransformMap()
    {
        _sceneTransformsByName.Clear();

        foreach (var transform in FindObjectsOfType<Transform>(true))
        {
            if (!transform)
            {
                continue;
            }

            string key = NormalizeSceneName(transform.name);
            if (!_sceneTransformsByName.TryGetValue(key, out var list))
            {
                list = new List<Transform>();
                _sceneTransformsByName[key] = list;
            }

            list.Add(transform);
        }

        _nextTransformMapRefreshTime = Time.time + Mathf.Max(0.1f, transformMapRefreshInterval);
    }

    private static string NormalizeSceneName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        return name.Replace("(Clone)", string.Empty).Trim();
    }

    private static Vector3 LerpWithoutClamp(Vector3 a, Vector3 b, float t)
    {
        return new Vector3(
            a.x + (b.x - a.x) * t,
            a.y + (b.y - a.y) * t,
            a.z + (b.z - a.z) * t);
    }

}

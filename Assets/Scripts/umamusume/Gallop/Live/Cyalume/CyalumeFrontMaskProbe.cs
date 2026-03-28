using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class CyalumeFrontMaskProbe : MonoBehaviour
{
    public float delay = 1.0f;

    public string shaderContains = "Gallop/3D/Live/Cyalume/CyalumeDefault";

    // 只处理 *_front
    public bool onlyFront = true;

    // 覆盖哪些顶点通道
    public bool overrideColor = true;
    public bool overrideUV2   = true;   // << 关键：TexCoord1 / uv2
    public bool overrideTangent = false; // 可选：如果 UV2 也不行再开

    // 写入的值（先用 1）
    public Color32 color = new Color32(255,255,255,255);
    public Vector2 uv2Value = new Vector2(1f, 1f);
    public Vector4 tangentValue = new Vector4(1f, 0f, 0f, 1f);

    // cache：同样 vertexCount + 组合复用一份 stream mesh
    readonly Dictionary<(int vc, bool c, bool u, bool t), Mesh> _cache = new();

    void Start() => Invoke(nameof(Apply), delay);

    [ContextMenu("Apply Now")]
    public void Apply()
    {
        int hit = 0, applied = 0;

        foreach (var mr in FindObjectsOfType<MeshRenderer>(true))
        {
            if (!mr || !mr.gameObject.activeInHierarchy) continue;
            if (onlyFront && !mr.name.EndsWith("_front", StringComparison.OrdinalIgnoreCase)) continue;

            if (!HasTargetShader(mr)) continue;

            var mf = mr.GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh) continue;

            hit++;

            int vc = mf.sharedMesh.vertexCount;
            var key = (vc, overrideColor, overrideUV2, overrideTangent);
            if (!_cache.TryGetValue(key, out var stream) || !stream)
            {
                stream = BuildStream(vc, overrideColor, overrideUV2, overrideTangent);
                _cache[key] = stream;
            }

            mr.additionalVertexStreams = stream;
            applied++;
        }

        Debug.Log($"[CyalumeFrontMaskProbe] hit={hit}, applied={applied}, cache={_cache.Count} (c={overrideColor} u2={overrideUV2} tan={overrideTangent})");
    }

    bool HasTargetShader(Renderer r)
    {
        foreach (var m in r.sharedMaterials)
        {
            if (!m || !m.shader) continue;
            if (m.shader.name.IndexOf(shaderContains, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    Mesh BuildStream(int vc, bool c, bool u2, bool tan)
    {
        var desc = new List<VertexAttributeDescriptor>();
        int streamIdx = 0;

        int colorStream = -1, uv2Stream = -1, tanStream = -1;

        if (c)   { colorStream = streamIdx++; desc.Add(new VertexAttributeDescriptor(VertexAttribute.Color,    VertexAttributeFormat.UNorm8, 4, colorStream)); }
        if (u2)  { uv2Stream   = streamIdx++; desc.Add(new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2, uv2Stream)); }
        if (tan) { tanStream   = streamIdx++; desc.Add(new VertexAttributeDescriptor(VertexAttribute.Tangent,  VertexAttributeFormat.Float32, 4, tanStream)); }

        var m = new Mesh();
        m.name = $"CyalumeStream_vc{vc}_c{c}_u2{u2}_t{tan}";
        m.SetVertexBufferParams(vc, desc.ToArray());

        if (c)
        {
            var cols = new Color32[vc];
            for (int i=0;i<vc;i++) cols[i] = color;
            m.SetVertexBufferData(cols, 0, 0, vc, colorStream, MeshUpdateFlags.DontRecalculateBounds);
        }

        if (u2)
        {
            var uv = new Vector2[vc];
            for (int i=0;i<vc;i++) uv[i] = uv2Value;
            m.SetVertexBufferData(uv, 0, 0, vc, uv2Stream, MeshUpdateFlags.DontRecalculateBounds);
        }

        if (tan)
        {
            var t = new Vector4[vc];
            for (int i=0;i<vc;i++) t[i] = tangentValue;
            m.SetVertexBufferData(t, 0, 0, vc, tanStream, MeshUpdateFlags.DontRecalculateBounds);
        }

        return m;
    }
}
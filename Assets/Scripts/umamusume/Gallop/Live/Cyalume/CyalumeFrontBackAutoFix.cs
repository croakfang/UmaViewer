using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class CyalumeFrontBackAutoFix : MonoBehaviour
{
    [Header("等观众/荧光棒生成出来")]
    public float delay = 1.0f;

    [Header("只处理名字含 cyalume 且以 _front 结尾的 Renderer")]
    public string namePrefix = "cyalume_";

    [Header("可选：只处理 CyalumeDefault shader（留空=不限制）")]
    public string shaderContains = "CyalumeDefault";

    [Header("把 back 的 MPB 复制给 front（如果 MPB 不同会救回来）")]
    public bool copyPropertyBlock = true;

    [Header("给 front 强制白色顶点色流（不需要 mesh Read/Write）")]
    public bool forceWhiteVertexColorStream = true;

    // 缓存：不同 vertexCount 共用一份 stream mesh
    private readonly Dictionary<int, Mesh> _colorStreamCache = new Dictionary<int, Mesh>();

    private bool _done;
    private MaterialPropertyBlock _mpb;

    void Awake() => _mpb = new MaterialPropertyBlock();

    void Start()
    {
        Invoke(nameof(ApplyOnce), delay);
    }

    [ContextMenu("Apply Once Now")]
    public void ApplyOnce()
    {
        if (_done) return;

        int pairs = 0, colored = 0, mpbCopied = 0;

        var all = FindObjectsOfType<MeshRenderer>(true);
        foreach (var frontMr in all)
        {
            var n = frontMr.gameObject.name;
            if (!n.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!n.EndsWith("_front", StringComparison.OrdinalIgnoreCase)) continue;

            if (!IsTargetShader(frontMr)) continue;

            // 找同父节点下的 back
            var parent = frontMr.transform.parent;
            if (!parent) continue;

            var backName = n.Substring(0, n.Length - "_front".Length) + "_back";
            var backTf = parent.Find(backName);
            var backMr = backTf ? backTf.GetComponent<MeshRenderer>() : null;

            pairs++;

            // 1) Copy MPB
            if (copyPropertyBlock && backMr)
            {
                backMr.GetPropertyBlock(_mpb);
                frontMr.SetPropertyBlock(_mpb);
                mpbCopied++;
            }

            // 2) Force vertex color stream on FRONT
            if (forceWhiteVertexColorStream)
            {
                var mf = frontMr.GetComponent<MeshFilter>();
                if (mf && mf.sharedMesh)
                {
                    int vc = mf.sharedMesh.vertexCount;
                    if (vc > 0)
                    {
                        if (!_colorStreamCache.TryGetValue(vc, out var stream) || !stream)
                        {
                            stream = BuildWhiteColorStream(vc);
                            _colorStreamCache[vc] = stream;
                        }
                        frontMr.additionalVertexStreams = stream;
                        colored++;
                    }
                }
            }
        }

        Debug.Log($"[CyalumeFrontBackAutoFix] pairs(front found)={pairs}, mpbCopied={mpbCopied}, colored(front)={colored}, cache={_colorStreamCache.Count}");
        _done = true;
    }

    bool IsTargetShader(Renderer r)
    {
        if (string.IsNullOrEmpty(shaderContains)) return true;
        foreach (var m in r.sharedMaterials)
        {
            if (!m || !m.shader) continue;
            if (m.shader.name.IndexOf(shaderContains, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    Mesh BuildWhiteColorStream(int vertexCount)
    {
        var m = new Mesh();
        m.name = $"CyalumeColorStream_vc{vertexCount}";
        m.SetVertexBufferParams(
            vertexCount,
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, 0)
        );

        var cols = new Color32[vertexCount];
        for (int i = 0; i < cols.Length; i++) cols[i] = new Color32(255, 255, 255, 255);

        m.SetVertexBufferData(cols, 0, 0, vertexCount, 0, MeshUpdateFlags.DontRecalculateBounds);
        return m;
    }
}

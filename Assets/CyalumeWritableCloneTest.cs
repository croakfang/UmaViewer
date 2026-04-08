using System;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class CyalumeWritableCloneTest : MonoBehaviour
{
    [Header("Test options")]
    public bool addSolidWhiteColors = true;
    public bool addUv2One = false;
    public bool applyOnStart = false;

    private Mesh _originalSharedMesh;
    private Mesh _runtimeClone;

    private void Start()
    {
        if (applyOnStart)
        {
            MakeWritableRuntimeClone();
        }
    }

    [ContextMenu("Make Writable Runtime Clone")]
    public void MakeWritableRuntimeClone()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            Debug.LogWarning("[CyalumeWritableCloneTest] MeshFilter/sharedMesh missing.");
            return;
        }

        _originalSharedMesh = mf.sharedMesh;
        var src = _originalSharedMesh;

#if UNITY_2020_2_OR_NEWER
        try
        {
            using (var ro = Mesh.AcquireReadOnlyMeshData(src))
            {
                if (ro.Length == 0)
                {
                    Debug.LogWarning("[CyalumeWritableCloneTest] AcquireReadOnlyMeshData returned 0 meshes.");
                    return;
                }

                var md = ro[0];
                var dst = new Mesh
                {
                    name = src.name + "_WritableClone"
                };

                // 这批 cyalume mesh 已确认是 Position / Normal / Tangent / UV0
                var layout = new[]
                {
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
                    new VertexAttributeDescriptor(VertexAttribute.Normal,   VertexAttributeFormat.Float32, 3, 0),
                    new VertexAttributeDescriptor(VertexAttribute.Tangent,  VertexAttributeFormat.Float32, 4, 0),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.Float32, 2, 0),
                };

                dst.SetVertexBufferParams(md.vertexCount, layout);

                var flags =
                    MeshUpdateFlags.DontRecalculateBounds |
                    MeshUpdateFlags.DontValidateIndices |
                    MeshUpdateFlags.DontNotifyMeshUsers;

                // 这批 mesh 只有 1 个 vertex stream
                var vb = md.GetVertexData<byte>(0);
                dst.SetVertexBufferData(vb, 0, 0, vb.Length, 0, flags);

                var ib = md.GetIndexData<byte>();
                int indexCount = md.indexFormat == IndexFormat.UInt16
                    ? ib.Length / 2
                    : ib.Length / 4;

                dst.SetIndexBufferParams(indexCount, md.indexFormat);
                dst.SetIndexBufferData(ib, 0, 0, ib.Length, flags);

                dst.subMeshCount = md.subMeshCount;
                for (int s = 0; s < md.subMeshCount; s++)
                {
                    dst.SetSubMesh(s, md.GetSubMesh(s), flags);
                }

                dst.bounds = src.bounds;

                if (addSolidWhiteColors)
                {
                    var colors = new Color32[md.vertexCount];
                    for (int i = 0; i < colors.Length; i++)
                    {
                        colors[i] = new Color32(255, 255, 255, 255);
                    }
                    dst.colors32 = colors;
                }

                if (addUv2One)
                {
                    var uv2 = new Vector2[md.vertexCount];
                    for (int i = 0; i < uv2.Length; i++)
                    {
                        uv2[i] = Vector2.one;
                    }
                    dst.uv2 = uv2;
                }

                _runtimeClone = dst;
            }

            mf.sharedMesh = _runtimeClone;

            Debug.Log(
                "[CyalumeWritableCloneTest] Success\n" +
                $"obj={name}\n" +
                $"original={(_originalSharedMesh ? _originalSharedMesh.name : "<null>")} id={(_originalSharedMesh ? _originalSharedMesh.GetInstanceID().ToString() : "-")}\n" +
                $"clone={(_runtimeClone ? _runtimeClone.name : "<null>")} id={(_runtimeClone ? _runtimeClone.GetInstanceID().ToString() : "-")}\n" +
                $"cloneReadable={(_runtimeClone ? _runtimeClone.isReadable.ToString() : "-")}\n" +
                $"addSolidWhiteColors={addSolidWhiteColors}, addUv2One={addUv2One}");
        }
        catch (Exception ex)
        {
            Debug.LogError("[CyalumeWritableCloneTest] Failed to build writable clone: " + ex);
        }
#else
        Debug.LogError("[CyalumeWritableCloneTest] Mesh.AcquireReadOnlyMeshData requires Unity 2020.2+.");
#endif
    }

    [ContextMenu("Restore Original SharedMesh")]
    public void RestoreOriginalSharedMesh()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf == null)
            return;

        if (_originalSharedMesh != null)
        {
            mf.sharedMesh = _originalSharedMesh;
            Debug.Log($"[CyalumeWritableCloneTest] Restored original mesh: {_originalSharedMesh.name}");
        }
    }
}
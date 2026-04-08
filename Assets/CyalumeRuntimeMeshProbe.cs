using UnityEngine;

public class CyalumeRuntimeMeshProbe : MonoBehaviour
{
    [ContextMenu("Probe Runtime Mesh")]
    void Probe()
    {
        var mf = GetComponent<MeshFilter>();
        var mr = GetComponent<MeshRenderer>();

        var shared = mf ? mf.sharedMesh : null;
        Mesh runtime = null;
        try { runtime = mf ? mf.mesh : null; } catch { }

        Debug.Log(
            "[CyalumeRuntimeMeshProbe]\n" +
            $"obj={name}\n" +
            $"sharedMesh={(shared ? shared.name : "<null>")} id={(shared ? shared.GetInstanceID().ToString() : "-")}\n" +
            $"mesh={(runtime ? runtime.name : "<null>")} id={(runtime ? runtime.GetInstanceID().ToString() : "-")}\n" +
            $"sameRef={(shared == runtime)}\n" +
            $"sharedReadable={(shared ? shared.isReadable.ToString() : "-")}\n" +
            $"meshReadable={(runtime ? runtime.isReadable.ToString() : "-")}\n" +
            $"additionalVertexStreams={(mr && mr.additionalVertexStreams ? mr.additionalVertexStreams.name : "<null>")}\n" +
            $"material={(mr && mr.sharedMaterial ? mr.sharedMaterial.name : "<null>")}");
    }
}
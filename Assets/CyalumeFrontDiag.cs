using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public class CyalumeFrontDiag : MonoBehaviour
{
    public Transform root;
    public string nameContains = "cyalume";
    public string shaderContains = "Cyalume";
    public bool onlyFront = true;
    public bool includeInactive = true;

    [ContextMenu("Dump Cyalume Front Diagnostics")]
    public void Dump()
    {
        var scanRoot = root != null ? root : transform;
        var renderers = scanRoot.GetComponentsInChildren<MeshRenderer>(includeInactive);

        var boundRendererIds = CollectBoundRendererIds();
        Debug.Log($"[CyalumeFrontDiag] scanRoot={scanRoot.name}, renderers={renderers.Length}, boundRendererIds={boundRendererIds.Count}");

        int hit = 0;
        foreach (var r in renderers)
        {
            if (!r) continue;
            if (!string.IsNullOrEmpty(nameContains) &&
                r.name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (onlyFront && !r.name.EndsWith("_front", StringComparison.OrdinalIgnoreCase))
                continue;

            hit++;

            bool nameOk = r.name.IndexOf("cyalume", StringComparison.OrdinalIgnoreCase) >= 0;
            bool shaderOk = false;
            string shaderNames = "";
            foreach (var m in r.sharedMaterials)
            {
                if (!m || !m.shader) continue;
                if (shaderNames.Length > 0) shaderNames += ", ";
                shaderNames += m.shader.name;
                if (string.IsNullOrEmpty(shaderContains) ||
                    m.shader.name.IndexOf(shaderContains, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    shaderOk = true;
                }
            }

            bool binderTargetLike = nameOk && shaderOk;
            bool bound = boundRendererIds.Contains(r.GetInstanceID());

            var mf = r.GetComponent<MeshFilter>();
            var mesh = mf ? mf.sharedMesh : null;
            string meshName = mesh ? mesh.name : "<null>";
            int vc = mesh ? mesh.vertexCount : 0;

            int colorsLen = 0;
            string alphaRange = "n/a";
            string rgSample = "n/a";
            if (mesh)
            {
                try
                {
                    var c = mesh.colors32;
                    colorsLen = c != null ? c.Length : 0;
                    if (c != null && c.Length > 0)
                    {
                        byte minA = 255, maxA = 0;
                        int sampleCount = Mathf.Min(8, c.Length);
                        var rg = new List<string>(sampleCount);
                        for (int i = 0; i < c.Length; i++)
                        {
                            if (c[i].a < minA) minA = c[i].a;
                            if (c[i].a > maxA) maxA = c[i].a;
                        }
                        for (int i = 0; i < sampleCount; i++)
                        {
                            rg.Add($"[{i}] r={c[i].r} g={c[i].g} b={c[i].b} a={c[i].a}");
                        }
                        alphaRange = $"{minA}..{maxA}";
                        rgSample = string.Join(" | ", rg);
                    }
                }
                catch (Exception ex)
                {
                    alphaRange = "ERR: " + ex.Message;
                }
            }

            string avsName = r.additionalVertexStreams ? r.additionalVertexStreams.name : "<null>";

            MeshRenderer back = FindBack(r.transform);
            string backInfo = "<none>";
            if (back)
            {
                var backMf = back.GetComponent<MeshFilter>();
                var backMesh = backMf ? backMf.sharedMesh : null;
                int backVc = backMesh ? backMesh.vertexCount : 0;
                int backColorsLen = 0;
                try
                {
                    var bc = backMesh ? backMesh.colors32 : null;
                    backColorsLen = bc != null ? bc.Length : 0;
                }
                catch { }

                backInfo = $"name={back.name}, mesh={(backMesh ? backMesh.name : "<null>")}, vc={backVc}, colorsLen={backColorsLen}, vertexCountMatch={(backColorsLen == vc)}";
            }

            Debug.Log(
                $"[CyalumeFrontDiag] renderer={r.name}\n" +
                $"  bound={bound}, binderTargetLike={binderTargetLike}, enabled={r.enabled}, gameObjectActive={r.gameObject.activeInHierarchy}\n" +
                $"  shaders={shaderNames}\n" +
                $"  mesh={meshName}, vertexCount={vc}, colorsLen={colorsLen}, alphaRange={alphaRange}, additionalVertexStreams={avsName}\n" +
                $"  back={backInfo}\n" +
                $"  colorSample={rgSample}");
        }

        Debug.Log($"[CyalumeFrontDiag] finished, matchedFrontRenderers={hit}");
    }

    private HashSet<int> CollectBoundRendererIds()
    {
        var result = new HashSet<int>();
        var binder = GetComponent<CyalumeAutoBinder>();
        if (!binder) return result;

        try
        {
            var f = typeof(CyalumeAutoBinder).GetField("_boundMaterials", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return result;

            var list = f.GetValue(binder) as System.Collections.IEnumerable;
            if (list == null) return result;

            foreach (var item in list)
            {
                if (item == null) continue;
                var rf = item.GetType().GetField("Renderer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (rf == null) continue;
                var r = rf.GetValue(item) as Renderer;
                if (r) result.Add(r.GetInstanceID());
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[CyalumeFrontDiag] Failed to reflect bound materials: " + ex.Message);
        }

        return result;
    }

    private MeshRenderer FindBack(Transform front)
    {
        if (!front) return null;
        if (!front.name.EndsWith("_front", StringComparison.OrdinalIgnoreCase)) return null;

        var parent = front.parent;
        if (!parent) return null;

        string backName = front.name.Substring(0, front.name.Length - "_front".Length) + "_back";
        var t = parent.Find(backName);
        return t ? t.GetComponent<MeshRenderer>() : null;
    }
}
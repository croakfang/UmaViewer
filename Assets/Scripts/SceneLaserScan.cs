using System;
using System.Collections;
using UnityEngine;
using Gallop.Live;

public class SceneLaserScan : MonoBehaviour
{
    public int delayFrames = 180;
    public int maxPrint = 200;

    // 扫描关键词：GameObject 名 / Mesh 名 / 材质名 / shader 名
    public string[] keywords = new[] { "laser", "beam", "ray" };

    private IEnumerator Start()
    {
        for (int i = 0; i < Mathf.Max(0, delayFrames); i++) yield return null;

        string bgid = Director.instance?.live?.BackGroundId ?? "null";
        Debug.Log($"[SceneLaserScan] bgid={bgid}");

        int hitGo = 0;
        int hitMesh = 0;
        int hitMat = 0;

        var all = UnityEngine.Object.FindObjectsOfType<GameObject>(true);

        foreach (var go in all)
        {
            if (!go) continue;

            // 1) GameObject 名字命中
            if (Hit(go.name))
            {
                Debug.Log($"[HIT_GO] {GetPath(go.transform)} active={go.activeInHierarchy}");
                hitGo++;
                if (hitGo >= maxPrint) break;
            }
        }

        // 2) Mesh 名命中（全场景）
        foreach (var mf in UnityEngine.Object.FindObjectsOfType<MeshFilter>(true))
        {
            if (!mf || !mf.sharedMesh) continue;
            if (Hit(mf.sharedMesh.name))
            {
                Debug.Log($"[HIT_MESH] go={GetPath(mf.transform)} mesh={mf.sharedMesh.name}");
                hitMesh++;
                if (hitMesh >= maxPrint) break;
            }
        }

        // 3) 材质 / shader 名命中（全场景）
        foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>(true))
        {
            if (!r) continue;
            var mats = r.sharedMaterials;
            if (mats == null) continue;

            foreach (var m in mats)
            {
                if (!m) continue;
                var matName = m.name ?? "";
                var shaderName = m.shader ? m.shader.name : "";
                if (Hit(matName) || Hit(shaderName))
                {
                    Debug.Log($"[HIT_MAT] go={GetPath(r.transform)} mat={matName} shader={shaderName} enabled={r.enabled}");
                    hitMat++;
                    if (hitMat >= maxPrint) break;
                }
            }
            if (hitMat >= maxPrint) break;
        }

        Debug.Log($"[SceneLaserScan] done. hitGo={hitGo} hitMesh={hitMesh} hitMat={hitMat}");
    }

    private bool Hit(string s)
    {
        if (string.IsNullOrEmpty(s) || keywords == null) return false;
        foreach (var kw in keywords)
        {
            if (string.IsNullOrEmpty(kw)) continue;
            if (s.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    private static string GetPath(Transform t)
    {
        if (!t) return "";
        string path = t.name;
        while (t.parent)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
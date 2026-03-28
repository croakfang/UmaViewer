using System;
using System.Collections;
using UnityEngine;
using Gallop.Live;

public class LaserSystemKeyProbe : MonoBehaviour
{
    public int delayFrames = 180;
    public int maxPrint = 200;

    private IEnumerator Start()
    {
        for (int i = 0; i < delayFrames; i++) yield return null;

        var ab = UmaViewerMain.Instance?.AbList;
        if (ab == null) { Debug.LogWarning("[LaserSystemKeyProbe] AbList null"); yield break; }

        string[] needles = { "laser", "driver", "raycast", "stage", "cmm" };

        int hits = 0;
        foreach (var kv in ab)
        {
            var key = kv.Key;

            // 命中 laser 且命中 driver/raycast 任一
            bool hasLaser = key.IndexOf("laser", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasDriver = key.IndexOf("driver", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             key.IndexOf("raycast", StringComparison.OrdinalIgnoreCase) >= 0;

            if (hasLaser && hasDriver)
            {
                Debug.Log("[LASER_SYS_KEY] " + key);
                hits++;
                if (hits >= maxPrint) break;
            }
        }

        Debug.Log($"[LaserSystemKeyProbe] done. hits={hits}");
    }
}
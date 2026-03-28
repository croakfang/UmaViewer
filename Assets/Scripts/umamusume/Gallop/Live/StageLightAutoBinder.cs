using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using Gallop.Live;

public class StageLightAutoBinder : MonoBehaviour
{
    public int musicId = 0;
    public int stageId = 0;

    [Header("等 Director/live 初始化")]
    public int waitFrames = 60;

    [Header("按 AbList 名称关键词预加载（模仿 CyalumeAutoBinder）")]
    public bool preloadFromIndex = true;

    [Header("控制台输出更多日志")]
    public bool verboseLog = true;

    private Director _director;

    private IEnumerator Start()
    {
        yield return DelayResolveAndPreload();

        // 确保驱动脚本存在（跟你 StageController 里那段反射逻辑一致）
        var t = Type.GetType("StageBlinkLightDriver, Assembly-CSharp")
             ?? Type.GetType("Gallop.Live.StageBlinkLightDriver, Assembly-CSharp");
        if (t != null && GetComponent(t) == null)
            gameObject.AddComponent(t);

        // 把解析到的 id 传给驱动（可选）
        /*var driver = GetComponent<Gallop.Live.StageLightAutoBinder>();
        if (driver != null)
        {
            driver.musicId = musicId;
            driver.stageId = stageId;
        }*/
    }

    private IEnumerator DelayResolveAndPreload()
    {
        for (int i = 0; i < waitFrames; i++)
        {
            _director = _director != null ? _director : GetComponentInChildren<Director>();
            if (_director == null) _director = Director.instance;

            if (_director != null && _director.live != null)
            {
                if (musicId <= 0) musicId = _director.live.MusicId;

                if (stageId <= 0 && !string.IsNullOrEmpty(_director.live.BackGroundId))
                {
                    int.TryParse(_director.live.BackGroundId, out stageId);
                }

                if (verboseLog)
                    Debug.Log($"[StageLightAutoBinder] musicId={musicId}, stageId={stageId}, bgIdRaw={_director.live.BackGroundId}");

                if (preloadFromIndex)
                {
                    if (musicId > 0) PreloadCameraLikeBundlesFromIndex(musicId);
                    if (stageId > 0) PreloadStageLightBundlesFromIndex(stageId);
                }
                yield break;
            }
            yield return null;
        }

        Debug.LogWarning("[StageLightAutoBinder] Director/live 取不到：请确认脚本挂在 LiveScene 里且 Director 已初始化。");
    }

    private void PreloadCameraLikeBundlesFromIndex(int mid)
    {
        var main = FindObjectOfType<UmaViewerMain>();
        if (main == null || main.AbList == null) return;

        string son = $"son{mid:D4}";
        // “camera 数据”通常就在 cutt/camera 相关 bundle 里；这里用宽松匹配（你可按自己索引实际命名再收紧）
        string[] needles =
        {
            $"cutt_{son}",         // e.g. cutt_son0123
            $"{son}_cutt",
            $"{son}_camera",
            "camera_" + son,
            "cutt/cutt_" + son,    // 有些 Name 会带路径
            "camera",              // 兜底（会多，但只加载索引里匹配到 son 的优先）
        };

        var entries = main.AbList.Values
            .Where(e => e != null && !string.IsNullOrEmpty(e.Name))
            .Where(e =>
                e.Name.IndexOf(son, StringComparison.OrdinalIgnoreCase) >= 0 &&
                needles.Any(n => e.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
            .ToList();

        if (verboseLog) Debug.Log($"[StageLightAutoBinder] cameraLikeEntries={entries.Count}");

        foreach (var e in entries)
            UmaAssetManager.LoadAssetBundle(e, neverUnload: true, isRecursive: true);
    }

    private void PreloadStageLightBundlesFromIndex(int sid)
    {
        var main = FindObjectOfType<UmaViewerMain>();
        if (main == null || main.AbList == null) return;

        string live = $"live{sid:D5}";
        string[] needles =
        {
            live,
            "blinklight",
            "blink_light",
            "light",
            "env_" + live,
            "pfb_env_" + live,
        };

        var entries = main.AbList.Values
            .Where(e => e != null && !string.IsNullOrEmpty(e.Name))
            .Where(e => needles.All(n => true) && needles.Any(n => e.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
            .Where(e => e.Name.IndexOf(live, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        if (verboseLog) Debug.Log($"[StageLightAutoBinder] stageLightEntries={entries.Count}");

        foreach (var e in entries)
            UmaAssetManager.LoadAssetBundle(e, neverUnload: true, isRecursive: true);
    }
}

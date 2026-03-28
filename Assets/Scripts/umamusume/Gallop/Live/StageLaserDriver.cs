using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    public class StageLaserDriver : MonoBehaviour
    {
        public bool verboseLog = true;

        [Header("自动把 laser prefab 实例化出来")]
        public bool autoSpawnLaserPrefab = true;

        private LiveTimelineControl _ctl;
        private StageController _stage;

        private string _bgId;
        private bool _spawnTried = false;
        private GameObject _laserRootInstance;

        private readonly Dictionary<string, LaserUpdateInfo> _latest = new Dictionary<string, LaserUpdateInfo>(64);

        private void OnEnable()
        {
            BindIfPossible();
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void BindIfPossible()
        {
            var dir = Director.instance;
            if (!dir) return;

            _ctl = dir._liveTimelineControl;
            _stage = dir._stageController;
            _bgId = dir.live != null ? dir.live.BackGroundId : null;

            if (_ctl == null || _stage == null) return;

            if (autoSpawnLaserPrefab && !_spawnTried)
            {
                _spawnTried = true;
                TrySpawnLaserPrefab();
            }

            _ctl.OnUpdateLaser += OnLaser;
            if (verboseLog) Debug.Log("[StageLaserDriver] bound");
        }

        private void Unbind()
        {
            if (_ctl != null) _ctl.OnUpdateLaser -= OnLaser;

            _ctl = null;
            _stage = null;
            _latest.Clear();

            _spawnTried = false;
            _laserRootInstance = null;
        }

        private void TrySpawnLaserPrefab()
        {
            if (string.IsNullOrEmpty(_bgId)) return;

            var main = UmaViewerMain.Instance;
            if (main == null || main.AbList == null) return;

            // ✅ 1) 先尝试：AbList 里有没有单独的 laser000 entry
            string laserPath = $"3d/env/live/live{_bgId}/pfb_env_live{_bgId}_laser000";
            if (main.AbList.TryGetValue(laserPath, out var laserEntry))
            {
                var prefab = laserEntry.Get<GameObject>(withDependencies: true);
                if (prefab != null)
                {
                    _laserRootInstance = Instantiate(prefab, _stage.transform);
                    RegisterAllChildrenToStageMap(_laserRootInstance);
                    if (verboseLog) Debug.Log($"[StageLaserDriver] spawned from AbList: {laserPath}");
                    return;
                }
            }

            // ✅ 2) 否则：从 controller000 的 AssetBundle 里“捞”出所有名字带 laser 的 GameObject
            string controllerPath = $"3d/env/live/live{_bgId}/pfb_env_live{_bgId}_controller000";
            if (!main.AbList.TryGetValue(controllerPath, out var stageEntry))
            {
                if (verboseLog) Debug.LogWarning($"[StageLaserDriver] stage entry not found: {controllerPath}");
                return;
            }

            var bundle = UmaAssetManager.LoadAssetBundle(stageEntry, neverUnload: true, isRecursive: true);
            if (bundle == null) return;

            var allGos = bundle.LoadAllAssets<GameObject>();
            var laserPrefabs = allGos
                .Where(go => go != null && go.name.IndexOf("laser", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (laserPrefabs.Count == 0)
            {
                if (verboseLog) Debug.LogWarning($"[StageLaserDriver] no laser-prefab found inside stage bundle (bgId={_bgId})");
                return;
            }

            // 避免重复实例化
            foreach (var p in laserPrefabs)
            {
                if (_stage.transform.Find(p.name + "(Clone)") != null) continue;

                var inst = Instantiate(p, _stage.transform);
                RegisterAllChildrenToStageMap(inst);

                if (_laserRootInstance == null)
                    _laserRootInstance = inst;
            }

            if (verboseLog) Debug.Log($"[StageLaserDriver] spawned {laserPrefabs.Count} laser prefabs from stage bundle (bgId={_bgId})");
        }

        private void OnLaser(ref LaserUpdateInfo info)
        {
            if (info.data == null || info.key == null) return;
            if (string.IsNullOrEmpty(info.data.name)) return;

            _latest[info.data.name] = info;
        }

        private void LateUpdate()
        {
            // 丢失绑定时自愈
            if (_ctl == null || _stage == null)
                BindIfPossible();

            if (_ctl == null || _stage == null) return;
            if (_latest.Count == 0) return;

            foreach (var kv in _latest)
            {
                ApplyLaser(kv.Key, kv.Value);
            }
        }

        private void ApplyLaser(string timelineName, LaserUpdateInfo info)
        {
            GameObject go = null;

            // 1) timelineName 直接命中 StageObjectMap
            if (_stage.StageObjectMap != null && _stage.StageObjectMap.TryGetValue(timelineName, out go) && go != null)
            {
                ApplyKey(go.transform, info);
                ApplyBlink(go, info);
                return;
            }

            // 2) fallback：很多激光是挂在 lasercontroller 下
            if (_laserRootInstance != null)
            {
                var t = _laserRootInstance.transform.Find("lasercontroller");
                if (t != null)
                {
                    ApplyKey(t, info);
                    ApplyBlink(t.gameObject, info);
                    return;
                }

                // 3) 再 fallback：直接动根
                ApplyKey(_laserRootInstance.transform, info);
                ApplyBlink(_laserRootInstance, info);
            }
        }

        private static void ApplyKey(Transform tr, LaserUpdateInfo info)
        {
            var k = info.key;
            tr.localPosition = k.objectPosition;
            tr.localRotation = Quaternion.Euler(k.objectRotate);
            tr.localScale = k.objectScale;
        }

        private static void ApplyBlink(GameObject root, LaserUpdateInfo info)
        {
            var k = info.key;
            bool visible = true;

            if (k.blink != 0 && k.blinkPeriod > 0f)
            {
                // 按周期翻转可见性
                visible = ((Mathf.FloorToInt(info.localTime / k.blinkPeriod) & 1) == 0);
            }

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                r.enabled = visible;
        }

        private static void RegisterAllChildrenToStageMap(GameObject root)
        {
            var stage = Director.instance ? Director.instance._stageController : null;
            if (stage == null || stage.StageObjectMap == null) return;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string key = t.name.Replace("(Clone)", "");
                if (!stage.StageObjectMap.ContainsKey(key))
                    stage.StageObjectMap.Add(key, t.gameObject);
            }
        }
    }
}

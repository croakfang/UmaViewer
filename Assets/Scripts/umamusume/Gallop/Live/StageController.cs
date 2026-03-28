using Gallop.Live.Cutt;
using System;
using System.Collections.Generic;
using UnityEngine;


namespace Gallop.Live
{
    [Serializable]
    public class StageObjectUnit
    {
        public string UnitName;
        public GameObject[] ChildObjects;
        public string[] _childObjectNames;
    }

    public class StageController : MonoBehaviour
    {   
        int _logEvery = 30; // 每30次刷一次
        int _logCount = 0;
        public List<GameObject> _stageObjects;
        public StageObjectUnit[] _stageObjectUnits;
        public Dictionary<string, StageObjectUnit> StageObjectUnitMap = new Dictionary<string, StageObjectUnit>();
        public Dictionary<string, GameObject> StageObjectMap = new Dictionary<string, GameObject>();
        public Dictionary<string, Transform> StageParentMap = new Dictionary<string, Transform>();
        [SerializeField] private bool _autoAddBlinkDriver = true;


        private void Awake()
        {
            // 自动挂载驱动组件（如果尚未存在）
            AutoAddDriver("StageBlinkLightDriver");
            AutoAddDriver("StageLaserDriver");
            AutoAddDriver("StageLensFlareDriver");

            // 初始化舞台对象映射
            InitializeStage();

            Debug.Log("[StageController] stage parts = " +
                string.Join(", ", _stageObjects.ConvertAll(o => o ? o.name : "<null>")));

            // 注册 Director 事件回调（仅一次）
            if (Director.instance)
            {
                Director.instance._stageController = this;
                Director.instance._liveTimelineControl.OnUpdateTransform += UpdateTransform;
                Director.instance._liveTimelineControl.OnUpdateObject += UpdateObject;
            }
        }
private void AutoAddDriver(string shortTypeName)
{
    var t = Type.GetType($"{shortTypeName}, Assembly-CSharp")
         ?? Type.GetType($"Gallop.Live.{shortTypeName}, Assembly-CSharp");

    if (t != null && GetComponent(t) == null)
        gameObject.AddComponent(t);
}
        private void OnDestroy()
        {
            if (Director.instance)
            {
                Director.instance._liveTimelineControl.OnUpdateTransform -= UpdateTransform;
                Director.instance._liveTimelineControl.OnUpdateObject -= UpdateObject;
            }
        }

        public void InitializeStage()
        {
            foreach (GameObject stage_part in _stageObjects)
            {
                // 跳过 AssetBundle 中无法解析的 null 引用
                if (stage_part == null)
                {
                    Debug.LogWarning("[StageController] 跳过 _stageObjects 中的 null 条目");
                    continue;
                }

                var instance = Instantiate(stage_part, transform);

                // 统计并警告实例化对象上的 missing script 数量
                int missingCount = CountMissingScripts(instance);
                if (missingCount > 0)
                {
                    Debug.LogWarning($"[StageController] '{stage_part.name}' 实例化后有 {missingCount} 个 missing script 组件");
                }

                foreach (var child in instance.GetComponentsInChildren<Transform>(true))
                {
                    if (!StageObjectMap.ContainsKey(child.name))
                    {
                        if (child.name.IndexOf("light", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            child.gameObject.SetActive(true);
                            // 不要 continue，让它继续走到 Add(tmp_name, ...)
                        }

                        var tmp_name = child.name.Replace("(Clone)", "");
                        StageObjectMap.Add(tmp_name, child.gameObject);
                    }
                }
            }

            foreach (var unit in _stageObjectUnits)
            {
                if (!StageObjectUnitMap.ContainsKey(unit.UnitName))
                {
                    StageObjectUnitMap.Add(unit.UnitName, unit);
                }
            }
        }

        public void UpdateObject(ref ObjectUpdateInfo updateInfo) {

            if (updateInfo.data == null)
            {
                return;
            }
            if (StageObjectMap.TryGetValue(updateInfo.data.name, out GameObject gameObject))
            {   
                
                gameObject.SetActive(updateInfo.renderEnable);

                Transform attach_transform = null;
                switch (updateInfo.AttachTarget)
                {
                    case AttachType.None:
                        if(StageParentMap.TryGetValue(updateInfo.data.name, out Transform parentTransform))
                        {
                            attach_transform = parentTransform;
                        }
                        break;
                    case AttachType.Character:
                        var chara = Director.instance.CharaContainerScript[updateInfo.CharacterPosition];
                        if (chara)
                        {
                            attach_transform = chara.transform;
                        }
                        break;
                    case AttachType.Camera:
                        attach_transform = Director.instance.MainCameraTransform;
                        break;
                }
                if (gameObject.transform.parent != attach_transform)
                {
                    gameObject.transform.SetParent(attach_transform);
                }

                if (updateInfo.data.enablePosition)
                {
                    gameObject.transform.localPosition = updateInfo.updateData.position;
                }
                if (updateInfo.data.enableRotate)
                {
                    gameObject.transform.localRotation = updateInfo.updateData.rotation;
                }
                if (updateInfo.data.enableScale)
                {
                    gameObject.transform.localScale = updateInfo.updateData.scale;
                }
            }
            
        }

        public void UpdateTransform(ref TransformUpdateInfo updateInfo)
{
    if (updateInfo.data == null)
        return;

    // （可选）只盯某些名字看它到底有没有在发
    // if (updateInfo.data.name.Contains("laser") || updateInfo.data.name.Contains("spot"))
    //     Debug.Log($"[Stage] UpdateTransform name={updateInfo.data.name} pos={updateInfo.updateData.position} rot={updateInfo.updateData.rotation.eulerAngles}");

    // 1) 优先走你原来的 Unit 映射（兼容你之前的结构）
    if (StageObjectUnitMap.TryGetValue(updateInfo.data.name, out StageObjectUnit objectUnit) &&
        objectUnit.ChildObjects != null && objectUnit.ChildObjects.Length > 0)
    {
        foreach (var child in objectUnit.ChildObjects)
        {
            if (child == null) continue;

            // 这里 child.name 通常就是实际对象名
            if (StageObjectMap.TryGetValue(child.name, out GameObject go) && go != null)
            {
                ApplyTransformTo(go.transform, updateInfo);
            }
        }
        return;
    }

    // 2) ✅ fallback：TransformList 的 name 直接对应 StageObjectMap 的 key
    //    很多灯/灯头/激光 pivot 就是这种情况，所以你之前会“完全不动”
    if (StageObjectMap.TryGetValue(updateInfo.data.name, out GameObject directGo) && directGo != null)
    {
        ApplyTransformTo(directGo.transform, updateInfo);
        return;
    }

    // 3) 再试一次去掉 (Clone)
    string key2 = updateInfo.data.name.Replace("(Clone)", "");
    if (StageObjectMap.TryGetValue(key2, out GameObject directGo2) && directGo2 != null)
    {
        ApplyTransformTo(directGo2.transform, updateInfo);
        return;
    }

    // 找不到就算了（必要时你可以在这里打印一次看看到底缺哪些名字）
    // Debug.LogWarning($"[Stage] UpdateTransform target not found: {updateInfo.data.name}");
}

private static void ApplyTransformTo(Transform tr, TransformUpdateInfo updateInfo)
{
    // 这三个开关来自 sheet.transformList[i] 的 enablePosition/Rotate/Scale
    if (updateInfo.data.enablePosition)
        tr.localPosition = updateInfo.updateData.position;

    if (updateInfo.data.enableRotate)
        tr.localRotation = updateInfo.updateData.rotation;

    if (updateInfo.data.enableScale)
        tr.localScale = updateInfo.updateData.scale;
}

        /// <summary>
        /// 递归统计 GameObject 及其子对象上 missing script（null Component）的数量。
        /// 用于在实例化 AssetBundle prefab 后输出一次性汇总日志，替代大量重复警告。
        /// </summary>
        public static int CountMissingScripts(GameObject root)
        {
            if (root == null) return 0;
            int count = 0;

            // missing script 的 Component 引用会变成 null
            var components = root.GetComponents<Component>();
            foreach (var c in components)
            {
                if (c == null) count++;
            }

            // 递归处理子对象
            foreach (Transform child in root.transform)
            {
                count += CountMissingScripts(child.gameObject);
            }
            return count;
        }

    }
}
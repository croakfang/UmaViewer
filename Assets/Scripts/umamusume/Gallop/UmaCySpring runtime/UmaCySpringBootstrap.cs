using System.Collections.Generic;
using UnityEngine;

namespace Gallop.UmaCySpring
{
    [DisallowMultipleComponent]
    public class UmaCySpringBootstrap : MonoBehaviour
    {
        public CySpringDataContainer Container;
        public bool VerboseLog = true;

        private readonly List<UmaCySpringCollisionRuntimeData> _runtimeCollisions = new List<UmaCySpringCollisionRuntimeData>();
        private Transform _searchRoot;
        private Dictionary<string, Transform> _boneFastMap;

        public IReadOnlyList<UmaCySpringCollisionRuntimeData> RuntimeCollisions => _runtimeCollisions;

        public void BuildCollisionRuntime()
        {
            if (Container == null)
                Container = GetComponent<CySpringDataContainer>();
            if (Container == null)
            {
                Debug.LogError("[UmaCySpringBootstrap] Missing CySpringDataContainer.");
                return;
            }

            var owner = GetComponentInParent<UmaContainerCharacter>();
            _searchRoot = owner != null ? owner.transform : transform.root;
            _boneFastMap = BuildFastBoneMap(_searchRoot);

            ClearOldRuntimeNodes();
            _runtimeCollisions.Clear();

            if (Container.collisionParam == null)
                return;

            foreach (var src in Container.collisionParam)
            {
                if (src == null)
                    continue;

                Transform attachBone = UmaCySpringResolver.Resolve(_searchRoot, _boneFastMap, src._targetObjectName);
                if (attachBone == null)
                {
                    if (VerboseLog)
                        Debug.LogWarning($"[UmaCySpringBootstrap] collision bind fail: {src._collisionName} -> {src._targetObjectName}");
                    continue;
                }

                GameObject node = new GameObject(src._collisionName + "__UmaRuntime");
                node.transform.SetParent(attachBone, false);

                var runtime = new UmaCySpringCollisionRuntimeData
                {
                    Name = src._collisionName,
                    Type = ConvertType(src),
                    IsInner = src._isInner,
                    SourceRadius = src._radius,
                    SourceOffsetA = src._offset,
                    SourceOffsetB = src._offset2,
                    SourceNormal = src._normal,
                    AttachBone = attachBone,
                    RuntimeTransform = node.transform,
                    TargetNameOrPath = src._targetObjectName,
                };

                runtime.ScaleParams(Container.UseCorrectScaleCalc);
                runtime.ApplyTransform();
                _runtimeCollisions.Add(runtime);

                if (VerboseLog)
                    Debug.Log($"[UmaCySpringBootstrap] collision ok: {runtime.Name} -> {UmaCySpringResolver.GetPath(attachBone)}");
            }
        }

        private void ClearOldRuntimeNodes()
        {
            List<Transform> toDelete = new List<Transform>();
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name.EndsWith("__UmaRuntime"))
                    toDelete.Add(t);
            }

            foreach (Transform t in toDelete)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(t.gameObject);
                else Destroy(t.gameObject);
#else
                Destroy(t.gameObject);
#endif
            }
        }

        private static UmaCySpringCollisionType ConvertType(CySpringCollisionData src)
        {
            switch (src._type)
            {
                case CySpringCollisionData.CollisionType.Sphere:
                    return UmaCySpringCollisionType.Sphere;
                case CySpringCollisionData.CollisionType.Capsule:
                    return UmaCySpringCollisionType.Capsule;
                case CySpringCollisionData.CollisionType.Plane:
                    return UmaCySpringCollisionType.Plane;
                default:
                    return UmaCySpringCollisionType.None;
            }
        }

        private static Dictionary<string, Transform> BuildFastBoneMap(Transform root)
        {
            var map = new Dictionary<string, Transform>();
            if (root == null)
                return map;

            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!map.ContainsKey(t.name))
                    map.Add(t.name, t);
            }
            return map;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace Gallop
{
    public class CySpringConnectedBoneDriver : MonoBehaviour
    {
        public CySpringDataContainer Container;

        [Range(0f, 2f)]
        public float GlobalIntensityScale = 0.4f;

        [Range(0f, 1f)]
        public float PositionBlend = 0.09f;

        public bool EnableDriver = true;

        private readonly List<BindingRuntime> _bindings = new List<BindingRuntime>();
        private readonly Dictionary<string, List<Transform>> _boneMap = new Dictionary<string, List<Transform>>();

        private class BindingRuntime
        {
            public DynamicBone dynamicBone;
            public int particleIndex;

            public Transform particle;
            public Transform parent;
            public Transform connectedBone;

            public float intensity;
            public bool isFold;

            public Vector3 restLocalDir;
            public float restLength;

            public Quaternion connectedBoneInitialRotation;
        }

        private void Awake()
        {
            if (Container == null)
                Container = GetComponent<CySpringDataContainer>();
        }

        public void Rebuild()
        {
            _bindings.Clear();
            _boneMap.Clear();

            if (Container == null)
                return;

            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (!_boneMap.TryGetValue(t.name, out List<Transform> list))
                {
                    list = new List<Transform>();
                    _boneMap.Add(t.name, list);
                }

                list.Add(t);
            }

            if (Container.ConnectedBoneList == null || Container.ConnectedBoneList.Count == 0)
                return;

            foreach (var item in Container.ConnectedBoneList)
            {
                if (item == null)
                    continue;

                // Bone1 = 主 spring 节点
                // Bone2 = connected / 相邻参考节点
                string springBoneName = GetBone1Name(item);
                string connectedBoneName = GetBone2Name(item);
                float intensity = GetIntensity(item);
                bool isFold = GetIsFold(item);

                if (string.IsNullOrEmpty(springBoneName) || string.IsNullOrEmpty(connectedBoneName))
                    continue;

                Transform springBone = FindBone(springBoneName);
                Transform connectedBone = FindBone(connectedBoneName);

                if (springBone == null || connectedBone == null)
                    continue;

                if (!TryFindParticleBinding(springBone, out DynamicBone db, out int particleIndex))
                    continue;

                if (db == null || db.Particles == null || particleIndex < 0 || particleIndex >= db.Particles.Count)
                    continue;

                var p = db.Particles[particleIndex];
                if (p == null)
                    continue;

                if (p.m_ParentIndex < 0 || p.m_ParentIndex >= db.Particles.Count)
                    continue;

                var parentParticle = db.Particles[p.m_ParentIndex];
                if (parentParticle == null || parentParticle.m_Transform == null)
                    continue;

                Vector3 restDir = springBone.position - parentParticle.m_Transform.position;
                float restLen = restDir.magnitude;
                if (restLen < 1e-5f)
                    continue;

                _bindings.Add(new BindingRuntime
                {
                    dynamicBone = db,
                    particleIndex = particleIndex,
                    particle = springBone,
                    parent = parentParticle.m_Transform,
                    connectedBone = connectedBone,
                    intensity = intensity,
                    isFold = isFold,
                    restLocalDir = parentParticle.m_Transform.InverseTransformDirection(restDir.normalized),
                    restLength = restLen,
                    connectedBoneInitialRotation = connectedBone.rotation
                });
            }
        }

        private void LateUpdate()
        {
            if (!EnableDriver || Container == null || _bindings.Count == 0)
                return;

            for (int i = 0; i < _bindings.Count; i++)
                ApplyBinding(_bindings[i]);
        }

        private void ApplyBinding(BindingRuntime b)
        {
            if (b == null || b.dynamicBone == null || b.connectedBone == null || b.parent == null)
                return;

            if (b.particleIndex <= 0 || b.particleIndex >= b.dynamicBone.Particles.Count)
                return;

            var p = b.dynamicBone.Particles[b.particleIndex];
            if (p == null)
                return;

            float chainFactor = 1f;
            if (b.particleIndex >= 2) chainFactor = 0.7f;
            if (b.particleIndex >= 3) chainFactor = 0.4f;
            if (b.particleIndex >= 4) chainFactor = 0.25f;

            float weight = Mathf.Max(0f, b.intensity * GlobalIntensityScale * chainFactor);
            if (weight <= 0f)
                return;

            Quaternion delta = b.connectedBone.rotation * Quaternion.Inverse(b.connectedBoneInitialRotation);

            Vector3 baseDirWorld = b.parent.TransformDirection(b.restLocalDir);
            Vector3 targetDir = delta * baseDirWorld;
            if (targetDir.sqrMagnitude < 1e-8f)
                return;

            Vector3 targetPos = b.parent.position + targetDir.normalized * b.restLength;

            float posBlend = Mathf.Clamp01(weight * PositionBlend);
            if (b.isFold)
                posBlend *= 0.5f;

            p.m_Position = Vector3.Lerp(p.m_Position, targetPos, posBlend);
        }

        private bool TryFindParticleBinding(Transform springBone, out DynamicBone db, out int particleIndex)
        {
            db = null;
            particleIndex = -1;

            if (Container == null || Container.DynamicBones == null)
                return false;

            foreach (DynamicBone dynamic in Container.DynamicBones)
            {
                if (dynamic == null || dynamic.Particles == null)
                    continue;

                for (int i = 0; i < dynamic.Particles.Count; i++)
                {
                    var p = dynamic.Particles[i];
                    if (p != null && p.m_Transform == springBone)
                    {
                        db = dynamic;
                        particleIndex = i;
                        return true;
                    }
                }
            }

            return false;
        }

        private Transform FindBone(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
                return null;

            if (boneName.Contains("/"))
            {
                GameObject goByPath = global::CySpring.FindGameObject(gameObject, boneName);
                return goByPath != null ? goByPath.transform : null;
            }

            if (_boneMap.TryGetValue(boneName, out List<Transform> list))
            {
                if (list != null && list.Count == 1)
                    return list[0];
            }

            GameObject go = global::CySpring.FindGameObject(gameObject, boneName);
            return go != null ? go.transform : null;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null)
                return null;

            if (root.name == name)
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static string GetBone1Name(object item)
        {
            var t = item.GetType();

            var f = t.GetField("Bone1");
            if (f != null) return f.GetValue(item) as string;

            f = t.GetField("_bone1");
            if (f != null) return f.GetValue(item) as string;

            var p = t.GetProperty("Bone1");
            if (p != null) return p.GetValue(item, null) as string;

            return null;
        }

        private static string GetBone2Name(object item)
        {
            var t = item.GetType();

            var f = t.GetField("Bone2");
            if (f != null) return f.GetValue(item) as string;

            f = t.GetField("_bone2");
            if (f != null) return f.GetValue(item) as string;

            var p = t.GetProperty("Bone2");
            if (p != null) return p.GetValue(item, null) as string;

            return null;
        }

        private static float GetIntensity(object item)
        {
            var t = item.GetType();

            var f = t.GetField("Intensity");
            if (f != null) return (float)f.GetValue(item);

            f = t.GetField("_intensity");
            if (f != null) return (float)f.GetValue(item);

            var p = t.GetProperty("Intensity");
            if (p != null) return (float)p.GetValue(item, null);

            return 0f;
        }

        private static bool GetIsFold(object item)
        {
            var t = item.GetType();

            var f = t.GetField("IsFold");
            if (f != null) return (bool)f.GetValue(item);

            f = t.GetField("_isFold");
            if (f != null) return (bool)f.GetValue(item);

            var p = t.GetProperty("IsFold");
            if (p != null) return (bool)p.GetValue(item, null);

            return false;
        }
    }
}
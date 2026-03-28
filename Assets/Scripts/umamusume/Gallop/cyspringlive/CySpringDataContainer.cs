using System.Collections.Generic;
using UnityEngine;

namespace Gallop
{
    public class CySpringDataContainer : MonoBehaviour
    {
        public List<CySpringCollisionData> collisionParam;
        public List<CySpringParamDataElement> springParam;
        public List<ConnectedBoneData> ConnectedBoneList;
        public bool enableVerticalWind;
        public bool enableHorizontalWind;
        public float centerWindAngleSlow;
        public float centerWindAngleFast;
        public float verticalCycleSlow;
        public float horizontalCycleSlow;
        public float verticalAngleWidthSlow;
        public float horizontalAngleWidthSlow;
        public float verticalCycleFast;
        public float horizontalCycleFast;
        public float verticalAngleWidthFast;
        public float horizontalAngleWidthFast;
        public bool IsEnableHipMoveParam;
        public float HipMoveInfluenceDistance;
        public float HipMoveInfluenceMaxDistance;
        public bool UseCorrectScaleCalc;

        public List<DynamicBone> DynamicBones = new List<DynamicBone>();

        [SerializeField]
        private bool _verboseResolveLog = true;

        [SerializeField]
        private bool _enableConnectedDriverInLive = true;

        private enum SpringKind
        {
            Hair,
            Ear,
            Skirt,
            Jacket,
            Tail,
            Bust,
            Cloth,
            Other
        }

        private static SpringKind DetectSpringKind(string springBoneName, string ownerName)
        {
            string n = (springBoneName ?? "").ToLower();
            string o = (ownerName ?? "").ToLower();

            if (n.Contains("tail") || o.Contains("tail"))
                return SpringKind.Tail;

            if (n.Contains("ear") || n.Contains("mimi"))
                return SpringKind.Ear;

            if (n.Contains("skirt"))
                return SpringKind.Skirt;

            if (n.Contains("jacket") || n.Contains("coat") || n.Contains("cloth") || n.Contains("mant"))
                return SpringKind.Jacket;

            if (n.Contains("hair") || n.Contains("kami") || n.Contains("bang"))
                return SpringKind.Hair;

            if (n.Contains("bust") || n.Contains("breast"))
                return SpringKind.Bust;

            return SpringKind.Other;
        }

        private static float MapStiffness(float src, SpringKind kind)
        {
            float value = 1f / Mathf.Max(src, 0.001f);

            switch (kind)
            {
                case SpringKind.Tail:
                    return Mathf.Clamp(value * 8.0f, 0.08f, 0.45f);
                case SpringKind.Ear:
                    return Mathf.Clamp(value * 7.5f, 0.10f, 0.42f);
                case SpringKind.Hair:
                    return Mathf.Clamp(value * 6.5f, 0.05f, 0.35f);
                case SpringKind.Skirt:
                    return Mathf.Clamp(value * 5.0f, 0.03f, 0.22f);
                case SpringKind.Bust:
                    return Mathf.Clamp(value * 9.0f, 0.20f, 0.55f);
                case SpringKind.Jacket:
                case SpringKind.Cloth:
                    return Mathf.Clamp(value * 4.5f, 0.03f, 0.20f);
                default:
                    return Mathf.Clamp(value * 5.0f, 0.04f, 0.25f);
            }
        }

        private static float MapElasticity(float src, SpringKind kind)
        {
            float value = 1f / Mathf.Max(src, 0.001f);

            switch (kind)
            {
                case SpringKind.Tail:
                    return Mathf.Clamp(value * 6.0f, 0.04f, 0.30f);
                case SpringKind.Ear:
                    return Mathf.Clamp(value * 6.0f, 0.06f, 0.28f);
                case SpringKind.Hair:
                    return Mathf.Clamp(value * 5.0f, 0.04f, 0.25f);
                case SpringKind.Skirt:
                    return Mathf.Clamp(value * 4.0f, 0.03f, 0.18f);
                case SpringKind.Bust:
                    return Mathf.Clamp(value * 7.5f, 0.10f, 0.32f);
                case SpringKind.Jacket:
                case SpringKind.Cloth:
                    return Mathf.Clamp(value * 3.5f, 0.03f, 0.15f);
                default:
                    return Mathf.Clamp(value * 4.0f, 0.03f, 0.18f);
            }
        }

        private static float MapDamping(SpringKind kind)
        {
            switch (kind)
            {
                case SpringKind.Tail: return 0.10f;
                case SpringKind.Ear: return 0.24f;
                case SpringKind.Hair: return 0.14f;
                case SpringKind.Skirt: return 0.18f;
                case SpringKind.Jacket: return 0.20f;
                case SpringKind.Bust: return 0.30f;
                case SpringKind.Cloth: return 0.18f;
                default: return 0.16f;
            }
        }

        private static float MapInert(float moveSpringApplyRate, SpringKind kind)
        {
            float baseValue = Mathf.Clamp01(moveSpringApplyRate * 0.35f);

            switch (kind)
            {
                case SpringKind.Tail: return Mathf.Clamp(baseValue, 0.08f, 0.30f);
                case SpringKind.Ear: return Mathf.Clamp(baseValue, 0.03f, 0.12f);
                case SpringKind.Hair: return Mathf.Clamp(baseValue, 0.06f, 0.22f);
                case SpringKind.Skirt: return Mathf.Clamp(baseValue, 0.04f, 0.16f);
                case SpringKind.Jacket: return Mathf.Clamp(baseValue, 0.04f, 0.14f);
                case SpringKind.Bust: return Mathf.Clamp(baseValue, 0.02f, 0.08f);
                case SpringKind.Cloth: return Mathf.Clamp(baseValue, 0.04f, 0.16f);
                default: return Mathf.Clamp(baseValue, 0.04f, 0.18f);
            }
        }

        private static float MapRadius(float srcRadius, SpringKind kind, bool isChild)
        {
            float mul = 1.0f;

            switch (kind)
            {
                case SpringKind.Tail:
                    mul = isChild ? 1.00f : 0.95f;
                    break;
                case SpringKind.Ear:
                    mul = isChild ? 0.90f : 0.85f;
                    break;
                case SpringKind.Hair:
                    mul = isChild ? 0.95f : 0.90f;
                    break;
                case SpringKind.Skirt:
                    mul = isChild ? 1.10f : 1.05f;
                    break;
                case SpringKind.Bust:
                    mul = isChild ? 0.90f : 0.85f;
                    break;
                case SpringKind.Jacket:
                case SpringKind.Cloth:
                    mul = isChild ? 1.05f : 1.00f;
                    break;
                default:
                    mul = isChild ? 1.00f : 0.95f;
                    break;
            }

            return Mathf.Max(srcRadius * mul, 0.002f);
        }

        private static float MapGravityY(float gravity, SpringKind kind)
        {
            if (Mathf.Abs(gravity) < 0.0001f)
                return 0f;

            float scale = 0.02f;
            float limit = 0.6f;

            switch (kind)
            {
                case SpringKind.Ear:
                    scale = 0.014f;
                    limit = 0.35f;
                    break;
                case SpringKind.Bust:
                    scale = 0.010f;
                    limit = 0.25f;
                    break;
            }

            float g = -gravity * scale;
            return Mathf.Clamp(g, -limit, limit);
        }

        private static Vector3 MapSpringForce(Vector3 springForce, SpringKind kind)
        {
            float scale = 0.01f;

            switch (kind)
            {
                case SpringKind.Ear:
                    scale = 0.006f;
                    break;
                case SpringKind.Bust:
                    scale = 0.004f;
                    break;
            }

            return springForce * scale;
        }

        private static float AdjustColliderRadius(string colliderName, float radius)
        {
            string n = (colliderName ?? "").ToLower();

            if (n == "col_b_hip_tail")
                return radius * 1.00f;
            if (n == "col_b_chest_tail")
                return radius * 1.00f;
            if (n.Contains("col_b_hip_skirt") || n.Contains("col_b_hip_mskirt"))
                return radius * 1.00f;
            if (n.Contains("col_b_hip_jacket"))
                return radius * 1.00f;
            if (n == "col_elbow_r_hair" || n == "col_elbow_l_hair")
                return radius * 0.55f;

            return radius * 1.00f;
        }

        private static void AddColliderIfMissing(List<DynamicBoneColliderBase> colliderList, DynamicBoneColliderBase collider)
        {
            if (collider != null && colliderList != null && !colliderList.Contains(collider))
                colliderList.Add(collider);
        }

        private static Quaternion CreatePlaneRotation(Vector3 normal)
        {
            if (normal.sqrMagnitude < 1e-6f)
                return Quaternion.identity;

            return Quaternion.FromToRotation(Vector3.up, normal.normalized);
        }

        private static void DestroyObjectSafe(Object obj)
        {
            if (obj == null)
                return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(obj);
            else
                Destroy(obj);
#else
            Destroy(obj);
#endif
        }

        private static string GetLeafName(string nameOrPath)
        {
            if (string.IsNullOrEmpty(nameOrPath))
                return nameOrPath;

            int index = nameOrPath.LastIndexOf('/');
            return index >= 0 ? nameOrPath.Substring(index + 1) : nameOrPath;
        }

        private Transform ResolveBone(Dictionary<string, Transform> bones, string nameOrPath)
        {
            if (string.IsNullOrEmpty(nameOrPath))
                return null;

            Transform searchRoot = GetResolveSearchRoot();

            // 路径优先，尽量更接近原版
            if (nameOrPath.Contains("/"))
            {
                GameObject goByPath = global::CySpring.FindGameObject(searchRoot.gameObject, nameOrPath);
                if (goByPath != null)
                    return goByPath.transform;
            }

            // 再走 fast map
            if (bones != null && bones.TryGetValue(nameOrPath, out Transform direct) && direct != null)
                return direct;

            // 再递归按名字找
            GameObject go = global::CySpring.FindGameObject(searchRoot.gameObject, nameOrPath);
            if (go != null)
                return go.transform;

            // 最后再试叶子名
            string leaf = GetLeafName(nameOrPath);
            if (!string.IsNullOrEmpty(leaf))
            {
                if (bones != null && bones.TryGetValue(leaf, out Transform leafBone) && leafBone != null)
                    return leafBone;

                GameObject goLeaf = global::CySpring.FindGameObject(searchRoot.gameObject, leaf);
                if (goLeaf != null)
                    return goLeaf.transform;
            }

            return null;
        }

        private Transform ResolveCollider(Dictionary<string, Transform> colliders, string colliderName)
        {
            if (string.IsNullOrEmpty(colliderName))
                return null;

            Transform searchRoot = GetResolveSearchRoot();

            if (colliders != null && colliders.TryGetValue(colliderName, out Transform t) && t != null)
                return t;

            GameObject go = global::CySpring.FindGameObject(searchRoot.gameObject, colliderName);
            return go != null ? go.transform : null;
        }

        private Vector3 CorrectLocalOffset(Transform parentBone, Vector3 offset)
        {
            if (!UseCorrectScaleCalc || parentBone == null)
                return offset;

            Vector3 s = parentBone.lossyScale;
            return new Vector3(
                Mathf.Abs(s.x) > 1e-6f ? offset.x / s.x : offset.x,
                Mathf.Abs(s.y) > 1e-6f ? offset.y / s.y : offset.y,
                Mathf.Abs(s.z) > 1e-6f ? offset.z / s.z : offset.z
            );
        }

        private float CorrectLocalRadius(Transform parentBone, float radius)
        {
            if (!UseCorrectScaleCalc || parentBone == null)
                return radius;

            float sx = Mathf.Abs(parentBone.lossyScale.x);
            return sx > 1e-6f ? radius / sx : radius;
        }

        private void RemoveOldColliderObject(Transform bone, string colliderName)
        {
            if (bone == null || string.IsNullOrEmpty(colliderName))
                return;

            Transform old = bone.Find(colliderName);
            if (old != null)
                DestroyObjectSafe(old.gameObject);
        }

        private void RemoveOldDynamicBones(Transform bone)
        {
            if (bone == null)
                return;

            DynamicBone[] oldDynamics = bone.GetComponents<DynamicBone>();
            if (oldDynamics == null || oldDynamics.Length == 0)
                return;

            foreach (DynamicBone old in oldDynamics)
                DestroyObjectSafe(old);

            CySpringPostSpringLimiter legacyLimiter = bone.GetComponent<CySpringPostSpringLimiter>();
            if (legacyLimiter != null)
                legacyLimiter.enabled = false;
        }

        public Dictionary<string, Transform> InitiallizeCollider(Dictionary<string, Transform> bones)
        {
            var colliders = new Dictionary<string, Transform>();
            var missingTargetBones = new List<string>();

            if (collisionParam == null)
                return colliders;

            foreach (CySpringCollisionData collider in collisionParam)
            {
                if (collider == null)
                    continue;

                Transform bone = ResolveBone(bones, collider._targetObjectName);
                if (bone == null)
                {
                    missingTargetBones.Add($"{collider._collisionName} -> {collider._targetObjectName}");
                    continue;
                }

                RemoveOldColliderObject(bone, collider._collisionName);

                var child = new GameObject(collider._collisionName);
                child.transform.SetParent(bone, false);
                child.transform.localPosition = Vector3.zero;
                child.transform.localRotation = Quaternion.identity;
                child.transform.localScale = Vector3.one;

                if (!colliders.ContainsKey(child.name))
                    colliders.Add(child.name, child.transform);

                collider._transform = child.transform;

                float radius = AdjustColliderRadius(collider._collisionName, collider._radius);
                radius = CorrectLocalRadius(bone, radius);

                Vector3 offset0 = CorrectLocalOffset(bone, collider._offset);
                Vector3 offset1 = CorrectLocalOffset(bone, collider._offset2);

                switch (collider._type)
                {
                    case CySpringCollisionData.CollisionType.Capsule:
                        {
                            var dynamic = child.AddComponent<DynamicBoneCollider>();
                            dynamic.ColliderName = collider._collisionName;

                            child.transform.localPosition = (offset0 + offset1) * 0.5f;
                            Vector3 capsuleAxis = offset1 - offset0;
                            child.transform.localRotation = capsuleAxis.sqrMagnitude > 1e-8f
                                ? Quaternion.FromToRotation(Vector3.forward, capsuleAxis.normalized)
                                : Quaternion.identity;

                            dynamic.m_Center = Vector3.zero;
                            dynamic.m_Direction = DynamicBoneColliderBase.Direction.Z;
                            dynamic.m_Height = (offset0 - offset1).magnitude + radius * 2f;
                            dynamic.m_Radius = radius;
                            dynamic.m_Bound = collider._isInner
                                ? DynamicBoneColliderBase.Bound.Inside
                                : DynamicBoneColliderBase.Bound.Outside;
                            break;
                        }

                    case CySpringCollisionData.CollisionType.Sphere:
                        {
                            var sphereDynamic = child.AddComponent<DynamicBoneCollider>();
                            sphereDynamic.ColliderName = collider._collisionName;

                            child.transform.localPosition = offset0;
                            child.transform.localRotation = Quaternion.identity;

                            sphereDynamic.m_Center = Vector3.zero;
                            sphereDynamic.m_Radius = radius;
                            sphereDynamic.m_Height = 0f;
                            sphereDynamic.m_Bound = collider._isInner
                                ? DynamicBoneColliderBase.Bound.Inside
                                : DynamicBoneColliderBase.Bound.Outside;
                            break;
                        }

                    case CySpringCollisionData.CollisionType.Plane:
                        {
                            var planeDynamic = child.AddComponent<DynamicBonePlaneCollider>();
                            planeDynamic.ColliderName = collider._collisionName;

                            child.transform.localPosition = offset0;
                            child.transform.localRotation = CreatePlaneRotation(collider._normal.normalized);
                            planeDynamic.m_Center = Vector3.zero;
                            planeDynamic.m_Direction = DynamicBoneColliderBase.Direction.Y;
                            planeDynamic.m_Bound = collider._isInner
                                ? DynamicBoneColliderBase.Bound.Inside
                                : DynamicBoneColliderBase.Bound.Outside;
                            break;
                        }

                    case CySpringCollisionData.CollisionType.None:
                    default:
                        break;
                }
            }

            if (_verboseResolveLog && missingTargetBones.Count > 0)
            {
                Debug.LogWarning(
                    $"[CySpringDataContainer] {gameObject.name} collider target resolve failed: {missingTargetBones.Count}\n" +
                    string.Join("\n", missingTargetBones));
            }

            return colliders;
        }

        public void InitializePhysics(Dictionary<string, Transform> bones, Dictionary<string, Transform> colliders)
        {
            DynamicBones.Clear();

            var missingRootBones = new List<string>();
            var missingChildBones = new List<string>();
            var missingColliders = new List<string>();

            if (springParam == null)
                return;

            foreach (CySpringParamDataElement spring in springParam)
            {
                if (spring == null)
                    continue;

                Transform bone = ResolveBone(bones, spring._boneName);
                if (bone == null)
                {
                    missingRootBones.Add(spring._boneName);
                    continue;
                }

                RemoveOldDynamicBones(bone);

                SpringKind kind = DetectSpringKind(spring._boneName, gameObject.name);

                var dynamic = bone.gameObject.AddComponent<DynamicBone>();
                dynamic.m_Root = bone;

                bool useEndBone =
                    spring._needSimulateEndBone &&
                    (kind == SpringKind.Hair || kind == SpringKind.Tail);
                dynamic.m_EndLength = useEndBone ? 1f : 0f;

                dynamic.m_Gravity = new Vector3(0f, MapGravityY(spring._gravity, kind), 0f);
                dynamic.m_Damping = MapDamping(kind);
                dynamic.m_Stiffness = MapStiffness(spring._stiffnessForce, kind);
                dynamic.m_Elasticity = MapElasticity(spring._dragForce, kind);
                dynamic.m_Radius = MapRadius(spring._collisionRadius, kind, false);
                dynamic.m_Inert = MapInert(spring.MoveSpringApplyRate, kind);
                dynamic.m_Force = MapSpringForce(spring._springForce, kind);

                bool useGarmentCollisionStabilizer =
                    kind == SpringKind.Skirt ||
                    kind == SpringKind.Jacket ||
                    kind == SpringKind.Cloth;

                dynamic.m_UseSweptCollision = useGarmentCollisionStabilizer;
                dynamic.m_RecheckCollisionAfterLength = useGarmentCollisionStabilizer;

                dynamic.m_LimitAngel_Min = spring._limitAngleMin;
                dynamic.m_LimitAngel_Max = spring._limitAngleMax;

                dynamic.SetupParticles();
                DynamicBones.Add(dynamic);

                if (spring._collisionNameList != null)
                {
                    foreach (string collisionName in spring._collisionNameList)
                    {
                        Transform t = ResolveCollider(colliders, collisionName);
                        if (t == null)
                        {
                            missingColliders.Add($"{spring._boneName} -> {collisionName}");
                            continue;
                        }

                        var collider = t.GetComponent<DynamicBoneColliderBase>();
                        if (collider != null)
                        {
                            for (int particleIndex = 1; particleIndex < dynamic.Particles.Count; particleIndex++)
                                AddColliderIfMissing(dynamic.Particles[particleIndex].m_Colliders, collider);
                        }
                    }
                }

                if (spring._childElements != null)
                {
                    foreach (var child in spring._childElements)
                    {
                        if (child == null)
                            continue;

                        Transform childBone = ResolveBone(bones, child._boneName);
                        if (childBone == null)
                        {
                            missingChildBones.Add($"{spring._boneName} -> {child._boneName}");
                            continue;
                        }

                        var tempParticle = dynamic.Particles.Find(p => p.m_Transform == childBone);
                        if (tempParticle == null)
                        {
                            missingChildBones.Add($"{spring._boneName} -> {child._boneName}");
                            continue;
                        }

                        SpringKind childKind = DetectSpringKind(child._boneName, gameObject.name);
                        bool useChildForceTransfer =
                            childKind == SpringKind.Hair ||
                            childKind == SpringKind.Tail;

                        tempParticle.m_Damping = MapDamping(childKind);
                        tempParticle.m_Stiffness = MapStiffness(child._stiffnessForce, childKind);
                        tempParticle.m_Elasticity = MapElasticity(child._dragForce, childKind);
                        tempParticle.m_Radius = MapRadius(child._collisionRadius, childKind, true);
                        tempParticle.m_Inert = MapInert(child.MoveSpringApplyRate, childKind);
                        tempParticle.m_AdditionalGravity = useChildForceTransfer
                            ? new Vector3(0f, MapGravityY(child._gravity, childKind), 0f) - dynamic.m_Gravity
                            : Vector3.zero;
                        tempParticle.m_AdditionalForce = useChildForceTransfer
                            ? MapSpringForce(child._springForce, childKind) - dynamic.m_Force
                            : Vector3.zero;

                        tempParticle.m_LimitAngel_Min = child._limitAngleMin;
                        tempParticle.m_LimitAngel_Max = child._limitAngleMax;

                        if (child._collisionNameList != null)
                        {
                            foreach (string collisionName in child._collisionNameList)
                            {
                                Transform t = ResolveCollider(colliders, collisionName);
                                if (t == null)
                                {
                                    missingColliders.Add($"{child._boneName} -> {collisionName}");
                                    continue;
                                }

                                var collider = t.GetComponent<DynamicBoneColliderBase>();
                                if (collider != null)
                                    AddColliderIfMissing(tempParticle.m_Colliders, collider);
                            }
                        }
                    }
                }

                var limiter = bone.gameObject.GetComponent<CySpringDeltaAngleLimiter>();
                if (limiter == null)
                    limiter = bone.gameObject.AddComponent<CySpringDeltaAngleLimiter>();

                limiter.RootDynamicBone = dynamic;
                limiter.EnableLimit = false;
            }

            var connectedDriver = gameObject.GetComponent<CySpringConnectedBoneDriver>();
            if (connectedDriver == null)
                connectedDriver = gameObject.AddComponent<CySpringConnectedBoneDriver>();

            connectedDriver.Container = this;
            var owner = GetComponentInParent<UmaContainerCharacter>();
            connectedDriver.EnableDriver = _enableConnectedDriverInLive || owner == null || !owner.IsLive;
            connectedDriver.Rebuild();

            if (missingRootBones.Count > 0 || missingChildBones.Count > 0 || missingColliders.Count > 0)
            {
                Debug.LogWarning(
                    $"[CySpringDataContainer] {gameObject.name} init issues. " +
                    $"Missing roots: {missingRootBones.Count}, missing children: {missingChildBones.Count}, missing colliders: {missingColliders.Count}\n" +
                    $"{string.Join("\n", missingRootBones)}\n" +
                    $"{string.Join("\n", missingChildBones)}\n" +
                    $"{string.Join("\n", missingColliders)}");
            }
        }

        public void EnablePhysics(bool isOn)
        {
            foreach (DynamicBone dynamic in DynamicBones)
            {
                if (dynamic != null)
                    dynamic.enabled = isOn;
            }
        }

        public void ResetPhysics()
        {
            foreach (DynamicBone dynamic in DynamicBones)
            {
                if (dynamic != null)
                    dynamic.ResetParticlesPosition();
            }
        }
        private Transform GetResolveSearchRoot()
        {
            var owner = GetComponentInParent<UmaContainerCharacter>();
            if (owner != null)
                return owner.transform;

            return transform.root;
        }
    }
}
using System.Collections;
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

        public Dictionary<string, Transform> InitiallizeCollider(Dictionary<string, Transform> bones)
        {
            var colliders = new Dictionary<string, Transform>();
            foreach (var data in collisionParam)
            {
                if (data._type == CySpringCollisionData.CollisionType.None ||
                    colliders.ContainsKey(data._collisionName) ||
                    !bones.TryGetValue(data._targetObjectName, out var bone)) continue;

                var child = new GameObject(data._collisionName);
                child.transform.SetParent(bone, false);
                AddDynamicCollider(child, data);

                colliders.Add(child.name, child.transform);
            }
            return colliders;
        }

        private static void AddDynamicCollider(GameObject child, CySpringCollisionData data)
        {
            DynamicBoneColliderBase shape;
            child.transform.localPosition = data._offset;
            if (data._type == CySpringCollisionData.CollisionType.Plane)
            {
                shape = child.AddComponent<DynamicBonePlaneCollider>();
                if (data._normal.sqrMagnitude > 0)
                    child.transform.localRotation = Quaternion.FromToRotation(Vector3.up, data._normal);
            }
            else
            {
                var round = child.AddComponent<DynamicBoneCollider>();
                round.m_Radius = data._radius;
                if (data._type == CySpringCollisionData.CollisionType.Capsule)
                {
                    var axis = data._offset2 - data._offset;
                    child.transform.localPosition += axis * 0.5f;
                    if (axis.sqrMagnitude > 0)
                        child.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
                    round.m_Height = axis.magnitude + 2f * round.m_Radius;
                }
                shape = round;
            }
            shape.ColliderName = data._collisionName;
            shape.m_Bound = data._isInner ? DynamicBoneColliderBase.Bound.Inside : DynamicBoneColliderBase.Bound.Outside;
        }

        public void InitializePhysics(Dictionary<string, Transform> bones, Dictionary<string, Transform> colliders)
        {
            DynamicBones.Clear();

            string nameLower = gameObject.name.ToLower();
            bool isTailObject = nameLower.Contains("tail");
            bool isHairObject = !isTailObject && nameLower.Contains("chr");

            foreach (CySpringParamDataElement spring in springParam)
            {
                if (!bones.TryGetValue(spring._boneName, out Transform bone)) continue;

                string boneNameLower = spring._boneName.ToLower();
                bool isEarSpring = isHairObject && boneNameLower.Contains("ear");
                bool isHairSpring = isHairObject && !isEarSpring;

                var dynamic = bone.gameObject.AddComponent<DynamicBone>();
                dynamic.m_Root = bone;
                dynamic.m_UpdateMode = DynamicBone.UpdateMode.Normal;
                dynamic.m_EndLength = spring._needSimulateEndBone ? 1f : 0f;

                dynamic.m_LimitAngel_Min = spring._limitAngleMin;
                dynamic.m_LimitAngel_Max = spring._limitAngleMax;

                if (isTailObject)
                {
                    dynamic.m_Damping = 0.05f;
                    dynamic.m_Elasticity = 0.2f;
                    dynamic.m_Stiffness = 0.1f;
                    dynamic.m_Inert = 0.5f;

                    dynamic.m_Gravity = Vector3.zero;
                    dynamic.m_Force = Vector3.zero;

                    dynamic.m_DampingDistrib = null;
                    dynamic.m_ElasticityDistrib = new AnimationCurve(
                        new Keyframe(0f, 1.6f),
                        new Keyframe(0.15f, 1.5f),
                        new Keyframe(0.45f, 0.5f),
                        new Keyframe(1f, 0.35f)
                    );
                    dynamic.m_StiffnessDistrib = new AnimationCurve(
                        new Keyframe(0f, 1.8f),
                        new Keyframe(0.15f, 1.6f),
                        new Keyframe(0.45f, 0.55f),
                        new Keyframe(1f, 0.3f)
                    );
                    dynamic.m_InertDistrib = null;
                    dynamic.m_RadiusDistrib = null;
                }
                else if (isEarSpring)
                {
                    dynamic.m_Damping = 0.15f;
                    dynamic.m_Elasticity = 0.35f;
                    dynamic.m_Stiffness = 0.7f;
                    dynamic.m_Inert = 0.0f;

                    dynamic.m_Gravity = Vector3.zero;
                    dynamic.m_Force = new Vector3(0f, -0.0005f, 0f);

                    dynamic.m_DampingDistrib = new AnimationCurve(
                        new Keyframe(0f, 1.0f),
                        new Keyframe(1f, 1.0f)
                    );
                    dynamic.m_ElasticityDistrib = new AnimationCurve(
                        new Keyframe(0f, 0.4f),
                        new Keyframe(0.3f, 0.7f),
                        new Keyframe(1f, 1.0f)
                    );
                    dynamic.m_StiffnessDistrib = new AnimationCurve(
                        new Keyframe(0f, 1.0f),
                        new Keyframe(0.3f, 0.9f),
                        new Keyframe(1f, 0.65f)
                    );
                    dynamic.m_InertDistrib = null;
                }
                else if (isHairSpring)
                {
                    dynamic.m_Damping = 0.3f;
                    dynamic.m_Elasticity = Mathf.Clamp01(50f / spring._dragForce);
                    dynamic.m_Stiffness = Mathf.Clamp01(100f / spring._stiffnessForce);
                    dynamic.m_Inert = 1f - spring.MoveSpringApplyRate;

                    dynamic.m_Gravity = Vector3.zero;
                    dynamic.m_Force = Vector3.zero;
                }
                else
                {
                    // Body
                    dynamic.m_Damping = 0.15f;
                    dynamic.m_Elasticity = Mathf.Clamp01(50f / spring._dragForce);
                    dynamic.m_Stiffness = Mathf.Clamp01(100f / spring._stiffnessForce);
                    dynamic.m_Inert = 1f - spring.MoveSpringApplyRate;

                    dynamic.m_Gravity = Vector3.zero;
                    dynamic.m_Force = Vector3.zero;
                }

                dynamic.SetupParticles();
                DynamicBones.Add(dynamic);

                // CySpring describes the segment starting at the named bone. DynamicBone moves
                // its child particle and uses that displacement to rotate the named bone.
                for (int i = 1; i < dynamic.Particles.Count; i++)
                {
                    var tip = dynamic.Particles[i];
                    var segment = dynamic.Particles[tip.m_ParentIndex].m_Transform;
                    var child = spring._childElements?.Find(c => c._boneName == segment.name);
                    // Some hairstyles use an inner guide sphere on a zero-angle root.
                    // Its tip is kinematic; projecting it inside the sphere folds the fringe into the face.
                    tip.m_PinToAnimatedRoot = isHairSpring && tip.m_ParentIndex == 0 && spring._isLimit &&
                        spring._limitAngleMin == Vector3.zero && spring._limitAngleMax == Vector3.zero;
                    tip.m_Radius = Mathf.Max(0f, child != null ? child._collisionRadius : spring._collisionRadius);
                    if (child != null)
                    {
                        // Keep each appendage's tuned root response, then use the asset's
                        // relative drag and restoring force along the strand.
                        if (isHairObject || isTailObject)
                        {
                            if (spring._dragForce > 0)
                                tip.m_Damping = Mathf.Clamp01(tip.m_Damping * child._dragForce / spring._dragForce);
                            if (spring._stiffnessForce > 0)
                                tip.m_Elasticity = Mathf.Clamp01(tip.m_Elasticity * child._stiffnessForce / spring._stiffnessForce);
                        }
                        tip.m_LimitAngel_Min = child._limitAngleMin;
                        tip.m_LimitAngel_Max = child._limitAngleMax;
                    }
                    var names = child != null ? child._collisionNameList : spring._collisionNameList;
                    if (names == null) continue;
                    foreach (var name in names)
                    {
                        if (colliders.TryGetValue(name, out var t) && t.TryGetComponent(out DynamicBoneColliderBase shape))
                            tip.m_Colliders.Add(shape);
                    }
                }
            }
        }


        public void EnablePhysics(bool isOn)
        {
            foreach(DynamicBone dynamic in DynamicBones)
            {
                dynamic.enabled = isOn;
            }
        }

        public void ResetPhysics()
        {
            foreach (DynamicBone dynamic in DynamicBones)
            {
                dynamic.ResetParticlesPosition();
            }
        }
    }
}

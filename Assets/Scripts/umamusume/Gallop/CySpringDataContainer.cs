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
            foreach (CySpringCollisionData collider in collisionParam)
            {
                if (collider._isInner) continue;
                if (bones.TryGetValue(collider._targetObjectName, out Transform bone))
                {

                    var child = new GameObject(collider._collisionName);
                    child.transform.SetParent(bone);
                    child.transform.localPosition = Vector3.zero;
                    child.transform.localRotation = Quaternion.identity;
                    child.transform.localScale = Vector3.one;
                    colliders.Add(child.name, child.transform);


                    //修改(动骨与碰撞相关)
                    //Tweak
                    if (collider._collisionName == "Col_B_Hip_Tail")
                    {
                        collider._radius *= 1.01f;
                    }
                    else if (collider._collisionName == "Col_B_Chest_Tail")
                    {
                        collider._radius *= 1.11f;
                    }
                    else if (collider._collisionName.Contains("Col_B_Hip_Skirt"))
                    {
                        collider._radius *= 0.8f;
                    }
                    else if (collider._collisionName == "Col_Elbow_R_Hair" || collider._collisionName == "Col_Elbow_L_Hair")
                    {
                        collider._radius *= 0.3f;
                    }
                    else
                    {
                        collider._radius *= 0.9f;
                    }


                    switch (collider._type)
                    {
                        case CySpringCollisionData.CollisionType.Capsule:
                            var dynamic = child.AddComponent<DynamicBoneCollider>();
                            dynamic.ColliderName = collider._collisionName;
                            child.transform.localPosition = (collider._offset + collider._offset2) / 2;
                            child.transform.LookAt(child.transform.TransformPoint(collider._offset2));
                            dynamic.m_Direction = DynamicBoneColliderBase.Direction.Z;
                            dynamic.m_Height = (collider._offset - collider._offset2).magnitude + collider._radius;
                            dynamic.m_Radius = collider._radius;
                            dynamic.m_Bound = collider._isInner ? DynamicBoneColliderBase.Bound.Inside : DynamicBoneColliderBase.Bound.Outside;
                            break;
                        case CySpringCollisionData.CollisionType.Sphere:
                            var Spheredynamic = child.AddComponent<DynamicBoneCollider>();
                            Spheredynamic.ColliderName = collider._collisionName;
                            child.transform.localPosition = collider._offset;
                            Spheredynamic.m_Radius = collider._radius;
                            Spheredynamic.m_Height = collider._distance;
                            Spheredynamic.m_Bound = collider._isInner ? DynamicBoneColliderBase.Bound.Inside : DynamicBoneColliderBase.Bound.Outside;
                            break;
                        case CySpringCollisionData.CollisionType.Plane:
                            var planedynamic = child.AddComponent<DynamicBonePlaneCollider>();
                            planedynamic.ColliderName = collider._collisionName;
                            child.transform.localPosition = collider._offset;
                            planedynamic.m_Bound = collider._isInner ? DynamicBoneColliderBase.Bound.Inside : DynamicBoneColliderBase.Bound.Outside;
                            break;
                        case CySpringCollisionData.CollisionType.None:
                            break;
                    }
                }
            }
            return colliders;
        }


        //修改(动骨与碰撞相关)
        //Tweak
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

                dynamic.m_LimitAngel_Min = spring._limitAngleMin;
                dynamic.m_LimitAngel_Max = spring._limitAngleMax;

                if (isTailObject)
                {
                    dynamic.m_Damping = 0.05f;
                    dynamic.m_Elasticity = 0.2f;
                    dynamic.m_Stiffness = 0.1f;
                    dynamic.m_Inert = 0.5f;
                    dynamic.m_Radius = 0.02f;

                    dynamic.m_Gravity = Vector3.zero;
                    dynamic.m_Force = Vector3.zero;

                    dynamic.m_DampingDistrib = null;
                    dynamic.m_ElasticityDistrib = new AnimationCurve(
                        new Keyframe(0f, 1.6f),
                        new Keyframe(0f, 1.5f),
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
                    dynamic.m_Radius = 0.03f;

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
                    dynamic.m_RadiusDistrib = new AnimationCurve(
                        new Keyframe(0f, 0.8f),
                        new Keyframe(1f, 1.2f)
                    );
                }
                else if (isHairSpring)
                {
                    dynamic.m_Damping = 0.3f;
                    dynamic.m_Elasticity = Mathf.Clamp01(50f / spring._dragForce);
                    dynamic.m_Stiffness = Mathf.Clamp01(100f / spring._stiffnessForce);
                    dynamic.m_Inert = 1f - spring.MoveSpringApplyRate;
                    dynamic.m_Radius = spring._collisionRadius * 0.9f;

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
                    dynamic.m_Radius = spring._collisionRadius * 0.8f;

                    dynamic.m_Gravity = Vector3.zero;
                    dynamic.m_Force = Vector3.zero;
                }

                dynamic.SetupParticles();
                DynamicBones.Add(dynamic);

                foreach (string collisionName in spring._collisionNameList)
                {
                    if (colliders.TryGetValue(collisionName, out Transform tmp))
                    {
                        var collider = tmp.GetComponent<DynamicBoneColliderBase>();
                        if (collider != null)
                        {
                            dynamic.Particles[0].m_Colliders.Add(collider);
                        }
                    }
                }

                foreach (var child in spring._childElements)
                {
                    var tempParticle = dynamic.Particles.Find(
                        p => p.m_Transform != null && p.m_Transform.gameObject.name == child._boneName
                    );
                    if (tempParticle == null) continue;

                    if (isTailObject)
                    {
                    }
                    else if (isEarSpring)
                    {
                    }
                    else if (isHairSpring)
                    {
                    }
                    else
                    {
                        // Body
                    }

                    tempParticle.m_LimitAngel_Min = child._limitAngleMin;
                    tempParticle.m_LimitAngel_Max = child._limitAngleMax;

                    foreach (string collisionName in child._collisionNameList)
                    {
                        if (colliders.TryGetValue(collisionName, out Transform tmp))
                        {
                            var collider = tmp.GetComponent<DynamicBoneColliderBase>();
                            if (collider != null)
                            {
                                tempParticle.m_Colliders.Add(collider);
                            }
                        }
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

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop
{
    //Matches the game's "ast_bdyXXXX_XX_skirt00" asset (class Gallop.SkirtParamDataAsset, assembly umamusume).
    //Field names/types must stay identical to the game's serialized layout so the asset deserializes into this class.
    public class SkirtParamDataAsset : ScriptableObject
    {
        public bool _isLongSkirt;
        public string _centerBoneName;
        public string _kneeLBoneName;
        public string _kneeRBoneName;
        public string _ankleLBoneName;
        public string _ankleRBoneName;
        public float _kneeColliderRadius;
        public float _ankleColliderRadius;
        public float _influenceAngle;
        public float _influenceMaxAngle;
        public bool IsOffsetAngleTargetEndNode;
        public bool IsScaleCollision;
        public List<SkirtParamDataOverrideCollision> OverrideCollisionList = new List<SkirtParamDataOverrideCollision>();
        public List<SkirtParamDataElement> _skirtBone = new List<SkirtParamDataElement>();

        private void GetLegRadii(int charaId, out float knee, out float ankle)
        {
            var entry = OverrideCollisionList?.Find(o => o != null && o.CharaId == charaId);
            knee = entry != null && entry.KneeCollisionSize > 0 ? entry.KneeCollisionSize : _kneeColliderRadius;
            ankle = entry != null && entry.AnkleCollisionSize > 0 ? entry.AnkleCollisionSize : _ankleColliderRadius;
        }

        public void BindDynamicSkirt(Dictionary<string, Transform> bones, Dictionary<string, Transform> colliders,
            List<CySpringDataContainer> parts, int charaId, SkinnedMeshRenderer surfaceSkin = null)
        {
            if (_skirtBone == null || _skirtBone.Count == 0) return;
            GetLegRadii(charaId, out var kneeRadius, out var ankleRadius);
            var leftThigh = MakeLegCollider(bones, _kneeLBoneName, kneeRadius);
            var rightThigh = MakeLegCollider(bones, _kneeRBoneName, kneeRadius);
            var leftShin = _isLongSkirt ? MakeLegCollider(bones, _ankleLBoneName, ankleRadius) : null;
            var rightShin = _isLongSkirt ? MakeLegCollider(bones, _ankleRBoneName, ankleRadius) : null;
            bones.TryGetValue(_kneeLBoneName ?? "", out var leftKnee);
            bones.TryGetValue(_kneeRBoneName ?? "", out var rightKnee);
            UmaSkirtPose pose = null;
            if (bones.TryGetValue(_centerBoneName ?? "", out var center) && _influenceMaxAngle > _influenceAngle)
            {
                pose = center.gameObject.AddComponent<UmaSkirtPose>();
                pose.Setup(center, _influenceAngle, _influenceMaxAngle);
            }

            var armContacts = new List<DynamicBoneColliderBase>();
            foreach (var name in new[] { "Col_B_Elbow_L_Multi", "Col_B_Elbow_R_Multi", "Col_B_Wrist_L_Multi", "Col_B_Wrist_R_Multi" })
            {
                if (colliders.TryGetValue(name, out var t) && t.TryGetComponent(out DynamicBoneColliderBase shape))
                    armContacts.Add(shape);
            }
            var surfaceChains = surfaceSkin ? new List<DynamicBone>() : null;
            foreach (var part in parts)
            foreach (var chain in part.DynamicBones)
            {
                var panel = _skirtBone.Find(p => p != null && p._rootBoneName == chain.m_Root.name);
                if (panel == null) continue;
                surfaceChains?.Add(chain);
                if (pose && bones.TryGetValue(panel._childBoneName ?? "", out var child))
                    pose.AddPanel(chain, child, panel._isCheckLeftLeg ? leftKnee : null, panel._isCheckRightLeg ? rightKnee : null, kneeRadius);
                for (int i = 1; i < chain.Particles.Count; i++)
                {
                    // The synthetic tip lies past the hem. Pushing it against the leg
                    // would twist the last skinned joint before the fabric touches.
                    if (!chain.Particles[i].m_Transform) continue;
                    var contacts = chain.Particles[i].m_Colliders;
                    foreach (var arm in armContacts)
                        if (!contacts.Contains(arm)) contacts.Add(arm);
                    if (panel._isCheckLeftLeg && leftThigh) contacts.Add(leftThigh);
                    if (panel._isCheckRightLeg && rightThigh) contacts.Add(rightThigh);
                    if (panel._isCheckLeftLeg && leftShin) contacts.Add(leftShin);
                    if (panel._isCheckRightLeg && rightShin) contacts.Add(rightShin);
                }
            }
            if (surfaceChains != null && surfaceChains.Count > 0)
            {
                var limbs = new List<DynamicBoneColliderBase>(armContacts) { leftThigh, rightThigh, leftShin, rightShin };
                var surface = UmaSkirtSurface.Attach(surfaceSkin, surfaceChains, limbs, center, armContacts.Count);
                if (pose) pose.Surface = surface;
            }
        }

        // Extend the joint sphere along the limb so a fast-moving thigh can lift the
        // panel before the knee reaches it. Offsets stay in the animated parent's space.
        private static DynamicBoneCollider MakeLegCollider(Dictionary<string, Transform> bones, string jointName, float radius)
        {
            if (radius <= 0 || string.IsNullOrEmpty(jointName) || !bones.TryGetValue(jointName, out var joint) || !joint.parent)
                return null;
            var go = new GameObject("Col_Skirt_" + jointName);
            go.transform.SetParent(joint.parent, false);
            var axis = joint.localPosition;
            go.transform.localPosition = axis * 0.5f;
            if (axis.sqrMagnitude > 0)
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
            var capsule = go.AddComponent<DynamicBoneCollider>();
            capsule.ColliderName = go.name;
            // The authored knee sphere includes panel clearance. A full-length capsule
            // with that same clearance would inflate the skirt all the way up to the hip.
            float length = axis.magnitude;
            capsule.m_Radius = length > 0f ? Mathf.Min(radius, length * 0.15f) : radius;
            capsule.m_Height = length + 2f * capsule.m_Radius;
            return capsule;
        }
    }

    [Serializable]
    public class SkirtParamDataOverrideCollision
    {
        public int CharaId;
        public float KneeCollisionSize;
        public float AnkleCollisionSize;
    }

    [Serializable]
    public class SkirtParamDataElement
    {
        public string _rootBoneName;
        public string _childBoneName;
        public bool _isCheckRightLeg;
        public bool _isCheckLeftLeg;
        public bool IsIgnoreRaceRun;
    }
}

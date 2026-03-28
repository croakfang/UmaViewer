using UnityEngine;

namespace Gallop.UmaCySpring
{
    public enum UmaCySpringCollisionType
    {
        None = 0,
        Sphere = 1,
        Capsule = 2,
        Plane = 3,
        Directional = 4,
    }

    public sealed class UmaCySpringCollisionRuntimeData
    {
        public string Name;
        public UmaCySpringCollisionType Type;
        public bool IsInner;

        public float SourceRadius;
        public Vector3 SourceOffsetA;
        public Vector3 SourceOffsetB;
        public Vector3 SourceNormal;

        public float Radius;
        public Vector3 OffsetA;
        public Vector3 OffsetB;
        public Vector3 Normal;

        public Transform AttachBone;
        public Transform RuntimeTransform;
        public string TargetNameOrPath;

        public void ScaleParams(bool useCorrectScaleCalc)
        {
            Radius = SourceRadius;
            OffsetA = SourceOffsetA;
            OffsetB = SourceOffsetB;
            Normal = SourceNormal;

            if (!useCorrectScaleCalc || AttachBone == null)
                return;

            Vector3 s = AttachBone.lossyScale;
            float sx = Mathf.Abs(s.x) > 1e-6f ? s.x : 1f;
            float sy = Mathf.Abs(s.y) > 1e-6f ? s.y : 1f;
            float sz = Mathf.Abs(s.z) > 1e-6f ? s.z : 1f;

            OffsetA = new Vector3(SourceOffsetA.x / sx, SourceOffsetA.y / sy, SourceOffsetA.z / sz);
            OffsetB = new Vector3(SourceOffsetB.x / sx, SourceOffsetB.y / sy, SourceOffsetB.z / sz);
            Radius = SourceRadius / Mathf.Abs(sx);
        }

        public void ApplyTransform()
        {
            if (RuntimeTransform == null)
                return;

            switch (Type)
            {
                case UmaCySpringCollisionType.Sphere:
                    RuntimeTransform.localPosition = OffsetA;
                    RuntimeTransform.localRotation = Quaternion.identity;
                    break;

                case UmaCySpringCollisionType.Capsule:
                    RuntimeTransform.localPosition = (OffsetA + OffsetB) * 0.5f;
                    Vector3 axis = OffsetB - OffsetA;
                    RuntimeTransform.localRotation = axis.sqrMagnitude > 1e-8f
                        ? Quaternion.FromToRotation(Vector3.forward, axis.normalized)
                        : Quaternion.identity;
                    break;

                case UmaCySpringCollisionType.Plane:
                    RuntimeTransform.localPosition = OffsetA;
                    RuntimeTransform.localRotation = Normal.sqrMagnitude > 1e-8f
                        ? Quaternion.FromToRotation(Vector3.up, Normal.normalized)
                        : Quaternion.identity;
                    break;

                case UmaCySpringCollisionType.Directional:
                    Vector3 dir = OffsetB;
                    RuntimeTransform.localPosition = OffsetA;
                    RuntimeTransform.localRotation = dir.sqrMagnitude > 1e-8f
                        ? Quaternion.FromToRotation(Vector3.forward, dir.normalized)
                        : Quaternion.identity;
                    break;
            }
        }
    }
}

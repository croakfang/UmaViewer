using UnityEngine;

namespace Gallop
{
    public class CySpringPostSpringLimiter : MonoBehaviour
    {
        public DynamicBone RootDynamicBone;

        [Header("Debug")]
        public bool EnableLimit = true;

        private void LateUpdate()
        {
            if (!EnableLimit || RootDynamicBone == null || RootDynamicBone.Particles == null)
                return;

            // 这里只做第一版：
            // 直接按粒子自带 limit 做后置 clamp
            // 下一步再补 BoneAxis / InitBoneDistance / reference rotation
            foreach (var p in RootDynamicBone.Particles)
            {
                if (p == null || p.m_Transform == null)
                    continue;

                Vector3 minAbs = p.m_LimitAngel_Min;
                Vector3 max = p.m_LimitAngel_Max;

                bool hasLimit =
                    (minAbs != Vector3.zero) ||
                    (max != Vector3.zero);

                if (!hasLimit)
                    continue;

                Vector3 euler = p.m_Transform.localEulerAngles;
                euler = MakePositive(euler);
                euler = RoundAngle(euler);

                euler.x = Mathf.Clamp(euler.x, -minAbs.x, max.x);
                euler.y = Mathf.Clamp(euler.y, -minAbs.y, max.y);
                euler.z = Mathf.Clamp(euler.z, -minAbs.z, max.z);

                p.m_Transform.localRotation = Quaternion.Euler(euler);
            }
        }

        private static Vector3 MakePositive(Vector3 euler)
        {
            if (euler.x < 0f) euler.x += 360f;
            if (euler.y < 0f) euler.y += 360f;
            if (euler.z < 0f) euler.z += 360f;
            return euler;
        }

        private static Vector3 RoundAngle(Vector3 euler)
        {
            euler.x = Normalize180(euler.x);
            euler.y = Normalize180(euler.y);
            euler.z = Normalize180(euler.z);
            return euler;
        }

        private static float Normalize180(float angle)
        {
            while (angle > 180f) angle -= 360f;
            while (angle < -180f) angle += 360f;
            return angle;
        }
    }
}
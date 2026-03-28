using UnityEngine;

namespace Gallop
{
    [DefaultExecutionOrder(10000)]
    public class CySpringDeltaAngleLimiter : MonoBehaviour
    {
        public DynamicBone RootDynamicBone;
        public bool EnableLimit = true;

        private void LateUpdate()
        {
            if (!EnableLimit || RootDynamicBone == null || RootDynamicBone.Particles == null)
                return;

            foreach (var particle in RootDynamicBone.Particles)
            {
                if (particle == null || particle.m_Transform == null)
                    continue;

                Vector3 minAbs = particle.m_LimitAngel_Min;
                Vector3 maxAbs = particle.m_LimitAngel_Max;
                bool hasLimit = minAbs != Vector3.zero || maxAbs != Vector3.zero;
                if (!hasLimit)
                    continue;

                Quaternion initLocalRotation = particle.m_InitLocalRotation;
                Quaternion deltaRotation = Quaternion.Inverse(initLocalRotation) * particle.m_Transform.localRotation;
                Vector3 deltaEuler = NormalizeEuler(deltaRotation.eulerAngles);

                deltaEuler.x = Mathf.Clamp(deltaEuler.x, -minAbs.x, maxAbs.x);
                deltaEuler.y = Mathf.Clamp(deltaEuler.y, -minAbs.y, maxAbs.y);
                deltaEuler.z = Mathf.Clamp(deltaEuler.z, -minAbs.z, maxAbs.z);

                particle.m_Transform.localRotation = initLocalRotation * Quaternion.Euler(deltaEuler);
            }
        }

        private static Vector3 NormalizeEuler(Vector3 euler)
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

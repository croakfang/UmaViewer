using UnityEngine;

[AddComponentMenu("Dynamic Bone/Dynamic Bone Collider")]
public class DynamicBoneCollider : DynamicBoneColliderBase
{
#if UNITY_5_3_OR_NEWER
	[Tooltip("The radius of the sphere or capsule.")]
#endif	
    public float m_Radius = 0.5f;
	
#if UNITY_5_3_OR_NEWER
	[Tooltip("The height of the capsule.")]
#endif		
    public float m_Height = 0;

    void OnValidate()
    {
        m_Radius = Mathf.Max(m_Radius, 0);
        m_Height = Mathf.Max(m_Height, 0);
    }

    public void GetWorldCapsule(out Vector3 center, out Vector3 halfAxis, out float radius)
    {
        radius = m_Radius * Mathf.Abs(transform.lossyScale.x);
        float h = m_Height * 0.5f - m_Radius;
        center = transform.TransformPoint(m_Center);
        halfAxis = Vector3.zero;
        if (h > 0)
        {
            var localAxis = m_Direction == Direction.X ? Vector3.right : m_Direction == Direction.Y ? Vector3.up : Vector3.forward;
            halfAxis = transform.TransformVector(localAxis * h);
        }
    }

    public override bool Collide(ref Vector3 particlePosition, float particleRadius)
    {
        GetWorldCapsule(out var center, out var axis, out var radius);
        if (axis.sqrMagnitude > 0)
            center += axis * Mathf.Clamp(Vector3.Dot(particlePosition - center, axis) / axis.sqrMagnitude, -1f, 1f);
        var delta = particlePosition - center;
        bool inside = m_Bound == Bound.Inside;
        float limit = inside ? Mathf.Max(0f, radius - particleRadius) : radius + particleRadius;
        float sqrDistance = delta.sqrMagnitude;
        if (inside ? sqrDistance <= limit * limit : sqrDistance >= limit * limit) return false;

        // On the capsule axis choose a perpendicular in collider space.
        var direction = sqrDistance > 0 ? delta / Mathf.Sqrt(sqrDistance)
            : (m_Direction == Direction.X ? transform.up : transform.right);
        particlePosition = center + direction * limit;
        return true;
    }

    void OnDrawGizmosSelected()
    {
        if (!enabled)
            return;

        if (m_Bound == Bound.Outside)
            Gizmos.color = Color.yellow;
        else
            Gizmos.color = Color.magenta;
        float radius = m_Radius * Mathf.Abs(transform.lossyScale.x);
        float h = m_Height * 0.5f - m_Radius;
        if (h <= 0)
        {
            Gizmos.DrawWireSphere(transform.TransformPoint(m_Center), radius);
        }
        else
        {
            Vector3 c0 = m_Center;
            Vector3 c1 = m_Center;

            switch (m_Direction)
            {
                case Direction.X:
                    c0.x -= h;
                    c1.x += h;
                    break;
                case Direction.Y:
                    c0.y -= h;
                    c1.y += h;
                    break;
                case Direction.Z:
                    c0.z -= h;
                    c1.z += h;
                    break;
            }
            Gizmos.DrawWireSphere(transform.TransformPoint(c0), radius);
            Gizmos.DrawWireSphere(transform.TransformPoint(c1), radius);
        }
    }
}

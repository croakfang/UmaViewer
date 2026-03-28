using UnityEngine;

[AddComponentMenu("Dynamic Bone/Dynamic Bone Collider")]
public class DynamicBoneCollider : DynamicBoneColliderBase
{
    private const float SweepMinSpacing = 0.005f;

    private const int SweepMaxSamples = 8;

    private const int SweepBinaryIterations = 6;

    private const float NormalEpsilon = 1e-8f;

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

    public override bool Collide(ref Vector3 particlePosition, float particleRadius)
    {
        return Collide(ref particlePosition, particleRadius, particlePosition);
    }

    public override bool Collide(ref Vector3 particlePosition, float particleRadius, Vector3 particlePrevPosition)
    {
        float radius = m_Radius * Mathf.Abs(transform.lossyScale.x);
        float h = m_Height * 0.5f - m_Radius;
        if (h <= 0)
        {
            if (m_Bound == Bound.Outside)
                return OutsideSphere(ref particlePosition, particleRadius, transform.TransformPoint(m_Center), radius, particlePrevPosition);
            return InsideSphere(ref particlePosition, particleRadius, transform.TransformPoint(m_Center), radius);
        }

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

        if (m_Bound == Bound.Outside)
            return OutsideCapsule(ref particlePosition, particleRadius, transform.TransformPoint(c0), transform.TransformPoint(c1), radius, particlePrevPosition);
        return InsideCapsule(ref particlePosition, particleRadius, transform.TransformPoint(c0), transform.TransformPoint(c1), radius);
    }

    static bool OutsideSphere(ref Vector3 particlePosition, float particleRadius, Vector3 sphereCenter, float sphereRadius, Vector3 particlePrevPosition)
    {
        float r = sphereRadius + particleRadius;
        if (ProjectOutsideSphere(ref particlePosition, sphereCenter, r, particlePrevPosition))
        {
            return true;
        }

        if (TryFindSphereSweepContact(particlePrevPosition, particlePosition, sphereCenter, r, out Vector3 hitPoint))
        {
            Vector3 normal = GetSafeNormal(hitPoint - sphereCenter, particlePrevPosition - sphereCenter, Vector3.up);
            particlePosition = sphereCenter + normal * r;
            return true;
        }

        return false;
    }

    static bool InsideSphere(ref Vector3 particlePosition, float particleRadius, Vector3 sphereCenter, float sphereRadius)
    {
        float r = sphereRadius - particleRadius;
        float r2 = r * r;
        Vector3 d = particlePosition - sphereCenter;
        float len2 = d.sqrMagnitude;

        if (len2 > r2)
        {
            float len = Mathf.Sqrt(len2);
            particlePosition = sphereCenter + d * (r / len);
            return true;
        }
        return false;
    }

    static bool OutsideCapsule(ref Vector3 particlePosition, float particleRadius, Vector3 capsuleP0, Vector3 capsuleP1, float capsuleRadius, Vector3 particlePrevPosition)
    {
        float r = capsuleRadius + particleRadius;
        if (ProjectOutsideCapsule(ref particlePosition, capsuleP0, capsuleP1, r, particlePrevPosition))
        {
            return true;
        }

        if (TryFindCapsuleSweepContact(particlePrevPosition, particlePosition, capsuleP0, capsuleP1, r, out Vector3 hitPoint))
        {
            Vector3 closestPoint = ClosestPointOnSegment(capsuleP0, capsuleP1, hitPoint);
            Vector3 normal = GetSafeNormal(hitPoint - closestPoint, particlePrevPosition - closestPoint, capsuleP1 - capsuleP0);
            particlePosition = closestPoint + normal * r;
            return true;
        }

        return false;
    }

    static bool InsideCapsule(ref Vector3 particlePosition, float particleRadius, Vector3 capsuleP0, Vector3 capsuleP1, float capsuleRadius)
    {
        float r = capsuleRadius - particleRadius;
        float r2 = r * r;
        Vector3 dir = capsuleP1 - capsuleP0;
        Vector3 d = particlePosition - capsuleP0;
        float t = Vector3.Dot(d, dir);

        if (t <= 0)
        {
            float len2 = d.sqrMagnitude;
            if (len2 > r2)
            {
                float len = Mathf.Sqrt(len2);
                particlePosition = capsuleP0 + d * (r / len);
                return true;
            }
        }
        else
        {
            float dl = dir.sqrMagnitude;
            if (t >= dl)
            {
                d = particlePosition - capsuleP1;
                float len2 = d.sqrMagnitude;
                if (len2 > r2)
                {
                    float len = Mathf.Sqrt(len2);
                    particlePosition = capsuleP1 + d * (r / len);
                    return true;
                }
            }
            else if (dl > 0)
            {
                t /= dl;
                d -= dir * t;
                float len2 = d.sqrMagnitude;
                if (len2 > r2)
                {
                    float len = Mathf.Sqrt(len2);
                    particlePosition += d * ((r - len) / len);
                    return true;
                }
            }
        }
        return false;
    }

    static bool ProjectOutsideSphere(ref Vector3 particlePosition, Vector3 sphereCenter, float radius, Vector3 particlePrevPosition)
    {
        Vector3 delta = particlePosition - sphereCenter;
        float len2 = delta.sqrMagnitude;
        float radius2 = radius * radius;

        if (len2 >= radius2)
        {
            return false;
        }

        Vector3 normal = GetSafeNormal(delta, particlePrevPosition - sphereCenter, Vector3.up);
        particlePosition = sphereCenter + normal * radius;
        return true;
    }

    static bool ProjectOutsideCapsule(ref Vector3 particlePosition, Vector3 capsuleP0, Vector3 capsuleP1, float radius, Vector3 particlePrevPosition)
    {
        Vector3 closestPoint = ClosestPointOnSegment(capsuleP0, capsuleP1, particlePosition);
        Vector3 delta = particlePosition - closestPoint;
        float len2 = delta.sqrMagnitude;
        float radius2 = radius * radius;

        if (len2 >= radius2)
        {
            return false;
        }

        Vector3 normal = GetSafeNormal(delta, particlePrevPosition - closestPoint, capsuleP1 - capsuleP0);
        particlePosition = closestPoint + normal * radius;
        return true;
    }

    static bool TryFindSphereSweepContact(Vector3 start, Vector3 end, Vector3 center, float radius, out Vector3 hitPoint)
    {
        hitPoint = end;

        Vector3 move = end - start;
        float moveLen = move.magnitude;
        if (moveLen <= NormalEpsilon)
        {
            return false;
        }

        int samples = Mathf.Clamp(Mathf.CeilToInt(moveLen / Mathf.Max(radius * 0.75f, SweepMinSpacing)), 1, SweepMaxSamples);
        float lowT = 0f;

        for (int i = 1; i <= samples; i++)
        {
            float highT = (float)i / samples;
            Vector3 sample = Vector3.Lerp(start, end, highT);
            if (!IsInsideSphere(sample, center, radius))
            {
                lowT = highT;
                continue;
            }

            for (int j = 0; j < SweepBinaryIterations; j++)
            {
                float midT = (lowT + highT) * 0.5f;
                Vector3 mid = Vector3.Lerp(start, end, midT);
                if (IsInsideSphere(mid, center, radius))
                {
                    highT = midT;
                }
                else
                {
                    lowT = midT;
                }
            }

            hitPoint = Vector3.Lerp(start, end, highT);
            return true;
        }

        return false;
    }

    static bool TryFindCapsuleSweepContact(Vector3 start, Vector3 end, Vector3 capsuleP0, Vector3 capsuleP1, float radius, out Vector3 hitPoint)
    {
        hitPoint = end;

        Vector3 move = end - start;
        float moveLen = move.magnitude;
        if (moveLen <= NormalEpsilon)
        {
            return false;
        }

        int samples = Mathf.Clamp(Mathf.CeilToInt(moveLen / Mathf.Max(radius * 0.75f, SweepMinSpacing)), 1, SweepMaxSamples);
        float lowT = 0f;

        for (int i = 1; i <= samples; i++)
        {
            float highT = (float)i / samples;
            Vector3 sample = Vector3.Lerp(start, end, highT);
            if (!IsInsideCapsule(sample, capsuleP0, capsuleP1, radius))
            {
                lowT = highT;
                continue;
            }

            for (int j = 0; j < SweepBinaryIterations; j++)
            {
                float midT = (lowT + highT) * 0.5f;
                Vector3 mid = Vector3.Lerp(start, end, midT);
                if (IsInsideCapsule(mid, capsuleP0, capsuleP1, radius))
                {
                    highT = midT;
                }
                else
                {
                    lowT = midT;
                }
            }

            hitPoint = Vector3.Lerp(start, end, highT);
            return true;
        }

        return false;
    }

    static bool IsInsideSphere(Vector3 point, Vector3 center, float radius)
    {
        return (point - center).sqrMagnitude < radius * radius;
    }

    static bool IsInsideCapsule(Vector3 point, Vector3 capsuleP0, Vector3 capsuleP1, float radius)
    {
        Vector3 closestPoint = ClosestPointOnSegment(capsuleP0, capsuleP1, point);
        return (point - closestPoint).sqrMagnitude < radius * radius;
    }

    static Vector3 ClosestPointOnSegment(Vector3 segmentStart, Vector3 segmentEnd, Vector3 point)
    {
        Vector3 segment = segmentEnd - segmentStart;
        float segmentLen2 = segment.sqrMagnitude;
        if (segmentLen2 <= NormalEpsilon)
        {
            return segmentStart;
        }

        float t = Mathf.Clamp01(Vector3.Dot(point - segmentStart, segment) / segmentLen2);
        return segmentStart + segment * t;
    }

    static Vector3 GetSafeNormal(Vector3 primary, Vector3 secondary, Vector3 axisHint)
    {
        if (primary.sqrMagnitude > NormalEpsilon)
        {
            return primary.normalized;
        }

        if (secondary.sqrMagnitude > NormalEpsilon)
        {
            return secondary.normalized;
        }

        Vector3 fallback = Vector3.Cross(axisHint, Vector3.up);
        if (fallback.sqrMagnitude <= NormalEpsilon)
        {
            fallback = Vector3.Cross(axisHint, Vector3.right);
        }
        if (fallback.sqrMagnitude <= NormalEpsilon)
        {
            fallback = Vector3.forward;
        }
        return fallback.normalized;
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

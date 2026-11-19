using System.Collections.Generic;
using UnityEngine;

namespace Gallop
{
    // Bias the animated rest pose before DynamicBone solves contacts. Panels facing
    // the same rising thigh lift together, including the ones just outside its capsule.
    [DefaultExecutionOrder(-50)]
    public class UmaSkirtPose : MonoBehaviour
    {
        private struct Leg
        {
            public Transform Knee;
            public Vector3 Down, Outward;
            private float _restAngle, _radius;

            public Leg(Transform center, Transform knee, Transform panel, Transform root, float radius)
            {
                Knee = knee;
                Down = knee ? center.InverseTransformDirection(knee.position - knee.parent.position).normalized : Vector3.zero;
                Outward = Vector3.ProjectOnPlane(center.InverseTransformDirection(panel.position - center.position), Down).normalized;
                var segment = center.InverseTransformVector(panel.position - root.position);
                _restAngle = Mathf.Atan2(Vector3.Dot(segment, Outward), Vector3.Dot(segment, Down)) * Mathf.Rad2Deg;
                _radius = radius;
            }

            // Angle from the panel hinge to the outer tangent of the held knee.
            // Adjacent panels share the lift over a width proportional to the knee.
            public float SupportAngle(Transform center, Vector3 hinge, Vector3 axis)
            {
                if (!Knee || _radius <= 0f || axis.sqrMagnitude < 0.5f) return 0f;
                var delta = center.InverseTransformVector(Knee.position - hinge);
                float side = Mathf.Abs(Vector3.Dot(delta, axis.normalized));
                float coverage = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_radius, 3f * _radius, side));
                float reach = Vector3.Dot(delta, Outward);
                float down = Vector3.Dot(delta, Down);
                float distance = Mathf.Sqrt(reach * reach + down * down);
                float tangent = Mathf.Asin(Mathf.Clamp01(_radius / Mathf.Max(_radius, distance)));
                return coverage * Mathf.Max(0f, (Mathf.Atan2(reach, down) + tangent) * Mathf.Rad2Deg - _restAngle);
            }

            public float Lift(Transform center, out Vector3 axis)
            {
                axis = Vector3.Cross(Down, Outward);
                if (!Knee || axis.sqrMagnitude < 0.5f) return 0;
                var leg = center.InverseTransformDirection(Knee.position - Knee.parent.position);
                return Mathf.Atan2(Vector3.Dot(leg, Outward), Vector3.Dot(leg, Down)) * Mathf.Rad2Deg;
            }
        }

        private struct Panel
        {
            public DynamicBone Chain;
            public Leg Left, Right;
        }

        private readonly List<Panel> _panels = new List<Panel>();
        private Transform _center;
        private float _clearance, _maxLift;
        public UmaSkirtSurface Surface { private get; set; }

        public void Setup(Transform center, float clearance, float maxAngle)
        {
            _center = center;
            _clearance = clearance;
            _maxLift = Mathf.Max(0f, maxAngle - clearance);
        }

        public void AddPanel(DynamicBone chain, Transform child, Transform leftKnee, Transform rightKnee, float radius)
        {
            _panels.Add(new Panel
            {
                Chain = chain,
                Left = new Leg(_center, leftKnee, child, chain.m_Root, radius),
                Right = new Leg(_center, rightKnee, child, chain.m_Root, radius)
            });
        }

        private void LateUpdate()
        {
            foreach (var panel in _panels)
            {
                if (!panel.Chain || !panel.Chain.isActiveAndEnabled || panel.Chain.GetWeight() <= 0f) continue;
                float left = panel.Left.Lift(_center, out var leftAxis);
                float right = panel.Right.Lift(_center, out var rightAxis);
                var axis = left > right ? leftAxis : rightAxis;
                float lift = 0.4f * Mathf.Clamp(Mathf.Max(left, right) - _clearance, 0, _maxLift);
                var root = panel.Chain.m_Root;
                if (Surface && Surface.LegStillness > 0f)
                {
                    left = panel.Left.SupportAngle(_center, root.position, leftAxis);
                    right = panel.Right.SupportAngle(_center, root.position, rightAxis);
                    // SupportAngle measures the required panel rotation directly. The
                    // free-travel threshold only applies to the thigh-driven lift above.
                    float support = Mathf.Min(_clearance + _maxLift, Mathf.Max(left, right));
                    if (support > lift)
                    {
                        var supportAxis = left > right ? leftAxis : rightAxis;
                        axis = Vector3.Lerp(axis, supportAxis, Surface.LegStillness).normalized;
                        lift = Mathf.Lerp(lift, support, Surface.LegStillness);
                    }
                }
                if (lift <= 0f) continue;
                axis = _center.TransformDirection(axis);
                // The capsule supplies the remaining local displacement at contact.
                root.rotation = Quaternion.AngleAxis(lift, axis) * root.rotation;
            }
        }
    }
}

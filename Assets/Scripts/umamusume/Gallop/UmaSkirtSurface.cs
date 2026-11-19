using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop
{
    // Update after all DynamicBone chains. Only triangles present in the costume mesh
    // can become contacts, so the proxy does not cap the hem or span an open slit.
    [DefaultExecutionOrder(20000)]
    public class UmaSkirtSurface : MonoBehaviour
    {
        private sealed class Chain
        {
            public DynamicBone Spring;
            public Vector3[] Offsets;
            public bool Moved;
        }

        private sealed class Joint
        {
            public Chain Chain;
            public int Index, LimbMask;
            public Vector3 Sum;
            public float Weight;
        }

        private struct Vertex
        {
            public Vector3 BindPosition, World;
            public BoneWeight Weights;
        }

        private struct Face
        {
            public int A, B, C, LimbMask;
            public Vector3 Outward;
        }

        private struct Limb
        {
            public DynamicBoneCollider Shape;
            public Vector3 A, B;
            public Bounds Bounds;
            public float Radius;
            public int Steps;
            public Vector3 PreviousA, PreviousB;
            public float Stillness;
            public bool HasPrevious, IsLeg;
        }

        private struct Seam
        {
            public int A, B;
            public float RestLength;
        }

        private readonly List<Chain> _chains = new List<Chain>();
        private readonly List<Face> _faces = new List<Face>();
        private readonly List<Seam> _seams = new List<Seam>();
        private Vertex[] _vertices;
        private Transform[] _bones;
        private Matrix4x4[] _bind, _skin;
        private Joint[] _joints;
        private Limb[] _limbs;
        private Transform _waist;
        private float _stillness;
        public float LegStillness => isActiveAndEnabled ? _stillness : 0f;
        public int FaceCount => _faces.Count;
        public int LastContactCount { get; private set; }

        public static UmaSkirtSurface Attach(SkinnedMeshRenderer renderer, List<DynamicBone> springs, List<DynamicBoneColliderBase> shapes,
            Transform waist, int armCount)
        {
            var mesh = renderer.sharedMesh;
            if (!mesh || !mesh.isReadable) return null;
            var surface = renderer.gameObject.AddComponent<UmaSkirtSurface>();
            surface._waist = waist ? waist : renderer.rootBone;
            surface.Build(renderer, mesh, springs, shapes, armCount);
            if (surface.FaceCount > 0) return surface;
            Destroy(surface);
            return null;
        }

        private void Build(SkinnedMeshRenderer renderer, Mesh mesh, List<DynamicBone> springs, List<DynamicBoneColliderBase> shapes, int armCount)
        {
            var limbs = new List<Limb>();
            for (int i = 0; i < shapes.Count; i++)
                if (shapes[i] is DynamicBoneCollider round && round.m_Bound == DynamicBoneColliderBase.Bound.Outside)
                    limbs.Add(new Limb { Shape = round, IsLeg = i >= armCount });
            _limbs = limbs.ToArray();
            if (_limbs.Length == 0) return;

            var joints = new Dictionary<Transform, Joint>();
            foreach (var spring in springs)
            {
                var chain = new Chain { Spring = spring, Offsets = new Vector3[spring.Particles.Count] };
                _chains.Add(chain);
                for (int i = 0; i < spring.Particles.Count; i++)
                {
                    var particle = spring.Particles[i];
                    if (!particle.m_Transform) continue;
                    var joint = new Joint { Chain = chain, Index = i };
                    for (int k = 0; k < _limbs.Length; k++)
                        if (particle.m_Colliders.Contains(_limbs[k].Shape)) joint.LimbMask |= 1 << k;
                    joints[particle.m_Transform] = joint;
                }
            }
            _bones = renderer.bones;
            _bind = mesh.bindposes;
            _skin = new Matrix4x4[_bones.Length];
            _joints = new Joint[_bones.Length];
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i]) joints.TryGetValue(_bones[i], out _joints[i]);

            // Cluster vertices per dominant bone; keep an actual skinned vertex as each
            // representative. Original mesh triangles determine connectivity across panels.
            var vertices = new List<Vertex>();
            var cells = new Dictionary<(int, Vector3Int), int>();
            var positions = mesh.vertices;
            var weights = mesh.boneWeights;
            if (weights.Length != positions.Length || _bind.Length != _bones.Length) return;
            var remap = new int[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                int bone = weights[i].boneIndex0;
                remap[i] = -1;
                if (_joints[bone] == null) continue;
                var cell = (bone, Vector3Int.FloorToInt(positions[i] / 0.06f));
                if (!cells.TryGetValue(cell, out int index))
                {
                    index = vertices.Count;
                    cells.Add(cell, index);
                    vertices.Add(new Vertex { BindPosition = positions[i], Weights = weights[i] });
                }
                remap[i] = index;
            }
            _vertices = vertices.ToArray();
            var seen = new HashSet<(int, int, int)>();
            var seamPairs = new HashSet<(int, int)>();
            var tris = mesh.triangles;
            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = remap[tris[i]], b = remap[tris[i+1]], c = remap[tris[i+2]];
                if (a < 0 || b < 0 || c < 0 || a == b || b == c || c == a) continue;
                int lo = Mathf.Min(a, Mathf.Min(b,c)), hi = Mathf.Max(a, Mathf.Max(b,c));
                if (!seen.Add((lo, a + b + c - lo - hi, hi))) continue;
                int mask = _joints[_vertices[a].Weights.boneIndex0].LimbMask |
                           _joints[_vertices[b].Weights.boneIndex0].LimbMask |
                           _joints[_vertices[c].Weights.boneIndex0].LimbMask;
                if (mask == 0) continue;
                var outward = Vector3.zero;
                if (_waist)
                {
                    var centroid = (_vertices[a].BindPosition + _vertices[b].BindPosition + _vertices[c].BindPosition) / 3f;
                    var radial = Vector3.ProjectOnPlane(renderer.transform.TransformPoint(centroid) - _waist.position, Vector3.up);
                    outward = _waist.InverseTransformDirection(radial.normalized);
                }
                _faces.Add(new Face { A = a, B = b, C = c, LimbMask = mask, Outward = outward });
                AddSeam(a, b, seamPairs);
                AddSeam(b, c, seamPairs);
                AddSeam(c, a, seamPairs);
            }
        }

        private void AddSeam(int vertexA, int vertexB, HashSet<(int, int)> pairs)
        {
            int a = _vertices[vertexA].Weights.boneIndex0;
            int b = _vertices[vertexB].Weights.boneIndex0;
            if (_joints[a].Chain == _joints[b].Chain || !pairs.Add((Mathf.Min(a,b), Mathf.Max(a,b)))) return;
            float length = Vector3.Distance(_bones[a].position, _bones[b].position);
            if (length > 0f)
                _seams.Add(new Seam { A = a, B = b, RestLength = length / Mathf.Max(1e-6f, Mathf.Abs(_bones[a].lossyScale.x)) });
        }

        private void LateUpdate()
        {
            LastContactCount = 0;
            if (_faces.Count == 0 || Time.deltaTime <= 0f) return;
            bool active = false;
            foreach (var chain in _chains)
                if (chain.Spring && chain.Spring.IsSimulating) { active = true; break; }
            if (!active) { _stillness = 0f; return; }
            bool stationary = UpdateGeometry();
            foreach (var chain in _chains)
                chain.Spring.HoldBetweenSurfaceSteps = stationary && _stillness >= 0.999f;
            foreach (var face in _faces) CollectContacts(face);
            if (LastContactCount > 0) ApplyContacts();
        }

        private void OnDisable()
        {
            foreach (var chain in _chains)
                if (chain.Spring) chain.Spring.HoldBetweenSurfaceSteps = false;
        }

        private bool UpdateGeometry()
        {
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i]) _skin[i] = _bones[i].localToWorldMatrix * _bind[i];
            for (int i = 0; i < _vertices.Length; i++)
            {
                ref var v = ref _vertices[i];
                var w = v.Weights;
                var p = _skin[w.boneIndex0].MultiplyPoint3x4(v.BindPosition) * w.weight0;
                if (w.weight1 > 0) p += _skin[w.boneIndex1].MultiplyPoint3x4(v.BindPosition) * w.weight1;
                if (w.weight2 > 0) p += _skin[w.boneIndex2].MultiplyPoint3x4(v.BindPosition) * w.weight2;
                if (w.weight3 > 0) p += _skin[w.boneIndex3].MultiplyPoint3x4(v.BindPosition) * w.weight3;
                v.World = p;
            }
            _stillness = 1f;
            bool hasLeg = false;
            bool stationary = true;
            for (int i = 0; i < _limbs.Length; i++)
            {
                ref var limb = ref _limbs[i];
                limb.Radius = 0;
                if (!limb.Shape || !limb.Shape.isActiveAndEnabled)
                {
                    limb.HasPrevious = false;
                    limb.Stillness = 0f;
                    continue;
                }
                limb.Shape.GetWorldCapsule(out var center, out var axis, out limb.Radius);
                if (limb.Radius <= 0) continue;
                limb.A = center - axis;
                limb.B = center + axis;
                if (_waist)
                {
                    var a = _waist.InverseTransformPoint(limb.A);
                    var b = _waist.InverseTransformPoint(limb.B);
                    float movementSq = Mathf.Max((a - limb.PreviousA).sqrMagnitude, (b - limb.PreviousB).sqrMagnitude);
                    stationary &= limb.HasPrevious && movementSq < 1e-10f;
                    if (limb.IsLeg)
                    {
                        hasLeg = true;
                        float speed = Mathf.Sqrt(movementSq) / Time.deltaTime;
                        float target = limb.HasPrevious ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.03f, 0.20f, speed)) : 0f;
                        limb.Stillness = Mathf.MoveTowards(limb.Stillness, target, Time.deltaTime * (target < limb.Stillness ? 20f : 5f));
                        _stillness = Mathf.Min(_stillness, limb.Stillness);
                    }
                    limb.PreviousA = a;
                    limb.PreviousB = b;
                    limb.HasPrevious = true;
                }
                limb.Bounds = new Bounds(limb.A, Vector3.zero);
                limb.Bounds.Encapsulate(limb.B);
                limb.Bounds.Expand(2f * limb.Radius * (1f + limb.Stillness));
                limb.Steps = Mathf.Clamp(Mathf.CeilToInt(2f * axis.magnitude / limb.Radius), 1, 24);
            }
            if (!hasLeg) _stillness = 0f;
            return hasLeg && stationary;
        }

        private void CollectContacts(Face f)
        {
            var a = _vertices[f.A].World;
            var b = _vertices[f.B].World;
            var c = _vertices[f.C].World;
            var normal = Vector3.Cross(b - a, c - a);
            var outward = _waist ? _waist.TransformDirection(f.Outward) : Vector3.zero;
            if (normal.sqrMagnitude < 1e-12f) return;
            var bounds = new Bounds(a, Vector3.zero);
            bounds.Encapsulate(b);
            bounds.Encapsulate(c);
            for (int k = 0; k < _limbs.Length; k++)
            {
                var limb = _limbs[k];
                if ((f.LimbMask & (1 << k)) == 0 || limb.Radius <= 0 || !bounds.Intersects(limb.Bounds)) continue;
                float bestDepth = 0f;
                Vector3 bary = default, direction = default;
                for (int s = 0; s <= limb.Steps; s++)
                {
                    var point = Vector3.Lerp(limb.A, limb.B, (float)s / limb.Steps);
                    var w = ClosestWeights(point, a, b, c);
                    var delta = a * w.x + b * w.y + c * w.z - point;
                    float distance = delta.magnitude;
                    float depth = limb.Radius - distance;
                    var dir = distance > 1e-6f ? delta / distance : normal.normalized;
                    // If a held knee has crossed a panel, the nearest radial direction
                    // points into the dress. Recover the exterior side from the bind pose.
                    float side = Vector3.Dot(delta, outward);
                    if (limb.Stillness > 0f && side < 0f && distance < limb.Radius * (1f + limb.Stillness))
                    {
                        float lateralSq = (delta - outward * side).sqrMagnitude;
                        float radiusSq = limb.Radius * limb.Radius;
                        if (lateralSq < radiusSq)
                        {
                            depth = Mathf.Lerp(depth, Mathf.Sqrt(radiusSq - lateralSq) - side, limb.Stillness);
                            dir = Vector3.Lerp(dir, outward, limb.Stillness).normalized;
                        }
                    }
                    if (depth <= bestDepth) continue;
                    bestDepth = depth;
                    direction = dir;
                    bary = w;
                }
                if (bary == Vector3.zero) continue;
                var push = direction * Mathf.Min(bestDepth, limb.Radius * 0.35f);
                AddVertex(f.A, bary.x, k, push);
                AddVertex(f.B, bary.y, k, push);
                AddVertex(f.C, bary.z, k, push);
                LastContactCount++;
            }
        }

        private void ApplyContacts()
        {
            float response = 1f - Mathf.Exp(-90f * Time.deltaTime);
            foreach (var j in _joints)
            {
                if (j == null || j.Weight <= 0f) continue;
                j.Chain.Offsets[j.Index] = j.Sum * (response / Mathf.Max(1f, j.Weight));
                j.Chain.Moved = true;
                j.Weight = 0;
                j.Sum = Vector3.zero;
            }
            // Share load only across adjacent panels in the source mesh. Do not sew
            // unrelated panels together or try to undo the whole animated pose.
            foreach (var seam in _seams)
            {
                var a = _joints[seam.A];
                var b = _joints[seam.B];
                if (!a.Chain.Moved && !b.Chain.Moved) continue;
                var edge = _bones[seam.B].position - _bones[seam.A].position;
                var target = edge + b.Chain.Offsets[b.Index] - a.Chain.Offsets[a.Index];
                float limit = Mathf.Max(edge.magnitude, 1.08f * seam.RestLength * Mathf.Abs(_bones[seam.A].lossyScale.x));
                float length = target.magnitude;
                if (length <= limit) continue;
                var correction = target * (0.5f * (length - limit) / length);
                if (a.Index > 0 && a.Chain.Spring.IsSimulating && !a.Chain.Spring.HoldingSurfacePose)
                {
                    a.Chain.Offsets[a.Index] += correction;
                    a.Chain.Moved = true;
                }
                if (b.Index > 0 && b.Chain.Spring.IsSimulating && !b.Chain.Spring.HoldingSurfacePose)
                {
                    b.Chain.Offsets[b.Index] -= correction;
                    b.Chain.Moved = true;
                }
            }
            foreach (var chain in _chains)
            {
                if (!chain.Moved) continue;
                chain.Spring.ApplySurfaceOffsets(chain.Offsets, _stillness);
                Array.Clear(chain.Offsets, 0, chain.Offsets.Length);
                chain.Moved = false;
            }
        }

        private void AddVertex(int index, float fraction, int limb, Vector3 push)
        {
            var w = _vertices[index].Weights;
            AddJoint(w.boneIndex0, fraction * w.weight0, limb, push);
            AddJoint(w.boneIndex1, fraction * w.weight1, limb, push);
            AddJoint(w.boneIndex2, fraction * w.weight2, limb, push);
            AddJoint(w.boneIndex3, fraction * w.weight3, limb, push);
        }

        private void AddJoint(int index, float weight, int limb, Vector3 push)
        {
            var j = _joints[index];
            if (weight <= 0 || j == null || (j.LimbMask & (1 << limb)) == 0 ||
                !j.Chain.Spring.IsSimulating || j.Chain.Spring.HoldingSurfacePose) return;
            j.Sum += push * weight;
            j.Weight += weight;
        }

        // Barycentric coordinates of the closest point on a nondegenerate triangle.
        private static Vector3 ClosestWeights(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            var ab = b - a;
            var ac = c - a;
            float d1 = Vector3.Dot(ab, p - a), d2 = Vector3.Dot(ac, p - a);
            if (d1 <= 0 && d2 <= 0) return Vector3.right;
            float d3 = Vector3.Dot(ab, p - b), d4 = Vector3.Dot(ac, p - b);
            if (d3 >= 0 && d4 <= d3) return Vector3.up;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0)
                return Vector3.Lerp(Vector3.right, Vector3.up, d1 / (d1 - d3));
            float d5 = Vector3.Dot(ab, p - c), d6 = Vector3.Dot(ac, p - c);
            if (d6 >= 0 && d5 <= d6) return Vector3.forward;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0)
                return Vector3.Lerp(Vector3.right, Vector3.forward, d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 >= d3 && d5 >= d6)
                return Vector3.Lerp(Vector3.up, Vector3.forward, (d4 - d3) / ((d4 - d3)+(d5 - d6)));
            return new Vector3(va, vb, vc) / (va + vb + vc);
        }
    }
}

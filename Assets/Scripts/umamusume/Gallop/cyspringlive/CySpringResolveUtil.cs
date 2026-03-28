using System.Collections.Generic;
using UnityEngine;

namespace Gallop
{
    internal static class CySpringResolveUtil
    {
        public static Transform ResolveBone(Transform root, Dictionary<string, Transform> fastMap, string nameOrPath)
        {
            if (root == null || string.IsNullOrEmpty(nameOrPath))
                return null;

            if (fastMap != null && fastMap.TryGetValue(nameOrPath, out Transform direct) && direct != null)
                return direct;

            GameObject go = global::CySpring.FindGameObject(root.gameObject, nameOrPath);
            if (go != null)
                return go.transform;

            if (nameOrPath.Contains("/"))
            {
                string leaf = nameOrPath.Substring(nameOrPath.LastIndexOf('/') + 1);
                if (fastMap != null && fastMap.TryGetValue(leaf, out Transform fallback) && fallback != null)
                    return fallback;
            }

            return null;
        }

        public static Transform ResolveCollider(Transform root, Dictionary<string, Transform> colliderMap, string colliderName)
        {
            if (root == null || string.IsNullOrEmpty(colliderName))
                return null;

            if (colliderMap != null && colliderMap.TryGetValue(colliderName, out Transform t) && t != null)
                return t;

            GameObject go = global::CySpring.FindGameObject(root.gameObject, colliderName);
            return go != null ? go.transform : null;
        }

        public static string GetPath(Transform t, Transform stopExclusive = null)
        {
            if (t == null)
                return "<null>";

            List<string> parts = new List<string>();
            Transform cur = t;
            while (cur != null && cur != stopExclusive)
            {
                parts.Add(cur.name);
                cur = cur.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
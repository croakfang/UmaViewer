using System.Collections.Generic;
using UnityEngine;

namespace Gallop.UmaCySpring
{
    public static class UmaCySpringResolver
    {
        public static Transform Resolve(Transform searchRoot, Dictionary<string, Transform> fastMap, string nameOrPath)
        {
            if (searchRoot == null || string.IsNullOrEmpty(nameOrPath))
                return null;

            if (nameOrPath.Contains("/"))
            {
                Transform byPath = FindByPath(searchRoot, nameOrPath);
                if (byPath != null)
                    return byPath;
            }

            if (fastMap != null && fastMap.TryGetValue(nameOrPath, out Transform fast) && fast != null)
                return fast;

            Transform byName = FindDeep(searchRoot, nameOrPath);
            if (byName != null)
                return byName;

            string leaf = GetLeafName(nameOrPath);
            if (!string.IsNullOrEmpty(leaf))
            {
                if (fastMap != null && fastMap.TryGetValue(leaf, out Transform fastLeaf) && fastLeaf != null)
                    return fastLeaf;

                return FindDeep(searchRoot, leaf);
            }

            return null;
        }

        public static Transform FindByPath(Transform root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path))
                return null;

            string[] parts = path.Split('/');
            Transform cur = root;
            foreach (string part in parts)
            {
                if (string.IsNullOrEmpty(part))
                    continue;
                cur = FindDeep(cur, part);
                if (cur == null)
                    return null;
            }
            return cur;
        }

        public static Transform FindDeep(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrEmpty(targetName))
                return null;

            foreach (Transform child in root)
            {
                if (child.name == targetName)
                    return child;
            }

            foreach (Transform child in root)
            {
                Transform found = FindDeep(child, targetName);
                if (found != null)
                    return found;
            }

            return null;
        }

        public static string GetPath(Transform t)
        {
            if (t == null)
                return "<null>";

            List<string> parts = new List<string>();
            while (t != null)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static string GetLeafName(string value)
        {
            int idx = value.LastIndexOf('/');
            return idx >= 0 ? value.Substring(idx + 1) : value;
        }
    }
}

using System;
using System.Collections.Generic;
using Gallop.Cyalume;
using UnityEngine;

namespace Gallop.Live.Cyalume
{
    /// <summary>
    /// Lightweight runtime reconstruction of the official controller's object-selection path.
    /// It instantiates only the "default" and "random" cyalume prefabs, keeps two renderer sets,
    /// and exposes the active target set so the binder can follow the same default/random split as the game.
    /// </summary>
    public class CyalumeController3D : CyalumeControllerBase
    {
        [SerializeField] private bool _initialized;
        [SerializeField] private bool _verboseLog;

        private readonly List<Renderer> _allRenderers = new List<Renderer>();
        private readonly List<Renderer> _defaultRenderers = new List<Renderer>();
        private readonly List<Renderer> _randomRenderers = new List<Renderer>();
        private readonly List<Renderer> _targetRenderers = new List<Renderer>();

        private GameObject _defaultInstance;
        private GameObject _randomInstance;
        private bool _usingRandomTarget;

        public IReadOnlyList<Renderer> AllRenderers => _allRenderers;
        public IReadOnlyList<Renderer> TargetRenderers => _targetRenderers;
        public bool IsInitializedRuntime => _initialized;
        public bool UsingRandomTarget => _usingRandomTarget;

        private void Awake()
        {
            InitializeCyalumeObjectsOnly();
        }

        public void InitializeCyalumeObjectsOnly(bool forceRebuild = false)
        {
            if (_initialized && !forceRebuild)
                return;

            _initialized = false;
            _usingRandomTarget = false;
            _defaultInstance = null;
            _randomInstance = null;
            _allRenderers.Clear();
            _defaultRenderers.Clear();
            _randomRenderers.Clear();
            _targetRenderers.Clear();

            var assetHolder = GetComponent<AssetHolder>();
            if (assetHolder == null || assetHolder._assetTable == null)
            {
                Debug.LogWarning("[CyalumeController3D] AssetHolder or asset table is missing.");
                return;
            }

            DestroyExistingCyalumeChildren();

            TryInstantiateNamedPrefab(assetHolder, "default", out _defaultInstance, _defaultRenderers);
            TryInstantiateNamedPrefab(assetHolder, "random", out _randomInstance, _randomRenderers);

            _allRenderers.AddRange(_defaultRenderers);
            _allRenderers.AddRange(_randomRenderers);

            for (int i = 0; i < _allRenderers.Count; i++)
            {
                var renderer = _allRenderers[i];
                if (renderer)
                    renderer.enabled = false;
            }

            _initialized = true;
            ApplyTargetSelection(false, true);

            if (_verboseLog)
            {
                Debug.Log($"[CyalumeController3D] Initialize complete. default={_defaultRenderers.Count}, random={_randomRenderers.Count}, all={_allRenderers.Count}");
            }
        }

        public bool ApplyTargetSelection(bool preferRandom, bool forceRefresh = false)
        {
            if (!_initialized)
            {
                InitializeCyalumeObjectsOnly();
                if (!_initialized)
                    return false;
            }

            bool useRandom = preferRandom || _defaultRenderers.Count == 0;
            if (!useRandom && _defaultRenderers.Count == 0 && _randomRenderers.Count > 0)
                useRandom = true;

            bool changed = forceRefresh || _usingRandomTarget != useRandom || _targetRenderers.Count == 0;
            _usingRandomTarget = useRandom;

            _targetRenderers.Clear();
            if (_usingRandomTarget)
                _targetRenderers.AddRange(_randomRenderers.Count > 0 ? _randomRenderers : _defaultRenderers);
            else
                _targetRenderers.AddRange(_defaultRenderers.Count > 0 ? _defaultRenderers : _randomRenderers);

            for (int i = 0; i < _allRenderers.Count; i++)
            {
                var renderer = _allRenderers[i];
                if (renderer)
                    renderer.enabled = false;
            }

            for (int i = 0; i < _targetRenderers.Count; i++)
            {
                var renderer = _targetRenderers[i];
                if (renderer)
                    renderer.enabled = true;
            }

            if (changed && _verboseLog)
            {
                Debug.Log($"[CyalumeController3D] Target selection -> {(_usingRandomTarget ? "random" : "default")}, count={_targetRenderers.Count}");
            }

            return changed;
        }

        public bool ContainsTargetRenderer(Renderer renderer)
        {
            if (!renderer)
                return false;

            for (int i = 0; i < _targetRenderers.Count; i++)
            {
                if (_targetRenderers[i] == renderer)
                    return true;
            }

            return false;
        }

        public bool ContainsAnyRenderer(Renderer renderer)
        {
            if (!renderer)
                return false;

            for (int i = 0; i < _allRenderers.Count; i++)
            {
                if (_allRenderers[i] == renderer)
                    return true;
            }

            return false;
        }

        private void DestroyExistingCyalumeChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child == null)
                    continue;

                string childName = child.name ?? string.Empty;
                bool isKnownCyalumeChild =
                    childName.Equals("default", StringComparison.OrdinalIgnoreCase) ||
                    childName.Equals("random", StringComparison.OrdinalIgnoreCase) ||
                    childName.IndexOf("cyalume", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isKnownCyalumeChild)
                {
                    Destroy(child.gameObject);
                }
            }
        }

        private bool TryInstantiateNamedPrefab(AssetHolder assetHolder, string assetName, out GameObject instance, List<Renderer> destination)
        {
            instance = null;
            destination?.Clear();

            if (!TryResolvePrefab(assetHolder, assetName, out var prefab) || !prefab)
            {
                if (_verboseLog)
                    Debug.LogWarning($"[CyalumeController3D] Missing cyalume prefab: {assetName}");
                return false;
            }

            instance = Instantiate(prefab, transform);
            instance.name = prefab.name;
            SetLayerRecursively(instance, gameObject.layer);

            if (destination != null)
            {
                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                destination.AddRange(renderers);
            }

            if (_verboseLog)
            {
                Debug.Log($"[CyalumeController3D] Spawned cyalume prefab '{assetName}' as '{instance.name}' renderers={destination?.Count ?? 0}");
            }

            return true;
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            if (!root)
                return;

            root.layer = layer;
            for (int i = 0; i < root.transform.childCount; i++)
            {
                var child = root.transform.GetChild(i);
                if (child)
                    SetLayerRecursively(child.gameObject, layer);
            }
        }

        private static bool TryResolvePrefab(AssetHolder assetHolder, string assetName, out GameObject prefab)
        {
            prefab = null;
            if (assetHolder == null || assetHolder._assetTable == null || assetHolder._assetTable.list == null)
                return false;

            foreach (var pair in assetHolder._assetTable.list)
            {
                var candidate = pair.Value as GameObject;
                if (!candidate)
                    continue;

                if (candidate.name.Equals(assetName, StringComparison.OrdinalIgnoreCase))
                {
                    prefab = candidate;
                    return true;
                }

                string keyText = pair.Key != null ? pair.Key.ToString() : string.Empty;
                if (!string.IsNullOrEmpty(keyText) && keyText.Equals(assetName, StringComparison.OrdinalIgnoreCase))
                {
                    prefab = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}

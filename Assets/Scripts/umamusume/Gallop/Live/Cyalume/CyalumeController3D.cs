/*using System;
using System.Collections.Generic;
using Gallop.Cyalume;
using UnityEngine;

namespace Gallop.Live.Cyalume
{
    public class CyalumeController3D : CyalumeControllerBase
    {
        // Intentionally left blank.
        // Use CyalumeAutoBinder as the single runtime controller for
        // texture binding and UV animation in this project.

private readonly List<Material> _cyalumeMaterials = new List<Material>();

private float _passedTime;
private float _intervalTime = 1f / 30f;
private float _yOffset;

private void Awake()
{
    InitializeCyalume();
}

private void FixedUpdate()
{
    if (_cyalumeMaterials.Count == 0)
    {
        return;
    }

    _passedTime += Time.fixedDeltaTime;
    if (_passedTime < _intervalTime)
    {
        return;
    }

    _passedTime = 0f;
    _yOffset += 1f / 32f;
    if (_yOffset >= 1f)
    {
        _yOffset = 0f;
    }

    var offset = new Vector2(0f, _yOffset);
    for (int i = 0; i < _cyalumeMaterials.Count; i++)
    {
        var material = _cyalumeMaterials[i];
        if (material && material.HasProperty("_MainTex"))
        {
            material.SetTextureOffset("_MainTex", offset);
        }
    }
}

public void InitializeCyalume()
{
    _cyalumeMaterials.Clear();

    var assetHolder = GetComponent<AssetHolder>();
    if (assetHolder == null || assetHolder._assetTable == null)
    {
        return;
    }

    foreach (var pair in assetHolder._assetTable.list)
    {
        var prefab = pair.Value as GameObject;
        if (!prefab)
        {
            continue;
        }

        var instance = Instantiate(prefab, transform);
        if (prefab.name.IndexOf("cyalume", StringComparison.OrdinalIgnoreCase) < 0)
        {
            continue;
        }

        foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
        {
            foreach (var material in renderer.materials)
            {
                if (material && material.HasProperty("_MainTex"))
                {
                    _cyalumeMaterials.Add(material);
                }
            }
        }
    }
}*/
using System;
using Gallop.Cyalume;
using UnityEngine;

namespace Gallop.Live.Cyalume
{
    public class CyalumeController3D : CyalumeControllerBase
    {
        [SerializeField] private bool _initialized;

        private void Awake()
        {
            InitializeCyalumeObjectsOnly();
        }

        public void InitializeCyalumeObjectsOnly()
        {
            if (_initialized)
                return;

            var assetHolder = GetComponent<AssetHolder>();
            if (assetHolder == null || assetHolder._assetTable == null)
            {
                Debug.LogWarning("[CyalumeController3D] AssetHolder or asset table is missing.");
                return;
            }

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child == null)
                    continue;

                if (child.name.IndexOf("cyalume", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    child.name.IndexOf("mob", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Destroy(child.gameObject);
                }
            }

            int spawned = 0;

            foreach (var pair in assetHolder._assetTable.list)
            {
                var prefab = pair.Value as GameObject;
                if (!prefab)
                    continue;

                var instance = Instantiate(prefab, transform);
                instance.name = prefab.name + "(Clone)";
                spawned++;

                Debug.Log($"[CyalumeController3D] Spawned child key={pair.Key}, prefab={prefab.name}");
            }

            _initialized = true;
            Debug.Log($"[CyalumeController3D] Initialize complete. spawned={spawned}");
        }
    }
}
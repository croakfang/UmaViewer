using System;
using System.Collections.Generic;
using Gallop.Cyalume;
using UnityEngine;

namespace Gallop.Live.Cyalume
{
    public class CyalumeController3D : CyalumeControllerBase
    {
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
        }
    }
}

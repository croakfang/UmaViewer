using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    /// <summary>
    /// Runtime blink-light driver for already-loaded live stage objects.
    ///
    /// Adjusted to follow today's reverse-engineered structure more closely:
    /// 1) Only drives _blinklight / _blinkbeamlight roots.
    /// 2) Skips _spotlight3d_controller roots.
    /// 3) Scans children by Transform, excluding the root itself.
    /// 4) Child objects must parse a light index; invalid names are skipped.
    /// 5) Slot count is clamped to 10.
    /// 6) LED light roots/children are intentionally left alone so their own driver can control them.
    /// </summary>
    public class StageBlinkLightDriver : MonoBehaviour
    {
        public bool verboseLog = false;

        // UVAlphaMask: _ColorPowerMultiply
        // BlinkSimple: multiply into _ColorPower directly
        // LightAdd1:   use _ColorPowerMultiply when present
        public float emissionBoost = 1.2f;

        public float blinkSimpleUseNormalCorrection = 0f;
        public float localTimeScale = 1f;
        public float powerSmoothing = 0f;      // 0 = disabled
        public float offEps = 0.0001f;

        // Hysteresis (only used when useForceRenderingOff=true)
        public float onThreshold = 0f;
        public float offThreshold = 0f;

        // Usually safer to keep false to avoid sorting/pop issues
        public bool useForceRenderingOff = false;

        // If a root does not get a timeline callback every frame, optionally keep advancing its localTime
        public bool continueWhenNoUpdate = true;
        public float maxCatchupSecondsPerFrame = 0.2f;

        // UV / non-UV separation to reduce sort fighting
        public int uvSortingBias = 2048;
        public int rootSortingStride = 8192;

        // Stable ordering within one root
        public int stableSortStride = 4;

        // The reverse-engineered controller collects Renderer / LensFlare / WashLight.
        // Raw Unity Light is kept optional and defaults to OFF so LED / other light systems are not overridden.
        public bool driveUnityLights = false;
        public bool neverDisableLights = true;
        public float lightMinIntensity = 0f;
        public float lightIntensityScale = 1f;

        // Timeline probe
        public bool probeTimeline = false;
        public string probeTimelineNameContains = "";
        public bool probeTimelineOncePerFrame = true;

        private const int kMaxBlinkSlots = 10;
        private int _probeFrame = -1;

        private LiveTimelineControl _ctl;
        private StageController _stage;
        private MaterialPropertyBlock _mpb;

        private Func<float> _liveNowGetter;

        private float LiveNow()
        {
            return (_liveNowGetter != null) ? _liveNowGetter() : Time.time;
        }

        private static Func<float> BuildLiveNowGetter(LiveTimelineControl ctl)
        {
            if (ctl == null) return null;

            var t = ctl.GetType();
            var p = t.GetProperty("currentLiveTime") ?? t.GetProperty("CurrentLiveTime");
            if (p != null && p.PropertyType == typeof(float))
                return () => (float)p.GetValue(ctl, null);

            var f = t.GetField("currentLiveTime") ?? t.GetField("CurrentLiveTime");
            if (f != null && f.FieldType == typeof(float))
                return () => (float)f.GetValue(ctl);

            return null;
        }

        private struct CacheItem
        {
            public LiveTimelineKeyBlinkLightData key;
            public float localTime;
            public int updatedFrame;
        }

        private readonly Dictionary<string, CacheItem> _latest = new Dictionary<string, CacheItem>(256);

        // Root sorting base
        private readonly Dictionary<string, int> _rootBase = new Dictionary<string, int>(256);
        private int _nextRootBase = 0;

        // Init-off
        private bool _allOffInited = false;

        // Reused temp container to avoid per-frame GC
        private readonly List<string> _tmpKeys = new List<string>(256);

        private struct GroundPick { public string root; public int frame; }
        private readonly Dictionary<string, GroundPick> _groundChosen = new Dictionary<string, GroundPick>(16);

        // Cached component lists under each root
        private sealed class RootCache
        {
            public string rootName;
            public GameObject rootGo;
            public bool rootIsUv;
            public bool rootIsBeam;

            public bool dirty = true;
            public readonly List<RendererEntry> renderers = new List<RendererEntry>(64);
            public readonly List<LightEntry> lights = new List<LightEntry>(16);

            public int lastRendererCount;
            public int lastLightCount;
        }

        private readonly Dictionary<string, RootCache> _rootCache = new Dictionary<string, RootCache>(256);

        private enum IndexMode
        {
            Invalid = -1,
            LightPrefixNumber,
            SuffixNumber,
            LastDigits
        }

        private struct IndexToken
        {
            public bool valid;
            public IndexMode mode;
            public int n;

            public int Resolve(int slotCount)
            {
                if (!valid) return -1;

                slotCount = Mathf.Clamp(slotCount, 1, kMaxBlinkSlots);

                switch (mode)
                {
                    case IndexMode.LightPrefixNumber:
                        if (n >= 0 && n < slotCount) return n;
                        if (n >= 1 && n <= slotCount) return n - 1;
                        return -1;

                    case IndexMode.SuffixNumber:
                        {
                            int idx1 = n - 1;
                            if (idx1 >= 0 && idx1 < slotCount) return idx1;
                            if (n >= 0 && n < slotCount) return n;
                            return -1;
                        }

                    case IndexMode.LastDigits:
                        if (n >= 0 && n < slotCount) return n;
                        if (n >= 1 && n <= slotCount) return n - 1;
                        return -1;

                    default:
                        return -1;
                }
            }
        }

        private struct RendererEntry
        {
            public Renderer r;

            // Stable ordering
            public int stableSlot;

            // Cached name/index
            public string cachedChildName;
            public IndexToken token;

            // Cached type
            public bool isBlinkSimple;
            public bool isUvAlphaMask;
            public bool isLightAdd1;
            public bool hasColorPowerMultiply;
            public bool wantsMpb;

            // Auto-refresh guards
            public Material[] cachedSharedMaterialsRef;
            public int cachedSharedMaterialsLen;

            // Smoothing / hysteresis state
            public bool hasSmoothed;
            public float smoothedP;
            public bool renderOnState;
        }

        private struct LightEntry
        {
            public Light l;
            public string cachedChildName;
            public IndexToken token;
        }

        private static readonly Regex ReLightPrefix = new Regex(@"^light(\d+)(?:_|$)", RegexOptions.Compiled);
        private static readonly Regex ReSuffixNumber = new Regex(@"_(\d+)$", RegexOptions.Compiled);
        private static readonly Regex ReLastDigits = new Regex(@"(\d+)(?!.*\d)", RegexOptions.Compiled);

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            BindIfPossible();
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void BindIfPossible()
        {
            var dir = Director.instance;
            _ctl = dir ? dir._liveTimelineControl : null;
            _stage = dir ? dir._stageController : null;

            if (_ctl == null || _stage == null) return;

            _liveNowGetter = BuildLiveNowGetter(_ctl);

            _ctl.OnUpdateBlinkLight += OnBlinkLight;
            if (verboseLog) Debug.Log("[StageBlinkLightDriver] bound");
        }

        private void Unbind()
        {
            if (_ctl != null) _ctl.OnUpdateBlinkLight -= OnBlinkLight;

            _ctl = null;
            _stage = null;
            _liveNowGetter = null;

            _latest.Clear();
            _rootCache.Clear();

            _rootBase.Clear();
            _nextRootBase = 0;
            _allOffInited = false;

            _tmpKeys.Clear();
            _groundChosen.Clear();
        }

        private static bool IsBlinkRootName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.IndexOf("_spotlight3d_controller", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (name.IndexOf("ledlight", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            return name.IndexOf("_blinklight", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("_blinkbeamlight", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsBeamRootName(string name)
        {
            return !string.IsNullOrEmpty(name) &&
                   name.IndexOf("_blinkbeamlight", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsProtectedLedName(string name)
        {
            return !string.IsNullOrEmpty(name) &&
                   name.IndexOf("ledlight", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IndexToken InvalidToken()
        {
            return new IndexToken { valid = false, mode = IndexMode.Invalid, n = 0 };
        }

        private static IndexToken ParseIndexToken(string childName)
        {
            if (string.IsNullOrEmpty(childName))
                return InvalidToken();

            var m = ReLightPrefix.Match(childName);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int lightN))
                return new IndexToken { valid = true, mode = IndexMode.LightPrefixNumber, n = lightN };

            m = ReSuffixNumber.Match(childName);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int suffixN))
                return new IndexToken { valid = true, mode = IndexMode.SuffixNumber, n = suffixN };

            m = ReLastDigits.Match(childName);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int lastN))
                return new IndexToken { valid = true, mode = IndexMode.LastDigits, n = lastN };

            return InvalidToken();
        }

        private void OnBlinkLight(ref BlinkLightUpdateInfo info)
        {
            string rootName = (info.data != null) ? info.data.name : null;
            if (string.IsNullOrEmpty(rootName) || info.key == null) return;
            if (!IsBlinkRootName(rootName)) return;

            if (probeTimeline)
            {
                bool ok = string.IsNullOrEmpty(probeTimelineNameContains) ||
                          rootName.IndexOf(probeTimelineNameContains, StringComparison.OrdinalIgnoreCase) >= 0;

                if (ok)
                {
                    int f = Time.frameCount;
                    if (!probeTimelineOncePerFrame || _probeFrame != f)
                    {
                        _probeFrame = f;
                        Debug.Log($"[TL] frame={f} root={rootName} localTime={info.localTime:F6} keyFrame={info.key.frame}");
                    }
                }
            }

            _latest[rootName] = new CacheItem
            {
                key = info.key,
                localTime = Mathf.Max(0f, info.localTime),
                updatedFrame = Time.frameCount
            };
        }

        private void LateUpdate()
        {
            if (_ctl == null || _stage == null)
                BindIfPossible();

            if (_ctl == null || _stage == null || _stage.StageObjectMap == null) return;

            if (!_allOffInited)
            {
                InitAllStageLightsOff();
                _allOffInited = true;
            }

            if (_latest.Count == 0) return;

            EnforceGroundPanelExclusive();

            float liveNow2 = LiveNow();

            if (continueWhenNoUpdate)
            {
                float dt = Mathf.Min(Time.deltaTime, maxCatchupSecondsPerFrame);
                if (dt > 0f)
                {
                    _tmpKeys.Clear();
                    foreach (var k in _latest.Keys) _tmpKeys.Add(k);

                    for (int i = 0; i < _tmpKeys.Count; i++)
                    {
                        var key = _tmpKeys[i];
                        var item = _latest[key];
                        if (item.updatedFrame != Time.frameCount)
                        {
                            item.localTime += dt;
                            _latest[key] = item;
                        }
                    }
                }
            }

            foreach (var kv in _latest)
            {
                string rootName = kv.Key;
                if (!IsBlinkRootName(rootName))
                    continue;

                var item = kv.Value;

                if (_stage.StageObjectUnitMap.TryGetValue(rootName, out var unit) &&
                    unit != null && unit.ChildObjects != null && unit.ChildObjects.Length > 0)
                {
                    float localTimeNow = item.localTime * Mathf.Max(0.0001f, localTimeScale);

                    for (int i = 0; i < unit.ChildObjects.Length; i++)
                    {
                        var childPrefab = unit.ChildObjects[i];
                        if (childPrefab == null) continue;

                        if (_stage.StageObjectMap.TryGetValue(childPrefab.name, out var realGo) && realGo != null)
                        {
                            ApplyToRootCached(rootName + "__U" + i, realGo, item.key, localTimeNow, liveNow2);
                        }
                    }
                }
                else if (_stage.StageObjectMap.TryGetValue(rootName, out var rootGo) && rootGo != null)
                {
                    float localTimeNow = item.localTime * Mathf.Max(0.0001f, localTimeScale);
                    ApplyToRootCached(rootName, rootGo, item.key, localTimeNow, liveNow2);
                }
            }
        }

        private RootCache GetOrBuildRootCache(string rootName, GameObject rootGo)
        {
            if (!_rootCache.TryGetValue(rootName, out var rc) || rc == null)
            {
                rc = new RootCache { rootName = rootName };
                _rootCache[rootName] = rc;
            }

            if (rc.rootGo != rootGo)
            {
                rc.rootGo = rootGo;
                rc.rootIsUv = rootName.IndexOf("_uv", StringComparison.OrdinalIgnoreCase) >= 0;
                rc.rootIsBeam = IsBeamRootName(rootName);
                rc.dirty = true;
            }

            if (rc.dirty)
                BuildRootCache(rc);

            return rc;
        }

        private void BuildRootCache(RootCache rc)
        {
            rc.renderers.Clear();
            rc.lights.Clear();

            if (rc.rootGo == null || !IsBlinkRootName(rc.rootName))
            {
                rc.dirty = false;
                return;
            }

            rc.rootIsUv = rc.rootName.IndexOf("_uv", StringComparison.OrdinalIgnoreCase) >= 0;
            rc.rootIsBeam = IsBeamRootName(rc.rootName);

            var transforms = rc.rootGo.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t == null) continue;

                var go = t.gameObject;
                if (go == null || go == rc.rootGo) continue;

                string childName = go.name ?? "";
                if (IsProtectedLedName(childName)) continue;

                var token = ParseIndexToken(childName);
                if (!token.valid) continue;

                var r = t.GetComponent<Renderer>();
                if (r != null)
                {
                    bool isBlinkSimple, isUvAlphaMask, isLightAdd1, hasColorPowerMultiply;
                    DetectType(r, out isBlinkSimple, out isUvAlphaMask, out isLightAdd1, out hasColorPowerMultiply);

                    var mats = r.sharedMaterials;

                    var e = new RendererEntry
                    {
                        r = r,
                        stableSlot = rc.renderers.Count,

                        cachedChildName = childName,
                        token = token,

                        isBlinkSimple = isBlinkSimple,
                        isUvAlphaMask = isUvAlphaMask,
                        isLightAdd1 = isLightAdd1,
                        hasColorPowerMultiply = hasColorPowerMultiply,
                        wantsMpb = (isBlinkSimple || isUvAlphaMask || isLightAdd1),

                        cachedSharedMaterialsRef = mats,
                        cachedSharedMaterialsLen = (mats != null) ? mats.Length : 0,

                        hasSmoothed = false,
                        smoothedP = 0f,
                        renderOnState = false
                    };

                    rc.renderers.Add(e);
                }

                if (driveUnityLights)
                {
                    var l = t.GetComponent<Light>();
                    if (l != null)
                    {
                        var le = new LightEntry
                        {
                            l = l,
                            cachedChildName = childName,
                            token = token
                        };
                        rc.lights.Add(le);
                    }
                }
            }

            rc.lastRendererCount = rc.renderers.Count;
            rc.lastLightCount = rc.lights.Count;
            rc.dirty = false;

            if (verboseLog)
                Debug.Log($"[StageBlinkLightDriver] cache built root={rc.rootName} beam={rc.rootIsBeam} R={rc.renderers.Count} L={rc.lights.Count}");
        }

        private void InitAllStageLightsOff()
        {
            foreach (var kv in _stage.StageObjectMap)
            {
                string name = kv.Key;
                var go = kv.Value;
                if (go == null) continue;
                if (!IsBlinkRootName(name)) continue;

                ApplyRootOffCached(name, go);
            }

            if (verboseLog) Debug.Log("[StageBlinkLightDriver] InitAllStageLightsOff done");
        }

        private void EnforceGroundPanelExclusive()
        {
            _groundChosen.Clear();

            foreach (var kv in _latest)
            {
                string rootName = kv.Key;
                string g = GetGroundPanelGroupBase(rootName);
                if (g == null) continue;

                var item = kv.Value;
                if (!_groundChosen.TryGetValue(g, out var cur) || item.updatedFrame > cur.frame)
                    _groundChosen[g] = new GroundPick { root = rootName, frame = item.updatedFrame };
            }

            if (_groundChosen.Count == 0) return;

            foreach (var kv in _stage.StageObjectMap)
            {
                string name = kv.Key;
                var go = kv.Value;
                if (go == null) continue;

                string g = GetGroundPanelGroupBase(name);
                if (g == null) continue;

                if (_groundChosen.TryGetValue(g, out var pick) && name != pick.root)
                    ApplyRootOffCached(name, go);
            }
        }

        private static string GetGroundPanelGroupBase(string rootName)
        {
            int i = rootName.IndexOf("_blinklight_ground_panel", StringComparison.Ordinal);
            if (i < 0) return null;
            return rootName.Substring(0, i + "_blinklight_ground_panel".Length);
        }

        private int GetRootBase(string rootName)
        {
            if (_rootBase.TryGetValue(rootName, out int b)) return b;
            b = _nextRootBase;
            _nextRootBase += rootSortingStride;
            _rootBase[rootName] = b;
            return b;
        }

        private void ApplyToRootCached(string rootName, GameObject rootGo, LiveTimelineKeyBlinkLightData k, float localTimeNow, float liveNow)
        {
            var rc = GetOrBuildRootCache(rootName, rootGo);

            float env01 = BlinkEnvelope01(k, localTimeNow);
            int slotCount = Mathf.Clamp(MaxSlotCount(k), 1, kMaxBlinkSlots);
            int baseOrder = GetRootBase(rootName);

            bool hasC0 = (k.color0Array != null && k.color0Array.Length > 0);
            bool hasC1 = (k.color1Array != null && k.color1Array.Length > 0);

            int stride = Mathf.Max(1, stableSortStride);

            for (int i = 0; i < rc.renderers.Count; i++)
            {
                var e = rc.renderers[i];
                var r = e.r;
                if (r == null)
                {
                    rc.dirty = true;
                    continue;
                }

                string childName = r.gameObject ? r.gameObject.name : "";
                if (!ReferenceEquals(childName, e.cachedChildName) && childName != e.cachedChildName)
                {
                    e.cachedChildName = childName;
                    e.token = ParseIndexToken(childName);
                }

                int idx = e.token.Resolve(slotCount);
                if (idx < 0)
                {
                    rc.renderers[i] = e;
                    continue;
                }

                var mats = r.sharedMaterials;
                int matsLen = (mats != null) ? mats.Length : 0;
                if (mats != e.cachedSharedMaterialsRef || matsLen != e.cachedSharedMaterialsLen)
                {
                    bool isBlinkSimple, isUvAlphaMask, isLightAdd1, hasColorPowerMultiply;
                    DetectType(r, out isBlinkSimple, out isUvAlphaMask, out isLightAdd1, out hasColorPowerMultiply);

                    e.isBlinkSimple = isBlinkSimple;
                    e.isUvAlphaMask = isUvAlphaMask;
                    e.isLightAdd1 = isLightAdd1;
                    e.hasColorPowerMultiply = hasColorPowerMultiply;
                    e.wantsMpb = (isBlinkSimple || isUvAlphaMask || isLightAdd1);

                    e.cachedSharedMaterialsRef = mats;
                    e.cachedSharedMaterialsLen = matsLen;
                }

                Color c0 = PickColor(k.color0Array, idx, Color.white);
                Color c1 = PickColor(k.color1Array, idx, Color.white);
                float base01 = PickFloat(k.powerArray, idx, 1f);

                float on01 = Mathf.Clamp01(base01) * env01;
                float pRaw = (on01 <= offEps) ? 0f : Mathf.Lerp(k.powerMin, k.powerMax, on01);

                float p = pRaw;
                if (powerSmoothing > 0f)
                {
                    float a = 1f - Mathf.Exp(-powerSmoothing * Time.deltaTime);
                    if (!e.hasSmoothed)
                    {
                        e.hasSmoothed = true;
                        e.smoothedP = pRaw;
                    }
                    else
                    {
                        e.smoothedP = Mathf.Lerp(e.smoothedP, pRaw, a);
                    }
                    p = e.smoothedP;
                }

                int uvBias = (rc.rootIsUv || e.isUvAlphaMask) ? uvSortingBias : 0;
                r.sortingOrder = baseOrder + uvBias + e.stableSlot * stride;
                r.enabled = true;

                if (e.wantsMpb)
                {
                    if (useForceRenderingOff)
                    {
                        float onTh = Mathf.Max(0f, onThreshold);
                        float offTh = Mathf.Clamp(offThreshold, 0f, onTh);

                        if (!e.renderOnState && p >= onTh) e.renderOnState = true;
                        else if (e.renderOnState && p <= offTh) e.renderOnState = false;

                        r.forceRenderingOff = !e.renderOnState;
                    }
                    else
                    {
                        r.forceRenderingOff = false;
                    }

                    // Beam mode is currently only structurally separated.
                    // The exact HSV special-case path is not fully re-created yet.
                    ApplyMpbToRenderer_MutualExclusive(
                        r, c0, c1, p, liveNow,
                        e.isBlinkSimple, e.isUvAlphaMask, e.isLightAdd1, e.hasColorPowerMultiply,
                        hasC0, hasC1);
                }
                else
                {
                    r.forceRenderingOff = false;
                }

                rc.renderers[i] = e;
            }

            if (driveUnityLights && rc.lights != null)
            {
                for (int i = 0; i < rc.lights.Count; i++)
                {
                    var le = rc.lights[i];
                    var l = le.l;
                    if (l == null)
                    {
                        rc.dirty = true;
                        continue;
                    }

                    string childName = l.gameObject ? l.gameObject.name : "";
                    if (!ReferenceEquals(childName, le.cachedChildName) && childName != le.cachedChildName)
                    {
                        le.cachedChildName = childName;
                        le.token = ParseIndexToken(childName);
                    }

                    int idx = le.token.Resolve(slotCount);
                    if (idx < 0)
                    {
                        rc.lights[i] = le;
                        continue;
                    }

                    Color c0 = PickColor(k.color0Array, idx, Color.white);
                    float base01 = PickFloat(k.powerArray, idx, 1f);

                    float on01 = Mathf.Clamp01(base01) * env01;
                    float p = (on01 <= offEps) ? 0f : Mathf.Lerp(k.powerMin, k.powerMax, on01);

                    if (neverDisableLights) l.enabled = true;
                    else l.enabled = (p > offEps);

                    l.color = c0;
                    l.intensity = Mathf.Max(lightMinIntensity, p) * lightIntensityScale;

                    rc.lights[i] = le;
                }
            }
        }

        private void ApplyRootOffCached(string rootName, GameObject rootGo)
        {
            var rc = GetOrBuildRootCache(rootName, rootGo);

            for (int i = 0; i < rc.renderers.Count; i++)
            {
                var e = rc.renderers[i];
                var r = e.r;
                if (r == null)
                {
                    rc.dirty = true;
                    continue;
                }

                r.enabled = true;

                if (e.wantsMpb)
                {
                    r.forceRenderingOff = useForceRenderingOff;
                    e.renderOnState = false;
                    e.hasSmoothed = false;
                    e.smoothedP = 0f;

                    _mpb.Clear();

                    if (e.isUvAlphaMask)
                    {
                        _mpb.SetColor("_MulColor0", Color.black);
                        _mpb.SetColor("_MulColor1", Color.black);
                        _mpb.SetFloat("_ColorPower", 0f);
                        _mpb.SetFloat("_ColorPowerMultiply", emissionBoost);
                        _mpb.SetFloat("_AppTime", LiveNow());
                    }
                    else if (e.isLightAdd1)
                    {
                        _mpb.SetColor("_MulColor0", Color.black);
                        _mpb.SetColor("_MulColor1", Color.black);
                        _mpb.SetFloat("_ColorPower", 0f);
                        if (e.hasColorPowerMultiply) _mpb.SetFloat("_ColorPowerMultiply", emissionBoost);
                    }
                    else if (e.isBlinkSimple)
                    {
                        _mpb.SetColor("_BlinkLightColor", Color.black);
                        _mpb.SetFloat("_ColorPower", 0f);
                        _mpb.SetFloat("_UseNormalCorrection", blinkSimpleUseNormalCorrection);
                    }

                    r.SetPropertyBlock(_mpb);
                }
                else
                {
                    r.forceRenderingOff = false;
                }

                rc.renderers[i] = e;
            }

            if (driveUnityLights && rc.lights != null)
            {
                for (int i = 0; i < rc.lights.Count; i++)
                {
                    var le = rc.lights[i];
                    var l = le.l;
                    if (l == null)
                    {
                        rc.dirty = true;
                        continue;
                    }

                    if (neverDisableLights) l.enabled = true;
                    else l.enabled = false;

                    l.intensity = 0f;
                    rc.lights[i] = le;
                }
            }
        }

        private void ApplyMpbToRenderer_MutualExclusive(
            Renderer r,
            Color c0,
            Color c1,
            float p,
            float liveNow,
            bool isBlinkSimple,
            bool isUvAlphaMask,
            bool isLightAdd1,
            bool hasColorPowerMultiply,
            bool hasC0,
            bool hasC1)
        {
            _mpb.Clear();

            if (isUvAlphaMask)
            {
                if (hasC0) _mpb.SetColor("_MulColor0", c0);
                if (hasC1) _mpb.SetColor("_MulColor1", c1);
                _mpb.SetFloat("_ColorPower", p);
                _mpb.SetFloat("_ColorPowerMultiply", emissionBoost);
                _mpb.SetFloat("_AppTime", liveNow);
            }
            else if (isLightAdd1)
            {
                if (hasC0) _mpb.SetColor("_MulColor0", c0);
                if (hasC1) _mpb.SetColor("_MulColor1", c1);
                _mpb.SetFloat("_ColorPower", p);
                if (hasColorPowerMultiply) _mpb.SetFloat("_ColorPowerMultiply", emissionBoost);
            }
            else if (isBlinkSimple)
            {
                if (hasC0) _mpb.SetColor("_BlinkLightColor", c0);
                _mpb.SetFloat("_ColorPower", p * emissionBoost);
                _mpb.SetFloat("_UseNormalCorrection", blinkSimpleUseNormalCorrection);
            }

            r.SetPropertyBlock(_mpb);
        }

        private void DetectType(
            Renderer r,
            out bool isBlinkSimple,
            out bool isUvAlphaMask,
            out bool isLightAdd1,
            out bool hasColorPowerMultiply)
        {
            isBlinkSimple = false;
            isUvAlphaMask = false;
            isLightAdd1 = false;
            hasColorPowerMultiply = false;

            var mats = r.sharedMaterials;
            if (mats == null) return;

            bool hasBlinkColor = false, hasColorPower = false, hasMul0 = false, hasMul1 = false, hasMultiply = false;

            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;

                var sh = m.shader;
                string sn = sh ? (sh.name ?? "") : "";

                if (sn.IndexOf("UVAlphaMask", StringComparison.OrdinalIgnoreCase) >= 0) isUvAlphaMask = true;
                if (sn.IndexOf("LightBlinkSimple", StringComparison.OrdinalIgnoreCase) >= 0) isBlinkSimple = true;

                if (!isUvAlphaMask && sn.IndexOf("LightAdd1", StringComparison.OrdinalIgnoreCase) >= 0) isLightAdd1 = true;

                if (m.HasProperty("_BlinkLightColor")) hasBlinkColor = true;
                if (m.HasProperty("_ColorPower")) hasColorPower = true;
                if (m.HasProperty("_MulColor0")) hasMul0 = true;
                if (m.HasProperty("_MulColor1")) hasMul1 = true;
                if (m.HasProperty("_ColorPowerMultiply")) hasMultiply = true;
            }

            if (!isBlinkSimple && hasBlinkColor && hasColorPower) isBlinkSimple = true;
            if (!isUvAlphaMask && hasMul0 && hasMul1 && hasColorPower && hasMultiply) isUvAlphaMask = true;
            if (!isLightAdd1 && !isUvAlphaMask && hasMul0 && hasMul1 && hasColorPower) isLightAdd1 = true;

            hasColorPowerMultiply = hasMultiply;
        }

        private static int MaxSlotCount(LiveTimelineKeyBlinkLightData k)
        {
            int a = (k?.color0Array != null) ? k.color0Array.Length : 0;
            int b = (k?.color1Array != null) ? k.color1Array.Length : 0;
            int c = (k?.powerArray != null) ? k.powerArray.Length : 0;
            return Mathf.Clamp(Mathf.Max(1, Mathf.Max(a, Mathf.Max(b, c))), 1, kMaxBlinkSlots);
        }

        private static Color PickColor(Color[] arr, int idx, Color fallback)
        {
            if (arr == null || arr.Length == 0) return fallback;
            idx = Mathf.Clamp(idx, 0, arr.Length - 1);
            return arr[idx];
        }

        private static float PickFloat(float[] arr, int idx, float fallback)
        {
            if (arr == null || arr.Length == 0) return fallback;
            idx = Mathf.Clamp(idx, 0, arr.Length - 1);
            return arr[idx];
        }

        private static float BlinkEnvelope01(LiveTimelineKeyBlinkLightData k, float t)
        {
            if (k == null) return 0f;

            if (t < k.waitTime) return 0f;
            t -= k.waitTime;

            float on = Mathf.Max(0f, k.turnOnTime);
            float keep = Mathf.Max(0f, k.keepTime);
            float off = Mathf.Max(0f, k.turnOffTime);
            float interval = Mathf.Max(0f, k.intervalTime);

            float cycle = on + keep + off + interval;
            if (cycle <= 0f) return 1f;

            float phase = Mathf.Repeat(t, cycle);

            if (on > 0f && phase < on) return phase / on;
            phase -= on;

            if (phase < keep) return 1f;
            phase -= keep;

            if (off > 0f && phase < off) return 1f - (phase / off);
            return 0f;
        }
    }
}

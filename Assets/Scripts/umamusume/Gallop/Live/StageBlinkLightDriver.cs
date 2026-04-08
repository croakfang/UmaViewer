using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
   
                if (phase < s.turnOnTime + s.keepTime)
                {
                    s.currentPower = s.powerMax;
                    return;
                }

                if (phase < s.turnOnTime + s.keepTime + s.turnOffTime)
                {
                    float u = 1f - ((phase - (s.turnOnTime + s.keepTime)) / Mathf.Max(0.0001f, s.turnOffTime));
                    s.currentPower = isLastLoop
                        ? (u * s.powerMax)
                        : (u * s.powerDiff + s.powerMin);
                    return;
                }

                s.currentPower = isLastLoop ? 0f : s.powerMin;
            }
            else
            {
                s.currentPower = s.basePower;
                s.currentHRatio = 0f;
                s.currentSRatio = 0f;
                s.currentVRatio = 0f;
                s.loopCount = 0;
            }
        }

        private static void BuildCurrentColors(BlinkRootRuntime rt, int slotCount)
        {
            for (int i = 0; i < slotCount; i++)
            {
                var s = rt.slots[i];
                float p = Mathf.Max(0f, s.currentPower);
                Color outColor;

                if (rt.pattern == 0)
                {
                    outColor = SafeColor(rt.color0, i, Color.white);
                }
                else
                {
                    switch (rt.colorType)
                    {
                        case 1:
                            {
                                int src = (i + slotCount - PositiveMod(s.loopCount, slotCount)) % slotCount;
                                outColor = SafeColor(rt.color0, src, Color.white);
                                break;
                            }
                        case 2:
                            {
                                int src = (i + PositiveMod(s.loopCount, slotCount)) % slotCount;
                                outColor = SafeColor(rt.color0, src, Color.white);
                                break;
                            }
                        case 3:
                            outColor = ((s.loopCount & 1) != 0)
                                ? SafeColor(rt.color1, i, Color.white)
                                : SafeColor(rt.color0, i, Color.white);
                            break;
                        default:
                            outColor = GetBlinkColor(
                                SafeColor(rt.color0, i, Color.white),
                                SafeColor(rt.color1, i, Color.white),
                                s.currentHRatio,
                                s.currentSRatio,
                                s.currentVRatio,
                                rt.isReverseHue[i]);
                            break;
                    }
                }

                // RGB stores pure color, W stores power to avoid double-multiplying brightness.
                rt.currentColors[i] = new Vector4(outColor.r, outColor.g, outColor.b, p);
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

        private void ApplyMpbToRenderer(
            Renderer r,
            Color currentColor,
            float p,
            float liveNow,
            bool isBlinkSimple,
            bool isUvAlphaMask,
            bool isLightAdd1,
            bool hasColorPowerMultiply)
        {
            _mpb.Clear();

            if (isUvAlphaMask)
            {
                _mpb.SetColor("_MulColor0", currentColor);
                _mpb.SetColor("_MulColor1", currentColor);
                _mpb.SetFloat("_ColorPower", p);
                _mpb.SetFloat("_ColorPowerMultiply", emissionBoost);
                _mpb.SetFloat("_AppTime", liveNow);
            }
            else if (isLightAdd1)
            {
                _mpb.SetColor("_MulColor0", currentColor);
                _mpb.SetColor("_MulColor1", currentColor);
                _mpb.SetFloat("_ColorPower", p);
                if (hasColorPowerMultiply) _mpb.SetFloat("_ColorPowerMultiply", emissionBoost);
            }
            else if (isBlinkSimple)
            {
                _mpb.SetColor("_BlinkLightColor", currentColor);
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

        private static int StableHash32(int a, int b)
        {
            unchecked
            {
                uint x = 2166136261u;
                x = (x ^ (uint)a) * 16777619u;
                x = (x ^ (uint)b) * 16777619u;
                x ^= x >> 13;
                x *= 1274126177u;
                x ^= x >> 16;
                return (int)x;
            }
        }

        private static float GetDeterministicRatio01(int a, int b)
        {
            unchecked
            {
                uint x = 2166136261u;
                x = (x ^ (uint)(a + 1)) * 16777619u;
                x = (x ^ (uint)(b + 1)) * 16777619u;
                x ^= x >> 13;
                x *= 1274126177u;
                x ^= x >> 16;
                return (x & 0x00FFFFFFu) / 16777215.0f;
            }
        }

        private static Color GetBlinkColor(Color color0, Color color1, float hueRatio, float saturationRatio, float valueRatio, bool isReverseHue)
        {
            Color.RGBToHSV(color0, out float h0, out float s0, out float v0);
            Color.RGBToHSV(color1, out float h1, out float s1, out float v1);

            float h;
            if (isReverseHue)
            {
                float delta = h1 - h0;
                if (Mathf.Abs(delta) < 1e-6f)
                {
                    h = h0;
                }
                else
                {
                    if (delta > 0f) delta -= 1f;
                    else delta += 1f;
                    h = Mathf.Repeat(h0 + delta * hueRatio, 1f);
                }
            }
            else
            {
                h = Mathf.LerpAngle(h0 * 360f, h1 * 360f, hueRatio) / 360f;
                h = Mathf.Repeat(h, 1f);
            }

            float s = Mathf.Lerp(s0, s1, saturationRatio);
            float v = Mathf.Lerp(v0, v1, valueRatio);
            return Color.HSVToRGB(h, Mathf.Clamp01(s), Mathf.Clamp01(v));
        }

        private static int PositiveMod(int a, int b)
        {
            if (b <= 0) return 0;
            int m = a % b;
            return (m < 0) ? (m + b) : m;
        }

        private static void CopyColors(Color[] dst, Color[] src, Color fill)
        {
            Color last = fill;
            for (int i = 0; i < kMaxBlinkSlots; i++)
            {
                if (src != null && i < src.Length)
                    last = src[i];
                dst[i] = last;
            }
        }

        private static void CopyReverseHue(bool[] dst, int[] src)
        {
            bool last = false;
            for (int i = 0; i < kMaxBlinkSlots; i++)
            {
                if (src != null && i < src.Length)
                    last = src[i] != 0;
                dst[i] = last;
            }
        }

        private static Color SafeColor(Color[] arr, int idx, Color fallback)
        {
            if (arr == null || arr.Length == 0) return fallback;
            idx = Mathf.Clamp(idx, 0, arr.Length - 1);
            return arr[idx];
        }

        private static int PickInt(int[] arr, int idx, int fallback)
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
    }
}
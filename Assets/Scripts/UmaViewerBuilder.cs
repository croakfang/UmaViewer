﻿using CriWareFormats;
using Gallop;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UmaMusumeAudio;
using UnityEditor;
using UnityEngine;

public class UmaViewerBuilder : MonoBehaviour
{
    public static UmaViewerBuilder Instance;
    UmaViewerMain Main => UmaViewerMain.Instance;
    UmaViewerUI UI => UmaViewerUI.Instance;

    public List<AssetBundle> Loaded;
    public List<Shader> ShaderList = new List<Shader>();
    public Material TransMaterialCharas;
    public Material TransMaterialProps;
    public UmaContainer CurrentUMAContainer;
    public UmaContainer CurrentLiveContainer;
    public UmaContainer CurrentOtherContainer;

    public List<AudioSource> CurrentAudioSources = new List<AudioSource>();

    public AnimatorOverrideController OverrideController;


    private void Awake()
    {
        Instance = this;
    }

    public IEnumerator LoadUma(int id, string costumeId, bool mini)
    {
        if (CurrentUMAContainer != null)
        {
            Destroy(CurrentUMAContainer);
        }
        CurrentUMAContainer = new GameObject($"{id}_{costumeId}").AddComponent<UmaContainer>();

        UnloadAllBundle();

        yield return UmaViewerDownload.DownloadText($"https://www.tracenacademy.com/api/CharaData/{id}", txt =>
        {
            Debug.Log(txt);
            CurrentUMAContainer.CharaData = JObject.Parse(txt);
            if (mini)
            {
                LoadMiniUma(id, costumeId);
            }
            else
            {
                LoadNormalUma(id, costumeId);
            }
        });
    }

    private void LoadNormalUma(int id, string costumeId)
    {
        JObject charaData = CurrentUMAContainer.CharaData;
        bool genericCostume = CurrentUMAContainer.IsGeneric = costumeId.Length >= 4;
        string skin = (string)charaData["skin"],
               height = (string)charaData["height"],
               socks = (string)charaData["socks"],
               bust = (string)charaData["bust"],
               sex = (string)charaData["sex"],
               costumeIdShort = "";

        UmaDatabaseEntry asset = null;
        if (genericCostume)
        {
            costumeIdShort = costumeId.Remove(costumeId.LastIndexOf('_'));
            CurrentUMAContainer.VarCostumeIdShort = costumeIdShort;
            CurrentUMAContainer.VarCostumeIdLong = costumeId;
            CurrentUMAContainer.VarBust = bust;
            CurrentUMAContainer.VarSkin = skin;
            CurrentUMAContainer.VarSocks = socks;
            CurrentUMAContainer.VarHeight = height;
            //Not sure about the pattern, some models are missing
            string body = "";
            if (costumeId.Split('_')[2] == "03")
                body = UmaDatabaseController.BodyPath + $"bdy{costumeIdShort}/pfb_bdy{costumeId}_{sex}_{0}_{(bust == "0" ? "1" : bust == "4" ? "3" : bust)}";
            else
                body = UmaDatabaseController.BodyPath + $"bdy{costumeIdShort}/pfb_bdy{costumeId}_{sex}_{0}_{(bust == "0" ? "1" : bust == "4" ? "3" : bust)}";
            Debug.Log("Looking for " + body);
            asset = UmaViewerMain.Instance.AbList.FirstOrDefault(a => a.Name == body);
        }
        else asset = UmaViewerMain.Instance.AbList.FirstOrDefault(a => a.Name == UmaDatabaseController.BodyPath + $"bdy{id}_{costumeId}/pfb_bdy{id}_{costumeId}");

        if (asset == null)
        {
            Debug.LogError("No body, can't load!");
            return;
        }
        else if (genericCostume)
        {
            string texPattern1 = "", texPattern2 = "", texPattern3 = "", texPattern4 = "";
            switch (costumeId.Split('_')[0])
            {
                case "0001":
                    texPattern1 = $"tex_bdy{costumeIdShort}_00_{skin}_{bust}_0{socks}";
                    texPattern2 = $"tex_bdy{costumeIdShort}_00_0_{bust}_00_";
                    texPattern3 = $"tex_bdy{costumeIdShort}_00_waku";
                    texPattern4 = $"tex_bdy{costumeIdShort}_num";
                    break;
                case "0003":
                    texPattern1 = $"tex_bdy{costumeIdShort}_00_{skin}_{bust}";
                    texPattern2 = $"tex_bdy{costumeIdShort}_00_0_{bust}";
                    break;
                case "0006": //last var is color?
                    texPattern1 = $"tex_bdy{costumeId}_{skin}_{bust}_0{0}";
                    texPattern2 = $"tex_bdy{costumeId}_0_{bust}_00_";
                    break;
                default:
                    texPattern1 = $"tex_bdy{costumeId}_{skin}_{bust}";
                    texPattern2 = $"tex_bdy{costumeId}_0_{bust}";
                    break;
            }
            Debug.Log(texPattern1 + " " + texPattern2);
            //Load Body Textures
            foreach (var asset1 in UmaViewerMain.Instance.AbList.Where(a => a.Name.StartsWith(UmaDatabaseController.BodyPath)
                && (a.Name.Contains(texPattern1)
                || a.Name.Contains(texPattern2)
                || (string.IsNullOrEmpty(texPattern3) ? false : a.Name.Contains(texPattern3))
                || (string.IsNullOrEmpty(texPattern4) ? false : a.Name.Contains(texPattern4)))))
            {
                RecursiveLoadAsset(asset1);
            }
            //Load Body
            RecursiveLoadAsset(asset);

            //Load Physics
            foreach (var asset1 in UmaViewerMain.Instance.AbList.Where(a => a.Name.StartsWith(UmaDatabaseController.BodyPath + $"bdy{costumeIdShort}/clothes")))
            {
                if (asset1.Name.Contains("cloth00") && asset1.Name.Contains("bust" + bust))
                    RecursiveLoadAsset(asset1);
            }
        }
        else
        {
            RecursiveLoadAsset(asset);

            //Load Physics
            foreach (var asset1 in UmaViewerMain.Instance.AbList.Where(a => a.Name.StartsWith(UmaDatabaseController.BodyPath + $"bdy{id}_{costumeId}/clothes")))
            {
                if (asset1.Name.Contains("cloth00"))
                    RecursiveLoadAsset(asset1);
            }
        }


        string head = UmaDatabaseController.HeadPath + $"chr{id}_{costumeId}/pfb_chr{id}_{costumeId}";
        asset = UmaViewerMain.Instance.AbList.FirstOrDefault(a => a.Name == head);
        bool isDefaultHead = false;
        //Some costumes don't have custom heads
        if (costumeId != "00" && asset == null)
        {
            asset = UmaViewerMain.Instance.AbList.FirstOrDefault(a => a.Name == UmaDatabaseController.HeadPath + $"chr{id}_00/pfb_chr{id}_00");
            isDefaultHead = true;
        }

        if (asset != null)
        {
            //Load Hair Textures
            foreach (var asset1 in UmaViewerMain.Instance.AbList.Where(a => a.Name.StartsWith($"{UmaDatabaseController.HeadPath}chr{id}_{costumeId}/textures")))
            {
                RecursiveLoadAsset(asset1);
            }
            //Load Head
            RecursiveLoadAsset(asset);

            //Load Physics
            if (isDefaultHead)
            {
                foreach (var asset1 in UmaViewerMain.Instance.AbList.Where(a => a.Name.StartsWith($"{UmaDatabaseController.HeadPath}chr{id}_00/clothes")))
                {
                    if (asset1.Name.Contains("cloth00"))
                        RecursiveLoadAsset(asset1);
                }
            }
            else
            {
                foreach (var asset1 in UmaViewerMain.Instance.AbList.Where(a => a.Name.StartsWith($"{UmaDatabaseController.HeadPath}chr{id}_{costumeId}/clothes")))
                {
                    if (asset1.Name.Contains("cloth00"))
                        RecursiveLoadAsset(asset1);
                }
            }

        }

        int tailId = (int)charaData["tailModelId"];
        if (tailId != 0)
        {
            string tailName = $"tail{tailId.ToString().PadLeft(4, '0')}_00";
            string tailPath = $"3d/chara/tail/{tailName}/";
            string tailPfb = tailPath + $"pfb_{tailName}";
            asset = UmaViewerMain.Instance.AbList.FirstOrDefault(a => a.Name == tailPfb);
            if (asset != null)
            {
                foreach (var asset1 in UmaViewerMain.Instance.AbList.Where(a => a.Name.StartsWith($"{tailPath}textures/tex_{tailName}_{id}") || a.Name.StartsWith($"{tailPath}textures/tex_{tailName}_0000")))
                {
                    RecursiveLoadAsset(asset1);
                }
                RecursiveLoadAsset(asset);


                //Load Physics
                foreach (var asset1 in UmaViewerMain.Instance.AbList.Where(a => a.Name.StartsWith($"{tailPath}clothes")))
                {
                    if (asset1.Name.Contains("cloth00"))
                        RecursiveLoadAsset(asset1);
                }
            }
            else
            {
                Debug.Log("no tail");
            }
        }

        //Load FacialMorph
        if (CurrentUMAContainer.Heads.Count > 0)
        {
            var firsehead = CurrentUMAContainer.Heads[0];
            var FaceDriven = firsehead.GetComponent<AssetHolder>()._assetTable.list.Find(a => { return a.Key == "facial_target"; }).Value as FaceDrivenKeyTarget;
            CurrentUMAContainer.FaceDrivenKeyTargets = FaceDriven;
            FaceDriven.callBack = UmaViewerUI.Instance;
            FaceDriven.Initialize(firsehead.GetComponentsInChildren<Transform>().ToList());
        }

        LoadAsset(UmaViewerMain.Instance.AbList.FirstOrDefault(a => a.Name.EndsWith($"anm_eve_chr{id}_00_idle01_loop")));
    }

    private void LoadMiniUma(int id, string costumeId)
    {
        JObject charaData = CurrentUMAContainer.CharaData;
        CurrentUMAContainer.IsMini = true;
        bool isGeneric = CurrentUMAContainer.IsGeneric = costumeId.Length >= 4;
        string skin = (string)charaData["skin"],
               height = (string)charaData["height"],
               socks = (string)charaData["socks"],
               bust = (string)charaData["bust"],
               sex = (string)charaData["sex"],
               costumeIdShort = "";
        bool customHead = true;

        UmaDatabaseEntry asset = null;
        if (isGeneric)
        {
            costumeIdShort = costumeId.Remove(costumeId.LastIndexOf('_'));
            string body = $"3d/chara/mini/body/mbdy{costumeIdShort}/pfb_mbdy{costumeId}_0";
            asset = UmaViewerMain.Instance.AbList.FirstOrDefault(a => a.Name == body);
        }
        else asset = UmaViewerMain.Instance.AbList.FirstOrDefault(a => a.Name == $"3d/chara/mini/body/mbdy{id}_{costumeId}/pfb_mbdy{id}_{costumeId}");
        if (asset == null)
        {
            Debug.LogError("No body, can't load!");
            return;
        }
        else if (isGeneric)
        {
            string texPattern1 = "";
            switch (costumeId.Split('_')[0])
            {
                case "0003":
                    texPattern1 = $"tex_mbdy{costumeIdShort}_00_{skin}_{0}";
                    break;
                default:
                    texPattern1 = $"tex_mbdy{costumeId}_{skin}_{0}";
                    break;
            }
            //Load Body Textures
            foreach (var asset1 in UmaViewerMain.Instance.AbList.Where(a => a.Name.StartsWith("3d/chara/mini/body/") && a.Name.Contains(texPattern1)))
            {
                RecursiveLoadAsset(asset1);
            }
            //Load Body
            RecursiveLoadAsset(asset);
        }
        else
            RecursiveLoadAsset(asset);

        string hair = $"3d/chara/mini/head/mchr{id}_{costumeId}/pfb_mchr{id}_{costumeId}_hair";
        asset = UmaViewerMain.Instance.AbList.FirstOrDefault(a => a.Name == hair);
        if (costumeId != "00" && asset == null)
        {
            customHead = false;
            asset = UmaViewerMain.Instance.AbList.FirstOrDefault(a => a.Name == $"3d/chara/mini/head/mchr{id}_00/pfb_mchr{id}_00_hair");
        }
        if (asset != null)
        {
            //Load Hair Textures
            if (customHead)
            {
                foreach (var asset1 in UmaViewerMain.Instance.AbList.Where(a => a.Name.StartsWith($"3d/chara/mini/head/mchr{id}_{costumeId}/textures")))
                {
                    RecursiveLoadAsset(asset1);
                }
            }
            else
            {
                foreach (var asset1 in UmaViewerMain.Instance.AbList.Where(a => a.Name.StartsWith($"3d/chara/mini/head/mchr{id}_00/textures")))
                {
                    RecursiveLoadAsset(asset1);
                }
            }

            //Load Hair
            RecursiveLoadAsset(asset);
        }

        string head = $"3d/chara/mini/head/mchr0001_00/pfb_mchr0001_00_face0";
        asset = UmaViewerMain.Instance.AbList.FirstOrDefault(a => a.Name == head);
        if (asset != null)
        {
            //Load Head Textures
            foreach (var asset1 in UmaViewerMain.Instance.AbList.Where(a => a.Name.StartsWith($"3d/chara/mini/head/mchr0001_00/textures/tex_mchr0001_00_face0_{skin}")))
            {
                RecursiveLoadAsset(asset1);
            }
            //Load Head
            RecursiveLoadAsset(asset);
        }
    }

    public void LoadProp(UmaDatabaseEntry entry)
    {
        if (CurrentOtherContainer != null)
        {
            Destroy(CurrentOtherContainer);
        }
        UnloadAllBundle();

        CurrentOtherContainer = new GameObject(Path.GetFileName(entry.Name)).AddComponent<UmaContainer>();
        RecursiveLoadAsset(entry);
    }

    public void LoadLive(int id)
    {
        var asset = UmaViewerMain.Instance.AbList.FirstOrDefault(a => a.Name.EndsWith("cutt_son" + id));
        if (CurrentLiveContainer != null)
        {
            Destroy(CurrentLiveContainer.gameObject);
        }
        UnloadAllBundle();
        CurrentLiveContainer = new GameObject(Path.GetFileName(asset.Name)).AddComponent<UmaContainer>();
        RecursiveLoadAsset(asset);

    }

    public void LoadLiveSound(int songid, UmaDatabaseEntry SongAwb)
    {
        if (CurrentAudioSources.Count > 0)
        {
            var tmp = CurrentAudioSources[0];
            CurrentAudioSources.Clear();
            Destroy(tmp.gameObject);
            UI.ResetPlayer();
        }


        foreach (AudioClip clip in LoadAudio(SongAwb))
        {
            AddAudioSource(clip);
        }

        string nameVar = $"snd_bgm_live_{songid}_oke";
        UmaDatabaseEntry BGawb = Main.AbList.FirstOrDefault(a => a.Name.Contains(nameVar) && a.Name.EndsWith("awb"));
        if (BGawb != null)
        {
            var BGclip = LoadAudio(BGawb);
            if (BGclip.Count>0)
            {
                AddAudioSource(BGclip[0]);
            }
        }
    }

    private void AddAudioSource(AudioClip clip)
    {
        AudioSource source;
        if (CurrentAudioSources.Count > 0)
        {

            if (Mathf.Abs(CurrentAudioSources[0].clip.length - clip.length) > 3) return;
            source = CurrentAudioSources[0].gameObject.AddComponent<AudioSource>();
        }
        else
        {
            source = new GameObject("SoundController").AddComponent<AudioSource>();
        }
        CurrentAudioSources.Add(source);
        source.clip = clip;
        source.Play();
    }

    public List<AudioClip> LoadAudio(UmaDatabaseEntry awb)
    {
        List<AudioClip> clips = new List<AudioClip>();
        UmaViewerUI.Instance.LoadedAssetsAdd(awb);
        string awbPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low"}\\Cygames\\umamusume\\dat\\{awb.Url.Substring(0, 2)}\\{awb.Url}";
        if (!File.Exists(awbPath)) return clips;

        FileStream awbFile = File.OpenRead(awbPath);
        AwbReader awbReader = new AwbReader(awbFile);
        foreach (Wave wave in awbReader.Waves)
        {
            var stream = new UmaWaveStream(awbReader, wave.WaveId);
            MemoryStream outputStream = new MemoryStream();
            WaveFileWriter.WriteWavFileToStream(outputStream, stream);
            WAV wav = new WAV(outputStream.ToArray());
            AudioClip clip = AudioClip.Create(Path.GetFileName(awb.Name).Replace(".awb", wave.WaveId.ToString()), wav.SampleCount, wav.ChannelCount, wav.Frequency, false);
            clip.SetData(wav.ChannelCount > 1 ? wav.StereoChannel : wav.LeftChannel, 0);
            clips.Add(clip);
            outputStream.Dispose();
        }

        awbReader.Dispose();
        awbFile.Dispose();

        return clips;
    }

    private void RecursiveLoadAsset(UmaDatabaseEntry entry, bool IsSubAsset = false)
    {
        if (!string.IsNullOrEmpty(entry.Prerequisites))
        {
            foreach (string prerequisite in entry.Prerequisites.Split(';'))
            {
                RecursiveLoadAsset(Main.AbList.FirstOrDefault(ab => ab.Name == prerequisite), true);
            }
        }
        LoadAsset(entry, IsSubAsset);
    }

    public void LoadAsset(UmaDatabaseEntry entry, bool IsSubAsset = false)
    {
        Debug.Log("Loading " + entry.Name);
        if (Main.LoadedBundles.ContainsKey(entry.Name)) return;

        string filePath = $"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low"}\\Cygames\\umamusume\\dat\\{entry.Url.Substring(0, 2)}\\{entry.Url}";
        if (File.Exists(filePath))
        {
            AssetBundle bundle = AssetBundle.LoadFromFile(filePath);
            if (bundle == null)
            {
                Debug.Log(filePath + " exists and doesn't work");
                return;
            }
            Main.LoadedBundles.Add(entry.Name, bundle);
            UI.LoadedAssetsAdd(entry);
            LoadBundle(bundle, IsSubAsset);
        }
    }

    private void LoadBundle(AssetBundle bundle, bool IsSubAsset = false)
    {
        if (bundle.name == "shader.a")
        {
            if (Main.ShadersLoaded) return;
            else Main.ShadersLoaded = true;
        }

        foreach (string name in bundle.GetAllAssetNames())
        {
            object asset = bundle.LoadAsset(name);

            if (asset == null) { continue; }
            Debug.Log("Bundle:" + bundle.name + "/" + name + $" ({asset.GetType()})");
            Type aType = asset.GetType();
            if (aType == typeof(AnimationClip))
            {
                if (CurrentUMAContainer && CurrentUMAContainer.Body)
                {
                    AnimationClip bbb = asset as AnimationClip;
                    bbb.wrapMode = WrapMode.Loop;
                    CurrentUMAContainer.OverrideController["clip"] = bbb;
                    CurrentUMAContainer.UmaAnimator.Play("clip", 0, 0);

                }

                if (!CurrentLiveContainer)
                    UnloadBundle(bundle, false);
            }
            else if (aType == typeof(GameObject))
            {
                if (bundle.name.Contains("cloth"))
                {
                    if (bundle.name.Contains("chr"))
                    {
                        GameObject head = Instantiate(asset as GameObject, CurrentUMAContainer.Heads[0].transform);
                    }
                    else if (bundle.name.Contains("bdy"))
                    {
                        GameObject head = Instantiate(asset as GameObject, CurrentUMAContainer.Body.transform);
                    }
                    else if (bundle.name.Contains("tail"))
                    {
                        GameObject head = Instantiate(asset as GameObject, CurrentUMAContainer.Tail.transform);
                    }

                }
                else if (bundle.name.Contains("/head/"))
                {
                    LoadHead(asset as GameObject);
                }
                else if (bundle.name.Contains("/body/"))
                {
                    LoadBody(asset as GameObject);
                }
                else if (bundle.name.Contains("/tail/"))
                {
                    LoadTail(asset as GameObject);
                }
                else
                {
                    if (!IsSubAsset)
                    {
                        LoadProp(asset as GameObject);
                    }
                }
            }
            else if (aType == typeof(FaceDrivenKeyTarget))
            {
                //CurrentContainer.FaceDrivenKeyTargets.Add(Instantiate(asset as FaceDrivenKeyTarget));
            }
            else if (aType == typeof(Shader))
            {
                ShaderList.Add(asset as Shader);
            }
            else if (aType == typeof(Texture2D))
            {
                if (bundle.name.Contains("/mini/head"))
                {
                    CurrentUMAContainer.MiniHeadTextures.Add(asset as Texture2D);
                }
                else if (bundle.name.Contains("/tail/"))
                {
                    CurrentUMAContainer.TailTextures.Add(asset as Texture2D);
                }
                else if (bundle.name.Contains("bdy0"))
                {
                    CurrentUMAContainer.GenericBodyTextures.Add(asset as Texture2D);
                }
            }
        }
    }

    private void LoadBody(GameObject go)
    {
        CurrentUMAContainer.Body = Instantiate(go, CurrentUMAContainer.transform);
        CurrentUMAContainer.UmaAnimator = CurrentUMAContainer.Body.GetComponent<Animator>();
        CurrentUMAContainer.UmaAnimator.runtimeAnimatorController = CurrentUMAContainer.OverrideController = Instantiate(OverrideController);

        if (CurrentUMAContainer.IsGeneric)
        {
            List<Texture2D> textures = CurrentUMAContainer.GenericBodyTextures;
            string costumeIdShort = CurrentUMAContainer.VarCostumeIdShort,
                   costumeIdLong = CurrentUMAContainer.VarCostumeIdLong,
                   height = CurrentUMAContainer.VarHeight,
                   skin = CurrentUMAContainer.VarSkin,
                   socks = CurrentUMAContainer.VarSocks,
                   bust = CurrentUMAContainer.VarBust;

            foreach (Renderer r in CurrentUMAContainer.Body.GetComponentsInChildren<Renderer>())
            {
                foreach (Material m in r.sharedMaterials)
                {
                    string mainTex = "", toonMap = "", tripleMap = "", optionMap = "";
                    if (CurrentUMAContainer.IsMini)
                    {

                        m.SetTexture("_MainTex", textures[0]);
                    }
                    else
                    {
                        switch (costumeIdShort.Split('_')[0]) //costume ID
                        {
                            case "0001":
                                switch (r.sharedMaterials.ToList().IndexOf(m))
                                {
                                    case 0:
                                        mainTex = $"tex_bdy{costumeIdShort}_00_waku0_diff";
                                        toonMap = $"tex_bdy{costumeIdShort}_00_waku0_shad_c";
                                        tripleMap = $"tex_bdy{costumeIdShort}_00_waku0_base";
                                        optionMap = $"tex_bdy{costumeIdShort}_00_waku0_ctrl";
                                        break;
                                    case 1:
                                        mainTex = $"tex_bdy{costumeIdShort}_00_{skin}_{bust}_{socks.PadLeft(2, '0')}_diff";
                                        toonMap = $"tex_bdy{costumeIdShort}_00_{skin}_{bust}_{socks.PadLeft(2, '0')}_shad_c";
                                        tripleMap = $"tex_bdy{costumeIdShort}_00_0_{bust}_00_base";
                                        optionMap = $"tex_bdy{costumeIdShort}_00_0_{bust}_00_ctrl";
                                        break;
                                    case 2:
                                        mainTex = $"tex_bdy0001_00_num01";
                                        toonMap = $"tex_bdy0001_00_num01";
                                        tripleMap = $"tex_bdy0001_00_num01";
                                        optionMap = $"tex_bdy0001_00_num01";
                                        break;
                                }
                                break;
                            case "0003":
                                mainTex = $"tex_bdy{costumeIdShort}_00_{skin}_{bust}_diff";
                                toonMap = $"tex_bdy{costumeIdShort}_00_{skin}_{bust}_shad_c";
                                tripleMap = $"tex_bdy{costumeIdShort}_00_0_{bust}_base";
                                optionMap = $"tex_bdy{costumeIdShort}_00_0_{bust}_ctrl";
                                break;
                            case "0006":
                                mainTex = $"tex_bdy{costumeIdLong}_{skin}_{bust}_{"00"}_diff";
                                toonMap = $"tex_bdy{costumeIdLong}_{skin}_{bust}_{"00"}_shad_c";
                                tripleMap = $"tex_bdy{costumeIdLong}_0_{bust}_00_base";
                                optionMap = $"tex_bdy{costumeIdLong}_0_{bust}_00_ctrl";
                                break;
                            case "0009":
                                mainTex = $"tex_bdy{costumeIdLong}_{skin}_{bust}_{"00"}_diff";
                                toonMap = $"tex_bdy{costumeIdLong}_{skin}_{bust}_{"00"}_shad_c";
                                tripleMap = $"tex_bdy{costumeIdLong}_0_{bust}_00_base";
                                optionMap = $"tex_bdy{costumeIdLong}_0_{bust}_00_ctrl";
                                break;
                            default:
                                mainTex = $"tex_bdy{costumeIdLong}_{skin}_{bust}_diff";
                                toonMap = $"tex_bdy{costumeIdLong}_{skin}_{bust}_shad_c";
                                tripleMap = $"tex_bdy{costumeIdLong}_0_{bust}_base";
                                optionMap = $"tex_bdy{costumeIdLong}_0_{bust}_ctrl";
                                break;

                        }
                        Debug.Log("Looking for texture " + mainTex);
                        m.SetTexture("_MainTex", textures.FirstOrDefault(t => t.name == mainTex));
                        m.SetTexture("_ToonMap", textures.FirstOrDefault(t => t.name == toonMap));
                        m.SetTexture("_TripleMaskMap", textures.FirstOrDefault(t => t.name == tripleMap));
                        m.SetTexture("_OptionMaskMap", textures.FirstOrDefault(t => t.name == optionMap));
                    }

                }
            }
        }
    }

    private void LoadHead(GameObject go)
    {
        GameObject head = Instantiate(go, CurrentUMAContainer.transform);
        CurrentUMAContainer.Heads.Add(head);
        CurrentUMAContainer.HeadNeckBones.Add(UmaContainer.FindBoneInChildren(head.transform, "Neck"));
        CurrentUMAContainer.HeadHeadBones.Add(UmaContainer.FindBoneInChildren(head.transform, "Head"));

        foreach (Renderer r in head.GetComponentsInChildren<Renderer>())
        {
            foreach (Material m in r.sharedMaterials)
            {
                if (head.name.Contains("mchr"))
                {
                    if (r.name == "M_Face")
                    {
                        m.SetTexture("_MainTex", CurrentUMAContainer.MiniHeadTextures.First(t => t.name.Contains("face") && t.name.Contains("diff")));
                    }
                    if (r.name == "M_Cheek")
                    {
                        m.CopyPropertiesFromMaterial(TransMaterialCharas);
                        m.SetTexture("_MainTex", CurrentUMAContainer.MiniHeadTextures.First(t => t.name.Contains("cheek")));
                    }
                    if (r.name == "M_Mouth")
                    {
                        m.SetTexture("_MainTex", CurrentUMAContainer.MiniHeadTextures.First(t => t.name.Contains("mouth")));
                    }
                    if (r.name == "M_Eye")
                    {
                        m.SetTexture("_MainTex", CurrentUMAContainer.MiniHeadTextures.First(t => t.name.Contains("eye")));
                    }
                    if (r.name.StartsWith("M_Mayu_"))
                    {
                        m.SetTexture("_MainTex", CurrentUMAContainer.MiniHeadTextures.First(t => t.name.Contains("mayu")));
                    }
                }
                else
                {
                    switch (m.shader.name)
                    {
                        case "Gallop/3D/Chara/ToonFace/TSER":
                            m.SetFloat("_CylinderBlend", 0.5f);
                            break;
                        case "Gallop/3D/Chara/ToonEye/T":
                            m.SetFloat("_CylinderBlend", 0.5f);
                            break;
                        case "Gallop/3D/Chara/ToonHair/TSER":
                            m.SetFloat("_CylinderBlend", 0.5f);
                            break;

                        default:
                            Debug.Log(m.shader.name);
                            // m.shader = Shader.Find("Nars/UmaMusume/Body");
                            break;
                    }
                }
            }
        }

        //foreach (var anim in Main.AbList.Where(a => a.Name.StartsWith("3d/chara/head/chr0001_00/facial/")))
        //{
        //    RecursiveLoadAsset(anim);
        //}
    }

    private void LoadTail(GameObject gameObject)
    {
        Transform hHip = UmaContainer.FindBoneInChildren(CurrentUMAContainer.Body.transform, "Hip");
        if (hHip == null) return;
        CurrentUMAContainer.Tail = Instantiate(gameObject, hHip);
        var textures = CurrentUMAContainer.TailTextures;
        foreach (Renderer r in CurrentUMAContainer.Tail.GetComponentsInChildren<Renderer>())
        {
            foreach (Material m in r.sharedMaterials)
            {
                m.SetTexture("_MainTex", textures.FirstOrDefault(t => t.name.EndsWith("diff")));
                m.SetTexture("_ToonMap", textures.FirstOrDefault(t => t.name.Contains("shad")));
                m.SetTexture("_TripleMaskMap", textures.FirstOrDefault(t => t.name.Contains("base")));
                m.SetTexture("_OptionMaskMap", textures.FirstOrDefault(t => t.name.Contains("ctrl")));
            }
        }
    }

    private void LoadProp(GameObject go)
    {
        var prop = Instantiate(go, (go.name.Contains("Cutt_son") ? CurrentLiveContainer : CurrentOtherContainer).transform);
        foreach (Renderer r in prop.GetComponentsInChildren<Renderer>())
        {
            foreach (Material m in r.sharedMaterials)
            {
                //Shaders can be differentiated by checking m.shader.name
                m.shader = Shader.Find("Unlit/Transparent Cutout");
            }
        }
    }


    private void UnloadBundle(AssetBundle bundle, bool unloadAllObjects)
    {
        var entry = Main.LoadedBundles.FirstOrDefault(b => b.Value == bundle);
        if (entry.Key != null)
        {
            Main.LoadedBundles.Remove(entry.Key);
        }
        bundle.Unload(unloadAllObjects);
    }

    public void UnloadAllBundle(bool unloadAllObjects = false)
    {
        foreach (var bundle in Main.LoadedBundles)
        {
            bundle.Value.Unload(unloadAllObjects);
        }
        if (unloadAllObjects)
        {
            if (CurrentUMAContainer) Destroy(CurrentUMAContainer);
            if (CurrentLiveContainer) Destroy(CurrentLiveContainer);
            if (CurrentOtherContainer) Destroy(CurrentOtherContainer);
        }
        Main.LoadedBundles.Clear();
        UI.LoadedAssetsClear();
    }
}

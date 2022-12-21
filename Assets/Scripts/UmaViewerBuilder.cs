using CriWare;
using CriWareFormats;
using Gallop;
using NAudio.Wave;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using UmaMusumeAudio;
using UnityEngine;
using static UnityEngine.EventSystems.EventTrigger;
using static UnityEngine.Rendering.VirtualTexturing.Debugging;

public class UmaViewerBuilder : MonoBehaviour
{
    public static UmaViewerBuilder Instance;
    UmaViewerMain Main => UmaViewerMain.Instance;
    UmaViewerUI UI => UmaViewerUI.Instance;

    public bool LoadingInProgress;

    public List<AssetBundle> Loaded;
    public List<Shader> ShaderList = new List<Shader>();
    public Material TransMaterialCharas;
    public Material TransMaterialProps;
    public UmaContainer CurrentUMAContainer;
    public UmaContainer CurrentLiveContainer;
    public UmaContainer CurrentOtherContainer;

    public UmaHeadData CurrentHead;

    public Shader hairShader;
    public Shader faceShader;
    public Shader eyeShader;
    public Shader cheekShader;
    public Shader eyebrowShader;
    public Shader alphaShader;
    public Shader bodyAlphaShader;
    public Shader bodyBehindAlphaShader;

    public List<AudioSource> CurrentAudioSources = new List<AudioSource>();
    public List<UmaLyricsData> CurrentLyrics = new List<UmaLyricsData>();

    public AnimatorOverrideController OverrideController;
    public AnimatorOverrideController FaceOverrideController;
    public AnimatorOverrideController CameraOverrideController;
    public Animator AnimationCameraAnimator;
    public Camera AnimationCamera;

    private void Awake()
    {
        Instance = this;
    }
    private void FixedUpdate()
    {
        if (AnimationCamera && AnimationCamera.enabled == true)
        {
            var fov = AnimationCamera.gameObject.transform.parent.transform.localScale.x;
            AnimationCamera.fieldOfView = fov;
        }
    }

    public IEnumerator LoadUma(int id, string costumeId, bool mini)
    {
        LoadingInProgress = true;
        UnloadUma();
        CurrentUMAContainer = new GameObject($"Chara_{id}_{costumeId}").AddComponent<UmaContainer>().SetType(UmaContainer.ContainerType.Uma);
        CurrentUMAContainer.CharaData = UmaDatabaseController.ReadCharaData(id);
        if (mini)
        {
            yield return LoadMiniUma(id, costumeId);
        }
        else
        {
            yield return LoadNormalUma(id, costumeId);
        }
        LoadingInProgress = false;
        Main.Downloaded = 0;
        Main.DownloadingBundles.Clear();
        yield break;
    }

    private IEnumerator LoadNormalUma(int id, string costumeId)
    {
        DataRow charaData = CurrentUMAContainer.CharaData;
        bool genericCostume = CurrentUMAContainer.IsGeneric = costumeId.Length >= 4;
        string skin = charaData["skin"].ToString(),
               height = charaData["height"].ToString(),
               socks = charaData["socks"].ToString(),
               bust = charaData["bust"].ToString(),
               sex = charaData["sex"].ToString(),
               shape = charaData["shape"].ToString(),
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

            // Pattern for generic body type is as follows:
            //
            // (costume id)_(body_type_sub)_(body_setting)_(height)_(shape)_(bust)
            //
            // body_type_sub is used for variants like the summer/winter uniform or the swimsuit/towel
            // body_setting is used for subvariants of each variant like the big belly version of the uniform, and the genders for the tracksuits
            //
            // Some models will naturally be missing due to how this system is designed.

            string body = "";
            body = UmaDatabaseController.BodyPath + $"bdy{costumeIdShort}/pfb_bdy{costumeId}_{height}_{shape}_{bust}";

            Debug.Log("Looking for " + body);
            asset = UmaViewerMain.Instance.AbChara.FirstOrDefault(a => a.Name == body);
        }
        else asset = UmaViewerMain.Instance.AbChara.FirstOrDefault(a => a.Name == UmaDatabaseController.BodyPath + $"bdy{id}_{costumeId}/pfb_bdy{id}_{costumeId}");

        if (asset == null)
        {
            Debug.LogError("No body, can't load!");
            yield break;
        }
        if (genericCostume)
        {
            string texPattern1 = "", texPattern2 = "", texPattern3 = "", texPattern4 = "", texPattern5 = "";
            switch (costumeId.Split('_')[0])
            {
                case "0001":
                    texPattern1 = $"tex_bdy{costumeIdShort}_00_{skin}_{bust}_0{socks}";
                    texPattern2 = $"tex_bdy{costumeIdShort}_00_0_{bust}";
                    texPattern3 = $"tex_bdy{costumeIdShort}_zekken";
                    texPattern4 = $"tex_bdy{costumeIdShort}_00_waku";
                    texPattern5 = $"tex_bdy{costumeIdShort}_num";
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
            foreach (var asset1 in UmaViewerMain.Instance.AbChara.Where(a => a.Name.StartsWith(UmaDatabaseController.BodyPath)
                && (a.Name.Contains(texPattern1)
                || a.Name.Contains(texPattern2)
                || (string.IsNullOrEmpty(texPattern3) ? false : a.Name.Contains(texPattern3))
                || (string.IsNullOrEmpty(texPattern4) ? false : a.Name.Contains(texPattern4))
                || (string.IsNullOrEmpty(texPattern5) ? false : a.Name.Contains(texPattern5)))))
            {
                yield return asset1.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => CurrentUMAContainer.LoadTextures(bundle));
            }
            //Load Body
            yield return asset.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => CurrentUMAContainer.LoadBody(bundle));

            //Load Physics
            foreach (var asset1 in UmaViewerMain.Instance.AbChara.Where(a => a.Name.StartsWith(UmaDatabaseController.BodyPath + $"bdy{costumeIdShort}/clothes")))
            {
                if (asset1.Name.Contains("cloth00") && asset1.Name.Contains("bust" + bust))
                    yield return asset1.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => CurrentUMAContainer.LoadPhysics(bundle));
            }
        }
        else
        {
            yield return asset.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => CurrentUMAContainer.LoadBody(bundle));

            //Load Physics
            foreach (var asset1 in UmaViewerMain.Instance.AbChara.Where(a => a.Name.StartsWith(UmaDatabaseController.BodyPath + $"bdy{id}_{costumeId}/clothes")))
            {
                if (asset1.Name.Contains("cloth00"))
                    yield return asset1.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => CurrentUMAContainer.LoadPhysics(bundle));
            }
        }

        // Record Head Data
        int head_id;
        string head_costumeId;
        int tailId = Convert.ToInt32(charaData["tail_model_id"]);

        //The tailId of 9006 is 1, but the character has no tail
        if (id == 9006) tailId = -1;

        if (UI.isHeadFix && CurrentHead != null)
        {
            head_id = CurrentHead.id;
            head_costumeId = CurrentHead.costumeId;
            tailId = CurrentHead.tailId;
        }
        else
        {
            head_id = id;
            head_costumeId = costumeId;

            CurrentHead = new UmaHeadData
            {
                id = id,
                costumeId = costumeId,
                tailId = tailId
            };
        }

        string head = UmaDatabaseController.HeadPath + $"chr{head_id}_{head_costumeId}/pfb_chr{head_id}_{head_costumeId}";
        asset = UmaViewerMain.Instance.AbChara.FirstOrDefault(a => a.Name == head);
        bool isDefaultHead = false;
        //Some costumes don't have custom heads
        if (head_costumeId != "00" && asset == null)
        {
            asset = UmaViewerMain.Instance.AbChara.FirstOrDefault(a => a.Name == UmaDatabaseController.HeadPath + $"chr{head_id}_00/pfb_chr{head_id}_00");
            isDefaultHead = true;
        }

        if (asset != null)
        {
            //Load Hair Textures
            foreach (var asset1 in UmaViewerMain.Instance.AbChara.Where(a => a.Name.StartsWith($"{UmaDatabaseController.HeadPath}chr{head_id}_{head_costumeId}/textures")))
            {
                yield return asset1.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => CurrentUMAContainer.LoadTextures(bundle));
            }
            //Load Head
            yield return asset.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => CurrentUMAContainer.LoadHead(bundle));

            //Load Physics
            if (isDefaultHead)
            {
                foreach (var asset1 in UmaViewerMain.Instance.AbChara.Where(a => a.Name.StartsWith($"{UmaDatabaseController.HeadPath}chr{head_id}_00/clothes")))
                {
                    if (asset1.Name.Contains("cloth00"))
                        yield return asset1.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => CurrentUMAContainer.LoadPhysics(bundle));
                }
            }
            else
            {
                foreach (var asset1 in UmaViewerMain.Instance.AbChara.Where(a => a.Name.StartsWith($"{UmaDatabaseController.HeadPath}chr{head_id}_{head_costumeId}/clothes")))
                {
                    if (asset1.Name.Contains("cloth00"))
                        yield return asset1.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => CurrentUMAContainer.LoadPhysics(bundle));
                }
            }
        }

        if (tailId != 0)
        {
            string tailName = $"tail{tailId.ToString().PadLeft(4, '0')}_00";
            string tailPath = $"3d/chara/tail/{tailName}/";
            string tailPfb = tailPath + $"pfb_{tailName}";
            asset = UmaViewerMain.Instance.AbChara.FirstOrDefault(a => a.Name == tailPfb);
            if (asset != null)
            {
                foreach (var asset1 in UmaViewerMain.Instance.AbChara.Where(a => a.Name.StartsWith($"{tailPath}textures/tex_{tailName}_{head_id}") || a.Name.StartsWith($"{tailPath}textures/tex_{tailName}_0000")))
                {
                    yield return asset1.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => CurrentUMAContainer.LoadTextures(bundle));
                }
                yield return asset.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => CurrentUMAContainer.LoadTail(bundle));


                //Load Physics
                foreach (var asset1 in UmaViewerMain.Instance.AbChara.Where(a => a.Name.StartsWith($"{tailPath}clothes")))
                {
                    if (asset1.Name.Contains("cloth00"))
                        yield return asset1.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => CurrentUMAContainer.LoadPhysics(bundle));
                }
            }
            else
            {
                Debug.Log("no tail");
            }
        }

        CurrentUMAContainer.LoadPhysics(); //Need to load physics before loading FacialMorph

        //Load FacialMorph
        if (CurrentUMAContainer.Head)
        {
            var locatorEntry = Main.AbList.FirstOrDefault(a => a.Name.EndsWith("3d/animator/drivenkeylocator"));
            var filePath = UmaDatabaseController.GetABPath(locatorEntry);
            AssetBundle bundle = null;
            yield return locatorEntry.LoadAssetBundle(CurrentUMAContainer.gameObject, bndl => bundle = bndl);
            var locator = Instantiate(bundle.LoadAsset("DrivenKeyLocator"), CurrentUMAContainer.transform) as GameObject;
            locator.name = "DrivenKeyLocator";

            var headBone = (GameObject)CurrentUMAContainer.Head.GetComponent<AssetHolder>()._assetTable["head"];
            var eyeLocator_L = headBone.transform.Find("Eye_target_locator_L");
            var eyeLocator_R = headBone.transform.Find("Eye_target_locator_R");

            var mangaEntry = Main.AbEffect.FindAll(a => a.Name.StartsWith("3d/effect/charaemotion/pfb_eff_chr_emo_eye"));
            var mangaObjects = new List<GameObject>();
            foreach(var entry in mangaEntry)
            {
                AssetBundle ab = null;
                yield return entry.LoadAssetBundle(CurrentUMAContainer.gameObject, bndl => ab = bndl);
                var obj = ab.LoadAsset(Path.GetFileNameWithoutExtension(entry.Name)) as GameObject;
                obj.SetActive(false);

                var leftObj = Instantiate(obj, eyeLocator_L.transform);
                new List<Renderer>(leftObj.GetComponentsInChildren<Renderer>()).ForEach(a => a.material.renderQueue = -1);
                CurrentUMAContainer.LeftMangaObject.Add(leftObj);

                var RightObj = Instantiate(obj, eyeLocator_R.transform);
                if (RightObj.TryGetComponent<AssetHolder>(out var holder))
                {
                    if (holder._assetTableValue["invert"] > 0)
                        RightObj.transform.localScale = new Vector3(-1, 1, 1);
                }
                new List<Renderer>(RightObj.GetComponentsInChildren<Renderer>()).ForEach(a => { a.material.renderQueue = -1; });
                CurrentUMAContainer.RightMangaObject.Add(RightObj);
            }

            var tearEntry = Main.AbChara.FindAll(a => a.Name.StartsWith("3d/chara/common/tear/") && a.Name.Contains("pfb_chr_tear"));
            if (tearEntry.Count > 0)
            {
                foreach(var tear in tearEntry)
                {
                    yield return tear.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => CurrentUMAContainer.LoadTear(bundle));
                }
            }
            if (CurrentUMAContainer.TearPrefab_0 && CurrentUMAContainer.TearPrefab_1)
            {
                var p0 = CurrentUMAContainer.TearPrefab_0;
                var p1 = CurrentUMAContainer.TearPrefab_1;
                var t = headBone.transform;
                CurrentUMAContainer.TearControllers.Add(new TearController(t, Instantiate(p0, t), Instantiate(p1, t), 0, 1));
                CurrentUMAContainer.TearControllers.Add(new TearController(t, Instantiate(p0, t), Instantiate(p1, t), 1, 1));
                CurrentUMAContainer.TearControllers.Add(new TearController(t, Instantiate(p0, t), Instantiate(p1, t), 0, 0));
                CurrentUMAContainer.TearControllers.Add(new TearController(t, Instantiate(p0, t), Instantiate(p1, t), 1, 0));
            }

            var firsehead = CurrentUMAContainer.Head;
            var faceDriven = firsehead.GetComponent<AssetHolder>()._assetTable["facial_target"] as FaceDrivenKeyTarget;
            var earDriven = firsehead.GetComponent<AssetHolder>()._assetTable["ear_target"] as DrivenKeyTarget;
            faceDriven._earTarget = earDriven._targetFaces;
            CurrentUMAContainer.FaceDrivenKeyTarget = faceDriven;
            CurrentUMAContainer.FaceDrivenKeyTarget.Container = CurrentUMAContainer;
            faceDriven.DrivenKeyLocator = locator.transform;
            faceDriven.Initialize(firsehead.GetComponentsInChildren<Transform>().ToList());

            var emotionDriven = ScriptableObject.CreateInstance<FaceEmotionKeyTarget>();
            emotionDriven.name = $"char{id}_{costumeId}_emotion_target";
            CurrentUMAContainer.FaceEmotionKeyTarget = emotionDriven;
            emotionDriven.FaceDrivenKeyTarget = faceDriven;
            emotionDriven.FaceEmotionKey = UmaDatabaseController.Instance.FaceTypeData.ToList();
            emotionDriven.Initialize();
        }

        CurrentUMAContainer.TearControllers.ForEach(a => a.SetDir(a.CurrentDir));
        CurrentUMAContainer.HeadBone = (GameObject)CurrentUMAContainer.Body.GetComponent<AssetHolder>()._assetTable["head"];
        CurrentUMAContainer.EyeHeight = CurrentUMAContainer.Head.GetComponent<AssetHolder>()._assetTableValue["head_center_offset_y"];
        CurrentUMAContainer.MergeModel();
        CurrentUMAContainer.Initialize();

        CurrentUMAContainer.SetHeight(Convert.ToInt32(CurrentUMAContainer.CharaData["scale"]));
        yield return UmaViewerMain.Instance.AbMotions.FirstOrDefault(a => a.Name.EndsWith($"anm_eve_chr{id}_00_idle01_loop"))?.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => LoadAnimation(bundle));
    }

    private IEnumerator LoadMiniUma(int id, string costumeId)
    {
        DataRow charaData = CurrentUMAContainer.CharaData;
        CurrentUMAContainer.IsMini = true;
        bool isGeneric = CurrentUMAContainer.IsGeneric = costumeId.Length >= 4;
        string skin = charaData["skin"].ToString(),
               height = charaData["height"].ToString(),
               socks = charaData["socks"].ToString(),
               bust = charaData["bust"].ToString(),
               sex = charaData["sex"].ToString(),
               costumeIdShort = "";
        bool customHead = true;

        UmaDatabaseEntry asset = null;
        if (isGeneric)
        {
            costumeIdShort = costumeId.Remove(costumeId.LastIndexOf('_'));
            string body = $"3d/chara/mini/body/mbdy{costumeIdShort}/pfb_mbdy{costumeId}_0";
            asset = UmaViewerMain.Instance.AbChara.FirstOrDefault(a => a.Name == body);
        }
        else asset = UmaViewerMain.Instance.AbChara.FirstOrDefault(a => a.Name == $"3d/chara/mini/body/mbdy{id}_{costumeId}/pfb_mbdy{id}_{costumeId}");
        if (asset == null)
        {
            Debug.LogError("No body, can't load!");
            yield break;
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
            foreach (var asset1 in UmaViewerMain.Instance.AbChara.Where(a => a.Name.StartsWith("3d/chara/mini/body/") && a.Name.Contains(texPattern1)))
            {
                yield return asset1.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => CurrentUMAContainer.LoadTextures(bundle));
            }
            //Load Body
            yield return asset.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => CurrentUMAContainer.LoadBody(bundle));
        }
        else
            yield return asset.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => CurrentUMAContainer.LoadBody(bundle));

        string hair = $"3d/chara/mini/head/mchr{id}_{costumeId}/pfb_mchr{id}_{costumeId}_hair";
        asset = UmaViewerMain.Instance.AbChara.FirstOrDefault(a => a.Name == hair);
        if (costumeId != "00" && asset == null)
        {
            customHead = false;
            asset = UmaViewerMain.Instance.AbChara.FirstOrDefault(a => a.Name == $"3d/chara/mini/head/mchr{id}_00/pfb_mchr{id}_00_hair");
        }
        if (asset != null)
        {
            //Load Hair Textures
            if (customHead)
            {
                foreach (var asset1 in UmaViewerMain.Instance.AbChara.Where(a => a.Name.StartsWith($"3d/chara/mini/head/mchr{id}_{costumeId}/textures")))
                {
                    yield return asset1.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => CurrentUMAContainer.LoadTextures(bundle));
                }
            }
            else
            {
                foreach (var asset1 in UmaViewerMain.Instance.AbChara.Where(a => a.Name.StartsWith($"3d/chara/mini/head/mchr{id}_00/textures")))
                {
                    yield return asset1.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => CurrentUMAContainer.LoadTextures(bundle));
                }
            }

            //Load Hair
            yield return asset.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => CurrentUMAContainer.LoadHead(bundle));
        }

        string head = $"3d/chara/mini/head/mchr0001_00/pfb_mchr0001_00_face0";
        asset = UmaViewerMain.Instance.AbChara.FirstOrDefault(a => a.Name == head);
        if (asset != null)
        {
            //Load Head Textures
            foreach (var asset1 in UmaViewerMain.Instance.AbChara.Where(a => a.Name.StartsWith($"3d/chara/mini/head/mchr0001_00/textures/tex_mchr0001_00_face0_{skin}")))
            {
                yield return asset1.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => CurrentUMAContainer.LoadTextures(bundle));
            }
            //Load Head
            yield return asset.LoadAssetBundle(CurrentUMAContainer.gameObject, bundle => CurrentUMAContainer.LoadHead(bundle));
        }

        CurrentUMAContainer.MergeModel();
    }

    public IEnumerator LoadAnimation(AssetBundle bundle)
    {
        LoadingInProgress = true;
        foreach (string name in bundle.GetAllAssetNames())
        {
            object asset = bundle.LoadAsset(name);

            if (asset == null || asset.GetType() != typeof(AnimationClip)) { continue; }
            var aClip = (AnimationClip)asset;
            if (CurrentUMAContainer && CurrentUMAContainer.UmaAnimator && CurrentUMAContainer.UmaAnimator.runtimeAnimatorController)
            {
                yield return CurrentUMAContainer.LoadAnimation(aClip);
            }
        }
        LoadingInProgress = false;
        Main.Downloaded = 0;
        Main.DownloadingBundles.Clear();
    }

    public IEnumerator LoadProp(UmaDatabaseEntry entry)
    {
        LoadingInProgress = true;
        UnloadProp();
        CurrentOtherContainer = new GameObject(Path.GetFileName(entry.Name)).AddComponent<UmaContainer>().SetType(UmaContainer.ContainerType.Prop);
        yield return entry.LoadAssetBundle(CurrentOtherContainer.gameObject, bundle => LoadProp(bundle));
        LoadingInProgress = false;
        Main.Downloaded = 0;
        Main.DownloadingBundles.Clear();
    }

    public IEnumerator LoadLive(LiveEntry live)
    {
        LoadingInProgress = true;
        var asset = UmaViewerMain.Instance.AbList.FirstOrDefault(a => a.Name.EndsWith("cutt_son" + live.MusicId));
        var BGasset = UmaViewerMain.Instance.AbList.FirstOrDefault(a => a.Name.EndsWith($"pfb_env_live{live.BackGroundId}_controller000"));
        if (asset == null || BGasset == null) yield break;
        UnloadLive();
        CurrentLiveContainer = new GameObject(Path.GetFileName(asset.Name)).AddComponent<UmaContainer>().SetType(UmaContainer.ContainerType.Live);
        yield return asset.LoadAssetBundle(CurrentLiveContainer.gameObject, bundle => LoadProp(bundle));
        yield return BGasset.LoadAssetBundle(CurrentLiveContainer.gameObject, bundle => LoadProp(bundle));
        LoadingInProgress = false;
        Main.Downloaded = 0;
        Main.DownloadingBundles.Clear();
    }

    //Use CriWare Library
    public void LoadLiveSoundCri(int songid, UmaDatabaseEntry SongAwb)
    {
        //清理
        if (CurrentAudioSources.Count > 0)
        {
            var tmp = CurrentAudioSources[0];
            CurrentAudioSources.Clear();
            Destroy(tmp.gameObject);
            UI.ResetAudioPlayer();
        }

        //获取Acb文件和Awb文件的路径
        string nameVar = SongAwb.Name.Split('.')[0].Split('/').Last();

        //使用Live的Bgm
        nameVar = $"snd_bgm_live_{songid}_oke";

        LoadSound Loader = (LoadSound)ScriptableObject.CreateInstance("LoadSound");
        LoadSound.UmaSoundInfo soundInfo = Loader.getSoundPath(nameVar);

        //音频组件添加路径，载入音频
        CriAtom.AddCueSheet(nameVar, soundInfo.acbPath, soundInfo.awbPath);

        //获得当前音频信息
        CriAtomEx.CueInfo[] cueInfoList;
        List<string> cueNameList = new List<string>();
        cueInfoList = CriAtom.GetAcb(nameVar).GetCueInfoList();
        foreach (CriAtomEx.CueInfo cueInfo in cueInfoList)
        {
            cueNameList.Add(cueInfo.name);
        }

        //创建播放器
        CriAtomSource source = new GameObject("CuteAudioSource").AddComponent<CriAtomSource>();
        source.transform.SetParent(GameObject.Find("AudioManager/AudioControllerBgm").transform);
        source.cueSheet = nameVar;

        //播放
        source.Play(cueNameList[0]);
    }

    //Use decrypt function
    public void LoadLiveSound(int songid, UmaDatabaseEntry SongAwb)
    {
        if (CurrentAudioSources.Count > 0)
        {
            var tmp = CurrentAudioSources[0];
            CurrentAudioSources.Clear();
            Destroy(tmp.gameObject);
            UI.ResetAudioPlayer();
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
            if (BGclip.Count > 0)
            {
                AddAudioSource(BGclip[0]);
            }
        }

        StartCoroutine(LoadLiveLyrics(songid));
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
        string awbPath = UmaDatabaseController.GetABPath(awb); ;
        if (!File.Exists(awbPath)) return clips;

        FileStream awbFile = File.OpenRead(awbPath);
        AwbReader awbReader = new AwbReader(awbFile);

        foreach (Wave wave in awbReader.Waves)
        {
            var stream = new UmaWaveStream(awbReader, wave.WaveId);
            var sampleProvider = stream.ToSampleProvider();

            int channels = stream.WaveFormat.Channels;
            int bytesPerSample = stream.WaveFormat.BitsPerSample / 8;
            int sampleRate = stream.WaveFormat.SampleRate;

            AudioClip clip = AudioClip.Create(
                Path.GetFileNameWithoutExtension(awb.Name) + "_" + wave.WaveId.ToString(),
                (int)(stream.Length / channels / bytesPerSample),
                channels,
                sampleRate,
                true,
                data => sampleProvider.Read(data, 0, data.Length),
                position => stream.Position = position * channels * bytesPerSample);

            clips.Add(clip);
        }

        return clips;
    }

    public IEnumerator LoadLiveLyrics(int songid)
    {
        if (CurrentLyrics.Count > 0) CurrentLyrics.Clear();

        string lyricsVar = $"live/musicscores/m{songid}/m{songid}_lyrics";
        UmaDatabaseEntry lyricsAsset = Main.AbList.FirstOrDefault(a => a.Name.Contains(lyricsVar));
        if (lyricsAsset != null)
        {
            AssetBundle bundle = null;
            yield return lyricsAsset.LoadAssetBundle(Main.gameObject, bundle1 =>
            {
                bundle = bundle1;
            });
            TextAsset asset = bundle.LoadAsset<TextAsset>(Path.GetFileNameWithoutExtension(lyricsVar));
            string[] lines = asset.text.Split("\n"[0]);

            for (int i = 1; i < lines.Length; i++)
            {
                string[] words = lines[i].Split(',');
                if (words.Length > 0)
                {
                    try
                    {
                        UmaLyricsData lyricsData = new UmaLyricsData()
                        {
                            time = float.Parse(words[0]) / 1000,
                            text = (words.Length > 1) ? words[1].Replace("[COMMA]", "，") : ""
                        };
                        CurrentLyrics.Add(lyricsData);
                    }
                    catch { }
                }
            }
        }
    }

    public void LoadShaders(AssetBundle bundle)
    {
        hairShader = (Shader)bundle.LoadAsset("assets/_gallop/resources/shader/3d/character/charactertoonhairtser.shader");
        faceShader = (Shader)bundle.LoadAsset("assets/_gallop/resources/shader/3d/character/charactertoonfacetser.shader");
        eyeShader = (Shader)bundle.LoadAsset("assets/_gallop/resources/shader/3d/character/charactertooneyet.shader");
        cheekShader = (Shader)bundle.LoadAsset("assets/_gallop/resources/shader/3d/character/charactermultiplycheek.shader");
        eyebrowShader = (Shader)bundle.LoadAsset("assets/_gallop/resources/shader/3d/character/charactertoonmayu.shader");
        alphaShader = (Shader)bundle.LoadAsset("assets/_gallop/resources/shader/3d/character/characteralphanolinetoonhairtser.shader");
        bodyAlphaShader = (Shader)bundle.LoadAsset("assets/_gallop/resources/shader/3d/character/characteralphanolinetoontser.shader");
        bodyBehindAlphaShader = (Shader)bundle.LoadAsset("assets/_gallop/resources/shader/3d/character/characteralphanolinetoonbehindtser.shader");
    }

    private void LoadProp(AssetBundle bundle)
    {
        foreach (string name in bundle.GetAllAssetNames())
        {
            object asset = bundle.LoadAsset(name);

            if (asset == null || asset.GetType() != typeof(GameObject)) { continue; }
            var go = asset as GameObject;
            var container = (go.name.Contains("Cutt_son") || go.name.Contains("pfb_env_live")) ? CurrentLiveContainer : CurrentOtherContainer;
            var prop = Instantiate(go, container.transform);
            foreach (Renderer r in prop.GetComponentsInChildren<Renderer>())
            {
                foreach (Material m in r.sharedMaterials)
                {
                    //Shaders can be differentiated by checking m.shader.name
                    m.shader = Shader.Find("Unlit/Transparent Cutout");
                }
            }
        }
    }

    public void SetPreviewCamera(AnimationClip clip)
    {
        if (clip)
        {
            if (!AnimationCameraAnimator.runtimeAnimatorController)
            {
                AnimationCameraAnimator.runtimeAnimatorController = Instantiate(CameraOverrideController);
            }
            (AnimationCameraAnimator.runtimeAnimatorController as AnimatorOverrideController)["clip_1"] = clip;
            AnimationCamera.enabled = true;
            AnimationCameraAnimator.Play("motion_1", 0, 0);
        }
        else
        {
            AnimationCamera.enabled = false;
        }
    }

    public Sprite LoadCharaIcon(string id)
    {
        string value = $"chara/chr{id}/chr_icon_{id}";
        var entry = UmaViewerMain.Instance.AbList.FirstOrDefault(a => a.Name.Equals(value));
        string path = UmaDatabaseController.GetABPath(entry);
        if (File.Exists(path))
        {
            AssetBundle assetBundle = AssetBundle.LoadFromFile(path);
            if (assetBundle.Contains($"chr_icon_{id}"))
            {
                Texture2D texture = (Texture2D)assetBundle.LoadAsset($"chr_icon_{id}");
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.zero);
                return sprite;
            }
        }
        return null;
    }

    public void ClearMorphs()
    {
        if (CurrentUMAContainer != null && CurrentUMAContainer.FaceDrivenKeyTarget != null)
        {
            foreach (var container in UI.EmotionList.GetComponentsInChildren<UmaUIContainer>())
            {
                if (container.Slider != null)
                    container.Slider.value = 0;
            }
            foreach (var container in UI.FacialList.GetComponentsInChildren<UmaUIContainer>())
            {
                if (container.Slider != null)
                    container.Slider.SetValueWithoutNotify(0);
            }
            if (CurrentUMAContainer.FaceDrivenKeyTarget)
            {
                CurrentUMAContainer.FaceDrivenKeyTarget.ClearMorph();
                CurrentUMAContainer.FaceDrivenKeyTarget.ChangeMorph();
            }
        }
    }

    public void UnloadProp()
    {
        if (CurrentOtherContainer != null)
        {
            DestroyImmediate(CurrentOtherContainer.gameObject);
        }
    }

    public void UnloadLive()
    {
        if (CurrentLiveContainer != null)
        {
            DestroyImmediate(CurrentLiveContainer.gameObject);
        }
    }

    public void UnloadUma()
    {
        if (CurrentUMAContainer != null)
        {
            DestroyImmediate(CurrentUMAContainer.gameObject);
        }
    }

    public void InterruptLoading()
    {
        if (LoadingInProgress)
        {
            if(CurrentUMAContainer != null)
            {
                CurrentUMAContainer.StopAllCoroutines();
                UnloadUma();
            }
            if (CurrentOtherContainer != null)
            {
                CurrentOtherContainer.StopAllCoroutines();
                UnloadProp();
            }
            if (CurrentLiveContainer != null)
            {
                CurrentLiveContainer.StopAllCoroutines();
                UnloadLive();
            }
            LoadingInProgress = false;
        }
    }
}

using Gallop;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using UnityEngine;
using static UnityEngine.Rendering.VirtualTexturing.Debugging;

public class UmaContainer : MonoBehaviour
{
    public ContainerType Type;
    public DataRow CharaData;
    public GameObject Body;
    public GameObject Tail;
    public GameObject Head;
    public GameObject PhysicsController;

    public List<Texture2D> TailTextures = new List<Texture2D>();

    [Header("Animator")]
    public Animator UmaAnimator;
    public AnimatorOverrideController OverrideController;
    public Animator UmaFaceAnimator;
    public AnimatorOverrideController FaceOverrideController;
    public bool isAnimatorControl;

    [Header("Body")]
    public GameObject UpBodyBone;
    public Vector3 UpBodyPosition;
    public Quaternion UpBodyRotation;

    [Header("Face")]
    public FaceDrivenKeyTarget FaceDrivenKeyTarget;
    public FaceEmotionKeyTarget FaceEmotionKeyTarget;
    public GameObject HeadBone;
    public GameObject TrackTarget;
    public float EyeHeight;
    public bool EnableEyeTracking = true;
    public Material FaceMaterial;

    [Header("Cheek")]
    public Texture CheekTex_0;
    public Texture CheekTex_1;
    public Texture CheekTex_2;

    [Header("Manga")]
    public List<GameObject> LeftMangaObject = new List<GameObject>();
    public List<GameObject> RightMangaObject = new List<GameObject>();

    [Header("Tear")]
    public GameObject TearPrefab_0;
    public GameObject TearPrefab_1;
    public List<TearController> TearControllers = new List<TearController>();

    [Header("Generic")]
    public bool IsGeneric = false;
    public string VarCostumeIdShort, VarCostumeIdLong, VarSkin, VarHeight, VarSocks, VarBust;
    public List<Texture2D> GenericBodyTextures = new List<Texture2D>();

    [Header("Mini")]
    public bool IsMini = false;
    public List<Texture2D> MiniHeadTextures = new List<Texture2D>();

    [Header("Physics")]
    public bool EnablePhysics = true;
    public List<CySpringDataContainer> cySpringDataContainers;

    private UmaViewerBuilder Builder => UmaViewerBuilder.Instance;
    private UmaViewerUI UI => UmaViewerUI.Instance;
    private UmaViewerMain Main => UmaViewerMain.Instance;

    public UmaContainer SetType(ContainerType type)
    {
        Type = type;
        return this;
    }

    public void Initialize()
    {
        TrackTarget = Camera.main.gameObject;
        UpBodyPosition = UpBodyBone.transform.localPosition;
        UpBodyRotation = UpBodyBone.transform.localRotation;

        //Models must be merged before handling extra morphs
        if (FaceDrivenKeyTarget)
            FaceDrivenKeyTarget.ChangeMorphWeight(FaceDrivenKeyTarget.MouthMorphs[3], 1);
    }

    private void FixedUpdate()
    {
        if (!IsMini)
        {
            if (TrackTarget && EnableEyeTracking && !isAnimatorControl)
            {
                var targetPosotion = TrackTarget.transform.position - HeadBone.transform.up * EyeHeight;
                var deltaPos = HeadBone.transform.InverseTransformPoint(targetPosotion);
                var deltaRotation = Quaternion.LookRotation(deltaPos.normalized, HeadBone.transform.up).eulerAngles;
                if (deltaRotation.x > 180) deltaRotation.x -= 360;
                if (deltaRotation.y > 180) deltaRotation.y -= 360;

                var finalRotation = new Vector2(Mathf.Clamp(deltaRotation.y / 35, -1, 1), Mathf.Clamp(-deltaRotation.x / 25, -1, 1));//Limited to the angle of view 
                FaceDrivenKeyTarget.SetEyeRange(finalRotation.x, finalRotation.y, finalRotation.x, -finalRotation.y);
            }

            if (isAnimatorControl)
            {
                FaceDrivenKeyTarget.ProcessLocator();
            }

            if (FaceMaterial)
            {
                if (isAnimatorControl)
                {
                    FaceMaterial.SetVector("_FaceForward", Vector3.zero);
                    FaceMaterial.SetVector("_FaceUp", Vector3.zero);
                    FaceMaterial.SetVector("_FaceCenterPos", Vector3.zero);

                }
                else
                {
                    //Used to calculate facial shadows
                    FaceMaterial.SetVector("_FaceForward", HeadBone.transform.forward);
                    FaceMaterial.SetVector("_FaceUp", HeadBone.transform.up);
                    FaceMaterial.SetVector("_FaceCenterPos", HeadBone.transform.position);
                }
                FaceMaterial.SetFloat("_faceShadowEndY", HeadBone.transform.position.y);
            }

            TearControllers.ForEach(a => a.UpdateOffset());
        }
    }

    public IEnumerator LoadAnimation(AnimationClip clip)
    {
        if (clip.name.EndsWith("_S"))
        {
            this.UpBodyReset();
            this.OverrideController["clip_s"] = clip;
        }
        else if (clip.name.EndsWith("_E"))
        {
            this.UpBodyReset();
            this.OverrideController["clip_e"] = clip;
        }
        else if (clip.name.EndsWith("_loop"))
        {
            this.UpBodyReset();
            UmaDatabaseEntry motion_e = null, motion_s = null;
            if (clip.name.EndsWith("_loop"))
            {
                motion_s = Main.AbMotions.FirstOrDefault(a => a.Name.EndsWith(clip.name.Replace("_loop", "_s")));
            }

            if (this.OverrideController["clip_2"].name.EndsWith("_loop"))
            {
                motion_e = Main.AbMotions.FirstOrDefault(a => a.Name.EndsWith(this.OverrideController["clip_2"].name.Replace("_loop", "_e")));
            }

            if (this.isAnimatorControl && this.FaceDrivenKeyTarget)
            {
                this.FaceDrivenKeyTarget.ResetLocator();
            }

            bool needTransit = false;
            needTransit = (motion_s != null && motion_e != null);
            if (needTransit)
            {
                yield return motion_e.LoadAssetBundle(this.gameObject);
                yield return motion_s.LoadAssetBundle(this.gameObject);
            }

            Builder.SetPreviewCamera(null);
            var lastTime = this.UmaAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            this.OverrideController["clip_1"] = this.OverrideController["clip_2"];
            this.OverrideController["clip_2"] = clip;

            this.UmaAnimator.Play("motion_1", -1);
            this.UmaAnimator.SetTrigger(needTransit ? "next_s" : "next");
            this.isAnimatorControl = false;
        }
        else if (clip.name.Contains("tail"))
        {
            if (this.IsMini) yield break;
            this.UpBodyReset();
            this.OverrideController["clip_t"] = clip;
            this.UmaAnimator.Play("motion_t", 1, 0);
        }
        else if (clip.name.Contains("face"))
        {
            if (this.IsMini) yield break;
            this.FaceDrivenKeyTarget.ResetLocator();
            this.FaceOverrideController["clip_1"] = clip;
            this.isAnimatorControl = true;
            this.UmaFaceAnimator.Play("motion_1", 0, 0);
        }
        else if (clip.name.Contains("ear"))
        {
            if (this.IsMini) yield break;
            this.FaceOverrideController["clip_2"] = clip;
            this.UmaFaceAnimator.Play("motion_1", 1, 0);
        }
        else if (clip.name.Contains("pos"))
        {
            if (this.IsMini) yield break;
            this.OverrideController["clip_p"] = clip;
            this.UmaAnimator.Play("motion_1", 2, 0);
        }
        else if (clip.name.Contains("cam"))
        {
            Builder.SetPreviewCamera(clip);
        }
        else
        {
            this.UpBodyReset();
            this.UmaAnimator.Rebind();
            this.OverrideController["clip_1"] = this.OverrideController["clip_2"];
            var lastTime = this.UmaAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            this.OverrideController["clip_2"] = clip;
            // If Cut-in, play immediately without state interpolation
            if (clip.name.Contains("crd") || clip.name.Contains("res_chr"))
            {
                var facialMotion = Main.AbMotions.FirstOrDefault(a => a.Name.EndsWith(clip.name + "_face"));
                var cameraMotion = Main.AbMotions.FirstOrDefault(a => a.Name.EndsWith(clip.name + "_cam"));
                var earMotion = Main.AbMotions.FirstOrDefault(a => a.Name.EndsWith(clip.name + "_ear"));
                var posMotion = Main.AbMotions.FirstOrDefault(a => a.Name.EndsWith(clip.name + "_pos"));

                if (facialMotion != null)
                {
                    yield return facialMotion.LoadAssetBundle(this.gameObject);
                }

                if (earMotion != null)
                {
                    yield return earMotion.LoadAssetBundle(this.gameObject);
                }

                if (cameraMotion != null)
                {
                    yield return cameraMotion.LoadAssetBundle(this.gameObject);
                }

                if (posMotion != null)
                {
                    yield return posMotion.LoadAssetBundle(this.gameObject);
                }

                if (this.IsMini)
                {
                    Builder.SetPreviewCamera(null);
                }

                this.UmaAnimator.Play("motion_2", 0, 0);

                if (clip.name.Contains("cti_crd"))
                {
                    string[] param = clip.name.Split('_');
                    if (param.Length > 4)
                    {
                        int index = int.Parse(param[4]) + 1;
                        var nextMotion = Main.AbMotions.FirstOrDefault(a => a.Name.EndsWith($"{param[0]}_{param[1]}_{param[2]}_{param[3]}_0{index}"));
                        var aevent = new AnimationEvent
                        {
                            time = clip.length * 0.99f,
                            stringParameter = (nextMotion != null ? nextMotion.Name : null),
                            functionName = (nextMotion != null ? "SetNextAnimationCut" : "SetEndAnimationCut")
                        };
                        clip.AddEvent(aevent);
                    }
                }
            }
            else
            {
                if (this.FaceDrivenKeyTarget)
                {
                    this.FaceDrivenKeyTarget.ResetLocator();
                }
                this.isAnimatorControl = false;
                Builder.SetPreviewCamera(null);

                this.UmaAnimator.Play("motion_1", 0, lastTime);
                this.UmaAnimator.SetTrigger("next");
            }
        }
    }

    public void LoadBody(AssetBundle bundle)
    {
        foreach (string name in bundle.GetAllAssetNames())
        {
            object asset = bundle.LoadAsset(name);

            if (asset == null || asset.GetType() != typeof(GameObject)) { continue; }
            this.Body = Instantiate((GameObject)asset, this.transform);
        }
        
        this.UmaAnimator = this.Body.GetComponent<Animator>();

        if (this.IsMini)
        {
            this.UpBodyBone = this.Body.transform.Find("Position/Hip").gameObject;
        }
        else
        {
            this.UpBodyBone = this.Body.GetComponent<AssetHolder>()._assetTable["upbody_ctrl"] as GameObject;
        }

        if (this.IsGeneric)
        {
            List<Texture2D> textures = this.GenericBodyTextures;
            string costumeIdShort = this.VarCostumeIdShort,
                   costumeIdLong = this.VarCostumeIdLong,
                   height = this.VarHeight,
                   skin = this.VarSkin,
                   socks = this.VarSocks,
                   bust = this.VarBust;

            foreach (Renderer r in this.Body.GetComponentsInChildren<Renderer>())
            {
                foreach (Material m in r.sharedMaterials)
                {
                    string mainTex = "", toonMap = "", tripleMap = "", optionMap = "", zekkenNumberTex = "";

                    if (this.IsMini)
                    {

                        m.SetTexture("_MainTex", textures[0]);
                    }
                    else
                    {
                        //BodyAlapha's shader need to change manually.
                        if (m.name.Contains("bdy") && m.name.Contains("Alpha"))
                        {
                            m.shader = Builder.bodyAlphaShader;
                        }

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
                                        int color = UnityEngine.Random.Range(0, 4);
                                        mainTex = $"tex_bdy0001_00_zekken{color}_{bust}_diff";
                                        toonMap = $"tex_bdy0001_00_zekken{color}_{bust}_shad_c";
                                        tripleMap = $"tex_bdy0001_00_zekken0_{bust}_base";
                                        optionMap = $"tex_bdy0001_00_zekken0_{bust}_ctrl";
                                        break;
                                }

                                zekkenNumberTex = $"tex_bdy0001_00_num{UnityEngine.Random.Range(1, 18):d2}";
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

                        if (!string.IsNullOrEmpty(zekkenNumberTex))
                            m.SetTexture("_ZekkenNumberTex", textures.FirstOrDefault(t => t.name == zekkenNumberTex));
                    }
                }
            }
        }
        else
        {
            foreach (Renderer r in this.Body.GetComponentsInChildren<Renderer>())
            {
                foreach (Material m in r.sharedMaterials)
                {
                    //BodyAlapha's shader need to change manually.
                    if (m.name.Contains("bdy") && m.name.Contains("Alpha"))
                    {
                        m.shader = Builder.bodyAlphaShader;
                    }
                }
            }
        }
    }

    public void LoadHead(AssetBundle bundle)
    {
        foreach (string name in bundle.GetAllAssetNames())
        {
            object asset = bundle.LoadAsset(name);

            if (asset == null || asset.GetType() != typeof(GameObject)) { continue; }
            this.Head = Instantiate((GameObject)asset, this.transform);
        }

        //Some setting for Head
        this.EnableEyeTracking = UI.EnableEyeTracking;

        foreach (Renderer r in Head.GetComponentsInChildren<Renderer>())
        {
            foreach (Material m in r.sharedMaterials)
            {
                if (Head.name.Contains("mchr"))
                {
                    if (r.name.Contains("Hair"))
                    {
                        this.Tail = Head;
                    }
                    if (r.name == "M_Face")
                    {
                        m.SetTexture("_MainTex", this.MiniHeadTextures.First(t => t.name.Contains("face") && t.name.Contains("diff")));
                    }
                    if (r.name == "M_Cheek")
                    {
                        m.CopyPropertiesFromMaterial(Builder.TransMaterialCharas);
                        m.SetTexture("_MainTex", this.MiniHeadTextures.First(t => t.name.Contains("cheek")));
                    }
                    if (r.name == "M_Mouth")
                    {
                        m.SetTexture("_MainTex", this.MiniHeadTextures.First(t => t.name.Contains("mouth")));
                    }
                    if (r.name == "M_Eye")
                    {
                        m.SetTexture("_MainTex", this.MiniHeadTextures.First(t => t.name.Contains("eye")));
                    }
                    if (r.name.StartsWith("M_Mayu_"))
                    {
                        m.SetTexture("_MainTex", this.MiniHeadTextures.First(t => t.name.Contains("mayu")));
                    }
                }
                else
                {
                    //Glasses's shader need to change manually.
                    if (r.name.Contains("Hair") && r.name.Contains("Alpha"))
                    {
                        m.shader = Builder.alphaShader;
                    }

                    //Blush Setting
                    if (r.name.Contains("Cheek"))
                    {
                        var table = this.Head.GetComponent<AssetHolder>()._assetTable.list;
                        this.CheekTex_0 = table.FindLast(a => a.Key.Equals("cheek0")).Value as Texture;
                        this.CheekTex_1 = table.FindLast(a => a.Key.Equals("cheek1")).Value as Texture;
                        this.CheekTex_2 = table.FindLast(a => a.Key.Equals("cheek2")).Value as Texture;
                    }
                    switch (m.shader.name)
                    {
                        case "Gallop/3D/Chara/MultiplyCheek":
                            m.shader = Builder.cheekShader;
                            break;
                        case "Gallop/3D/Chara/ToonFace/TSER":
                            m.shader = Builder.faceShader;
                            m.SetFloat("_CylinderBlend", 0.25f);
                            m.SetColor("_RimColor", new Color(0, 0, 0, 0));
                            break;
                        case "Gallop/3D/Chara/ToonEye/T":
                            m.shader = Builder.eyeShader;
                            m.SetFloat("_CylinderBlend", 0.25f);
                            break;
                        case "Gallop/3D/Chara/ToonHair/TSER":
                            m.shader = Builder.hairShader;
                            m.SetFloat("_CylinderBlend", 0.25f);
                            break;
                        case "Gallop/3D/Chara/ToonMayu":
                            m.shader = Builder.eyebrowShader;
                            break;
                        default:
                            Debug.LogError(m.shader.name);
                            break;
                    }
                }
            }
        }
    }

    public void LoadTail(AssetBundle bundle)
    {
        foreach (string name in bundle.GetAllAssetNames())
        {
            object asset = bundle.LoadAsset(name);

            if (asset == null || asset.GetType() != typeof(GameObject)) { continue; }
            this.Tail = Instantiate((GameObject)asset, this.transform);
        }

        var textures = this.TailTextures;
        foreach (Renderer r in this.Tail.GetComponentsInChildren<Renderer>())
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

    public void LoadPhysics(AssetBundle bundle)
    {
        foreach (string name in bundle.GetAllAssetNames())
        {
            object asset = bundle.LoadAsset(name);

            if (asset == null || asset.GetType() != typeof(GameObject)) { continue; }
            if (bundle.name.Contains("cloth"))
            {
                if (!this.PhysicsController)
                {
                    this.PhysicsController = new GameObject("PhysicsController");
                    this.PhysicsController.transform.SetParent(this.transform);
                }
                Instantiate((GameObject)asset, this.PhysicsController.transform);
            }
        }
    }

    public void LoadTextures(AssetBundle bundle)
    {
        foreach (string name in bundle.GetAllAssetNames())
        {
            object asset = bundle.LoadAsset(name);

            if (asset == null || asset.GetType() != typeof(Texture2D)) { continue; }
            var tex2D = asset as Texture2D;
            if (bundle.name.Contains("/mini/head"))
            {
                this.MiniHeadTextures.Add(tex2D);
            }
            else if (bundle.name.Contains("/tail/"))
            {
                this.TailTextures.Add(tex2D);
            }
            else if (bundle.name.Contains("bdy0"))
            {
                this.GenericBodyTextures.Add(tex2D);
            }
        }
    }

    public void LoadTear(AssetBundle bundle)
    {
        foreach (string name in bundle.GetAllAssetNames())
        {
            object asset = bundle.LoadAsset(name);

            if (asset == null || asset.GetType() != typeof(GameObject)) { continue; }
            GameObject go = asset as GameObject;
            if (go.name.EndsWith("000"))
            {
                this.TearPrefab_0 = go;
            }
            else if (go.name.EndsWith("001"))
            {
                this.TearPrefab_1 = go;
            }
        }
    }

    public void MergeModel()
    {
        if (!Body) return;

        List<Transform> bodybones = new List<Transform>(Body.GetComponentInChildren<SkinnedMeshRenderer>().bones);
        List<Transform> emptyBones = new List<Transform>();
        emptyBones.Add(Body.GetComponentInChildren<SkinnedMeshRenderer>().rootBone.Find("Tail_Ctrl"));
        while (Body.transform.childCount > 0)
        {
            var child = Body.transform.GetChild(0);
            child.SetParent(transform);
        }
        Body.SetActive(false); //for debugging


        //MergeHead
        if (Head)
        {
            var headskins = Head.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (SkinnedMeshRenderer headskin in headskins)
            {
                emptyBones.AddRange(MergeBone(headskin, bodybones));
            }
            var eyes = new GameObject("Eyes");
            eyes.transform.SetParent(transform);
            while (Head.transform.childCount > 0)
            {
                var child = Head.transform.GetChild(0);
                child.SetParent(child.name.Contains("info") ? eyes.transform : transform);
            }
            Head.SetActive(false); //for debugging
        }


        //MergeTail
        if (Tail)
        {
            var tailskin = Tail.GetComponentInChildren<SkinnedMeshRenderer>();
            emptyBones.AddRange(MergeBone(tailskin, bodybones));
            while (Tail.transform.childCount > 0)
            {
                var child = Tail.transform.GetChild(0);
                child.SetParent(transform);
            }
            Tail.SetActive(false); //for debugging
        }


        emptyBones.ForEach(a => { if (a) Destroy(a.gameObject); });

        //MergeAvatar
        UmaAnimator = gameObject.AddComponent<Animator>();
        UmaAnimator.avatar = AvatarBuilder.BuildGenericAvatar(gameObject, gameObject.name);
        OverrideController = Instantiate(UmaViewerBuilder.Instance.OverrideController);
        UmaAnimator.runtimeAnimatorController = OverrideController;
    }

    public void SetHeight(int scale)
    {
        transform.Find("Position").localScale *= (scale / 160f);//WIP
    }

    public Transform[] MergeBone(SkinnedMeshRenderer from, List<Transform> targetBones)
    {
        var rootbone = targetBones.FindLast(a => a.name.Equals(from.rootBone.name));
        if (rootbone) from.rootBone = rootbone;

        List<Transform> emptyBones = new List<Transform>();
        Transform[] tmpBone = new Transform[from.bones.Length];
        for (int i = 0; i < tmpBone.Length; i++)
        {
            var targetbone = targetBones.FindLast(a => a.name.Equals(from.bones[i].name));
            if (targetbone)
            {
                tmpBone[i] = targetbone;
                from.bones[i].position = targetbone.position;
                while (from.bones[i].transform.childCount > 0)
                {
                    from.bones[i].transform.GetChild(0).SetParent(targetbone);
                }
                emptyBones.Add(from.bones[i]);
            }
            else
            {
                tmpBone[i] = from.bones[i];
            }
        }
        from.bones = tmpBone;
        return emptyBones.ToArray();
    }

    public void LoadPhysics()
    {
        cySpringDataContainers = new List<CySpringDataContainer>(PhysicsController.GetComponentsInChildren<CySpringDataContainer>());
        var bones = new List<Transform>(GetComponentsInChildren<Transform>());
        var colliders = new List<GameObject>();

        foreach (CySpringDataContainer spring in cySpringDataContainers)
        {
            colliders.AddRange(spring.InitiallizeCollider(bones));
        }
        foreach (CySpringDataContainer spring in cySpringDataContainers)
        {
            spring.InitializePhysics(bones, colliders);
        }
    }

    public void SetDynamicBoneEnable(bool isOn)
    {
        if (IsMini) return;
        EnablePhysics = isOn;
        foreach (CySpringDataContainer cySpring in cySpringDataContainers)
        {
            cySpring.EnablePhysics(isOn);
        }
    }

    public void UpBodyReset()
    {
        if (UpBodyBone)
        {
            UpBodyBone.transform.localPosition = UpBodyPosition;
            UpBodyBone.transform.localRotation = UpBodyRotation;
        }
    }

    private void OnDestroy()
    {
        Main.UnloadByDependency(this.gameObject);
        if(Type == ContainerType.Uma)
        {
            //It seems that OnDestroy will executed after new model loaded, which cause new FacialPanels empty...
            UmaViewerUI.Instance.currentFaceDrivenKeyTarget = null;
            UmaViewerUI.Instance.LoadEmotionPanels(null);
            UmaViewerUI.Instance.LoadFacialPanels(null);
        }
    }

    public enum ContainerType
    {
        Uma,
        Prop,
        Live
    }
}

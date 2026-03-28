using Gallop.Live.Cutt;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Gallop.Live
{
    public class Director : MonoBehaviour
    {
        private static Director _instance = null;
        public LiveTimelineControl _liveTimelineControl; //Edited to public
        [SerializeField]
        public float _liveCurrentTime;  //Edited to public
        public bool _isLiveSetup; //Edit to pulic
        public StageController _stageController; //Edited to public
        [SerializeField]
        private GameObject[] _cameraNodes;
        private Camera[] _cameraObjects;
        private Transform[] _cameraTransforms;
        [SerializeField]
        private CameraLookAt _cameraLookAt;
        private int _activeCameraIndex  = 1;
        private readonly int[] kTimelineCameraIndices = new int[3] { 1, 2, 3 };
        [SerializeField] private bool _enableMirrorReflection = true;
        [SerializeField] private List<MirrorReflection> _mirrorReflections = new List<MirrorReflection>();
        [SerializeField] private bool _mirrorRenderInLateUpdate = true;

        public static Director instance => _instance;

        //real work start
        public LiveEntry live;
        private const string CUTT_PATH = "cutt/cutt_son{0}/cutt_son{0}";
        private const string STAGE_PATH = "3d/env/live/live{0}/pfb_env_live{0}_controller000";
        private const string SONG_PATH = "sound/l/{0}/snd_bgm_live_{0}_oke_01";
        private const string VOCAL_PATH = "sound/l/{0}/snd_bgm_live_{0}_chara_{1}_01";
        private const string RANDOM_VOCAL_PATH = "sound/l/{0}/snd_bgm_live_{0}_chara";
        private const string LIVE_PART_PATH = "live/musicscores/m{0}/m{0}_part";

        private UmaViewerBuilder Builder => UmaViewerBuilder.Instance;

        public List<Transform> charaObjs;

        public List<UmaContainerCharacter> CharaContainerScript = new List<UmaContainerCharacter>();

        public List<Animation> charaAnims;
        public List<UmaViewerAudio.CuteAudioSource> liveVocal = new List<UmaViewerAudio.CuteAudioSource>();
        public UmaViewerAudio.CuteAudioSource liveMusic = new UmaViewerAudio.CuteAudioSource();

        public PartEntry partInfo;

        public bool _syncTime = false;
        public bool _soloMode = false;

        public int characterCount = 0;
        public int allowCount = 0;

        public int liveMode = 1;

        public LiveViewerUI UI;

        public float totalTime;

        public SliderControl sliderControl;

        public bool IsRecordVMD;

        public bool RequireStage = true;

        private bool _lateTimelineAppliedThisFrame;

        public Transform MainCameraTransform => _mainCameraTransform;

        private Transform _mainCameraTransform;

        private static readonly Dictionary<string, UmaDatabaseEntry> _laserBundleCache
            = new Dictionary<string, UmaDatabaseEntry>();

        private static UmaDatabaseEntry FindLaserBundleEntry(string bgId)
{
    if (string.IsNullOrEmpty(bgId)) return null;
    if (_laserBundleCache.TryGetValue(bgId, out var cached)) return cached;

    var ab = UmaViewerMain.Instance?.AbList;
    if (ab == null)
    {
        Debug.LogWarning("[LaserResolve] AbList is null");
        _laserBundleCache[bgId] = null;
        return null;
    }

    string folderPrefix = $"3d/env/live/live{bgId}/";
    string needle = $"pfb_env_live{bgId}_laser000";

    // 只扫这个 bgId 目录下的 env bundle
    foreach (var kv in ab)
    {
        var key = kv.Key;
        var entry = kv.Value;
        if (entry == null || !entry.IsAssetBundle) continue;

        // ✅ 只看 live{bgId} 目录
        if (!key.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase) &&
            (entry.Name == null || !entry.Name.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase)))
            continue;

        // ✅ 禁止碰 effect/live 的 laser 包（它不是舞台 laser000）
        if (entry.Name != null &&
            entry.Name.StartsWith("3d/effect/live/pfb_eff_live_laser_", StringComparison.OrdinalIgnoreCase))
            continue;

        // ✅ 本地没文件就跳过
        if (!File.Exists(entry.Path)) continue;

        // ✅ 不递归：只为 GetAllAssetNames 做快速探测
        var bundle = UmaAssetManager.LoadAssetBundle(entry, neverUnload: true, isRecursive: false);
        if (!bundle) continue;

        var names = bundle.GetAllAssetNames();
        if (names != null && names.Any(n => n.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0))
        {
            Debug.Log($"[LaserResolve] FOUND needle={needle} in key={key} bundleName={entry.Name}");
            _laserBundleCache[bgId] = entry;
            return entry;
        }
    }

    Debug.Log($"[LaserResolve] NOT FOUND needle={needle} in {folderPrefix}");
    _laserBundleCache[bgId] = null;
    return null;
}


        private static void PreloadLaserIfNeeded(string bgId)
        {
            var entry = FindLaserBundleEntry(bgId);
            if (entry != null)
            {
                UmaAssetManager.LoadAssetBundle(entry, neverUnload: true, isRecursive: true);
            }
        }

        public bool isTimelineControlled
        {
            get
            {
                if (_liveTimelineControl != null)
                {
                    return _liveTimelineControl.data != null;
                }
                return false;
            }
        }

        public float CalcFrameJustifiedMusicTime()
        {
            if (isTimelineControlled)
            {
                return Mathf.RoundToInt(musicScoreTime * 60f) / 60f;
            }
            return musicScoreTime;
        }

        public float musicScoreTime => Mathf.Clamp(smoothMusicScoreTime, 0f, 99999f);

        private float smoothMusicScoreTime => _liveCurrentTime;//temp to liveCurrentTime

        public void Initialize()
        {
            if (live != null)
            {
                _instance = this;
                Debug.Log(string.Format(CUTT_PATH, live.MusicId));
                Builder.LoadAssetPath(string.Format(CUTT_PATH, live.MusicId), transform);
                if (RequireStage)
                {
                    Debug.Log(live.BackGroundId);

                    // ✅ 关键：先 preload 再 instantiate
                    PreloadStageBundlesBeforeInstantiate(live.BackGroundId);
                    //PreloadLaserIfNeeded(live.BackGroundId);

                    Builder.LoadAssetPath(string.Format(STAGE_PATH, live.BackGroundId), transform);
                    

                    _liveTimelineControl.StageObjectMap = _stageController.StageObjectMap;
                }


                //Make CharacterObject

                var characterStandPos = _liveTimelineControl.transform.Find("CharacterStandPos");
                int counter = 0;
                var standPos = characterStandPos.GetComponentsInChildren<Transform>();
                var count = _liveTimelineControl.data.characterSettings.useHighPolygonModel.Length;
                for (int i = 0; i < count; i++)
                {
                    if (i < characterStandPos.childCount)
                    {
                        var newObj = Instantiate(standPos[i + 1], transform);
                        newObj.gameObject.name = string.Format("CharacterObject{0}", counter);
                        charaObjs.Add(newObj.transform);
                        counter++;
                    }
                    else
                    {
                        var newObj = Instantiate(standPos[i % characterStandPos.childCount + 1], transform);
                        newObj.gameObject.name = string.Format("CharacterObject{0}", counter);
                        charaObjs.Add(newObj.transform);
                        counter++;
                    }
                };


                //Get live parts info
                UmaDatabaseEntry partAsset = UmaViewerMain.Instance.AbList[string.Format(LIVE_PART_PATH, live.MusicId)];
                UmaViewerAudio.LastAudioPartIndex = -1;

                Debug.Log(partAsset.Name);

                AssetBundle bundle = UmaAssetManager.LoadAssetBundle(partAsset);
                TextAsset partData = bundle.LoadAsset<TextAsset>($"m{live.MusicId}_part");
                partInfo = new PartEntry(partData.text);

            }

        }

        public void InitializeUI()
        {
            UI = GameObject.Find("LiveUI").GetComponent<LiveViewerUI>();

            sliderControl = UI.ProgressBar.GetComponent<SliderControl>();
            LiveViewerUI.Instance.RecordingUI.SetActive(IsRecordVMD);
            LiveViewerUI.Instance.RecordingText.text = $"�� Recording...\r\n VMD will be saved in {Path.GetFullPath(Application.dataPath + UnityHumanoidVMDRecorder.FileSavePath)}";
        }

        public void InitializeTimeline(List<LiveCharacterSelect> characters, int mode)
        {
            totalTime = _liveTimelineControl.data.timeLength;

            liveMode = mode;

            allowCount = characters.Count;

            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].CharaEntry.Name != "")
                {
                    characterCount += 1;
                }
            }
            if (characterCount == 1)
            {
                _soloMode = true;
            }

            _liveTimelineControl.InitCharaMotionSequence(_liveTimelineControl.data.characterSettings.motionSequenceIndices);

            _liveTimelineControl.OnUpdateLipSync += delegate (LiveTimelineKeyIndex keyData_, float liveTime_)
            {
                var prevKey = keyData_.prevKey as LiveTimelineKeyLipSyncData;
                var curKey = keyData_.key as LiveTimelineKeyLipSyncData;
                var nextKey = keyData_.nextKey as LiveTimelineKeyLipSyncData;
                for (int k = 0; k < charaObjs.Count; k++)
                {
                    if (k < CharaContainerScript.Count)
                    {
                        var container = CharaContainerScript[k];
                        container.FaceDrivenKeyTarget.AlterUpdateAutoLip(prevKey, curKey, liveTime_, ((int)curKey.character >> k) % 2);
                    }
                }
            };

            _liveTimelineControl.OnUpdateFacial += delegate (FacialDataUpdateInfo updateInfo_, float liveTime_, int position)
            {
                if (position < charaObjs.Count)
                {
                    var container = CharaContainerScript[position];
                    container.FaceDrivenKeyTarget.AlterUpdateFacialNew(ref updateInfo_, liveTime_);
                }
            };

            _liveTimelineControl.OnUpdateGlobalLight += delegate (ref GlobalLightUpdateInfo updateInfo)
            {
                var tmpPos = -(updateInfo.lightRotation * Vector3.forward).normalized;
                foreach (var locator in _liveTimelineControl.liveCharactorLocators)
                {
                    if (locator != null && updateInfo.flags.hasFlag(locator.liveCharaStandingPosition) && locator is LiveTimelineCharaLocator charaLocator)
                    {
                        var container = charaLocator.UmaContainer;
                        if (container)
                        {
                            MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
                            propertyBlock.SetFloat("_RimShadowRate", updateInfo.globalRimShadowRate);
                            propertyBlock.SetColor("_RimColor", updateInfo.rimColor);
                            propertyBlock.SetFloat("_RimStep", updateInfo.rimStep);
                            propertyBlock.SetFloat("_RimFeather", updateInfo.rimFeather);
                            propertyBlock.SetFloat("_RimSpecRate", updateInfo.rimSpecRate);
                            propertyBlock.SetFloat("_RimHorizonOffset", updateInfo.RimHorizonOffset);
                            propertyBlock.SetFloat("_RimVerticalOffset", updateInfo.RimVerticalOffset);
                            propertyBlock.SetFloat("_RimHorizonOffset2", updateInfo.RimHorizonOffset2);
                            propertyBlock.SetFloat("_RimVerticalOffset2", updateInfo.RimVerticalOffset2);
                            propertyBlock.SetColor("_RimColor2", updateInfo.rimColor2);
                            propertyBlock.SetFloat("_RimStep2", updateInfo.rimStep2);
                            propertyBlock.SetFloat("_RimFeather2", updateInfo.rimFeather2);
                            propertyBlock.SetFloat("_RimSpecRate2", updateInfo.rimSpecRate2);
                            propertyBlock.SetFloat("_RimShadowRate2", updateInfo.globalRimShadowRate2);
                            foreach (var renderer in container.Renderers)
                            {
                                renderer.SetPropertyBlock(propertyBlock);
                                foreach(var mat in renderer.materials)
                                {
                                    mat.SetFloat("_UseOriginalDirectionalLight", 1);
                                    mat.SetVector("_OriginalDirectionalLightDir", tmpPos);
                                }
                            }
                        }
                    }
                }
            };

            _liveTimelineControl.OnUpdateBgColor1 += delegate (ref BgColor1UpdateInfo updateInfo)
            {
                foreach (var locator in _liveTimelineControl.liveCharactorLocators)
                {
                    var EFlags = (LiveCharaPositionFlag)updateInfo.flags;
                    if (locator != null && (updateInfo.flags == 0 || EFlags.hasFlag(locator.liveCharaStandingPosition)) && locator is LiveTimelineCharaLocator charaLocator)
                    {
                        var container = charaLocator.UmaContainer;
                        if (container)
                        {
                            MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
                            propertyBlock.SetColor("_CharaColor", updateInfo.color);
                            propertyBlock.SetColor("_ToonDarkColor", updateInfo.toonDarkColor);
                            propertyBlock.SetColor("_ToonBrightColor", updateInfo.toonBrightColor);
                            propertyBlock.SetColor("_OutlineColor", updateInfo.outlineColor);
                            propertyBlock.SetFloat("_Saturation", updateInfo.Saturation);
                            foreach (var renderer in container.Renderers)
                            {
                                renderer.SetPropertyBlock(propertyBlock);
                            }
                        }
                    }
                }
            };

            SetupCharacterLocator();
            InitializeCamera();
            InitializeMirrorReflections();
            UpdateMainCamera();
            InitializeMultiCamera(_liveTimelineControl);
            for (int i = 0; i < kTimelineCameraIndices.Length; i++)
            {
                int num = kTimelineCameraIndices[i];
                if (num < _cameraObjects.Length)
                {
                    _liveTimelineControl.SetTimelineCamera(_cameraObjects[num], i);
                }
            }

            _liveTimelineControl.OnUpdateCameraSwitcher += delegate (int cameraIndex_)
            {
                if (cameraIndex_ < 0)
                {
                    _activeCameraIndex = 0;
                }
                else if (cameraIndex_ < kTimelineCameraIndices.Length)
                {
                    _activeCameraIndex = kTimelineCameraIndices[cameraIndex_];
                }
            };
        }

        public void InitializeCamera()
        {
            if (_cameraObjects == null)
            {
                _cameraObjects = new Camera[_cameraNodes.Length + 1];
                _cameraTransforms = new Transform[_cameraNodes.Length + 1];
                for (int i = 0; i < _cameraNodes.Length; i++)
                {
                    GameObject gameObject = _cameraNodes[i];
                    Camera camera = gameObject.GetComponent<Camera>();
                    if (camera == null)
                    {
                        camera = gameObject.GetComponentInChildren<Camera>();
                    }
                    //camera.cullingMask = num;
                    _cameraObjects[i] = camera;
                    _cameraTransforms[i] = camera.transform;
                }
            }
        }

        public void InitializeMultiCamera(LiveTimelineControl control)
        {
            var cameraCount = control.data.multiCameraSettings.cameraNum;
            MultiCamera[] cameras = new MultiCamera[cameraCount];
            var root = new GameObject("MultiCameras");
            root.transform.SetParent(control.transform);
            for (int i = 0; i < cameraCount; i++)
            {
                var camObj = new GameObject($"MultiCamera_{i}");
                camObj.transform.SetParent(root.transform);

                var cam = camObj.AddComponent<MultiCamera>();
                cam.Initialize();
                cameras[i] = cam;
                control.MultiRecordFrames.Add(new List<LiveCameraFrame>());
            }
            control.SetMultiCamera(cameras);
        }

        private void UpdateMainCamera()
        {
            if (_cameraObjects == null) return;
            for (int i = 0; i < _cameraNodes.Length; i++)
            {
                bool activeSelf = _cameraNodes[i].activeSelf;
                bool flag = i == _activeCameraIndex;
                _cameraNodes[i].SetActive(flag);
                if (i == 0 && activeSelf != flag && flag && _cameraLookAt != null)
                {
                    _cameraLookAt.ActivationUpdate();
                }
            }
            _mainCameraTransform = _cameraTransforms[_activeCameraIndex];
        }

        private void SetupCharacterLocator()
        {
            if (!_liveTimelineControl) return;
            for (int i = 0; i < CharaContainerScript.Count; i++)
            {
                var container = CharaContainerScript[i];
                container.LiveLocator = new LiveTimelineCharaLocator(container);
                container.LiveLocator.liveCharaStandingPosition = (LiveCharaPosition)i;
                _liveTimelineControl.liveCharactorLocators[i] = container.LiveLocator;
                container.LiveLocator.liveCharaInitialPosition = container.transform.position;
            }
        }

        public void InitializeMusic(int songid, List<LiveCharacterSelect> characters)
        {

            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].CharaEntry.Name != "" && i < partInfo.SingerCount)
                {
                    var charaid = characters[i].CharaEntry.Id;

                    var entry = UmaViewerMain.Instance.AbSounds.FirstOrDefault(a => a.Name.Contains(string.Format(VOCAL_PATH, songid, charaid)) && a.Name.EndsWith("awb"));
                    if (entry == null)
                    {
                        List<UmaDatabaseEntry> entries = new List<UmaDatabaseEntry>();
                        foreach (var random in UmaViewerMain.Instance.AbSounds.Where(a => (a.Name.Contains(string.Format(RANDOM_VOCAL_PATH, songid)) && a.Name.EndsWith("awb"))))
                        {
                            entries.Add(random);
                        }
                        if (entries.Count > 0)
                        {
                            entry = entries[UnityEngine.Random.Range(0, entries.Count - 1)];
                        }
                    }

                    if (entry != null)
                    {
                        Debug.Log(entry.Name);
                        liveVocal.Add(UmaViewerAudio.ApplySound(entry.Name.Split('.')[0], i));
                    }
                }
            }


            liveMusic = UmaViewerAudio.ApplySound(string.Format(SONG_PATH, songid), -1);
        }

        public void Play()
        {

            foreach (var vocal in liveVocal)
            {
                UmaViewerAudio.Play(vocal);
            }
            UmaViewerAudio.Play(liveMusic);

            _isLiveSetup = true;
            _liveCurrentTime = 0;

            if (IsRecordVMD)
            {
                foreach (var container in CharaContainerScript)
                {
                    var rootbone = container.transform.Find("Position");
                    var newRecorder = rootbone.gameObject.AddComponent<UnityHumanoidVMDRecorder>();
                    newRecorder.UseParentOfAll = true;
                    newRecorder.UseAbsoluteCoordinateSystem = true;
                    newRecorder.Initialize();
                    if (!newRecorder.IsRecording)
                    {
                        newRecorder.StartRecording(true);
                    }
                }
            }
        }

        private void OnTimelineUpdate(float _liveCurrentTime)
        {
            _liveTimelineControl.AlterUpdate(_liveCurrentTime);
            if (!_soloMode)
            {
                UmaViewerAudio.AlterUpdate(_liveCurrentTime, partInfo, liveVocal, sliderControl.is_Outed);
            }
        }

        private void ApplyTimelineLateUpdate()
        {
            if (_lateTimelineAppliedThisFrame || _liveTimelineControl == null)
                return;

            _liveTimelineControl.AlterLateUpdate();
            _lateTimelineAppliedThisFrame = true;
        }

        bool isExit;
        void Update()
        {
            if (isExit) return;

            if (_isLiveSetup)
            {
                _lateTimelineAppliedThisFrame = false;

                if ((!UmaViewerMain.TryConsumeEscapeForFullScreen() && Input.GetKeyDown(KeyCode.Escape)) || _liveCurrentTime >= totalTime)
                {
                    ExitLive();
                }

                if (_syncTime == false)
                {
                    if(liveMusic.sourceList.Count == 0)
                    {
                        _syncTime = true;
                    }
                    else if (liveMusic.sourceList[0].time > 0.01)
                    {
                        _liveCurrentTime = UI.ProgressBar.value * totalTime;
                        _liveCurrentTime = Mathf.Clamp(_liveCurrentTime, 0f, Mathf.Max(0f, totalTime - 0.001f));
                        _liveCurrentTime = liveMusic.sourceList[0].time;
                        _syncTime = true;
                    }
                }
                else
                {
                    if (IsRecordVMD)
                    {
                        _liveCurrentTime += (1 / 60f);
                        if (liveMusic != null)
                        {
                            UmaViewerAudio.Stop(liveMusic);
                            foreach (var vocal in liveVocal)
                            {
                                UmaViewerAudio.Stop(vocal);
                            }
                        }

                        UI.ProgressBar.SetValueWithoutNotify(_liveCurrentTime / totalTime);
                        OnTimelineUpdate(_liveCurrentTime);
                        ApplyTimelineLateUpdate();
                    }
                    else if (sliderControl.is_Outed)
                    {
                        _liveCurrentTime = UI.ProgressBar.value * totalTime;

                        if (liveMusic != null)
                        {
                            UmaViewerAudio.SetTime(liveMusic, _liveCurrentTime);

                            foreach (var vocal in liveVocal)
                            {
                                UmaViewerAudio.SetTime(vocal, _liveCurrentTime);
                            }

                            UmaViewerAudio.Play(liveMusic);

                            foreach (var vocal in liveVocal)
                            {
                                UmaViewerAudio.Play(vocal);
                            }
                        }

                        OnTimelineUpdate(_liveCurrentTime);
                        ApplyTimelineLateUpdate();

                        sliderControl.is_Outed = false;
                        sliderControl.is_Touched = false;
                        _syncTime = false;
                    }
                    else if (sliderControl.is_Touched)
                    {
                        _liveCurrentTime = UI.ProgressBar.value * totalTime;

                        if (liveMusic != null)
                        {
                            UmaViewerAudio.Stop(liveMusic);
                            foreach (var vocal in liveVocal)
                            {
                                UmaViewerAudio.Stop(vocal);
                            }
                        }

                        OnTimelineUpdate(_liveCurrentTime);
                        ApplyTimelineLateUpdate();
                    }
                    else
                    {
                        _liveCurrentTime += Time.deltaTime;
                        UI.ProgressBar.SetValueWithoutNotify(_liveCurrentTime / totalTime);
                        OnTimelineUpdate(_liveCurrentTime);
                        ApplyTimelineLateUpdate();
                    }
                }

                UpdateMainCamera();
            }
        }

        private void LateUpdate()
        {
            if (_isLiveSetup && _syncTime && !IsRecordVMD)
            {
                ApplyTimelineLateUpdate();
            }
            
            if (_enableMirrorReflection && _mirrorRenderInLateUpdate)
            {
                UpdateMirrorReflections();
            }
        }

        private void FixedUpdate()
        {
            LiveViewerUI.Instance.UpdateLyrics(_liveCurrentTime);
        }

        DateTime ExitTime;
        private void ExitLive()
        {
            isExit = true;
            if (_liveTimelineControl.IsRecordVMD)
            {
                ExitTime = DateTime.Now;
                SaveCameraVMD();
                SaveMultiCameraVMD();
                SaveCharacterVMD();
            }
            UmaSceneController.LoadScene("Version2");
            UmaAssetManager.UnloadAllBundle(true);
        }

        private void SaveCharacterVMD()
        {
            foreach (var container in CharaContainerScript)
            {
                var rootbone = container.transform.Find("Position");
                if (rootbone.gameObject.TryGetComponent(out UnityHumanoidVMDRecorder recorder))
                {
                    if (recorder.IsRecording)
                    {
                        recorder.StopRecording();
                        recorder.SaveLiveVMD(live, ExitTime, $"Live{live.MusicId}_Pos{CharaContainerScript.IndexOf(container)}", Config.Instance.VmdKeyReductionLevel);
                    }
                }
            }
        }

        private void SaveMultiCameraVMD()
        {
            for (int i = 0; i < _liveTimelineControl.data.worksheetList[0].multiCameraPosKeys.Count; i++)
            {
                var frames = _liveTimelineControl.MultiRecordFrames[i];
                frames[0].FovVaild = true;
                var fov = _liveTimelineControl.data.worksheetList[0].multiCameraPosKeys[i].keys.thisList;
                fov.ForEach(k =>
                {
                    var keyframe = frames.Find(f => f.frameIndex == k.frame);
                    if (keyframe != null)
                    {
                        var index = frames.IndexOf(keyframe);
                        keyframe.FovVaild = true;
                        if (index + 1 < frames.Count) frames[index + 1].FovVaild = true;
                        if (index - 1 > 0) frames[index - 1].FovVaild = true;
                        if (index - 2 > 0) frames[index - 2].FovVaild = true;
                        if (index - 3 > 0) frames[index - 3].FovVaild = true;
                    }
                });

                UnityCameraVMDRecorder.SaveLiveCameraVMD(live, ExitTime, frames, i);
            }
        }

        private void SaveCameraVMD()
        {
            var frames = _liveTimelineControl.RecordFrames;
            frames[0].FovVaild = true;
            var fov = _liveTimelineControl.data.worksheetList[0].cameraFovKeys.thisList;
            fov.ForEach(k =>
            {

                var keyframe = frames.Find(f => f.frameIndex == k.frame);
                if (keyframe != null)
                {
                    var index = frames.IndexOf(keyframe);
                    keyframe.FovVaild = true;
                    if (index + 1 < frames.Count) frames[index + 1].FovVaild = true;
                    if (index - 1 > 0) frames[index - 1].FovVaild = true;
                    if (index - 2 > 0) frames[index - 2].FovVaild = true;
                    if (index - 3 > 0) frames[index - 3].FovVaild = true;
                }
            });

            UnityCameraVMDRecorder.SaveLiveCameraVMD(live, ExitTime, frames);
        }

        public static List<UmaDatabaseEntry> GetLiveAllVoiceEntry(int songid, List<LiveCharacterSelect> characters)
        {
            List<UmaDatabaseEntry> entryList = new List <UmaDatabaseEntry>();
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].CharaEntry.Name != "")
                {
                    var charaid = characters[i].CharaEntry.Id;

                    var entry = UmaViewerMain.Instance.AbSounds.FirstOrDefault(a => a.Name.Contains(string.Format(VOCAL_PATH, songid, charaid)) && a.Name.EndsWith("awb"));
                    if (entry == null)
                    {
                        List<UmaDatabaseEntry> entries = new List<UmaDatabaseEntry>();
                        foreach (var random in UmaViewerMain.Instance.AbSounds.Where(a => (a.Name.Contains(string.Format(RANDOM_VOCAL_PATH, songid)) && a.Name.EndsWith("awb"))))
                        {
                            entries.Add(random);
                        }
                        if (entries.Count > 0)
                        {
                            entry = entries[UnityEngine.Random.Range(0, entries.Count - 1)];
                        }
                    }

                    if (entry != null)
                    {
                        entryList.Add(entry);
                    }
                }
            }

            var bgEntry = UmaViewerMain.Instance.AbSounds.FirstOrDefault(a => a.Name.Contains(string.Format(SONG_PATH, songid)) && a.Name.EndsWith("awb"));
            if (bgEntry != null)
            {
                entryList.Add(bgEntry);
            }
            return entryList;
        }
        private void PreloadStageBundlesBeforeInstantiate(string bgId)
        {
            if (string.IsNullOrEmpty(bgId)) return;

            var main = UmaViewerMain.Instance;
            if (main == null || main.AbList == null) return;

            string folderPrefix = $"3d/env/live/live{bgId}/";

            // 先找出 live{bgId} 目录下所有条目（只 preload AssetBundle）
            var stageKvs = main.AbList
                .Where(kv => kv.Key.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
                .Where(kv => kv.Value != null && kv.Value.IsAssetBundle)
                .ToList();

            // 关键：controller000 最后再 load，确保 laser 等外部 prefab bundle 先在内存里
            foreach (var kv in stageKvs)
            {
                if (kv.Key.IndexOf("_controller000", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                UmaAssetManager.LoadAssetBundle(kv.Value, neverUnload: true, isRecursive: true);
            }

            // 再 load controller000 自己（可选，但建议做，保证它也处于 loaded 状态）
            string controllerKey = string.Format(STAGE_PATH, bgId);
            if (main.AbList.TryGetValue(controllerKey, out var controllerEntry))
                UmaAssetManager.LoadAssetBundle(controllerEntry, neverUnload: true, isRecursive: true);

                Debug.Log($"[StagePreload] bgId={bgId}, stageBundles={stageKvs.Count}, prefix={folderPrefix}");
        }
        private void InitializeMirrorReflections()
        {
            if (!_enableMirrorReflection)
                return;

            _mirrorReflections.Clear();

            var mirrors = GetComponentsInChildren<MirrorReflection>(true);
            if (mirrors == null || mirrors.Length == 0)
            {
                Debug.Log("[Mirror] No MirrorReflection found.");
                return;
            }

            _mirrorReflections.AddRange(mirrors);

            Camera mainCam = null;
            if (_cameraObjects != null && _activeCameraIndex >= 0 && _activeCameraIndex < _cameraObjects.Length)
                mainCam = _cameraObjects[_activeCameraIndex];

            if (mainCam == null)
                mainCam = Camera.main;

            for (int i = 0; i < _mirrorReflections.Count; i++)
            {
                var mirror = _mirrorReflections[i];
                if (mirror == null) continue;

                mirror.Initialize(mainCam, i, false);
                mirror.SetupBaseCamera(mainCam, GetMainCameraFovFactor);
            }

            Debug.Log($"[Mirror] Initialized {_mirrorReflections.Count} mirrors.");
        }
        private float GetMainCameraFovFactor()
        {
            return 1f;
        }

        private void UpdateMirrorReflections()
        {
            if (_mirrorReflections == null || _mirrorReflections.Count == 0)
                return;

            Camera mainCam = null;
            if (_cameraObjects != null && _activeCameraIndex >= 0 && _activeCameraIndex < _cameraObjects.Length)
                mainCam = _cameraObjects[_activeCameraIndex];

            if (mainCam == null)
                mainCam = Camera.main;

            for (int i = 0; i < _mirrorReflections.Count; i++)
            {
                var mirror = _mirrorReflections[i];
                if (mirror == null) continue;

                mirror.SetBaseCamera(mainCam);
                mirror.SetFovFactorGetter(GetMainCameraFovFactor);
                mirror.UpdateMirrorParams();
                mirror.ForceRenderOnce();
            }
        }

    }

}

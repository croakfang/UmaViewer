using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using System;
using Newtonsoft.Json.Linq;
using System.Collections;
using static ManifestCategory;
using BaseNcoding;
using System.Text;

public class UmaViewerMain : MonoBehaviour
{
    public static UmaViewerMain Instance;
    private UmaViewerUI UI => UmaViewerUI.Instance;
    private UmaViewerBuilder Builder => UmaViewerBuilder.Instance;

    public List<CharaEntry> Characters = new List<CharaEntry>();
    public List<LiveEntry> Lives = new List<LiveEntry>();
    public List<UmaDatabaseEntry> AbList = new List<UmaDatabaseEntry>();
    public List<UmaDatabaseEntry> AbMotions = new List<UmaDatabaseEntry>();
    public List<UmaDatabaseEntry> AbSounds = new List<UmaDatabaseEntry>();
    public List<UmaDatabaseEntry> AbChara = new List<UmaDatabaseEntry>();
    public List<UmaDatabaseEntry> AbEffect = new List<UmaDatabaseEntry>();

    [Header("Asset Memory")]
    public bool ShadersLoaded = false;
    public List <UmaDatabaseEntry> LoadedBundles = new List<UmaDatabaseEntry>();
    public int Downloaded = 0;
    public List<string> DownloadingBundles = new List<string>();

    private void Awake()
    {
        Instance = this;
        Application.targetFrameRate = -1;
        AbList = UmaDatabaseController.Instance.MetaEntries.ToList();
        AbChara = AbList.Where(ab => ab.Name.StartsWith(UmaDatabaseController.CharaPath)).ToList();
        AbMotions = AbList.Where(ab => ab.Name.StartsWith(UmaDatabaseController.MotionPath)).ToList();
        AbEffect = AbList.Where(ab => ab.Name.StartsWith(UmaDatabaseController.EffectPath)).ToList();
        AbSounds = AbList.Where(ab => ab.Name.EndsWith(".awb") || ab.Name.EndsWith(".acb")).ToList();
    }

    private IEnumerator Start()
    {
        Dictionary<int, string> enNames = new Dictionary<int, string>();
        yield return UmaViewerDownload.DownloadText("https://www.tracenacademy.com/api/BasicCharaDataInfo", txt =>
        {
            if (string.IsNullOrEmpty(txt)) return;
            var umaData = JArray.Parse(txt);
            foreach (var item in umaData)
            {
                if (!enNames.ContainsKey((int)item["charaId"]))
                {
                    enNames.Add((int)item["charaId"], item["charaNameEnglish"].ToString());
                }
            }
        });

        var UmaCharaData = UmaDatabaseController.Instance.CharaData;
        foreach (var item in UmaCharaData)
        {
            var id = Convert.ToInt32(item["id"]);
            if (!Characters.Where(c => c.Id == id).Any())
            {
                Characters.Add(new CharaEntry()
                {
                    Name = enNames.ContainsKey(id) ? enNames[id] : item["charaname"].ToString(),
                    Icon = UmaViewerBuilder.Instance.LoadCharaIcon(id.ToString()),
                    Id = id,
                    ThemeColor = "#"+item["ui_nameplate_color_1"].ToString()
                });
            }
        }

        UI.LoadModelPanels();
        UI.LoadMiniModelPanels();
        UI.LoadPropPanel();
        UI.LoadMapPanel();

        var livesettings = AbList.FirstOrDefault(a => a.Name.Equals("livesettings"));
        if (File.Exists(UmaDatabaseController.GetABPath(livesettings)))
        {
            StartCoroutine(UmaViewerDownload.DownloadText("https://www.tracenacademy.com/api/BasicLiveDataInfo", txt =>
            {
                var UmaLiveData = JArray.Parse(txt);
                var asset = AbList.FirstOrDefault(a => a.Name.Equals("livesettings"));
                if (asset != null)
                {
                    string filePath = UmaDatabaseController.GetABPath(asset);
                    if (File.Exists(filePath))
                    {
                        AssetBundle bundle = AssetBundle.LoadFromFile(filePath);
                        foreach (var item in UmaLiveData)
                        {
                            if (!Lives.Where(c => c.MusicId == (int)item["musicId"]).Any())
                            {
                                if (bundle.Contains((string)item["musicId"]))
                                {
                                    TextAsset liveData = bundle.LoadAsset<TextAsset>((string)item["musicId"]);

                                    Lives.Add(new LiveEntry(liveData.text)
                                    {
                                        MusicId = (int)item["musicId"],
                                        SongName = (string)item["songName"]
                                    });
                                }
                            }
                        }
                        UI.LoadLivePanels();
                    }
                }
            }));
        }
        else
        yield return livesettings.LoadAssetBundle(Instance.gameObject, (value) => {
            if (value)
            {
                StartCoroutine(UmaViewerDownload.DownloadText("https://www.tracenacademy.com/api/BasicLiveDataInfo", txt =>
                {
                    var UmaLiveData = JArray.Parse(txt);
                    var asset = AbList.FirstOrDefault(a => a.Name.Equals("livesettings"));
                    if (asset != null)
                    {
                        string filePath = UmaDatabaseController.GetABPath(asset);
                        if (File.Exists(filePath))
                        {
                            AssetBundle bundle = AssetBundle.LoadFromFile(filePath);
                            foreach (var item in UmaLiveData)
                            {
                                if (!Lives.Where(c => c.MusicId == (int)item["musicId"]).Any())
                                {
                                    if (bundle.Contains((string)item["musicId"]))
                                    {
                                        TextAsset liveData = bundle.LoadAsset<TextAsset>((string)item["musicId"]);

                                        Lives.Add(new LiveEntry(liveData.text)
                                        {
                                            MusicId = (int)item["musicId"],
                                            SongName = (string)item["songName"]
                                        });
                                    }
                                }
                            }
                            UI.LoadLivePanels();
                        }
                    }
                }));
            }
        });
    }

    public void UnloadByDependency(GameObject go)
    {
        foreach(var bundle in LoadedBundles)
        {
            bundle.UnloadAssetBundle(go);
        }
    }

    public void OpenUrl(string url)
    {
        Application.OpenURL(url);
    }
}

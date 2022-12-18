using RootMotion.Dynamics;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Gallop;
using static UnityEngine.EventSystems.EventTrigger;
using System.Linq;
using static UnityEngine.Rendering.VirtualTexturing.Debugging;

public class UmaDatabaseEntry
{
    public UmaFileType Type;
    public string Name;
    public string Url;
    public string Checksum;
    public string Prerequisites;

    public bool Loaded => AssetBundle != null;
    public AssetBundle AssetBundle;
    public List<object> ReferencedBy = new List<object>();

    public IEnumerator LoadAssetBundle(GameObject caller, System.Action<AssetBundle> callback = null)
    {
        var Main = UmaViewerMain.Instance;
        var entry = this;
        if (Loaded)
        {
            if(!ReferencedBy.Contains(caller))
                ReferencedBy.Add(caller);
            callback?.Invoke(AssetBundle);
            yield break;
        }

        if (!string.IsNullOrEmpty(entry.Prerequisites))
        {
            foreach (string prerequisite in entry.Prerequisites.Split(';'))
            {
                if (prerequisite.StartsWith(UmaDatabaseController.CharaPath))
                {
                    yield return Main.AbChara.FirstOrDefault(ab => ab.Name == prerequisite)?.LoadAssetBundle(caller);
                }
                else if (prerequisite.StartsWith(UmaDatabaseController.MotionPath))
                {
                    yield return Main.AbMotions.FirstOrDefault(ab => ab.Name == prerequisite)?.LoadAssetBundle(caller);
                }
                else
                    yield return Main.AbList.FirstOrDefault(ab => ab.Name == prerequisite)?.LoadAssetBundle(caller);
            }
        }

        yield return UmaViewerDownload.DownOrLoadAsset(caller, entry, (bundle) =>
        {
            AssetBundle = bundle;
        });
        Main.LoadedBundles.Add(this);
        ReferencedBy.Add(caller);
        UmaViewerUI.Instance.LoadedAssetsAdd(entry);
        callback?.Invoke(AssetBundle);
    }

    public void UnloadAssetBundle(GameObject caller)
    {
        if (ReferencedBy.Contains(caller))
        {
            ReferencedBy.Remove(caller);
            if (ReferencedBy.Where(c => c != null).Count() == 0)
            {
                UmaViewerUI.Instance.LoadedAssetsRemove(this);
                AssetBundle.Unload(true);
                AssetBundle = null;
            }
        }
    }
}

public class UmaCharaData
{
    public int id;
    public string tail_model_id;
}

public class UmaHeadData
{
    public int id;
    public string costumeId;
    public int tailId;
}

public class UmaLyricsData
{
    public float time;
    public string text;
}

[System.Serializable]
public class EmotionKey
{
    public FacialMorph morph;
    public float weight;
}

[System.Serializable]
public class FaceTypeData
{
    public string label, eyebrow_l, eyebrow_r, eye_l, eye_r, mouth, inverce_face_type;
    public int mouth_shape_type, set_face_group;

    public FaceEmotionKeyTarget target;

    public List<EmotionKey> emotionKeys;

    private float _weight;
    public float Weight
    {
        get
        {
            return _weight;
        }
        set
        {
            _weight = value;
            target.UpdateAllFacialKeyTargets();
        }
    }
}

[System.Serializable]
public class CharaEntry
{
    public string Name;
    public Sprite Icon;
    public int Id;
    public string ThemeColor;
}

[System.Serializable]
public class LiveEntry
{
    public int MusicId;
    public string SongName;
    public string BackGroundId;
    public List<string[]> LiveSettings = new List<string[]>();

    public LiveEntry(string data)
    {
        string[] lines = data.Split("\n"[0]);
        for (int i = 1; i < lines.Length; i++)
        {
            LiveSettings.Add(lines[i].Split(','));
        }
        BackGroundId = LiveSettings[1][2];
    }
}

public enum UmaFileType
{
    _3d_cutt,
    announce,
    atlas,
    bg,
    chara,
    font,
    gacha,
    gachaselect,
    guide,
    home,
    imageeffect,
    item,
    lipsync,
    live,
    loginbonus,
    manifest,
    manifest2,
    manifest3,
    master,
    minigame,
    mob,
    movie,
    outgame,
    paddock,
    race,
    shader,
    single,
    sound,
    story,
    storyevent,
    supportcard,
    uianimation,
    transferevent,
    teambuilding,
    challengematch,
    collectevent
}

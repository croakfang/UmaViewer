using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

public class UmaViewerDownload : MonoBehaviour
{
    private static string _assetsUrl = "https://prd-storage-umamusume.akamaized.net/dl/resources/Windows/assetbundles/";
    private static UmaViewerMain Main => UmaViewerMain.Instance;
    private static UmaViewerBuilder Builder => UmaViewerBuilder.Instance;
    private static UmaViewerUI UI => UmaViewerUI.Instance;

    public static Dictionary<string, Coroutine> AwaitCoroutines = new Dictionary<string, Coroutine>();
    public static Dictionary<string, Coroutine> DownloadCoroutines = new Dictionary<string, Coroutine>();

    public static IEnumerator DownloadText(string url, System.Action<string> callback)
    {
        if (PlayerPrefs.GetString(url, "")=="")
        {
            UnityWebRequest www = UnityWebRequest.Get(url);
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.Log(www.error);
            }
            else
            {
                PlayerPrefs.SetString(url, www.downloadHandler.text);
                callback(www.downloadHandler.text);
            }
        }
        else
        {
            callback(PlayerPrefs.GetString(url, ""));
        }

    }

    public static void DownOrLoadAsset(UmaDatabaseEntry entry, bool IsSubAsset = false)
    {
        if (Main.LoadedBundles.ContainsKey(entry.Name)
            || Main.DownloadingBundles.Contains(entry.Name)
        ) return;

        foreach (var req in entry.Prerequisites.Split(';'))                             //If any dependencies are not loaded, stop loading this and wait for them to finish downloading
        {
            if (Main.DownloadingBundles.Contains(req))
            {
                AwaitCoroutines.Add(entry.Name, Main.StartCoroutine(AwaitDependencies(entry, (value) =>
                {
                    AwaitCoroutines.Remove(entry.Name);
                    DownOrLoadAsset(value, IsSubAsset);
                })));
                Debug.Log(entry.Url + " failed check");
                return;
            }
        }

        string filePath = UmaDatabaseController.GetABPath(entry);
        if (File.Exists(filePath))                                                      //Load file if it exists
        {
            AssetBundle bundle = AssetBundle.LoadFromFile(filePath);
            if (bundle == null)
            {
                Debug.Log(filePath + " exists and doesn't work");
                return;
            }
            Main.LoadedBundles.Add(entry.Name, bundle);
            Main.AwaitingLoadBundles.Remove(entry.Name);
            UI.LoadedAssetsAdd(entry);
            Builder.LoadBundle(bundle, IsSubAsset);
        }

        else if(!Main.DownloadingBundles.Contains(entry.Name))                           //Download it if not
        {
            Main.DownloadingBundles.Add(entry.Name);
            DownloadCoroutines.Add(entry.Name, UmaViewerMain.Instance.StartCoroutine(DownloadAsset(entry, (value) =>
            {
                DownloadCoroutines.Remove(entry.Name);
                Main.DownloadingBundles.Remove(entry.Name);
                if (value)
                {
                    DownOrLoadAsset(entry, IsSubAsset);
                }
                else
                {
                    Debug.LogError($"{entry.Name} - {filePath} download failed");
                }
            })));
        }
    }

    public static IEnumerator AwaitDependencies(UmaDatabaseEntry entry, System.Action<UmaDatabaseEntry> callback)
    {
        var dependencies = entry.Prerequisites.Split(';').ToList();
        while(dependencies.Count > 0)
        {
            for(int i = dependencies.Count - 1; i >= 0; i--)
            {
                if (!Main.DownloadingBundles.Contains(dependencies[i]))
                {
                    dependencies.RemoveAt(i);
                }
            }
            yield return 0;
        }
        callback(entry);
    }

    public static IEnumerator DownloadAsset(UmaDatabaseEntry entry, System.Action<bool> callback)
    {
        using (UnityWebRequest www = UnityWebRequest.Get(_assetsUrl + $"{entry.Url.Substring(0, 2)}/{entry.Url}"))
        {
            string filePath = UmaDatabaseController.GetABPath(entry);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            yield return www.SendWebRequest();
            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(www.error);
                callback(false);
            }
            else
            {
                Debug.Log("saving " + entry.Url);
                File.WriteAllBytes(filePath, www.downloadHandler.data);
                callback(true);
            }
        }
    }
}

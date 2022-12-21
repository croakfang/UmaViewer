using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

public class UmaViewerDownload : MonoBehaviour
{
    private static string _assetsUrl = "https://prd-storage-umamusume.akamaized.net/dl/resources/Windows/assetbundles/";
    private static UmaViewerMain Main => UmaViewerMain.Instance;
    private static UmaViewerBuilder Builder => UmaViewerBuilder.Instance;
    private static UmaViewerUI UI => UmaViewerUI.Instance;

    public static string MANIFEST_ROOT_URL = "https://prd-storage-app-umamusume.akamaized.net/dl/resources/Manifest";
    public static string GENERIC_BASE_URL = "https://prd-storage-umamusume.akamaized.net/dl/resources/Generic";
    public static string ASSET_BASE_URL = "https://prd-storage-umamusume.akamaized.net/dl/resources/Windows/assetbundles/";

    public static IEnumerator DownloadText(string url, System.Action<string> callback)
    {
        UnityWebRequest www = UnityWebRequest.Get(url);
        www.timeout = 3;
        yield return www.SendWebRequest();
        
        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.Log(www.error);
            callback("");
        }
        else
        {
            callback(www.downloadHandler.text);
        }
    }

    public static IEnumerator DownOrLoadAsset(GameObject caller, UmaDatabaseEntry entry, System.Action<AssetBundle> callback = null)
    {
        if (entry == null) yield break;
        if (entry.Loaded)
        {
            if (!entry.ReferencedBy.Contains(caller))
                entry.ReferencedBy.Add(caller);
            callback?.Invoke(entry.AssetBundle);
            yield break;
        }

        var dependencies = entry.Prerequisites.Split(new char[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries).ToList();
        Main.DownloadingBundles.AddRange(dependencies);
        foreach (var dependency in dependencies)
        {
            var dep = Main.AbList.FirstOrDefault(a => a.Name == dependency);
            yield return dep.LoadAssetBundle(caller);
            Main.Downloaded += 1;
        }

        string filePath = UmaDatabaseController.GetABPath(entry);
        if (!File.Exists(filePath))
        {
            bool result = false;
            yield return DownloadAsset(entry, (value) =>
            {
                result = value;
            });
            if (!result)
            {
                Debug.LogError($"{entry.Name} - {filePath} download failed");
                callback.Invoke(null);
                yield break;
            }
        }

        AssetBundle bundle = AssetBundle.LoadFromFile(filePath);
        if (bundle == null)
        {
            Debug.LogError(filePath + " exists but doesn't work");
            callback?.Invoke(null);
            yield break;
        }
        if (bundle.name == "shader.a")
        {
            Builder.LoadShaders(bundle);
        }
        callback?.Invoke(bundle);
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

    public static string GetManifestRequestUrl(string hash)
    {
        return $"{MANIFEST_ROOT_URL}/{hash.Substring(0, 2)}/{hash}";
    }

    public static string GetGenericRequestUrl(string hash)
    {
        return $"{GENERIC_BASE_URL}/{hash.Substring(0, 2)}/{hash}";
    }
    
    public static string GetAssetRequestUrl(string hash)
    {
        return $"{ASSET_BASE_URL}/{hash.Substring(0, 2)}/{hash}";
    }
}

using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace CodeLittleDog.TableTool
{
    /// <summary>
    /// 跨平台文本加载器：用 UnityWebRequest 从 StreamingAssets 读文本。
    /// 解决：Android APK 内文件、WebGL StreamingAssets(远程 URL)、iOS 等 File.IO 不可用场景。
    /// 桌面平台同样可用(本地 file://)。
    /// </summary>
    public static class TableWebLoader
    {
        private class Runner : MonoBehaviour { }
        private static Runner _runner;

        private static Runner GetRunner()
        {
            if (_runner != null) return _runner;
            var go = new GameObject("[TableWebLoader]");
            _runner = go.AddComponent<Runner>();
            UnityEngine.Object.DontDestroyOnLoad(go);
            return _runner;
        }

        /// <summary>从 StreamingAssets 异步加载文本。relPath 相对 StreamingAssets(如 "Tables/Player.csv")。
        /// 注意：xlsx 在 WebGL/iOS 无法解析，请导出 csv 后用本方法。</summary>
        public static void LoadStreamingAssetText(string relPath, Action<string> onComplete)
        {
            // 规范化分隔符
            relPath = relPath.Replace('\\', '/');
            string url = Path.Combine(Application.streamingAssetsPath, relPath).Replace('\\', '/');
            GetRunner().StartCoroutine(LoadRoutine(url, onComplete));
        }

        /// <summary>协程版本：可由调用方 yield return。</summary>
        public static IEnumerator LoadStreamingAssetTextCoroutine(string relPath, Action<string> onComplete)
        {
            relPath = relPath.Replace('\\', '/');
            string url = Path.Combine(Application.streamingAssetsPath, relPath).Replace('\\', '/');
            yield return LoadRoutine(url, onComplete);
        }

        private static IEnumerator LoadRoutine(string url, Action<string> onComplete)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[TableWebLoader] 加载失败: {url} - {req.error}");
                    onComplete?.Invoke(null);
                    yield break;
                }
                onComplete?.Invoke(req.downloadHandler.text);
            }
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Unity.Connect.Share.Editor
{
    /// <summary>
    /// Performs operations according to the state of the application
    /// </summary>
    public class ShareMiddleware
    {
        const string WebglSharingFile = "webgl_sharing";
        const string ZipName = "connectwebgl.zip";
        const string UploadEndpoint = "/api/webgl/upload";
        const string QueryProgressEndpoint = "/api/webgl/progress";
        const string UndefinedGUID = "UNDEFINED_GUID";
        const int ZipFileLimitBytes = 500 * 1024 * 1024;

        static EditorCoroutine waitUntilUserLogsInRoutine;
        static UnityWebRequest uploadRequest;

        /// <summary>
        /// Creates a new middleware according to the state
        /// </summary>
        /// <returns></returns>
        public static Middleware<AppState> Create()
        {
            return (store) => (next) => (action) =>
            {
                var result = next(action);

                switch (action)
                {
                    case ShareStartAction shared: ZipAndShare(shared.title, shared.buildPath, store); break;
                    case UploadStartAction upload: Upload(store, upload.buildGUID); break;
                    case QueryProgressAction query: CheckProgress(store, query.key); break;
                    case StopUploadAction stopUpload: StopUploadAction(); break;
                    case NotLoginAction login: CheckLoginStatus(store); break;
                }
                return result;
            };
        }

        private static void ZipAndShare(string title, string buildPath, Store<AppState> store)
        {
            store.Dispatch(new TitleChangeAction { title = title });

            if (!ShareUtils.BuildIsValid(buildPath))
            {
                store.Dispatch(new OnErrorAction { errorMsg = Localization.Tr("ERROR_BUILD_ABSENT") });
                return;
            }

            if (!Zip(store, buildPath)) { return; }
            string GUIDPath = Path.Combine(buildPath, "GUID.txt");
            if (File.Exists(GUIDPath))
            {
                store.Dispatch(new UploadStartAction() { buildGUID = File.ReadAllText(GUIDPath) });
                return;
            }
            Debug.LogWarningFormat("Missing GUID file for {0}, consider deleting the build and making a new one through the WebGL Publisher", buildPath);
            store.Dispatch(new UploadStartAction() { buildGUID = UndefinedGUID });
        }

        private static bool Zip(Store<AppState> store, string buildOutputDir)
        {
            var projectDir = Directory.GetParent(Application.dataPath).FullName;
            var destPath = Path.Combine(projectDir, ZipName);

            File.Delete(destPath);

            ZipFile.CreateFromDirectory(buildOutputDir, destPath);
            FileInfo fileInfo = new FileInfo(destPath);

            if (fileInfo.Length > ZipFileLimitBytes)
            {
                store.Dispatch(new OnErrorAction { errorMsg = string.Format(Localization.Tr("ERROR_MAX_SIZE"), ShareUtils.FormatBytes(ZipFileLimitBytes))});
                return false;
            }
            store.Dispatch(new ZipPathChangeAction { zipPath = destPath });
            return true;
        }

        private static void Upload(Store<AppState> store, string buildGUID)
        {
            var token = UnityConnectSession.instance.GetAccessToken();
            if (token.Length == 0)
            {
                CheckLoginStatus(store);
                return;
            }

            string path = store.state.zipPath;
            string title = string.IsNullOrEmpty(store.state.title) ? ShareUtils.DefaultGameName : store.state.title;

            string baseUrl = GetAPIBaseUrl();
            string projectId = GetProjectId();
            var formSections = new List<IMultipartFormSection>();

            formSections.Add(new MultipartFormDataSection("title", title));

            if (buildGUID.Length > 0)
            {
                formSections.Add(new MultipartFormDataSection("buildGUID", buildGUID));
            }

            if (projectId.Length > 0)
            {
                formSections.Add(new MultipartFormDataSection("projectId", projectId));
            }

            formSections.Add(new MultipartFormFileSection("file",
                File.ReadAllBytes(path), Path.GetFileName(path), "application/zip"));

            uploadRequest = UnityWebRequest.Post(baseUrl + UploadEndpoint, formSections);
            uploadRequest.SetRequestHeader("Authorization", $"Bearer {token}");
            uploadRequest.SetRequestHeader("X-Requested-With", "XMLHTTPREQUEST");

            var op = uploadRequest.SendWebRequest();

            EditorCoroutineUtility.StartCoroutineOwnerless(UpdateProgress(store, uploadRequest));

            op.completed += operation =>
            {
#if UNITY_2020
                if ((uploadRequest.result == UnityWebRequest.Result.ConnectionError)
                    || (uploadRequest.result == UnityWebRequest.Result.ProtocolError))
#else
                if (uploadRequest.isNetworkError || uploadRequest.isHttpError)
#endif
                {
                    if (uploadRequest.error != "Request aborted")
                    {
                        store.Dispatch(new OnErrorAction { errorMsg = uploadRequest.error });
                    }
                }
                else
                {
                    var response = JsonUtility.FromJson<UploadResponse>(op.webRequest.downloadHandler.text);
                    if (!string.IsNullOrEmpty(response.key))
                    {
                        store.Dispatch(new QueryProgressAction { key = response.key });
                    }
                }
            };
        }

        private static void StopUploadAction()
        {
            if (uploadRequest == null) { return; }
            uploadRequest.Abort();
        }

        private static void CheckProgress(Store<AppState> store, string key)
        {
            var token = UnityConnectSession.instance.GetAccessToken();
            if (token.Length == 0)
            {
                CheckLoginStatus(store);
                return;
            }

            key = key ?? store.state.key;
            string baseUrl = GetAPIBaseUrl();

            var uploadRequest = UnityWebRequest.Get($"{baseUrl + QueryProgressEndpoint}?key={key}");
            uploadRequest.SetRequestHeader("Authorization", $"Bearer {token}");
            uploadRequest.SetRequestHeader("X-Requested-With", "XMLHTTPREQUEST");
            var op = uploadRequest.SendWebRequest();

            op.completed += operation =>
            {
#if UNITY_2020
                if ((uploadRequest.result == UnityWebRequest.Result.ConnectionError)
                    || (uploadRequest.result == UnityWebRequest.Result.ProtocolError))
#else
                if (uploadRequest.isNetworkError || uploadRequest.isHttpError)
#endif
                {
                    AnalyticsHelper.UploadCompleted(UploadResult.Failed);
                    Debug.LogError(uploadRequest.error);
                    StopUploadAction();
                    return;
                }
                var response = JsonUtility.FromJson<GetProgressResponse>(op.webRequest.downloadHandler.text);

                store.Dispatch(new QueryProgressResponseAction { response = response });
                if (response.progress == 100 || !string.IsNullOrEmpty(response.error))
                {
                    SaveProjectID(response.projectId);
                    return;
                }
                EditorCoroutineUtility.StartCoroutineOwnerless(RefreshProcessingProgress(1.5f, store));
            };
        }

        private static void SaveProjectID(string projectId)
        {
            if (projectId.Length == 0) { return; }

            StreamWriter writer = new StreamWriter(WebglSharingFile, false);
            writer.Write(projectId);
            writer.Close();
        }

        private static string GetProjectId()
        {
            if (!File.Exists(WebglSharingFile)) { return string.Empty; }

            var reader = new StreamReader(WebglSharingFile);
            var projectId = reader.ReadLine();

            reader.Close();
            return projectId;
        }

        private static IEnumerator UpdateProgress(Store<AppState> store, UnityWebRequest request)
        {
            EditorWaitForSeconds waitForSeconds = new EditorWaitForSeconds(0.5f);
            while (true)
            {
                if (request.isDone) { break; }

                int progress = (int)(Mathf.Clamp(request.uploadProgress, 0, 1) * 100);
                store.Dispatch(new UploadProgressAction { progress = progress });
                yield return waitForSeconds;
            }
            yield return null;
        }

        private static void CheckLoginStatus(Store<AppState> store)
        {
            var token = UnityConnectSession.instance.GetAccessToken();
            if (token.Length != 0)
            {
                store.Dispatch(new LoginAction());
                return;
            }

            if (waitUntilUserLogsInRoutine != null) { return; }

            waitUntilUserLogsInRoutine = EditorCoroutineUtility.StartCoroutineOwnerless(WaitUntilUserLogsIn(2f, store));
        }

        private static IEnumerator WaitUntilUserLogsIn(float refreshDelay, Store<AppState> store)
        {
            EditorWaitForSeconds waitAmount = new EditorWaitForSeconds(refreshDelay);
            while (EditorWindow.HasOpenInstances<ShareWindow>())
            {
                yield return waitAmount; //Debug.LogError("Rechecking login in " + refreshDelay);
                if (UnityConnectSession.instance.GetAccessToken().Length != 0)
                {
                    store.Dispatch(new LoginAction()); //Debug.LogError("Connected!");
                    waitUntilUserLogsInRoutine = null;
                    yield break;
                }
            }
            waitUntilUserLogsInRoutine = null; //Debug.LogError("Window closed");
        }

        private static IEnumerator RefreshProcessingProgress(float refreshDelay, Store<AppState> store)
        {
            EditorWaitForSeconds waitAmount = new EditorWaitForSeconds(refreshDelay);
            yield return waitAmount;
            store.Dispatch(new QueryProgressAction());
        }

        private static string GetAPIBaseUrl()
        {
            string env = UnityConnectSession.instance.GetEnvironment();
            if (env == "staging")
            {
                return "https://connect-staging.unity.com";
            }
            else if (env == "dev")
            {
                return "https://connect-dev.unity.com";
            }

            return "https://play.unity.com";
        }
    }


    /// <summary>
    /// The response received on an Upload request
    /// </summary>
    [Serializable]
    public class UploadResponse
    {
        /// <summary>
        /// The key that identifies the uploaded project
        /// </summary>
        public string key;
    }

    /// <summary>
    /// A response that contains data about upload progress
    /// </summary>
    [Serializable]
    public class GetProgressResponse
    {
        /// <summary>
        /// ID of the project
        /// </summary>
        public string projectId;

        /// <summary>
        /// URL of the project
        /// </summary>
        public string url;

        /// <summary>
        /// Upload progress
        /// </summary>
        public int progress;

        /// <summary>
        /// Error which occured
        /// </summary>
        public string error;
    }
}

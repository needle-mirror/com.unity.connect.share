using System.Linq;
using UnityEngine;

namespace Unity.Connect.Share.Editor
{
    /// <summary>
    /// base class for all actions
    /// </summary>
    public class ShareAction {}

    /// <summary>
    /// Dispatch this action to start the Publishing process
    /// </summary>
    public class ShareStartAction : ShareAction
    {
        /// <summary>
        /// Title of the build
        /// </summary>
        public string title;
        /// <summary>
        /// Path where the build is located
        /// </summary>
        public string buildPath;
    }

    /// <summary>
    /// Dispatch this action to notify about the end of the build process
    /// </summary>
    public class BuildFinishAction : ShareAction
    {
        /// <summary>
        /// Output directory of the build
        /// </summary>
        public string outputDir;

        /// <summary>
        /// GUID of the build
        /// </summary>
        public string buildGUID;
    }

    /// <summary>
    /// Dispatch this action to notify about the end of the Zipping process
    /// </summary>
    public class ZipPathChangeAction : ShareAction
    {
        /// <summary>
        /// Path of the zipped build
        /// </summary>
        public string zipPath;
    }

    /// <summary>
    /// Dispatch this action to start the Upload process
    /// </summary>
    public class UploadStartAction : ShareAction
    {
        /// <summary>
        /// GUID of the build
        /// </summary>
        public string buildGUID;
    }

    /// <summary>
    /// Dispatch this action to query progress data about the Upload process
    /// </summary>
    public class UploadProgressAction : ShareAction
    {
        /// <summary>
        /// The progress made until now
        /// </summary>
        public int progress;
    }

    /// <summary>
    /// Dispatch this action to query progress data
    /// </summary>
    public class QueryProgressAction : ShareAction
    {
        /// <summary>
        /// A key that identifies the action
        /// </summary>
        public string key;
    }

    /// <summary>
    /// Dispatch this action to query progress response data
    /// </summary>
    public class QueryProgressResponseAction : ShareAction
    {
        /// <summary>
        /// The response
        /// </summary>
        public GetProgressResponse response;
    }

    /// <summary>
    /// Dispatch this action to change the title of the build
    /// </summary>
    public class TitleChangeAction : ShareAction
    {
        /// <summary>
        /// The new title
        /// </summary>
        public string title;
    }

    /// <summary>
    /// Dispatch this action to destroy the app
    /// </summary>
    public class DestroyAction : ShareAction {}

    /// <summary>
    /// Dispatch this action to notify an error
    /// </summary>
    public class OnErrorAction : ShareAction
    {
        /// <summary>
        /// The error message
        /// </summary>
        public string errorMsg;
    }

    /// <summary>
    /// Dispatch this action to stop the Upload process
    /// </summary>
    public class StopUploadAction : ShareAction {}

    /// <summary>
    /// Dispatch this action to Notify that the user is not logged in
    /// </summary>
    public class NotLoginAction : ShareAction {}

    /// <summary>
    /// Dispatch this action to Notify that the user logged in
    /// </summary>
    public class LoginAction : ShareAction {}

    /// <summary>
    /// Processes the state of the app
    /// </summary>
    public class ShareReducer
    {
        /// <summary>
        /// Processes the state of the app according to an action
        /// </summary>
        /// <param name="old">old state</param>
        /// <param name="action">dispatched action</param>
        /// <returns>an updated AppState</returns>
        public static AppState reducer(AppState old, object action)
        {
            switch (action)
            {
                case BuildFinishAction build:
                    return old.CopyWith(
                        buildOutputDir: build.outputDir,
                        buildGUID: build.buildGUID
                    );

                case ZipPathChangeAction zip:
                    return old.CopyWith(
                        zipPath: zip.zipPath,
                        step: ShareStep.Zip
                    );

                case UploadStartAction upload:
                    AnalyticsHelper.UploadStarted();
                    return old.CopyWith(step: ShareStep.Upload);

                case QueryProgressAction query:

                    return old.CopyWith(
                        step: ShareStep.Process,
                        key: query.key
                    );

                case UploadProgressAction upload:
                    ShareWindow.FindInstance()?.OnUploadProgress(upload.progress);
                    return old;

                case QueryProgressResponseAction queryResponse:
                    ShareStep? step = null;
                    if (queryResponse.response.progress == 100)
                    {
                        step = ShareStep.Idle;
                    }

                    ShareWindow.FindInstance()?.OnProcessingProgress(queryResponse.response.progress);
                    return old.CopyWith(url: queryResponse.response.url, step: step);

                case TitleChangeAction titleChangeAction: return old.CopyWith(title: titleChangeAction.title);

                case DestroyAction destroyAction: return new AppState(buildOutputDir: old.buildOutputDir, buildGUID: old.buildGUID);

                case OnErrorAction errorAction: return old.CopyWith(errorMsg: errorAction.errorMsg);

                case StopUploadAction stopUploadAction: return new AppState(buildOutputDir: old.buildOutputDir, buildGUID: old.buildGUID);

                case NotLoginAction login: return old.CopyWith(step: ShareStep.Login);

                case LoginAction login: return old.CopyWith(step: ShareStep.Idle);
            }
            return old;
        }
    }
}

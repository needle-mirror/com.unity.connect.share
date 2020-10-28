using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.Connect.Share.Editor
{
    /// <summary>
    /// A collection of utility methods used by the Share Package
    /// </summary>
    public static class ShareUtils
    {
        /// <summary>
        /// The max number of builds that can be displayed in the UI at the same time
        /// </summary>
        public const int MaxDisplayedBuilds = 10;

        /// <summary>
        /// the default name of every uploaded game
        /// </summary>
        public const string DefaultGameName = "Untitled";
        const string ProjectVersionRegex = "^\\d{4}\\.\\d{1}\\Z";

        /// <summary>
        /// Returns a list of MaxDisplayedBuilds build directiories, filling the gaps with empty paths
        /// </summary>
        /// <returns>A list of MaxDisplayedBuilds build directiories, filling the gaps with empty paths</returns>
        public static List<string> GetAllBuildsDirectories()
        {
            List<string> result = Enumerable.Repeat(string.Empty, MaxDisplayedBuilds).ToList();
            string path = GetEditorPreference("buildOutputDirList");

            if (string.IsNullOrEmpty(path)) { return result; }

            List<string> existingPaths = path.Split(';').ToList();
            for (int i = 0; i < existingPaths.Count; i++)
            {
                result[i] = existingPaths[i];
            }
            return result;
        }

        /// <summary>
        /// Adds a directory to the list of tracked builds
        /// </summary>
        /// <param name="buildPath">The path to a build</param>
        public static void AddBuildDirectory(string buildPath)
        {
            string path = GetEditorPreference("buildOutputDirList");
            List<string> buildPaths = path.Split(';').ToList();
            if (buildPaths.Contains(buildPath)) { return; }

            while (buildPaths.Count < MaxDisplayedBuilds)
            {
                buildPaths.Add(string.Empty);
            }

            //Left Shift
            for (int i = MaxDisplayedBuilds - 1; i > 0; i--)
            {
                buildPaths[i] = buildPaths[i - 1];
            }

            buildPaths[0] = buildPath;
            SetEditorPreference("buildOutputDirList", string.Join(";", buildPaths));
        }

        /// <summary>
        /// Removes a directory from the list of tracked builds
        /// </summary>
        /// <param name="buildPath">The path to a build</param>
        public static void RemoveBuildDirectory(string buildPath)
        {
            List<string> buildPaths = GetEditorPreference("buildOutputDirList").Split(';').ToList();

            buildPaths.Remove(buildPath);

            while (buildPaths.Count < MaxDisplayedBuilds)
            {
                buildPaths.Add(string.Empty);
            }

            SetEditorPreference("buildOutputDirList", string.Join(";", buildPaths));
        }

        /// <summary>
        /// Is a valid build tracked?
        /// </summary>
        /// <returns></returns>
        public static bool ValidBuildExists() => !string.IsNullOrEmpty(GetFirstValidBuildPath());

        /// <summary>
        /// Returns the first valid build path among all builds tracked
        /// </summary>
        /// <returns></returns>
        public static string GetFirstValidBuildPath() => GetAllBuildsDirectories().FirstOrDefault(BuildIsValid);

        /// <summary>
        /// Determines whether a build is valid or not
        /// </summary>
        /// <param name="buildPath">The path to a build</param>
        /// <returns>True if the build follows the standard for a supported Unity version, false otherwise</returns>
        public static bool BuildIsValid(string buildPath)
        {
            if (string.IsNullOrEmpty(buildPath)) { return false; }

            string unityVersionOfBuild = GetUnityVersionOfBuild(buildPath); //UnityEngine.Debug.Log("unity version: " + unityVersionOfBuild);
            if (string.IsNullOrEmpty(unityVersionOfBuild)) { return false; }

            string descriptorFileName = buildPath.Split('/').Last();

            switch (unityVersionOfBuild)
            {
                case "2019.3": return BuildIsCompatibleFor2019_3(buildPath, descriptorFileName);
                case "2020.2": return BuildIsCompatibleFor2020_2(buildPath, descriptorFileName);
                default: return true; //if we don't know the exact build structure for other unity versions, we assume the build is valid
            }
        }

        /// <summary>
        /// Determines whether a build is valid or not, according to Unity 2019.3 WebGL build standard output
        /// </summary>
        /// <param name="buildPath">The path to a build</param>
        /// <param name="descriptorFileName"></param>
        /// <returns>True if the build follows the standard for a supported Unity version, false otherwise</returns>
        public static bool BuildIsCompatibleFor2019_3(string buildPath, string descriptorFileName)
        {
            return File.Exists(Path.Combine(buildPath, string.Format("Build/{0}.data.unityweb", descriptorFileName)))
                && File.Exists(Path.Combine(buildPath, string.Format("Build/{0}.wasm.code.unityweb", descriptorFileName)))
                && File.Exists(Path.Combine(buildPath, string.Format("Build/{0}.wasm.framework.unityweb", descriptorFileName)))
                && File.Exists(Path.Combine(buildPath, string.Format("Build/{0}.json", descriptorFileName)))
                && File.Exists(Path.Combine(buildPath, string.Format("Build/UnityLoader.js", descriptorFileName)));
        }

        /// <summary>
        /// Determines whether a build is valid or not, according to Unity 2020.2 WebGL build standard output
        /// </summary>
        /// <param name="buildPath">The path to a build</param>
        /// <param name="descriptorFileName"></param>
        /// <returns>True if the build follows the standard for a supported Unity version, false otherwise</returns>
        public static bool BuildIsCompatibleFor2020_2(string buildPath, string descriptorFileName)
        {
            string buildFilesPath = Path.Combine(buildPath, "Build/");
            return Directory.GetFiles(buildFilesPath, string.Format("{0}.data.*", descriptorFileName)).Length > 0
                && Directory.GetFiles(buildFilesPath, string.Format("{0}.framework.js.*", descriptorFileName)).Length > 0
                && File.Exists(Path.Combine(buildPath, string.Format("Build/{0}.loader.js", descriptorFileName)))
                && Directory.GetFiles(buildFilesPath, string.Format("{0}.wasm.*", descriptorFileName)).Length > 0;
        }

        /// <summary>
        /// Gets the Unity version with which a WebGL build was made
        /// </summary>
        /// <param name="buildPath">The path to a build</param>
        /// <returns></returns>
        public static string GetUnityVersionOfBuild(string buildPath)
        {
            if (string.IsNullOrEmpty(buildPath)) { return string.Empty; }

            string versionFile = Path.Combine(buildPath, "ProjectVersion.txt");
            if (!File.Exists(versionFile)) { return string.Empty; }

            string version = File.ReadAllLines(versionFile)[0].Split(' ')[1].Substring(0, 6); //The row is something like: m_EditorVersion: 2019.3.4f1, so it will return 2019.3
            return Regex.IsMatch(version, ProjectVersionRegex) ?  version : string.Empty;
        }

        /// <summary>
        /// Sets an editor preference for the project
        /// </summary>
        /// <param name="key">ID of the preference</param>
        /// <param name="value">New value</param>
        public static void SetEditorPreference(string key, string value) { ShareSettingsManager.instance.Set(key, value, SettingsScope.Project); }
        /// <summary>
        /// Gets an editor preference for the project
        /// </summary>
        /// <param name="key">ID of the preference</param>
        /// <returns>the value of the preference</returns>
        public static string GetEditorPreference(string key)
        {
            string result = ShareSettingsManager.instance.Get<string>(key, SettingsScope.Project);
            if (result == null)
            {
                result = string.Empty;
                SetEditorPreference(key, result);
            }
            return result;
        }

        /// <summary>
        /// Filters the name of the game, removing all spaces.
        /// </summary>
        /// <param name="currentGameTitle">The original name of the game</param>
        /// <returns>The name of the game without spaces, or a default name if the result would be invalid.</returns>
        public static string GetFilteredGameTitle(string currentGameTitle)
        {
            if (string.IsNullOrEmpty(currentGameTitle?.Trim())) { return DefaultGameName; }

            return currentGameTitle;
        }

        /// <summary>
        /// Supports GB, MB, KB, or B
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns>xB with two decimals, B with zero decimals</returns>
        public static string FormatBytes(ulong bytes)
        {
            double gb = bytes / (1024.0 * 1024.0 * 1024.0);
            double mb = bytes / (1024.0 * 1024.0);
            double kb = bytes / 1024.0;
            // Use :#.000 to specify further precision if wanted
            if (mb >= 1000) return $"{gb:#.00} GB";
            if (kb >= 1000) return $"{mb:#.00} MB";
            if (kb >= 1) return $"{kb:#.00} KB";
            return $"{bytes} B";
        }

        /// <summary>
        /// Gets the size of a folder, in bytes
        /// </summary>
        /// <param name="folder">The folder to analyze</param>
        /// <returns>The size of the folder, in bytes</returns>
        public static ulong GetSizeFolderSize(string folder)
        {
            ulong size = 0;
            DirectoryInfo directoryInfo = new DirectoryInfo(folder);
            foreach (FileInfo fileInfo in directoryInfo.GetFiles("*", SearchOption.AllDirectories))
            {
                size += (ulong)fileInfo.Length;
            }
            return size;
        }

        /// <summary>
        /// Allows a visual element to react on left click
        /// </summary>
        public class LeftClickManipulator : MouseManipulator
        {
            Action<VisualElement> OnClick;
            bool active;

            /// <summary>
            /// LeftClickManipulator Constructor
            /// </summary>
            /// <param name="OnClick">The default callback that will be triggered when the element is clicked</param>
            public LeftClickManipulator(Action<VisualElement> OnClick)
            {
                activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
                this.OnClick = OnClick;
            }

            /// <summary>
            /// Registers the callbacks on the target
            /// </summary>
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<MouseDownEvent>(OnMouseDown);
                target.RegisterCallback<MouseUpEvent>(OnMouseUp);
            }

            /// <summary>
            /// Unregisters the callbacks on the target
            /// </summary>
            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<MouseUpEvent>(OnMouseUp);
                target.UnregisterCallback<MouseDownEvent>(OnMouseDown);
            }

            /// <summary>
            /// Called when the mouse is clicked on the target, when the user starts pressing the button
            /// </summary>
            /// <param name="e"></param>
            protected void OnMouseDown(MouseDownEvent e)
            {
                if (active)
                {
                    e.StopImmediatePropagation();
                    return;
                }

                if (CanStartManipulation(e))
                {
                    active = true;
                    target.CaptureMouse();
                    e.StopPropagation();
                }
            }

            /// <summary>
            /// Called when the mouse is clicked on the target, when the user stops pressing the button
            /// </summary>
            /// <param name="e"></param>
            protected void OnMouseUp(MouseUpEvent e)
            {
                if (!active || !target.HasMouseCapture() || !CanStopManipulation(e)) { return; }

                active = false;
                target.ReleaseMouse();
                e.StopPropagation();

                if (OnClick == null) { return; }
                OnClick.Invoke(target);
            }
        }
    }
}

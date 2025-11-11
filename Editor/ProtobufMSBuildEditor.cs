using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace ProtobufMSBuildForUnity.Protobuf.Editor
{
    [InitializeOnLoad]
    public static class ProtoAutoBuilder
    {
        const string AutoBuildKey = "ProtobufMSBuild.AutoBuildEnabled";
        const string ProtoRootPrefKey = "ProtobufMSBuild.ProtoRoot";
        const string CsOutPrefKey = "ProtobufMSBuild.CsOutDir";
        static ProtoAutoBuilder()
        {
            if (!EditorPrefs.HasKey(AutoBuildKey)) EditorPrefs.SetBool(AutoBuildKey, false);
        }

        static string FindPackageRoot()
        {
            string[] guids = AssetDatabase.FindAssets("ProtobufMSBuildEditor t:script");
            string scriptPath = guids.Length > 0 ? AssetDatabase.GUIDToAssetPath(guids[0]) : null;
            if (string.IsNullOrEmpty(scriptPath)) return null;
            var dir = Path.GetDirectoryName(scriptPath);
            if (dir == null) return null;
            var editorDir = new DirectoryInfo(dir);
            var root = editorDir?.Parent?.FullName;
            return string.IsNullOrEmpty(root) ? null : root.Replace('/', Path.DirectorySeparatorChar);
        }

        static string ProtosDir()
        {
            var custom = EditorPrefs.GetString(ProtoRootPrefKey, string.Empty);
            if (!string.IsNullOrEmpty(custom))
            {
                try { return Path.GetFullPath(custom); } catch { }
            }
            var root = FindPackageRoot();
            if (string.IsNullOrEmpty(root)) return null;
            var p = Path.Combine(root, "Dotnet~", "ProtobufMSBuild", "Protos");
            return p;
        }

        static string CsprojPath()
        {
            var root = FindPackageRoot();
            if (string.IsNullOrEmpty(root)) return null;
            return Path.Combine(root, "Dotnet~", "ProtobufMSBuild", "ProtobufMSBuild.Protobuf.Messages.csproj");
        }

        public static bool AutoBuildEnabled
        {
            get => EditorPrefs.GetBool(AutoBuildKey, false);
            set => EditorPrefs.SetBool(AutoBuildKey, value);
        }

        class Postprocessor : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
            {
                if (!AutoBuildEnabled) return;
                var protosDir = ProtosDir();
                if (string.IsNullOrEmpty(protosDir)) return;
                string protosDirUnity = protosDir.Replace(Path.DirectorySeparatorChar, '/');
                bool InProtoRoot(string p) => p.EndsWith(".proto", StringComparison.OrdinalIgnoreCase) && p.Replace('\\', '/').StartsWith(protosDirUnity, StringComparison.OrdinalIgnoreCase);
                bool changed = importedAssets.Concat(movedAssets).Concat(deletedAssets).Any(InProtoRoot);
                if (!changed) return;
                EditorApplication.delayCall += BuildNow;
            }
        }

        [MenuItem("Protobuf MSBuild/Toggle Auto Build")]
        static void ToggleAutoBuild()
        {
            AutoBuildEnabled = !AutoBuildEnabled;
            EditorUtility.DisplayDialog("Protobuf MSBuild", $"Auto Build: {AutoBuildEnabled}", "OK");
        }

        [MenuItem("Protobuf MSBuild/Set Proto Folder...")]
        static void SetProtoFolder()
        {
            var curr = ProtosDir();
            var selected = EditorUtility.OpenFolderPanel("Select Proto Root Folder", string.IsNullOrEmpty(curr) ? Application.dataPath : curr, "");
            if (string.IsNullOrEmpty(selected)) return;
            try
            {
                EditorPrefs.SetString(ProtoRootPrefKey, selected);
                EditorUtility.DisplayDialog("Protobuf MSBuild", $"Proto Root set to:\n{selected}", "OK");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(e);
            }
        }

        [MenuItem("Protobuf MSBuild/Set C# Output Folder...")]
        static void SetCsOutputFolder()
        {
            var curr = EditorPrefs.GetString(CsOutPrefKey, string.Empty);
            var selected = EditorUtility.OpenFolderPanel("Select Generated C# Output Folder", string.IsNullOrEmpty(curr) ? Application.dataPath : curr, "");
            if (string.IsNullOrEmpty(selected)) return;
            try
            {
                EditorPrefs.SetString(CsOutPrefKey, selected);
                EditorUtility.DisplayDialog("Protobuf MSBuild", $"C# Output Folder set to:\n{selected}", "OK");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(e);
            }
        }

        [MenuItem("Protobuf MSBuild/Build")]
        public static void BuildNow()
        {
            var csproj = CsprojPath();
            if (string.IsNullOrEmpty(csproj) || !File.Exists(csproj))
            {
                EditorUtility.DisplayDialog("Protobuf MSBuild", "Cannot locate csproj. If the package is not embedded, embed it first.", "OK");
                return;
            }
            try
            {
                // Require user-specified proto root and C# output folder
                var userProtoRoot = EditorPrefs.GetString(ProtoRootPrefKey, string.Empty);
                var csOutDir = EditorPrefs.GetString(CsOutPrefKey, string.Empty);
                if (string.IsNullOrEmpty(userProtoRoot) || string.IsNullOrEmpty(csOutDir))
                {
                    EditorUtility.DisplayDialog(
                        "Protobuf MSBuild",
                        "Please set both the Proto Root folder and the Generated C# Output folder before building.\nMenu: Protobuf MSBuild -> Set Proto Folder... and Set C# Output Folder...",
                        "OK");
                    return;
                }
                var protoRoot = userProtoRoot;
                var psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"build \"{csproj}\" -c Release -p:ProtoRootDir=\"{protoRoot}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(csproj)
                };
                var p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd();
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (!string.IsNullOrEmpty(err)) UnityEngine.Debug.LogWarning(err);
                if (p.ExitCode != 0)
                {
                    EditorUtility.DisplayDialog("Protobuf MSBuild", "Build failed. See Console for details.", "OK");
                    UnityEngine.Debug.LogError(output);
                    return;
                }
                try
                {
                    var projDir = Path.GetDirectoryName(csproj);
                    var intermediate = Path.Combine(projDir, "obj", "Release", "netstandard2.0");
                    if (Directory.Exists(intermediate))
                    {
                        var protoDirLower = Path.Combine(intermediate, "protobuf");
                        var protoDirUpper = Path.Combine(intermediate, "Protobuf");
                        var sourceRoot = Directory.Exists(protoDirLower) ? protoDirLower : (Directory.Exists(protoDirUpper) ? protoDirUpper : intermediate);
                        var generatedFiles = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories).ToArray();
                        if (generatedFiles.Length > 0)
                        {
                            foreach (var file in generatedFiles)
                            {
                                string rel = file.Substring(sourceRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                                var dest = Path.Combine(csOutDir, rel);
                                var destDir = Path.GetDirectoryName(dest);
                                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                                File.Copy(file, dest, true);
                            }
                            UnityEngine.Debug.Log($"Protobuf MSBuild: Copied {generatedFiles.Length} generated files to {csOutDir}");
                        }
                    }
                }
                catch (Exception copyEx)
                {
                    UnityEngine.Debug.LogWarning($"Copy generated C# files failed: {copyEx}");
                }
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Protobuf MSBuild", "Build finished.", "OK");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(e);
            }
        }
    }
}

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
            // Prefer user-selected proto root
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

        static bool IsEmbedded()
        {
            var root = FindPackageRoot();
            if (string.IsNullOrEmpty(root)) return false;
            var projPackages = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages"));
            return Path.GetFullPath(root).StartsWith(Path.GetFullPath(projPackages), StringComparison.OrdinalIgnoreCase);
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

        [MenuItem("Protobuf MSBuild/Build Now")]
        public static void BuildNow()
        {
            var csproj = CsprojPath();
            if (string.IsNullOrEmpty(csproj) || !File.Exists(csproj))
            {
                EditorUtility.DisplayDialog("Protobuf MSBuild", "Cannot locate csproj. If the package is not embedded, embed it first.", "OK");
                return;
            }
            if (!IsEmbedded())
            {
                if (!EditorUtility.DisplayDialog("Protobuf MSBuild", "Package is not embedded. Building in cache is not supported. Embed the package first?", "OK", "Cancel"))
                    return;
            }
            try
            {
                var protoRoot = ProtosDir();
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
                UnityEngine.Debug.Log(output);
                if (!string.IsNullOrEmpty(err)) UnityEngine.Debug.LogWarning(err);
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Protobuf MSBuild", "Build finished.", "OK");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(e);
            }
        }

        [MenuItem("Protobuf MSBuild/Fetch Protos")]
        public static void FetchProtos()
        {
            var protos = ProtosDir();
            var root = FindPackageRoot();
            if (string.IsNullOrEmpty(protos) || string.IsNullOrEmpty(root))
            {
                EditorUtility.DisplayDialog("Protobuf MSBuild", "Cannot locate package root. Embed the package first.", "OK");
                return;
            }
            var cfgA = Path.Combine(root, "ProtoSources.txt");
            var cfgB = Path.Combine(Application.dataPath, "ProtoSources.txt");
            var cfg = File.Exists(cfgA) ? cfgA : cfgB;
            if (!File.Exists(cfg))
            {
                File.WriteAllText(cfgA, "# Put http(s) URLs or local file paths to .proto files, one per line\n");
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Protobuf MSBuild", "Created ProtoSources.txt in package root.", "OK");
                return;
            }
            Directory.CreateDirectory(protos);
            foreach (var raw in File.ReadAllLines(cfg))
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("#")) continue;
                try
                {
                    if (line.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || line.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var wc = new WebClient())
                        {
                            var fileName = Path.GetFileName(new Uri(line).AbsolutePath);
                            if (string.IsNullOrEmpty(fileName)) fileName = Guid.NewGuid() + ".proto";
                            var dst = Path.Combine(protos, fileName);
                            wc.DownloadFile(line, dst);
                        }
                    }
                    else
                    {
                        var src = Path.GetFullPath(line);
                        var dst = Path.Combine(protos, Path.GetFileName(src));
                        File.Copy(src, dst, true);
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"Fetch failed: {line}\n{ex}");
                }
            }
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Protobuf MSBuild", "Fetch finished.", "OK");
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BumperTanks.Frontend;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BumperTanks.Editor
{
    /// <summary>Reproducible player build entry points for local development and CI.</summary>
    public static class BumperTanksBuildEntrypoint
    {
        private const string ProductFileName = "BumperTanksIO";

        public static void BuildAndroidDevelopment()
        {
            var targetGroup = BuildTargetGroup.Android;
            var previousBackend = PlayerSettings.GetScriptingBackend(targetGroup);
            var previousAndroidArchitectures = PlayerSettings.Android.targetArchitectures;
            var androidPluginDirectory = Path.Combine("Assets", "Plugins", "Android");
            var manifestPath = Path.Combine(androidPluginDirectory, "AndroidManifest.xml");
            var metaPath = manifestPath + ".meta";
            var hadManifest = File.Exists(manifestPath);
            var hadMeta = File.Exists(metaPath);
            var originalManifest = hadManifest ? File.ReadAllText(manifestPath) : null;
            try
            {
                // Keep the fast Development APK path separate from release backend settings.
                PlayerSettings.SetScriptingBackend(targetGroup, ScriptingImplementation.Mono2x);
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7;
                Directory.CreateDirectory(androidPluginDirectory);
                File.WriteAllText(manifestPath, AddDevelopmentCleartextPolicy(originalManifest));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Build(BuildTarget.Android, true);
            }
            finally
            {
                try
                {
                    if (hadManifest) File.WriteAllText(manifestPath, originalManifest);
                    else if (File.Exists(manifestPath)) File.Delete(manifestPath);
                    if (!hadMeta && File.Exists(metaPath)) File.Delete(metaPath);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
                finally
                {
                    PlayerSettings.SetScriptingBackend(targetGroup, previousBackend);
                    PlayerSettings.Android.targetArchitectures = previousAndroidArchitectures;
                }
            }
        }

        private static string AddDevelopmentCleartextPolicy(string existingManifest)
        {
            XNamespace android = "http://schemas.android.com/apk/res/android";
            var document = string.IsNullOrWhiteSpace(existingManifest)
                ? new XDocument(new XElement("manifest"))
                : XDocument.Parse(existingManifest);
            var manifest = document.Root ?? throw new InvalidDataException("The Android manifest has no root element.");
            var applicationName = manifest.Name.Namespace + "application";
            var application = manifest.Element(applicationName);
            if (application == null)
            {
                application = new XElement(applicationName);
                manifest.Add(application);
            }
            application.SetAttributeValue(android + "usesCleartextTraffic", "true");
            return document.ToString(SaveOptions.DisableFormatting);
        }
        public static void BuildWindowsDevelopment() => Build(BuildTarget.StandaloneWindows64, true);
        public static void BuildWebGlDevelopment() => Build(BuildTarget.WebGL, true);
        public static void BuildWindowsRelease() => Build(BuildTarget.StandaloneWindows64, false);
        public static void BuildWebGlRelease() => Build(BuildTarget.WebGL, false);

        private static void Build(BuildTarget target, bool development)
        {
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length == 0) throw new BuildFailedException("No enabled scenes are configured in Build Settings.");

            if (!development)
            {
                var settings = ServiceEndpointSettings.Load();
                if (settings == null)
                    throw new BuildFailedException("Release build blocked by service endpoint configuration: " +
                                                   "Resources/ServiceEndpointSettings is missing.");
                if (!ServiceEndpointSettings.TryResolve(settings.ApiBaseUrl, settings.RealtimeWebSocketUrl,
                        false, out _, out var error))
                    throw new BuildFailedException("Release build blocked by service endpoint configuration: " + error);
            }

            var suffix = development ? "Development" : "Release";
            var folder = Path.Combine("Build", target.ToString() + suffix);
            Directory.CreateDirectory(folder);
            var extension = target switch
            {
                BuildTarget.StandaloneWindows64 => ".exe",
                BuildTarget.Android => ".apk",
                _ => string.Empty
            };
            var outputPath = Path.GetFullPath(Path.Combine(folder, ProductFileName + extension));
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = target,
                options = development ? BuildOptions.Development | BuildOptions.AllowDebugging : BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"{target} {suffix} build failed: {report.summary.result} ({report.summary.totalErrors} errors).");

            Debug.Log($"Built {target} {suffix} player at {report.summary.outputPath} " +
                      $"({report.summary.totalSize / (1024 * 1024)} MiB, {report.summary.totalTime}).");
        }
    }
}

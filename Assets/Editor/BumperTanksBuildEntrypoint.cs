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
            BuildAndroidDevelopment(AndroidArchitecture.ARMv7, ScriptingImplementation.Mono2x,
                Path.Combine("Build", "AndroidDevelopment"));
        }

        public static void BuildAndroidArm64Development()
        {
            BuildAndroidDevelopment(AndroidArchitecture.ARM64, ScriptingImplementation.IL2CPP,
                Path.Combine("Build", "AndroidArm64Development"));
        }

        private static void BuildAndroidDevelopment(AndroidArchitecture architecture, ScriptingImplementation backend,
            string outputDirectory)
        {
            var targetGroup = BuildTargetGroup.Android;
            var previousBackend = PlayerSettings.GetScriptingBackend(targetGroup);
            var previousAndroidArchitectures = PlayerSettings.Android.targetArchitectures;
            var androidPluginDirectory = Path.Combine("Assets", "Plugins", "Android");
            var manifestPath = Path.Combine(androidPluginDirectory, "AndroidManifest.xml");
            var metaPath = manifestPath + ".meta";
            var resolverSettingsPath = Path.Combine("ProjectSettings", "AndroidResolverDependencies.xml");
            var gradleTemplatePath = Path.Combine(androidPluginDirectory, "mainTemplate.gradle");
            var hadManifest = File.Exists(manifestPath);
            var hadMeta = File.Exists(metaPath);
            var hadResolverSettings = File.Exists(resolverSettingsPath);
            var hadGradleTemplate = File.Exists(gradleTemplatePath);
            var originalManifest = hadManifest ? File.ReadAllText(manifestPath) : null;
            var originalResolverSettings = hadResolverSettings ? File.ReadAllText(resolverSettingsPath) : null;
            var originalGradleTemplate = hadGradleTemplate ? File.ReadAllText(gradleTemplatePath) : null;
            try
            {
                PlayerSettings.SetScriptingBackend(targetGroup, backend);
                PlayerSettings.Android.targetArchitectures = architecture;
                Directory.CreateDirectory(androidPluginDirectory);
                File.WriteAllText(manifestPath, AddDevelopmentCleartextPolicy(originalManifest));
                if (hadResolverSettings)
                    File.WriteAllText(resolverSettingsPath,
                        SetAndroidResolverAbis(originalResolverSettings, architecture));
                if (hadGradleTemplate)
                    File.WriteAllText(gradleTemplatePath,
                        SetAndroidResolverAbiExclusions(originalGradleTemplate, architecture));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Build(BuildTarget.Android, true, outputDirectory);
            }
            finally
            {
                try
                {
                    if (hadManifest) File.WriteAllText(manifestPath, originalManifest);
                    else if (File.Exists(manifestPath)) File.Delete(manifestPath);
                    if (!hadMeta && File.Exists(metaPath)) File.Delete(metaPath);
                    if (hadResolverSettings) File.WriteAllText(resolverSettingsPath, originalResolverSettings);
                    if (hadGradleTemplate) File.WriteAllText(gradleTemplatePath, originalGradleTemplate);
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

        private static string SetAndroidResolverAbis(string settingsXml, AndroidArchitecture architecture)
        {
            var document = XDocument.Parse(settingsXml);
            var setting = document.Root?.Element("settings")?
                .Elements("setting").FirstOrDefault(element => (string)element.Attribute("name") == "androidAbis");
            if (setting == null)
                throw new InvalidDataException("Android Resolver settings do not define androidAbis.");

            setting.SetAttributeValue("value", architecture == AndroidArchitecture.ARM64
                ? "arm64-v8a"
                : "armeabi-v7a");
            return document.ToString(SaveOptions.DisableFormatting);
        }

        private static string SetAndroidResolverAbiExclusions(string gradleTemplate, AndroidArchitecture architecture)
        {
            const string startMarker = "// Android Resolver Exclusions Start";
            const string endMarker = "// Android Resolver Exclusions End";
            var start = gradleTemplate.IndexOf(startMarker, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidDataException("Android Gradle template has no Android Resolver Exclusions block.");
            var end = gradleTemplate.IndexOf(endMarker, start, StringComparison.Ordinal);
            if (end < 0)
                throw new InvalidDataException("Android Gradle template has no Android Resolver Exclusions block.");
            end += endMarker.Length;

            var targetAbi = architecture == AndroidArchitecture.ARM64 ? "arm64-v8a" : "armeabi-v7a";
            var allAbis = new[] { "armeabi", "armeabi-v7a", "arm64-v8a", "mips", "mips64", "x86", "x86_64" };
            var exclusions = string.Join(Environment.NewLine, allAbis.Where(abi => abi != targetAbi)
                .Select(abi => $"      exclude ('/lib/{abi}/*' + '*')"));
            var block = $"{startMarker}{Environment.NewLine}android {{{Environment.NewLine}" +
                        $"  packaging {{{Environment.NewLine}{exclusions}{Environment.NewLine}" +
                        $"  }}{Environment.NewLine}}}{Environment.NewLine}{endMarker}";
            return gradleTemplate.Substring(0, start) + block + gradleTemplate.Substring(end);
        }
        public static void BuildWindowsDevelopment() => Build(BuildTarget.StandaloneWindows64, true);
        public static void BuildWebGlDevelopment() => Build(BuildTarget.WebGL, true);
        public static void BuildWindowsRelease() => Build(BuildTarget.StandaloneWindows64, false);
        public static void BuildWebGlRelease() => Build(BuildTarget.WebGL, false);

        private static void Build(BuildTarget target, bool development, string outputDirectory = null)
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
            var folder = outputDirectory ?? Path.Combine("Build", target.ToString() + suffix);
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

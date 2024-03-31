using System;
using System.IO;
using System.Linq;
using System.Text;
using BuildSystem.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BuildSystem
{
    public static class BuildScript
    {
        private static readonly string Eol = Environment.NewLine;
        private static readonly StringBuilder _builder = new();

        private static BuildPlayerOptions GetBuildOptions()
        {
            var settingsPath = Path.Combine(".ci", "build_options.json");
            var settingsJson = File.ReadAllText(settingsPath);
            var settings = Json.Deserialise<BuildPlayerOptions>(settingsJson);

            if (settings.scenes.Length == 0)
                settings.scenes = BuildSettings.GetEditorSettingsScenes();
            
            Console.WriteLine($"settings: {JObject.FromObject(settings)}");
            return settings;
        }

        /// <summary>
        /// Called from build server
        /// </summary>
        public static void BuildPlayer()
        {
            var args = Environment.GetCommandLineArgs();
            Console.WriteLine("BuildPlayer called with args: " + string.Join(", ", args));

            var options = GetBuildOptions();
            BuildPlayer(options);
        }

        private static void BuildPlayer(BuildPlayerOptions options)
        {
            // TODO: set android keystore 
            // SetAndroidKeystore(settings);

            if (!EnsureBuildDirectoryExists(options))
            {
                ExitWithResult(BuildResult.Failed);
                return;
            }

            Application.logMessageReceived += OnLogReceived;
            PrintBuildOptions(options);
            var report = BuildPipeline.BuildPlayer(options);
            Application.logMessageReceived -= OnLogReceived;

            PrintReportSummary(report.summary);
            DumpErrorLog(report);
            ExitWithResult(report.summary.result, report);
        }

        private static void ExitWithResult(BuildResult result, BuildReport report = null)
        {
            switch (result)
            {
                case BuildResult.Succeeded:
                    Console.WriteLine("Build succeeded!");
                    EditorApplication.Exit(0);
                    break;
                case BuildResult.Failed:
                    if (report != null)
                    {
                        var errors = report.steps
                            .SelectMany(x => x.messages)
                            .Where(x => x.type is LogType.Error or LogType.Exception or LogType.Assert)
                            .Select(x => $"[{x.type.ToString().ToUpper()}] {x.content}")
                            .Reverse()
                            .ToArray();

                        Console.WriteLine(string.Join("\n", errors), LogType.Error);
                    }
                    else
                    {
                        Console.WriteLine("Build failed!");
                    }

                    EditorApplication.Exit(101);
                    break;
                case BuildResult.Cancelled:
                    Console.WriteLine("Build cancelled!");
                    EditorApplication.Exit(102);
                    break;
                case BuildResult.Unknown:
                default:
                    Console.WriteLine("Build result is unknown!");
                    EditorApplication.Exit(103);
                    break;
            }
        }

        private static void PrintReportSummary(BuildSummary summary)
        {
            Console.WriteLine(
                $"###########################{Eol}" +
                $"#      Build results      #{Eol}" +
                $"###########################{Eol}" +
                $"{Eol}" +
                $"Duration: {summary.totalTime.ToString()}{Eol}" +
                $"Warnings: {summary.totalWarnings.ToString()}{Eol}" +
                $"Errors: {summary.totalErrors.ToString()}{Eol}" +
                $"Size: {summary.totalSize.ToString()} bytes{Eol}" +
                $"{Eol}"
            );
        }

        private static void PrintBuildOptions(BuildPlayerOptions buildOptions)
        {
            var jsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                Converters = { new StringEnumConverter() }
            };

            Console.WriteLine(
                $"{Eol}" +
                $"###########################{Eol}" +
                $"#   Build Player Options  #{Eol}" +
                $"###########################{Eol}" +
                $"{Eol}" +
                $"{JsonConvert.SerializeObject(buildOptions, jsonSettings)}" +
                $"{Eol}"
            );
        }

        private static bool EnsureBuildDirectoryExists(BuildPlayerOptions options)
        {
            var fullDir = new FileInfo(options.locationPathName).Directory;

            // ensure build target folder exits
            if (fullDir == null)
                throw new NullReferenceException($"Directory is null: {options.locationPathName}");

            if (!fullDir.Exists)
                fullDir.Create();

            return true;
        }

        private static void SetAndroidKeystore(BuildSettings settings)
        {
            if (settings.Target != BuildTarget.Android)
                return;

            EditorUserBuildSettings.buildAppBundle = true;
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = settings.KeystorePath;
            PlayerSettings.Android.keystorePass = settings.KeystorePassword;
            PlayerSettings.Android.keyaliasName = settings.KeystoreAlias;
            PlayerSettings.Android.keyaliasPass = settings.KeystorePassword;
        }

        private static void DumpErrorLog(BuildReport report)
        {
            if (report.summary.totalErrors == 0)
                return;

            Console.WriteLine($"Build Failed is {report.summary.totalErrors} errors...\n{_builder}");

            var logFile = GetArgValue("-logFile");

            if (string.IsNullOrEmpty(logFile))
                return;

            var errorFileName = logFile.Replace(".log", "_errors.log");
            File.WriteAllText(errorFileName, _builder.ToString());
        }

        private static void OnLogReceived(string condition, string stacktrace, LogType type)
        {
            if (type is LogType.Log or LogType.Warning)
                return;

            _builder.AppendLine($"[{type.ToString().ToUpper()}] {condition}");

            if (!string.IsNullOrEmpty(stacktrace))
                _builder.AppendLine(stacktrace);
        }

        private static string GetArgValue(string arg)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == arg)
                    return args[i + 1];
            }

            return null;
        }
    }
}
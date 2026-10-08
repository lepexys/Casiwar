using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Casiwar.EditorTools
{
    /// <summary>
    /// Меню «Casiwar → Собрать билд (Windows)». Собирает только демо-сцену в Builds/Windows/Casiwar.exe
    /// (на весь экран, Alt+Enter — в окно) и упаковывает папку в Builds/Casiwar-Windows.zip — этот архив и отправляйте.
    /// </summary>
    public static class CasiwarBuild
    {
        public const string OutputFolder = "Builds/Windows";
        public const string ZipPath = "Builds/Casiwar-Windows.zip";
        private const string ExeName = "Casiwar.exe";

        [MenuItem("Casiwar/Собрать билд (Windows)", false, 20)]
        public static void BuildFromMenu()
        {
            if (Build(out string message))
                EditorUtility.RevealInFinder(ZipPath);
            EditorUtility.DisplayDialog("Casiwar", message, "OK");
        }

        /// <summary>Запуск без UI: Unity.exe -batchmode -quit -executeMethod Casiwar.EditorTools.CasiwarBuild.BuildFromCommandLine</summary>
        public static void BuildFromCommandLine()
        {
            bool ok = Build(out string message);
            Debug.Log("[Casiwar] " + message);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool Build(out string message)
        {
            if (!File.Exists(CasiwarSceneBuilder.ScenePath))
            {
                message = "Нет сцены " + CasiwarSceneBuilder.ScenePath + " — сначала Casiwar → Собрать демо-сцену.";
                return false;
            }

            // На весь экран в родном разрешении (горизонтальная раскладка — AdaptiveLayout); Alt+Enter — окно 1600×900
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.usePlayerLog = true;
            AssetDatabase.SaveAssets();

            // Игра из прошлого билда запущена — её файлы заняты: собираем в соседнюю папку, архив — как обычно
            string folder = IsRunning(OutputFolder) ? OutputFolder + "-new" : OutputFolder;
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { CasiwarSceneBuilder.ScenePath },
                locationPathName = Path.Combine(folder, ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                message = $"Сборка не удалась: {report.summary.result}, ошибок: {report.summary.totalErrors}. Подробности — в Console.";
                return false;
            }

            // Отладочные символы Burst игрокам не нужны
            foreach (string dir in Directory.GetDirectories(folder, "*_DoNotShip"))
                Directory.Delete(dir, true);

            if (File.Exists(ZipPath)) File.Delete(ZipPath);
            ZipFile.CreateFromDirectory(folder, ZipPath, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);

            long zipBytes = new FileInfo(ZipPath).Length;
            message = $"Готово: {Path.GetFullPath(ZipPath)} ({zipBytes / (1024f * 1024f):0.0} МБ).\n" +
                      (folder != OutputFolder ? $"Прошлый билд запущен — новый лежит в {folder}.\n" : string.Empty) +
                      "Отправьте архив: распаковать и запустить Casiwar.exe.";
            return true;
        }

        /// <summary>Запущен ли Casiwar.exe из этой папки (файл занят — его не удалить).</summary>
        private static bool IsRunning(string folder)
        {
            string exe = Path.Combine(folder, ExeName);
            if (!File.Exists(exe)) return false;
            try
            {
                using (new FileStream(exe, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch (System.UnauthorizedAccessException)
            {
                return true;
            }
        }
    }
}

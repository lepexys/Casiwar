using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Casiwar.EditorTools
{
    /// <summary>
    /// Следит, чтобы открытая демо-сцена не отставала от кода: в сцене хранится версия сборщика
    /// (GameManager.sceneVersion). Если она старше CasiwarSceneBuilder.SceneVersion — после перекомпиляции
    /// и при нажатии Play редактор предлагает пересобрать сцену одной кнопкой
    /// (то же самое, что Casiwar → Собрать демо-сцену).
    /// </summary>
    [InitializeOnLoad]
    public static class CasiwarSceneUpdateCheck
    {
        private const string DeclinedKey = "Casiwar.SceneUpdate.Declined";

        static CasiwarSceneUpdateCheck()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += () => Check(enteringPlayMode: false);
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) Check(enteringPlayMode: true);
        }

        private static void Check(bool enteringPlayMode)
        {
            if (Application.isBatchMode || SessionState.GetBool(DeclinedKey, false)) return;
            if (!enteringPlayMode && EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorSceneManager.GetActiveScene().path != CasiwarSceneBuilder.ScenePath) return;
            GameManager game = Object.FindFirstObjectByType<GameManager>();
            if (game == null || game.sceneVersion >= CasiwarSceneBuilder.SceneVersion) return;

            int choice = EditorUtility.DisplayDialogComplex("Casiwar",
                "Сцена CasiwarDemo собрана старой версией — в ней нет последних изменений интерфейса " +
                "(вкладка «Войска», порядок вкладок «Город, Наука, Поле», звёзды на скамейке, легенда трофеев и др.).\n\n" +
                "Пересобрать сцену сейчас? (Как Casiwar → Собрать демо-сцену.)",
                "Пересобрать", enteringPlayMode ? "Играть так" : "Позже", "Отмена");
            if (choice == 1)
            {
                SessionState.SetBool(DeclinedKey, true); // больше не спрашиваем до перезапуска редактора
                return;
            }
            if (enteringPlayMode) EditorApplication.isPlaying = false; // сначала пересборка (или отмена), потом Play
            if (choice != 0) return;

            EditorApplication.delayCall += () =>
            {
                if (CasiwarSceneBuilder.RebuildOutdated())
                {
                    Debug.Log("[Casiwar] Сцена пересобрана под текущую версию кода.");
                    if (enteringPlayMode) EditorApplication.EnterPlaymode();
                }
            };
        }
    }
}

using UnityEditor;
using UnityEditor.SceneManagement;

namespace Casiwar.EditorTools
{
    /// <summary>Временный запуск теста экономики: открыть демо-сцену и войти в Play (бот — EconomyBot, аргумент -casiwarEcon).</summary>
    public static class EconomySimLauncher
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene(CasiwarSceneBuilder.ScenePath);
            EditorApplication.isPlaying = true;
        }
    }
}

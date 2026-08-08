using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gacha.Presentation
{
    public static class ContentReturnNavigation
    {
        private static string returnScene;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            returnScene = null;
        }

        public static void RememberCurrentScene()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            if (!string.IsNullOrWhiteSpace(sceneName) &&
                !string.Equals(sceneName, "006_ContentScene", StringComparison.Ordinal))
                returnScene = sceneName;
        }

        public static string ConsumeOrDefault(string defaultScene)
        {
            string result = string.IsNullOrWhiteSpace(returnScene) ? defaultScene : returnScene;
            returnScene = null;
            return result;
        }
    }
}

using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace Gacha.Presentation
{
    /// <summary>
    /// Starts legacy scene background videos after scene activation, while keeping batch-mode
    /// validation independent from a graphics device.
    /// </summary>
    public static class BackgroundVideoPlaybackService
    {
        private static bool registered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistration()
        {
            if (registered)
                SceneManager.sceneLoaded -= OnSceneLoaded;
            registered = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if (registered)
                return;
            SceneManager.sceneLoaded += OnSceneLoaded;
            registered = true;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            bool noGraphics = Environment.GetCommandLineArgs()
                .Any(argument => string.Equals(
                    argument,
                    "-nographics",
                    StringComparison.OrdinalIgnoreCase));

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (VideoPlayer player in root.GetComponentsInChildren<VideoPlayer>(true))
                {
                    if (noGraphics)
                    {
                        player.Stop();
                        player.enabled = false;
                    }
                    else if (player.enabled && player.gameObject.activeInHierarchy && !player.isPlaying)
                    {
                        player.Play();
                    }
                }
            }
        }
    }
}

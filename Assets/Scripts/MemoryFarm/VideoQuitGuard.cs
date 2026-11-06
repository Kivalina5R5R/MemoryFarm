using UnityEngine;
using UnityEngine.Video;

namespace Deepspace
{
    /// <summary>
    /// Bug 2026-10-29 (all 4 builds hung "Not responding" when the launcher closed them): a VideoPlayer still playing
    /// or paused at quit is stopped by Unity inside CleanupEngine, after the job system is gone, and
    /// VideoClipPlayback::Stop -> ScheduleClearJob spins there forever. MenuIntroVideo only pauses its players.
    /// Stopping every VideoPlayer on Application.quitting, while jobs still run, lets the player exit.
    /// </summary>
    static class VideoQuitGuard
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            Application.quitting -= StopAllVideos;
            Application.quitting += StopAllVideos;
        }

        static void StopAllVideos()
        {
            foreach (var player in Object.FindObjectsByType<VideoPlayer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (player != null) player.Stop();
            }
        }
    }
}

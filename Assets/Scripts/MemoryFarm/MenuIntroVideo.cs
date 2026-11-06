using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Deepspace
{
    // บาส 2026-10-22: the menu animation behind a game's logo, the same idea as Deepspace's lower-menu video (Intro once,
    // then Intro-Loop forever), shared by Memory Farm, Boss Slayer and VolleyBall on both screens. The clips live in
    // Resources/MenuIntro/<game>/Intro[-Wall] and Intro-Loop[-Wall] (AI clips cut so each intro ends on its loop's first
    // frame, see D:/WORK/Recordings/menu_intro_ai/build_veo_set.py). It lives on a full-size RawImage at the back of the
    // menu panel and plays while that panel is shown (OnEnable / OnDisable), so the menus need no extra calls. Until the
    // first frame is decoded it shows the panel's own ground colour instead of a white RenderTexture.
    [RequireComponent(typeof(RawImage))]
    public sealed class MenuIntroVideo : MonoBehaviour
    {
        public VideoClip intro, loop;
        public Color waitColor = Color.black;
        [Tooltip("Colour the playing video is drawn with: below white dims it under busy UI.")]
        public Color tint = Color.white;
        // Two players into one RenderTexture: the loop is prepared while the intro plays, so the hand-over at the
        // intro's last frame is instant (switching clips on one player stalls on a re-prepare and freezes a frame).
        VideoPlayer introPlayer, loopPlayer; RenderTexture target; RawImage view;

        public bool Showing => view != null && view.texture == target && target != null;
        public bool OnLoop => loopPlayer != null && loopPlayer.isPlaying;
        // The intro has played to its end since the panel was last shown.
        public bool IntroFinished => intro == null || introEnded;
        bool introEnded;
        public string State => $"intro {(introPlayer ? $"{introPlayer.time:0.0}/{introPlayer.length:0.0}s frame {introPlayer.frame}/{introPlayer.frameCount} playing={introPlayer.isPlaying} prepared={introPlayer.isPrepared}" : "-")} | loop {(loopPlayer ? $"{loopPlayer.time:0.0}s playing={loopPlayer.isPlaying} prepared={loopPlayer.isPrepared}" : "-")}";

        // Find-or-create the video at the back of panel. Null when the clips are missing (nothing is added).
        // Safe to call again (themes re-apply): an unchanged video keeps playing and keeps its picture.
        public static MenuIntroVideo Attach(RectTransform panel, string game, bool wall, Color waitColor)
        {
            if (panel == null) return null;
            string suffix = wall ? "-Wall" : "";
            var intro = Resources.Load<VideoClip>($"MenuIntro/{game}/Intro{suffix}");
            var loop = Resources.Load<VideoClip>($"MenuIntro/{game}/Intro-Loop{suffix}");
            if (intro == null && loop == null) return null;
            var rect = UiThemeKit.Child(panel, "RawImage == Menu Intro Video");
            UiThemeKit.Stretch(rect, 0);
            rect.SetAsFirstSibling();
            if (!rect.TryGetComponent(out RawImage raw)) raw = rect.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            // AddComponent runs OnEnable at once, before the clips are set: Begin below starts it instead.
            if (!rect.TryGetComponent(out MenuIntroVideo video)) video = rect.gameObject.AddComponent<MenuIntroVideo>();
            video.waitColor = waitColor;
            if (video.intro == intro && video.loop == loop && video.Showing) return video;
            video.intro = intro; video.loop = loop;
            if (video.isActiveAndEnabled) video.Begin();
            else { raw.texture = null; raw.color = waitColor; }
            return video;
        }

        void OnEnable() => Begin();

        void Begin()
        {
            if (!Application.isPlaying || (intro == null && loop == null)) return;
            introEnded = false;
            view = GetComponent<RawImage>();
            if (introPlayer != null && introPlayer.clip != intro) { Destroy(introPlayer); introPlayer = null; }
            if (loopPlayer != null && loopPlayer.clip != loop) { Destroy(loopPlayer); loopPlayer = null; }
            if (target == null)
            {
                // the clip's own size (the UI rect can still be zero on the first frame)
                var clip = loop != null ? loop : intro;
                target = new RenderTexture(Mathf.Clamp((int)clip.width, 256, 4096), Mathf.Clamp((int)clip.height, 256, 2160), 0, RenderTextureFormat.ARGB32) { name = "RT_Menu_Intro_Video" };
                target.Create();
            }
            if (intro != null && introPlayer == null) { introPlayer = NewPlayer(intro, false); introPlayer.loopPointReached += OnIntroEnded; }
            if (loop != null && loopPlayer == null) loopPlayer = NewPlayer(loop, true);
            // back to the start; players that are already prepared (shown before) stay prepared and start at once
            Rewind(introPlayer); Rewind(loopPlayer);
            view.texture = null; view.color = waitColor;
            if (introPlayer != null)
            {
                introPlayer.sendFrameReadyEvents = true; introPlayer.Play();
                if (loopPlayer != null && !loopPlayer.isPrepared) loopPlayer.Prepare();
            }
            else { loopPlayer.sendFrameReadyEvents = true; loopPlayer.Play(); }
        }

        VideoPlayer NewPlayer(VideoClip clip, bool loops)
        {
            var p = gameObject.AddComponent<VideoPlayer>();
            p.playOnAwake = false; p.renderMode = VideoRenderMode.RenderTexture; p.targetTexture = target;
            p.audioOutputMode = VideoAudioOutputMode.None; p.waitForFirstFrame = true; p.skipOnDrop = true;
            p.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;   // menus can sit at timeScale 0: the video must still run
            p.aspectRatio = VideoAspectRatio.FitOutside;   // fill the screen without bars, like Deepspace
            p.clip = clip; p.isLooping = loops;
            p.frameReady += OnFrameReady;
            return p;
        }

        // Pause, not Stop: Stop releases the decoder, and preparing it again takes seconds on the venue PCs.
        void OnDisable() { Rewind(introPlayer); Rewind(loopPlayer); }

        static void Rewind(VideoPlayer p)
        {
            if (p == null) return;
            if (p.isPrepared) { p.Pause(); p.time = 0; } else p.Stop();
        }

        void OnDestroy() { if (target != null) { target.Release(); Destroy(target); } }

        void OnFrameReady(VideoPlayer source, long frame)
        {
            view.texture = target; view.color = tint;
            source.sendFrameReadyEvents = false;   // only the first frame matters
        }

        // The intro ends on the loop's first frame (MenuIntro clips are cut that way), and the loop is already prepared.
        void OnIntroEnded(VideoPlayer source)
        {
            introEnded = true;
            if (loopPlayer == null) return;   // no loop: the intro's last frame stays up
            loopPlayer.Play();
            source.Pause(); source.time = 0;
        }
    }
}

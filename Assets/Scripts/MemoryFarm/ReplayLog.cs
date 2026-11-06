using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Deepspace
{
    /// <summary>
    /// Shared replay/highlight logger for every Fortal floor game (same file as the Main Arena Replay Kit's, namespace Deepspace).
    /// Writes one JSON-lines file per session to &lt;project or build folder&gt;/ReplayLogs/.
    /// Line kinds: session_start, frame (sampled player state), event (what happened), session_end.
    /// Since schema 2 (2026-10-07) events carry no tier: the game only says WHAT happened, and the replay tool
    /// (AI or its log reader) decides what is worth cutting.
    /// Times are unscaled seconds since session start (slow-mo does not stretch them) plus a UTC stamp
    /// so the external replay tool can line the log up with the recorded video.
    /// Schema: D:/WORK/Fortal/GDD/Replay Log Spec.md (and SPEC.md in the ReplayKit).
    /// </summary>
    public static class ReplayLog
    {
        public const int SchemaVersion = 2;
        [Obsolete("Events have no tier since schema 2 — the replay tool decides what to cut.")]
        public const string TierS = "S", TierA = "A", TierB = "B";

        public static bool Enabled = true;
        public static float FrameInterval = .1f;
        /// <summary>Also print session start/end and every event to the Unity Console (frames are never printed).</summary>
        public static bool LogToConsole = true;
        /// <summary>Kept so kit 3.0 code still compiles; every event is printed when LogToConsole is on.</summary>
        [Obsolete("Events have no tier since schema 2 — every event is printed when LogToConsole is on.")]
        public static string ConsoleMinTier = "B";
        /// <summary>Override the output folder; null = "ReplayLogs" next to Assets/ (Editor) or the build's _Data folder.</summary>
        public static string OutputDirectory;

        public static bool Active => writer != null;
        public static string CurrentGame { get; private set; }
        public static string CurrentSessionId { get; private set; }
        public static string CurrentFilePath { get; private set; }
        public static float SessionTime => Active ? Time.realtimeSinceStartup - sessionStart : 0f;

        static StreamWriter writer;
        static float sessionStart, nextFrameTime, lastFlush;
        static bool quitHooked;
        /// <summary>Signal file for recorders (OBS script): state "live" while a session is open, "ended" after.</summary>
        public const string LiveSignalFile = "_live.json";
        static readonly StringBuilder sb = new StringBuilder(512);

        // ---------------------------------------------------------------- session

        /// <summary>Start a new session file. Ends any open session first.</summary>
        public static void BeginSession(string game, int playerCount, ReplayData data = null)
        {
            if (!Enabled) return;
            if (Active) EndSession("restarted");
            try
            {
                string dir = OutputDirectory ?? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ReplayLogs"));
                Directory.CreateDirectory(dir);
                DateTime now = DateTime.UtcNow;
                CurrentGame = game;
                CurrentSessionId = game + "_" + now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
                CurrentFilePath = Path.Combine(dir, CurrentSessionId + ".jsonl");
                writer = new StreamWriter(CurrentFilePath, false, new UTF8Encoding(false));
                sessionStart = Time.realtimeSinceStartup;
                nextFrameTime = 0f;
                lastFlush = sessionStart;
                if (!quitHooked) { Application.quitting += () => EndSession("quit"); quitHooked = true; }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ReplayLog] Could not open log file: " + e.Message);
                writer = null;
                return;
            }
            Begin("session_start");
            Str("game", game); Num("players", playerCount); Num("schema", SchemaVersion);
            Str("session", CurrentSessionId); Str("scene", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            Num("frameInterval", FrameInterval);
            if (data != null) data.WriteTo();
            End(true);
            WriteLiveSignal("live", null);
            if (LogToConsole && Active)
                Debug.Log($"<b>[Replay]</b> ▶ {game} session started — {playerCount} players{(data != null ? "  " + data.Describe() : "")}\n{CurrentFilePath}");
        }

        /// <summary>Close the session. winners = player indexes (empty = draw / none), scores indexed by player.</summary>
        public static void EndSession(string reason, int[] winners = null, int[] scores = null, ReplayData data = null)
        {
            if (!Active) return;
            Begin("session_end");
            Str("reason", reason);
            if (winners != null) Arr("winners", winners);
            if (scores != null) Arr("scores", scores);
            if (data != null) data.WriteTo();
            End(true);
            if (LogToConsole)
                Debug.Log($"<b>[Replay]</b> ■ {CurrentGame} session {reason} at {SessionTime:0.0}s" +
                          (winners != null ? "  winners=" + PlayerList(winners) : "") +
                          (scores != null ? "  scores=[" + string.Join(", ", scores) + "]" : "") +
                          (data != null ? "  " + data.Describe() : "") + "\n" + CurrentFilePath);
            try { writer?.Dispose(); } catch { }
            writer = null;
            WriteLiveSignal("ended", reason);
        }

        // One small file the OBS auto-record script polls; written atomically (temp + copy) so it never reads half a file.
        static void WriteLiveSignal(string state, string reason)
        {
            if (string.IsNullOrEmpty(CurrentFilePath)) return;
            try
            {
                string dir = Path.GetDirectoryName(CurrentFilePath);
                string json = "{\"state\":\"" + state + "\",\"game\":\"" + CurrentGame + "\",\"session\":\"" + CurrentSessionId +
                              "\",\"log\":\"" + CurrentFilePath.Replace("\\", "/") + "\",\"reason\":\"" + (reason ?? "") +
                              "\",\"utc\":\"" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\"}";
                string tmp = Path.Combine(dir, LiveSignalFile + ".tmp");
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                File.Copy(tmp, Path.Combine(dir, LiveSignalFile), true);
                File.Delete(tmp);
            }
            catch (Exception e) { Debug.LogWarning("[ReplayLog] Could not write live signal: " + e.Message); }
        }

        // ---------------------------------------------------------------- frames

        /// <summary>True once per FrameInterval while a session is open. Call WriteFrame when it returns true.</summary>
        public static bool ShouldSampleFrame()
        {
            if (!Active) return false;
            float t = SessionTime;
            if (t < nextFrameTime) return false;
            // Step from the scheduled time (not from now) so the rate stays at 1/FrameInterval instead of drifting low.
            nextFrameTime = t - nextFrameTime > FrameInterval ? t + FrameInterval : nextFrameTime + FrameInterval;
            return true;
        }

        /// <summary>
        /// One sampled frame. Positions are normalized floor coordinates: x 0 = left → 1 = right,
        /// y 0 = near/bottom edge → 1 = far/top edge (wall side). Use -1 when a player is not tracked.
        /// </summary>
        public static void WriteFrame(string phase, IList<ReplayPlayer> players, ReplayData data = null)
        {
            if (!Active) return;
            Begin("frame");
            Str("phase", phase);
            sb.Append(",\"p\":[");
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"i\":").Append(p.index)
                  .Append(",\"on\":").Append(p.tracked ? 1 : 0)
                  .Append(",\"x\":"); AppendFloat(p.tracked ? p.x : -1f);
                sb.Append(",\"y\":"); AppendFloat(p.tracked ? p.y : -1f);
                sb.Append(",\"s\":").Append(p.score);
                if (!string.IsNullOrEmpty(p.state)) { sb.Append(",\"st\":"); AppendString(p.state); }
                sb.Append('}');
            }
            sb.Append(']');
            if (data != null) data.WriteTo();
            End(false);
        }

        // ---------------------------------------------------------------- events

        /// <summary>
        /// Something happened. Log it plainly — no importance rating: the replay tool decides what is worth cutting.
        /// pre/post = seconds of video that show the cause before / the reaction after this moment.
        /// players = involved player indexes (can be null). data = the facts (score, column, time left, final, ...) —
        /// the more facts, the better the tool can judge (e.g. log "final" or "size" instead of rating the moment).
        /// </summary>
        public static void Event(string type, float pre, float post, int[] players = null, ReplayData data = null)
        {
            if (!Active) return;
            Begin("event");
            Str("type", type);
            Num("pre", pre); Num("post", post);
            if (players != null) Arr("players", players);
            if (data != null) data.WriteTo();
            End(true);
            if (LogToConsole)
                Debug.Log($"<b>[Replay]</b> {type}  t={SessionTime:0.00}s" +
                          (players != null && players.Length > 0 ? "  " + PlayerList(players) : "") +
                          (data != null ? "  " + data.Describe() : "") + $"  (clip -{pre:0.#}s/+{post:0.#}s)");
        }

        public static void Event(string type, float pre, float post, int player, ReplayData data = null)
            => Event(type, pre, post, player >= 0 ? new[] { player } : null, data);

        /// <summary>Schema-1 signature kept so older game code still compiles; the tier is ignored.</summary>
        [Obsolete("Drop the tier: ReplayLog.Event(type, pre, post, players, data).")]
        public static void Event(string type, string tier, float pre, float post, int[] players = null, ReplayData data = null)
            => Event(type, pre, post, players, data);

        /// <summary>Human-readable 1-based player labels for the Console (the file keeps 0-based indexes).</summary>
        static string PlayerList(int[] players)
        {
            if (players.Length == 0) return "none";
            var parts = new string[players.Length];
            for (int i = 0; i < players.Length; i++) parts[i] = "P" + (players[i] + 1);
            return string.Join(",", parts);
        }

        [Obsolete("Drop the tier: ReplayLog.Event(type, pre, post, player, data).")]
        public static void Event(string type, string tier, float pre, float post, int player, ReplayData data = null)
            => Event(type, pre, post, player, data);

        /// <summary>Start a context payload: ReplayLog.Data().Add("column", 16).Add("timeLeft", 4.2f)</summary>
        public static ReplayData Data() => new ReplayData();

        // ---------------------------------------------------------------- json writer

        static void Begin(string kind)
        {
            sb.Clear();
            sb.Append("{\"k\":"); AppendString(kind);
            sb.Append(",\"t\":"); AppendFloat(SessionTime);
            sb.Append(",\"f\":").Append(Time.frameCount);
            if (kind != "frame") { sb.Append(",\"utc\":"); AppendString(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)); }
        }

        static void End(bool flush)
        {
            sb.Append('}');
            try
            {
                writer.WriteLine(sb.ToString());
                float now = Time.realtimeSinceStartup;
                if (flush || now - lastFlush > 1f) { writer.Flush(); lastFlush = now; }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ReplayLog] Write failed, logging stopped: " + e.Message);
                try { writer.Dispose(); } catch { }
                writer = null;
            }
        }

        internal static void Str(string key, string value) { Key(key); AppendString(value); }
        internal static void Num(string key, float value) { Key(key); AppendFloat(value); }
        internal static void Num(string key, int value) { Key(key); sb.Append(value); }
        internal static void Bool(string key, bool value) { Key(key); sb.Append(value ? "true" : "false"); }
        internal static void Arr(string key, int[] values)
        {
            Key(key); sb.Append('[');
            for (int i = 0; i < values.Length; i++) { if (i > 0) sb.Append(','); sb.Append(values[i]); }
            sb.Append(']');
        }
        internal static void Arr(string key, float[] values)
        {
            Key(key); sb.Append('[');
            for (int i = 0; i < values.Length; i++) { if (i > 0) sb.Append(','); AppendFloat(values[i]); }
            sb.Append(']');
        }

        static void Key(string key) { sb.Append(','); AppendString(key); sb.Append(':'); }

        static void AppendFloat(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) { sb.Append("null"); return; }
            sb.Append(Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture));
        }

        static void AppendString(string s)
        {
            if (s == null) { sb.Append("null"); return; }
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    /// <summary>One player's sampled state for ReplayLog.WriteFrame.</summary>
    public struct ReplayPlayer
    {
        public int index;
        public bool tracked;
        public float x, y;
        public int score;
        public string state;

        public ReplayPlayer(int index, bool tracked, float x, float y, int score, string state = null)
        {
            this.index = index; this.tracked = tracked; this.x = x; this.y = y; this.score = score; this.state = state;
        }
    }

    /// <summary>Context key/values attached to a log line. Written flat into the line (no nested object) so any tool can read them.</summary>
    public sealed class ReplayData
    {
        readonly List<(string key, object value)> items = new List<(string, object)>(6);

        public ReplayData Add(string key, int value) { items.Add((key, value)); return this; }
        public ReplayData Add(string key, float value) { items.Add((key, value)); return this; }
        public ReplayData Add(string key, bool value) { items.Add((key, value)); return this; }
        public ReplayData Add(string key, string value) { items.Add((key, value)); return this; }
        public ReplayData Add(string key, int[] value) { items.Add((key, value)); return this; }
        public ReplayData Add(string key, float[] value) { items.Add((key, value)); return this; }

        /// <summary>key=value list for Console output.</summary>
        internal string Describe()
        {
            var parts = new List<string>(items.Count);
            foreach (var (key, value) in items)
            {
                string v = value switch
                {
                    float f => f.ToString("0.##", CultureInfo.InvariantCulture),
                    int[] ia => "[" + string.Join(",", ia) + "]",
                    float[] fa => "[" + string.Join(",", Array.ConvertAll(fa, x => x.ToString("0.##", CultureInfo.InvariantCulture))) + "]",
                    _ => value?.ToString()
                };
                parts.Add(key + "=" + v);
            }
            return string.Join("  ", parts);
        }

        internal void WriteTo()
        {
            if (items.Count == 0) return;
            foreach (var (key, value) in items)
            {
                switch (value)
                {
                    case int i: ReplayLog.Num(key, i); break;
                    case float f: ReplayLog.Num(key, f); break;
                    case bool b: ReplayLog.Bool(key, b); break;
                    case string s: ReplayLog.Str(key, s); break;
                    case int[] ia: ReplayLog.Arr(key, ia); break;
                    case float[] fa: ReplayLog.Arr(key, fa); break;
                }
            }
        }
    }

    /// <summary>Detects a change of sole leader in a score list (ties never count as a lead).</summary>
    public sealed class ReplayLeadTracker
    {
        int leader = -1;
        public int Leader => leader;
        public void Reset() => leader = -1;

        /// <summary>Returns true when a different player becomes the sole leader. previous = old leader (-1 if none).</summary>
        public bool Update(IList<int> scores, out int newLeader, out int previous)
        {
            previous = leader;
            newLeader = -1;
            int best = int.MinValue; bool tie = false;
            for (int i = 0; i < scores.Count; i++)
            {
                if (scores[i] > best) { best = scores[i]; newLeader = i; tie = false; }
                else if (scores[i] == best) tie = true;
            }
            if (tie || best <= 0) { newLeader = leader; return false; }
            if (newLeader == leader) return false;
            leader = newLeader;
            return previous >= 0;
        }
    }

    /// <summary>Counts hits per player inside a sliding time window (multi-kill, multi-coin, ...).</summary>
    public sealed class ReplayBurstCounter
    {
        readonly float window;
        readonly Dictionary<int, Queue<float>> hits = new Dictionary<int, Queue<float>>();
        readonly Dictionary<int, int> reported = new Dictionary<int, int>();

        public ReplayBurstCounter(float windowSeconds) { window = windowSeconds; }
        public void Reset() { hits.Clear(); reported.Clear(); }

        /// <summary>Add one hit and return how many hits this player has inside the window (including this one).</summary>
        public int Add(int player, float time)
        {
            if (!hits.TryGetValue(player, out var q)) hits[player] = q = new Queue<float>();
            while (q.Count > 0 && time - q.Peek() > window) q.Dequeue();
            if (q.Count == 0) reported[player] = 0;
            q.Enqueue(time);
            return q.Count;
        }

        /// <summary>True the first time a burst reaches `threshold` (and again only after the burst ends).</summary>
        public bool Reached(int player, int count, int threshold)
        {
            reported.TryGetValue(player, out int done);
            if (count < threshold || done >= threshold) return false;
            reported[player] = count;
            return true;
        }
    }
}

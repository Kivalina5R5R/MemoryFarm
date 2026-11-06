using System.Collections.Generic;
using UnityEngine;

namespace Deepspace
{
    // บาส 2026-10-06 (Memory Farm balance note A2 + C):
    //   * Divider band: a player standing on a zone divider used to flip between two answers on tag jitter in the
    //     last second. Within ±zoneDividerBandMeters/2 of a divider the answer stays on the last zone seen outside the
    //     band. The dividers are not drawn: each zone already has its own answer sign.
    //   * Counting question: distractors are the numbers nearest the answer, all within 0..farmers in the parade, so
    //     players have to remember instead of ruling out impossible options (a "5" for a parade of 4).
    public sealed partial class MemoryFarmGameManager
    {
        [Header("Answer zones")]
        [Tooltip("Width of the no-switch band on every zone divider, metres. Inside it the last zone outside the band counts.")]
        [SerializeField, Min(0f)] float zoneDividerBandMeters = .3f;

        int ApplyDividerBand(PlayerState player, float t, int zone, int zones)
        {
            float halfBand = zoneDividerBandMeters * .5f / Mathf.Max(.1f, arenaWidthMeters); // in 0..1 floor-width units
            int divider = Mathf.RoundToInt(t * zones);                                        // nearest divider (1..zones-1)
            bool inBand = divider > 0 && divider < zones && Mathf.Abs(t - divider / (float)zones) < halfBand;
            if (!inBand) { player.StableZone = zone; return zone; }
            // Keep the zone the player came from; only the two zones beside this divider qualify.
            return player.StableZone == divider - 1 || player.StableZone == divider ? player.StableZone : zone;
        }

        // Nearest numbers to the answer first (±1, then ±2...), clamped to 0..farmers; ties go either way at random.
        static string[] CountChoices(int answer, int farmers, int count)
        {
            var picked = new List<int> { answer };
            for (int d = 1; picked.Count < count && d <= farmers; d++)
            {
                var ring = new List<int>();
                if (answer - d >= 0) ring.Add(answer - d);
                if (answer + d <= farmers) ring.Add(answer + d);
                Shuffle(ring);
                foreach (int v in ring) if (picked.Count < count) picked.Add(v);
            }
            var pool = new string[picked.Count];
            for (int i = 0; i < picked.Count; i++) pool[i] = picked[i].ToString();
            return pool;
        }
    }
}

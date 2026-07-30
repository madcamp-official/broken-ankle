using UnityEngine;

namespace Ashburn.Cutscenes
{
    /// <summary>
    /// Whether an authored story beat is playing right now, for anything that must hold off while
    /// one is.
    ///
    /// Global rather than per-player on purpose. The monster runs on the host and asks about the
    /// other player through the copy of them it holds, and that copy is not the one a cutscene
    /// suspends — a beat takes the hands off the keyboard that is driving the character, which is
    /// the far machine. Both clients run the same beat at the same time, though, so a host that is
    /// in one knows the other player is too, and that is the only thing the question needs.
    ///
    /// Counted rather than a flag: a dialogue that locks input can run inside a sequence that has
    /// already suspended everybody, and the inner one finishing must not say the outer one has.
    /// </summary>
    public static class StoryBeat
    {
        static int _depth;

        /// <summary>True while at least one beat has the players' hands off the keys.</summary>
        public static bool Running => _depth > 0;

        /// <summary>A beat has taken control. Must be paired with <see cref="End"/>.</summary>
        public static void Begin() => _depth++;

        /// <summary>
        /// A beat has given control back.
        ///
        /// Floored at zero rather than trusted to balance. One unmatched release over a long
        /// session leaves a counter that never reaches zero again, and the wrong side of this is a
        /// monster that has stopped being able to touch anybody.
        /// </summary>
        public static void End() => _depth = Mathf.Max(0, _depth - 1);

        // A static outlives a play session when the editor skips its domain reload, and a beat left
        // counted from the last run would start this one with nobody catchable.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad() => _depth = 0;
    }
}

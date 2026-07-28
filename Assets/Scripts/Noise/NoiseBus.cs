using System;
using UnityEngine;

namespace Ashburn.Noise
{
    /// <summary>
    /// Who made a sound. The player's own footsteps are not shown back to them, but a partner's
    /// and the monster's are, and they have to be told apart at a glance.
    /// </summary>
    public enum NoiseKind
    {
        Self,
        Ally,
        Monster,
    }

    /// <summary>One sound, at the moment it happened.</summary>
    public readonly struct NoiseEvent
    {
        /// <summary>A sound belonging to no map. Nothing in a map will hear it.</summary>
        public const int Unzoned = -1;

        /// <summary>Where the sound came from, in world space.</summary>
        public readonly Vector2 Position;

        /// <summary>How far it carries, in world units. Doubles as how loud it reads up close.</summary>
        public readonly float Range;

        public readonly NoiseKind Kind;

        /// <summary>
        /// Which map it happened in. Carried explicitly rather than inferred from
        /// <see cref="Position"/>: maps are far enough apart that distance alone keeps them
        /// separate, but that is an accident of how they are laid out, and a sound leaking into the
        /// next map is not the kind of bug that announces itself. Listeners drop anything from
        /// another map before they even measure the distance.
        /// </summary>
        public readonly int Map;

        public NoiseEvent(Vector2 position, float range, NoiseKind kind, int map = Unzoned)
        {
            Position = position;
            Range = range;
            Kind = kind;
            Map = map;
        }
    }

    /// <summary>
    /// Carries sounds from whatever made them to whatever is listening.
    ///
    /// A shared bus rather than direct references because the two ends should not know each other:
    /// footsteps do not care whether a monster exists, and the monster does not care whether the
    /// noise came from a player, a thrown bottle, or a door. Both ends can be built and tested on
    /// their own.
    /// </summary>
    public static class NoiseBus
    {
        public static event Action<NoiseEvent> Heard;

        public static void Emit(NoiseEvent noise) => Heard?.Invoke(noise);

        public static void Emit(Vector2 position, float range, NoiseKind kind,
                                int map = NoiseEvent.Unzoned) =>
            Emit(new NoiseEvent(position, range, kind, map));

        // Static events survive a play session when the editor skips its domain reload, which
        // would otherwise leave listeners from the previous run wired to destroyed objects.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad() => Heard = null;
    }
}

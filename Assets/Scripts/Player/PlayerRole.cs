using Ashburn.Interaction;
using UnityEngine;

namespace Ashburn.Player
{
    /// <summary>
    /// Which of the two a character is, and what that means a prop will let them do.
    ///
    /// <c>PlayerRoles.MD</c> already splits the pair by slot — 0 is Nathan on sound and record
    /// recovery, 1 is Grant on power and machinery — but until now the split only chose a spawn
    /// point and a sprite. The gimmicks are where it becomes a rule: the elevator's motor is
    /// Grant's job and the archive is Nathan's, and a beat that either of them could finish alone
    /// is a beat that does not need two people.
    ///
    /// Read off <see cref="Inventory.Slot"/> rather than a tag or the object's name, because that
    /// is the one value <c>PlayerSpawner.Configure</c> sets on both machines, for the offline
    /// split-keyboard test and the networked spawn alike. Photon Voice renames the object out from
    /// under us — see <see cref="Inventory.SetOwnerName"/> — so the name is not a thing to judge by.
    /// </summary>
    public static class PlayerRole
    {
        /// <summary>A prop with this required slot does not care who uses it.</summary>
        public const int Anyone = -1;

        /// <summary>Player A. Sound equipment, records.</summary>
        public const int Nathan = 0;

        /// <summary>Player B. Power, mechanical plant.</summary>
        public const int Grant = 1;

        /// <summary>What to call the character in that slot, for a prompt the player reads.</summary>
        public static string NameOf(int slot) => slot switch
        {
            Nathan => "네이선",
            Grant => "그랜트",
            _ => "누구든",
        };

        /// <summary>Whether the character that just reached for something is the one it wants.</summary>
        public static bool Matches(GameObject interactor, int requiredSlot)
        {
            if (requiredSlot == Anyone)
                return true;

            var pockets = Inventory.Of(interactor);

            // Something with no pockets did this: a scripted grab or a test rig. Refusing it would
            // make the beat unfinishable from a place that has no way to prove who it is, so the
            // role is treated as satisfied and the slot is only ever used to turn one of the two
            // players away.
            return pockets == null || pockets.Slot == requiredSlot;
        }

        /// <summary>
        /// The prompt to show the wrong one of the two, naming who should be here.
        ///
        /// Phrased without a subject particle on purpose. "네이선이" and "그랜트가" take different
        /// ones, and a prompt built by gluing a name to a fixed sentence would get one of the two
        /// wrong every time.
        /// </summary>
        public static string Refusal(int requiredSlot) =>
            requiredSlot == Anyone ? string.Empty : $"{NameOf(requiredSlot)} 담당이다";
    }
}

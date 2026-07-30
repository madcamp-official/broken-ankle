using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Ashburn.Core
{
    /// <summary>
    /// Turns action bindings into the short key names a hint card can show.
    ///
    /// Read out of the input asset rather than typed in anywhere, because the bindings have already
    /// moved several times — arrow keys left the first player, a flashlight toggle arrived, a second
    /// local player appeared — and a hint card that lies is worse than none.
    ///
    /// Lives apart from whoever draws it because two things now show the same list: the corner card
    /// and the pause menu. Which actions to list and what to call them stays with each of them, since
    /// that is authored data rather than logic; if a third one appears it is worth moving those
    /// arrays into a shared asset.
    /// </summary>
    public static class BindingText
    {
        /// <summary>
        /// Unity's readable names are written out in full, which is fine in a rebinding menu and
        /// far too wide on a corner card. Four arrow keys alone run past a third of the screen.
        /// </summary>
        static readonly Dictionary<string, string> ShortNames = new()
        {
            { "Up Arrow", "↑" },
            { "Down Arrow", "↓" },
            { "Left Arrow", "←" },
            { "Right Arrow", "→" },
            { "Left Shift", "Shift" },
            { "Right Shift", "R Shift" },
            { "Left Control", "Ctrl" },
            { "Right Control", "R Ctrl" },
            { "Numpad 0", "Num 0" },
            { "Numpad .", "Num ." },
        };

        /// <summary>
        /// Only the keyboard bindings of one action, joined.
        ///
        /// A gamepad's buttons mean nothing to somebody reading a card while sitting at a keyboard,
        /// and listing both doubles the height for no gain.
        /// </summary>
        public static string Keyboard(InputAction action)
        {
            if (action == null)
                return string.Empty;

            var parts = new List<string>();

            foreach (var binding in action.bindings)
            {
                if (binding.isComposite || string.IsNullOrEmpty(binding.path))
                    continue;

                if (!binding.path.StartsWith("<Keyboard>"))
                    continue;

                var readable = InputControlPath.ToHumanReadableString(
                    binding.path, InputControlPath.HumanReadableStringOptions.OmitDevice);

                if (ShortNames.TryGetValue(readable, out var shortened))
                    readable = shortened;

                if (!parts.Contains(readable))
                    parts.Add(readable);
            }

            // A stick composite reads as four separate keys, which is exactly what the player
            // presses, so they are joined rather than summarised.
            return string.Join(" ", parts);
        }

        /// <summary>
        /// Fills two columns with a heading per action map and a row per action.
        ///
        /// Two columns rather than one padded string, because Korean glyphs are twice the width of
        /// Latin ones and padding with spaces never lines up.
        ///
        /// Actions with no keyboard binding are skipped rather than shown blank: an action that
        /// exists only on a gamepad is not a key the reader can press.
        /// </summary>
        public static void BuildRows(InputActionAsset asset, string[] maps, string[] mapTitles,
                                     string[] actions, string[] labels,
                                     List<string> labelColumn, List<string> keyColumn)
        {
            labelColumn.Clear();
            keyColumn.Clear();

            if (asset == null)
            {
                labelColumn.Add("(입력 에셋이 연결되지 않음)");
                keyColumn.Add(string.Empty);
                return;
            }

            for (var m = 0; m < maps.Length; m++)
            {
                var map = asset.FindActionMap(maps[m], throwIfNotFound: false);
                if (map == null)
                    continue;

                if (labelColumn.Count > 0)
                {
                    labelColumn.Add(string.Empty);
                    keyColumn.Add(string.Empty);
                }

                labelColumn.Add(m < mapTitles.Length ? mapTitles[m] : maps[m]);
                keyColumn.Add(string.Empty);

                for (var a = 0; a < actions.Length; a++)
                {
                    var action = map.FindAction(actions[a]);
                    if (action == null)
                        continue;

                    var keys = Keyboard(action);
                    if (keys.Length == 0)
                        continue;

                    labelColumn.Add("  " + (a < labels.Length ? labels[a] : actions[a]));
                    keyColumn.Add(keys);
                }
            }
        }
    }
}

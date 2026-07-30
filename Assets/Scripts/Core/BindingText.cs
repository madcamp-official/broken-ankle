using System.Collections.Generic;
using Ashburn.Interaction;
using UnityEngine;
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
    /// One player's keys, not a table of everybody's. This is a game two people play on two machines,
    /// each holding one character, so the only keys worth printing on a screen are the keys belonging
    /// to whoever is looking at it — and they need no "1P" above them, because there is nobody at this
    /// keyboard for them to be told apart from.
    ///
    /// Lives apart from whoever draws it because two things show the same list: the corner card and
    /// the pause menu. Which actions to list and what to call them stays with each of them, since that
    /// is authored data rather than logic.
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
            // A stick composite reads as four separate keys, which is exactly what the player
            // presses, so they are joined rather than summarised.
            return string.Join(" ", KeyboardKeys(action));
        }

        /// <summary>
        /// The first keyboard key of one action, or empty.
        ///
        /// For a one-line prompt, where naming every alternative is worse than naming one: player B's
        /// interact key is bound twice and "Num 0 R Ctrl 상자를 연다" reads as three words of
        /// nonsense followed by the sentence.
        /// </summary>
        public static string FirstKeyboard(InputAction action)
        {
            foreach (var key in KeyboardKeys(action))
                return key;

            return string.Empty;
        }

        static List<string> KeyboardKeys(InputAction action)
        {
            var parts = new List<string>();
            if (action == null)
                return parts;

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

            return parts;
        }

        /// <summary>
        /// The action map the character at this keyboard is actually driving.
        ///
        /// Asked of the character rather than assumed, so the card cannot claim a key the player does
        /// not have. Over the network both people use the first map — each has a keyboard to
        /// themselves — and in the split-keyboard test the viewer is whichever character the screen
        /// belongs to, which is the one whose keys are worth printing either way.
        ///
        /// Falls back to a named map for the moment before anybody has spawned, so the card has
        /// something to show on the first frame rather than a gap.
        /// </summary>
        public static InputActionMap LocalMap(string viewerTag, InputActionAsset fallbackAsset,
                                              string fallbackMap)
        {
            var tagged = string.IsNullOrEmpty(viewerTag)
                ? null
                : GameObject.FindGameObjectWithTag(viewerTag);

            var interactor = tagged != null ? tagged.GetComponent<PlayerInteractor>() : null;
            var action = interactor != null ? interactor.InteractAction : null;
            if (action != null && action.actionMap != null)
                return action.actionMap;

            return fallbackAsset != null
                ? fallbackAsset.FindActionMap(fallbackMap, throwIfNotFound: false)
                : null;
        }

        /// <summary>
        /// Fills two columns with a row per action, for one player.
        ///
        /// Two columns rather than one padded string, because Korean glyphs are twice the width of
        /// Latin ones and padding with spaces never lines up.
        ///
        /// Actions with no keyboard binding are skipped rather than shown blank: an action that
        /// exists only on a gamepad is not a key the reader can press.
        /// </summary>
        public static void BuildRows(InputActionMap map, string[] actions, string[] labels,
                                     List<string> labelColumn, List<string> keyColumn)
        {
            labelColumn.Clear();
            keyColumn.Clear();

            if (map == null)
            {
                labelColumn.Add("(입력 맵을 찾을 수 없음)");
                keyColumn.Add(string.Empty);
                return;
            }

            for (var a = 0; a < actions.Length; a++)
            {
                var action = map.FindAction(actions[a]);
                if (action == null)
                    continue;

                var keys = Keyboard(action);
                if (keys.Length == 0)
                    continue;

                labelColumn.Add(a < labels.Length ? labels[a] : actions[a]);
                keyColumn.Add(keys);
            }
        }
    }
}

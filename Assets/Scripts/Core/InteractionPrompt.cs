using Ashburn.Interaction;
using UnityEngine;

namespace Ashburn.Core
{
    /// <summary>
    /// The bar above the bottom of the screen that says what the player is standing in front of and
    /// which key does it: <c>E  마을로 이동하기</c>.
    ///
    /// Both halves are read rather than written down. The key comes out of the action the character
    /// actually responds to, so it cannot go stale when a binding moves — and in the split-keyboard
    /// test each character names its own key rather than both claiming E. The sentence comes from the
    /// interactable's own <see cref="IInteractable.Prompt"/>, which is where a door already decides
    /// whether to say it opens or that it is locked.
    ///
    /// It shows for locked things too, and that is deliberate: <see cref="Ashburn.World.MapDoor"/> and
    /// <see cref="Ashburn.World.LockedDoor"/> both keep answering CanInteract while shut so the player
    /// learns the door is a door and that it is the lock stopping them, rather than the level having
    /// no way on. Saying nothing at all would undo that.
    ///
    /// While an interaction with a timeout is being held — a Hold, a SlowTap — the bar fills up as it
    /// completes. Driven off the action's own timeout rather than a timer of ours, so a key that is
    /// not a hold simply never shows a bar and nothing has to be configured to say which is which.
    /// </summary>
    public class InteractionPrompt : MonoBehaviour
    {
        [Header("Whose screen this is")]
        [Tooltip("Tag PlayerRig puts on the character the screen belongs to. The prompt follows that " +
                 "character, so a partner's doorway is not announced on this player's screen.")]
        [SerializeField] string viewerTag = "Player";

        [Header("Look")]
        [Tooltip("Height above the bottom of the picture, in the game's own pixels.")]
        [SerializeField] float bottomMargin = 28f;

        [SerializeField] int fontSize = 12;

        [Tooltip("Gap between the key and the sentence.")]
        [SerializeField] float gutter = 9f;

        [SerializeField] Color textColour = new(0.95f, 0.95f, 0.98f);
        [SerializeField] Color panelColour = new(0.05f, 0.05f, 0.07f, 0.86f);

        [Tooltip("The key's own box, so it reads as a key rather than as the first word.")]
        [SerializeField] Color keyColour = new(0.22f, 0.23f, 0.28f, 0.96f);

        [Tooltip("Fills the bar as a held interaction completes.")]
        [SerializeField] Color progressColour = new(0.85f, 0.76f, 0.42f, 0.95f);

        [SerializeField] float progressHeight = 3f;

        PlayerInteractor _interactor;
        GUIStyle _text;
        GUIStyle _key;

        void OnGUI()
        {
            // Not while the menu is up: it dims the picture and takes the player's hands off the keys,
            // so a prompt naming a key that currently does nothing would be a lie.
            if (PauseMenu.AnyOpen)
                return;

            var interactor = Interactor();
            if (interactor == null)
                return;

            var target = interactor.CurrentTarget;
            if (target == null)
                return;

            var sentence = target.Prompt;
            if (string.IsNullOrEmpty(sentence))
                return;

            EnsureStyles();

            var key = BindingText.FirstKeyboard(interactor.InteractAction);
            var keySize = _key.CalcSize(new GUIContent(key));
            var textSize = _text.CalcSize(new GUIContent(sentence));

            const float pad = 8f;
            var keyWidth = string.IsNullOrEmpty(key) ? 0f : Mathf.Max(keySize.x + 10f, 20f);
            var height = Mathf.Max(keySize.y, textSize.y) + pad;
            var width = keyWidth + (keyWidth > 0f ? gutter : 0f) + textSize.x + pad * 2f;

            // Centred on the camera's viewport, not the window: Pixel Perfect letterboxes the game
            // into the middle of a larger window and the window's centre is not the picture's.
            var viewport = Viewport();
            var bar = new Rect(
                viewport.x + (viewport.width - width) * 0.5f,
                viewport.yMax - bottomMargin - height,
                width, height);

            Imgui.Fill(bar, panelColour);

            var y = bar.y + (bar.height - textSize.y) * 0.5f;
            var x = bar.x + pad;

            if (keyWidth > 0f)
            {
                var keyBox = new Rect(x, bar.y + 3f, keyWidth, bar.height - 6f);
                Imgui.Fill(keyBox, keyColour);
                GUI.Label(keyBox, key, _key);
                x += keyWidth + gutter;
            }

            GUI.Label(new Rect(x, y, textSize.x, textSize.y), sentence, _text);

            DrawProgress(interactor, bar);
        }

        /// <summary>
        /// Fills the foot of the bar as a held interaction completes.
        ///
        /// The Input System already counts this out for whatever interaction is on the action, so it
        /// is asked rather than timed here: a Hold of a different length, or a SlowTap instead, needs
        /// no change. Nothing in progress reads as zero, which is also every press-and-go action, so
        /// the bar stays hidden on its own.
        /// </summary>
        void DrawProgress(PlayerInteractor interactor, Rect bar)
        {
            var action = interactor.InteractAction;
            if (action == null)
                return;

            // Two sources, whichever is running. The Input System counts out a Hold or a SlowTap
            // put on the action itself; the interactor counts out a target that asked to be held
            // without the shared key becoming a hold for doors and breakers too.
            var completion = Mathf.Max(action.GetTimeoutCompletionPercentage(),
                                       interactor.HoldProgress);
            if (completion <= 0f)
                return;

            var track = new Rect(bar.x, bar.yMax - progressHeight, bar.width, progressHeight);
            Imgui.Fill(track, new Color(0f, 0f, 0f, 0.45f));
            Imgui.Fill(new Rect(track.x, track.y, track.width * Mathf.Clamp01(completion), track.height),
                       progressColour);
        }

        /// <summary>
        /// The viewer's interactor, looked up again whenever it is gone.
        ///
        /// Not cached once: characters are created after this object exists, and the viewer is
        /// replaced outright when a networked game takes over from the offline fill.
        /// </summary>
        PlayerInteractor Interactor()
        {
            if (_interactor != null && _interactor.isActiveAndEnabled)
                return _interactor;

            var tagged = GameObject.FindGameObjectWithTag(viewerTag);
            _interactor = tagged != null ? tagged.GetComponent<PlayerInteractor>() : null;
            return _interactor;
        }

        Rect Viewport()
        {
            var camera = Camera.main;
            if (camera == null)
                return new Rect(0f, 0f, Screen.width, Screen.height);

            // pixelRect counts up from the bottom of the window, GUI coordinates down from the top.
            var view = camera.pixelRect;
            return new Rect(view.x, Screen.height - view.yMax, view.width, view.height);
        }

        void EnsureStyles()
        {
            if (_text != null)
                return;

            _text = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0),
                wordWrap = false,
            };
            _text.normal.textColor = textColour;

            _key = new GUIStyle(_text)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            _key.normal.textColor = textColour;
        }
    }
}

using System.Collections;
using UnityEngine;

namespace Ashburn.Core
{
    /// <summary>
    /// Covers the screen, so a change of view can happen where nobody is looking.
    ///
    /// Drawn in OnGUI like <see cref="PauseMenu"/> rather than through a Canvas, because it
    /// is one rectangle and a Canvas would mean a prefab, a sorting order and a thing to forget to
    /// put in the scene. This has nothing to wire.
    ///
    /// The fade is what makes a room change read as moving somewhere rather than as the camera
    /// sliding. Sliding shows the player the wall between two rooms and tells them the building is
    /// a flat drawing; a moment of black lets the next room simply be there.
    /// </summary>
    public class ScreenFade : MonoBehaviour
    {
        [Tooltip("What the screen fades to. Black unless a scene wants something worse.")]
        [SerializeField] Color colour = Color.black;

        [Tooltip("IMGUI draw order. Lower is drawn on top, so this sits over the controls card.")]
        [SerializeField] int depth = -1000;

        /// <summary>The fade in the current scene, if there is one.</summary>
        public static ScreenFade Current { get; private set; }

        /// <summary>How covered the screen is, 0 clear to 1 solid.</summary>
        public float Alpha { get; private set; }

        /// <summary>True while a fade is in progress.</summary>
        public bool IsBusy { get; private set; }

        Texture2D _pixel;

        void Awake()
        {
            // Outlives the scene, because the whole point of a map change is that the screen stays
            // covered across the load. A fade that belonged to the old scene would be destroyed
            // halfway through and show the player the new map assembling itself.
            if (Current != null && Current != this)
            {
                Destroy(gameObject);
                return;
            }

            Current = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);

            _pixel = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            _pixel.SetPixel(0, 0, Color.white);
            _pixel.Apply();
        }

        void OnDestroy()
        {
            if (Current == this)
                Current = null;

            if (_pixel != null)
                Destroy(_pixel);
        }

        /// <summary>
        /// Fades to <paramref name="target"/> over the given seconds. Yield on it from a coroutine
        /// to wait for it to finish.
        /// </summary>
        public IEnumerator To(float target, float seconds)
        {
            target = Mathf.Clamp01(target);

            if (seconds <= 0f)
            {
                Alpha = target;
                yield break;
            }

            IsBusy = true;

            var from = Alpha;
            var elapsed = 0f;

            while (elapsed < seconds)
            {
                // Unscaled: a fade that stops when the game is paused would trap the player
                // behind a black screen.
                elapsed += Time.unscaledDeltaTime;
                Alpha = Mathf.Lerp(from, target, Mathf.Clamp01(elapsed / seconds));
                yield return null;
            }

            Alpha = target;
            IsBusy = false;
        }

        /// <summary>Sets the cover immediately, for a cut or for starting a scene already black.</summary>
        public void Set(float alpha) => Alpha = Mathf.Clamp01(alpha);

        void OnGUI()
        {
            if (Alpha <= 0.001f || _pixel == null)
                return;

            GUI.depth = depth;

            var previous = GUI.color;
            GUI.color = new Color(colour.r, colour.g, colour.b, Alpha);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _pixel);
            GUI.color = previous;
        }

        // Statics outlive a play session when the editor skips its domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad() => Current = null;
    }
}

using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Ashburn.Core
{
    /// <summary>
    /// The small amount of drawing the project's stopgap IMGUI screens share.
    ///
    /// There are six of them now — the controls card, the pause menu, the interaction prompt, the
    /// dialogue box, the game-over card and the title screen — and they are here rather than in a
    /// Canvas because none is worth authoring scene objects for until there is menu art. When that
    /// arrives this file goes with them.
    /// </summary>
    public static class Imgui
    {
        /// <summary>The size the game is drawn at before the camera scales it up.</summary>
        public const int ReferenceWidth = 640;
        public const int ReferenceHeight = 360;

        // Camera.main walks the scene looking for a tag. These screens ask for it several times a
        // frame between them, so the answer is kept until it stops being true.
        static Camera _camera;
        static PixelPerfectCamera _pixelPerfect;

        /// <summary>
        /// Lays a screen out in the game's own 640x360 pixels instead of the monitor's.
        ///
        /// Everything here draws in real screen pixels, which IMGUI does not scale for anybody. The
        /// world does not have that problem — the Pixel Perfect Camera blows 640x360 up by a whole
        /// number — so on a 1080p monitor the game is drawn at 3x while a 13-pixel caption stayed 13
        /// pixels: about one part in eighty of the screen's height, next to characters three times
        /// the size they were authored at. Every layout number in these files was picked by eye
        /// against the reference size, and this is what makes those numbers mean what they meant.
        ///
        /// The camera's own scale is used rather than one worked out here, so the menu steps up at
        /// exactly the window sizes the picture does and the two can never disagree by one.
        ///
        /// Used with <c>using</c>, because these screens return out of the middle of themselves and
        /// a matrix left behind would be inherited by whatever drew next.
        /// </summary>
        public static ScaledScreen Scaled() => new(Viewport());

        /// <summary>
        /// The part of the window the game is actually drawn in, in real screen pixels.
        ///
        /// Not the window. The crop frame is Windowbox, so anything anchored to a window corner
        /// lands out in the black bars where it is easy to miss entirely.
        /// </summary>
        public static Rect Viewport()
        {
            var camera = Camera.main;
            if (camera == null)
                return new Rect(0f, 0f, Screen.width, Screen.height);

            // pixelRect counts up from the bottom of the window, GUI coordinates down from the top.
            var view = camera.pixelRect;
            return new Rect(view.x, Screen.height - view.yMax, view.width, view.height);
        }

        /// <summary>
        /// How many screen pixels one of the game's pixels covers. Always a whole number.
        ///
        /// Asked of the camera, which has already decided it. Only when there is no such camera is
        /// it worked out from the viewport, and then it is floored: a fractional scale would put the
        /// menu on half pixels, which is the blur this whole thing exists to avoid.
        /// </summary>
        public static int Scale(Rect viewport)
        {
            var camera = Camera.main;

            if (camera != _camera)
            {
                _camera = camera;
                _pixelPerfect = camera != null ? camera.GetComponent<PixelPerfectCamera>() : null;
            }

            if (_pixelPerfect != null && _pixelPerfect.isActiveAndEnabled && _pixelPerfect.pixelRatio > 0)
                return _pixelPerfect.pixelRatio;

            return Mathf.Max(1, Mathf.FloorToInt(Mathf.Min(viewport.width / ReferenceWidth,
                                                           viewport.height / ReferenceHeight)));
        }

        /// <summary>
        /// A screen being drawn at the game's scale. See <see cref="Scaled"/>.
        ///
        /// <see cref="Area"/> is what a layout should measure itself against: it starts at zero
        /// whatever corner of the window the picture happens to be in, and it is about 640x360
        /// whatever the monitor is.
        /// </summary>
        public readonly struct ScaledScreen : System.IDisposable
        {
            readonly Matrix4x4 _restore;

            /// <summary>The drawable area in the game's own pixels, with its origin at zero.</summary>
            public Rect Area { get; }

            /// <summary>Screen pixels per game pixel. Rarely needed; the matrix handles the rest.</summary>
            public int Factor { get; }

            internal ScaledScreen(Rect viewport)
            {
                Factor = Scale(viewport);
                _restore = GUI.matrix;

                // Translation and scale together: the origin moves to the corner of the picture, so
                // a layout never has to know about the bars, and lengths multiply up from there.
                // IMGUI applies the inverse of this to the mouse before hit-testing, so buttons keep
                // working without anything being converted by hand.
                GUI.matrix = Matrix4x4.TRS(new Vector3(viewport.x, viewport.y, 0f),
                                           Quaternion.identity,
                                           new Vector3(Factor, Factor, 1f));

                Area = new Rect(0f, 0f, viewport.width / Factor, viewport.height / Factor);
            }

            public void Dispose() => GUI.matrix = _restore;
        }

        /// <summary>
        /// Blocks in a colour.
        ///
        /// The conversion is the whole point of this existing. The project renders in linear colour
        /// space, where IMGUI gamma-encodes what it is given on the way to the screen: both of these
        /// screens asked for a panel of 0.06 and got mid grey, about 0.27, which is exactly that
        /// encoding applied to a number that had not been decoded first. Converting to linear here
        /// cancels it, so the colour picked in the inspector is the colour that appears. Alpha is not
        /// a brightness and <see cref="Color.linear"/> leaves it alone.
        ///
        /// Tinting the built-in white texture rather than each screen keeping a 1x1 texture of its
        /// own, which is what they used to do — one place to get this right, and nothing to create or
        /// destroy.
        /// </summary>
        public static void Fill(Rect rect, Color colour)
        {
            var was = GUI.color;
            GUI.color = QualitySettings.activeColorSpace == ColorSpace.Linear ? colour.linear : colour;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = was;
        }
    }
}

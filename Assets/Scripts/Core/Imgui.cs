using UnityEngine;

namespace Ashburn.Core
{
    /// <summary>
    /// The small amount of drawing the project's stopgap IMGUI screens share.
    ///
    /// There are two of them — the controls card and the pause menu — and they are here rather than in
    /// a Canvas because neither is worth authoring scene objects for until there is menu art. When
    /// that arrives this file goes with them.
    /// </summary>
    public static class Imgui
    {
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

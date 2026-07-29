using UnityEngine;

namespace Ashburn.EditorTools
{
    /// <summary>
    /// A greybox map, written as text.
    ///
    /// Kept as an asset so the layout is versioned with the scene it produces, and because the
    /// layout is the thing that gets edited twenty times in an evening; moving a wall should be
    /// retyping one character, not dragging a cube and then remembering which of its three
    /// components needed setting.
    /// </summary>
    class GreyboxSettings : ScriptableObject
    {
        [Tooltip("The map. Rows may be ragged; short ones are treated as empty on the right.\n\n" +
                 "#  wall        .  floor       (space) nothing\n" +
                 "+  doorway     O  pillar      B  breaker box\n" +
                 "N  nest        L  lamp        1 2  spawn points")]
        [TextArea(14, 40)]
        public string layout =
            "###########################################\n" +
            "#.............#.............#.............#\n" +
            "#.............#.............#.............#\n" +
            "#....O........#......B......#......N......#\n" +
            "#.............#.............#.............#\n" +
            "#.............#.............#.............#\n" +
            "#.............#.............#.............#\n" +
            "#.............#.............#.............#\n" +
            "#######+#############+#############+#######\n" +
            "#.........................................#\n" +
            "#..1..2...................................#\n" +
            "#.........................................#\n" +
            "###########################################\n";

        [Tooltip("World units per character. The player is about one unit across, so 1 gives " +
                 "corridors you can read the width of by counting.")]
        public float cellSize = 1f;

        [Tooltip("Moves the generated layout in world space. Use half-cell offsets when an odd " +
                 "map dimension needs to line up with Unity Tilemap cell centres.")]
        public Vector2 layoutOffset;

        [Header("Templates")]
        [Tooltip("Instanced for every wall run. Scaled, so its sprite should be one unit at scale 1.")]
        public GameObject wall;

        [Tooltip("Instanced per floor rectangle. Needs a Tiled sprite renderer; its size is set, " +
                 "not its scale, so the grid does not stretch.")]
        public GameObject floor;

        public GameObject pillar;
        public GameObject breakerBox;
        public GameObject nest;
        public GameObject lamp;

        [Header("Output")]
        [Tooltip("Everything generated goes under one object with this name, and rebuilding " +
                 "replaces it. Nothing outside it is touched.")]
        public string rootName = "Level (generated)";

        [Tooltip("Point the scene's PlayerSpawner at the generated spawn points. Without this a " +
                 "rebuild leaves it holding references to objects that no longer exist.")]
        public bool rewireSpawner = true;
    }
}

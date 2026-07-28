using UnityEngine;

namespace Ashburn.EditorTools
{
    /// <summary>
    /// A greybox map, written as text.
    ///
    /// Kept as an asset so the layout is versioned with the scene it produces, and because the
    /// layout is the thing that gets edited twenty times in an evening — moving a wall should be
    /// retyping one character, not dragging a cube and then remembering which of its three
    /// components needed setting.
    /// </summary>
    class GreyboxSettings : ScriptableObject
    {
        /// <summary>
        /// One map's shape. Kept as a list rather than a single field because a house and the
        /// street it stands on are two scenes, and building one used to mean pasting the other
        /// one's text somewhere safe first.
        /// </summary>
        [System.Serializable]
        public class Map
        {
            [Tooltip("What this map is called here. Matches its scene name if you like.")]
            public string name = "Map";

            [Tooltip("Rows may be ragged; short ones are treated as empty on the right.\n\n" +
                     "#  wall        .  floor       (space) nothing\n" +
                     "+  doorway     O  pillar      B  breaker box\n" +
                     "N  nest        L  lamp        1 2  spawn points\n" +
                     "D  way to another map (set its target in the inspector)\n" +
                     "E  where arrivals from another map come out")]
            [TextArea(14, 40)]
            public string layout = "";

            [Tooltip("Scene name every D in this layout leads to. Set here rather than per door, " +
                     "because a map usually has one way out and filling it in by hand after every " +
                     "rebuild is how it ends up empty.")]
            public string doorTarget = "";

            [Tooltip("Id of the MapEntry to arrive at over there. 'Default' matches a generated E.")]
            public string doorEntry = "Default";
        }

        [Tooltip("Every map this project builds. Build acts on the one picked above the button.")]
        public Map[] maps =
        {
            new Map { name = "House", layout = HouseLayout, doorTarget = "Street" },
            new Map { name = "Street", layout = StreetLayout, doorTarget = "House" },
        };

        [Tooltip("Index of the map to build. Set from the dropdown, not by hand.")]
        public int selected;

        /// <summary>The map the build button acts on, or null when the list is empty.</summary>
        public Map Selected => maps != null && maps.Length > 0
            ? maps[Mathf.Clamp(selected, 0, maps.Length - 1)]
            : null;

        /// <summary>
        /// Inside the house: three rooms off one corridor, with the front door at the bottom left
        /// and the mark beside it where somebody coming in from the street arrives.
        /// </summary>
        const string HouseLayout =
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
            "#..1..2..............L....................#\n" +
            "#..E......................................#\n" +
            "###D#######################################\n";

        /// <summary>
        /// The estate outside. Two terraces facing each other across the road, the middle one on
        /// the south side being the house the interior belongs to — its door is the D.
        ///
        /// The neighbours are solid: they are shapes to walk around and to lose sight of each
        /// other behind, not places. Ashburn.MD opens on the players arriving from the south, so
        /// the spawn marks sit on the access road at the bottom.
        /// </summary>
        const string StreetLayout =
            "##############################################################################\n" +
            "#............................................................................#\n" +
            "#............................................................................#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#............................................................................#\n" +
            "#.......................O.........................O..........................#\n" +
            "#............................................................................#\n" +
            "#............................................................................#\n" +
            "#.........L...............L...............L...............L...........L......#\n" +
            "#...................................................................B........#\n" +
            "#............................................................................#\n" +
            "#.......................O.........................O..........................#\n" +
            "#.....................................E......................................#\n" +
            "#....################..........#######D########..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#....################..........################..........################....#\n" +
            "#............................................................................#\n" +
            "#............................................................................#\n" +
            "#.............O......................1..2.......................O............#\n" +
            "#............................................................................#\n" +
            "#............................................................................#\n" +
            "##############################################################################\n";

        [Tooltip("World units per character. The player is about one unit across, so 1 gives " +
                 "corridors you can read the width of by counting.")]
        public float cellSize = 1f;

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

        [Tooltip("Character to spawn. Used when a fresh scene has no PlayerSpawner yet, so a new " +
                 "map is playable the moment it is built.")]
        public GameObject playerPrefab;

        [Tooltip("Camera rig dropped into a scene that has none. Assets/Prefabs/CameraRig.")]
        public GameObject cameraRig;

        [Header("Lamp")]
        [Tooltip("Used when there is no lamp template. With no global light in a horror scene the " +
                 "lamps are the only fixed light there is, so L has to work without one.")]
        public float lampRadius = 4.5f;

        [Tooltip("Radius of the fully lit core. The gap out to lampRadius is the falloff.")]
        public float lampInnerRadius = 0.6f;

        public float lampIntensity = 0.85f;

        [Tooltip("Sodium street lighting is warm and slightly sick, which suits the place.")]
        public Color lampColour = new Color(1f, 0.86f, 0.66f);

        [Header("Map light")]
        [Tooltip("How much light the map has with the power out. Must stay well under the " +
                 "flashlight or the beam stops reading as a beam, and must not be zero or the " +
                 "beam becomes a hole onto black.")]
        public float darkIntensity = 0.25f;

        public Color darkColour = Color.white;

        [Tooltip("How much light the map has once the breaker is thrown.")]
        public float poweredIntensity = 3f;

        public Color poweredColour = Color.white;

        [Header("Output")]
        [Tooltip("Everything generated goes under one object with this name, and rebuilding " +
                 "replaces it. Nothing outside it is touched.")]
        public string rootName = "Level (generated)";

        [Tooltip("Lay the floor as one piece under the whole map instead of one object per " +
                 "rectangle. The walls are drawn over the top, so it looks identical. Ignored for " +
                 "a layout with holes in it, where one piece would floor over the gaps.")]
        public bool singleFloor = true;

        [Tooltip("Take the spawner, the fade, the camera and the controls overlay out of the map " +
                 "scene. They belong to the systems scene: several maps are loaded at once, and a " +
                 "second copy of each of them arrives with every one of them.")]
        public bool stripSystems = true;

        [Tooltip("Create a PowerGrid if the scene has none and point it at the lights, so the " +
                 "breaker works. Rebuilding used to lose this wiring silently.")]
        public bool wirePower = true;

        [Tooltip("Floor regions smaller than this are cupboards, not rooms, and get no camera of " +
                 "their own.")]
        public int minRoomCells = 12;

        [Tooltip("Add the scene being built to the build's scene list. A map missing from it cannot " +
                 "be loaded at runtime, and the door leading to it fails with nothing on screen to " +
                 "say why.")]
        public bool addToBuildList = true;
    }
}

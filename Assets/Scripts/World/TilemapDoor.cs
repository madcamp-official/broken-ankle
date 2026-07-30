using Ashburn.Noise;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Ashburn.World
{
    /// <summary>
    /// A door drawn into the tilemaps that opens when something else in the building says so.
    ///
    /// The third kind of door, and the first that nobody walks up to. <see cref="MapDoor"/> carries
    /// a character into another scene and <see cref="LockedDoor"/> is a wall that stops being one
    /// when the pair are carrying the right thing; this is the hangar's, which opens because Nathan
    /// pressed a button in the office two rooms away. That is the point of it — the players are
    /// separated, and the only sign the button did anything is a door unlocking somewhere Grant can
    /// hear.
    ///
    /// Both pictures are already in the scene: the artist drew the shut leaf on one tilemap and the
    /// open one on another. So rather than being handed tile assets to stamp, this takes the open
    /// cells out at load and puts them back on opening — the scene keeps looking in the editor
    /// exactly like the thing it will become, and no reference here can point at the wrong tile.
    /// </summary>
    public class TilemapDoor : MonoBehaviour
    {
        [Header("What opens it")]
        [Tooltip("WorldState flag that swings it. Raised by a button, a repair, or a story beat. " +
                 "It is world state rather than an event, so a player who walks up a minute later " +
                 "finds it open.")]
        [SerializeField] string requiredFlag;

        [Header("Shut")]
        [Tooltip("Tilemap holding the closed leaf.")]
        [SerializeField] Tilemap closedTiles;

        [Tooltip("Cells of the closed leaf, cleared when it opens.")]
        [SerializeField] Vector3Int[] closedCells;

        [Header("Open")]
        [Tooltip("Tilemap holding the open leaf. Its cells are lifted out at load and put back " +
                 "when it opens, so the scene can be authored with both states visible.")]
        [SerializeField] Tilemap openTiles;

        [Tooltip("Cells of the open leaf, hidden until it opens.")]
        [SerializeField] Vector3Int[] openCells;

        [Header("What stands in the way")]
        [Tooltip("Turned off once it is open. The tilemaps are only the picture — this is what " +
                 "actually stops somebody walking through, because the wall layer has a hole cut " +
                 "in it where a door goes.")]
        [SerializeField] GameObject[] blockers;

        [Header("Noise")]
        [Tooltip("How far the lock carries. Grant is meant to hear this from the other room.")]
        [SerializeField] float noiseRange = 12f;

        TileBase[] _liftedOpen;

        /// <summary>Whether it is open, now or because whoever got here first opened it.</summary>
        public bool IsOpen => string.IsNullOrEmpty(requiredFlag) || WorldState.Has(requiredFlag);

        void Awake() => LiftOpenLeaf();

        void OnEnable()
        {
            WorldState.Set += OnFlagSet;

            // Already open when this map loads, because the pair opened it an hour ago or because
            // the other machine opened it while this one was elsewhere.
            if (IsOpen)
                Open(silent: true);
            else
                Shut();
        }

        void OnDisable() => WorldState.Set -= OnFlagSet;

        void OnFlagSet(string flag)
        {
            if (flag == requiredFlag)
                Open(silent: false);
        }

        /// <summary>
        /// Takes the open leaf out of the tilemap and remembers it.
        ///
        /// Done once at Awake rather than every time the door shuts, because the tilemap is the
        /// only copy of those tiles and reading them back after they have been cleared would
        /// remember nothing.
        /// </summary>
        void LiftOpenLeaf()
        {
            if (openTiles == null || openCells == null)
                return;

            _liftedOpen = new TileBase[openCells.Length];
            for (var i = 0; i < openCells.Length; i++)
                _liftedOpen[i] = openTiles.GetTile(openCells[i]);
        }

        void Shut()
        {
            if (openTiles != null && openCells != null)
                foreach (var cell in openCells)
                    openTiles.SetTile(cell, null);

            foreach (var blocker in blockers)
                if (blocker != null)
                    blocker.SetActive(true);
        }

        void Open(bool silent)
        {
            if (closedTiles != null && closedCells != null)
                foreach (var cell in closedCells)
                    closedTiles.SetTile(cell, null);

            if (openTiles != null && _liftedOpen != null)
                for (var i = 0; i < openCells.Length; i++)
                    openTiles.SetTile(openCells[i], _liftedOpen[i]);

            foreach (var blocker in blockers)
                if (blocker != null)
                    blocker.SetActive(false);

            if (!silent)
                NoiseBus.Emit(transform.position, noiseRange, NoiseKind.Self, MapZone.IdOf(this));
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.9f);
            Gizmos.DrawWireCube(transform.position, new Vector3(1f, 1f, 0f));

            if (closedTiles == null || closedCells == null)
                return;

            foreach (var cell in closedCells)
                Gizmos.DrawWireCube(closedTiles.GetCellCenterWorld(cell), new Vector3(0.9f, 0.9f, 0f));
        }
    }
}

using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

namespace Ashburn.Core
{
    /// <summary>
    /// Points a <see cref="CinemachineCamera"/> at whichever object is tagged Player once one
    /// exists.
    ///
    /// A prefab cannot store a reference to an object that lives in a scene, so without this the
    /// camera rig would have to be wired by hand in every room. Finding the target at runtime is
    /// what lets the camera ship as a drop-in prefab alongside Player.prefab.
    ///
    /// It keeps looking rather than checking once, because the character it is looking for does
    /// not exist yet when the scene starts. PlayerSpawner creates it, and two components' Start
    /// methods run in no particular order. Looking once meant the camera followed nothing whenever
    /// it happened to win that race, which changed every time the scene was rearranged. Waiting
    /// also covers a networked spawn, where the character arrives some frames after the level.
    /// </summary>
    [RequireComponent(typeof(CinemachineCamera))]
    public class CinemachinePlayerBinder : MonoBehaviour
    {
        [Tooltip("Tag to search for. Leave as Player unless the rig is following something else.")]
        [SerializeField] string targetTag = "Player";

        [Tooltip("Also drive LookAt. Off is correct for a top-down 2D game, which never rotates.")]
        [SerializeField] bool bindLookAt = false;

        [Tooltip("How long to keep looking before deciding nothing is coming and saying so. " +
                 "Long enough for a slow join, short enough to still be a useful error.")]
        [SerializeField] float giveUpAfter = 10f;

        IEnumerator Start()
        {
            var camera = GetComponent<CinemachineCamera>();

            // A target wired by hand in the scene beats anything we could guess, so never
            // overwrite one that is already set.
            if (camera.Follow != null)
                yield break;

            var deadline = Time.time + giveUpAfter;
            GameObject target;

            while ((target = GameObject.FindGameObjectWithTag(targetTag)) == null)
            {
                if (Time.time >= deadline)
                {
                    Debug.LogWarning(
                        $"Waited {giveUpAfter}s and no GameObject tagged '{targetTag}' appeared, so " +
                        "the camera has nothing to follow. Check that a PlayerSpawner is in the " +
                        "scene and enabled.", this);
                    yield break;
                }

                yield return null;
            }

            // Switching this component off does not stop a coroutine it already started, and the
            // wait above outlives the moment RoomCamera takes the camera over: it disables this and
            // clears Follow, and a frame later the loop below would hand the camera straight back.
            // The result is a camera that frames a room for one frame and then trails the player
            // around inside it, which looks like the room framing never worked at all.
            if (!enabled)
                yield break;

            camera.Follow = target.transform;
            if (bindLookAt)
                camera.LookAt = target.transform;
        }
    }
}

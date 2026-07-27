using Unity.Cinemachine;
using UnityEngine;

namespace Ashburn.Core
{
    /// <summary>
    /// Points a <see cref="CinemachineCamera"/> at whichever object is tagged Player once the
    /// scene starts.
    ///
    /// A prefab cannot store a reference to an object that lives in a scene, so without this the
    /// camera rig would have to be wired by hand in every room. Finding the target at runtime is
    /// what lets the camera ship as a drop-in prefab alongside Player.prefab.
    /// </summary>
    [RequireComponent(typeof(CinemachineCamera))]
    public class CinemachinePlayerBinder : MonoBehaviour
    {
        [Tooltip("Tag to search for. Leave as Player unless the rig is following something else.")]
        [SerializeField] string targetTag = "Player";

        [Tooltip("Also drive LookAt. Off is correct for a top-down 2D game, which never rotates.")]
        [SerializeField] bool bindLookAt = false;

        void Start()
        {
            var camera = GetComponent<CinemachineCamera>();

            // A target wired by hand in the scene beats anything we could guess, so never
            // overwrite one that is already set.
            if (camera.Follow != null)
                return;

            var target = GameObject.FindGameObjectWithTag(targetTag);
            if (target == null)
            {
                Debug.LogWarning($"No GameObject tagged '{targetTag}' in the scene, so the camera has nothing to follow.", this);
                return;
            }

            camera.Follow = target.transform;
            if (bindLookAt)
                camera.LookAt = target.transform;
        }
    }
}

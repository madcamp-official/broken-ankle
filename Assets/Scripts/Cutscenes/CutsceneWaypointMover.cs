using System.Collections;
using Ashburn.Player;
using UnityEngine;

namespace Ashburn.Cutscenes
{
    /// <summary>
    /// Moves controlled players along simple waypoint paths for prototype escape beats.
    ///
    /// This is deliberately small: final authored scenes can replace it with Timeline tracks, but
    /// this lets the first company escape be blocked out with empty GameObjects today.
    /// </summary>
    public class CutsceneWaypointMover : MonoBehaviour
    {
        [SerializeField] Transform[] waypoints;
        [SerializeField] float speed = 4.6f;
        [SerializeField] float arriveDistance = 0.08f;
        [SerializeField] bool lockInput = true;

        public bool IsMoving { get; private set; }

        public void PlayForAllControlledPlayers()
        {
            StartCoroutine(PlayForAllControlledPlayersRoutine());
        }

        public IEnumerator PlayForAllControlledPlayersRoutine()
        {
            foreach (var rig in FindObjectsByType<PlayerRig>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (rig.IsControlled)
                    yield return Move(rig);
            }
        }

        IEnumerator Move(PlayerRig rig)
        {
            if (rig == null || waypoints == null || waypoints.Length == 0)
                yield break;

            IsMoving = true;

            if (lockInput)
                rig.SuspendInput(true);

            var body = rig.GetComponent<Rigidbody2D>();
            var controller = rig.GetComponent<PlayerController>();

            try
            {
                foreach (var waypoint in waypoints)
                {
                    if (waypoint == null)
                        continue;

                    while (rig != null &&
                           Vector2.Distance(rig.transform.position, waypoint.position) > arriveDistance)
                    {
                        var current = body != null ? body.position : (Vector2)rig.transform.position;
                        var target = (Vector2)waypoint.position;
                        var deltaTime = body != null ? Time.fixedDeltaTime : Time.deltaTime;
                        var next = Vector2.MoveTowards(current, target, speed * deltaTime);
                        var delta = next - current;

                        if (controller != null && delta.sqrMagnitude > 0.0001f)
                            controller.Drive(delta.normalized, MovementMode.Run);

                        if (body != null)
                        {
                            body.MovePosition(next);
                            yield return new WaitForFixedUpdate();
                        }
                        else
                        {
                            rig.transform.position = next;
                            yield return null;
                        }
                    }
                }
            }
            finally
            {
                if (controller != null)
                    controller.Drive(Vector2.zero, MovementMode.Walk);

                if (lockInput)
                    rig.SuspendInput(false);

                IsMoving = false;
            }
        }
    }
}

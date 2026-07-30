using System.Collections;
using System.Collections.Generic;
using Ashburn.Interaction;
using Ashburn.Player;
using Ashburn.World;
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
        readonly struct CollisionPair
        {
            public readonly Collider2D First;
            public readonly Collider2D Second;

            public CollisionPair(Collider2D first, Collider2D second)
            {
                First = first;
                Second = second;
            }
        }

        [SerializeField] Transform[] waypoints;
        [SerializeField] float speed = 4.6f;
        [SerializeField] float arriveDistance = 0.08f;
        [SerializeField] bool lockInput = true;
        [Tooltip("World-space path offset per player slot. Keeps two characters from overlapping.")]
        [SerializeField] Vector2[] slotOffsets =
        {
            new(-0.3f, 0f),
            new(0.3f, 0f),
        };

        public bool IsMoving { get; private set; }

        public void PlayForAllControlledPlayers()
        {
            StartCoroutine(PlayForAllControlledPlayersRoutine());
        }

        public IEnumerator PlayForAllControlledPlayersRoutine()
        {
            var moving = new List<Coroutine>();
            var zone = MapZone.Of(this);
            var ignoredCollisions = IgnoreParticipantCollisions();

            try
            {
                foreach (var rig in FindObjectsByType<PlayerRig>(
                             FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    var presence = rig.GetComponent<MapPresence>();
                    if (rig.IsControlled &&
                        (zone == null || presence != null && presence.Zone == zone))
                    {
                        moving.Add(StartCoroutine(Move(rig)));
                    }
                }

                IsMoving = moving.Count > 0;

                foreach (var routine in moving)
                    yield return routine;
            }
            finally
            {
                RestoreParticipantCollisions(ignoredCollisions);
                IsMoving = false;
            }
        }

        IEnumerator Move(PlayerRig rig)
        {
            if (rig == null || waypoints == null || waypoints.Length == 0)
                yield break;

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

                    var target = (Vector2)waypoint.position + OffsetFor(rig);

                    while (rig != null &&
                           Vector2.Distance(rig.transform.position, target) > arriveDistance)
                    {
                        var current = body != null ? body.position : (Vector2)rig.transform.position;
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

            }
        }

        Vector2 OffsetFor(PlayerRig rig)
        {
            var inventory = rig != null ? rig.GetComponent<Inventory>() : null;
            var slot = inventory != null ? inventory.Slot : 0;

            return slotOffsets != null && slot >= 0 && slot < slotOffsets.Length
                ? slotOffsets[slot]
                : Vector2.zero;
        }

        List<CollisionPair> IgnoreParticipantCollisions()
        {
            var zone = MapZone.Of(this);
            var colliders = new List<Collider2D[]>();

            foreach (var rig in FindObjectsByType<PlayerRig>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var presence = rig.GetComponent<MapPresence>();
                if (zone != null && (presence == null || presence.Zone != zone))
                    continue;

                colliders.Add(rig.GetComponentsInChildren<Collider2D>(false));
            }

            var ignored = new List<CollisionPair>();
            for (var firstRig = 0; firstRig < colliders.Count; firstRig++)
            {
                for (var secondRig = firstRig + 1; secondRig < colliders.Count; secondRig++)
                {
                    foreach (var first in colliders[firstRig])
                    foreach (var second in colliders[secondRig])
                    {
                        if (first == null || second == null || first.isTrigger || second.isTrigger ||
                            Physics2D.GetIgnoreCollision(first, second))
                        {
                            continue;
                        }

                        Physics2D.IgnoreCollision(first, second, true);
                        ignored.Add(new CollisionPair(first, second));
                    }
                }
            }

            return ignored;
        }

        static void RestoreParticipantCollisions(List<CollisionPair> ignored)
        {
            foreach (var pair in ignored)
                if (pair.First != null && pair.Second != null)
                    Physics2D.IgnoreCollision(pair.First, pair.Second, false);
        }
    }
}

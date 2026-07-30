using Ashburn.Noise;
using Ashburn.World;
using Photon.Pun;
using UnityEngine;

namespace Ashburn.Monster
{
    /// <summary>
    /// Hurts a player on contact and leaves them where they fell.
    ///
    /// This replaced hauling the catch back to a nest. Dragging took the monster out of the room,
    /// which handed the standing player a free rescue at the far end of a long walk; leaving the
    /// body where it dropped keeps the monster where it caught somebody, so the rescue has to be
    /// made past it. What a partner races is <see cref="Downed"/>'s hold, not a trip across the map.
    ///
    /// Host authority, per Multiplayer.MD §5. Every client still runs a monster of its own, so
    /// without that check two monsters would put the same player down twice for one contact.
    /// </summary>
    [RequireComponent(typeof(MonsterAI))]
    public class MonsterStrike : MonoBehaviour
    {
        [Tooltip("How close it has to get, in world units.")]
        [SerializeField] float reach = 0.55f;

        [Tooltip("Seconds before it can hurt anybody again. Long enough that a partner helping " +
                 "somebody up beside it is not knocked down on the same frame.")]
        [SerializeField] float cooldownSeconds = 3f;

        [Tooltip("Layers to look for players on.")]
        [SerializeField] LayerMask playerLayers = ~0;

        [Header("Noise")]
        [Tooltip("How far the sound of it carries, in world units. Going down is not quiet, and a " +
                 "partner has to be able to place where it happened.")]
        [SerializeField] float strikeNoiseRange = 16f;

        float _readyAt;
        readonly Collider2D[] _hits = new Collider2D[8];

        void Update()
        {
            // Only the host decides, and offline everybody is the host. Left to every client, both
            // monsters would strike and the second request would land on a player already down.
            if (PhotonNetwork.IsConnected && !PhotonNetwork.IsMasterClient)
                return;

            if (Time.time < _readyAt)
                return;

            var count = Physics2D.OverlapCircleNonAlloc(transform.position, reach, _hits, playerLayers);
            for (var i = 0; i < count; i++)
            {
                var player = _hits[i].GetComponentInParent<Downed>();
                if (player == null || player.IsDown)
                    continue;

                _readyAt = Time.time + cooldownSeconds;
                player.RequestDown();

                // Loud, and tagged with the monster's own map. A partner in the next room should
                // hear that it happened even if they cannot see where.
                NoiseBus.Emit(transform.position, strikeNoiseRange, NoiseKind.Monster,
                              MapZone.IdOf(this));
                return;
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, reach);
        }
    }
}

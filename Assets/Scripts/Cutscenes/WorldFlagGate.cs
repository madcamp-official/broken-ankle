using Ashburn.World;
using UnityEngine;

namespace Ashburn.Cutscenes
{
    /// <summary>Turns objects on or off when a world flag changes.</summary>
    public class WorldFlagGate : MonoBehaviour
    {
        [SerializeField] string flag;
        [SerializeField] bool activeWhenFlagSet = true;
        [SerializeField] GameObject[] targets;

        void OnEnable()
        {
            WorldState.Set += OnFlagSet;
            Apply();
        }

        void OnDisable() => WorldState.Set -= OnFlagSet;

        void OnFlagSet(string changed)
        {
            if (changed == flag)
                Apply();
        }

        void Apply()
        {
            var active = WorldState.Has(flag) == activeWhenFlagSet;
            foreach (var target in targets)
                if (target != null)
                    target.SetActive(active);
        }
    }
}

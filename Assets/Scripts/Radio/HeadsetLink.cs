using UnityEngine;

namespace Ashburn.Radio
{
    /// <summary>
    /// Turns the distance between the two players into how badly the radio breaks up.
    ///
    /// The briefing says the two headsets cancel each other's noise when they are near, which is
    /// why walking away costs the players the one thing keeping them honest with each other. It is
    /// not true — the equipment decides what to give them and distance is only the dial it uses —
    /// but it needs to behave exactly as if it were, or the lie is not worth telling.
    ///
    /// Lives on the partner's voice object next to the <see cref="RadioDsp"/> it drives, and finds
    /// the local player by the "Player" tag, which PlayerRig puts on whichever character this
    /// screen belongs to and takes off every other.
    /// </summary>
    public class HeadsetLink : MonoBehaviour
    {
        [Header("Ends of the link")]
        [Tooltip("The partner being listened to. Defaults to this object.")]
        [SerializeField] Transform partner;

        [Tooltip("This screen's own character. Found by the 'Player' tag when left empty.")]
        [SerializeField] Transform listener;

        [Header("Range")]
        [Tooltip("Closer than this the link is clean. World units.")]
        [SerializeField] float clearDistance = 9f;

        [Tooltip("Past this the voice is barely there. World units.")]
        [SerializeField] float lostDistance = 45f;

        [Tooltip("Shapes the fall-off. Above 1 keeps the link usable further out and then loses it " +
                 "quickly, which reads as equipment giving up rather than sound fading.")]
        [Range(0.25f, 4f)]
        [SerializeField] float falloff = 1.8f;

        [Header("Delay")]
        [Tooltip("Seconds of delay to add at full interference, on top of whatever the DSP is set " +
                 "to. Zero leaves the delay alone for scripted moments to use.")]
        [SerializeField] float delayWhenLost;

        [SerializeField] RadioDsp dsp;

        /// <summary>Current link quality, 1 clean and 0 gone. Useful to UI and to the mix.</summary>
        public float Quality { get; private set; } = 1f;

        void Reset()
        {
            dsp = GetComponent<RadioDsp>();
            partner = transform;
        }

        void Awake()
        {
            if (dsp == null)
                dsp = GetComponent<RadioDsp>();

            if (partner == null)
                partner = transform;

            if (dsp == null)
                Debug.LogError($"{nameof(HeadsetLink)} on '{name}' has no {nameof(RadioDsp)} to drive.", this);
        }

        void Update()
        {
            if (dsp == null)
                return;

            if (listener == null)
            {
                // Deferred rather than cached in Awake: the local character is spawned, and on a
                // client it may not exist yet when this object does.
                var tagged = GameObject.FindGameObjectWithTag("Player");
                if (tagged == null)
                    return;

                listener = tagged.transform;
            }

            var distance = Vector2.Distance(listener.position, partner.position);
            var raw = Mathf.InverseLerp(clearDistance, lostDistance, distance);
            var interference = Mathf.Pow(raw, falloff);

            Quality = 1f - interference;
            dsp.Interference = interference;

            if (delayWhenLost > 0f)
                dsp.DelaySeconds = delayWhenLost * interference;
        }
    }
}

using UnityEngine;

namespace Ashburn.Core
{
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(Collider2D))]
    public class BuildingRoofFade : MonoBehaviour
    {
        [SerializeField, Range(0.1f, 1f)] float fadedAlpha = 0.45f;
        [SerializeField] float fadeSpeed = 12f;
        SpriteRenderer _renderer;
        int _overlappingPlayers;
        float _normalAlpha = 1f;

        void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _normalAlpha = _renderer.color.a;

            var trigger = GetComponent<Collider2D>();
            trigger.isTrigger = true;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (IsPlayer(other))
                _overlappingPlayers++;
        }

        void OnTriggerExit2D(Collider2D other)
        {
            if (IsPlayer(other))
                _overlappingPlayers = Mathf.Max(0, _overlappingPlayers - 1);
        }

        void Update()
        {
            var color = _renderer.color;
            var targetAlpha = ShouldFade() ? fadedAlpha : _normalAlpha;
            color.a = Mathf.MoveTowards(color.a, targetAlpha, fadeSpeed * Time.deltaTime);
            _renderer.color = color;
        }

        bool ShouldFade()
        {
            return _overlappingPlayers > 0;
        }

        static bool IsPlayer(Collider2D other)
        {
            return other.CompareTag("Player") || other.GetComponentInParent<Ashburn.Player.PlayerController>() != null;
        }
    }
}

using Ashburn.Monster;
using Ashburn.Noise;
using Ashburn.Player;
using Ashburn.World;
using UnityEngine;

namespace Ashburn.Audio
{
    /// <summary>Owns lightweight world SFX and the local monster-proximity music layer.</summary>
    public sealed class GameAudio : MonoBehaviour
    {
        const int PoolSize = 8;
        const float DangerNear = 3f;
        const float DangerFar = 13f;

        static GameAudio _current;

        AudioClip[] _footsteps;
        AudioClip[] _paper;
        AudioSource[] _pool;
        AudioSource _danger;
        int _nextSource;
        int _nextFootstep;
        int _nextPaper;
        float _dangerVolume;
        float _dangerTarget;
        float _nextDangerScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (_current != null)
                return;

            var host = new GameObject("GameAudio");
            DontDestroyOnLoad(host);
            _current = host.AddComponent<GameAudio>();
        }

        void Awake()
        {
            if (_current != null && _current != this)
            {
                Destroy(gameObject);
                return;
            }

            _current = this;
            DontDestroyOnLoad(gameObject);

            _footsteps = new[]
            {
                Resources.Load<AudioClip>("Audio/footstep_soft_01"),
                Resources.Load<AudioClip>("Audio/footstep_soft_02"),
                Resources.Load<AudioClip>("Audio/footstep_soft_03"),
                Resources.Load<AudioClip>("Audio/footstep_soft_04"),
            };
            _paper = new[]
            {
                Resources.Load<AudioClip>("Audio/paper_rummage_01"),
                Resources.Load<AudioClip>("Audio/paper_rummage_02"),
            };

            _pool = new AudioSource[PoolSize];
            for (var i = 0; i < _pool.Length; i++)
            {
                _pool[i] = gameObject.AddComponent<AudioSource>();
                _pool[i].playOnAwake = false;
                _pool[i].spatialBlend = 0f;
            }

            _danger = gameObject.AddComponent<AudioSource>();
            _danger.playOnAwake = false;
            _danger.loop = true;
            _danger.spatialBlend = 0f;
            _danger.clip = Resources.Load<AudioClip>("Audio/monster_proximity_loop");
            if (_danger.clip != null)
                _danger.Play();
        }

        void Update()
        {
            if (Time.unscaledTime >= _nextDangerScan)
            {
                _nextDangerScan = Time.unscaledTime + 0.15f;
                _dangerTarget = DangerLevel(FindViewer());
            }

            _dangerVolume = Mathf.MoveTowards(
                _dangerVolume, _dangerTarget * 0.52f, Time.unscaledDeltaTime * 0.7f);

            if (_danger != null)
                _danger.volume = _dangerVolume;
        }

        public static void PlayFootstep(
            Vector3 position, MovementMode mode, NoiseKind kind, int map)
        {
            if (_current == null || _current._footsteps == null)
                return;

            var baseVolume = mode switch
            {
                MovementMode.Crouch => 0.16f,
                MovementMode.Run => 0.42f,
                _ => 0.28f,
            };

            _current.PlayWorld(
                _current._footsteps,
                ref _current._nextFootstep,
                position,
                map,
                kind == NoiseKind.Self ? baseVolume : baseVolume * 0.85f,
                mode == MovementMode.Run ? 0.94f : 1f);
        }

        public static void PlayPaper(Vector3 position, int map)
        {
            if (_current == null || _current._paper == null)
                return;

            _current.PlayWorld(
                _current._paper, ref _current._nextPaper, position, map, 0.5f, 1f);
        }

        void PlayWorld(
            AudioClip[] clips,
            ref int clipIndex,
            Vector3 position,
            int map,
            float volume,
            float pitch)
        {
            var viewer = FindViewer();
            if (viewer == null)
                return;

            var presence = viewer.GetComponent<MapPresence>();
            if (presence == null || presence.MapId != map)
                return;

            var delta = position - viewer.transform.position;
            var distance = delta.magnitude;
            if (distance > 18f)
                return;

            var clip = clips[clipIndex++ % clips.Length];
            if (clip == null)
                return;

            var source = _pool[_nextSource++ % _pool.Length];
            source.Stop();
            source.clip = clip;
            source.volume = volume * Mathf.Lerp(1f, 0.15f, distance / 18f);
            source.panStereo = Mathf.Clamp(delta.x / 8f, -0.75f, 0.75f);
            source.pitch = pitch * Random.Range(0.96f, 1.04f);
            source.Play();
        }

        static PlayerRig FindViewer()
        {
            foreach (var rig in PlayerRig.All)
                if (rig != null && rig.IsViewer)
                    return rig;

            return null;
        }

        static float DangerLevel(PlayerRig viewer)
        {
            if (viewer == null)
                return 0f;

            var presence = viewer.GetComponent<MapPresence>();
            if (presence == null || presence.Zone == null)
                return 0f;

            var nearest = float.PositiveInfinity;
            foreach (var monster in FindObjectsByType<MonsterAI>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (monster == null || monster.IsDormant || MapZone.IdOf(monster) != presence.MapId)
                    continue;

                nearest = Mathf.Min(
                    nearest, Vector2.Distance(viewer.transform.position, monster.transform.position));
            }

            return float.IsPositiveInfinity(nearest)
                ? 0f
                : Mathf.InverseLerp(DangerFar, DangerNear, nearest);
        }
    }
}

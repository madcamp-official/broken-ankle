using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Morrow.Player
{
    /// <summary>
    /// Cuts the flashlight's beam off wherever it meets a wall, so the light stops at the surface
    /// instead of washing through it.
    ///
    /// URP's own 2D shadows would do this, but only for Point lights, and a Point light cannot use
    /// a cookie — the two were measured to be mutually exclusive. Since the hand-drawn dithered
    /// cookie is what makes the beam look like it belongs in the art, the occlusion is done to the
    /// cookie instead: a fan of rays measures how far the beam reaches at each angle, and every
    /// texel past that distance is erased before the light is drawn.
    ///
    /// The authored sprite is never modified. A runtime copy is built once and rewritten in place.
    /// </summary>
    [RequireComponent(typeof(Light2D))]
    [DefaultExecutionOrder(950)]
    public class FlashlightOcclusion : MonoBehaviour
    {
        [Tooltip("Which layers stop the beam. Walls and props, not trigger zones or the player.")]
        [SerializeField] LayerMask blockers = ~0;

        [Tooltip("Rays cast across the cone. More is smoother along the wall edge but costs more.")]
        [SerializeField, Range(8, 256)] int rayCount = 96;

        [Tooltip("How far past a wall the light still bleeds, in world units. A little hides the hard cut.")]
        [SerializeField, Range(0f, 0.5f)] float surfaceBleed = 0.08f;

        Light2D _light;
        Sprite _sourceSprite;
        Texture2D _runtimeTexture;
        Color32[] _pixels;

        // Per-texel data that never changes: the beam-local distance and which ray covers it.
        float[] _texelDistance;
        int[] _texelRay;
        byte[] _sourceAlpha;

        float[] _rayReach;
        float _halfAngleRad;
        int _width, _height;

        readonly List<RaycastHit2D> _hits = new();
        ContactFilter2D _filter;
        Transform _self;
        Vector3 _lastPosition;
        Quaternion _lastRotation;
        bool _hasDrawn;

        void Awake()
        {
            _light = GetComponent<Light2D>();
            _self = transform;
            _sourceSprite = _light.lightCookieSprite;

            if (_sourceSprite == null)
            {
                Debug.LogError($"{nameof(FlashlightOcclusion)} on '{name}' needs the light to have a cookie sprite.", this);
                enabled = false;
                return;
            }

            _filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = blockers,
                // Interaction zones and other triggers are not walls, so they must not stop light.
                useTriggers = false,
            };

            BuildTables();
        }

        void BuildTables()
        {
            var rect = _sourceSprite.rect;
            _width = (int)rect.width;
            _height = (int)rect.height;
            var ppu = _sourceSprite.pixelsPerUnit;
            var pivot = _sourceSprite.pivot;

            var source = _sourceSprite.texture.GetPixels32();
            var texWidth = _sourceSprite.texture.width;
            var originX = (int)rect.x;
            var originY = (int)rect.y;

            var count = _width * _height;
            _pixels = new Color32[count];
            _sourceAlpha = new byte[count];
            _texelDistance = new float[count];
            _texelRay = new int[count];

            // The cone is as wide as the cookie: half its width at full height.
            _halfAngleRad = Mathf.Atan2(pivot.x, _height);
            _rayReach = new float[rayCount];

            for (var y = 0; y < _height; y++)
            {
                for (var x = 0; x < _width; x++)
                {
                    var i = y * _width + x;
                    _sourceAlpha[i] = source[(originY + y) * texWidth + originX + x].a;

                    // Local units, measured from the cookie's pivot, which sits at the cone's apex.
                    var lx = (x + 0.5f - pivot.x) / ppu;
                    var ly = (y + 0.5f - pivot.y) / ppu;
                    _texelDistance[i] = Mathf.Sqrt(lx * lx + ly * ly);

                    // Angle away from the beam's centre line, which runs along local +Y.
                    var angle = Mathf.Atan2(lx, Mathf.Max(ly, 0.0001f));
                    var t = Mathf.InverseLerp(-_halfAngleRad, _halfAngleRad, angle);
                    _texelRay[i] = Mathf.Clamp(Mathf.RoundToInt(t * (rayCount - 1)), 0, rayCount - 1);
                }
            }

            _runtimeTexture = new Texture2D(_width, _height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = _sourceSprite.name + " (occluded)",
            };

            var pivotNormalised = new Vector2(pivot.x / _width, pivot.y / _height);
            var runtimeSprite = Sprite.Create(
                _runtimeTexture, new Rect(0f, 0f, _width, _height), pivotNormalised, ppu,
                0, SpriteMeshType.FullRect);
            runtimeSprite.name = _runtimeTexture.name;

            _light.lightCookieSprite = runtimeSprite;
        }

        void OnDestroy()
        {
            if (_runtimeTexture != null)
                Destroy(_runtimeTexture);
        }

        void LateUpdate()
        {
            // Aiming runs at 900 and this at 950, so the rotation read here is already final.
            // Nothing moved means the previous cut is still correct, and the upload can be skipped.
            if (_hasDrawn && _self.position == _lastPosition && _self.rotation == _lastRotation)
                return;

            _lastPosition = _self.position;
            _lastRotation = _self.rotation;
            _hasDrawn = true;

            CastFan();
            Redraw();
        }

        void CastFan()
        {
            var origin = (Vector2)_self.position;
            var reach = _height / _sourceSprite.pixelsPerUnit;

            for (var r = 0; r < rayCount; r++)
            {
                var t = rayCount == 1 ? 0.5f : r / (float)(rayCount - 1);
                var angle = Mathf.Lerp(-_halfAngleRad, _halfAngleRad, t);

                // Local +Y rotated into the world, then swung out to this ray's angle.
                var direction = (Vector2)(_self.rotation * new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f));

                var nearest = reach;
                var hitCount = Physics2D.Raycast(origin, direction, _filter, _hits, reach);
                for (var h = 0; h < hitCount; h++)
                {
                    // Our own colliders are not walls; the beam starts inside the player.
                    if (_hits[h].collider.transform.IsChildOf(_self.root))
                        continue;

                    // Hits are not documented as distance-sorted, so take the closest rather
                    // than the first, or a far wall could cut the beam before a near one.
                    if (_hits[h].distance < nearest)
                        nearest = _hits[h].distance;
                }

                _rayReach[r] = nearest >= reach ? reach : nearest + surfaceBleed;
            }
        }

        void Redraw()
        {
            for (var i = 0; i < _pixels.Length; i++)
            {
                var alpha = _texelDistance[i] <= _rayReach[_texelRay[i]] ? _sourceAlpha[i] : (byte)0;
                _pixels[i] = new Color32(255, 255, 255, alpha);
            }

            _runtimeTexture.SetPixels32(_pixels);
            _runtimeTexture.Apply(false);
        }
    }
}

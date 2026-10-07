// EnemyHPBar - 空洞骑士怪物血条模组
// v1.1.0: 白色血条 + 头顶显示具体数字(hp/maxHp)，Boss 也显示
// 游戏内按 F8 开关血条显示
//
// 本模组自带轻量加载方式：由补丁版 Assembly-CSharp.dll 在 GameManager.Awake
// 开头调用 EnemyHPBar.Bootstrap.Start()，不依赖官方 Modding API。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace EnemyHPBar
{
    /// <summary>入口：由游戏程序集在启动时调用，只执行一次。</summary>
    public static class Bootstrap
    {
        private static bool _started;

        public static void Start()
        {
            if (_started) return;
            _started = true;
            try
            {
                GameObject go = new GameObject("EnemyHPBar_Manager");
                Object.DontDestroyOnLoad(go);
                go.AddComponent<HPBarManager>();
                Debug.Log("[EnemyHPBar] v1.1.0 已加载（白色血条+数字，Boss 也显示），游戏内按 F8 切换。");
            }
            catch (Exception e)
            {
                Debug.LogError("[EnemyHPBar] 启动失败: " + e);
            }
        }
    }

    /// <summary>常驻管理器：周期性扫描场景中的怪物，每帧刷新血条。</summary>
    public class HPBarManager : MonoBehaviour
    {
        private const float ScanInterval = 0.4f;

        private readonly Dictionary<HealthManager, HPBar> _bars =
            new Dictionary<HealthManager, HPBar>();

        private readonly List<HealthManager> _pendingRemove = new List<HealthManager>();

        private float _nextScan;
        private bool _legacyInputBroken;
        private bool _newInputBroken;

        private void Update()
        {
            CheckToggle();

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + ScanInterval;
                try
                {
                    Scan();
                    Cleanup();
                }
                catch (Exception e)
                {
                    Debug.LogError("[EnemyHPBar] 扫描出错: " + e);
                }
            }

            foreach (KeyValuePair<HealthManager, HPBar> kvp in _bars)
            {
                kvp.Value.Refresh();
            }
        }

        // 新版游戏使用新输入系统，旧版 Input 在部分配置下会抛异常，两条通道都做检测
        private void CheckToggle()
        {
            bool pressed = false;

            if (!_legacyInputBroken)
            {
                try
                {
                    pressed = Input.GetKeyDown(KeyCode.F8);
                }
                catch (Exception)
                {
                    _legacyInputBroken = true;
                }
            }

            if (!pressed && !_newInputBroken)
            {
                try
                {
                    Keyboard kb = Keyboard.current;
                    if (kb != null && kb.f8Key.wasPressedThisFrame)
                    {
                        pressed = true;
                    }
                }
                catch (Exception)
                {
                    _newInputBroken = true;
                }
            }

            if (pressed)
            {
                HPBar.BarsVisible = !HPBar.BarsVisible;
            }
        }

        private void Scan()
        {
            // FindObjectsOfTypeAll 能同时找到未激活的怪物与对象池里的怪物，
            // 再用 scene.IsValid 过滤掉资源包里的预制体。
            HealthManager[] found = Resources.FindObjectsOfTypeAll<HealthManager>();
            for (int i = 0; i < found.Length; i++)
            {
                HealthManager hm = found[i];
                if ((Object)hm == null) continue;
                if (_bars.ContainsKey(hm)) continue;

                GameObject go = hm.gameObject;
                if (go == null || !go.scene.IsValid()) continue;

                _bars.Add(hm, new HPBar(hm));
            }
        }

        private void Cleanup()
        {
            _pendingRemove.Clear();
            foreach (KeyValuePair<HealthManager, HPBar> kvp in _bars)
            {
                if ((Object)kvp.Key == null)
                {
                    _pendingRemove.Add(kvp.Key);
                }
            }
            for (int i = 0; i < _pendingRemove.Count; i++)
            {
                HealthManager key = _pendingRemove[i];
                HPBar bar;
                if (_bars.TryGetValue(key, out bar))
                {
                    bar.DestroyBar();
                }
                _bars.Remove(key);
            }
        }
    }

    /// <summary>内置 3x5 像素点阵数字字模（0-9 和 /），运行时生成贴图，不依赖游戏字体资源。</summary>
    internal static class PixelDigits
    {
        private const string Chars = "0123456789/";

        private static readonly string[] Patterns =
        {
            "111101101101111", // 0
            "010110010010111", // 1
            "111001111100111", // 2
            "111001111001111", // 3
            "101101111001001", // 4
            "111100111001111", // 5
            "111100111101111", // 6
            "111001010010010", // 7
            "111101111101111", // 8
            "111101111001111", // 9
            "001001010100100"  // /
        };

        private static Sprite[] _sprites;

        /// <summary>每个字符在世界上占 0.375 x 0.625 单位（pixelsPerUnit = 8）。</summary>
        public const float GlyphW = 3f / 8f;
        public const float GlyphH = 5f / 8f;

        public static Sprite GetSprite(char c)
        {
            if (_sprites == null) Build();
            int i = Chars.IndexOf(c);
            return i < 0 ? null : _sprites[i];
        }

        private static void Build()
        {
            const int stride = 4; // 3 像素字宽 + 1 像素间隔
            Texture2D tex = new Texture2D(Chars.Length * stride, 5, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Point;

            var pixels = new Color32[Chars.Length * stride * 5];
            var on = new Color32(255, 255, 255, 255);
            var off = new Color32(0, 0, 0, 0);

            for (int g = 0; g < Chars.Length; g++)
            {
                for (int r = 0; r < 5; r++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        bool set = Patterns[g][r * 3 + c] == '1';
                        int x = g * stride + c;
                        int y = 4 - r; // 贴图 y=0 是底行，字模从顶行写起
                        pixels[y * Chars.Length * stride + x] = set ? on : off;
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);

            _sprites = new Sprite[Chars.Length];
            for (int g = 0; g < Chars.Length; g++)
            {
                _sprites[g] = Sprite.Create(tex, new Rect(g * stride, 0f, 3f, 5f),
                    new Vector2(0.5f, 0.5f), 8f);
            }
        }
    }

    /// <summary>单个怪物的血条：白色填充条 + 黑底白边 + 头顶像素数字。</summary>
    public class HPBar
    {
        public static bool BarsVisible = true;

        private static Sprite _centerSprite;
        private static Sprite _leftSprite;

        private static readonly Color ColFill = new Color(1f, 1f, 1f, 1f);
        private static readonly Color ColFillFlash = new Color(1f, 0.45f, 0.40f, 1f);
        private static readonly Color ColBg = new Color(0f, 0f, 0f, 0.78f);
        private static readonly Color ColFrame = new Color(1f, 1f, 1f, 0.9f);
        private static readonly Color ColText = new Color(1f, 1f, 1f, 1f);
        private static readonly Color ColTextShadow = new Color(0f, 0f, 0f, 0.85f);

        private const float Border = 0.05f;
        private const int BaseOrder = 32000;

        private readonly HealthManager _hm;
        private readonly GameObject _root;
        private readonly SpriteRenderer _frame;
        private readonly SpriteRenderer _bg;
        private readonly SpriteRenderer _fill;
        private readonly List<SpriteRenderer> _labelParts = new List<SpriteRenderer>();

        private int _maxHp;
        private int _lastHp;
        private float _flashUntil;
        private float _width;
        private float _height;
        private bool _sized;
        private string _lastLabel = "<init>";
        private string _layerName = "Default";

        public HPBar(HealthManager hm)
        {
            _hm = hm;
            InitSprites();

            _maxHp = Mathf.Max(1, hm.hp);
            _lastHp = hm.hp;

            _root = new GameObject("EnemyHPBar_Bar");
            _root.transform.position = hm.transform.position;
            _root.SetActive(false);

            _frame = MakePart(_centerSprite, ColFrame, BaseOrder);
            _bg = MakePart(_centerSprite, ColBg, BaseOrder + 1);
            _fill = MakePart(_leftSprite, ColFill, BaseOrder + 2);

            _frame.transform.SetParent(_root.transform, false);
            _bg.transform.SetParent(_root.transform, false);
            _fill.transform.SetParent(_root.transform, false);
        }

        private static SpriteRenderer MakePart(Sprite sprite, Color color, int order)
        {
            GameObject go = new GameObject("EnemyHPBar_Part");
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        private static void InitSprites()
        {
            if (_centerSprite != null) return;

            // 8x8 白色贴图，pixelsPerUnit = 8，使精灵默认占 1 个世界单位，方便用 localScale 控制长宽。
            Texture2D tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            Color32[] pixels = new Color32[64];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);

            Rect rect = new Rect(0f, 0f, 8f, 8f);
            _centerSprite = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 8f);
            _leftSprite = Sprite.Create(tex, rect, new Vector2(0f, 0.5f), 8f);
        }

        public void Refresh()
        {
            if ((Object)_hm == null || _root == null)
            {
                Hide();
                return;
            }

            GameObject go = _hm.gameObject;
            if (go == null || !go.activeInHierarchy || _hm.isDead || !BarsVisible)
            {
                Hide();
                return;
            }

            if (!_sized && !ResolveSize())
            {
                Hide();
                return;
            }

            int hp = _hm.hp;
            if (hp > _maxHp) _maxHp = hp;
            if (hp < _lastHp) _flashUntil = Time.unscaledTime + 0.12f;
            _lastHp = hp;

            float frac = _maxHp <= 0 ? 0f : Mathf.Clamp01((float)hp / _maxHp);

            Bounds b = GetBounds(go);
            Vector3 pos = _root.transform.position;
            pos.x = b.center.x;
            pos.y = b.max.y + 0.35f + _height;
            pos.z = _hm.transform.position.z;
            _root.transform.position = pos;

            if (!_root.activeSelf) _root.SetActive(true);

            _frame.transform.localPosition = Vector3.zero;
            _frame.transform.localScale = new Vector3(_width + Border * 2.6f, _height + Border * 2.6f, 1f);

            _bg.transform.localPosition = Vector3.zero;
            _bg.transform.localScale = new Vector3(_width + Border, _height + Border, 1f);

            // 填充条用左端锚点，从左向右按比例缩短；受击时短暂变红提供反馈
            _fill.transform.localPosition = new Vector3(-_width * 0.5f, 0f, 0f);
            _fill.transform.localScale = new Vector3(_width * frac, _height, 1f);
            _fill.color = Time.unscaledTime < _flashUntil ? ColFillFlash : ColFill;

            UpdateLabel(hp + "/" + _maxHp);
        }

        private void Hide()
        {
            if (_root != null && _root.activeSelf)
            {
                _root.SetActive(false);
            }
        }

        // 数字标签：每个字符两份精灵（黑色阴影 + 白色主体），血量变化时才重建布局
        private void UpdateLabel(string text)
        {
            if (text == _lastLabel) return;
            _lastLabel = text;

            float scale = Mathf.Clamp(_width * 0.14f, 0.20f, 0.32f) / PixelDigits.GlyphH;
            float advance = PixelDigits.GlyphW * scale + 0.025f;
            float y = _height * 0.5f + PixelDigits.GlyphH * scale * 0.5f + 0.06f;

            int need = text.Length * 2;
            while (_labelParts.Count < need)
            {
                // 偶数位是阴影、奇数位是主体，顺序错开保证阴影先画
                bool shadow = _labelParts.Count % 2 == 0;
                SpriteRenderer part = MakePart(null, ColText, BaseOrder + (shadow ? 3 : 4));
                part.sortingLayerName = _layerName;
                part.transform.SetParent(_root.transform, false);
                _labelParts.Add(part);
            }

            for (int i = 0; i < _labelParts.Count; i++)
            {
                _labelParts[i].gameObject.SetActive(i < need);
            }

            float x = -(text.Length - 1) * advance * 0.5f;
            for (int c = 0; c < text.Length; c++)
            {
                Sprite g = PixelDigits.GetSprite(text[c]);

                SpriteRenderer shadow = _labelParts[c * 2];
                SpriteRenderer main = _labelParts[c * 2 + 1];

                shadow.sprite = g;
                shadow.color = ColTextShadow;
                shadow.transform.localPosition = new Vector3(x + 0.02f, y - 0.02f, 0f);
                shadow.transform.localScale = new Vector3(scale, scale, 1f);

                main.sprite = g;
                main.color = ColText;
                main.transform.localPosition = new Vector3(x, y, 0f);
                main.transform.localScale = new Vector3(scale, scale, 1f);

                x += advance;
            }
        }

        private bool ResolveSize()
        {
            Bounds b = GetBounds(_hm.gameObject);
            if (b.size.x < 0.01f && b.size.y < 0.01f)
            {
                return false; // 碰撞体还没就绪，下一帧再试
            }

            _width = Mathf.Clamp(b.size.x * 1.15f, 0.9f, 10f);
            _height = Mathf.Clamp(_width * 0.16f, 0.16f, 0.42f);
            _sized = true;

            ApplySortingLayer();
            return true;
        }

        // 跟随怪物本身的渲染层，保证血条画在怪物贴图之上
        private void ApplySortingLayer()
        {
            Renderer r = _hm.gameObject.GetComponentInChildren<Renderer>();
            if (r == null) return;

            _layerName = r.sortingLayerName;
            _frame.sortingLayerName = _layerName;
            _bg.sortingLayerName = _layerName;
            _fill.sortingLayerName = _layerName;
            foreach (SpriteRenderer part in _labelParts)
            {
                part.sortingLayerName = _layerName;
            }
        }

        private static Bounds GetBounds(GameObject go)
        {
            // 优先取非触发器碰撞体（怪物本体），攻击判定框一般是 trigger
            Collider2D[] cols = go.GetComponentsInChildren<Collider2D>();
            Collider2D fallback = null;
            for (int i = 0; i < cols.Length; i++)
            {
                Collider2D c = cols[i];
                if (c == null || !c.enabled) continue;
                if (fallback == null) fallback = c;
                if (!c.isTrigger) return c.bounds;
            }
            if (fallback != null) return fallback.bounds;

            Renderer r = go.GetComponentInChildren<Renderer>();
            if (r != null) return r.bounds;

            return new Bounds(go.transform.position, Vector3.one);
        }

        public void DestroyBar()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
            }
        }
    }
}

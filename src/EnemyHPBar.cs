// EnemyHPBar - 空洞骑士怪物血条模组
// v1.2.0: Boss/精英改用屏幕顶部 HUD 血条并显示名称，普通怪物保持头顶血条+数字
// 游戏内按 F8 开关血条显示
//
// 本模组自带轻量加载方式：由补丁版 Assembly-CSharp.dll 在 GameManager.Awake
// 开头调用 EnemyHPBar.Bootstrap.Start()，不依赖官方 Modding API。
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Gdi = System.Drawing;
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
                Debug.Log("[EnemyHPBar] v1.2.0 已加载（小怪头顶血条，Boss/精英顶部HUD血条+名称），游戏内按 F8 切换。");
            }
            catch (Exception e)
            {
                Debug.LogError("[EnemyHPBar] 启动失败: " + e);
            }
        }
    }

    /// <summary>Boss 名称映射：游戏内部对象名 -> 中文显示名。未命中的显示清洗后的原名。</summary>
    internal static class BossNames
    {
        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            { "False Knight", "假骑士" },
            { "Gruz Mother", "格鲁兹之母" },
            { "Hornet Boss 1", "守卫大黄蜂" },
            { "Hornet Boss 2", "哨兵大黄蜂" },
            { "Hornet", "大黄蜂" },
            { "Mantis Lord", "螳螂领主" },
            { "Mage Lord", "灵魂大师" },
            { "Mage Knight", "灵魂战士" },
            { "Infected Knight", "残破容器" },
            { "Black Knight", "守望者骑士" },
            { "Mimic Spider", "诺斯克" },
            { "Traitor Lord", "叛徒领主" },
            { "Dung Defender", "粪便守卫" },
            { "White Defender", "白色守卫" },
            { "Hive Knight", "蜂巢骑士" },
            { "Final Boss", "辐光" },
            { "Hollow Knight Boss", "空洞骑士" },
            { "HK Prime", "空洞骑士" },
            { "Grimm Nightmare", "梦魇王·格里姆" },
            { "Grimm", "格里姆" },
            { "Grey Prince", "灰王子·佐特" },
            { "Pure Vessel", "纯净容器" },
            { "Vengefly King", "复仇蝇王" },
            { "Big Fly", "复仇蝇王" },
            { "Crystal Guardian", "水晶守卫" },
            { "Oblobble", "奥布洛波" },
            { "Uumuu", "乌穆" },
            { "Mato", "马托" },
            { "Oro", "奥罗" },
            { "Tiso", "蒂索" },
            { "Sheo", "希欧" },
            { "Sly", "斯莱" },
            { "No Eyes", "无眼" },
            { "Elder Hu", "胡长老" },
            { "Galien", "加连" },
            { "Marmu", "马穆" },
            { "Gorb", "戈布" },
            { "Xero", "泽罗" },
            { "Markoth", "马科斯" },
        };

        private static string[] _sortedKeys;

        public static string Resolve(GameObject go)
        {
            if (go == null) return "";

            string raw = go.name;
            // 去掉克隆与括号后缀，再查表；未命中则用最长前缀匹配（覆盖 "Hornet Boss 1 (Clone)" 之类变体）
            string cleaned = raw.Replace("(Clone)", "").Trim();
            while (cleaned.Length > 0 && cleaned[cleaned.Length - 1] == ')')
            {
                int open = cleaned.LastIndexOf('(');
                if (open < 0) break;
                cleaned = cleaned.Substring(0, open).Trim();
            }

            string v;
            if (Map.TryGetValue(cleaned, out v)) return v;

            if (_sortedKeys == null)
            {
                var keys = new string[Map.Count];
                Map.Keys.CopyTo(keys, 0);
                Array.Sort(keys, (a, b) => -a.Length.CompareTo(b.Length));
                _sortedKeys = keys;
            }
            foreach (string k in _sortedKeys)
            {
                if (cleaned.StartsWith(k, StringComparison.OrdinalIgnoreCase)) return Map[k];
            }

            // 兜底：去掉结尾的编号/Boss 字样后展示
            string fallback = cleaned;
            int cut = fallback.LastIndexOf(" Boss", StringComparison.OrdinalIgnoreCase);
            if (cut > 0) fallback = fallback.Substring(0, cut);
            fallback = fallback.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', ' ', '_');
            return fallback.Length > 0 ? fallback : raw;
        }
    }

    /// <summary>常驻管理器：周期性扫描场景中的怪物，每帧刷新血条。</summary>
    public class HPBarManager : MonoBehaviour
    {
        private const float ScanInterval = 0.4f;

        private readonly Dictionary<HealthManager, HPBar> _bars =
            new Dictionary<HealthManager, HPBar>();

        private readonly List<HealthManager> _pendingRemove = new List<HealthManager>();
        private readonly List<HPBar> _hudBars = new List<HPBar>();

        private BossHud _hud;
        private float _nextScan;
        private bool _legacyInputBroken;
        private bool _newInputBroken;
        private FieldInfo _godFinderField;

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

            _hudBars.Clear();
            foreach (KeyValuePair<HealthManager, HPBar> kvp in _bars)
            {
                HPBar bar = kvp.Value.Refresh();
                if (bar != null) _hudBars.Add(bar);
            }

            if (_hudBars.Count > 0)
            {
                if (_hud == null) _hud = BossHud.Create();
                if (_hud != null) _hud.Refresh(_hudBars);
            }
            else if (_hud != null)
            {
                _hud.Hide();
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

                _bars.Add(hm, new HPBar(hm, IsHudBoss(hm, go)));
            }
        }

        // Boss/精英判定：Boss战列表、名字带 Boss、或图鉴(Godfinder)标记，任一命中
        private bool IsHudBoss(HealthManager hm, GameObject go)
        {
            BossSceneController bsc = BossSceneController.Instance;
            if (bsc != null && bsc.bosses != null)
            {
                foreach (HealthManager b in bsc.bosses)
                {
                    if (b == hm) return true;
                }
            }

            if (go.name.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            try
            {
                if (_godFinderField == null)
                {
                    _godFinderField = typeof(HealthManager).GetField("showGodfinderIcon",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                }
                if (_godFinderField != null)
                {
                    object v = _godFinderField.GetValue(hm);
                    if (v != null && (bool)v) return true;
                }
            }
            catch (Exception)
            {
                // 反射失败就当没有这个标记
            }
            return false;
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

    /// <summary>Boss HUD：屏幕顶部居中的名称+血条+数字，多个 Boss 合并为一根条。</summary>
    public class BossHud
    {
        private const float BarWidthFactor = 0.44f;
        private const float BarWidthMax = 860f;
        private const float BarHeight = 14f;

        private static readonly Color ColFrame = new Color(1f, 1f, 1f, 0.9f);
        private static readonly Color ColBg = new Color(0f, 0f, 0f, 0.72f);
        private static readonly Color ColFill = new Color(1f, 1f, 1f, 1f);
        private static readonly Color ColFillFlash = new Color(1f, 0.45f, 0.40f, 1f);
        private static readonly Color ColText = new Color(1f, 1f, 1f, 1f);
        private static readonly Color ColTextShadow = new Color(0f, 0f, 0f, 0.9f);

        private readonly GameObject _root;
        private readonly RectTransform _container;
        private readonly Image _frame;
        private readonly Image _bg;
        private readonly Image _fill;
        private readonly Image _nameMain;
        private readonly Image _nameShadow;
        private readonly Image _numMain;
        private readonly Image _numShadow;

        private string _nameKey = "<init>";
        private string _numKey = "<init>";
        private int _lastHp;
        private float _flashUntil;
        private int _lastScreenWidth;

        public static BossHud Create()
        {
            try
            {
                return new BossHud();
            }
            catch (Exception e)
            {
                Debug.LogError("[EnemyHPBar] HUD 创建失败: " + e);
                return null;
            }
        }

        private BossHud()
        {
            _root = new GameObject("EnemyHPBar_Hud");
            Object.DontDestroyOnLoad(_root);

            Canvas canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;

            _container = MakeRect("Container", _root.transform);
            _container.anchorMin = new Vector2(0.5f, 1f);
            _container.anchorMax = new Vector2(0.5f, 1f);
            _container.pivot = new Vector2(0.5f, 1f);
            _container.anchoredPosition = new Vector2(0f, -30f);

            Sprite white = WhiteSprite.Get();

            _frame = MakeImage("Frame", _container, white, ColFrame);
            _bg = MakeImage("Bg", _container, white, ColBg);
            // 三层条都锚定在容器底部中央：白描边 -> 黑底 -> 白填充
            foreach (Image img in new[] { _frame, _bg })
            {
                img.rectTransform.anchorMin = new Vector2(0.5f, 0f);
                img.rectTransform.anchorMax = new Vector2(0.5f, 0f);
                img.rectTransform.pivot = new Vector2(0.5f, 0f);
            }
            _frame.rectTransform.anchoredPosition = new Vector2(0f, 0f);
            _bg.rectTransform.anchoredPosition = new Vector2(0f, 5f);

            _fill = MakeImage("Fill", _bg.transform, white, ColFill);
            _fill.rectTransform.anchorMin = new Vector2(0f, 0f);
            _fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            _fill.rectTransform.pivot = new Vector2(0f, 0.5f);

            _nameShadow = MakeImage("NameShadow", _container, null, ColTextShadow);
            _nameMain = MakeImage("Name", _container, null, ColText);
            _numShadow = MakeImage("NumShadow", _container, null, ColTextShadow);
            _numMain = MakeImage("Num", _container, null, ColText);

            _root.SetActive(false);
        }

        public void Refresh(List<HPBar> bosses)
        {
            if (!HPBar.BarsVisible)
            {
                Hide();
                return;
            }

            int hpSum = 0;
            int maxSum = 0;
            HPBar head = null;
            for (int i = 0; i < bosses.Count; i++)
            {
                HPBar b = bosses[i];
                if (!b.IsAlive) continue;
                if (head == null) head = b;
                hpSum += b.CurrentHp;
                maxSum += b.MaxHp;
            }

            if (head == null || maxSum <= 0)
            {
                Hide();
                return;
            }

            if (!_root.activeSelf) _root.SetActive(true);

            float barW = Mathf.Min(Screen.width * BarWidthFactor, BarWidthMax);
            if (Screen.width != _lastScreenWidth)
            {
                _lastScreenWidth = Screen.width;
                _nameKey = "<resize>";
                _numKey = "<resize>";
            }

            _container.sizeDelta = new Vector2(barW, 50f);
            _frame.rectTransform.sizeDelta = new Vector2(barW + 10f, BarHeight + 10f);
            _bg.rectTransform.sizeDelta = new Vector2(barW, BarHeight);

            int hp = Mathf.Max(0, hpSum);
            if (hp < _lastHp) _flashUntil = Time.unscaledTime + 0.12f;
            _lastHp = hp;
            float frac = Mathf.Clamp01((float)hp / maxSum);
            _fill.color = Time.unscaledTime < _flashUntil ? ColFillFlash : ColFill;
            _fill.rectTransform.offsetMin = new Vector2(3f, 3f);
            _fill.rectTransform.offsetMax = new Vector2(3f + (barW - 6f) * frac, -3f);

            SetText(_nameMain, _nameShadow, head.DisplayName, 19, true, ref _nameKey,
                delegate(RectTransform t, Vector2 size)
                {
                    t.anchorMin = new Vector2(0.5f, 1f);
                    t.anchorMax = new Vector2(0.5f, 1f);
                    t.pivot = new Vector2(0.5f, 1f);
                    t.anchoredPosition = new Vector2(0f, -1f);
                    t.sizeDelta = size;
                });

            SetText(_numMain, _numShadow, hp + "/" + maxSum, 17, false, ref _numKey,
                delegate(RectTransform t, Vector2 size)
                {
                    t.anchorMin = new Vector2(1f, 1f);
                    t.anchorMax = new Vector2(1f, 1f);
                    t.pivot = new Vector2(0f, 1f);
                    t.anchoredPosition = new Vector2(12f, -1f);
                    t.sizeDelta = size;
                });
        }

        private delegate void PlaceRect(RectTransform t, Vector2 size);

        private static void SetText(Image main, Image shadow, string text, int px, bool bold,
            ref string cacheKey, PlaceRect place)
        {
            string key = px + (bold ? "|B|" : "|N|") + text;
            if (key != cacheKey)
            {
                cacheKey = key;
                Sprite s = GdiText.Get(text, px, bold);
                main.sprite = s;
                shadow.sprite = s;
                if (s == null)
                {
                    main.gameObject.SetActive(false);
                    shadow.gameObject.SetActive(false);
                    return;
                }
                main.gameObject.SetActive(true);
                shadow.gameObject.SetActive(true);
            }

            if (main.sprite == null) return;

            Vector2 size = new Vector2(main.sprite.rect.width, main.sprite.rect.height);
            place(main.rectTransform, size);
            place(shadow.rectTransform, size);
            shadow.rectTransform.anchoredPosition += new Vector2(1.5f, -1.5f);
        }

        public void Hide()
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
        }

        private static RectTransform MakeRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        private static Image MakeImage(string name, Transform parent, Sprite sprite, Color color)
        {
            RectTransform rt = MakeRect(name, parent);
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }
    }

    /// <summary>共享的纯白方块精灵（UI Image 拉伸用）。</summary>
    internal static class WhiteSprite
    {
        private static Sprite _sprite;

        public static Sprite Get()
        {
            if (_sprite != null) return _sprite;

            Texture2D tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            Color32[] px = new Color32[64];
            for (int i = 0; i < 64; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply(false, false);
            _sprite = Sprite.Create(tex, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0.5f), 100f);
            return _sprite;
        }
    }

    /// <summary>用系统字体把文字渲染成精灵（支持中文），带缓存。</summary>
    internal static class GdiText
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        private static readonly string[] FontNames = { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei" };
        private const int MaxCache = 600;

        public static Sprite Get(string text, int px, bool bold)
        {
            if (string.IsNullOrEmpty(text)) return null;

            string key = px + (bold ? "|B|" : "|N|") + text;
            Sprite cached;
            if (Cache.TryGetValue(key, out cached)) return cached;

            Sprite result = null;
            try
            {
                result = Render(text, px, bold);
            }
            catch (Exception e)
            {
                Debug.LogError("[EnemyHPBar] 文字渲染失败: " + e.Message);
            }

            if (Cache.Count > MaxCache) Cache.Clear();
            Cache[key] = result;
            return result;
        }

        private static Sprite Render(string text, int px, bool bold)
        {
            Gdi.Font font = null;
            for (int i = 0; i < FontNames.Length; i++)
            {
                try
                {
                    font = new Gdi.Font(FontNames[i], px,
                        bold ? Gdi.FontStyle.Bold : Gdi.FontStyle.Regular, Gdi.GraphicsUnit.Pixel);
                    break;
                }
                catch (Exception)
                {
                    // 换下一个字体
                }
            }
            if (font == null)
            {
                font = new Gdi.Font(Gdi.FontFamily.GenericSansSerif, px,
                    bold ? Gdi.FontStyle.Bold : Gdi.FontStyle.Regular, Gdi.GraphicsUnit.Pixel);
            }

            using (Gdi.Bitmap probe = new Gdi.Bitmap(1, 1))
            using (Gdi.Graphics mg = Gdi.Graphics.FromImage(probe))
            {
                Gdi.SizeF size = mg.MeasureString(text, font, int.MaxValue,
                    Gdi.StringFormat.GenericTypographic);
                int w = (int)Math.Ceiling(size.Width) + 4;
                int h = (int)Math.Ceiling(size.Height) + 4;
                if (w <= 4 || h <= 4) return null;

                using (Gdi.Bitmap bmp = new Gdi.Bitmap(w, h, Gdi.Imaging.PixelFormat.Format32bppArgb))
                using (Gdi.Graphics g = Gdi.Graphics.FromImage(bmp))
                {
                    g.TextRenderingHint = Gdi.Text.TextRenderingHint.AntiAlias;
                    g.DrawString(text, font, Gdi.Brushes.White, 2f, 2f, Gdi.StringFormat.GenericTypographic);

                    Gdi.Imaging.BitmapData data = bmp.LockBits(
                        new Gdi.Rectangle(0, 0, w, h),
                        Gdi.Imaging.ImageLockMode.ReadOnly,
                        Gdi.Imaging.PixelFormat.Format32bppArgb);

                    byte[] raw = new byte[w * h * 4];
                    byte[] row = new byte[w * 4];
                    for (int y = 0; y < h; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(
                            data.Scan0 + y * data.Stride, row, 0, w * 4);
                        Buffer.BlockCopy(row, 0, raw, (h - 1 - y) * w * 4, w * 4); // 垂直翻转
                    }
                    bmp.UnlockBits(data);

                    for (int i = 0; i < raw.Length; i += 4)
                    {
                        byte b = raw[i];        // GDI 是 BGRA
                        raw[i] = raw[i + 2];
                        raw[i + 2] = b;
                    }

                    Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.filterMode = FilterMode.Bilinear;
                    tex.LoadRawTextureData(raw);
                    tex.Apply(false, false);
                    return Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f);
                }
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

    /// <summary>单个怪物的血条。普通怪物：头顶白色血条+像素数字；Boss/精英：仅追踪数据，由 BossHud 统一绘制。</summary>
    public class HPBar
    {
        public static bool BarsVisible = true;

        public readonly bool IsHudBoss;
        public readonly string DisplayName;

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

        // 以下字段仅普通怪物使用
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

        public int CurrentHp
        {
            get { return Mathf.Max(0, _hm.hp); }
        }

        public int MaxHp
        {
            get { return _maxHp; }
        }

        public bool IsAlive
        {
            get
            {
                GameObject go = _hm.gameObject;
                return go != null && go.activeInHierarchy && !_hm.isDead && _hm.hp > 0;
            }
        }

        public HPBar(HealthManager hm, bool hudBoss)
        {
            _hm = hm;
            IsHudBoss = hudBoss;
            DisplayName = hudBoss ? BossNames.Resolve(hm.gameObject) : "";
            _maxHp = Mathf.Max(1, hm.hp);
            _lastHp = hm.hp;

            if (hudBoss)
            {
                _root = null;
                _frame = null;
                _bg = null;
                _fill = null;
                return;
            }

            InitSprites();

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

        /// <summary>刷新。普通怪物返回 null；Boss/精英返回自身供 HUD 汇总。</summary>
        public HPBar Refresh()
        {
            if ((Object)_hm == null) return null;

            int hpNow = _hm.hp;
            if (hpNow > _maxHp) _maxHp = hpNow;

            if (IsHudBoss) return this;

            RefreshOverhead();
            return null;
        }

        private void RefreshOverhead()
        {
            if (_root == null) return;

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

            int hpNow = _hm.hp;
            if (hpNow > _maxHp) _maxHp = hpNow;
            if (hpNow < _lastHp) _flashUntil = Time.unscaledTime + 0.12f;
            _lastHp = hpNow;

            float frac = _maxHp <= 0 ? 0f : Mathf.Clamp01((float)hpNow / _maxHp);

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

            UpdateLabel(hpNow + "/" + _maxHp);
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

        public void DestroyBar()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
            }
        }
    }
}

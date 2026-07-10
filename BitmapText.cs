using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// 位图字体增强组件，通过重写 Mesh 实现缩放、字间距、等宽、BestFit 等功能。
/// 挂载到带 Text 组件的节点上即可生效，不依赖 Transform 缩放。
/// 
/// 参数优先级（从高到低）：
///   1. BestFit 开启 → Scale 参数被忽略，自动计算缩放以适配 RectTransform 宽度，
///      _minScale / _maxScale 限制缩放范围。
///   2. BestFit 关闭 → 使用手动设置的 Scale 作为缩放值。
///   3. Monospace 开启 → 指定字符集（_monospaceChars）统一使用 _monospaceWidth
///      （为0时自动取字符集中最宽的 advance）。
///   4. LetterSpacing 附加在每个字符 advance 之后，总是叠加生效。
///   5. 最终字符占用宽度 = advance × effectiveScale + letterSpacing。
/// 
/// 数据流：
///   Font 赋值 / 属性变更 → RebuildCharCache（缓存字符宽高）→ ComputeLayout
///   → 计算每字符 x 位置、effectiveScale、totalWidth → ModifyMesh 只写顶点。
/// </summary>
[RequireComponent(typeof(Text))]
[ExecuteInEditMode]
public class BitmapText : BaseMeshEffect
{
    [Header("BitMap Text")] [Tooltip("位图字体资源 (.fontsettings)")] [SerializeField]
    private Font _font;

    [Header("缩放")] [Tooltip("缩放倍数，1=原始大小。BestFit开启时此值被忽略")] [SerializeField]
    private float _scale = 1f;

    [Header("字间距")] [Tooltip("字符之间的额外间距（像素单位）")] [SerializeField]
    private float _letterSpacing = 0f;

    [Header("等宽")] [Tooltip("强制指定字符集等宽，开启后忽略指定字符自身的advance")] [SerializeField]
    private bool _monospace = false;

    [Tooltip("等宽模式下每个字符的宽度，0=自动取指定字符集中最宽字符")] [SerializeField]
    private float _monospaceWidth = 0f;

    [Tooltip("等宽模式作用的字符集，默认为数字0-9")] [SerializeField]
    private string _monospaceChars = "0123456789";

    [Header("BestFit（覆盖手动Scale，受Min/Max限制）")] [Tooltip("自动缩放文字以适配RectTransform宽度。优先级最高，开启后Scale参数被忽略")] [SerializeField]
    private bool _bestFit = false;

    [Tooltip("BestFit 模式下的最小缩放倍数")] [SerializeField]
    private float _minScale = 0.1f;

    [Tooltip("BestFit 模式下的最大缩放倍数")] [SerializeField]
    private float _maxScale = 3f;

    private Text _text;

    private Dictionary<char, CharData> _charCache = new Dictionary<char, CharData>();
    private List<float> _charXPositions = new List<float>();
    private float _totalWidth;
    private float _effectiveScale;
    private float _monoAdvance;
    private HashSet<char> _monoCharSet = new HashSet<char>();

    private string _lastText = "";
    private float _lastScale = float.NaN;
    private float _lastLetterSpacing = float.NaN;
    private bool _lastMonospace;
    private float _lastMonospaceWidth = float.NaN;
    private string _lastMonospaceChars = "";
    private bool _lastBestFit;
    private Font _lastFont;
    private float _lastRTWidth = float.NaN;
    private float _lastRTHeight = float.NaN;
    private TextAnchor _lastAlignment;
    private float _maxCharHeight;

    private static readonly string CharSet = "0123456789,.-ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz:%$+xX";

    private struct CharData
    {
        public float advance;
        public float width;
        public float height;
    }

    public Font font
    {
        get { return _font; }
        set
        {
            if (_font != value)
            {
                _font = value;
                InvalidateLayout();
            }
        }
    }

    public float scale
    {
        get { return _scale; }
        set
        {
            if (!Mathf.Approximately(_scale, value))
            {
                _scale = value;
                InvalidateLayout();
            }
        }
    }

    public float letterSpacing
    {
        get { return _letterSpacing; }
        set
        {
            if (!Mathf.Approximately(_letterSpacing, value))
            {
                _letterSpacing = value;
                InvalidateLayout();
            }
        }
    }

    public bool monospace
    {
        get { return _monospace; }
        set
        {
            if (_monospace != value)
            {
                _monospace = value;
                InvalidateLayout();
            }
        }
    }

    public float monospaceWidth
    {
        get { return _monospaceWidth; }
        set
        {
            if (!Mathf.Approximately(_monospaceWidth, value))
            {
                _monospaceWidth = value;
                InvalidateLayout();
            }
        }
    }

    public string monospaceChars
    {
        get { return _monospaceChars; }
        set
        {
            if (_monospaceChars != value)
            {
                _monospaceChars = value;
                InvalidateLayout();
            }
        }
    }

    public bool bestFit
    {
        get { return _bestFit; }
        set
        {
            if (_bestFit != value)
            {
                _bestFit = value;
                InvalidateLayout();
            }
        }
    }

    public float minScale
    {
        get { return _minScale; }
        set
        {
            if (!Mathf.Approximately(_minScale, value))
            {
                _minScale = value;
                InvalidateLayout();
            }
        }
    }

    public float maxScale
    {
        get { return _maxScale; }
        set
        {
            if (!Mathf.Approximately(_maxScale, value))
            {
                _maxScale = value;
                InvalidateLayout();
            }
        }
    }

    public float effectiveScale
    {
        get { return _effectiveScale; }
    }

    public float totalWidth
    {
        get { return _totalWidth; }
    }

    protected override void Awake()
    {
        base.Awake();
        _text = GetComponent<Text>();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        InvalidateLayout();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
    }

    private void InvalidateLayout()
    {
        _lastText = null;
        _lastFont = null;
        if (_text != null)
        {
            _text.SetVerticesDirty();
        }
    }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            InvalidateLayout();
        }
#endif

    private void RebuildCharCache()
    {
        _charCache.Clear();
        _maxCharHeight = 0f;
        if (_font == null) return;

        foreach (char ch in CharSet)
        {
            CacheCharacter(ch);
        }

        if (_text != null && !string.IsNullOrEmpty(_text.text))
        {
            foreach (char ch in _text.text)
            {
                CacheCharacter(ch);
            }
        }
    }

    private void CacheCharacter(char ch)
    {
        if (_charCache.ContainsKey(ch)) return;

        if (_font.GetCharacterInfo(ch, out CharacterInfo info))
        {
            float h = info.glyphHeight > 0 ? info.glyphHeight : info.vert.height;
            var cd = new CharData
            {
                advance = info.advance,
                width = info.glyphWidth > 0 ? info.glyphWidth : info.vert.width,
                height = h
            };
            _charCache[ch] = cd;
            if (h > _maxCharHeight) _maxCharHeight = h;
        }
    }

    private bool EnsureLayout()
    {
        RectTransform rt = transform as RectTransform;
        float rtWidth = rt != null ? rt.rect.width : 100f;
        float rtHeight = rt != null ? rt.rect.height : 0f;
        TextAnchor alignment = _text != null ? _text.alignment : TextAnchor.MiddleCenter;

        bool needsUpdate =
            _lastText != (_text != null ? _text.text : "") ||
            !Mathf.Approximately(_lastScale, _scale) ||
            !Mathf.Approximately(_lastLetterSpacing, _letterSpacing) ||
            _lastMonospace != _monospace ||
            !Mathf.Approximately(_lastMonospaceWidth, _monospaceWidth) ||
            _lastMonospaceChars != _monospaceChars ||
            _lastBestFit != _bestFit ||
            _lastFont != _font ||
            !Mathf.Approximately(_lastRTWidth, rtWidth) ||
            !Mathf.Approximately(_lastRTHeight, rtHeight) ||
            _lastAlignment != alignment;

        if (!needsUpdate) return true;

        if (_lastFont != _font)
        {
            RebuildCharCache();
        }

        if (_lastMonospaceChars != _monospaceChars)
        {
            _monoCharSet.Clear();
            if (!string.IsNullOrEmpty(_monospaceChars))
            {
                foreach (char c in _monospaceChars)
                    _monoCharSet.Add(c);
            }
        }

        ComputeLayout(rtWidth, rtHeight, alignment);

        _lastText = _text != null ? _text.text : "";
        _lastScale = _scale;
        _lastLetterSpacing = _letterSpacing;
        _lastMonospace = _monospace;
        _lastMonospaceWidth = _monospaceWidth;
        _lastMonospaceChars = _monospaceChars;
        _lastBestFit = _bestFit;
        _lastFont = _font;
        _lastRTWidth = rtWidth;
        _lastRTHeight = rtHeight;
        _lastAlignment = alignment;

        return true;
    }

    private void ComputeLayout(float rectWidth, float rectHeight, TextAnchor alignment)
    {
        string text = _text != null ? _text.text : "";
        _charXPositions.Clear();
        _effectiveScale = _scale;

        _monoAdvance = 0f;
        if (_monospace)
        {
            if (_monospaceWidth > 0f)
            {
                _monoAdvance = _monospaceWidth;
            }
            else
            {
                foreach (char mc in _monospaceChars)
                {
                    if (_charCache.TryGetValue(mc, out CharData mcd) && mcd.advance > _monoAdvance)
                        _monoAdvance = mcd.advance;
                }
            }
        }

        float xPos = 0f;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (!_charCache.TryGetValue(ch, out CharData cd)) continue;

            float advance = _monospace && _monoCharSet.Contains(ch) && _monoAdvance > 0f ? _monoAdvance : cd.advance;
            _charXPositions.Add(xPos);
            xPos += advance * _effectiveScale + _letterSpacing;
        }

        float lastSpacing = _charXPositions.Count > 0 ? _letterSpacing : 0f;
        _totalWidth = xPos - lastSpacing;

        if (_bestFit && _totalWidth > 0f && _maxCharHeight > 0f)
        {
            float low = _minScale;
            float high = _maxScale;

            bool hasWidth = rectWidth > 0f;
            bool hasHeight = rectHeight > 0f;

            if (!hasWidth && !hasHeight)
                return;

            for (int iter = 0; iter < 20; iter++)
            {
                float mid = (low + high) * 0.5f;
                bool fits = true;

                if (hasWidth)
                {
                    float testWidth = ComputeWidthAtScale(mid, text);
                    if (testWidth > rectWidth) fits = false;
                }

                if (hasHeight && fits)
                {
                    float testHeight = _maxCharHeight * mid;
                    if (testHeight > rectHeight) fits = false;
                }

                if (fits)
                    low = mid;
                else
                    high = mid;
            }

            _effectiveScale = low;

            _charXPositions.Clear();
            xPos = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (!_charCache.TryGetValue(ch, out CharData cd)) continue;

                float advance = _monospace && _monoCharSet.Contains(ch) && _monoAdvance > 0f
                    ? _monoAdvance
                    : cd.advance;
                _charXPositions.Add(xPos);
                xPos += advance * _effectiveScale + _letterSpacing;
            }

            _totalWidth = xPos - (_charXPositions.Count > 0 ? _letterSpacing : 0f);
        }
    }

    private float ComputeWidthAtScale(float s, string text)
    {
        float w = 0f;
        int charCount = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (!_charCache.TryGetValue(ch, out CharData cd)) continue;

            float advance = _monospace && _monoCharSet.Contains(ch) && _monoAdvance > 0f ? _monoAdvance : cd.advance;
            if (charCount > 0) w += _letterSpacing;
            w += advance * s;
            charCount++;
        }

        return w;
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive()) return;
        if (_font == null) return;
        if (_text == null) return;

        EnsureLayout();

        vh.Clear();

        string displayText = _text.text;
        if (string.IsNullOrEmpty(displayText)) return;

        RectTransform rt = transform as RectTransform;
        float rectWidth = rt != null ? rt.rect.width : 100f;
        float rectHeight = rt != null ? rt.rect.height : 100f;
        Vector2 pivot = rt != null ? rt.pivot : new Vector2(0.5f, 0.5f);

        float startX = GetStartX(rectWidth, _text.alignment, pivot.x);
        float textYCenter = GetTextYCenter(rectHeight, _text.alignment, pivot.y);

        Color textColor = _text.color;
        int charLayoutIdx = 0;
        int vertCount = 0;

        for (int i = 0; i < displayText.Length; i++)
        {
            char ch = displayText[i];
            if (!_charCache.TryGetValue(ch, out CharData cd)) continue;
            if (!_font.GetCharacterInfo(ch, out CharacterInfo ci)) continue;

            float xCenter = startX + _charXPositions[charLayoutIdx] + cd.width * _effectiveScale * 0.5f;
            float hw = cd.width * _effectiveScale * 0.5f;
            float hh = cd.height * _effectiveScale * 0.5f;

            float uvL = ci.uv.x;
            float uvR = ci.uv.x + ci.uv.width;
            float uvT = ci.uv.y;
            float uvB = ci.uv.y + ci.uv.height;

            if (ci.flipped)
            {
                float tmp = uvL;
                uvL = uvR;
                uvR = tmp;
            }

            UIVertex vert = UIVertex.simpleVert;
            vert.color = textColor;

            float yBottom = textYCenter - hh;
            float yTop = textYCenter + hh;

            vert.position = new Vector3(xCenter - hw, yBottom, 0f);
            vert.uv0 = new Vector2(uvL, uvB);
            vh.AddVert(vert);

            vert.position = new Vector3(xCenter - hw, yTop, 0f);
            vert.uv0 = new Vector2(uvL, uvT);
            vh.AddVert(vert);

            vert.position = new Vector3(xCenter + hw, yTop, 0f);
            vert.uv0 = new Vector2(uvR, uvT);
            vh.AddVert(vert);

            vert.position = new Vector3(xCenter + hw, yBottom, 0f);
            vert.uv0 = new Vector2(uvR, uvB);
            vh.AddVert(vert);

            vh.AddTriangle(vertCount, vertCount + 1, vertCount + 2);
            vh.AddTriangle(vertCount, vertCount + 2, vertCount + 3);

            vertCount += 4;
            charLayoutIdx++;
        }
    }

    private float GetStartX(float rectWidth, TextAnchor alignment, float pivotX)
    {
        float leftEdge = -rectWidth * pivotX;
        float rectCenter = leftEdge + rectWidth * 0.5f;
        float rightEdge = leftEdge + rectWidth;

        switch (alignment)
        {
            case TextAnchor.UpperLeft:
            case TextAnchor.MiddleLeft:
            case TextAnchor.LowerLeft:
                return leftEdge;
            case TextAnchor.UpperRight:
            case TextAnchor.MiddleRight:
            case TextAnchor.LowerRight:
                return rightEdge - _totalWidth;
            case TextAnchor.UpperCenter:
            case TextAnchor.MiddleCenter:
            case TextAnchor.LowerCenter:
            default:
                return rectCenter - _totalWidth * 0.5f;
        }
    }

    private float GetTextYCenter(float rectHeight, TextAnchor alignment, float pivotY)
    {
        float bottomEdge = -rectHeight * pivotY;
        float rectVCenter = bottomEdge + rectHeight * 0.5f;
        float topEdge = bottomEdge + rectHeight;
        float textHalfH = _maxCharHeight * _effectiveScale * 0.5f;

        switch (alignment)
        {
            case TextAnchor.UpperLeft:
            case TextAnchor.UpperCenter:
            case TextAnchor.UpperRight:
                return topEdge - textHalfH;
            case TextAnchor.LowerLeft:
            case TextAnchor.LowerCenter:
            case TextAnchor.LowerRight:
                return bottomEdge + textHalfH;
            case TextAnchor.MiddleLeft:
            case TextAnchor.MiddleCenter:
            case TextAnchor.MiddleRight:
            default:
                return rectVCenter;
        }
    }

#if UNITY_EDITOR
        [ContextMenu("Force Refresh")]
        private void ForceRefresh()
        {
            InvalidateLayout();
        }
#endif
}

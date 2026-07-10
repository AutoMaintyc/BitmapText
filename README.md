# BitmapText - Unity 位图字体增强组件

`BitmapText` 是一个 Unity UI 扩展组件，继承自 `BaseMeshEffect`，通过重写 `ModifyMesh` 直接生成字符网格顶点，实现标准 `Text` 组件不具备的能力：**任意缩放、字间距、等宽模式、BestFit 自动适配宽度** —— 且不依赖 Transform 缩放。

## 特性

| 功能 | 说明 |
|------|------|
| **独立缩放** | `_scale` 控制缩放倍数，不影响 Transform |
| **字间距** | `_letterSpacing` 在字符间插入像素级间距 |
| **等宽模式** | `_monospace` 对指定字符集强制等宽，适合数字/代码显示 |
| **BestFit** | `_bestFit` 自动缩放以适配 RectTransform 宽高（受 min/max 限制） |
| **对齐支持** | 完整支持 9 种 `TextAnchor` 对齐方式 |
| **Editor 实时预览** | `[ExecuteInEditMode]` 无需运行即可看到效果 |

## 参数优先级

1. **BestFit** → 覆盖手动 Scale，自动二分查找最佳缩放
2. **Scale** → BestFit 关闭时的手动缩放值
3. **Monospace** → 开启后对 `_monospaceChars` 指定字符统一宽度
4. **LetterSpacing** → 总是叠加在 advance 之后

```
最终字符占用宽度 = advance × effectiveScale + letterSpacing
```

## 环境要求

- Unity 5.x / 2017+ / 2018+ / 2019+ / 2020+ / 2021+
- 需要 `Font` 位图字体资源（.fontsettings）
- 目标节点需要挂载 `UnityEngine.UI.Text` 组件

## 快速开始

1. 将 `BitmapText.cs` 放入项目中任意可访问的文件夹
2. 在带 `Text` 组件的 GameObject 上添加 `BitmapText` 组件
3. 指定 `Font` 为你的位图字体资源
4. 调整 `Scale`、`LetterSpacing`、`Monospace`、`BestFit` 等参数
5. 运行或直接在 Scene 视图中查看效果

```csharp
// 代码中动态调整示例
var bmp = GetComponent<BitmapText>();
bmp.scale = 2f;
bmp.letterSpacing = 1.5f;
bmp.bestFit = true;
bmp.minScale = 0.5f;
bmp.maxScale = 2f;
bmp.monospace = true;
bmp.monospaceChars = "0123456789";
```

## 支持的字符集

```
0123456789,.-ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz:%$+xX
```

预缓存以上字符 + Text 中实际使用的字符，运行时动态扩展缓存。

## 属性一览

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `font` | `Font` | null | 位图字体资源 |
| `scale` | `float` | 1 | 缩放倍数（BestFit 开启时忽略） |
| `letterSpacing` | `float` | 0 | 字符额外间距 |
| `monospace` | `bool` | false | 等宽模式 |
| `monospaceWidth` | `float` | 0 | 等宽宽度（0=自动取最宽） |
| `monospaceChars` | `string` | `"0123456789"` | 等宽模式作用的字符集 |
| `bestFit` | `bool` | false | 自动适配宽度 |
| `minScale` | `float` | 0.1 | BestFit 最小缩放 |
| `maxScale` | `float` | 3 | BestFit 最大缩放 |
| `effectiveScale` | `float` | (只读) | 当前有效缩放 |
| `totalWidth` | `float` | (只读) | 文字总宽度 |

## 运行时 API

所有属性均可在运行时通过代码读写，修改后自动触发 `InvalidateLayout` → 刷新 Mesh。

## 工作原理

```
Font 赋值 / 属性变更
    ↓
RebuildCharCache（缓存字符 advance/width/height）
    ↓
ComputeLayout（逐字符计算 x 位置、effectiveScale、totalWidth）
    ↓
ModifyMesh（直接写顶点、UV、三角形到 VertexHelper）
```

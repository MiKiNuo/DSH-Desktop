namespace DshDesktop.Presentation.Avalonia.Themes;

/// <summary>
/// <c>DshTheme.axaml</c> 中资源键的编译期常量。
///
/// 缺口（候选 02）：这些键此前以裸字符串字面量散落在 C# 中（<c>RuntimeLifecycleBrushes.Resolve</c>），
/// 而 <c>ThemeResourceTests</c> 的正则只匹配 XAML 标记扩展 <c>{DynamicResource X}</c>，**扫不到 C#**。
/// 键名拼错或字典删键时，四条既有测试全部保持绿色，运行时静默回退到硬编码色。
///
/// 现在拼错键名是**编译失败**。<c>ThemeKeyTests</c> 双向校验本类与字典一致：
/// 字典新增键必须来此登记（<c>EveryDefinedThemeKey_HasConstant</c>），
/// 本类不得指向不存在的键（<c>EveryConstant_ResolvesToADefinedKey</c>）。
/// </summary>
public static class DshThemeKeys
{
    // ===== 中性基色 =====
    public const string BgBrush = "DshBgBrush";
    public const string Surface1Brush = "DshSurface1Brush";
    public const string Surface2Brush = "DshSurface2Brush";
    public const string Surface3Brush = "DshSurface3Brush";
    public const string Surface4Brush = "DshSurface4Brush";
    public const string LineBrush = "DshLineBrush";
    public const string LineSoftBrush = "DshLineSoftBrush";

    // ===== 文字 =====
    public const string TextBrush = "DshTextBrush";
    public const string Text2Brush = "DshText2Brush";
    public const string Text3Brush = "DshText3Brush";
    public const string Text4Brush = "DshText4Brush";

    // ===== 强调 =====
    public const string AccentBrush = "DshAccentBrush";
    public const string AccentHiBrush = "DshAccentHiBrush";
    public const string AccentLoBrush = "DshAccentLoBrush";
    public const string AccentSoftBrush = "DshAccentSoftBrush";
    public const string AccentLineBrush = "DshAccentLineBrush";
    public const string OnAccentBrush = "DshOnAccentBrush";

    // ===== 语义 =====
    public const string OkBrush = "DshOkBrush";
    public const string OkLineBrush = "DshOkLineBrush";
    public const string OkSoftBrush = "DshOkSoftBrush";
    public const string WarnBrush = "DshWarnBrush";
    public const string WarnLineBrush = "DshWarnLineBrush";
    public const string WarnSoftBrush = "DshWarnSoftBrush";
    public const string ErrBrush = "DshErrBrush";
    public const string ErrLineBrush = "DshErrLineBrush";
    public const string ErrSoftBrush = "DshErrSoftBrush";
    public const string ScrimBrush = "DshScrimBrush";

    // ===== 字号 =====
    public const string FontSizeHero = "DshFontSizeHero";
    public const string FontSizeTitle = "DshFontSizeTitle";
    public const string FontSizeBody = "DshFontSizeBody";
    public const string FontSizeBodySmall = "DshFontSizeBodySmall";
    public const string FontSizeSmall = "DshFontSizeSmall";
    public const string FontSizeCaption = "DshFontSizeCaption";

    // ===== 圆角 =====
    public const string RadiusCard = "DshRadiusCard";
    public const string RadiusMedium = "DshRadiusMedium";
    public const string RadiusSmall = "DshRadiusSmall";
    public const string RadiusBadge = "DshRadiusBadge";
}

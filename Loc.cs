using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UsageMon;

// Small runtime localisation: English source strings are the keys, so a missing entry simply stays English.
internal static class Loc
{
    public static bool Chinese { get; private set; }
    private static readonly ConditionalWeakTable<object, StrongBox<string>> Originals = new();

    // preference: "System", "zh-Hans" or "en"
    public static void Init(string? preference)
    {
        Chinese = preference switch
        {
            "zh-Hans" => true,
            "en" => false,
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
        };
    }

    public static string T(string text)
    {
        if (!Chinese || text.Length == 0) return text;
        if (Zh.TryGetValue(text, out var exact)) return exact;
        foreach (var (english, chinese) in Prefixes)
            if (text.StartsWith(english, StringComparison.Ordinal))
                return (chinese + text[english.Length..]).Replace(" · experimental", " · 实验性");
        return text;
    }

    // Translates (or restores) every known string under a window's content, including context menus.
    public static void Apply(object? node)
    {
        switch (node)
        {
            case null: return;
            case TextBlock text: Set(text, text.Text, v => text.Text = v); break;
            case MenuFlyoutSubItem sub:
                Set(sub, sub.Text, v => sub.Text = v);
                foreach (var item in sub.Items) Apply(item);
                break;
            case MenuFlyoutItem item: Set(item, item.Text, v => item.Text = v); break;
            case ScrollViewer viewer: Apply(viewer.Content); break;
            case ContentControl control:
                if (control.Content is string s) Set(control, s, v => control.Content = v);
                else Apply(control.Content);
                break;
            case ItemsControl items:
                foreach (var item in items.Items) Apply(item);
                break;
            case Panel panel:
                foreach (var child in panel.Children) Apply(child);
                break;
            case Border border: Apply(border.Child); break;
        }
        if (node is FrameworkElement { ContextFlyout: MenuFlyout menu })
            foreach (var item in menu.Items) Apply(item);
    }

    private static void Set(object owner, string current, Action<string> assign)
    {
        if (!Originals.TryGetValue(owner, out var original))
        {
            if (!Zh.ContainsKey(current)) return; // dynamic or unknown text is translated where it is produced
            original = new StrongBox<string>(current);
            Originals.Add(owner, original);
        }
        assign(T(original.Value!));
    }

    private static readonly (string English, string Chinese)[] Prefixes =
    [
        ("Claude rate limited · retry after ", "Claude 已限流 · 可在以下时间重试："),
        ("Balance unavailable (HTTP ", "余额不可用 (HTTP "),
        ("Claude usage unavailable (HTTP ", "Claude 用量不可用 (HTTP "),
        ("Last received ", "最近更新 "),
        ("Resets in ", "重置倒计时 "),
        ("Resets at ", "重置于 "),
        ("Resets ", "重置于 "),
        ("Cached reset · ", "缓存的重置时间 · "),
        ("Could not load image: ", "无法加载图片："),
        ("Open Platform balance · ", "开放平台余额 · "),
        ("Total (", "合计 ("),
        ("Set the DEEPSEEK_API_KEY environment variable", "请设置 DEEPSEEK_API_KEY 环境变量"),
        ("Set the MOONSHOT_API_KEY environment variable", "请设置 MOONSHOT_API_KEY 环境变量"),
    ];

    private static readonly Dictionary<string, string> Zh = new()
    {
        // sections and settings
        ["Panel"] = "面板", ["Start with Windows"] = "开机自动启动", ["Show"] = "显示", ["Layout"] = "布局", ["Joined"] = "合并", ["Separate"] = "分开",
        ["Direction"] = "方向", ["Horizontal"] = "水平", ["Vertical"] = "垂直", ["Always on top"] = "始终置顶",
        ["Snap to edges"] = "吸附屏幕边缘", ["Auto-hide at edge"] = "靠边自动隐藏",
        ["Taskbar"] = "任务栏", ["Taskbar meter"] = "任务栏指示器", ["Position"] = "位置", ["Right"] = "右侧", ["Left"] = "左侧",
        ["Percentage text size"] = "百分比文字大小", ["Experimental"] = "实验性",
        ["DeepSeek balance"] = "DeepSeek 余额", ["Kimi balance"] = "Kimi 余额",
        ["Needs DEEPSEEK_API_KEY environment variable"] = "需要设置 DEEPSEEK_API_KEY 环境变量",
        ["Needs MOONSHOT_API_KEY environment variable"] = "需要设置 MOONSHOT_API_KEY 环境变量",
        ["Appearance"] = "外观", ["Theme"] = "主题", ["Follow Windows"] = "跟随 Windows", ["Light"] = "浅色", ["Dark"] = "深色",
        ["Language"] = "语言", ["Follow system"] = "跟随系统",
        ["Size"] = "大小", ["Show percentage"] = "显示百分比", ["On hover"] = "悬停时", ["Always"] = "始终", ["Never"] = "从不",
        ["Material"] = "材质", ["Mica"] = "云母 (Mica)", ["Acrylic"] = "亚克力 (Acrylic)", ["Transparency"] = "透明度",
        ["Background image"] = "背景图片", ["Choose PNG…"] = "选择 PNG…", ["Remove"] = "移除",
        ["Usage"] = "用量", ["Codex · checking…"] = "Codex · 检查中…", ["Claude · checking…"] = "Claude · 检查中…",
        ["Refresh"] = "刷新", ["Auto refresh"] = "自动刷新", ["30 seconds"] = "30 秒", ["1 minute"] = "1 分钟",
        ["2 minutes"] = "2 分钟", ["5 minutes"] = "5 分钟",
        ["Claude Code status-line bridge"] = "Claude Code 状态栏桥接", ["Fallback when sign-in expires."] = "登录过期时的备用方案。",
        ["Enable"] = "启用", ["UsageMon Settings"] = "UsageMon 设置",
        // menus
        ["Settings"] = "设置", ["Refresh usage"] = "刷新用量", ["Hide panel"] = "隐藏面板", ["Exit"] = "退出",
        ["Exit UsageMon"] = "退出 UsageMon", ["Show / hide panel"] = "显示/隐藏面板", ["Swap order"] = "交换顺序",
        // tooltips
        ["Current session"] = "当前会话", ["This week"] = "本周", ["Outer ring · 5 hours"] = "外圈 · 5 小时",
        ["Inner ring · 7 days"] = "内圈 · 7 天", ["Unavailable"] = "不可用", ["balance"] = "余额",
        ["Cached reading · awaiting a successful refresh"] = "缓存读数 · 等待刷新成功",
        ["To fix: run claude in a terminal and sign in (/login), then send a message. The desktop app doesn't feed this ring."] =
            "修复方法：在终端运行 claude 并登录（/login），然后发送一条消息。桌面应用不会为此圆环提供数据。",
        ["Granted"] = "赠送", ["Topped up"] = "充值", ["Available"] = "可用", ["Cash"] = "现金", ["Voucher"] = "代金券",
        // states
        ["Live"] = "实时", ["Stale"] = "缓存", ["Loading"] = "加载中", ["SignInRequired"] = "需要登录",
        // messages
        ["Checking usage…"] = "正在检查用量…", ["Checking balance…"] = "正在检查余额…", ["Awaiting refresh"] = "等待刷新",
        ["Account quota · percent used"] = "账户配额 · 已用百分比", ["Quota unavailable for this account"] = "此账户无可用配额",
        ["No quota supplied by this account"] = "此账户未提供配额", ["Waiting before next refresh"] = "等待下次刷新",
        ["Sign in to Claude Code"] = "请登录 Claude Code", ["Claude subscription login required"] = "需要登录 Claude 订阅账户",
        ["Claude sign-in expired · sign in again"] = "Claude 登录已过期 · 请重新登录",
        ["Claude usage request timed out"] = "Claude 用量请求超时", ["Could not read Claude usage"] = "无法读取 Claude 用量",
        ["Last supplied by Claude Code. Updates while Claude Code is active."] = "由 Claude Code 提供，Claude Code 运行时更新。",
        ["Codex CLI not found. Set its executable in settings."] = "未找到 Codex CLI，请在设置中指定其可执行文件。",
        ["Sign in to Codex CLI"] = "请登录 Codex CLI", ["Codex usage request timed out"] = "Codex 用量请求超时",
        ["Could not read Codex usage. Check the CLI and login."] = "无法读取 Codex 用量，请检查 CLI 和登录状态。",
        ["Codex quota request failed."] = "Codex 配额请求失败。", ["Codex app-server closed its output."] = "Codex app-server 已关闭输出。",
        ["Usage unavailable · check settings"] = "用量不可用 · 请检查设置", ["Balance unavailable"] = "余额不可用",
        ["Saved reading · checking for updates…"] = "已保存的读数 · 正在检查更新…",
        ["API key rejected"] = "API 密钥被拒绝", ["API key rejected (Open Platform key required)"] = "API 密钥被拒绝（需要开放平台密钥）",
        ["No balance returned for this key"] = "此密钥未返回余额", ["Unexpected balance response"] = "余额响应格式异常",
        ["Account balance · experimental"] = "账户余额 · 实验性", ["Balance is too low for API calls"] = "余额不足，无法调用 API",
        ["Balance request timed out"] = "余额请求超时", ["Could not read DeepSeek balance"] = "无法读取 DeepSeek 余额",
        ["Could not read Kimi balance"] = "无法读取 Kimi 余额",
        ["No background image"] = "无背景图片", ["Please choose a PNG image."] = "请选择 PNG 图片。",
        ["Could not install the bridge. Check Claude settings access and try again."] = "无法安装桥接，请检查对 Claude 设置的访问权限后重试。",
        ["Claude bridge enabled. Restart Claude Code; quota arrives after a normal response. Existing settings were backed up."] =
            "Claude 桥接已启用。请重启 Claude Code，正常回复后即可显示配额。原有设置已备份。",
        ["A Claude status line already exists. It was left unchanged. See the integration instructions in README."] =
            "已存在 Claude 状态栏，未做更改。请参阅 README 中的集成说明。",
    };
}

namespace DshDesktop.Application.Updates;

/// <summary>
/// 借用失效时的 Active Runtime 回退选择策略（2026-09-19 v0.1.3 实机回归）：
/// 借用外部安装被删除后 HealRuntimePaths 清空 dshEntryPath，但 activeDshRuntime 仍为 null，
/// 启动链抱着空入口在 ValidateOptions 秒抛，连续 2 次进安全模式——磁盘上完好存在的自建版本
/// 全程无人过问。本策略回答一个问题：此刻该不该把激活切到某个自建版本、切到哪一个。
/// 纯函数：候选目录名由调用方枚举注入，不触碰文件系统。
/// </summary>
public static class ActiveRuntimeFallback
{
    /// <summary>
    /// 选择应回退激活的自建 Runtime 版本目录名。
    /// </summary>
    /// <param name="activeRuntime">当前激活的自建版本目录名（null/空 = 借用模式）。</param>
    /// <param name="borrowedEntryUsable">借用入口是否仍可用（非空且文件存在）。</param>
    /// <param name="selfBuiltVersions">磁盘上合法自建版本的目录名（调用方已按入口包目录存在过滤）。</param>
    /// <returns>应激活的版本目录名；无需回退或无候选时为 null。</returns>
    public static string? Select(
        string? activeRuntime,
        bool borrowedEntryUsable,
        IReadOnlyList<string> selfBuiltVersions)
    {
        // 已激活自建版本：尊重现状，绝不抢占。
        if (!string.IsNullOrWhiteSpace(activeRuntime))
        {
            return null;
        }

        // 借用安装仍可用：借用是默认形态（Q7-A），不自动切走。
        if (borrowedEntryUsable)
        {
            return null;
        }

        // 无自建候选：留给首启自检弹「下载并安装」，这里无能为力。
        if (selfBuiltVersions.Count == 0)
        {
            return null;
        }

        string best = selfBuiltVersions[0];
        for (int i = 1; i < selfBuiltVersions.Count; i++)
        {
            if (CompareVersionDirectories(selfBuiltVersions[i], best) > 0)
            {
                best = selfBuiltVersions[i];
            }
        }

        return best;
    }

    /// <summary>
    /// 版本目录名比较：数值版本优先（"0.1.10" &gt; "0.1.9"，字典序会误判）；
    /// 数值相同按 semver 约定正式版高于预发布；不可解析按序数兜底，保证确定性。
    /// </summary>
    private static int CompareVersionDirectories(string left, string right)
    {
        (Version? leftVersion, string leftPreRelease) = Parse(left);
        (Version? rightVersion, string rightPreRelease) = Parse(right);

        if (leftVersion is not null && rightVersion is not null)
        {
            int numeric = leftVersion.CompareTo(rightVersion);
            if (numeric != 0)
            {
                return numeric;
            }

            // 数值相同：正式版（无预发布标记）高于预发布；都带预发布标记按段比较（rc.10 > rc.2，字典序会误判）。
            bool leftIsStable = leftPreRelease.Length == 0;
            bool rightIsStable = rightPreRelease.Length == 0;
            if (leftIsStable != rightIsStable)
            {
                return leftIsStable ? 1 : -1;
            }

            int preRelease = ComparePreRelease(leftPreRelease, rightPreRelease);
            if (preRelease != 0)
            {
                return preRelease;
            }
        }

        return string.Compare(left, right, StringComparison.Ordinal);
    }

    /// <summary>semver 预发布段比较：按 "." 分段，数值段按数值、文本段按序数、段多者高。</summary>
    private static int ComparePreRelease(string left, string right)
    {
        string[] leftSegments = left.Split('.');
        string[] rightSegments = right.Split('.');
        for (int i = 0; i < Math.Max(leftSegments.Length, rightSegments.Length); i++)
        {
            if (i >= leftSegments.Length)
            {
                return -1;
            }

            if (i >= rightSegments.Length)
            {
                return 1;
            }

            bool leftNumeric = int.TryParse(leftSegments[i], out int leftNumber);
            bool rightNumeric = int.TryParse(rightSegments[i], out int rightNumber);
            if (leftNumeric && rightNumeric)
            {
                int numeric = leftNumber.CompareTo(rightNumber);
                if (numeric != 0)
                {
                    return numeric;
                }

                continue;
            }

            // semver：数值段低于文本段。
            if (leftNumeric != rightNumeric)
            {
                return leftNumeric ? -1 : 1;
            }

            int text = string.Compare(leftSegments[i], rightSegments[i], StringComparison.Ordinal);
            if (text != 0)
            {
                return text;
            }
        }

        return 0;
    }

    private static (Version? version, string preRelease) Parse(string directoryName)
    {
        string numericPart = directoryName;
        string preRelease = string.Empty;
        int dashIndex = directoryName.IndexOf('-');
        if (dashIndex >= 0)
        {
            numericPart = directoryName[..dashIndex];
            preRelease = directoryName[(dashIndex + 1)..];
        }

        return Version.TryParse(numericPart, out Version? version)
            ? (version, preRelease)
            : (null, preRelease);
    }
}

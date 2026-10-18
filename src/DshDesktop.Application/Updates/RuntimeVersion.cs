namespace DshDesktop.Application.Updates;

/// <summary>
/// DSH Runtime 版本（semver）比较。ADR-0011 前版本比较只在
/// <c>ActiveRuntimeFallback</c> 内以私有方法存在；更新源从 npm dist-tag 迁到 GitHub Releases
/// 后，"通道最新版本是否新于当前激活版本"必须做真正的 semver 比较——
/// 字符串判等会把 <c>0.2.1-rc.10 &gt; 0.2.1-rc.2</c> 误判为可更新，把回退版本误判为新版。
/// 纯函数，不触碰文件系统。
/// </summary>
public static class RuntimeVersion
{
    /// <summary>
    /// 比较两个版本字符串：数值段优先（"0.1.10" &gt; "0.1.9"，字典序会误判）；
    /// 数值相同按 semver 约定正式版高于预发布；不可解析按序数兜底，保证确定性。
    /// </summary>
    /// <param name="left">左侧版本。</param>
    /// <param name="right">右侧版本。</param>
    /// <returns>左大于右为正，小于为负，相等（含互为同一版本的等价写法）为 0。</returns>
    public static int Compare(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

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

    /// <summary>
    /// 判断候选版本是否严格新于当前版本。
    /// </summary>
    /// <param name="candidate">候选版本。</param>
    /// <param name="current">当前版本。</param>
    /// <returns>候选严格更新为 true。</returns>
    public static bool IsNewer(string candidate, string current) => Compare(candidate, current) > 0;

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

    private static (Version? version, string preRelease) Parse(string version)
    {
        string numericPart = version;
        string preRelease = string.Empty;
        int dashIndex = version.IndexOf('-');
        if (dashIndex >= 0)
        {
            numericPart = version[..dashIndex];
            preRelease = version[(dashIndex + 1)..];
        }

        return Version.TryParse(numericPart, out Version? parsed)
            ? (parsed, preRelease)
            : (null, preRelease);
    }
}
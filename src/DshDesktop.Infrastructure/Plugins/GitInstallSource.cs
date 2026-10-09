namespace DshDesktop.Infrastructure.Plugins;

/// <summary>
/// Git/远端安装源分类（pnpm add 原生接受 GitHub 源，但 git 协议依赖系统 PATH 上的 git；
/// tarball URL 由 pnpm 直接下载解压，不需要 git）。
/// </summary>
internal static class GitInstallSource
{
    /// <summary>
    /// 是否需要 git 才能安装的源：git 协议、托管简写（owner/repo）、非 tarball 的 http(s) 仓库地址。
    /// </summary>
    internal static bool RequiresGit(string source)
    {
        if (source.StartsWith("git+", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("git://", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("git@", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("github:", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("gitlab:", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("bitbucket:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return !IsTarballUrl(source);
        }

        return IsShorthand(source);
    }

    /// <summary>是否远端源（含 tarball URL）——包名反查走清单 dependencies 值匹配的门槛。</summary>
    internal static bool IsRemoteSource(string source)
        => RequiresGit(source) || IsTarballUrl(source);

    /// <summary>
    /// 归一化远端规格：等价写法（git+ / ssh / git@ / 托管前缀 / 简写 / .git / #片段）规约到同一形式，
    /// 供与清单 dependencies 值匹配（pnpm 落盘的写法与用户输入可能不同）。
    /// </summary>
    internal static string NormalizeSpec(string spec)
    {
        string s = spec.Trim();
        int fragment = s.IndexOf('#');
        if (fragment >= 0)
        {
            s = s[..fragment];
        }

        if (s.StartsWith("git+", StringComparison.OrdinalIgnoreCase))
        {
            s = s[4..];
        }

        if (s.StartsWith("ssh://git@github.com/", StringComparison.OrdinalIgnoreCase))
        {
            s = "https://github.com/" + s["ssh://git@github.com/".Length..];
        }
        else if (s.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
        {
            s = "https://github.com/" + s["git@github.com:".Length..];
        }
        else if (s.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
        {
            s = "https://github.com/" + s["github:".Length..];
        }
        else if (s.StartsWith("gitlab:", StringComparison.OrdinalIgnoreCase))
        {
            s = "https://gitlab.com/" + s["gitlab:".Length..];
        }
        else if (s.StartsWith("bitbucket:", StringComparison.OrdinalIgnoreCase))
        {
            s = "https://bitbucket.org/" + s["bitbucket:".Length..];
        }
        else if (IsShorthand(s))
        {
            s = "https://github.com/" + s;
        }

        if (s.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            s = s[..^4];
        }

        return s.TrimEnd('/').ToLowerInvariant();
    }

    /// <summary>远端源末段名（去 query/fragment 与 .git/.tgz/.tar.gz 后缀）——值反查未命中时的兜底。</summary>
    internal static string LeafName(string source)
    {
        string leaf = source;
        int cut = leaf.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            leaf = leaf[..cut];
        }

        leaf = leaf.TrimEnd('/');
        int separator = leaf.LastIndexOfAny(['/', ':']);
        if (separator >= 0)
        {
            leaf = leaf[(separator + 1)..];
        }

        if (leaf.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            leaf = leaf[..^7];
        }
        else if (leaf.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)
            || leaf.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            leaf = leaf[..^4];
        }

        return leaf;
    }

    /// <summary>tarball URL（.tgz / .tar.gz，允许 query/fragment）：直接下载解压，无需 git。</summary>
    private static bool IsTarballUrl(string source)
    {
        if (!source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string path = source;
        int cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            path = path[..cut];
        }

        return path.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>GitHub 简写 owner/repo（恰好一个 /，且字符集内不含 @，故不吞 npm 的 @scope/name）。</summary>
    private static bool IsShorthand(string source)
    {
        int slash = source.IndexOf('/');
        if (slash <= 0 || slash == source.Length - 1 || slash != source.LastIndexOf('/'))
        {
            return false;
        }

        return source.All(c => char.IsLetterOrDigit(c) || c is '_' or '.' or '-' or '/');
    }
}

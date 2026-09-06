using System.IO.Compression;

namespace DshDesktop.Infrastructure.Diagnostics;

/// <summary>
/// 表示诊断包导出器（Phase 8 Issue 06，原型 "导出诊断包" 按钮）：把日志目录打包为 zip。
/// System.IO.Compression 属共享框架，零新依赖；被占用（如 Serilog 正在写入）的日志文件
/// 以 FileShare.ReadWrite 尝试读取，仍失败则跳过该文件而非让整个导出失败。
/// </summary>
public static class DiagnosticsBundleExporter
{
    /// <summary>
    /// 把源目录（data/logs）下所有文件打包到目标 zip。
    /// </summary>
    /// <param name="sourceDirectory">日志目录绝对路径。</param>
    /// <param name="destinationZipPath">目标 zip 绝对路径（已存在则覆盖）。</param>
    public static void Export(string sourceDirectory, string destinationZipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationZipPath);

        using FileStream zipStream = new(destinationZipPath, FileMode.Create, FileAccess.Write);
        using ZipArchive archive = new(zipStream, ZipArchiveMode.Create);

        foreach (string filePath in Directory.EnumerateFiles(sourceDirectory))
        {
            // 目标 zip 存进源目录时跳过自身（FileMode.Create 已建半成品文件，惰性枚举会枚举到）。
            if (string.Equals(filePath, destinationZipPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            FileStream? source = TryOpenShared(filePath);
            if (source is null)
            {
                continue; // 被独占占用的文件跳过（不炸整个导出）。
            }

            using (source)
            {
                ZipArchiveEntry entry = archive.CreateEntry(Path.GetFileName(filePath));
                using Stream target = entry.Open();
                source.CopyTo(target);
            }
        }
    }

    private static FileStream? TryOpenShared(string filePath)
    {
        try
        {
            return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

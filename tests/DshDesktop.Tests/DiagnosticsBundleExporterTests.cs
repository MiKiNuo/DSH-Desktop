using System.IO.Compression;
using DshDesktop.Infrastructure.Diagnostics;

namespace DshDesktop.Tests;

/// <summary>
/// 诊断包导出测试（Phase 8 Issue 06：打包 data/logs 为 zip，System.IO.Compression 零新依赖）；
/// 真实临时目录，Serilog 占用中的日志文件跳过不炸。
/// </summary>
public sealed class DiagnosticsBundleExporterTests
{
    [Test]
    public async Task Export_PackagesAllLogFilesWithContent()
    {
        string sourceDir = NewTempDirectory();
        string zipPath = Path.Combine(Path.GetTempPath(), $"dsh-diag-test-{Guid.NewGuid():N}.zip");
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "dsh-desktop-20260101.log"), "第一行日志");
            File.WriteAllText(Path.Combine(sourceDir, "dsh-desktop-20260102.log"), "第二行日志");

            DiagnosticsBundleExporter.Export(sourceDir, zipPath);

            await Assert.That(File.Exists(zipPath)).IsTrue();
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            await Assert.That(archive.Entries.Count).IsEqualTo(2);
            ZipArchiveEntry entry = archive.GetEntry("dsh-desktop-20260101.log")!;
            using StreamReader reader = new(entry.Open());
            await Assert.That(await reader.ReadToEndAsync()).IsEqualTo("第一行日志");
        }
        finally
        {
            Directory.Delete(sourceDir, true);
            File.Delete(zipPath);
        }
    }

    [Test]
    public async Task Export_LockedFile_IsSkippedNotFatal()
    {
        string sourceDir = NewTempDirectory();
        string zipPath = Path.Combine(Path.GetTempPath(), $"dsh-diag-test-{Guid.NewGuid():N}.zip");
        string freeFile = Path.Combine(sourceDir, "free.log");
        string lockedFile = Path.Combine(sourceDir, "locked.log");
        try
        {
            File.WriteAllText(freeFile, "可读");
            File.WriteAllText(lockedFile, "被占用");

            // 模拟 Serilog 正在写入的日志文件（无共享）。
            using FileStream lockHandle = new(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            DiagnosticsBundleExporter.Export(sourceDir, zipPath);

            await Assert.That(File.Exists(zipPath)).IsTrue();
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            await Assert.That(archive.Entries.Count).IsEqualTo(1);
            await Assert.That(archive.GetEntry("free.log")).IsNotNull();
        }
        finally
        {
            Directory.Delete(sourceDir, true);
            File.Delete(zipPath);
        }
    }

    [Test]
    public async Task Export_EmptyDirectory_ProducesEmptyZip()
    {
        string sourceDir = NewTempDirectory();
        string zipPath = Path.Combine(Path.GetTempPath(), $"dsh-diag-test-{Guid.NewGuid():N}.zip");
        try
        {
            DiagnosticsBundleExporter.Export(sourceDir, zipPath);

            await Assert.That(File.Exists(zipPath)).IsTrue();
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            await Assert.That(archive.Entries.Count).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(sourceDir, true);
            File.Delete(zipPath);
        }
    }

    private static string NewTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dsh-diag-src-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}

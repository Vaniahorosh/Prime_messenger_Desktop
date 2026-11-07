using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace PrimeDesktop.Services;

public static class ExtensionInstallerService
{
    public static string PrepareExtensionFolder(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException("Путь к расширению не указан.", nameof(inputPath));

        // If folder selected directly
        if (Directory.Exists(inputPath))
        {
            if (File.Exists(Path.Combine(inputPath, "manifest.json")))
            {
                return inputPath;
            }

            var subManifest = Directory.GetFiles(inputPath, "manifest.json", SearchOption.AllDirectories).FirstOrDefault();
            if (subManifest != null)
            {
                return Path.GetDirectoryName(subManifest)!;
            }

            throw new Exception("В выбранной папке не найден файл manifest.json");
        }

        // If archive (.zip or .crx)
        if (File.Exists(inputPath))
        {
            string ext = Path.GetExtension(inputPath).ToLowerInvariant();
            string extName = Path.GetFileNameWithoutExtension(inputPath);
            string baseTargetDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PrimeDesktop", "Extensions", extName);

            if (Directory.Exists(baseTargetDir))
            {
                try { Directory.Delete(baseTargetDir, true); } catch { }
            }
            Directory.CreateDirectory(baseTargetDir);

            if (ext == ".zip")
            {
                ZipFile.ExtractToDirectory(inputPath, baseTargetDir, true);
            }
            else if (ext == ".crx")
            {
                ExtractCrx(inputPath, baseTargetDir);
            }
            else
            {
                throw new Exception("Поддерживаются только архивы .zip, .crx или папки с manifest.json.");
            }

            // Find manifest.json
            var manifestFile = Directory.GetFiles(baseTargetDir, "manifest.json", SearchOption.AllDirectories).FirstOrDefault();
            if (manifestFile != null)
            {
                return Path.GetDirectoryName(manifestFile)!;
            }

            return baseTargetDir;
        }

        throw new FileNotFoundException("Файл или папка расширения не найдены: " + inputPath);
    }

    private static void ExtractCrx(string crxPath, string targetDir)
    {
        byte[] bytes = File.ReadAllBytes(crxPath);
        // Find PK zip signature: 0x50, 0x4B, 0x03, 0x04
        int zipOffset = -1;
        for (int i = 0; i < bytes.Length - 4; i++)
        {
            if (bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] == 0x03 && bytes[i + 3] == 0x04)
            {
                zipOffset = i;
                break;
            }
        }

        if (zipOffset < 0)
        {
            throw new Exception("Некорректный формат CRX: архив ZIP внутри файла не обнаружен.");
        }

        using var ms = new MemoryStream(bytes, zipOffset, bytes.Length - zipOffset);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        archive.ExtractToDirectory(targetDir, true);
    }
}

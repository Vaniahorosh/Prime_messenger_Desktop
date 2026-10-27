using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PrimeDesktop.Services
{
    public static class AvatarManager
    {
        private static readonly string AvatarsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PrimeDesktop", "Avatars");

        static AvatarManager()
        {
            Directory.CreateDirectory(AvatarsDir);
        }

        public static string GetSafePeerKey(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return "avatar_unknown";

            // If it is a raw hardware address or MAC, convert to opaque peer id
            if (DeviceAnonymizer.IsHardwareIdentifier(identifier))
            {
                return DeviceAnonymizer.GetOpaquePeerId(identifier);
            }

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                identifier = identifier.Replace(c, '_');
            }
            return identifier.Replace(':', '_').Replace('/', '_').Replace('\\', '_');
        }

        public static string? SaveAvatar(string identifier, string sourceFilePath)
        {
            return SaveAvatarToHistory(identifier, sourceFilePath);
        }

        public static string? SaveAvatarToHistory(string identifier, string sourceFilePath)
        {
            if (!File.Exists(sourceFilePath)) return null;

            try
            {
                string safeKey = GetSafePeerKey(identifier);
                string ext = Path.GetExtension(sourceFilePath);
                if (string.IsNullOrWhiteSpace(ext)) ext = ".png";

                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");
                string historyPath = Path.Combine(AvatarsDir, $"avatar_{safeKey}_{timestamp}{ext}");
                File.Copy(sourceFilePath, historyPath, overwrite: true);

                // Also update canonical path
                string canonicalPath = Path.Combine(AvatarsDir, $"avatar_{safeKey}{ext}");
                File.Copy(sourceFilePath, canonicalPath, overwrite: true);

                return historyPath;
            }
            catch
            {
                return null;
            }
        }

        public static string? SaveAvatarBytesToHistory(string identifier, byte[] bytes, string ext = ".png")
        {
            if (bytes == null || bytes.Length == 0) return null;

            try
            {
                string safeKey = GetSafePeerKey(identifier);
                if (!ext.StartsWith(".")) ext = "." + ext;

                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");
                string historyPath = Path.Combine(AvatarsDir, $"avatar_{safeKey}_{timestamp}{ext}");
                File.WriteAllBytes(historyPath, bytes);

                // Also update canonical path
                string canonicalPath = Path.Combine(AvatarsDir, $"avatar_{safeKey}{ext}");
                File.WriteAllBytes(canonicalPath, bytes);

                return historyPath;
            }
            catch
            {
                return null;
            }
        }

        public static List<string> GetAvatarHistory(string identifier)
        {
            var list = new List<string>();
            try
            {
                string safeKey = GetSafePeerKey(identifier);
                var dirInfo = new DirectoryInfo(AvatarsDir);
                if (dirInfo.Exists)
                {
                    var files = dirInfo.GetFiles($"avatar_{safeKey}*.*")
                                       .OrderByDescending(f => f.LastWriteTimeUtc)
                                       .Select(f => f.FullName)
                                       .ToList();
                    list.AddRange(files);
                }
            }
            catch { }
            return list;
        }

        public static string? GetAvatarPath(string identifier)
        {
            try
            {
                string safeKey = GetSafePeerKey(identifier);
                string[] files = Directory.GetFiles(AvatarsDir, $"avatar_{safeKey}.*");
                if (files.Length > 0 && File.Exists(files[0]))
                {
                    return files[0];
                }
            }
            catch { }
            return null;
        }

        public static bool DeleteAvatar(string identifier)
        {
            try
            {
                string safeKey = GetSafePeerKey(identifier);
                string[] files = Directory.GetFiles(AvatarsDir, $"avatar_{safeKey}*.*");
                foreach (var file in files)
                {
                    try { File.Delete(file); } catch { }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

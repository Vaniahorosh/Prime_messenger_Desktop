using System;
using System.IO;

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
            if (!File.Exists(sourceFilePath)) return null;

            try
            {
                string safeKey = GetSafePeerKey(identifier);
                string ext = Path.GetExtension(sourceFilePath);
                if (string.IsNullOrWhiteSpace(ext)) ext = ".png";

                string destPath = Path.Combine(AvatarsDir, $"avatar_{safeKey}{ext}");

                // Remove existing if any
                if (File.Exists(destPath))
                {
                    try { File.Delete(destPath); } catch { }
                }

                File.Copy(sourceFilePath, destPath, overwrite: true);
                return destPath;
            }
            catch
            {
                return null;
            }
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
                string[] files = Directory.GetFiles(AvatarsDir, $"avatar_{safeKey}.*");
                foreach (var file in files)
                {
                    File.Delete(file);
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

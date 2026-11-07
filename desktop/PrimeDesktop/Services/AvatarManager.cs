using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

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

        public static string ComputeSha256(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }

        public static string? ComputeFileSha256(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;
            try
            {
                using var stream = File.OpenRead(filePath);
                using var sha = SHA256.Create();
                byte[] hash = sha.ComputeHash(stream);
                return Convert.ToHexString(hash);
            }
            catch
            {
                return null;
            }
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
                byte[] bytes = File.ReadAllBytes(sourceFilePath);
                string ext = Path.GetExtension(sourceFilePath);
                if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
                return SaveAvatarBytesToHistory(identifier, bytes, ext);
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

                string newHash = ComputeSha256(bytes);
                string canonicalPath = Path.Combine(AvatarsDir, $"avatar_{safeKey}{ext}");

                // 1. If canonical avatar already has the exact same content, do not duplicate
                if (File.Exists(canonicalPath))
                {
                    string? existingHash = ComputeFileSha256(canonicalPath);
                    if (string.Equals(existingHash, newHash, StringComparison.OrdinalIgnoreCase))
                    {
                        return canonicalPath;
                    }
                }

                // 2. Check if this avatar already exists in history files for this safeKey
                var dirInfo = new DirectoryInfo(AvatarsDir);
                if (dirInfo.Exists)
                {
                    var existingFiles = dirInfo.GetFiles($"avatar_{safeKey}*.*");
                    foreach (var f in existingFiles)
                    {
                        if (string.Equals(ComputeFileSha256(f.FullName), newHash, StringComparison.OrdinalIgnoreCase))
                        {
                            // Already in history: make it canonical and return canonical
                            try { File.WriteAllBytes(canonicalPath, bytes); } catch { }
                            return canonicalPath;
                        }
                    }
                }

                // 3. If there was a previous canonical file with different content,
                // archive it into history with a content hash tag before overwriting
                if (File.Exists(canonicalPath))
                {
                    string? oldHash = ComputeFileSha256(canonicalPath);
                    if (!string.IsNullOrEmpty(oldHash))
                    {
                        string oldExt = Path.GetExtension(canonicalPath);
                        string oldHistPath = Path.Combine(AvatarsDir, $"avatar_{safeKey}_h_{oldHash[..Math.Min(12, oldHash.Length)]}{oldExt}");
                        if (!File.Exists(oldHistPath))
                        {
                            try { File.Copy(canonicalPath, oldHistPath, overwrite: true); } catch { }
                        }
                    }
                }

                // 4. Save new history file with deterministic hash tag (prevents duplicate copies)
                string hashTag = newHash.Length >= 12 ? newHash[..12] : newHash;
                string historyPath = Path.Combine(AvatarsDir, $"avatar_{safeKey}_h_{hashTag}{ext}");
                File.WriteAllBytes(historyPath, bytes);

                // 5. Update canonical path
                File.WriteAllBytes(canonicalPath, bytes);

                return canonicalPath;
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
                    var allFiles = dirInfo.GetFiles($"avatar_{safeKey}*.*")
                                          .OrderByDescending(f => f.LastWriteTimeUtc)
                                          .ToList();

                    string? canonicalFile = allFiles.FirstOrDefault(f => 
                        Path.GetFileNameWithoutExtension(f.Name).Equals($"avatar_{safeKey}", StringComparison.OrdinalIgnoreCase))?.FullName;

                    var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // Add canonical file first if it exists
                    if (!string.IsNullOrEmpty(canonicalFile) && File.Exists(canonicalFile))
                    {
                        string? cHash = ComputeFileSha256(canonicalFile);
                        if (!string.IsNullOrEmpty(cHash))
                        {
                            seenHashes.Add(cHash);
                            list.Add(canonicalFile);
                        }
                    }

                    // Process history files, deduplicating identical content and cleaning up disk duplicates
                    foreach (var f in allFiles)
                    {
                        if (string.Equals(f.FullName, canonicalFile, StringComparison.OrdinalIgnoreCase))
                            continue;

                        string? hash = ComputeFileSha256(f.FullName);
                        if (string.IsNullOrEmpty(hash)) continue;

                        if (seenHashes.Contains(hash))
                        {
                            // Redundant duplicate file: clean up from disk to save storage
                            try { f.Delete(); } catch { }
                        }
                        else
                        {
                            seenHashes.Add(hash);
                            list.Add(f.FullName);
                        }
                    }
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

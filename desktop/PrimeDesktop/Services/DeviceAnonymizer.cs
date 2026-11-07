using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PrimeDesktop.Services
{
    public static class DeviceAnonymizer
    {
        private static readonly Regex MacRegex = new(
            @"^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$|^([0-9A-Fa-f]{12})$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex IpRegex = new(
            @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}(:\d+)?$",
            RegexOptions.Compiled);

        private static readonly Regex EmbeddedMacOrPeerRegex = new(
            @"\b([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})\b|\b([0-9A-Fa-f]{12})\b|\bpeer_[0-9a-fA-F]+\b|\bBluetooth#[^\s,\)\]]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool IsHardwareIdentifier(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string clean = value.Trim();
            return MacRegex.IsMatch(clean) || 
                   IpRegex.IsMatch(clean) || 
                   clean.StartsWith("Bluetooth#", StringComparison.OrdinalIgnoreCase) ||
                   clean.StartsWith("peer_", StringComparison.OrdinalIgnoreCase) ||
                   EmbeddedMacOrPeerRegex.IsMatch(clean);
        }

        public static string MaskMacAddresses(string? input, Func<string, string?>? resolver = null)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            return EmbeddedMacOrPeerRegex.Replace(input, match =>
            {
                string val = match.Value;
                if (resolver != null)
                {
                    var resolved = resolver(val);
                    if (!string.IsNullOrWhiteSpace(resolved) && !IsHardwareIdentifier(resolved))
                        return resolved;
                }
                return AnonymizeIdentifier(val);
            });
        }

        public static string GetOpaquePeerId(string? rawHardwareIdentifier)
        {
            if (string.IsNullOrWhiteSpace(rawHardwareIdentifier))
                return "peer_unknown";

            string clean = rawHardwareIdentifier.Trim().ToUpperInvariant();
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(clean));
            string hex = Convert.ToHexString(hash)[..8].ToLowerInvariant();
            return $"peer_{hex}";
        }

        public static string AnonymizeIdentifier(string? rawAddress)
        {
            if (string.IsNullOrWhiteSpace(rawAddress))
                return "Собеседник";

            string clean = rawAddress.Trim();

            // Wi-Fi / LAN IP address
            if (IpRegex.IsMatch(clean))
            {
                return "Prime Телефон 📱";
            }

            // Cryptographic SHA-256 hash to a 4-digit code (1000..9999) - never reveals MAC bytes!
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(clean.ToUpperInvariant()));
            int code = (Math.Abs(BitConverter.ToInt32(hash, 0)) % 9000) + 1000;
            return $"Собеседник #{code}";
        }

        public static string SanitizeDisplayName(string? candidateName, string? rawAddress = null)
        {
            if (string.IsNullOrWhiteSpace(candidateName) ||
                candidateName.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
                candidateName.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                IsHardwareIdentifier(candidateName))
            {
                return AnonymizeIdentifier(rawAddress ?? candidateName);
            }

            // Scrub any embedded MAC addresses or hardware tags
            string cleaned = EmbeddedMacOrPeerRegex.Replace(candidateName, "").Trim();
            cleaned = cleaned.Replace("()", "").Replace("[]", "").Trim();

            if (string.IsNullOrWhiteSpace(cleaned) || IsHardwareIdentifier(cleaned))
            {
                return AnonymizeIdentifier(rawAddress ?? candidateName);
            }

            return cleaned;
        }

        public static string SanitizeUsername(string? candidateUsername, string? rawAddress = null)
        {
            if (string.IsNullOrWhiteSpace(candidateUsername) ||
                candidateUsername.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
                candidateUsername.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                IsHardwareIdentifier(candidateUsername))
            {
                if (!string.IsNullOrWhiteSpace(rawAddress) && IpRegex.IsMatch(rawAddress.Trim()))
                    return "prime_companion";

                string seed = rawAddress ?? candidateUsername ?? "user";
                using var sha = SHA256.Create();
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(seed.ToUpperInvariant()));
                int code = (Math.Abs(BitConverter.ToInt32(hash, 0)) % 9000) + 1000;
                return $"id_{code}";
            }

            string cleaned = EmbeddedMacOrPeerRegex.Replace(candidateUsername, "").Trim().TrimStart('@');
            if (string.IsNullOrWhiteSpace(cleaned) || IsHardwareIdentifier(cleaned))
            {
                string seed = rawAddress ?? candidateUsername ?? "user";
                using var sha = SHA256.Create();
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(seed.ToUpperInvariant()));
                int code = (Math.Abs(BitConverter.ToInt32(hash, 0)) % 9000) + 1000;
                return $"id_{code}";
            }

            return cleaned;
        }

        /// <summary>
        /// Identifies unwanted device names such as headphones, earbuds, smartwatches, mice, etc.
        /// </summary>
        public static bool IsDisallowedPeripherals(string? deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return false;
            string name = deviceName.ToLowerInvariant();

            string[] blacklist = new[]
            {
                "buds", "airpods", "earphones", "headphone", "headset", "earbuds",
                "watch", "band", "gear", "fit", "mouse", "keyboard", "trackpad",
                "speaker", "soundbar", "jbl", "sony wh-", "sony wf-", "bose", "audio",
                "gamepad", "controller", "printer", "scanner", "tv", "dongle"
            };

            foreach (var b in blacklist)
            {
                if (name.Contains(b)) return true;
            }

            return false;
        }
    }
}
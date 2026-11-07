using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace PrimeCommon
{
    /// <summary>
    /// Валидация логина и отображаемого имени пользователя в соответствии с мобильной версией Prime Messenger.
    /// </summary>
    public static class ValidationUtils
    {
        private static readonly Regex LoginRegex = new(@"^[a-zA-Z0-9_]+$", RegexOptions.Compiled);
        private static readonly Regex NonAlphanumericRegex = new(@"[^\p{L}\p{N}]", RegexOptions.Compiled);

        private static readonly Dictionary<char, char> SkeletonMapping = new()
        {
            { 'а', 'a' }, { 'б', 'b' }, { 'в', 'v' }, { 'г', 'g' }, { 'д', 'd' },
            { 'е', 'e' }, { 'ё', 'e' }, { 'ж', 'z' }, { 'з', 'z' }, { 'и', 'i' },
            { 'й', 'i' }, { 'к', 'k' }, { 'л', 'l' }, { 'м', 'm' }, { 'н', 'n' },
            { 'о', 'o' }, { 'п', 'n' }, { 'р', 'p' }, { 'с', 'c' }, { 'т', 't' },
            { 'у', 'y' }, { 'ф', 'f' }, { 'х', 'x' }, { 'ц', 'c' }, { 'ч', 'c' },
            { 'ш', 's' }, { 'щ', 's' }, { 'ы', 'i' }, { 'э', 'e' }, { 'ю', 'u' },
            { 'я', 'a' }, { 'і', 'i' }, { 'ј', 'j' }, { 'ь', 'b' }, { 'ъ', 'b' },
            { '@', 'a' }, { '4', 'a' }, { '0', 'o' }, { '3', 'e' }, { '1', 'i' },
            { '!', 'i' }, { '$', 's' }, { '5', 's' }, { '7', 't' }, { '8', 'b' },
            { '|', 'l' }, { 'v', 'v' }, { 'w', 'v' }
        };

        private static readonly string[] RestrictedKeywords =
        {
            "prime",
            "прайм",
            "праим",
            "службаподдержки",
            "службаподдржки",
            "заметки",
            "support",
            "admin",
            "админ"
        };

        private static readonly List<string> NormalizedRestrictedKeywords = new();

        static ValidationUtils()
        {
            foreach (var kw in RestrictedKeywords)
            {
                string clean = NonAlphanumericRegex.Replace(kw, "");
                NormalizedRestrictedKeywords.Add(NormalizeToSkeleton(clean));
            }
        }

        public static bool IsValidLoginFormat(string text)
        {
            return LoginRegex.IsMatch(text);
        }

        public static string? ValidateLogin(string login)
        {
            string trimmed = login?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(trimmed))
                return "Логин не может быть пустым. Пожалуйста, придумайте уникальное имя пользователя.";
            if (trimmed.Length < 3)
                return "Слишком короткий логин (минимум 3 символа). Добавьте еще несколько букв или цифр.";
            if (trimmed.Length > 25)
                return "Слишком длинный логин (максимум 25 символов). Постарайтесь сделать его короче.";
            if (!IsValidLoginFormat(trimmed))
                return "В логине допускаются только латинские буквы (a-z, A-Z), цифры (0-9) и знак подчеркивания (_). Пробелы и кириллица запрещены.";
            if (IsRestricted(trimmed))
                return "Этот логин зарезервирован системой. Пожалуйста, выберите другой.";

            return null;
        }

        public static string? ValidateName(string name)
        {
            string trimmed = name?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(trimmed))
                return "Имя не может быть пустым. Как к вам обращаться?";
            if (trimmed.Length < 2)
                return "Имя должно содержать минимум 2 символа.";
            if (trimmed.Length > 32)
                return "Имя слишком длинное (максимум 32 символа).";
            if (IsRestricted(trimmed))
                return "Это имя защищено системой. Выберите другое.";

            return null;
        }

        public static bool IsRestricted(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            string lower = text.ToLowerInvariant();
            string skeleton = NonAlphanumericRegex.Replace(NormalizeToSkeleton(lower), "");
            string baseClean = NonAlphanumericRegex.Replace(lower, "");

            foreach (var normalizedKeyword in NormalizedRestrictedKeywords)
            {
                if (skeleton.Contains(normalizedKeyword) || baseClean.Contains(normalizedKeyword))
                    return true;
            }

            return false;
        }

        private static string NormalizeToSkeleton(string input)
        {
            var sb = new StringBuilder(input.Length);
            foreach (char c in input)
            {
                if (SkeletonMapping.TryGetValue(c, out char mapped))
                    sb.Append(mapped);
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }
    }
}

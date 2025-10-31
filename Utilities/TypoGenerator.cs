using System.Text.RegularExpressions;

namespace GonePhishing.Utilities
{
    public static class TypoGenerator
    {
        // Very simple safe generator: omission, transposition, repeated char, replacement of adjacent keys.
        // Keep small to avoid hammering networks. You can expand later.
        public static IEnumerable<string> GenerateVariants(string domain)
        {
            // remove protocol / path
            domain = domain.Trim().ToLower();
            domain = Regex.Replace(domain, @"^https?://", "");
            domain = domain.Split('/')[0];

            // split name and tld
            var parts = domain.Split('.');
            if (parts.Length < 2) yield break;
            var tld = parts[^1];
            var name = string.Join(".", parts.Take(parts.Length - 1)); // handle subdomains conservatively

            // 1) omission (remove one character)
            for (int i = 0; i < name.Length; i++)
            {
                var s = name.Remove(i, 1);
                if (s.Length >= 1) yield return $"{s}.{tld}";
            }

            // 2) transposition
            for (int i = 0; i < name.Length - 1; i++)
            {
                var arr = name.ToCharArray();
                var tmp = arr[i];
                arr[i] = arr[i + 1];
                arr[i + 1] = tmp;
                yield return $"{new string(arr)}.{tld}";
            }

            // 3) double char
            for (int i = 0; i < name.Length; i++)
            {
                var s = name.Insert(i, name[i].ToString());
                yield return $"{s}.{tld}";
            }

            // 4) append common digits
            yield return $"{name}1.{tld}";
            yield return $"{name}0.{tld}";

            // NOTE: keep list small to avoid mass generation => don't create thousands.
        }
    }
}

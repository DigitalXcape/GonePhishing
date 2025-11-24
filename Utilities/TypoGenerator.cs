using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GonePhishing.Utilities
{
    using System.Text.RegularExpressions;

    public static class TypoGenerator
    {
        public static IEnumerable<string> GenerateVariants(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
                yield break;

            // ----------------------------
            // Normalize domain
            // ----------------------------
            domain = domain.Trim().ToLower();
            domain = Regex.Replace(domain, @"^https?://", "");
            domain = domain.Split('/')[0];

            var parts = domain.Split('.');
            if (parts.Length < 2) yield break;

            string tld = parts[^1];
            string name = string.Join(".", parts.Take(parts.Length - 1));

            var output = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string candidate)
            {
                if (string.IsNullOrWhiteSpace(candidate)) return;
                string full = $"{candidate}.{tld}";
                if (!full.Equals(domain, StringComparison.OrdinalIgnoreCase))
                    output.Add(full);
            }

            const int MaxVariants = 1000;

            bool Cap() => output.Count < MaxVariants;

            // ---------------------------------------------------------
            // 1) Omission (exapmle.com → example.com)
            // ---------------------------------------------------------
            for (int i = 0; i < name.Length && Cap(); i++)
                Add(name.Remove(i, 1));

            // ---------------------------------------------------------
            // 2) Transposition (exampl → exmaple)
            // ---------------------------------------------------------
            for (int i = 0; i < name.Length - 1 && Cap(); i++)
            {
                var arr = name.ToCharArray();
                (arr[i], arr[i + 1]) = (arr[i + 1], arr[i]);
                Add(new string(arr));
            }

            // ---------------------------------------------------------
            // 3) Double character (exammple)
            // ---------------------------------------------------------
            for (int i = 0; i < name.Length && Cap(); i++)
                Add(name.Insert(i, name[i].ToString()));

            // ---------------------------------------------------------
            // 4) Full homoglyph substitution map
            // ---------------------------------------------------------
            var homoglyphs = new Dictionary<char, string[]>
        {
            { 'a', new[]{ "á","à","ä","â","@" } },
            { 'e', new[]{ "é","è","ë","3" } },
            { 'i', new[]{ "1","í","ï","l" } },
            { 'o', new[]{ "0","ó","ö","ò" } },
            { 'u', new[]{ "ü","ú","ù" } },
            { 'c', new[]{ "ç" } },
            { 'l', new[]{ "1","|" } },
            { 's', new[]{ "$","5" } },
            { 'g', new[]{ "9" } },
            { 'm', new[]{ "rn" } },
            { 'n', new[]{ "m" } },
        };

            for (int i = 0; i < name.Length && Cap(); i++)
            {
                char c = name[i];
                if (homoglyphs.ContainsKey(c))
                {
                    foreach (var rep in homoglyphs[c])
                    {
                        Add(name[..i] + rep + name[(i + 1)..]);
                        if (!Cap()) break;
                    }
                }
            }

            // ---------------------------------------------------------
            // 5) Keyboard adjacency map (big)
            // ---------------------------------------------------------
            var keyboardMap = new Dictionary<char, char[]>
        {
            { 'q', new[]{ 'w','a' } }, { 'w', new[]{ 'q','e','s' } },
            { 'e', new[]{ 'w','r','d' } }, { 'r', new[]{ 'e','t','f' } },
            { 't', new[]{ 'r','y','g' } }, { 'y', new[]{ 't','u','h' } },
            { 'u', new[]{ 'y','i','j' } }, { 'i', new[]{ 'u','o','k' } },
            { 'o', new[]{ 'i','p','l' } }, { 'p', new[]{ 'o' } },
            { 'a', new[]{ 'q','s','z' } }, { 's', new[]{ 'a','d','w','x' } },
            { 'd', new[]{ 's','f','e','c' } }, { 'f', new[]{ 'd','g','r','v' } },
            { 'g', new[]{ 'f','h','t','b' } }, { 'h', new[]{ 'g','j','y','n' } },
            { 'j', new[]{ 'h','k','u','m' } }, { 'k', new[]{ 'j','l','i' } },
            { 'l', new[]{ 'k','o' } },
            { 'z', new[]{ 'a','x' } }, { 'x', new[]{ 'z','c','s' } },
            { 'c', new[]{ 'x','v','d' } }, { 'v', new[]{ 'c','b','f' } },
            { 'b', new[]{ 'v','n','g' } }, { 'n', new[]{ 'b','m','h' } },
            { 'm', new[]{ 'n','j' } }
        };

            for (int i = 0; i < name.Length && Cap(); i++)
            {
                if (keyboardMap.TryGetValue(name[i], out var nbs))
                {
                    foreach (var rep in nbs)
                        Add(name[..i] + rep + name[(i + 1)..]);
                }
            }

            // ---------------------------------------------------------
            // 6) Bitsquatting (flip each bit in each character)
            // ---------------------------------------------------------
            foreach (var bits in Bitsquat(name).TakeWhile(_ => Cap()))
                Add(bits);

            // ---------------------------------------------------------
            // 7) Vowel swaps
            // ---------------------------------------------------------
            char[] vowels = { 'a', 'e', 'i', 'o', 'u' };
            for (int i = 0; i < name.Length && Cap(); i++)
            {
                if (vowels.Contains(name[i]))
                {
                    foreach (var v in vowels)
                        if (v != name[i])
                            Add(name[..i] + v + name[(i + 1)..]);
                }
            }

            // ---------------------------------------------------------
            // 8) Hyphenation variants
            // ---------------------------------------------------------
            for (int i = 1; i < name.Length && Cap(); i++)
                Add(name.Insert(i, "-"));

            // ---------------------------------------------------------
            // 9) Insert extra dot (subdomain injection)
            // ---------------------------------------------------------
            for (int i = 1; i < name.Length && Cap(); i++)
                Add(name[..i] + "." + name[i..]);

            // ---------------------------------------------------------
            // 10) Reverse string
            // ---------------------------------------------------------
            Add(new string(name.Reverse().ToArray()));

            // ---------------------------------------------------------
            // 11) TLD swapping
            // ---------------------------------------------------------
            string[] commonTlds = { "net", "org", "co", "info", "shop", "tech", "site", "xyz" };
            foreach (var newTld in commonTlds)
                output.Add($"{name}.{newTld}");

            // ---------------------------------------------------------
            // 12) Prefix/Suffix expansions
            // ---------------------------------------------------------
            string[] prefixes = { "my", "the", "get", "try", "go", "secure", "login" };
            string[] suffixes = { "app", "site", "online", "portal", "secure", "login" };

            foreach (var p in prefixes)
                Add(p + name);

            foreach (var s in suffixes)
                Add(name + s);

            // ---------------------------------------------------------
            // Final yield
            // ---------------------------------------------------------
            foreach (var item in output.Take(MaxVariants))
                yield return item;
        }

        // Bitsquatting generator (flip each bit in ASCII)
        private static IEnumerable<string> Bitsquat(string name)
        {
            for (int i = 0; i < name.Length; i++)
            {
                for (int bit = 0; bit < 8; bit++)
                {
                    char flipped = (char)(name[i] ^ (1 << bit));
                    if (char.IsLetterOrDigit(flipped))
                        yield return name[..i] + flipped + name[(i + 1)..];
                }
            }
        }
    }
}

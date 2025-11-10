using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GonePhishing.Utilities
{
    public static class TypoGenerator
    {
        // Very simple safe generator: omission, transposition, repeated char, replacement of adjacent keys.
        // Extended with: homoglyphs (m <-> rn), visual substitutions (o->0, l->1, a->@, etc.),
        // keyboard-adjacent replacements, hyphen/underscore insertion, dot removal, trailing punct/digit
        // and some conservative subdomain-aware handling.
        //
        // This generator is intentionally conservative (bounded and de-duplicated).
        public static IEnumerable<string> GenerateVariants(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain)) yield break;

            domain = domain.Trim().ToLower();
            domain = Regex.Replace(domain, @"^https?://", "");
            domain = domain.Split('/')[0];

            var parts = domain.Split('.');
            if (parts.Length < 2) yield break;
            var tld = parts[^1];
            var name = string.Join(".", parts.Take(parts.Length - 1)); // keep subdomains conservative

            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return;
                var candidate = $"{s}.{tld}";
                if (!candidate.Equals(domain, StringComparison.OrdinalIgnoreCase))
                    results.Add(candidate);
            }

            // limit to avoid explosion
            const int MaxVariants = 300;

            // Helper: apply per-position action with cap
            bool CheckCapAndYield()
            {
                if (results.Count > MaxVariants)
                {
                    // keep under limit - stop adding more
                    return false;
                }
                return true;
            }

            // 1) omission (remove one character)
            for (int i = 0; i < name.Length; i++)
            {
                var s = name.Remove(i, 1);
                if (s.Length >= 1)
                {
                    Add(s);
                    if (!CheckCapAndYield()) break;
                }
            }

            // 2) transposition (swap adjacent)
            for (int i = 0; i < name.Length - 1; i++)
            {
                var arr = name.ToCharArray();
                var tmp = arr[i];
                arr[i] = arr[i + 1];
                arr[i + 1] = tmp;
                Add(new string(arr));
                if (!CheckCapAndYield()) break;
            }

            // 3) double char (insert duplicate)
            for (int i = 0; i < name.Length; i++)
            {
                var s = name.Insert(i, name[i].ToString());
                Add(s);
                if (!CheckCapAndYield()) break;
            }

            // 4) append common digits
            Add($"{name}1");
            Add($"{name}0");

            // 5) trailing dash or underscore insertion (common typos)
            Add($"{name}-");
            Add($"{name}_");

            // 6) insert dash/underscore between characters (a few positions only; conservative)
            for (int i = 1; i < name.Length && i <= 4; i++) // only try a few early positions to limit growth
            {
                Add(name.Substring(0, i) + "-" + name.Substring(i));
                Add(name.Substring(0, i) + "_" + name.Substring(i));
                if (!CheckCapAndYield()) break;
            }

            // 7) remove dots in case subdomain dot gets removed (e.g., "www.example" -> "wwwexample")
            if (name.Contains('.'))
            {
                Add(name.Replace(".", ""));
            }

            // 8) collapse repeated characters (remove one of a run)
            for (int i = 0; i < name.Length - 1; i++)
            {
                if (name[i] == name[i + 1])
                {
                    var s = name.Remove(i, 1);
                    Add(s);
                    if (!CheckCapAndYield()) break;
                }
            }

            // 9) common visual/homoglyph substitutions (conservative set)
            var homoglyphs = new Dictionary<string, string[]>
            {
                // key -> possible visual replacements
                { "m", new[]{ "rn" } },         // m -> rn (and we'll handle reverse below)
                { "rn", new[]{ "m" } },         // rn -> m
                { "o", new[]{ "0" } },
                { "0", new[]{ "o" } },
                { "l", new[]{ "1", "i" } },
                { "i", new[]{ "l", "1" } },
                { "s", new[]{ "5" } },
                { "a", new[]{ "@" } },
                { "e", new[]{ "3" } },
                { "g", new[]{ "9" } },
            };

            // apply homoglyph replacements at each position where substring matches a key (use longest-first)
            var keysByLength = homoglyphs.Keys.OrderByDescending(k => k.Length).ToArray();
            for (int i = 0; i < name.Length; i++)
            {
                foreach (var k in keysByLength)
                {
                    if (i + k.Length <= name.Length && name.Substring(i, k.Length) == k)
                    {
                        foreach (var rep in homoglyphs[k])
                        {
                            var s = name.Substring(0, i) + rep + name.Substring(i + k.Length);
                            Add(s);
                            if (!CheckCapAndYield()) break;
                        }
                    }
                    if (!CheckCapAndYield()) break;
                }
                if (!CheckCapAndYield()) break;
            }

            // 10) keyboard-adjacent replacements (QWERTY neighbors) - conservative map
            var adjacent = new Dictionary<char, char[]>
            {
                { 'q', new[]{ 'w' } }, { 'w', new[]{ 'q','e','s' } }, { 'e', new[]{ 'w','r','s','d' } },
                { 'r', new[]{ 'e','t','d','f' } }, { 't', new[]{ 'r','y','f','g' } }, { 'y', new[]{ 't','u','g','h' } },
                { 'u', new[]{ 'y','i','h','j' } }, { 'i', new[]{ 'u','o','j','k' } }, { 'o', new[]{ 'i','p','k','l' } },
                { 'p', new[]{ 'o','l' } }, { 'a', new[]{ 's','q' } }, { 's', new[]{ 'a','d','w' } },
                { 'd', new[]{ 's','f','e' } }, { 'f', new[]{ 'd','g','r' } }, { 'g', new[]{ 'f','h','t' } },
                { 'h', new[]{ 'g','j','y' } }, { 'j', new[]{ 'h','k','u' } }, { 'k', new[]{ 'j','l','i' } },
                { 'l', new[]{ 'k','o' } }, { 'z', new[]{ 'x' } }, { 'x', new[]{ 'z','c' } },
                { 'c', new[]{ 'x','v' } }, { 'v', new[]{ 'c','b' } }, { 'b', new[]{ 'v','n' } },
                { 'n', new[]{ 'b','m' } }, { 'm', new[]{ 'n' } },
                { '1', new[]{ '2' } }, { '2', new[]{ '1','3' } }
            };

            for (int i = 0; i < name.Length; i++)
            {
                var ch = name[i];
                if (adjacent.TryGetValue(ch, out var neighbors))
                {
                    foreach (var nb in neighbors)
                    {
                        var s = name.Substring(0, i) + nb + name.Substring(i + 1);
                        Add(s);
                        if (!CheckCapAndYield()) break;
                    }
                }
                if (!CheckCapAndYield()) break;
            }

            // 11) replace characters with similar-looking punctuation/digits at end of word (e.g., 'store' -> 'stor3')
            for (int i = 0; i < name.Length; i++)
            {
                var c = name[i];
                string[] repl = c switch
                {
                    'o' => new[] { "0" },
                    'i' => new[] { "1", "l" },
                    'l' => new[] { "1", "i" },
                    's' => new[] { "5" },
                    'e' => new[] { "3" },
                    'a' => new[] { "@" },
                    _ => Array.Empty<string>()
                };
                foreach (var r in repl)
                {
                    var s = name.Substring(0, i) + r + name.Substring(i + 1);
                    Add(s);
                    if (!CheckCapAndYield()) break;
                }
                if (!CheckCapAndYield()) break;
            }

            // 12) "visual pair" special cases beyond homoglyph map: m <-> rn handled earlier.
            // Also add a small set of whole-word common mistakes (conservative)
            var commonWhole = new Dictionary<string, string[]>
            {
                { "www", new[]{ "" } }, // user might drop www (www.example -> example)
                { "shop", new[]{ "sh0p", "s-hop" } },
            };
            foreach (var kv in commonWhole)
            {
                if (name.Contains(kv.Key))
                {
                    foreach (var rep in kv.Value)
                    {
                        Add(name.Replace(kv.Key, rep));
                        if (!CheckCapAndYield()) break;
                    }
                }
                if (!CheckCapAndYield()) break;
            }

            // 13) small suffix/prefix additions (common typos)
            Add($"the{name}");
            Add($"{name}online");
            Add($"get{name}");
            Add($"{name}app");

            // Final: yield deduplicated results up to cap
            foreach (var v in results.Take(MaxVariants))
            {
                yield return v;
            }
        }
    }
}

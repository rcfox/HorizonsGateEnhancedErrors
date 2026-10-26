using System;
using System.Collections.Generic;
using System.Linq;

namespace EnhancedErrors
{
    public static class KeyLookup
    {
        /// <summary>Drop-in replacement for <c>dict[key]</c> that names the missing key.</summary>
        public static TValue Get<TKey, TValue>(Dictionary<TKey, TValue> dict, TKey key)
        {
            if (dict.TryGetValue(key, out var value))
                return value;
            throw new MissingKeyException(
                $"The given key {Describe(key)} was not present in the dictionary ({TypeName(typeof(Dictionary<TKey, TValue>))}).",
                key as string, dict.Keys as IEnumerable<string>);
        }

        static string Describe(object key)
        {
            if (key is string s)
                return "\"" + s + "\"";
            string text;
            try
            {
                text = key.ToString();
            }
            catch (Exception e)
            {
                text = "<ToString threw " + e.GetType().Name + ">";
            }
            return "'" + text + "' (" + TypeName(key.GetType()) + ")";
        }

        static string TypeName(Type type)
        {
            if (type == typeof(string)) return "string";
            if (type == typeof(int)) return "int";
            if (type == typeof(float)) return "float";
            if (type == typeof(bool)) return "bool";
            if (!type.IsGenericType)
                return type.Name;
            var name = type.Name.Substring(0, type.Name.IndexOf('`'));
            return name + "<" + string.Join(", ", type.GetGenericArguments().Select(TypeName)) + ">";
        }
    }

    /// <summary>
    /// KeyNotFoundException whose message adds "did you mean" suggestions for string keys.
    /// </summary>
    /// <remarks>
    /// Suggestions are only worked out when the message is read, so game code that catches
    /// KeyNotFoundException as part of normal flow doesn't pay for scanning the dictionary.
    /// </remarks>
    [Serializable]
    public class MissingKeyException : KeyNotFoundException
    {
        // Same threshold as the community mod validator.
        const int MaxDistance = 3;
        const int MaxSuggestions = 3;

        [NonSerialized] readonly string key;
        [NonSerialized] readonly IEnumerable<string> keys;
        string message;

        public MissingKeyException(string message, string key, IEnumerable<string> keys) : base(message)
        {
            this.key = key;
            this.keys = keys;
        }

        public override string Message
        {
            get
            {
                if (message == null)
                {
                    string suggestions = null;
                    try
                    {
                        suggestions = Suggest();
                    }
                    catch
                    {
                        // The dictionary may have changed since the throw; just skip the suggestions.
                    }
                    message = suggestions == null ? base.Message : base.Message + " Did you mean " + suggestions + "?";
                }
                return message;
            }
        }

        string Suggest()
        {
            if (key == null || keys == null)
                return null;
            var matches = keys
                .Where(k => k != null)
                .Select(k => new { Key = k, Distance = Distance(key, k) })
                .Where(m => m.Distance <= MaxDistance)
                .OrderBy(m => m.Distance)
                .ThenBy(m => m.Key, StringComparer.Ordinal)
                .Take(MaxSuggestions)
                .Select(m => "\"" + m.Key + "\"")
                .ToList();
            return matches.Count == 0 ? null : string.Join(" or ", matches);
        }

        static int Distance(string a, string b)
        {
            if (Math.Abs(a.Length - b.Length) > MaxDistance)
                return int.MaxValue;
            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++)
                previous[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                }
                var swap = previous;
                previous = current;
                current = swap;
            }
            return previous[b.Length];
        }
    }
}

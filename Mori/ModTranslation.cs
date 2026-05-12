using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Mafi.Localization;
using UnityEngine;

namespace UtilitiesPP
{
    internal static class ModTranslation
    {
        private static Dictionary<string, string> s_translations = new Dictionary<string, string>();
        private static bool s_initialized;

        public static void Initialize(string modRootPath)
        {
            if (s_initialized) return;
            s_initialized = true;

            try
            {
                string cultureId = LocalizationManager.CurrentLangInfo.CultureInfoId;
                string langCode = cultureId.Length >= 2 ? cultureId.Substring(0, 2) : "en";

                string translationsDir = Path.Combine(modRootPath, "translations");
                string langFile = Path.Combine(translationsDir, langCode + ".json");

                if (!File.Exists(langFile))
                    langFile = Path.Combine(translationsDir, "en.json");

                if (File.Exists(langFile))
                {
                    string json = File.ReadAllText(langFile);
                    s_translations = ParseJson(json);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[U++] Failed to load translations: {ex.Message}");
            }
        }

        public static string Get(string englishText)
        {
            if (s_translations.Count > 0 && s_translations.TryGetValue(englishText, out string translated))
                return translated;
            return englishText;
        }

        public static string GetFmt(string template, params object[] args)
        {
            string translated = Get(template);
            try
            {
                return string.Format(translated, args);
            }
            catch
            {
                return string.Format(template, args);
            }
        }

        private static Dictionary<string, string> ParseJson(string json)
        {
            var dict = new Dictionary<string, string>();
            var matches = Regex.Matches(json, @"""((?:[^""\\]|\\.)*)""\s*:\s*""((?:[^""\\]|\\.)*)""");
            foreach (Match m in matches)
            {
                string key = Unescape(m.Groups[1].Value);
                string value = Unescape(m.Groups[2].Value);
                dict[key] = value;
            }
            return dict;
        }

        private static string Unescape(string s)
        {
            return s.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\n", "\n").Replace("\\t", "\t");
        }
    }
}

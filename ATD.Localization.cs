// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mafi.Localization;
using UnityEngine;

namespace AutoTerrainDesignations
{
    internal static class AtdLocalization
    {
        private const string DEFAULT_LANG = "en";

        private static readonly Dictionary<string, string> s_selectedTranslations =
            new Dictionary<string, string>();
        private static readonly Dictionary<string, string> s_englishTranslations =
            new Dictionary<string, string>();
        private static readonly HashSet<string> s_loggedFallbacks = new HashSet<string>();
        private static bool s_initialized;

        internal static void Initialize(string modRootPath)
        {
            if (s_initialized)
                return;

            s_initialized = true;
            s_selectedTranslations.Clear();
            s_englishTranslations.Clear();
            s_loggedFallbacks.Clear();

            string translationsDir = Path.Combine(modRootPath, "translations");
            LoadTranslations(Path.Combine(translationsDir, DEFAULT_LANG + ".json"), s_englishTranslations, DEFAULT_LANG);

            LangSelection langSelection = GetCurrentLangSelection();
            foreach (string langCode in GetLanguageCandidates(langSelection))
            {
                if (string.Equals(langCode, DEFAULT_LANG, StringComparison.OrdinalIgnoreCase))
                    break;

                string langPath = Path.Combine(translationsDir, langCode + ".json");
                if (!File.Exists(langPath))
                    continue;

                LoadTranslations(langPath, s_selectedTranslations, langCode);
                if (s_selectedTranslations.Count > 0)
                {
                    Debug.Log("[ATD] Loaded localization '" + langCode + "' for culture '" + langSelection.CultureId + "'.");
                    break;
                }
            }

            if (s_selectedTranslations.Count == 0)
                Debug.Log("[ATD] Localization culture '" + langSelection.CultureId + "' is using English/code defaults.");
        }

        internal static string Tr(string key, string englishDefault)
        {
            if (TryGetUsableTranslation(s_selectedTranslations, key, out string translated))
                return translated;

            if (TryGetUsableTranslation(s_englishTranslations, key, out translated))
                return translated;

            LogFallbackOnce(key, "missing translation; using code default");
            return englishDefault;
        }

        internal static string TrFormat(string key, string englishDefault, params object[] args)
        {
            string template = Tr(key, englishDefault);
            try
            {
                return string.Format(template, args);
            }
            catch (Exception ex)
            {
                LogFallbackOnce(key, "format failed; using unformatted translation: " + ex.Message);
                return template;
            }
        }

        internal static string Tt(string key, string englishDefault)
        {
            return Tr(key, englishDefault) + "\n[" + AutoTerrainDesignationsMod.ModMarker + "]";
        }

        private static bool TryGetUsableTranslation(
            Dictionary<string, string> translations,
            string key,
            out string value)
        {
            if (translations.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value))
                return true;

            value = string.Empty;
            return false;
        }

        private static LangSelection GetCurrentLangSelection()
        {
            try
            {
                LocalizationManager.LangInfo langInfo = LocalizationManager.CurrentLangInfo;
                return new LangSelection(langInfo.CultureInfoId, langInfo.FileName);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ATD] Failed to detect current language: " + ex.Message);
            }

            return new LangSelection(DEFAULT_LANG, DEFAULT_LANG + ".json");
        }

        private static IEnumerable<string> GetLanguageCandidates(LangSelection langSelection)
        {
            var candidates = new List<string>();
            string fileName = (langSelection.FileName ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(fileName))
                AddCandidate(candidates, Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant());

            string normalized = (langSelection.CultureId ?? string.Empty).Trim().Replace('_', '-').ToLowerInvariant();

            AddCandidate(candidates, normalized);
            int separatorIndex = normalized.IndexOf('-');
            if (separatorIndex > 0)
                AddCandidate(candidates, normalized.Substring(0, separatorIndex));
            else if (normalized.Length >= 2)
                AddCandidate(candidates, normalized.Substring(0, 2));

            if (candidates.Contains("se") && !candidates.Contains("sv"))
                candidates.Add("sv");
            if (candidates.Contains("sv") && !candidates.Contains("se"))
                candidates.Add("se");

            AddCandidate(candidates, DEFAULT_LANG);
            return candidates;
        }

        private static void AddCandidate(List<string> candidates, string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !candidates.Contains(value))
                candidates.Add(value);
        }

        private static void LoadTranslations(
            string path,
            Dictionary<string, string> target,
            string langCode)
        {
            try
            {
                if (!File.Exists(path))
                {
                    if (!string.Equals(langCode, DEFAULT_LANG, StringComparison.OrdinalIgnoreCase))
                        Debug.LogWarning("[ATD] Translation file not found: " + path);
                    return;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                foreach (KeyValuePair<string, string> kvp in ParseTranslations(json))
                    target[kvp.Key] = kvp.Value;

                if (target.Count == 0)
                    Debug.LogWarning("[ATD] Translation file contained no usable entries: " + path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ATD] Failed to load translation file '" + path + "': " + ex.Message);
            }
        }

        private static Dictionary<string, string> ParseTranslations(string json)
        {
            var translations = new Dictionary<string, string>();

            int index = 0;
            SkipWhitespace(json, ref index);
            if (!TryConsume(json, ref index, '{'))
                return translations;

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                if (TryConsume(json, ref index, '}'))
                    break;

                if (!TryReadJsonString(json, ref index, out string key))
                    break;

                SkipWhitespace(json, ref index);
                if (!TryConsume(json, ref index, ':'))
                    break;

                SkipWhitespace(json, ref index);
                if (TryReadJsonString(json, ref index, out string simpleValue))
                {
                    translations[key] = simpleValue;
                }
                else if (TryConsume(json, ref index, '{'))
                {
                    if (TryReadTranslationObject(json, ref index, out string objectValue))
                        translations[key] = objectValue;
                }
                else
                {
                    SkipJsonValue(json, ref index);
                }

                SkipWhitespace(json, ref index);
                TryConsume(json, ref index, ',');
            }

            return translations;
        }

        private static bool TryReadTranslationObject(string json, ref int index, out string value)
        {
            string enValue = string.Empty;
            string textValue = string.Empty;

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                if (TryConsume(json, ref index, '}'))
                    break;

                if (!TryReadJsonString(json, ref index, out string propertyName))
                    break;

                SkipWhitespace(json, ref index);
                if (!TryConsume(json, ref index, ':'))
                    break;

                SkipWhitespace(json, ref index);
                if (TryReadJsonString(json, ref index, out string propertyValue))
                {
                    if (string.Equals(propertyName, "text", StringComparison.OrdinalIgnoreCase))
                        textValue = propertyValue;
                    else if (string.Equals(propertyName, "en", StringComparison.OrdinalIgnoreCase))
                        enValue = propertyValue;
                }
                else
                {
                    SkipJsonValue(json, ref index);
                }

                SkipWhitespace(json, ref index);
                TryConsume(json, ref index, ',');
            }

            value = !string.IsNullOrWhiteSpace(textValue) ? textValue : enValue;
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool TryReadJsonString(string json, ref int index, out string value)
        {
            value = string.Empty;
            SkipWhitespace(json, ref index);
            if (!TryConsume(json, ref index, '"'))
                return false;

            var sb = new StringBuilder();
            while (index < json.Length)
            {
                char c = json[index++];
                if (c == '"')
                {
                    value = sb.ToString();
                    return true;
                }

                if (c != '\\' || index >= json.Length)
                {
                    sb.Append(c);
                    continue;
                }

                char escaped = json[index++];
                switch (escaped)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (index + 4 <= json.Length
                            && int.TryParse(
                                json.Substring(index, 4),
                                System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out int codePoint))
                        {
                            sb.Append((char)codePoint);
                            index += 4;
                        }
                        break;
                    default:
                        sb.Append(escaped);
                        break;
                }
            }

            return false;
        }

        private static void SkipJsonValue(string json, ref int index)
        {
            int depth = 0;
            while (index < json.Length)
            {
                char c = json[index];
                if (c == '"')
                {
                    TryReadJsonString(json, ref index, out _);
                    continue;
                }

                if (c == '{' || c == '[')
                {
                    depth++;
                    index++;
                    continue;
                }

                if (c == '}' || c == ']')
                {
                    if (depth == 0)
                        return;
                    depth--;
                    index++;
                    continue;
                }

                if (depth == 0 && c == ',')
                    return;

                index++;
            }
        }

        private static void SkipWhitespace(string value, ref int index)
        {
            while (index < value.Length && char.IsWhiteSpace(value[index]))
                index++;
        }

        private static bool TryConsume(string value, ref int index, char expected)
        {
            if (index < value.Length && value[index] == expected)
            {
                index++;
                return true;
            }

            return false;
        }

        private static void LogFallbackOnce(string key, string reason)
        {
            if (s_loggedFallbacks.Add(key))
                Debug.LogWarning("[ATD] Localization fallback for '" + key + "': " + reason + ".");
        }

        private readonly struct LangSelection
        {
            public readonly string CultureId;
            public readonly string FileName;

            public LangSelection(string cultureId, string fileName)
            {
                CultureId = cultureId;
                FileName = fileName;
            }
        }
    }
}

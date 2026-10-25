using System.Collections.Generic;
using UnityEngine;

namespace EcoBotBuilders.Level1.View
{
    /// <summary>
    /// Offline progression store. Completed-level state is persisted on the device with
    /// <see cref="PlayerPrefs"/> (no backend, no network). The unlock decision itself is
    /// delegated to the Unity-free <see cref="Progression"/> rules in Core, so the lock is
    /// enforced in the persistence/state layer rather than only in the UI.
    /// </summary>
    public static class GameProgress
    {
        const string CompletedKey = "EcoBot.CompletedLevels";
        const string CosmeticKey = "EcoBot.Cosmetics";

        /// <summary>Which level a freshly loaded game scene should present. Defaults to Level 1.</summary>
        public static int SelectedLevel = LevelCatalog.First;

        /// <summary>Cosmetic reward slugs understood by the client, matching backend gameConfig.js.</summary>
        public static string CosmeticFor(int levelId)
        {
            switch (levelId)
            {
                case 1: return "leaf_antenna";
                case 2: return "solar_visor";
                default: return "";
            }
        }

        static HashSet<int> ParseSet(string key)
        {
            var set = new HashSet<int>();
            string raw = PlayerPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(raw)) return set;
            foreach (var token in raw.Split(','))
            {
                int value;
                if (int.TryParse(token, out value)) set.Add(value);
            }
            return set;
        }

        static void WriteSet(string key, HashSet<int> set)
        {
            var parts = new List<string>();
            foreach (var value in set) parts.Add(value.ToString());
            PlayerPrefs.SetString(key, string.Join(",", parts.ToArray()));
            PlayerPrefs.Save();
        }

        public static HashSet<int> CompletedLevels() { return ParseSet(CompletedKey); }

        public static bool IsCompleted(int levelId) { return CompletedLevels().Contains(levelId); }

        /// <summary>Authoritative unlock check used by both the Level Select UI and scene entry.</summary>
        public static bool IsUnlocked(int levelId) { return Progression.IsUnlocked(levelId, CompletedLevels()); }

        /// <summary>Records a first-time completion and awards the level's cosmetic. Ignores unknown ids.</summary>
        public static void MarkCompleted(int levelId)
        {
            if (!LevelCatalog.Exists(levelId)) return;
            var completed = CompletedLevels();
            if (completed.Add(levelId)) WriteSet(CompletedKey, completed);

            string cosmetic = CosmeticFor(levelId);
            if (!string.IsNullOrEmpty(cosmetic))
            {
                string owned = PlayerPrefs.GetString(CosmeticKey, "");
                if (("," + owned + ",").IndexOf("," + cosmetic + ",") < 0)
                {
                    owned = string.IsNullOrEmpty(owned) ? cosmetic : owned + "," + cosmetic;
                    PlayerPrefs.SetString(CosmeticKey, owned);
                    PlayerPrefs.Save();
                }
            }
        }

        /// <summary>The next level if completing <paramref name="levelId"/> has unlocked it, else 0.</summary>
        public static int NextUnlocked(int levelId) { return Progression.UnlockedNext(levelId, CompletedLevels()); }

        /// <summary>
        /// Safely choose which level to actually load. Falls back to Level 1 when a caller asks
        /// for a locked or unknown level, so the lock cannot be bypassed by jumping scenes.
        /// </summary>
        public static int ResolveSelected()
        {
            return IsUnlocked(SelectedLevel) ? SelectedLevel : LevelCatalog.First;
        }
    }
}

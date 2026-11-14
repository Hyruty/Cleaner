using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Cleaner
{
    /// <summary>
    /// User preferences persisted as pretty printed JSON in the data folder.
    /// Unknown keys are ignored, missing keys fall back to defaults, so a
    /// hand-edited or older file never breaks the app.
    /// </summary>
    internal sealed class Settings
    {
        public string Language = "auto";
        public bool AutoCleanEnabled;
        public int LogRetentionDays = 30;
        public int WindowWidth = 1000;
        public int WindowHeight = 780;
        public bool WindowMaximized;
        public int WindowX = int.MinValue;
        public int WindowY = int.MinValue;
        public Dictionary<string, bool> Categories = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public bool IsEnabled(string categoryId)
        {
            bool value;
            if (Categories.TryGetValue(categoryId, out value))
            {
                return value;
            }

            return false;
        }

        public void SetEnabled(string categoryId, bool enabled)
        {
            if (string.IsNullOrEmpty(categoryId))
            {
                return;
            }

            Categories[categoryId] = enabled;
        }

        public static Settings CreateDefault()
        {
            Settings settings = new Settings();
            foreach (CleanupCategory category in CleanupCatalog.All())
            {
                settings.Categories[category.Id] = category.DefaultEnabled;
            }

            return settings;
        }

        public static Settings Load()
        {
            Settings defaults = CreateDefault();
            string file = AppInfo.SettingsFile;
            if (!File.Exists(file))
            {
                return defaults;
            }

            try
            {
                Dictionary<string, object> root = Json.AsObject(Json.Parse(File.ReadAllText(file, Encoding.UTF8)));
                if (root == null)
                {
                    return defaults;
                }

                string language = Json.GetString(root, "language", defaults.Language);
                if (!string.IsNullOrEmpty(language))
                {
                    defaults.Language = language;
                }

                defaults.AutoCleanEnabled = Json.GetBool(root, "autoCleanEnabled", defaults.AutoCleanEnabled);
                defaults.LogRetentionDays = Math.Max(1, Math.Min(3650, Json.GetInt(root, "logRetentionDays", defaults.LogRetentionDays)));
                defaults.WindowWidth = Math.Max(760, Json.GetInt(root, "windowWidth", defaults.WindowWidth));
                defaults.WindowHeight = Math.Max(560, Json.GetInt(root, "windowHeight", defaults.WindowHeight));
                defaults.WindowMaximized = Json.GetBool(root, "windowMaximized", false);
                defaults.WindowX = Json.GetInt(root, "windowX", int.MinValue);
                defaults.WindowY = Json.GetInt(root, "windowY", int.MinValue);

                object categoriesNode;
                if (root.TryGetValue("categories", out categoriesNode))
                {
                    Dictionary<string, object> categories = Json.AsObject(categoriesNode);
                    if (categories != null)
                    {
                        foreach (CleanupCategory category in CleanupCatalog.All())
                        {
                            object raw;
                            if (categories.TryGetValue(category.Id, out raw) && raw != null)
                            {
                                bool parsed;
                                if (raw is bool)
                                {
                                    defaults.Categories[category.Id] = (bool)raw;
                                }
                                else if (bool.TryParse(Convert.ToString(raw), out parsed))
                                {
                                    defaults.Categories[category.Id] = parsed;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Exception("Reading settings failed, using defaults", ex);
            }

            return defaults;
        }

        public bool Save()
        {
            try
            {
                Directory.CreateDirectory(AppInfo.DataRoot);

                Dictionary<string, object> root = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "version", 2 },
                    { "language", Language ?? "auto" },
                    { "autoCleanEnabled", AutoCleanEnabled },
                    { "logRetentionDays", LogRetentionDays },
                    { "windowWidth", WindowWidth },
                    { "windowHeight", WindowHeight },
                    { "windowMaximized", WindowMaximized },
                    { "windowX", WindowX },
                    { "windowY", WindowY }
                };

                Dictionary<string, object> categories = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (CleanupCategory category in CleanupCatalog.All())
                {
                    categories[category.Id] = IsEnabled(category.Id);
                }

                root["categories"] = categories;

                string json = Json.Serialize(root);
                string target = AppInfo.SettingsFile;
                string temp = target + ".tmp";
                File.WriteAllText(temp, json, new UTF8Encoding(false));

                if (File.Exists(target))
                {
                    try
                    {
                        File.Replace(temp, target, null);
                    }
                    catch (IOException)
                    {
                        File.Copy(temp, target, true);
                        File.Delete(temp);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        File.Copy(temp, target, true);
                        File.Delete(temp);
                    }
                }
                else
                {
                    File.Move(temp, target);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Exception("Saving settings failed", ex);
                return false;
            }
        }
    }
}

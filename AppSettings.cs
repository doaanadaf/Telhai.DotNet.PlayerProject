using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Telhai.DotNet.PlayerProject
{
    public class AppSettings
    {
        public List<string> MusicFolders { get; set; } = new List<string>();
        private const string SETTINGS_FILE = "settings.json";

        /// <summary>
        /// Save List into Settings
        /// </summary>
        /// <param name="settings"></param>
        public static void Save(AppSettings settings)
        {
            string json = JsonSerializer.Serialize(settings);
            File.WriteAllText(SETTINGS_FILE, json);
        }
        public static AppSettings Load()
        {
            if (File.Exists(SETTINGS_FILE))
            {
                string json = File.ReadAllText(SETTINGS_FILE);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            return new AppSettings();
        }





    }
}

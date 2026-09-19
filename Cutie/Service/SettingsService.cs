using Cutie.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cutie.Service
{
    public class SettingsService
    {
        private readonly string _path;

        public SettingsService(string path)
        {
            _path = path;
        }

        public SettingsModel GetSettings()
        {
            if (!System.IO.File.Exists(_path))
            {
                var defaultSettings = new SettingsModel();
                SaveSettings(defaultSettings);
                return defaultSettings;
            }
            var json = System.IO.File.ReadAllText(_path);
            return Newtonsoft.Json.JsonConvert.DeserializeObject<SettingsModel>(json);
        }

        public void SaveSettings(SettingsModel settings)
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(settings, Newtonsoft.Json.Formatting.Indented);
            System.IO.File.WriteAllText(_path, json);
        }
    }
}

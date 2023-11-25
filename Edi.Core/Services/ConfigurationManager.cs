﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace Edi.Core
{

    public class ConfigurationManager
    {
        private string _filePath;
        private Dictionary<string, JObject> _configurations;

        public ConfigurationManager(string filePath)
        {
            _filePath = filePath;

            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                _configurations = JsonConvert.DeserializeObject<Dictionary<string, JObject>>(json);
            }

            if (_configurations == null)
            {
                _configurations = new Dictionary<string, JObject>();
            }
        }

        public T Get<T>() where T : new()
        {
            var typeName = typeof(T).Name.Replace("Config", "");

            if (_configurations.TryGetValue(typeName, out var configJson))
            {
                var config = configJson.ToObject<T>();
                SubscribeToChanges(config, typeName);
                return config;
            }
            else
            {
                var config = new T();
                Save(config);
                SubscribeToChanges(config, typeName);
                return config;
            }
        }
        private void SubscribeToChanges<T>(T config, string typeName)
        {
            if (config is INotifyPropertyChanged notifyConfig)
            {
                notifyConfig.PropertyChanged += (sender, e) => Save(typeName, sender);
            }
        }
        private void Save(string typeName, object config)
        {
            var configJson = JObject.FromObject(config);

            if (_configurations.ContainsKey(typeName))
            {
                _configurations[typeName] = configJson;
            }
            else
            {
                _configurations.Add(typeName, configJson);
            }

            var json = JsonConvert.SerializeObject(_configurations, Formatting.Indented);
            FileStream fileStream = null;
            while (true)
            {
                try
                {
                    fileStream = new FileStream(_filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                    break; // Salir del bucle si se obtiene el acceso al archivo.
                }
                catch (IOException)
                {
                    // Esperar un tiempo breve antes de reintentar.
                    Thread.Sleep(100);
                }
            }

            using (var streamWriter = new StreamWriter(fileStream))
            {
                streamWriter.Write(json);
            }
        }

        public void Save<T>(T config)
        {
            Save(typeof(T).Name.Replace("Config", ""), config);
        }
    }
}
 
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;

namespace Edi.Core
{
    [AttributeUsage(AttributeTargets.Class)]
    public class UserConfigAttribute : Attribute
    {
    }

    public class ConfigurationManager
    {
        private string _gameConfigPath; // Cambié _filePath a _fileName porque ahora el path varía
        private string _userConfigPath = Path.Combine(Edi.OutputDir, "UserConfig.json");
        private Dictionary<string, JObject> _configurations;
        private Dictionary<string, object> _configObject = new Dictionary<string, object>();

        public ConfigurationManager(string fileName)
        {
            _gameConfigPath = fileName;
            _configurations = LoadCombinedConfigurations();
        }

        private Dictionary<string, JObject> LoadCombinedConfigurations()
        {
            var combinedConfigs = new Dictionary<string, JObject>();

            // Cargar configuraciones del directorio actual
            if (File.Exists(_gameConfigPath))
            {
                var json = File.ReadAllText(_gameConfigPath);
                var defaultConfigs = JsonConvert.DeserializeObject<Dictionary<string, JObject>>(json);
                foreach (var config in defaultConfigs)
                {
                    combinedConfigs[config.Key] = config.Value;
                }
            }

            // Cargar configuraciones de la carpeta de Windows y sobrescribir si hay colisiones
            if (File.Exists(_userConfigPath))
            {
                var json = File.ReadAllText(_userConfigPath);
                var userConfigs = JsonConvert.DeserializeObject<Dictionary<string, JObject>>(json);
                foreach (var config in userConfigs)
                {
                    combinedConfigs[config.Key] = config.Value; // Sobrescribe si ya existe
                }
            }

            return combinedConfigs.Count > 0 ? combinedConfigs : new Dictionary<string, JObject>();
        }


        public T Get<T>() where T : class, new()
        {
            var typeName = typeof(T).Name.Replace("Config", "");
            if (_configObject.TryGetValue(typeName, out var configObj))
                return (T)configObj;

            T config;

            if (_configurations.TryGetValue(typeName, out var configJson))
            {
                config = configJson.ToObject<T>();
                _configObject.Add(typeName, config);
                SubscribeToChanges(config, typeName);
                return config;
            }

            config = new T();
            Save(config);
            SubscribeToChanges(config, typeName);
            return config;
        }

        public void Save<T>(T config) where T : class
        {
            Save(typeof(T).Name.Replace("Config", ""), config);
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
            string targetPath = Attribute.IsDefined(config.GetType(), typeof(UserConfigAttribute))
                ? _userConfigPath // Ruta para UserConfig
                : _gameConfigPath; // Ruta por defecto

            // Actualizar el diccionario en memoria
            if (_configurations.ContainsKey(typeName))
            {
                _configObject[typeName] = config;
                _configurations[typeName] = configJson;
            }
            else
            {
                _configObject.Add(typeName, config);
                _configurations.Add(typeName, configJson);
            }

            // Cargar las configuraciones existentes del archivo objetivo
            var existingConfigs = new Dictionary<string, JObject>();
            if (File.Exists(targetPath))
            {
                var json = File.ReadAllText(targetPath);
                existingConfigs = JsonConvert.DeserializeObject<Dictionary<string, JObject>>(json) ?? new Dictionary<string, JObject>();
            }

            // Actualizar solo la sección relevante
            existingConfigs[typeName] = configJson;

            // Guardar solo en el archivo correspondiente si hay cambios
            if (existingConfigs.Count > 0)
            {
                var json = JsonConvert.SerializeObject(existingConfigs, Formatting.Indented);
                FileStream fileStream = null;
                int maxRetries = 10; // Maximum number of retries
                int retryCount = 0;

                while (retryCount < maxRetries)
                {
                    try
                    {
                        fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                        break;
                    }
                    catch (IOException)
                    {
                        retryCount++;
                        if (retryCount == maxRetries)
                        {
                            throw new IOException($"Could not access the file {targetPath} after {maxRetries} attempts.");
                        }
                        Thread.Sleep(100);
                    }
                }

                using (var streamWriter = new StreamWriter(fileStream))
                {
                    streamWriter.Write(json);
                }
            }
        }

    }
}
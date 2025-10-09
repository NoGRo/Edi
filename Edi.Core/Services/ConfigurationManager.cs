using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Edi.Core.Services
{
    [AttributeUsage(AttributeTargets.Class)]
    public class UserConfigAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class GameConfigAttribute : Attribute
    {
    }
    public class ConfigurationManager
    {
        private string _gameConfigPath; // Cambié _filePath a _fileName porque ahora el path varía
        private string _userConfigPath = Path.Combine(Edi.OutputDir, "UserConfig.json");
        private Dictionary<string, JObject> _configurations;
        private Dictionary<string, object> _configObject = new Dictionary<string, object>();
        

        public string GamePathConfig => _gameConfigPath;

        public ConfigurationManager(string fileName)
        {
            _gameConfigPath = fileName;
            _configurations = LoadCombinedConfigurations();
        }

        /// <summary>
        /// Cambia la ruta del archivo de configuración principal y recarga las configuraciones.
        /// Actualiza los valores de las instancias ya creadas sin perder sus referencias.
        /// </summary>
        /// <param name="newPath">Nueva ruta del archivo de configuración.</param>
        public void SetGamePath(string newPath)
        {
            
            // Asegura que el archivo de configuración de juego exista y tenga contenido válido
            
            _gameConfigPath = EnsureGameConfigFile(newPath);
            _configurations = LoadCombinedConfigurations();
            // Actualizar instancias existentes con los nuevos valores
            foreach (var kvp in _configObject)
            {
                if (_configurations.TryGetValue(kvp.Key, out var configJson) && !HasUserConfigAttribute(kvp.Key))
                {
                    JsonConvert.PopulateObject(configJson.ToString(), kvp.Value);
                }
            }
        }



        private bool HasUserConfigAttribute(string configName)
        {
            var configType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == configName + "Config" || t.Name == configName);

            return configType != null && Attribute.IsDefined(configType, typeof(UserConfigAttribute));
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
                    if (HasUserConfigAttribute(config.Key))
                    {
                        combinedConfigs[config.Key] = config.Value; // Sobrescribe solo si tiene el atributo
                    }
                }
            }
            else
            {
                // Crear archivo con solo las configuraciones que tienen el atributo UserConfigAttribute
                EnsureUserConfigFile(combinedConfigs);
            }

            return combinedConfigs.Count > 0 ? combinedConfigs : new Dictionary<string, JObject>();
        }

        private void EnsureUserConfigFile(Dictionary<string, JObject> combinedConfigs)
        {
            var userConfigDict = new Dictionary<string, JObject>();
            foreach (var config in combinedConfigs)
            {
                if (HasUserConfigAttribute(config.Key))
                {
                    // Buscar el tipo correspondiente en los ensamblados cargados
                    var configType = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => a.GetTypes())
                        .FirstOrDefault(t => t.Name == config.Key + "Config" || t.Name == config.Key);

                    // Crear una instancia del tipo real y aplicar los valores existentes
                    object instance = Activator.CreateInstance(configType)!;
                    if (config.Value != null)
                    {
                        JsonConvert.PopulateObject(config.Value.ToString(), instance);
                    }
                    // Serializar la instancia real para incluir todas las propiedades (aunque no estén en el JSON original)
                    userConfigDict[config.Key] = JObject.FromObject(instance);
                }
            }
            var userConfigJson = JsonConvert.SerializeObject(userConfigDict, Formatting.Indented);
            Directory.CreateDirectory(Path.GetDirectoryName(_userConfigPath)!);
            File.WriteAllText(_userConfigPath, userConfigJson);
        }
        private string EnsureGameConfigFile(string path)
        {
            string configFilePath;
            if (Directory.Exists(path))
            {
                configFilePath = Path.Combine(path, "EdiConfig.json");
            }
            else if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                configFilePath = fileInfo.Name.Equals("EdiConfig.json", StringComparison.OrdinalIgnoreCase)
                    ? fileInfo.FullName
                    : Path.Combine(fileInfo.DirectoryName!, "EdiConfig.json");
            }
            else
            {
                // Si es un path que no existe, asumimos que es un archivo y usamos su carpeta
                var dir = Path.GetDirectoryName(path);
                configFilePath = Path.Combine(dir ?? ".", "EdiConfig.json");
            }

            bool shouldCreate = !File.Exists(configFilePath) || new FileInfo(configFilePath).Length == 0;
            if (shouldCreate)
            {
                var configTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .Where(t => t.IsClass && t.Name.EndsWith("Config") && t.GetConstructor(Type.EmptyTypes) != null && Attribute.IsDefined(t, typeof(GameConfigAttribute)))
                    .ToList();
                var configDict = new Dictionary<string, JObject>();
                foreach (var type in configTypes)
                {
                    try
                    {
                        var instance = Activator.CreateInstance(type);
                        var key = type.Name.Replace("Config", "");
                        configDict[key] = JObject.FromObject(instance);
                    }
                    catch { }
                }
                var json = JsonConvert.SerializeObject(configDict, Formatting.Indented);
                Directory.CreateDirectory(Path.GetDirectoryName(configFilePath)!);
                File.WriteAllText(configFilePath, json);
            }
            return configFilePath;
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
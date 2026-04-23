using System;
using System.IO;
using System.Text.Json;
using AdminInfoTools.Models;

namespace AdminInfoTools.Services
{
    public class ConfigurationService
    {
        // This holds the global state of our settings, just like $script:AppConfig
        public RootSettings CurrentSettings { get; private set; }

        public bool LoadConfiguration(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            try
            {
                // Configure the deserializer to ignore JSONC comments and map property names flexibly
                var jsonOptions = new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    PropertyNameCaseInsensitive = true
                };

                string jsonString = File.ReadAllText(filePath);
                CurrentSettings = JsonSerializer.Deserialize<RootSettings>(jsonString, jsonOptions);
                
                return CurrentSettings != null;
            }
            catch (Exception ex)
            {
                // In a full WPF app, you would throw this or pass it to a logging service
                Console.WriteLine($"Error parsing JSON: {ex.Message}");
                return false;
            }
        }
    }
}
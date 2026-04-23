using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AdminInfoTools.Models
{
    public class RootSettings
    {
        [JsonPropertyName("AppConfig")]
        public AppConfig AppConfig { get; set; }

        [JsonPropertyName("ActiveDirectory")]
        public ActiveDirectoryConfig ActiveDirectory { get; set; }

        [JsonPropertyName("ExternalTools")]
        public ExternalToolsConfig ExternalTools { get; set; }
    }

    public class AppConfig
    {
        public string LogsDirectory { get; set; }
        public string DefaultComputerList { get; set; }
        public string DefaultUserList { get; set; }
        public string RemoteTempPath { get; set; }
    }

    public class ActiveDirectoryConfig
    {
        public string DomainName { get; set; }
        public string TargetServer { get; set; }
        public Dictionary<string, ComputerTypeConfig> ComputerTypes { get; set; }
        public List<string> DefaultDeploymentGroups { get; set; }
        
        // UPDATE THIS LINE: Make it a dictionary so it dynamically reads your custom JSON names
        public Dictionary<string, string> SpecialOUs { get; set; } 
        
        public string ComputerNamePattern { get; set; }
    }

    public class ComputerTypeConfig
    {
        public string Prefix { get; set; }
        public string TargetOU { get; set; }
        public string DefaultDescription { get; set; }
    }


    public class ExternalToolsConfig
    {
        public string PsExecPath { get; set; }
        public string DamewarePath { get; set; }
        public string BatchScriptsLocation { get; set; }
    }
}
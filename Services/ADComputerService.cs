using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Threading.Tasks;
using AdminInfoTools.Models;

namespace AdminInfoTools.Services
{
    public interface IADComputerService
    {
        bool CreateComputerObject(string hostname, string computerTypeKey);
        Task<bool> CreateComputerObjectAsync(string hostname, string computerTypeKey);
        bool DeleteComputerObject(string hostname);
        Task<bool> DeleteComputerObjectAsync(string hostname);
        bool SetComputerStatus(string hostname, bool isEnabled);
        Task<bool> SetComputerStatusAsync(string hostname, bool isEnabled);
        bool SetComputerDescription(string hostname, string newDescription);
        Task<bool> SetComputerDescriptionAsync(string hostname, string newDescription);
        bool MoveComputerObject(string hostname, string targetOuLdapPath, string newDescription);
        Task<bool> MoveComputerObjectAsync(string hostname, string targetOuLdapPath, string newDescription);
        AdComputerInfoResult GetAdComputerInfo(string targetName);
        Task<AdComputerInfoResult> GetAdComputerInfoAsync(string targetName);
        List<string> GetComputersFromOu(string domainName, string ouPath, string username, string password);
        Task<List<string>> GetComputersFromOuAsync(string domainName, string ouPath, string username, string password);
    }

    public class ADComputerService : IADComputerService
    {
        private readonly IADConnectionProvider _connectionProvider;
        private readonly LogService _logger;

        public ADComputerService(IADConnectionProvider connectionProvider, LogService logger)
        {
            _connectionProvider = connectionProvider;
            _logger = logger;
        }

        public bool CreateComputerObject(string hostname, string computerTypeKey)
        {
            _logger.LogAdOperation("CreateComputer", hostname, "STARTED", $"Type: {computerTypeKey}");
            try
            {
                if (!_connectionProvider.AdConfig.ComputerTypes.TryGetValue(computerTypeKey, out var typeConfig))
                {
                    string error = $"Unknown computer type: {computerTypeKey}";
                    _logger.LogAdOperation("CreateComputer", hostname, "ERROR", error);
                    return false;
                }

                string ldapPath = $"LDAP://{_connectionProvider.AdConfig.TargetServer}/{typeConfig.TargetOU}";
                
                using (DirectoryEntry ouEntry = new DirectoryEntry(ldapPath, _connectionProvider.DomainUser, _connectionProvider.DomainPass))
                {
                    using (DirectoryEntry newComputer = ouEntry.Children.Add($"CN={hostname}", "computer"))
                    {
                        newComputer.Properties["sAMAccountName"].Value = hostname + "$";
                        newComputer.Properties["userAccountControl"].Value = 0x1000; 
                        if (!string.IsNullOrWhiteSpace(typeConfig.DefaultDescription))
                            newComputer.Properties["description"].Value = typeConfig.DefaultDescription;
                        
                        newComputer.CommitChanges();
                    }
                }

                using (var context = _connectionProvider.GetDomainContext())
                {
                    using (var computerPrincipal = ComputerPrincipal.FindByIdentity(context, hostname))
                    {
                        if (computerPrincipal != null)
                        {
                            foreach (var groupName in _connectionProvider.AdConfig.DefaultDeploymentGroups)
                            {
                                using (var group = GroupPrincipal.FindByIdentity(context, groupName))
                                {
                                    if (group != null)
                                    {
                                        group.Members.Add(computerPrincipal);
                                        group.Save();
                                    }
                                }
                            }
                        }
                    }
                }
                _logger.LogAdOperation("CreateComputer", hostname, "SUCCESS");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogAdOperation("CreateComputer", hostname, "ERROR", ex.Message);
                return false;
            }
        }
        public Task<bool> CreateComputerObjectAsync(string hostname, string computerTypeKey) => Task.Run(() => CreateComputerObject(hostname, computerTypeKey));

        public bool DeleteComputerObject(string hostname)
        {
            _logger.LogAdOperation("DeleteComputer", hostname, "STARTED");
            try
            {
                using (var context = _connectionProvider.GetDomainContext())
                using (var computer = ComputerPrincipal.FindByIdentity(context, hostname))
                {
                    if (computer != null)
                    {
                        computer.Delete();
                        _logger.LogAdOperation("DeleteComputer", hostname, "SUCCESS");
                        return true;
                    }
                }
                _logger.LogAdOperation("DeleteComputer", hostname, "FAILED", "Computer not found.");
                return false;
            }
            catch (Exception ex) { _logger.LogAdOperation("DeleteComputer", hostname, "ERROR", ex.Message); return false; }
        }
        public Task<bool> DeleteComputerObjectAsync(string hostname) => Task.Run(() => DeleteComputerObject(hostname));

        public bool SetComputerStatus(string hostname, bool isEnabled)
        {
            string operation = isEnabled ? "EnableComputer" : "DisableComputer";
            _logger.LogComputerModified(operation, hostname, "STARTED");
            try
            {
                using (var context = _connectionProvider.GetDomainContext())
                using (var computer = ComputerPrincipal.FindByIdentity(context, hostname))
                {
                    if (computer != null)
                    {
                        computer.Enabled = isEnabled;
                        computer.Description = $"{(isEnabled ? "Enabled" : "Disabled")} on : {DateTime.Now.ToShortDateString()}";
                        computer.Save();
                        _logger.LogComputerModified(operation, hostname, "SUCCESS");
                        return true;
                    }
                }
                _logger.LogComputerModified(operation, hostname, "FAILED", "Computer not found.");
                return false;
            }
            catch (Exception ex) { _logger.LogComputerModified(operation, hostname, "ERROR", ex.Message); return false; }
        }
        public Task<bool> SetComputerStatusAsync(string hostname, bool isEnabled) => Task.Run(() => SetComputerStatus(hostname, isEnabled));

        public bool SetComputerDescription(string hostname, string newDescription)
        {
            _logger.LogComputerModified("SetCompDesc", hostname, "STARTED", $"NewDesc: {newDescription}");
            try
            {
                using (var context = _connectionProvider.GetDomainContext())
                using (var computer = ComputerPrincipal.FindByIdentity(context, hostname))
                {
                    if (computer != null)
                    {
                        computer.Description = newDescription;
                        computer.Save();
                        _logger.LogComputerModified("SetCompDesc", hostname, "SUCCESS");
                        return true;
                    }
                }
                _logger.LogComputerModified("SetCompDesc", hostname, "FAILED", "Computer not found.");
                return false;
            }
            catch (Exception ex) { _logger.LogComputerModified("SetCompDesc", hostname, "ERROR", ex.Message); return false; }
        }
        public Task<bool> SetComputerDescriptionAsync(string hostname, string newDescription) => Task.Run(() => SetComputerDescription(hostname, newDescription));

        public bool MoveComputerObject(string hostname, string targetOuLdapPath, string newDescription)
        {
            _logger.LogComputerModified("MoveComputer", hostname, "STARTED", $"TargetOU: {targetOuLdapPath}");
            try
            {
                using (var context = _connectionProvider.GetDomainContext())
                using (var computer = ComputerPrincipal.FindByIdentity(context, hostname))
                {
                    if (computer != null)
                    {
                        var underlyingEntry = (DirectoryEntry)computer.GetUnderlyingObject();
                        string newParentPath = $"LDAP://{_connectionProvider.AdConfig.TargetServer}/{targetOuLdapPath}";
                        
                        using (var newParent = new DirectoryEntry(newParentPath, _connectionProvider.DomainUser, _connectionProvider.DomainPass))
                        {
                            underlyingEntry.MoveTo(newParent);
                        }
                        computer.Description = newDescription;
                        computer.Save();
                        _logger.LogComputerModified("MoveComputer", hostname, "SUCCESS");
                        return true;
                    }
                }
                _logger.LogComputerModified("MoveComputer", hostname, "FAILED", "Computer not found.");
                return false;
            }
            catch (Exception ex) { _logger.LogComputerModified("MoveComputer", hostname, "ERROR", ex.Message); return false; }
        }
        public Task<bool> MoveComputerObjectAsync(string hostname, string targetOuLdapPath, string newDescription) => Task.Run(() => MoveComputerObject(hostname, targetOuLdapPath, newDescription));

        public AdComputerInfoResult GetAdComputerInfo(string targetName)
        {
            _logger.LogAdOperation("GetCompInfo", targetName, "STARTED");
            var result = new AdComputerInfoResult { InputName = targetName, Status = "Not Found" };
            string searchName = targetName;

            try
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(targetName, @"^(?:[0-9]{1,3}\.){3}[0-9]{1,3}$"))
                {
                    try 
                    {
                        var hostEntry = System.Net.Dns.GetHostEntry(targetName);
                        string hostName = hostEntry.HostName;
                        int dotIndex = hostName.IndexOf('.');
                        searchName = dotIndex > 0 ? hostName.Substring(0, dotIndex) : hostName;
                        result.ResolvedHostname = searchName;
                    } 
                    catch (Exception dnsEx)
                    {
                        result.Status = "DNS Resolution Failed";
                        _logger.LogAdOperation("GetCompInfo", targetName, "ERROR", $"DNS Resolution Failed: {dnsEx.Message}");
                        return result;
                    }
                }
                else 
                {
                    result.ResolvedHostname = targetName;
                }

                using (var context = _connectionProvider.GetDomainContext())
                using (var computer = ComputerPrincipal.FindByIdentity(context, searchName))
                {
                    if (computer != null)
                    {
                        result.Description = computer.Description ?? "N/A";
                        result.IsEnabled = computer.Enabled ?? false;
                        
                        var de = (DirectoryEntry)computer.GetUnderlyingObject();
                        var osProp = de.Properties["operatingSystem"].Value;
                        result.OperatingSystem = osProp != null ? osProp.ToString() : "N/A";
                        
                        var dnProp = de.Properties["distinguishedName"].Value;
                        result.DistinguishedName = dnProp != null ? dnProp.ToString() : "N/A";
                        
                        result.Status = "Found";
                        _logger.LogAdOperation("GetCompInfo", targetName, "SUCCESS", $"Found: {searchName}");
                    }
                    else
                    {
                        _logger.LogAdOperation("GetCompInfo", targetName, "NOT_FOUND", $"SearchName: {searchName}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Status = $"Error: {ex.Message}";
                _logger.LogAdOperation("GetCompInfo", targetName, "ERROR", ex.Message);
            }
            return result;
        }
        public Task<AdComputerInfoResult> GetAdComputerInfoAsync(string targetName) => Task.Run(() => GetAdComputerInfo(targetName));

        public List<string> GetComputersFromOu(string domainName, string ouPath, string username, string password)
        {
            var computerNames = new List<string>();
            if (string.IsNullOrWhiteSpace(ouPath)) { return computerNames; }
            
            try
            {
                bool hasCreds = !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password);

                using (PrincipalContext context = hasCreds 
                    ? new PrincipalContext(ContextType.Domain, domainName, null, ContextOptions.Negotiate, username, password)
                    : new PrincipalContext(ContextType.Domain, domainName, ouPath))
                {
                    ComputerPrincipal qbeComputer = new ComputerPrincipal(context);
                    using (PrincipalSearcher searcher = new PrincipalSearcher(qbeComputer))
                    {
                        if (hasCreds)
                        {
                            ((DirectorySearcher)searcher.GetUnderlyingSearcher()).SearchRoot = 
                                new DirectoryEntry($"LDAP://{domainName}/{ouPath}", username, password);
                        }

                        foreach (var result in searcher.FindAll())
                        {
                            computerNames.Add(result.Name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogAdOperation("QueryOU", ouPath, "ERROR", ex.Message);
                throw;
            }
            return computerNames;
        }
        public Task<List<string>> GetComputersFromOuAsync(string domainName, string ouPath, string username, string password) => 
            Task.Run(() => GetComputersFromOu(domainName, ouPath, username, password));
    }
}
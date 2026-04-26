using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using AdminInfoTools.Models;

namespace AdminInfoTools.Services
{
    public class ActiveDirectoryService
    {
        private readonly ConfigurationService _configService;
        private readonly ActiveDirectoryConfig _adConfig;
        private readonly LogService _logger;

        // --- TEST LAB CREDENTIALS ---
        public string DomainUser { get; set; }
        public string DomainPass { get; set; }

        public ActiveDirectoryService(ConfigurationService configService)
        {
            _configService = configService;
            _adConfig = _configService.CurrentSettings?.ActiveDirectory 
                        ?? throw new InvalidOperationException("Active Directory Configuration is missing.");
            _logger = new LogService();
        }

        private PrincipalContext GetDomainContext()
        {
            return new PrincipalContext(ContextType.Domain, _adConfig.TargetServer, DomainUser, DomainPass);
        }

        public bool CreateComputerObject(string hostname, string computerTypeKey)
        {
            _logger.LogAdOperation("CreateComputer", hostname, "STARTED", $"Type: {computerTypeKey}");
            try
            {
                if (!_adConfig.ComputerTypes.TryGetValue(computerTypeKey, out var typeConfig))
                {
                    string error = $"Unknown computer type: {computerTypeKey}";
                    _logger.LogAdOperation("CreateComputer", hostname, "ERROR", error);
                    return false;
                }

                string ldapPath = $"LDAP://{_adConfig.TargetServer}/{typeConfig.TargetOU}";
                
                using (DirectoryEntry ouEntry = new DirectoryEntry(ldapPath, DomainUser, DomainPass))
                {
                    using (DirectoryEntry newComputer = ouEntry.Children.Add($"CN={hostname}", "computer"))
                    {
                        newComputer.Properties["sAMAccountName"].Value = hostname + "$";
                        newComputer.Properties["userAccountControl"].Value = 0x1000; 
                        if (!string.IsNullOrWhiteSpace(typeConfig.DefaultDescription))
                        {
                            newComputer.Properties["description"].Value = typeConfig.DefaultDescription;
                        }
                        newComputer.CommitChanges();
                    }
                }

                using (var context = GetDomainContext())
                {
                    using (var computerPrincipal = ComputerPrincipal.FindByIdentity(context, hostname))
                    {
                        if (computerPrincipal != null)
                        {
                            foreach (var groupName in _adConfig.DefaultDeploymentGroups)
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

        public bool DeleteComputerObject(string hostname)
        {
            _logger.LogAdOperation("DeleteComputer", hostname, "STARTED");
            try
            {
                using (var context = GetDomainContext())
                {
                    using (var computer = ComputerPrincipal.FindByIdentity(context, hostname))
                    {
                        if (computer != null)
                        {
                            computer.Delete();
                            _logger.LogAdOperation("DeleteComputer", hostname, "SUCCESS");
                            return true;
                        }
                    }
                }
                _logger.LogAdOperation("DeleteComputer", hostname, "FAILED", "Computer not found.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogAdOperation("DeleteComputer", hostname, "ERROR", ex.Message);
                return false;
            }
        }

        public bool SetComputerStatus(string hostname, bool isEnabled)
        {
            string operation = isEnabled ? "EnableComputer" : "DisableComputer";
            _logger.LogAdOperation(operation, hostname, "STARTED");
            try
            {
                using (var context = GetDomainContext())
                {
                    using (var computer = ComputerPrincipal.FindByIdentity(context, hostname))
                    {
                        if (computer != null)
                        {
                            computer.Enabled = isEnabled;
                            computer.Description = $"{(isEnabled ? "Enabled" : "Disabled")} on : {DateTime.Now.ToShortDateString()}";
                            computer.Save();
                            _logger.LogAdOperation(operation, hostname, "SUCCESS");
                            return true;
                        }
                    }
                }
                _logger.LogAdOperation(operation, hostname, "FAILED", "Computer not found.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogAdOperation(operation, hostname, "ERROR", ex.Message);
                return false;
            }
        }

        public bool SetComputerDescription(string hostname, string newDescription)
        {
            _logger.LogAdOperation("SetCompDesc", hostname, "STARTED", $"NewDesc: {newDescription}");
            try
            {
                using (var context = GetDomainContext())
                {
                    using (var computer = ComputerPrincipal.FindByIdentity(context, hostname))
                    {
                        if (computer != null)
                        {
                            computer.Description = newDescription;
                            computer.Save();
                            _logger.LogAdOperation("SetCompDesc", hostname, "SUCCESS");
                            return true;
                        }
                    }
                }
                _logger.LogAdOperation("SetCompDesc", hostname, "FAILED", "Computer not found.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogAdOperation("SetCompDesc", hostname, "ERROR", ex.Message);
                return false;
            }
        }

        public bool MoveComputerObject(string hostname, string targetOuLdapPath, string newDescription)
        {
            _logger.LogAdOperation("MoveComputer", hostname, "STARTED", $"TargetOU: {targetOuLdapPath}");
            try
            {
                using (var context = GetDomainContext())
                {
                    using (var computer = ComputerPrincipal.FindByIdentity(context, hostname))
                    {
                        if (computer != null)
                        {
                            var underlyingEntry = (DirectoryEntry)computer.GetUnderlyingObject();
                            string newParentPath = $"LDAP://{_adConfig.TargetServer}/{targetOuLdapPath}";
                            
                            using (var newParent = new DirectoryEntry(newParentPath, DomainUser, DomainPass))
                            {
                                underlyingEntry.MoveTo(newParent);
                            }

                            computer.Description = newDescription;
                            computer.Save();
                            _logger.LogAdOperation("MoveComputer", hostname, "SUCCESS");
                            return true;
                        }
                    }
                }
                _logger.LogAdOperation("MoveComputer", hostname, "FAILED", "Computer not found.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogAdOperation("MoveComputer", hostname, "ERROR", ex.Message);
                return false;
            }
        }

        // --- THIS WAS THE MISSING METHOD ---
        public AdComputerInfoResult GetAdComputerInfo(string targetName)
        {
            _logger.LogAdOperation("GetCompInfo", targetName, "STARTED");
            var result = new AdComputerInfoResult { InputName = targetName, Status = "Not Found" };
            string searchName = targetName;

            try
            {
                // Check if it's an IP address
                if (System.Text.RegularExpressions.Regex.IsMatch(targetName, @"^(?:[0-9]{1,3}\.){3}[0-9]{1,3}$"))
                {
                    try 
                    {
                        var hostEntry = System.Net.Dns.GetHostEntry(targetName);
                        string hostName = hostEntry.HostName;
                        
                        // Safely extract just the machine name without using arrays
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

                using (var context = GetDomainContext())
                {
                    using (var computer = ComputerPrincipal.FindByIdentity(context, searchName))
                    {
                        if (computer != null)
                        {
                            result.Description = computer.Description ?? "N/A";
                            result.IsEnabled = computer.Enabled ?? false;
                            
                            var de = (DirectoryEntry)computer.GetUnderlyingObject();
                            
                            // Safely extract properties that might be null
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
            }
            catch (Exception ex)
            {
                result.Status = $"Error: {ex.Message}";
                _logger.LogAdOperation("GetCompInfo", targetName, "ERROR", ex.Message);
            }
            return result;
        }

        public AdUserInfoResult GetAdUserInfo(string targetName)
        {
            _logger.LogAdOperation("GetUserInfo", targetName, "STARTED");
            var result = new AdUserInfoResult { InputName = targetName, Status = "Not Found" };

            try
            {
                using (var context = GetDomainContext())
                {
                    using (var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, targetName) 
                                   ?? UserPrincipal.FindByIdentity(context, IdentityType.Name, targetName))
                    {
                        if (user != null)
                        {
                            result.Name = user.Name ?? "N/A";
                            result.SAMAccountName = user.SamAccountName ?? "N/A";
                            result.UPN = user.UserPrincipalName ?? "N/A";
                            result.Phone = user.VoiceTelephoneNumber ?? "N/A";

                            // --- NEW PROPERTIES ---
                            result.IsLockedOut = user.IsAccountLockedOut();
                            result.LastLogon = user.LastLogon?.ToString("yyyy-MM-dd HH:mm") ?? "Never";

                            var de = (DirectoryEntry)user.GetUnderlyingObject();
                            var managerProp = de.Properties["manager"].Value;
                            
                            if (managerProp != null)
                            {
                                string managerDn = managerProp.ToString();
                                var match = System.Text.RegularExpressions.Regex.Match(managerDn, @"CN=([^,]*)");
                                result.Manager = match.Success ? match.Groups[1].Value : managerDn;
                            }
                            else
                            {
                                result.Manager = "N/A";
                            }

                            result.Status = "Found";
                            _logger.LogAdOperation("GetUserInfo", targetName, "SUCCESS");
                        }
                        else
                        {
                            _logger.LogAdOperation("GetUserInfo", targetName, "NOT_FOUND");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Status = $"Error: {ex.Message}";
                _logger.LogAdOperation("GetUserInfo", targetName, "ERROR", ex.Message);
            }
            return result;
        }

        public bool UnlockUserAccount(string targetName)
        {
            _logger.LogAdOperation("UnlockAccount", targetName, "STARTED");
            try
            {
                using (var context = GetDomainContext())
                using (var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, targetName))
                {
                    if (user != null)
                    {
                        user.UnlockAccount();
                        user.Save();
                        _logger.LogAdOperation("UnlockAccount", targetName, "SUCCESS");
                        return true;
                    }
                }
                _logger.LogAdOperation("UnlockAccount", targetName, "FAILED", "User not found.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogAdOperation("UnlockAccount", targetName, "ERROR", ex.Message);
                return false;
            }
        }

        public bool SetUserStatus(string targetName, bool isEnabled)
        {
            string operation = isEnabled ? "EnableUser" : "DisableUser";
            _logger.LogAdOperation(operation, targetName, "STARTED");
            try
            {
                using (var context = GetDomainContext())
                using (var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, targetName))
                {
                    if (user != null)
                    {
                        user.Enabled = isEnabled;
                        user.Save();
                        _logger.LogAdOperation(operation, targetName, "SUCCESS");
                        return true;
                    }
                }
                _logger.LogAdOperation(operation, targetName, "FAILED", "User not found.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogAdOperation(operation, targetName, "ERROR", ex.Message);
                return false;
            }
        }

        public bool ForcePasswordReset(string targetName)
        {
            _logger.LogAdOperation("ForcePassReset", targetName, "STARTED");
            try
            {
                using (var context = GetDomainContext())
                using (var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, targetName))
                {
                    if (user != null)
                    {
                        user.ExpirePasswordNow();
                        user.Save();
                        _logger.LogAdOperation("ForcePassReset", targetName, "SUCCESS");
                        return true;
                    }
                }
                _logger.LogAdOperation("ForcePassReset", targetName, "FAILED", "User not found.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogAdOperation("ForcePassReset", targetName, "ERROR", ex.Message);
                return false;
            }
        }

        public bool SetUserOrganization(string targetName, string department, string title)
        {
            _logger.LogAdOperation("SetUserOrg", targetName, "STARTED", $"Dept: {department}, Title: {title}");
            try
            {
                using (var context = GetDomainContext())
                using (var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, targetName))
                {
                    if (user != null)
                    {
                        var de = (DirectoryEntry)user.GetUnderlyingObject();
                        if (!string.IsNullOrWhiteSpace(department)) de.Properties["department"].Value = department;
                        if (!string.IsNullOrWhiteSpace(title)) de.Properties["title"].Value = title;
                        de.CommitChanges();
                        _logger.LogAdOperation("SetUserOrg", targetName, "SUCCESS");
                        return true;
                    }
                }
                _logger.LogAdOperation("SetUserOrg", targetName, "FAILED", "User not found.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogAdOperation("SetUserOrg", targetName, "ERROR", ex.Message);
                return false;
            }
        }
    }
}
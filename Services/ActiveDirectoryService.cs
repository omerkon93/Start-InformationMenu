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

        // --- TEST LAB CREDENTIALS ---
        public string DomainUser { get; set; }
        public string DomainPass { get; set; }

        public ActiveDirectoryService(ConfigurationService configService)
        {
            _configService = configService;
            _adConfig = _configService.CurrentSettings?.ActiveDirectory 
                        ?? throw new InvalidOperationException("Active Directory Configuration is missing.");
        }

        private PrincipalContext GetDomainContext()
        {
            return new PrincipalContext(ContextType.Domain, _adConfig.TargetServer, DomainUser, DomainPass);
        }

        public bool CreateComputerObject(string hostname, string computerTypeKey)
        {
            try
            {
                if (!_adConfig.ComputerTypes.TryGetValue(computerTypeKey, out var typeConfig))
                {
                    throw new ArgumentException($"Unknown computer type: {computerTypeKey}");
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
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create {hostname}: {ex.Message}");
                return false;
            }
        }

        public bool DeleteComputerObject(string hostname)
        {
            try
            {
                using (var context = GetDomainContext())
                {
                    using (var computer = ComputerPrincipal.FindByIdentity(context, hostname))
                    {
                        if (computer != null)
                        {
                            computer.Delete();
                            return true;
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to delete {hostname}: {ex.Message}");
                return false;
            }
        }

        public bool SetComputerStatus(string hostname, bool isEnabled)
        {
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
                            return true;
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to update status for {hostname}: {ex.Message}");
                return false;
            }
        }

        public bool SetComputerDescription(string hostname, string newDescription)
        {
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
                            return true;
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to set description for {hostname}: {ex.Message}");
                return false;
            }
        }

        public bool MoveComputerObject(string hostname, string targetOuLdapPath, string newDescription)
        {
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
                            return true;
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to move {hostname}: {ex.Message}");
                return false;
            }
        }

        // --- THIS WAS THE MISSING METHOD ---
        public AdComputerInfoResult GetAdComputerInfo(string targetName)
        {
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
                    catch 
                    {
                        result.Status = "DNS Resolution Failed";
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
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Status = $"Error: {ex.Message}";
            }
            return result;
        }

        public AdUserInfoResult GetAdUserInfo(string targetName)
        {
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
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Status = $"Error: {ex.Message}";
            }
            return result;
        }

        public bool UnlockUserAccount(string targetName)
        {
            try
            {
                using (var context = GetDomainContext())
                using (var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, targetName))
                {
                    if (user != null) { user.UnlockAccount(); user.Save(); return true; }
                }
            }
            catch { }
            return false;
        }

        public bool SetUserStatus(string targetName, bool isEnabled)
        {
            try
            {
                using (var context = GetDomainContext())
                using (var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, targetName))
                {
                    if (user != null) { user.Enabled = isEnabled; user.Save(); return true; }
                }
            }
            catch { }
            return false;
        }

        public bool ForcePasswordReset(string targetName)
        {
            try
            {
                using (var context = GetDomainContext())
                using (var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, targetName))
                {
                    if (user != null) { user.ExpirePasswordNow(); user.Save(); return true; }
                }
            }
            catch { }
            return false;
        }

        public bool SetUserOrganization(string targetName, string department, string title)
        {
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
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }
    }
}
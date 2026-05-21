using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Linq;
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
            _logger.LogComputerModified(operation, hostname, "STARTED");
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
                            _logger.LogComputerModified(operation, hostname, "SUCCESS");
                            return true;
                        }
                    }
                }
                _logger.LogComputerModified(operation, hostname, "FAILED", "Computer not found.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogComputerModified(operation, hostname, "ERROR", ex.Message);
                return false;
            }
        }

        public bool SetComputerDescription(string hostname, string newDescription)
        {
            _logger.LogComputerModified("SetCompDesc", hostname, "STARTED", $"NewDesc: {newDescription}");
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
                            _logger.LogComputerModified("SetCompDesc", hostname, "SUCCESS");
                            return true;
                        }
                    }
                }
                _logger.LogComputerModified("SetCompDesc", hostname, "FAILED", "Computer not found.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogComputerModified("SetCompDesc", hostname, "ERROR", ex.Message);
                return false;
            }
        }

        public bool MoveComputerObject(string hostname, string targetOuLdapPath, string newDescription)
        {
            _logger.LogComputerModified("MoveComputer", hostname, "STARTED", $"TargetOU: {targetOuLdapPath}");
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
                            _logger.LogComputerModified("MoveComputer", hostname, "SUCCESS");
                            return true;
                        }
                    }
                }
                _logger.LogComputerModified("MoveComputer", hostname, "FAILED", "Computer not found.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogComputerModified("MoveComputer", hostname, "ERROR", ex.Message);
                return false;
            }
        }

        public string ResolveSidToName(string sidString)
        {
            try
            {
                using (var context = GetDomainContext()) 
                {
                    using (var principal = Principal.FindByIdentity(context, IdentityType.Sid, sidString))
                    {
                        if (principal != null)
                        {
                            // Attempt to get the NetBIOS domain name, fallback to TargetServer if null
                            string domain = principal.Context.Name ?? _adConfig.TargetServer;
                            return $"{domain}\\{principal.SamAccountName}";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogAdOperation("ResolveSid", sidString, "ERROR", ex.Message);
            }
            
            return sidString; // Fallback to returning the raw SID if it fails completely
        }

        // --- FUZZY IDENTITY MATCHING ---
        public List<string> FindSimilarIdentities(string query)
        {
            var suggestions = new List<string>();
            if (string.IsNullOrWhiteSpace(query)) return suggestions;

            string searchName = query.Contains("\\") ? query.Split('\\').Last() : query;

            try
            {
                using (var context = GetDomainContext())
                {
                    string ldapPath = $"LDAP://{_adConfig.TargetServer}";
                    using (DirectoryEntry rootEntry = new DirectoryEntry(ldapPath, DomainUser, DomainPass))
                    using (DirectorySearcher searcher = new DirectorySearcher(rootEntry))
                    {
                        // Build a fuzzy wildcard pattern based on the first few alphanumeric characters
                        string cleanName = new string(searchName.Where(char.IsLetterOrDigit).ToArray());
                        
                        // Take up to 4 chars to prevent overly broad/slow LDAP queries without interspersing wildcards
                        string prefix = new string(cleanName.Take(4).ToArray());
                        string wildcardPattern = string.IsNullOrEmpty(prefix) ? $"*{searchName}*" : $"*{prefix}*";

                        // Filter matches users and groups using ANR, direct wildcard, and the fuzzy wildcard pattern
                        searcher.Filter = $"(&(|(objectClass=user)(objectClass=group))(|(anr={searchName}*)(samAccountName=*{searchName}*)(samAccountName={wildcardPattern})(name={wildcardPattern})))";
                        searcher.PropertiesToLoad.Add("samAccountName");
                        searcher.PropertiesToLoad.Add("name");
                        searcher.SizeLimit = 100; // Fetch up to 100 to find the best in-memory Levenshtein matches

                        var allResults = new List<(string SamAccountName, string Name, string Formatted, int Distance)>();

                        using (SearchResultCollection results = searcher.FindAll())
                        {
                            foreach (SearchResult result in results)
                            {
                                string samAccountName = result.Properties["samAccountName"].Count > 0 ? result.Properties["samAccountName"][0].ToString() : string.Empty;
                                string name = result.Properties["name"].Count > 0 ? result.Properties["name"][0].ToString() : string.Empty;

                                if (!string.IsNullOrEmpty(samAccountName))
                                {
                                    string suggestion = $"{_adConfig.TargetServer}\\{samAccountName}";
                                    
                                    // Clean strings for a more accurate Levenshtein comparison (ignoring dashes/spaces)
                                    string cleanSam = new string(samAccountName.Where(char.IsLetterOrDigit).ToArray()).ToLower();
                                    string cleanSearch = cleanName.ToLower();

                                    int distance = ComputeLevenshteinDistance(cleanSearch, cleanSam);
                                    
                                    // Prevent duplicate accounts
                                    if (!allResults.Any(x => x.SamAccountName.Equals(samAccountName, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        allResults.Add((samAccountName, name, suggestion, distance));
                                    }
                                }
                            }
                        }

                        // Order by the lowest Levenshtein distance and return the top 5
                        var closestMatches = allResults
                            .OrderBy(x => x.Distance)
                            .Take(5)
                            .ToList();
                        
                        foreach (var match in closestMatches)
                        {
                            string displayName = string.IsNullOrWhiteSpace(match.Name) ? "" : $" ({match.Name})";
                            suggestions.Add($"{match.Formatted}{displayName}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogAdOperation("FuzzySearch", query, "ERROR", ex.Message);
            }

            return suggestions;
        }

        private int ComputeLevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
            if (string.IsNullOrEmpty(t)) return s.Length;

            int[] v0 = new int[t.Length + 1];
            int[] v1 = new int[t.Length + 1];

            for (int i = 0; i < v0.Length; i++) v0[i] = i;

            for (int i = 0; i < s.Length; i++)
            {
                v1[0] = i + 1;
                for (int j = 0; j < t.Length; j++)
                {
                    int cost = (char.ToLower(s[i]) == char.ToLower(t[j])) ? 0 : 1;
                    v1[j + 1] = Math.Min(Math.Min(v1[j] + 1, v0[j + 1] + 1), v0[j] + cost);
                }
                for (int j = 0; j < v0.Length; j++) v0[j] = v1[j];
            }
            return v1[t.Length];
        }

        public bool IsValidAdIdentity(string identityName)
        {
            // 1. Strip domain prefix if present (e.g., "DOMAIN\Username" -> "Username")
            string searchName = identityName.Contains("\\") ? identityName.Split('\\').Last() : identityName;

            try
            {
                using (var context = GetDomainContext())
                {
                    // Check if it's a valid Domain User
                    using (var user = System.DirectoryServices.AccountManagement.UserPrincipal.FindByIdentity(context, searchName))
                    {
                        if (user != null) return true;
                    }
                    
                    // Check if it's a valid Domain Group
                    using (var group = System.DirectoryServices.AccountManagement.GroupPrincipal.FindByIdentity(context, searchName))
                    {
                        if (group != null) return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogAdOperation("CheckIdentity", identityName, "ERROR", ex.Message);
            }
            
            // 2. Fallback: Check if it's a valid local or built-in account (e.g., SYSTEM, Administrators)
            try
            {
                var ntAccount = new System.Security.Principal.NTAccount(identityName);
                ntAccount.Translate(typeof(System.Security.Principal.SecurityIdentifier));
                return true;
            }
            catch (System.Security.Principal.IdentityNotMappedException)
            {
                return false;
            }
        }

        public System.Security.Principal.SecurityIdentifier GetSidFromIdentity(string identityName)
        {
            // 1. Strip domain prefix if present (e.g., "DOMAIN\Username" -> "Username")
            string searchName = identityName.Contains("\\") ? identityName.Split('\\').Last() : identityName;

            // 2. Try resolving via our explicit Active Directory connection
            try
            {
                using (var context = GetDomainContext())
                {
                    using (var user = System.DirectoryServices.AccountManagement.UserPrincipal.FindByIdentity(context, searchName))
                    {
                        if (user != null) return user.Sid;
                    }
                    using (var group = System.DirectoryServices.AccountManagement.GroupPrincipal.FindByIdentity(context, searchName))
                    {
                        if (group != null) return group.Sid;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogAdOperation("GetSid", identityName, "ERROR", ex.Message);
            }

            // 3. Fallback: Try resolving local/built-in accounts (e.g., "SYSTEM", "Administrators")
            try
            {
                var ntAccount = new System.Security.Principal.NTAccount(identityName);
                return (System.Security.Principal.SecurityIdentifier)ntAccount.Translate(typeof(System.Security.Principal.SecurityIdentifier));
            }
            catch
            {
                return null;
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
            _logger.LogUserModified("UnlockAccount", targetName, "STARTED");
            try
            {
                using (var context = GetDomainContext())
                using (var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, targetName))
                {
                    if (user != null)
                    {
                        user.UnlockAccount();
                        user.Save();
                        _logger.LogUserModified("UnlockAccount", targetName, "SUCCESS");
                        return true;
                    }
                }
                _logger.LogUserModified("UnlockAccount", targetName, "FAILED", "User not found.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogUserModified("UnlockAccount", targetName, "ERROR", ex.Message);
                return false;
            }
        }

        public bool SetUserStatus(string targetName, bool isEnabled)
        {
            string operation = isEnabled ? "EnableUser" : "DisableUser";
            _logger.LogUserModified(operation, targetName, "STARTED");
            try
            {
                using (var context = GetDomainContext())
                using (var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, targetName))
                {
                    if (user != null)
                    {
                        user.Enabled = isEnabled;
                        user.Save();
                        _logger.LogUserModified(operation, targetName, "SUCCESS");
                        return true;
                    }
                }
                _logger.LogUserModified(operation, targetName, "FAILED", "User not found.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogUserModified(operation, targetName, "ERROR", ex.Message);
                return false;
            }
        }

        public bool ForcePasswordReset(string targetName)
        {
            _logger.LogUserModified("ForcePassReset", targetName, "STARTED");
            try
            {
                using (var context = GetDomainContext())
                using (var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, targetName))
                {
                    if (user != null)
                    {
                        user.ExpirePasswordNow();
                        user.Save();
                        _logger.LogUserModified("ForcePassReset", targetName, "SUCCESS");
                        return true;
                    }
                }
                _logger.LogUserModified("ForcePassReset", targetName, "FAILED", "User not found.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogUserModified("ForcePassReset", targetName, "ERROR", ex.Message);
                return false;
            }
        }

        public bool SetUserOrganization(string targetName, string department, string title)
        {
            _logger.LogUserModified("SetUserOrg", targetName, "STARTED", $"Dept: {department}, Title: {title}");
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
                        _logger.LogUserModified("SetUserOrg", targetName, "SUCCESS");
                        return true;
                    }
                }
                _logger.LogUserModified("SetUserOrg", targetName, "FAILED", "User not found.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogUserModified("SetUserOrg", targetName, "ERROR", ex.Message);
                return false;
            }
        }

        public List<string> GetComputersFromOu(string domainName, string ouPath, string username, string password)
        {
            var computerNames = new List<string>();

            if (string.IsNullOrWhiteSpace(ouPath))
            {
                _logger.LogAdOperation("QueryOU", "EMPTY_OU_PATH", "FAILED", "OU path was empty or null.");
                return computerNames;
            }

            try
            {
                // 1. Check if we have explicit credentials (running from off-domain)
                bool hasCreds = !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password);

                // 2. Create the context using those credentials if they exist
                using (PrincipalContext context = hasCreds 
                    ? new PrincipalContext(ContextType.Domain, domainName, null, ContextOptions.Negotiate, username, password)
                    : new PrincipalContext(ContextType.Domain, domainName, ouPath)) // Fallback for Domain PCs
                {
                    // Note: If using credentials, we have to bind the OU differently in the searcher
                    ComputerPrincipal qbeComputer = new ComputerPrincipal(context);
                    using (PrincipalSearcher searcher = new PrincipalSearcher(qbeComputer))
                    {
                        // If we passed credentials, the PrincipalContext is bound to the root domain.
                        // We must restrict the search to the specific OU container manually.
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

        // --- OU MANAGEMENT METHODS ---
        public ObservableCollection<OuNode> GetOuHierarchy()
        {
            _logger.LogOuOperation("GetOuHierarchy", "Domain", "STARTED");
            var rootNodes = new ObservableCollection<OuNode>();
            
            try
            {
                string rootLdap = $"LDAP://{_adConfig.TargetServer}";
                using (DirectoryEntry rootEntry = new DirectoryEntry(rootLdap, DomainUser, DomainPass))
                using (DirectorySearcher searcher = new DirectorySearcher(rootEntry))
                {
                    searcher.Filter = "(objectCategory=organizationalUnit)";
                    searcher.PropertiesToLoad.Add("name");
                    searcher.PropertiesToLoad.Add("distinguishedName");
                    searcher.SearchScope = SearchScope.Subtree;
                    searcher.PageSize = 1000;

                    var allOus = new List<OuNode>();
                    using (SearchResultCollection results = searcher.FindAll())
                    {
                        foreach (SearchResult result in results)
                        {
                            string dn = result.Properties["distinguishedName"].Count > 0 ? result.Properties["distinguishedName"][0].ToString() : string.Empty;
                            string name = result.Properties["name"].Count > 0 ? result.Properties["name"][0].ToString() : string.Empty;
                            if (!string.IsNullOrEmpty(dn))
                            {
                                allOus.Add(new OuNode { Name = name, DistinguishedName = dn });
                            }
                        }
                    }

                    // Build the tree by tracking depth using the number of commas in the DistinguishedName
                    var sortedOus = allOus.OrderBy(ou => ou.DistinguishedName.Count(c => c == ',')).ToList();
                    var ouDict = sortedOus.ToDictionary(ou => ou.DistinguishedName, StringComparer.OrdinalIgnoreCase);

                    foreach (var ou in sortedOus)
                    {
                        int firstComma = ou.DistinguishedName.IndexOf(',');
                        if (firstComma > 0 && firstComma < ou.DistinguishedName.Length - 1)
                        {
                            string parentDn = ou.DistinguishedName.Substring(firstComma + 1);
                            if (ouDict.TryGetValue(parentDn, out OuNode parentNode))
                            {
                                parentNode.Children.Add(ou);
                            }
                            else
                            {
                                // If the parent isn't an OU, this acts as a top-level OU
                                rootNodes.Add(ou);
                            }
                        }
                        else
                        {
                            rootNodes.Add(ou);
                        }
                    }
                }
                _logger.LogOuOperation("GetOuHierarchy", "Domain", "SUCCESS");
            }
            catch (Exception ex)
            {
                _logger.LogOuOperation("GetOuHierarchy", "Domain", "ERROR", ex.Message);
            }
            
            return rootNodes;
        }

        public bool CreateOu(string parentDn, string newOuName)
        {
            _logger.LogOuOperation("CreateOu", newOuName, "STARTED", $"Parent: {parentDn}");
            try
            {
                string ldapPath = $"LDAP://{_adConfig.TargetServer}/{parentDn}";
                using (DirectoryEntry parentEntry = new DirectoryEntry(ldapPath, DomainUser, DomainPass))
                using (DirectoryEntry newOu = parentEntry.Children.Add($"OU={newOuName}", "organizationalUnit"))
                {
                    newOu.CommitChanges();
                }
                _logger.LogOuOperation("CreateOu", newOuName, "SUCCESS");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogOuOperation("CreateOu", newOuName, "ERROR", ex.Message);
                return false;
            }
        }

        public bool RenameOu(string targetDn, string newName)
        {
            _logger.LogOuOperation("RenameOu", targetDn, "STARTED", $"NewName: {newName}");
            try
            {
                string ldapPath = $"LDAP://{_adConfig.TargetServer}/{targetDn}";
                using (DirectoryEntry targetEntry = new DirectoryEntry(ldapPath, DomainUser, DomainPass))
                {
                    targetEntry.Rename($"OU={newName}");
                    targetEntry.CommitChanges();
                }
                _logger.LogOuOperation("RenameOu", targetDn, "SUCCESS");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogOuOperation("RenameOu", targetDn, "ERROR", ex.Message);
                return false;
            }
        }

        public bool DeleteOu(string targetDn)
        {
            _logger.LogOuOperation("DeleteOu", targetDn, "STARTED");
            try
            {
                string ldapPath = $"LDAP://{_adConfig.TargetServer}/{targetDn}";
                using (DirectoryEntry targetEntry = new DirectoryEntry(ldapPath, DomainUser, DomainPass))
                {
                    targetEntry.DeleteTree();
                    targetEntry.CommitChanges();
                }
                _logger.LogOuOperation("DeleteOu", targetDn, "SUCCESS");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogOuOperation("DeleteOu", targetDn, "ERROR", ex.Message);
                return false;
            }
        }

        public bool MoveOu(string sourceOuDn, string destinationParentDn)
        {
            _logger.LogOuOperation("MoveOu", sourceOuDn, "STARTED", $"Destination: {destinationParentDn}");
            try
            {
                string sourceLdapPath = $"LDAP://{_adConfig.TargetServer}/{sourceOuDn}";
                string destLdapPath = $"LDAP://{_adConfig.TargetServer}/{destinationParentDn}";

                using (DirectoryEntry sourceEntry = new DirectoryEntry(sourceLdapPath, DomainUser, DomainPass))
                using (DirectoryEntry destParentEntry = new DirectoryEntry(destLdapPath, DomainUser, DomainPass))
                {
                    // The name of the object being moved is preserved automatically.
                    sourceEntry.MoveTo(destParentEntry);
                }
                _logger.LogOuOperation("MoveOu", sourceOuDn, "SUCCESS");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogOuOperation("MoveOu", sourceOuDn, "ERROR", ex.Message);
                return false;
            }
        }
    }
}
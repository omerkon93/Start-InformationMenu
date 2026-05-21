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
    public class ActiveDirectoryService : IADConnectionProvider
    {
        private readonly ConfigurationService _configService;
        private readonly ActiveDirectoryConfig _adConfig;
        private readonly LogService _logger;

        private readonly IADComputerService _computerService;
        private readonly IADUserService _userService;
        private readonly IADOuService _ouService;

        // --- TEST LAB CREDENTIALS ---
        public string DomainUser { get; set; }
        public string DomainPass { get; set; }

        public ActiveDirectoryConfig AdConfig => _adConfig;

        public ActiveDirectoryService(ConfigurationService configService)
        {
            _configService = configService;
            _adConfig = _configService.CurrentSettings?.ActiveDirectory 
                        ?? throw new InvalidOperationException("Active Directory Configuration is missing.");
            _logger = new LogService();

            // Initialize the sub-services required for the facade
            _computerService = new ADComputerService(this, _logger);
            _userService = new ADUserService(this, _logger);
            _ouService = new ADOuService(this, _logger);
        }

        public PrincipalContext GetDomainContext()
        {
            return new PrincipalContext(ContextType.Domain, _adConfig.TargetServer, DomainUser, DomainPass);
        }

        // --- Computer Facade ---
        public bool CreateComputerObject(string hostname, string computerTypeKey) => _computerService.CreateComputerObject(hostname, computerTypeKey);
        public bool DeleteComputerObject(string hostname) => _computerService.DeleteComputerObject(hostname);
        public bool SetComputerStatus(string hostname, bool isEnabled) => _computerService.SetComputerStatus(hostname, isEnabled);
        public bool SetComputerDescription(string hostname, string newDescription) => _computerService.SetComputerDescription(hostname, newDescription);
        public bool MoveComputerObject(string hostname, string targetOuLdapPath, string newDescription) => _computerService.MoveComputerObject(hostname, targetOuLdapPath, newDescription);

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

        // --- User, Info & OU Facade Methods ---
        public AdComputerInfoResult GetAdComputerInfo(string targetName) => _computerService.GetAdComputerInfo(targetName);
        public List<string> GetComputersFromOu(string domainName, string ouPath, string username, string password) => _computerService.GetComputersFromOu(domainName, ouPath, username, password);
        
        public AdUserInfoResult GetAdUserInfo(string targetName) => _userService.GetAdUserInfo(targetName);
        public bool UnlockUserAccount(string targetName) => _userService.UnlockUserAccount(targetName);
        public bool SetUserStatus(string targetName, bool isEnabled) => _userService.SetUserStatus(targetName, isEnabled);
        public bool ForcePasswordReset(string targetName) => _userService.ForcePasswordReset(targetName);
        public bool SetUserOrganization(string targetName, string department, string title) => _userService.SetUserOrganization(targetName, department, title);

        public ObservableCollection<OuNode> GetOuHierarchy() => _ouService.GetOuHierarchy();
        public bool CreateOu(string parentDn, string newOuName) => _ouService.CreateOu(parentDn, newOuName);
        public bool RenameOu(string targetDn, string newName) => _ouService.RenameOu(targetDn, newName);
        public bool DeleteOu(string targetDn) => _ouService.DeleteOu(targetDn);
        public bool MoveOu(string sourceOuDn, string destinationParentDn) => _ouService.MoveOu(sourceOuDn, destinationParentDn);
    }
}
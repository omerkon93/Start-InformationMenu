using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.DirectoryServices;
using System.Linq;
using System.Threading.Tasks;
using AdminInfoTools.Models;

namespace AdminInfoTools.Services
{
    public interface IADOuService
    {
        ObservableCollection<OuNode> GetOuHierarchy();
        Task<ObservableCollection<OuNode>> GetOuHierarchyAsync();
        bool CreateOu(string parentDn, string newOuName);
        Task<bool> CreateOuAsync(string parentDn, string newOuName);
        bool RenameOu(string targetDn, string newName);
        Task<bool> RenameOuAsync(string targetDn, string newName);
        bool DeleteOu(string targetDn);
        Task<bool> DeleteOuAsync(string targetDn);
        bool MoveOu(string sourceOuDn, string destinationParentDn);
        Task<bool> MoveOuAsync(string sourceOuDn, string destinationParentDn);
    }

    public class ADOuService : IADOuService
    {
        private readonly IADConnectionProvider _connectionProvider;
        private readonly LogService _logger;

        public ADOuService(IADConnectionProvider connectionProvider, LogService logger)
        {
            _connectionProvider = connectionProvider;
            _logger = logger;
        }

        public ObservableCollection<OuNode> GetOuHierarchy()
        {
            _logger.LogOuOperation("GetOuHierarchy", "Domain", "STARTED");
            var rootNodes = new ObservableCollection<OuNode>();
            
            try
            {
                string rootLdap = $"LDAP://{_connectionProvider.AdConfig.TargetServer}";
                using (DirectoryEntry rootEntry = new DirectoryEntry(rootLdap, _connectionProvider.DomainUser, _connectionProvider.DomainPass))
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
                            if (!string.IsNullOrEmpty(dn)) allOus.Add(new OuNode { Name = name, DistinguishedName = dn });
                        }
                    }

                    var sortedOus = allOus.OrderBy(ou => ou.DistinguishedName.Count(c => c == ',')).ToList();
                    var ouDict = sortedOus.ToDictionary(ou => ou.DistinguishedName, StringComparer.OrdinalIgnoreCase);

                    foreach (var ou in sortedOus)
                    {
                        int firstComma = ou.DistinguishedName.IndexOf(',');
                        if (firstComma > 0 && firstComma < ou.DistinguishedName.Length - 1)
                        {
                            string parentDn = ou.DistinguishedName.Substring(firstComma + 1);
                            if (ouDict.TryGetValue(parentDn, out OuNode parentNode)) parentNode.Children.Add(ou);
                            else rootNodes.Add(ou);
                        }
                        else rootNodes.Add(ou);
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
        public Task<ObservableCollection<OuNode>> GetOuHierarchyAsync() => Task.Run(() => GetOuHierarchy());

        public bool CreateOu(string parentDn, string newOuName)
        {
            _logger.LogOuOperation("CreateOu", newOuName, "STARTED", $"Parent: {parentDn}");
            try
            {
                string ldapPath = $"LDAP://{_connectionProvider.AdConfig.TargetServer}/{parentDn}";
                using (DirectoryEntry parentEntry = new DirectoryEntry(ldapPath, _connectionProvider.DomainUser, _connectionProvider.DomainPass))
                using (DirectoryEntry newOu = parentEntry.Children.Add($"OU={newOuName}", "organizationalUnit"))
                {
                    newOu.CommitChanges();
                }
                _logger.LogOuOperation("CreateOu", newOuName, "SUCCESS");
                return true;
            }
            catch (Exception ex) { _logger.LogOuOperation("CreateOu", newOuName, "ERROR", ex.Message); return false; }
        }
        public Task<bool> CreateOuAsync(string parentDn, string newOuName) => Task.Run(() => CreateOu(parentDn, newOuName));

        public bool RenameOu(string targetDn, string newName)
        {
            _logger.LogOuOperation("RenameOu", targetDn, "STARTED", $"NewName: {newName}");
            try
            {
                string ldapPath = $"LDAP://{_connectionProvider.AdConfig.TargetServer}/{targetDn}";
                using (DirectoryEntry targetEntry = new DirectoryEntry(ldapPath, _connectionProvider.DomainUser, _connectionProvider.DomainPass))
                {
                    targetEntry.Rename($"OU={newName}");
                    targetEntry.CommitChanges();
                }
                _logger.LogOuOperation("RenameOu", targetDn, "SUCCESS");
                return true;
            }
            catch (Exception ex) { _logger.LogOuOperation("RenameOu", targetDn, "ERROR", ex.Message); return false; }
        }
        public Task<bool> RenameOuAsync(string targetDn, string newName) => Task.Run(() => RenameOu(targetDn, newName));

        public bool DeleteOu(string targetDn)
        {
            _logger.LogOuOperation("DeleteOu", targetDn, "STARTED");
            try
            {
                string ldapPath = $"LDAP://{_connectionProvider.AdConfig.TargetServer}/{targetDn}";
                using (DirectoryEntry targetEntry = new DirectoryEntry(ldapPath, _connectionProvider.DomainUser, _connectionProvider.DomainPass))
                {
                    targetEntry.DeleteTree();
                    targetEntry.CommitChanges();
                }
                _logger.LogOuOperation("DeleteOu", targetDn, "SUCCESS");
                return true;
            }
            catch (Exception ex) { _logger.LogOuOperation("DeleteOu", targetDn, "ERROR", ex.Message); return false; }
        }
        public Task<bool> DeleteOuAsync(string targetDn) => Task.Run(() => DeleteOu(targetDn));

        public bool MoveOu(string sourceOuDn, string destinationParentDn)
        {
            _logger.LogOuOperation("MoveOu", sourceOuDn, "STARTED", $"Destination: {destinationParentDn}");
            try
            {
                string sourceLdapPath = $"LDAP://{_connectionProvider.AdConfig.TargetServer}/{sourceOuDn}";
                string destLdapPath = $"LDAP://{_connectionProvider.AdConfig.TargetServer}/{destinationParentDn}";

                using (DirectoryEntry sourceEntry = new DirectoryEntry(sourceLdapPath, _connectionProvider.DomainUser, _connectionProvider.DomainPass))
                using (DirectoryEntry destParentEntry = new DirectoryEntry(destLdapPath, _connectionProvider.DomainUser, _connectionProvider.DomainPass))
                {
                    sourceEntry.MoveTo(destParentEntry);
                }
                _logger.LogOuOperation("MoveOu", sourceOuDn, "SUCCESS");
                return true;
            }
            catch (Exception ex) { _logger.LogOuOperation("MoveOu", sourceOuDn, "ERROR", ex.Message); return false; }
        }
        public Task<bool> MoveOuAsync(string sourceOuDn, string destinationParentDn) => Task.Run(() => MoveOu(sourceOuDn, destinationParentDn));
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using AdminInfoTools.Models;
using System.Threading;
using System.Threading.Tasks;

namespace AdminInfoTools.Services
{
    public class NtfsManagementService
    {
        private readonly LogService _logger;
        private readonly ActiveDirectoryService _adService;
        private readonly TemporaryPermissionTracker _tempPermissionTracker;

        public NtfsManagementService(LogService logger, ActiveDirectoryService adService = null)
        {
            _logger = logger;
            _adService = adService;
            
            // Initialize the tracking mechanism for time-restricted permissions
            _tempPermissionTracker = new TemporaryPermissionTracker(this);
        }

        public List<FilePermissionRule> GetPermissions(string targetPath)
        {
            var rulesList = new List<FilePermissionRule>();
            _logger.LogAdOperation("NtfsGetPerms", targetPath, "STARTED");

            try
            {
                FileSystemSecurity security;
                if (File.Exists(targetPath))
                    security = new FileInfo(targetPath).GetAccessControl();
                else
                    security = new DirectoryInfo(targetPath).GetAccessControl();

                AuthorizationRuleCollection rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));

                foreach (FileSystemAccessRule rule in rules)
                {
                    string readableIdentity = rule.IdentityReference.Value;
                    try
                    {
                        var ntAccount = rule.IdentityReference.Translate(typeof(NTAccount));
                        readableIdentity = ntAccount.Value;
                    }
                    catch (IdentityNotMappedException)
                    {
                        if (_adService != null)
                        {
                            readableIdentity = _adService.ResolveSidToName(rule.IdentityReference.Value);
                        }
                        else
                        {
                            readableIdentity = rule.IdentityReference.Value;
                        }
                    }

                    rulesList.Add(new FilePermissionRule
                    {
                        Identity = readableIdentity,
                        Rights = rule.FileSystemRights.ToString(),
                        AccessType = rule.AccessControlType.ToString(),
                        IsInherited = rule.IsInherited
                    });
                }
                
                _logger.LogAdOperation("NtfsGetPerms", targetPath, "SUCCESS");
            }
            catch (Exception ex)
            {
                _logger.LogAdOperation("NtfsGetPerms", targetPath, "ERROR", ex.Message);
                throw;
            }

            return rulesList;
        }

        private SecurityIdentifier ResolveIdentity(string identity)
        {
            SecurityIdentifier sid = null;
            if (_adService != null)
            {
                sid = _adService.GetSidFromIdentity(identity);
                if (sid == null)
                {
                    var suggestions = _adService.FindSimilarIdentities(identity);
                    if (suggestions != null && suggestions.Count > 0)
                    {
                        throw new IdentityAmbiguousException(identity, suggestions);
                    }
                }
            }
            else
            {
                try { sid = (SecurityIdentifier)new NTAccount(identity).Translate(typeof(SecurityIdentifier)); } catch { }
            }

            if (sid == null)
            {
                throw new Exception($"Could not resolve identity '{identity}' to a valid SID.");
            }
            return sid;
        }

        public void AddPermission(string targetPath, string identity, FileSystemRights rights, AccessControlType accessType)
        {
            _logger.LogAdOperation("NtfsAddPerm", targetPath, "STARTED", $"{identity} - {rights}");
            
            SecurityIdentifier sid = ResolveIdentity(identity);

            if (File.Exists(targetPath))
            {
                FileInfo fInfo = new FileInfo(targetPath);
                FileSecurity security = fInfo.GetAccessControl();
                // Files do not support inheritance flags
                FileSystemAccessRule accessRule = new FileSystemAccessRule(sid, rights, InheritanceFlags.None, PropagationFlags.None, accessType);
                security.AddAccessRule(accessRule);
                fInfo.SetAccessControl(security);
            }
            else
            {
                DirectoryInfo dInfo = new DirectoryInfo(targetPath);
                DirectorySecurity security = dInfo.GetAccessControl();
                FileSystemAccessRule accessRule = new FileSystemAccessRule(sid, rights, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, accessType);
                security.AddAccessRule(accessRule);
                dInfo.SetAccessControl(security);
            }
            _logger.LogAdOperation("NtfsAddPerm", targetPath, "SUCCESS");
        }

        public void RemovePermission(string targetPath, string identity, FileSystemRights rights, AccessControlType accessType)
        {
            _logger.LogAdOperation("NtfsRemovePerm", targetPath, "STARTED", $"{identity} - {rights}");
            
            SecurityIdentifier sid = ResolveIdentity(identity);

            if (File.Exists(targetPath))
            {
                FileInfo fInfo = new FileInfo(targetPath);
                FileSecurity security = fInfo.GetAccessControl();
                FileSystemAccessRule accessRule = new FileSystemAccessRule(sid, rights, InheritanceFlags.None, PropagationFlags.None, accessType);
                security.RemoveAccessRule(accessRule);
                fInfo.SetAccessControl(security);
            }
            else
            {
                DirectoryInfo dInfo = new DirectoryInfo(targetPath);
                DirectorySecurity security = dInfo.GetAccessControl();
                FileSystemAccessRule accessRule = new FileSystemAccessRule(sid, rights, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, accessType);
                security.RemoveAccessRule(accessRule);
                dInfo.SetAccessControl(security);
            }
            _logger.LogAdOperation("NtfsRemovePerm", targetPath, "SUCCESS");
        }

        public void AddPermissionsBatch(string targetPath, List<string> identities, FileSystemRights rights, AccessControlType accessType)
        {
            _logger.LogAdOperation("NtfsAddBatch", targetPath, "STARTED", $"{identities.Count} identities");

            bool isFile = File.Exists(targetPath);
            FileSystemSecurity security = isFile ? (FileSystemSecurity)new FileInfo(targetPath).GetAccessControl() : new DirectoryInfo(targetPath).GetAccessControl();
            bool changesMade = false;

            foreach (var identity in identities)
            {
                try
                {
                    SecurityIdentifier sid = ResolveIdentity(identity);
                    FileSystemAccessRule accessRule;
                    if (isFile)
                        accessRule = new FileSystemAccessRule(sid, rights, InheritanceFlags.None, PropagationFlags.None, accessType);
                    else
                    {
                        var inheritanceFlags = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                        var propagationFlags = PropagationFlags.None;
                        accessRule = new FileSystemAccessRule(sid, rights, inheritanceFlags, propagationFlags, accessType);
                    }

                    security.AddAccessRule(accessRule);
                    changesMade = true;
                }
                catch (IdentityAmbiguousException ex)
                {
                    _logger.LogAdOperation("NtfsAddBatch", targetPath, "WARNING", $"Ambiguous identity '{identity}'. Suggestions: {string.Join(", ", ex.Suggestions)}");
                    throw; // Rethrowing allows the UI to prompt the user
                }
                catch (Exception ex)
                {
                    _logger.LogAdOperation("NtfsAddBatch", targetPath, "WARNING", $"Failed to process identity {identity}: {ex.Message}");
                }
            }

            if (changesMade)
            {
                if (isFile)
                    new FileInfo(targetPath).SetAccessControl((FileSecurity)security);
                else
                    new DirectoryInfo(targetPath).SetAccessControl((DirectorySecurity)security);
            }
            _logger.LogAdOperation("NtfsAddBatch", targetPath, "SUCCESS");
        }

        public void RemovePermissionsBatch(string targetPath, List<string> identities, FileSystemRights rights, AccessControlType accessType)
        {
            _logger.LogAdOperation("NtfsRemoveBatch", targetPath, "STARTED", $"{identities.Count} identities");

            bool isFile = File.Exists(targetPath);
            FileSystemSecurity security = isFile ? (FileSystemSecurity)new FileInfo(targetPath).GetAccessControl() : new DirectoryInfo(targetPath).GetAccessControl();
            bool changesMade = false;

            foreach (var identity in identities)
            {
                try
                {
                    SecurityIdentifier sid = ResolveIdentity(identity);
                    FileSystemAccessRule accessRule;
                    if (isFile)
                        accessRule = new FileSystemAccessRule(sid, rights, InheritanceFlags.None, PropagationFlags.None, accessType);
                    else
                    {
                        var inheritanceFlags = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                        var propagationFlags = PropagationFlags.None;
                        accessRule = new FileSystemAccessRule(sid, rights, inheritanceFlags, propagationFlags, accessType);
                    }

                    security.RemoveAccessRule(accessRule);
                    changesMade = true;
                }
                catch (IdentityAmbiguousException ex)
                {
                    _logger.LogAdOperation("NtfsRemoveBatch", targetPath, "WARNING", $"Ambiguous identity '{identity}'. Suggestions: {string.Join(", ", ex.Suggestions)}");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogAdOperation("NtfsRemoveBatch", targetPath, "WARNING", $"Failed to process identity {identity}: {ex.Message}");
                }
            }

            if (changesMade)
            {
                if (isFile)
                    new FileInfo(targetPath).SetAccessControl((FileSecurity)security);
                else
                    new DirectoryInfo(targetPath).SetAccessControl((DirectorySecurity)security);
            }
            _logger.LogAdOperation("NtfsRemoveBatch", targetPath, "SUCCESS");
        }

        /// <summary>
        /// Grants a user permission to a file or folder for a limited time, after which the permission is automatically revoked.
        /// </summary>
        public void AddTemporaryPermission(string targetPath, string identity, FileSystemRights rights, AccessControlType accessType, DateTime expirationTime)
        {
            // 1. Add the permission using the existing method
            AddPermission(targetPath, identity, rights, accessType);

            // 2. Register the permission for removal
            var task = new TemporaryPermissionTask
            {
                Id = Guid.NewGuid(),
                TargetPath = targetPath,
                Identity = identity,
                Rights = rights,
                AccessType = accessType,
                ExpirationTime = expirationTime
            };

            _tempPermissionTracker.TrackPermission(task);
            _logger.LogAdOperation("NtfsAddTempPerm", targetPath, "SUCCESS", $"Will expire at {expirationTime}");
        }

        public void AddTemporaryPermission(string targetPath, string identity, FileSystemRights rights, AccessControlType accessType, TimeSpan duration)
        {
            AddTemporaryPermission(targetPath, identity, rights, accessType, DateTime.Now.Add(duration));
        }
    }
}
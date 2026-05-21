using System;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Threading.Tasks;
using AdminInfoTools.Models;

namespace AdminInfoTools.Services
{
    public interface IADUserService
    {
        AdUserInfoResult GetAdUserInfo(string targetName);
        Task<AdUserInfoResult> GetAdUserInfoAsync(string targetName);
        bool UnlockUserAccount(string targetName);
        Task<bool> UnlockUserAccountAsync(string targetName);
        bool SetUserStatus(string targetName, bool isEnabled);
        Task<bool> SetUserStatusAsync(string targetName, bool isEnabled);
        bool ForcePasswordReset(string targetName);
        Task<bool> ForcePasswordResetAsync(string targetName);
        bool SetUserOrganization(string targetName, string department, string title);
        Task<bool> SetUserOrganizationAsync(string targetName, string department, string title);
    }

    public class ADUserService : IADUserService
    {
        private readonly IADConnectionProvider _connectionProvider;
        private readonly LogService _logger;

        public ADUserService(IADConnectionProvider connectionProvider, LogService logger)
        {
            _connectionProvider = connectionProvider;
            _logger = logger;
        }

        public AdUserInfoResult GetAdUserInfo(string targetName)
        {
            _logger.LogAdOperation("GetUserInfo", targetName, "STARTED");
            var result = new AdUserInfoResult { InputName = targetName, Status = "Not Found" };
            try
            {
                using (var context = _connectionProvider.GetDomainContext())
                using (var user = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, targetName) ?? UserPrincipal.FindByIdentity(context, IdentityType.Name, targetName))
                {
                    if (user != null)
                    {
                        result.Name = user.Name ?? "N/A";
                        result.SAMAccountName = user.SamAccountName ?? "N/A";
                        result.UPN = user.UserPrincipalName ?? "N/A";
                        result.Phone = user.VoiceTelephoneNumber ?? "N/A";
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
                        else { result.Manager = "N/A"; }

                        result.Status = "Found";
                        _logger.LogAdOperation("GetUserInfo", targetName, "SUCCESS");
                    }
                    else { _logger.LogAdOperation("GetUserInfo", targetName, "NOT_FOUND"); }
                }
            }
            catch (Exception ex) { result.Status = $"Error: {ex.Message}"; _logger.LogAdOperation("GetUserInfo", targetName, "ERROR", ex.Message); }
            return result;
        }
        public Task<AdUserInfoResult> GetAdUserInfoAsync(string targetName) => Task.Run(() => GetAdUserInfo(targetName));

        public bool UnlockUserAccount(string targetName)
        {
            _logger.LogUserModified("UnlockAccount", targetName, "STARTED");
            try
            {
                using (var context = _connectionProvider.GetDomainContext())
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
                return false;
            }
            catch (Exception ex) { _logger.LogUserModified("UnlockAccount", targetName, "ERROR", ex.Message); return false; }
        }
        public Task<bool> UnlockUserAccountAsync(string targetName) => Task.Run(() => UnlockUserAccount(targetName));

        public bool SetUserStatus(string targetName, bool isEnabled)
        {
            string operation = isEnabled ? "EnableUser" : "DisableUser";
            _logger.LogUserModified(operation, targetName, "STARTED");
            try
            {
                using (var context = _connectionProvider.GetDomainContext())
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
            catch (Exception ex) { _logger.LogUserModified(operation, targetName, "ERROR", ex.Message); return false; }
        }
        public Task<bool> SetUserStatusAsync(string targetName, bool isEnabled) => Task.Run(() => SetUserStatus(targetName, isEnabled));

        public bool ForcePasswordReset(string targetName)
        {
            _logger.LogUserModified("ForcePassReset", targetName, "STARTED");
            try
            {
                using (var context = _connectionProvider.GetDomainContext())
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
            catch (Exception ex) { _logger.LogUserModified("ForcePassReset", targetName, "ERROR", ex.Message); return false; }
        }
        public Task<bool> ForcePasswordResetAsync(string targetName) => Task.Run(() => ForcePasswordReset(targetName));

        public bool SetUserOrganization(string targetName, string department, string title)
        {
            _logger.LogUserModified("SetUserOrg", targetName, "STARTED", $"Dept: {department}, Title: {title}");
            try
            {
                using (var context = _connectionProvider.GetDomainContext())
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
            catch (Exception ex) { _logger.LogUserModified("SetUserOrg", targetName, "ERROR", ex.Message); return false; }
        }
        public Task<bool> SetUserOrganizationAsync(string targetName, string department, string title) => Task.Run(() => SetUserOrganization(targetName, department, title));
    }
}
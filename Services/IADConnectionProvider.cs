using System.DirectoryServices.AccountManagement;
using AdminInfoTools.Models;

namespace AdminInfoTools.Services
{
    public interface IADConnectionProvider
    {
        string DomainUser { get; }
        string DomainPass { get; }
        ActiveDirectoryConfig AdConfig { get; }
        PrincipalContext GetDomainContext();
    }
}
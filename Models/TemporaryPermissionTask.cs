using System;
using System.Security.AccessControl;

namespace AdminInfoTools.Models
{
    public class TemporaryPermissionTask
    {
        public Guid Id { get; set; }
        public string TargetPath { get; set; }
        public string Identity { get; set; }
        public FileSystemRights Rights { get; set; }
        public AccessControlType AccessType { get; set; }
        public DateTime ExpirationTime { get; set; }
        public bool IsRevoked { get; set; }
    }
}
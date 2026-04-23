namespace AdminInfoTools.Models
{
    public class AdUserInfoResult
    {
        public string InputName { get; set; }
        public string Name { get; set; }
        public string SAMAccountName { get; set; }
        public string UPN { get; set; }
        public string Phone { get; set; }
        public string Manager { get; set; }
        public bool IsLockedOut { get; set; }
        public string LastLogon { get; set; }
        public string Status { get; set; }
    }
}
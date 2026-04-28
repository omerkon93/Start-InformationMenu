namespace AdminInfoTools.Models
{
    public class FilePermissionRule
    {
        public string Identity { get; set; }
        public string Rights { get; set; }
        public string AccessType { get; set; }
        public bool IsInherited { get; set; }
    }
}
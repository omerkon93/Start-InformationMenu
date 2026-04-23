namespace AdminInfoTools.Models
{
    public class AdComputerInfoResult
    {
        public string InputName { get; set; }
        public string ResolvedHostname { get; set; }
        public string Description { get; set; }
        public string OperatingSystem { get; set; }
        public bool IsEnabled { get; set; }
        public string DistinguishedName { get; set; }
        public string Status { get; set; }
    }
}
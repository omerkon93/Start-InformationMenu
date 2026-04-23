namespace AdminInfoTools.Models
{
    public class ComputerInfoResult
    {
        public string ComputerName { get; set; }
        public string CsModel { get; set; }
        public string CsName { get; set; }
        public string CsProcessors { get; set; }
        public string CsUserName { get; set; }
        public string OsName { get; set; }
        public string OSDisplayVersion { get; set; }
        public string BiosCaption { get; set; }
        public string Status { get; set; }
        public bool IsOnline { get; set; }
    }
}
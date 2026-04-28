using System.Collections.ObjectModel;

namespace AdminInfoTools.Models
{
    public class OuNode
    {
        public string Name { get; set; }
        public string DistinguishedName { get; set; }
        
        public ObservableCollection<OuNode> Children { get; set; } = new ObservableCollection<OuNode>();
    }
}
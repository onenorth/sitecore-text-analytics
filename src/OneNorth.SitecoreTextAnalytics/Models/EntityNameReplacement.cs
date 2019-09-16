using System.Text.RegularExpressions;

namespace OneNorth.SitecoreTextAnalytics.Models
{
    public class EntityNameReplacement
    {
        public Regex Regex { get; set; }
        public string Replacement { get; set; }
    }
}
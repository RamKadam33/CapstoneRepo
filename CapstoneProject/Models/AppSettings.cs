using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CapstoneProject.Models
{
    

    public class AppSettings
    {
        public ConfluenceSettings Confluence { get; set; } = new();
        public GitHubSettings GitHub { get; set; } = new();
    }

    public class ConfluenceSettings
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string SpaceKey { get; set; } = string.Empty;
        public string TemplatePageTitle { get; set; } = string.Empty;
    }

    public class GitHubSettings
    {
        public string RepositoryUrl { get; set; } = string.Empty;
        public string Branch { get; set; } = "main";
    }
}

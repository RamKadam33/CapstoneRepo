using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
    using CapstoneProject.Models;
    using global::CapstoneProject.Models;

namespace CapstoneProject.Services
{  

    public class ConfluencePageService
    {
        public Task CreatePageAsync(GeneratedPage page)
        {
            // TODO: send final page content to Confluence API
            return Task.CompletedTask;
        }

        public Task<string> GetTemplatePageAsync(string templatePageTitle)
        {
            // TODO: read template page content from Confluence
            return Task.FromResult(string.Empty);
        }
    }
}

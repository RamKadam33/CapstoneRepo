using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CapstoneProject.Models;

namespace CapstoneProject.Services
{

   

    public class DocumentationSyncService
    {
        private readonly ConfluenceService _confluenceService;
        private readonly GitHubService _gitHubService;

        public DocumentationSyncService(ConfluenceService confluenceService, GitHubService gitHubService)
        {
            _confluenceService = confluenceService;
            _gitHubService = gitHubService;
        }

        public Task RunAsync()
        {
            // TODO: coordinate template read, repo scan, extraction, and page creation
            return Task.CompletedTask;
        }
    }
}

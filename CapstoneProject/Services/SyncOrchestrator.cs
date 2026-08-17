using CapstoneProject.Models;
using Microsoft.Extensions.Configuration;

namespace CapstoneProject.Services
{
    public class SyncOrchestrator
    {
        private readonly TemplateParserService _templateParserService;
        private readonly ExtractionService _extractionService;
        private readonly PageBuilderService _pageBuilderService;
        private readonly ConfluenceService _confluenceService;
        private readonly GitHubService _gitHubService;
        private readonly IConfiguration _configuration;

        public SyncOrchestrator(
            TemplateParserService templateParserService,
            ExtractionService extractionService,
            PageBuilderService pageBuilderService,
            ConfluenceService confluenceService,
            GitHubService gitHubService,
            IConfiguration configuration)
        {
            _templateParserService = templateParserService;
            _extractionService = extractionService;
            _pageBuilderService = pageBuilderService;
            _confluenceService = confluenceService;
            _gitHubService = gitHubService;
            _configuration = configuration;
        }

        public async Task RunAsync()
        {
            var repositoryUrl = _configuration["GitHub:RepositoryUrl"];
            var branch = _configuration["GitHub:Branch"] ?? "main";

            if (string.IsNullOrWhiteSpace(repositoryUrl))
                throw new InvalidOperationException("GitHub:RepositoryUrl is not configured.");

            // 1. Read template page from Confluence
            var templateContent = await _confluenceService.GetTemplatePageAsync();

            if (string.IsNullOrWhiteSpace(templateContent))
                throw new InvalidOperationException("Template page content could not be loaded from Confluence.");

            // 2. Parse template structure
            var template = _templateParserService.Parse(templateContent);

            // 3. Read repository structure
            var repositoryInfo = await _gitHubService.GetRepositoryInfoAsync(repositoryUrl, branch);

            // 4. Extract facts from repo content
            var extractionResult = await _extractionService.ExtractAsync(template, repositoryInfo, repositoryUrl);

            // 5. Build final Confluence page
            var generatedPage = _pageBuilderService.Build(extractionResult);

            // 6. Create page in Confluence
            await _confluenceService.CreatePageAsync(generatedPage);
        }
    }
}
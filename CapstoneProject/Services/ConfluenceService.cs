using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CapstoneProject.Models;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace CapstoneProject.Services
{
    public class ConfluenceService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _spaceKey;
        private readonly string _templatePageId;
        private readonly string _templatePageTitle;
        private readonly string _username;
        private readonly string _apiToken;

        public ConfluenceService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;

            _baseUrl = configuration["Confluence:BaseUrl"]?.TrimEnd('/') ?? string.Empty;
            _spaceKey = configuration["Confluence:SpaceKey"] ?? string.Empty;
            _templatePageId = configuration["Confluence:TemplatePageId"] ?? string.Empty;
            _templatePageTitle = configuration["Confluence:TemplatePageI"] ?? string.Empty;
            _username = configuration["Confluence:Username"] ?? string.Empty;
            _apiToken = configuration["Confluence:ApiToken"] ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(_username) && !string.IsNullOrWhiteSpace(_apiToken))
            {
                var authValue = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{_username}:{_apiToken}"));

                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", authValue);
            }
        }

        public async Task<string> GetTemplatePageAsync()
        {
            if (string.IsNullOrWhiteSpace(_baseUrl))
                throw new InvalidOperationException("Confluence BaseUrl is not configured.");

            if (string.IsNullOrWhiteSpace(_templatePageId))
                throw new InvalidOperationException("Confluence TemplatePageId is not configured.");

            var url = $"{_baseUrl}/spaces/{_spaceKey}/pages/{_templatePageId}/{_templatePageTitle}";
       // https://kb.epam.com/spaces/EPMQAOPQEP/pages/2898120078/App-Manifest-Template-v1
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            
            JObject o = JsonSerializer.Deserialize<JObject>(json);
            string? content = (string?)o["body"]?["storage"]?["value"];

            return content ?? string.Empty;
        }

        public async Task CreatePageAsync(GeneratedPage page)
        {
            if (string.IsNullOrWhiteSpace(_baseUrl))
                throw new InvalidOperationException("Confluence BaseUrl is not configured.");

            if (string.IsNullOrWhiteSpace(_spaceKey))
                throw new InvalidOperationException("Confluence SpaceKey is not configured.");

            var payload = new
            {
                type = "page",
                title = page.Title,
                space = new { key = _spaceKey },
                body = new
                {
                    storage = new
                    {
                        value = page.Content,
                        representation = "storage"
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/spaces/{_spaceKey}/pages/{_templatePageId}/{_templatePageTitle}", content);
            response.EnsureSuccessStatusCode();
        }
    }

    public class BodyStorage
    {
        public Body Body { get; set; } = new();
    }
    public class Body
    {
        public Storage Storage { get; set; } = new();
    }
    public class Storage
    {
        public string? Value { get; set; }
    }
}
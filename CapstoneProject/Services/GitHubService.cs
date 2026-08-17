using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CapstoneProject.Models;
using Microsoft.Extensions.Configuration;

namespace CapstoneProject.Services
{
    public class GitHubService
    {
        private readonly HttpClient _httpClient;
        private readonly string _token;

        public GitHubService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _token = configuration["GitHub:Token"] ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(_token))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _token);
            }

            if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            {
                _httpClient.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue("CapstoneProject", "1.0"));
            }

            if (!_httpClient.DefaultRequestHeaders.Accept.Any())
            {
                _httpClient.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            }
        }

        public async Task<RepositoryInfo> GetRepositoryInfoAsync(string repositoryUrl, string branch = "main")
        {
            var (owner, repo) = ParseOwnerAndRepo(repositoryUrl);

            var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/git/trees/{branch}?recursive=1";

            using var response = await _httpClient.GetAsync(apiUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var files = new List<string>();

            if (doc.RootElement.TryGetProperty("tree", out var tree))
            {
                foreach (var item in tree.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var typeProp) &&
                        item.TryGetProperty("path", out var pathProp))
                    {
                        var type = typeProp.GetString();
                        var path = pathProp.GetString();

                        if (type == "blob" && !string.IsNullOrWhiteSpace(path))
                        {
                            files.Add(path!);
                        }
                    }
                }
            }

            return new RepositoryInfo
            {
                RepositoryUrl = repositoryUrl,
                Branch = branch,
                Files = files
            };
        }

        public async Task<string> ReadFileAsync(string repositoryUrl, string branch, string filePath)
        {
            var (owner, repo) = ParseOwnerAndRepo(repositoryUrl);

            var encodedPath = string.Join("/",
                filePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                        .Select(Uri.EscapeDataString));

            var apiUrl =
                $"https://api.github.com/repos/{owner}/{repo}/contents/{encodedPath}?ref={Uri.EscapeDataString(branch)}";

            using var response = await _httpClient.GetAsync(apiUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("content", out var contentProp))
                return string.Empty;

            var content = contentProp.GetString() ?? string.Empty;
            content = content.Replace("\n", string.Empty).Replace("\r", string.Empty);

            var bytes = Convert.FromBase64String(content);
            return Encoding.UTF8.GetString(bytes);
        }

        public async Task<Dictionary<string, string>> ReadKeyFilesAsync(
            string repositoryUrl,
            string branch,
            IEnumerable<string> filePaths)
        {
            var result = new Dictionary<string, string>();

            foreach (var filePath in filePaths)
            {
                try
                {
                    var content = await ReadFileAsync(repositoryUrl, branch, filePath);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        result[filePath] = content;
                    }
                }
                catch
                {
                    // Ignore files that fail to read
                }
            }

            return result;
        }

        private static (string owner, string repo) ParseOwnerAndRepo(string repositoryUrl)
        {
            if (string.IsNullOrWhiteSpace(repositoryUrl))
                throw new ArgumentException("Repository URL cannot be empty.", nameof(repositoryUrl));

            if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri))
                throw new ArgumentException("Invalid GitHub repository URL.", nameof(repositoryUrl));

            var segments = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            if (segments.Count < 2)
                throw new ArgumentException("Invalid GitHub repository URL.", nameof(repositoryUrl));

            var owner = segments[0];
            var repo = segments[1].Replace(".git", string.Empty);

            return (owner, repo);
        }
    }
}
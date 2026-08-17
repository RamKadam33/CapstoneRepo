using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CapstoneProject.Models;

namespace CapstoneProject.Services
{
    public class ExtractionService
    {
        private readonly GitHubService _gitHubService;

        public ExtractionService(GitHubService gitHubService)
        {
            _gitHubService = gitHubService;
        }

        public ExtractionResult Extract(ConfluenceTemplate template, RepositoryInfo repositoryInfo, string repositoryUrl)
        {
            return ExtractAsync(template, repositoryInfo, repositoryUrl).GetAwaiter().GetResult();
        }

        public async Task<ExtractionResult> ExtractAsync(
            ConfluenceTemplate template,
            RepositoryInfo repositoryInfo,
            string repositoryUrl)
        {
            template ??= new ConfluenceTemplate();
            repositoryInfo ??= new RepositoryInfo();

            var result = new ExtractionResult();

            var importantFiles = SelectImportantFiles(repositoryInfo.Files);
            var fileContents = await _gitHubService.ReadKeyFilesAsync(
                repositoryUrl,
                string.IsNullOrWhiteSpace(repositoryInfo.Branch) ? "main" : repositoryInfo.Branch,
                importantFiles);

            var facts = BuildFacts(fileContents, repositoryInfo);

            var templateFields = (template.Fields != null && template.Fields.Count > 0)
                ? template.Fields
                : GetDefaultTemplateFields();

            foreach (var field in templateFields)
            {
                var value = ResolveFieldValue(field.Name, facts);
                var source = GetSourceForField(field.Name, facts);

                var outputField = new DocumentationField
                {
                    Name = field.Name,
                    Value = string.IsNullOrWhiteSpace(value) ? "Not Found" : value,
                    IsRequired = field.IsRequired,
                    Source = source
                };

                result.Fields.Add(outputField);

                if (string.IsNullOrWhiteSpace(value))
                {
                    result.MissingFields.Add(field.Name);
                }
            }

            return result;
        }

        private static List<string> SelectImportantFiles(IEnumerable<string> files)
        {
            var selected = new List<string>();

            foreach (var file in files ?? Enumerable.Empty<string>())
            {
                var lower = file.ToLowerInvariant();

                if (lower.EndsWith("readme.md") ||
                    lower.EndsWith("package.json") ||
                    lower.EndsWith("pom.xml") ||
                    lower.EndsWith("requirements.txt") ||
                    lower.EndsWith("pyproject.toml") ||
                    lower.EndsWith("build.gradle") ||
                    lower.EndsWith("build.gradle.kts") ||
                    lower.EndsWith(".csproj") ||
                    lower.EndsWith(".env.example") ||
                    lower.EndsWith("dockerfile") ||
                    lower.Contains(".github/workflows/") ||
                    lower.EndsWith("azure-pipelines.yml") ||
                    lower.EndsWith("gitlab-ci.yml"))
                {
                    selected.Add(file);
                }
            }

            return selected.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<DocumentationField> GetDefaultTemplateFields()
        {
            return new List<DocumentationField>
            {
                new() { Name = "Application Name", IsRequired = true },
                new() { Name = "Project Overview", IsRequired = true },
                new() { Name = "System Architecture", IsRequired = true },
                new() { Name = "Endpoint/Entry Points", IsRequired = true },
                new() { Name = "Environment Config", IsRequired = true },
                new() { Name = "Maintainers", IsRequired = false }
            };
        }

        private static Dictionary<string, string> BuildFacts(
            Dictionary<string, string> fileContents,
            RepositoryInfo repositoryInfo)
        {
            var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Repository-level fallback
            SetFact(facts, sources, "repository name", GetRepoName(repositoryInfo.RepositoryUrl), "repository url");

            foreach (var kvp in fileContents)
            {
                var path = kvp.Key;
                var content = kvp.Value ?? string.Empty;
                var lowerPath = path.ToLowerInvariant();

                if (lowerPath.EndsWith("readme.md"))
                {
                    ExtractFromReadme(content, facts, sources);
                }
                else if (lowerPath.EndsWith("package.json"))
                {
                    ExtractFromPackageJson(content, facts, sources);
                }
                else if (lowerPath.EndsWith("pom.xml"))
                {
                    ExtractFromPomXml(content, facts, sources);
                }
                else if (lowerPath.EndsWith(".csproj"))
                {
                    ExtractFromCsproj(content, facts, sources);
                }
                else if (lowerPath.EndsWith("requirements.txt") || lowerPath.EndsWith("pyproject.toml"))
                {
                    ExtractFromPythonFiles(content, facts, sources, path);
                }
                else if (lowerPath.EndsWith(".env.example") || lowerPath.Contains("/.env"))
                {
                    ExtractEnvironmentVariables(content, facts, sources, path);
                }
                else if (lowerPath.Contains(".github/workflows/") ||
                         lowerPath.EndsWith("azure-pipelines.yml") ||
                         lowerPath.EndsWith("gitlab-ci.yml"))
                {
                    SetFact(facts, sources, "deployment pipeline", InferDeploymentPipeline(path), path);
                }
                else if (lowerPath.EndsWith("dockerfile"))
                {
                    SetFact(facts, sources, "infrastructure", "Docker", path);
                }
            }

            InferGeneralFacts(fileContents, repositoryInfo, facts, sources);

            return facts;
        }

        private static void InferGeneralFacts(
            Dictionary<string, string> fileContents,
            RepositoryInfo repositoryInfo,
            Dictionary<string, string> facts,
            Dictionary<string, string> sources)
        {
            var files = repositoryInfo.Files ?? new List<string>();
            var hasCs = files.Any(f =>
                f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase));

            var hasNode = files.Any(f =>
                f.EndsWith("package.json", StringComparison.OrdinalIgnoreCase));

            var hasJava = files.Any(f =>
                f.EndsWith("pom.xml", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith("build.gradle", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith("build.gradle.kts", StringComparison.OrdinalIgnoreCase));

            var hasPython = files.Any(f =>
                f.EndsWith("requirements.txt", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith("pyproject.toml", StringComparison.OrdinalIgnoreCase));

            if (hasCs && !facts.ContainsKey("system architecture"))
            {
                var csprojInfo = fileContents
                    .Where(x => x.Key.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Value)
                    .FirstOrDefault();

                var targetFramework = TryExtractCsprojTargetFramework(csprojInfo);
                var techStack = string.IsNullOrWhiteSpace(targetFramework)
                    ? "C# / .NET"
                    : $"C# / .NET ({targetFramework})";

                SetFact(facts, sources, "system architecture", techStack, ".csproj");
            }

            if (hasNode && !facts.ContainsKey("system architecture"))
            {
                SetFact(facts, sources, "system architecture", "Node.js", "package.json");
            }

            if (hasJava && !facts.ContainsKey("system architecture"))
            {
                SetFact(facts, sources, "system architecture", "Java", "pom.xml / build.gradle");
            }

            if (hasPython && !facts.ContainsKey("system architecture"))
            {
                SetFact(facts, sources, "system architecture", "Python", "requirements.txt / pyproject.toml");
            }

            // Entry point detection
            var entryPoints = files.Where(f =>
                    f.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith("app.py", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith("main.py", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith("index.js", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith("index.ts", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith("server.js", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith("server.ts", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (entryPoints.Any())
            {
                SetFact(facts, sources, "endpoint/entry points", string.Join(", ", entryPoints), "repository file tree");
            }

            // Maintainers / authors
            if (!facts.ContainsKey("maintainers"))
            {
                var maintainer = TryGetAuthorFromFiles(fileContents);
                if (!string.IsNullOrWhiteSpace(maintainer))
                {
                    SetFact(facts, sources, "maintainers", maintainer, "metadata files");
                }
            }

            // Build tool
            if (!facts.ContainsKey("build tool"))
            {
                if (hasCs)
                {
                    SetFact(facts, sources, "build tool", ".NET SDK / MSBuild", ".csproj");
                }
                else if (hasNode)
                {
                    SetFact(facts, sources, "build tool", "NPM / Yarn", "package.json");
                }
                else if (hasJava)
                {
                    SetFact(facts, sources, "build tool", files.Any(f => f.EndsWith("pom.xml", StringComparison.OrdinalIgnoreCase)) ? "Maven" : "Gradle", "build file");
                }
                else if (hasPython)
                {
                    SetFact(facts, sources, "build tool", "Pip / Poetry", "python config");
                }
            }

            // Test frameworks
            if (!facts.ContainsKey("test frameworks"))
            {
                var testFrameworks = InferTestFrameworks((IEnumerable<string>)fileContents);
                if (!string.IsNullOrWhiteSpace(testFrameworks)) 
                {
                    SetFact(facts, sources, "test frameworks", testFrameworks, "package/config files");
                }
            }

            // Cloud provider
            if (!facts.ContainsKey("cloud provider"))
            {
                var cloud = InferCloudProvider(fileContents.Values);
                if (!string.IsNullOrWhiteSpace(cloud))
                {
                    SetFact(facts, sources, "cloud provider", cloud, "repository files");
                }
            }

            // Database
            if (!facts.ContainsKey("primary database"))
            {
                var db = InferDatabase(fileContents.Values);
                if (!string.IsNullOrWhiteSpace(db))
                {
                    SetFact(facts, sources, "primary database", db, "repository files");
                }
            }

            // Infrastructure
            if (!facts.ContainsKey("infrastructure"))
            {
                var infra = InferInfrastructure(fileContents.Values);
                if (!string.IsNullOrWhiteSpace(infra))
                {
                    SetFact(facts, sources, "infrastructure", infra, "repository files");
                }
            }

            // Deployment pipeline fallback
            if (!facts.ContainsKey("deployment pipeline"))
            {
                if (files.Any(f => f.Contains(".github/workflows/", StringComparison.OrdinalIgnoreCase)))
                {
                    SetFact(facts, sources, "deployment pipeline", "GitHub Actions", ".github/workflows");
                }
                else if (files.Any(f => f.EndsWith("azure-pipelines.yml", StringComparison.OrdinalIgnoreCase)))
                {
                    SetFact(facts, sources, "deployment pipeline", "Azure Pipelines", "azure-pipelines.yml");
                }
                else if (files.Any(f => f.EndsWith("gitlab-ci.yml", StringComparison.OrdinalIgnoreCase)))
                {
                    SetFact(facts, sources, "deployment pipeline", "GitLab CI", "gitlab-ci.yml");
                }
            }

            // Application name fallback
            if (!facts.ContainsKey("application name"))
            {
                var readmeTitle = fileContents
                    .Where(x => x.Key.EndsWith("README.md", StringComparison.OrdinalIgnoreCase))
                    .Select(x => TryExtractReadmeTitle(x.Value))
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

                if (!string.IsNullOrWhiteSpace(readmeTitle))
                {
                    SetFact(facts, sources, "application name", readmeTitle!, "README.md");
                }
            }

            // Project overview fallback
            if (!facts.ContainsKey("project overview"))
            {
                var overview = fileContents
                    .Where(x => x.Key.EndsWith("README.md", StringComparison.OrdinalIgnoreCase))
                    .Select(x => TryExtractReadmeOverview(x.Value))
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

                if (!string.IsNullOrWhiteSpace(overview))
                {
                    SetFact(facts, sources, "project overview", overview!, "README.md");
                }
            }
        }

        private static void ExtractFromReadme(
            string content,
            Dictionary<string, string> facts,
            Dictionary<string, string> sources)
        {
            var title = TryExtractReadmeTitle(content);
            if (!string.IsNullOrWhiteSpace(title))
            {
                SetFact(facts, sources, "application name", title, "README.md");
            }

            var overview = TryExtractReadmeOverview(content);
            if (!string.IsNullOrWhiteSpace(overview))
            {
                SetFact(facts, sources, "project overview", overview, "README.md");
            }

            var maintainer = TryExtractMaintainer(content);
            if (!string.IsNullOrWhiteSpace(maintainer))
            {
                SetFact(facts, sources, "maintainers", maintainer, "README.md");
            }
        }

        private static void ExtractFromPackageJson(
            string content,
            Dictionary<string, string> facts,
            Dictionary<string, string> sources)
        {
            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                var name = GetJsonString(root, "name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    SetFact(facts, sources, "application name", name, "package.json");
                }

                var description = GetJsonString(root, "description");
                if (!string.IsNullOrWhiteSpace(description))
                {
                    SetFact(facts, sources, "project overview", description, "package.json");
                }

                var author = GetJsonString(root, "author");
                if (!string.IsNullOrWhiteSpace(author))
                {
                    SetFact(facts, sources, "maintainers", author, "package.json");
                }

                var scripts = GetJsonObject(root, "scripts");
                if (scripts.ValueKind == JsonValueKind.Object && scripts.TryGetProperty("start", out var startScript))
                {
                    SetFact(facts, sources, "endpoint/entry points", $"npm start -> {startScript.GetString()}", "package.json");
                }

                var dependencyHints = CollectDependencyHints(root);
                if (!string.IsNullOrWhiteSpace(dependencyHints))
                {
                    SetFact(facts, sources, "system architecture", dependencyHints, "package.json");
                }

                var testFrameworks = InferTestFrameworksFromPackageJson(root);
                if (!string.IsNullOrWhiteSpace(testFrameworks))
                {
                    SetFact(facts, sources, "test frameworks", testFrameworks, "package.json");
                }
            }
            catch
            {
                // Ignore malformed JSON
            }
        }

        private static void ExtractFromPomXml(
            string content,
            Dictionary<string, string> facts,
            Dictionary<string, string> sources)
        {
            try
            {
                var doc = XDocument.Parse(content);
                var root = doc.Root;
                if (root == null) return;

                XNamespace ns = root.Name.Namespace;

                var artifactId = root.Descendants(ns + "artifactId").FirstOrDefault()?.Value;
                if (!string.IsNullOrWhiteSpace(artifactId))
                {
                    SetFact(facts, sources, "application name", artifactId, "pom.xml");
                }

                var name = root.Descendants(ns + "name").FirstOrDefault()?.Value;
                if (!string.IsNullOrWhiteSpace(name) && !facts.ContainsKey("application name"))
                {
                    SetFact(facts, sources, "application name", name, "pom.xml");
                }

                var description = root.Descendants(ns + "description").FirstOrDefault()?.Value;
                if (!string.IsNullOrWhiteSpace(description))
                {
                    SetFact(facts, sources, "project overview", description, "pom.xml");
                }

                var javaVersion = root.Descendants(ns + "java.version").FirstOrDefault()?.Value;
                if (!string.IsNullOrWhiteSpace(javaVersion))
                {
                    SetFact(facts, sources, "system architecture", $"Java {javaVersion}", "pom.xml");
                }

                var maintainer = root.Descendants(ns + "developer")
                    .Descendants(ns + "name")
                    .FirstOrDefault()?.Value;

                if (!string.IsNullOrWhiteSpace(maintainer))
                {
                    SetFact(facts, sources, "maintainers", maintainer, "pom.xml");
                }

                SetFact(facts, sources, "build tool", "Maven", "pom.xml");
            }
            catch
            {
                // Ignore malformed XML
            }
        }

        private static void ExtractFromCsproj(
            string content,
            Dictionary<string, string> facts,
            Dictionary<string, string> sources)
        {
            try
            {
                var doc = XDocument.Parse(content);
                var root = doc.Root;
                if (root == null) return;

                var ns = root.Name.Namespace;

                var targetFramework = root.Descendants(ns + "TargetFramework").FirstOrDefault()?.Value
                                      ?? root.Descendants(ns + "TargetFrameworks").FirstOrDefault()?.Value;

                var rootNamespace = root.Descendants(ns + "RootNamespace").FirstOrDefault()?.Value;
                var assemblyName = root.Descendants(ns + "AssemblyName").FirstOrDefault()?.Value;

                if (!string.IsNullOrWhiteSpace(assemblyName))
                {
                    SetFact(facts, sources, "application name", assemblyName, ".csproj");
                }
                else if (!string.IsNullOrWhiteSpace(rootNamespace) && !facts.ContainsKey("application name"))
                {
                    SetFact(facts, sources, "application name", rootNamespace, ".csproj");
                }

                if (!string.IsNullOrWhiteSpace(targetFramework))
                {
                    SetFact(facts, sources, "system architecture", $"C# / .NET ({targetFramework})", ".csproj");
                }
                else
                {
                    SetFact(facts, sources, "system architecture", "C# / .NET", ".csproj");
                }

                var packageRefs = root.Descendants(ns + "PackageReference")
                    .Select(x => x.Attribute("Include")?.Value)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToList();

                var testFrameworks = string.Join(", ",
                    packageRefs.Where(p =>
                        p.Contains("xunit", StringComparison.OrdinalIgnoreCase) ||
                        p.Contains("nunit", StringComparison.OrdinalIgnoreCase) ||
                        p.Contains("mstest", StringComparison.OrdinalIgnoreCase)));

                if (!string.IsNullOrWhiteSpace(testFrameworks))
                {
                    SetFact(facts, sources, "test frameworks", testFrameworks, ".csproj");
                }

                SetFact(facts, sources, "build tool", ".NET SDK / MSBuild", ".csproj");
            }
            catch
            {
                // Ignore malformed XML
            }
        }

        private static void ExtractFromPythonFiles(
            string content,
            Dictionary<string, string> facts,
            Dictionary<string, string> sources,
            string path)
        {
            if (path.EndsWith("requirements.txt", StringComparison.OrdinalIgnoreCase))
            {
                var frameworks = new List<string>();

                if (content.Contains("pytest", StringComparison.OrdinalIgnoreCase))
                    frameworks.Add("pytest");
                if (content.Contains("unittest", StringComparison.OrdinalIgnoreCase))
                    frameworks.Add("unittest");
                if (content.Contains("fastapi", StringComparison.OrdinalIgnoreCase))
                    frameworks.Add("FastAPI");
                if (content.Contains("flask", StringComparison.OrdinalIgnoreCase))
                    frameworks.Add("Flask");
                if (content.Contains("django", StringComparison.OrdinalIgnoreCase))
                    frameworks.Add("Django");

                if (frameworks.Count > 0)
                {
                    SetFact(facts, sources, "test frameworks", string.Join(", ", frameworks.Distinct()), "requirements.txt");
                }

                SetFact(facts, sources, "build tool", "pip", "requirements.txt");
            }
            else if (path.EndsWith("pyproject.toml", StringComparison.OrdinalIgnoreCase))
            {
                if (content.Contains("[tool.poetry]", StringComparison.OrdinalIgnoreCase))
                {
                    SetFact(facts, sources, "build tool", "Poetry", "pyproject.toml");
                }
                else
                {
                    SetFact(facts, sources, "build tool", "Python packaging", "pyproject.toml");
                }
            }
        }

        private static void ExtractEnvironmentVariables(
            string content,
            Dictionary<string, string> facts,
            Dictionary<string, string> sources,
            string path)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in content.Split('\n'))
            {
                var trimmed = line.Trim();

                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                    continue;

                var match = Regex.Match(trimmed, @"^(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=");
                if (match.Success)
                {
                    keys.Add(match.Groups[1].Value.Trim());
                }
            }

            if (keys.Count > 0)
            {
                SetFact(facts, sources, "environment config", string.Join(", ", keys.OrderBy(x => x)), path);
            }
        }

        private static string ResolveFieldValue(string fieldName, Dictionary<string, string> facts)
        {
            var key = Normalize(fieldName);

            return key switch
            {
                "applicationname" or "appname" or "name" or "servicename" =>
                    GetFact(facts, "application name", "repository name"),

                "projectoverview" or "overview" or "description" =>
                    GetFact(facts, "project overview"),

                "systemarchitecture" or "techstack" or "architecture" =>
                    GetFact(facts, "system architecture"),

                "endpointentrypoints" or "entrypoints" or "entrypoint" =>
                    GetFact(facts, "endpoint/entry points"),

                "environmentconfig" or "envconfig" or "criticalenvvariables" =>
                    GetFact(facts, "environment config"),

                "maintainers" or "owner" or "authors" or "serviceowner" =>
                    GetFact(facts, "maintainers"),

                "buildtool" =>
                    GetFact(facts, "build tool"),

                "deploymentpipeline" =>
                    GetFact(facts, "deployment pipeline"),

                "testframeworks" =>
                    GetFact(facts, "test frameworks"),

                "primarydatabase" =>
                    GetFact(facts, "primary database"),

                "cloudprovider" =>
                    GetFact(facts, "cloud provider"),

                "infrastructure" =>
                    GetFact(facts, "infrastructure"),

                _ => GetFact(facts, key)
            };
        }

        private static string GetSourceForField(string fieldName, Dictionary<string, string> facts)
        {
            var key = Normalize(fieldName);

            return key switch
            {
                "applicationname" or "appname" or "name" or "servicename" => "See source in extracted facts",
                "projectoverview" or "overview" or "description" => "See source in extracted facts",
                "systemarchitecture" or "techstack" or "architecture" => "See source in extracted facts",
                "endpointentrypoints" or "entrypoints" or "entrypoint" => "See source in extracted facts",
                "environmentconfig" or "envconfig" or "criticalenvvariables" => "See source in extracted facts",
                "maintainers" or "owner" or "authors" or "serviceowner" => "See source in extracted facts",
                "buildtool" => "See source in extracted facts",
                "deploymentpipeline" => "See source in extracted facts",
                "testframeworks" => "See source in extracted facts",
                "primarydatabase" => "See source in extracted facts",
                "cloudprovider" => "See source in extracted facts",
                "infrastructure" => "See source in extracted facts",
                _ => string.Empty
            };
        }

        private static string GetFact(Dictionary<string, string> facts, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (facts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static void SetFact(
            Dictionary<string, string> facts,
            Dictionary<string, string> sources,
            string key,
            string value,
            string source)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            facts[Normalize(key)] = value.Trim();
            sources[Normalize(key)] = source;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return new string(value
                .Where(char.IsLetterOrDigit)
                .ToArray())
                .ToLowerInvariant();
        }

        private static string? TryExtractReadmeTitle(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            var lines = content.Split('\n');

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("# "))
                {
                    return trimmed.TrimStart('#').Trim();
                }
            }

            return null;
        }

        private static string? TryExtractReadmeOverview(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            var lines = content.Split('\n');
            var afterTitle = false;
            var paragraph = new List<string>();

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();

                if (!afterTitle)
                {
                    if (line.StartsWith("# "))
                    {
                        afterTitle = true;
                    }

                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    if (paragraph.Count > 0)
                        break;

                    continue;
                }

                if (line.StartsWith("# "))
                    break;

                if (line.StartsWith("- "))
                    continue;

                paragraph.Add(line);
            }

            var text = string.Join(" ", paragraph).Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static string? TryExtractMaintainer(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            var match = Regex.Match(content, @"(?im)^\s*(author|maintainer|owners?)\s*[:\-]\s*(.+)$");
            if (match.Success)
            {
                return match.Groups[2].Value.Trim();
            }

            return null;
        }

        private static string GetJsonString(JsonElement root, string propertyName)
        {
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(propertyName, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static JsonElement GetJsonObject(JsonElement root, string propertyName)
        {
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(propertyName, out var prop) &&
                prop.ValueKind == JsonValueKind.Object)
            {
                return prop;
            }

            return default;
        }

        private static string CollectDependencyHints(JsonElement packageJson)
        {
            var dependencies = new List<string>();

            foreach (var propName in new[] { "dependencies", "devDependencies" })
            {
                if (!packageJson.TryGetProperty(propName, out var deps) || deps.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var dep in deps.EnumerateObject())
                {
                    var name = dep.Name;
                    if (name.Contains("react", StringComparison.OrdinalIgnoreCase))
                        dependencies.Add("React");
                    else if (name.Contains("express", StringComparison.OrdinalIgnoreCase))
                        dependencies.Add("Express");
                    else if (name.Contains("next", StringComparison.OrdinalIgnoreCase))
                        dependencies.Add("Next.js");
                    else if (name.Contains("nestjs", StringComparison.OrdinalIgnoreCase))
                        dependencies.Add("NestJS");
                    else if (name.Contains("angular", StringComparison.OrdinalIgnoreCase))
                        dependencies.Add("Angular");
                    else if (name.Contains("vue", StringComparison.OrdinalIgnoreCase))
                        dependencies.Add("Vue");
                    else if (name.Contains("svelte", StringComparison.OrdinalIgnoreCase))
                        dependencies.Add("Svelte");
                }
            }

            return dependencies.Count > 0
                ? string.Join(", ", dependencies.Distinct(StringComparer.OrdinalIgnoreCase))
                : string.Empty;
        }

        private static string InferTestFrameworksFromPackageJson(JsonElement packageJson)
        {
            var frameworks = new List<string>();

            if (packageJson.TryGetProperty("devDependencies", out var devDeps) &&
                devDeps.ValueKind == JsonValueKind.Object)
            {
                foreach (var dep in devDeps.EnumerateObject())
                {
                    var name = dep.Name;

                    if (name.Contains("jest", StringComparison.OrdinalIgnoreCase))
                        frameworks.Add("Jest");
                    if (name.Contains("mocha", StringComparison.OrdinalIgnoreCase))
                        frameworks.Add("Mocha");
                    if (name.Contains("vitest", StringComparison.OrdinalIgnoreCase))
                        frameworks.Add("Vitest");
                    if (name.Contains("cypress", StringComparison.OrdinalIgnoreCase))
                        frameworks.Add("Cypress");
                    if (name.Contains("playwright", StringComparison.OrdinalIgnoreCase))
                        frameworks.Add("Playwright");
                }
            }

            return frameworks.Count > 0
                ? string.Join(", ", frameworks.Distinct(StringComparer.OrdinalIgnoreCase))
                : string.Empty;
        }

        private static string? TryExtractCsprojTargetFramework(string? csprojContent)
        {
            if (string.IsNullOrWhiteSpace(csprojContent))
                return null;

            try
            {
                var doc = XDocument.Parse(csprojContent);
                var root = doc.Root;
                if (root == null) return null;

                var ns = root.Name.Namespace;

                var targetFramework = root.Descendants(ns + "TargetFramework").FirstOrDefault()?.Value;
                if (!string.IsNullOrWhiteSpace(targetFramework))
                    return targetFramework;

                var targetFrameworks = root.Descendants(ns + "TargetFrameworks").FirstOrDefault()?.Value;
                return string.IsNullOrWhiteSpace(targetFrameworks) ? null : targetFrameworks;
            }
            catch
            {
                return null;
            }
        }

        private static string InferDeploymentPipeline(string path)
        {
            var lower = path.ToLowerInvariant();

            if (lower.Contains(".github/workflows/"))
                return "GitHub Actions";

            if (lower.EndsWith("azure-pipelines.yml"))
                return "Azure Pipelines";

            if (lower.EndsWith("gitlab-ci.yml"))
                return "GitLab CI";

            return "CI/CD Pipeline";
        }

        private static string InferCloudProvider(IEnumerable<string> contents)
        {
            var combined = string.Join("\n", contents ?? Enumerable.Empty<string>());

            if (combined.Contains("aws", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("amazon", StringComparison.OrdinalIgnoreCase))
                return "AWS";

            if (combined.Contains("azure", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("arm template", StringComparison.OrdinalIgnoreCase))
                return "Azure";

            if (combined.Contains("gcp", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("google cloud", StringComparison.OrdinalIgnoreCase))
                return "GCP";

            return string.Empty;
        }

        private static string InferDatabase(IEnumerable<string> contents)
        {
            var combined = string.Join("\n", contents ?? Enumerable.Empty<string>());

            if (combined.Contains("postgres", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("npgsql", StringComparison.OrdinalIgnoreCase))
                return "PostgreSQL";

            if (combined.Contains("mysql", StringComparison.OrdinalIgnoreCase))
                return "MySQL";

            if (combined.Contains("sqlserver", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("mssql", StringComparison.OrdinalIgnoreCase))
                return "SQL Server";

            if (combined.Contains("mongodb", StringComparison.OrdinalIgnoreCase))
                return "MongoDB";

            if (combined.Contains("sqlite", StringComparison.OrdinalIgnoreCase))
                return "SQLite";

            if (combined.Contains("dynamodb", StringComparison.OrdinalIgnoreCase))
                return "DynamoDB";

            return string.Empty;
        }

        private static string InferInfrastructure(IEnumerable<string> contents)
        {
            var combined = string.Join("\n", contents ?? Enumerable.Empty<string>());

            var items = new List<string>();

            if (combined.Contains("docker", StringComparison.OrdinalIgnoreCase))
                items.Add("Docker");

            if (combined.Contains("kubernetes", StringComparison.OrdinalIgnoreCase) ||
                combined.Contains("k8s", StringComparison.OrdinalIgnoreCase))
                items.Add("Kubernetes");

            if (combined.Contains("serverless", StringComparison.OrdinalIgnoreCase))
                items.Add("Serverless");

            return items.Count > 0 ? string.Join(", ", items.Distinct(StringComparer.OrdinalIgnoreCase)) : string.Empty;
        }

        private static string InferTestFrameworks(IEnumerable<string> fileContents)
        {
            var combined = string.Join("\n", fileContents ?? Enumerable.Empty<string>());

            var tests = new List<string>();

            if (combined.Contains("xunit", StringComparison.OrdinalIgnoreCase))
                tests.Add("xUnit");

            if (combined.Contains("nunit", StringComparison.OrdinalIgnoreCase))
                tests.Add("NUnit");

            if (combined.Contains("mstest", StringComparison.OrdinalIgnoreCase))
                tests.Add("MSTest");

            if (combined.Contains("pytest", StringComparison.OrdinalIgnoreCase))
                tests.Add("pytest");

            if (combined.Contains("jest", StringComparison.OrdinalIgnoreCase))
                tests.Add("Jest");

            if (combined.Contains("mocha", StringComparison.OrdinalIgnoreCase))
                tests.Add("Mocha");

            if (combined.Contains("playwright", StringComparison.OrdinalIgnoreCase))
                tests.Add("Playwright");

            if (combined.Contains("cypress", StringComparison.OrdinalIgnoreCase))
                tests.Add("Cypress");

            return tests.Count > 0 ? string.Join(", ", tests.Distinct(StringComparer.OrdinalIgnoreCase)) : string.Empty;
        }

        private static string? TryGetAuthorFromFiles(Dictionary<string, string> fileContents)
        {
            foreach (var kvp in fileContents)
            {
                var path = kvp.Key.ToLowerInvariant();
                var content = kvp.Value ?? string.Empty;

                if (path.EndsWith("package.json"))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(content);
                        var author = GetJsonString(doc.RootElement, "author");
                        if (!string.IsNullOrWhiteSpace(author))
                            return author;
                    }
                    catch
                    {
                        // ignore
                    }
                }

                if (path.EndsWith("pom.xml"))
                {
                    try
                    {
                        var xml = XDocument.Parse(content);
                        var root = xml.Root;
                        if (root == null) continue;

                        var ns = root.Name.Namespace;
                        var name = root.Descendants(ns + "developer")
                            .Descendants(ns + "name")
                            .FirstOrDefault()?.Value;

                        if (!string.IsNullOrWhiteSpace(name))
                            return name;
                    }
                    catch
                    {
                        // ignore
                    }
                }

                if (path.EndsWith("readme.md"))
                {
                    var maintainer = TryExtractMaintainer(content);
                    if (!string.IsNullOrWhiteSpace(maintainer))
                        return maintainer;
                }
            }

            return null;
        }

        private static string GetRepoName(string repositoryUrl)
        {
            if (string.IsNullOrWhiteSpace(repositoryUrl))
                return string.Empty;

            try
            {
                var uri = new Uri(repositoryUrl);
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 2)
                    return segments[1].Replace(".git", string.Empty);
            }
            catch
            {
                // ignore
            }

            return string.Empty;
        }
    }
}
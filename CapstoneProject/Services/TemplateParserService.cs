using System.Net;
using System.Text.RegularExpressions;
using CapstoneProject.Models;

namespace CapstoneProject.Services
{
    public class TemplateParserService
    {
        public ConfluenceTemplate Parse(string templateContent)
        {
            var template = new ConfluenceTemplate
            {
                Title = ExtractTitle(templateContent)
            };

            if (string.IsNullOrWhiteSpace(templateContent))
                return template;

            var placeholders = ExtractPlaceholders(templateContent);

            foreach (var placeholder in placeholders)
            {
                template.Fields.Add(new DocumentationField
                {
                    Name = ToDisplayName(placeholder),
                    Value = string.Empty,
                    IsRequired = true,
                    Source = "Confluence Template"
                });
            }

            return template;
        }

        private static string ExtractTitle(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return "Template";

            // Try to extract from first heading in HTML storage format
            var htmlTitleMatch = Regex.Match(
                content,
                @"<h1[^>]*>(.*?)<\/h1>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (htmlTitleMatch.Success)
            {
                var title = WebUtility.HtmlDecode(StripTags(htmlTitleMatch.Groups[1].Value)).Trim();
                if (!string.IsNullOrWhiteSpace(title))
                    return title;
            }

            // Try markdown style title
            var markdownTitleMatch = Regex.Match(
                content,
                @"^\s*#\s+(.+)$",
                RegexOptions.Multiline);

            if (markdownTitleMatch.Success)
            {
                var title = WebUtility.HtmlDecode(markdownTitleMatch.Groups[1].Value).Trim();
                if (!string.IsNullOrWhiteSpace(title))
                    return title;
            }

            return "Template";
        }

        private static List<string> ExtractPlaceholders(string content)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(content))
                return result;

            // Match placeholders like {{App_Name}}
            var matches = Regex.Matches(content, @"\{\{\s*([A-Za-z0-9_.\-]+)\s*\}\}");

            foreach (Match match in matches)
            {
                if (match.Success && match.Groups.Count > 1)
                {
                    var placeholder = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(placeholder) &&
                        !result.Contains(placeholder, StringComparer.OrdinalIgnoreCase))
                    {
                        result.Add(placeholder);
                    }
                }
            }

            return result;
        }

        private static string ToDisplayName(string placeholder)
        {
            if (string.IsNullOrWhiteSpace(placeholder))
                return string.Empty;

            // Convert things like:
            // App_Name -> App Name
            // Tech_Stack -> Tech Stack
            // endpoint-entry-points -> Endpoint Entry Points
            var cleaned = placeholder
                .Replace("_", " ")
                .Replace("-", " ")
                .Replace(".", " ");

            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            return CultureToTitleCase(cleaned);
        }

        private static string CultureToTitleCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var words = value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(word =>
                {
                    if (word.Length == 1)
                        return word.ToUpperInvariant();

                    return char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant();
                });

            return string.Join(" ", words);
        }

        private static string StripTags(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            return Regex.Replace(input, "<.*?>", string.Empty);
        }
    }
}
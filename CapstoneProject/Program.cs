using CapstoneProject.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// HTTP clients for services that call external APIs
builder.Services.AddHttpClient<ConfluenceService>();
builder.Services.AddHttpClient<GitHubService>();

// Internal services
builder.Services.AddSingleton<TemplateParserService>();
builder.Services.AddSingleton<ExtractionService>();
builder.Services.AddSingleton<PageBuilderService>();
builder.Services.AddSingleton<SyncOrchestrator>();

builder.Logging.AddConsole();

var app = builder.Build();

var orchestrator = app.Services.GetRequiredService<SyncOrchestrator>();
await orchestrator.RunAsync();
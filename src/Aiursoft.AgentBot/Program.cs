using Aiursoft.Canon;
using Aiursoft.AgentBot;
using Aiursoft.GitRunner;
using Aiursoft.AgentBot.Configuration;
using Aiursoft.AgentBot.Services;
using Aiursoft.AgentBot.Services.Abstractions;
using Aiursoft.NugetNinja.GitServerBase.Models;
using Aiursoft.NugetNinja.GitServerBase.Services.Providers;
using Aiursoft.NugetNinja.GitServerBase.Services.Providers.Gitea;
using Aiursoft.NugetNinja.GitServerBase.Services.Providers.GitHub;
using Aiursoft.NugetNinja.GitServerBase.Services.Providers.GitLab;
using Aiursoft.CSTools.Services;
using Aiursoft.NugetNinja.GitServerBase.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

await CreateHostBuilder(args)
    .Build()
    .Services
    .GetRequiredService<Entry>()
    .RunAsync();

static IHostBuilder CreateHostBuilder(string[] args)
{
    return Host
        .CreateDefaultBuilder(args)
        .ConfigureLogging(logging =>
        {
            logging
                .AddFilter("Microsoft.Extensions", LogLevel.Warning)
                .AddFilter("System", LogLevel.Warning);
            logging.AddSimpleConsole(options =>
            {
                options.IncludeScopes = false;
                options.SingleLine = true;
                options.TimestampFormat = "mm:ss ";
            });
        })
        .ConfigureServices((context, services) =>
        {
            services.AddMemoryCache();
            services.AddHttpClient();
            services.Configure<List<Server>>(context.Configuration.GetSection("Servers"));
            services.Configure<AgentBotOptions>(context.Configuration.GetSection("AgentBot"));
            services.AddGitRunner();
            services.AddTransient<IVersionControlService, GitHubService>();
            services.AddTransient<IVersionControlService, GiteaService>();
            services.AddTransient<IVersionControlService, GitLabService>();
            services.AddTransient<HttpWrapper>();
            services.AddTransient<IAiWorkspaceManager, AiWorkspaceManager>();
            services.AddTransient<IAiCommandService, AiCommandService>();
            services.AddTransient<BotWorkflowEngine>();
            services.AddTransient<WorkspaceManager>();
            services.AddTransient<CommandService>();
            services.AddTransient<IssueProcessor>();
            services.AddTransient<MergeRequestProcessor>();
            services.AddTransient<MergeRequestReviewerProcessor>();
            services.AddTransient<PipelineProcessor>();
            services.AddTransient<Entry>();
            services.AddTransient<AiCliService>();
            services.AddTaskCanon();
        });
}

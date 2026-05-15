# Gemini Bot

[![MIT licensed](https://img.shields.io/badge/license-MIT-blue.svg)](https://gitlab.aiursoft.com/aiursoft/geminibot/-/blob/master/LICENSE)
[![Pipeline stat](https://gitlab.aiursoft.com/aiursoft/geminibot/badges/master/pipeline.svg)](https://gitlab.aiursoft.com/aiursoft/geminibot/-/pipelines)
[![Test Coverage](https://gitlab.aiursoft.com/aiursoft/geminibot/badges/master/coverage.svg)](https://gitlab.aiursoft.com/aiursoft/geminibot/-/pipelines)
[![NuGet version (aiursoft.geminibot)](https://img.shields.io/nuget/v/Aiursoft.geminibot.svg)](https://www.nuget.org/packages/Aiursoft.geminibot/)
[![Man hours](https://manhours.aiursoft.com/r/gitlab.aiursoft.com/aiursoft/geminibot.svg)](https://manhours.aiursoft.com/r/gitlab.aiursoft.com/aiursoft/geminibot.html)

## How it works

Gemini Bot is designed to autonomously manage the entire lifecycle of software development tasks on GitLab/GitHub. It follows a strictly prioritized workflow to ensure existing work is maintained before starting new tasks.

### Workflow Priorities

The bot operates in two main phases:

#### Phase 1: Merge Request Maintenance (Highest Priority)
Before looking for new work, the bot ensures its existing contributions are healthy. It scans for Merge Requests assigned to it or created by it that need attention:
- **Conflict Resolution**: If an MR has merge conflicts, the bot merges the target branch and invokes AI to resolve the conflicts.
- **Addressing Reviews**: If a human reviewer provides feedback, the bot reads the feedback and asks AI to implement the requested changes.
- **Fixing Pipelines**: If the CI/CD pipeline fails, the bot automatically downloads the failure logs and asks AI to fix the root cause.

#### Phase 2: Issue Resolution
Once all existing MRs are healthy, the bot looks for new issues assigned to it:
- It clones the repository and creates a dedicated branch.
- It passes the issue description to AI to implement the feature or fix.
- It automatically handles forking if it doesn't have direct push access to the repository.
- It creates a new Merge Request and assigns itself to it for continued maintenance.

### AI Engine Support

The bot supports multiple AI backends via the `Engine` configuration:

| Engine | CLI | Yolo flag |
|--------|-----|-----------|
| `Gemini` | `gemini --yolo` | Built-in |
| `Claude` | `claude --dangerously-skip-permissions --print` | `--dangerously-skip-permissions` |

When `Engine` is `Claude`, you can point it to any Anthropic-compatible API (DeepSeek, Ollama, etc.) via `ApiEndpoint`.

## Configuration

Configuration follows standard .NET conventions: `appsettings.json` → environment variables → CLI args. Environment variables use `__` as separator.

### appsettings.json

```json
{
  "Servers": [
    {
      "Provider": "GitLab",
      "EndPoint": "https://gitlab.aiursoft.com",
      "PushEndPoint": "https://{0}@gitlab.aiursoft.com",
      "DisplayName": "Bot",
      "UserName": "gemini-bot",
      "UserEmail": "bot@aiursoft.com",
      "ContributionBranch": "users/gemini/auto-fix-issue",
      "Token": "",
      "OnlyUpdate": false
    }
  ],
  "GeminiBot": {
    "Engine": "Claude",
    "Model": "deepseek-v4-pro",
    "ApiKey": "sk-xxx",
    "ApiEndpoint": "https://api.deepseek.com/anthropic"
  }
}
```

### Configuration reference

| Key | Env var | Required | Description |
|-----|---------|----------|-------------|
| `Engine` | `GeminiBot__Engine` | No | AI backend: `Gemini` (default) or `Claude` |
| `Model` | `GeminiBot__Model` | Yes | Model name (e.g. `gemini-3-pro-preview`, `deepseek-v4-pro`) |
| `ApiKey` | `GeminiBot__ApiKey` | Yes* | API key for the AI provider |
| `ApiEndpoint` | `GeminiBot__ApiEndpoint` | Claude only | Custom API base URL for Anthropic-compatible endpoints |
| `WorkspaceFolder` | `GeminiBot__WorkspaceFolder` | No | Temp directory for cloned repos (default: OS temp) |
| `GeminiTimeout` | `GeminiBot__GeminiTimeout` | No | CLI timeout (default: `00:35:00`) |
| `Servers__N__Provider` | `Servers__N__Provider` | Yes | Git host: `GitLab`, `GitHub`, `Gitea`, `AzureDevOps` |
| `Servers__N__Token` | `Servers__N__Token` | Yes | Personal access token for the git host |
| `Servers__N__EndPoint` | `Servers__N__EndPoint` | Yes | API endpoint URL |
| `Servers__N__PushEndPoint` | `Servers__N__PushEndPoint` | Yes | Git push URL template (use `{0}` for username placeholder) |
| `Servers__N__DisplayName` | `Servers__N__DisplayName` | Yes | Bot's display name for commits |
| `Servers__N__UserName` | `Servers__N__UserName` | Yes | Bot's username on the git host |
| `Servers__N__UserEmail` | `Servers__N__UserEmail` | Yes | Bot's email for commits |
| `Servers__N__ContributionBranch` | `Servers__N__ContributionBranch` | Yes | Branch name for bot's MRs/PRs |

**Server config → Docker env vars:**

```
GeminiBot__WorkspaceFolder=/workspace
GeminiBot__Model=gemini-3.1-pro-preview
GeminiBot__ApiKey=AIza...
Servers__0__Provider=GitLab
Servers__0__EndPoint=https://gitlab.aiursoft.com
Servers__0__PushEndPoint=https://{0}@gitlab.aiursoft.com
Servers__0__DisplayName=Gemini Bot
Servers__0__UserName=gemini-bot
Servers__0__UserEmail=gemini@aiursoft.com
Servers__0__ContributionBranch=users/gemini/auto-fix-issue
Servers__0__Token=glpat-...
```

## Installation

Requirements:
1. [.NET 10 SDK](http://dot.net/)

```bash
dotnet tool install --global Aiursoft.GeminiBot
```

## Local run

```bash
gemini-bot
```

## Docker Deployment

The container runs silently in the background via cron. No ports exposed.

### Docker Run

```bash
docker run -d \
  --name gemini-bot \
  -e GeminiBot__Engine=Claude \
  -e GeminiBot__Model=deepseek-v4-pro \
  -e GeminiBot__ApiKey=sk-xxx \
  -e GeminiBot__ApiEndpoint=https://api.deepseek.com/anthropic \
  -e Servers__0__Provider=GitLab \
  -e Servers__0__EndPoint=https://gitlab.aiursoft.com \
  -e Servers__0__PushEndPoint="https://{0}@gitlab.aiursoft.com" \
  -e Servers__0__DisplayName="Bot" \
  -e Servers__0__UserName=gemini-bot \
  -e Servers__0__UserEmail=bot@aiursoft.com \
  -e Servers__0__ContributionBranch=users/gemini/auto-fix-issue \
  -e Servers__0__Token=glpat-xxx \
  hub.aiursoft.com/aiursoft/geminibot
```

### Docker Compose

```yaml
version: "3.8"
services:
  gemini-bot:
    image: hub.aiursoft.com/aiursoft/geminibot
    restart: unless-stopped
    environment:
      GeminiBot__Engine: Claude
      GeminiBot__Model: deepseek-v4-pro
      GeminiBot__ApiKey: sk-xxx
      GeminiBot__ApiEndpoint: https://api.deepseek.com/anthropic
      Servers__0__Provider: GitLab
      Servers__0__EndPoint: https://gitlab.aiursoft.com
      Servers__0__PushEndPoint: "https://{0}@gitlab.aiursoft.com"
      Servers__0__DisplayName: Bot
      Servers__0__UserName: gemini-bot
      Servers__0__UserEmail: bot@aiursoft.com
      Servers__0__ContributionBranch: users/gemini/auto-fix-issue
      Servers__0__Token: glpat-xxx
    volumes:
      - gemini-bot-workspace:/workspace
      - gemini-bot-logs:/logs

volumes:
  gemini-bot-workspace:
  gemini-bot-logs:
```

### Kubernetes (CronJob)

```yaml
apiVersion: batch/v1
kind: CronJob
metadata:
  name: gemini-bot
spec:
  schedule: "0,30 * * * *"
  jobTemplate:
    spec:
      template:
        spec:
          containers:
            - name: gemini-bot
              image: hub.aiursoft.com/aiursoft/geminibot
              env:
                - name: GeminiBot__Engine
                  value: Claude
                - name: GeminiBot__Model
                  value: deepseek-v4-pro
                - name: GeminiBot__ApiKey
                  valueFrom:
                    secretKeyRef:
                      name: gemini-bot-secrets
                      key: api-key
                - name: GeminiBot__ApiEndpoint
                  value: https://api.deepseek.com/anthropic
                - name: Servers__0__Provider
                  value: GitLab
                - name: Servers__0__EndPoint
                  value: https://gitlab.aiursoft.com
                - name: Servers__0__PushEndPoint
                  value: "https://{0}@gitlab.aiursoft.com"
                - name: Servers__0__DisplayName
                  value: Bot
                - name: Servers__0__UserName
                  value: gemini-bot
                - name: Servers__0__UserEmail
                  value: bot@aiursoft.com
                - name: Servers__0__ContributionBranch
                  value: users/gemini/auto-fix-issue
                - name: Servers__0__Token
                  valueFrom:
                    secretKeyRef:
                      name: gemini-bot-secrets
                      key: gitlab-token
              volumeMounts:
                - name: workspace
                  mountPath: /workspace
                - name: logs
                  mountPath: /logs
          volumes:
            - name: workspace
              emptyDir: {}
            - name: logs
              emptyDir: {}
          restartPolicy: OnFailure
---
apiVersion: v1
kind: Secret
metadata:
  name: gemini-bot-secrets
type: Opaque
stringData:
  api-key: sk-xxx
  gitlab-token: glpat-xxx
```

### Docker Swarm

```bash
docker service create \
  --name gemini-bot \
  --restart-condition any \
  -e GeminiBot__Engine=Claude \
  -e GeminiBot__Model=deepseek-v4-pro \
  -e GeminiBot__ApiKey=sk-xxx \
  -e GeminiBot__ApiEndpoint=https://api.deepseek.com/anthropic \
  -e Servers__0__Provider=GitLab \
  -e Servers__0__EndPoint=https://gitlab.aiursoft.com \
  -e Servers__0__PushEndPoint="https://{0}@gitlab.aiursoft.com" \
  -e Servers__0__DisplayName="Bot" \
  -e Servers__0__UserName=gemini-bot \
  -e Servers__0__UserEmail=bot@aiursoft.com \
  -e Servers__0__ContributionBranch=users/gemini/auto-fix-issue \
  -e Servers__0__Token=glpat-xxx \
  hub.aiursoft.com/aiursoft/geminibot
```

## Run in Microsoft Visual Studio

1. Open the `.sln` file in the project path.
2. Press `F5`.

## How to contribute

There are many ways to contribute to the project: logging bugs, submitting pull requests, reporting issues, and creating suggestions.

Even if you with push rights on the repository, you should create a personal fork and create feature branches there when you need them. This keeps the main repository clean and your workflow cruft out of sight.

We're also interested in your feedback on the future of this project. You can submit a suggestion or feature request through the issue tracker. To make this process more effective, we're asking that these include more information to help define them more clearly.

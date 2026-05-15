# Agent Bot

[![MIT licensed](https://img.shields.io/badge/license-MIT-blue.svg)](https://gitlab.aiursoft.com/aiursoft/agentbot/-/blob/master/LICENSE)
[![Pipeline stat](https://gitlab.aiursoft.com/aiursoft/agentbot/badges/master/pipeline.svg)](https://gitlab.aiursoft.com/aiursoft/agentbot/-/pipelines)
[![Test Coverage](https://gitlab.aiursoft.com/aiursoft/agentbot/badges/master/coverage.svg)](https://gitlab.aiursoft.com/aiursoft/agentbot/-/pipelines)
[![NuGet version (aiursoft.agentbot)](https://img.shields.io/nuget/v/Aiursoft.agentbot.svg)](https://www.nuget.org/packages/Aiursoft.agentbot/)
[![Man hours](https://manhours.aiursoft.com/r/gitlab.aiursoft.com/aiursoft/agentbot.svg)](https://manhours.aiursoft.com/r/gitlab.aiursoft.com/aiursoft/agentbot.html)

## How it works

Agent Bot is designed to autonomously manage the entire lifecycle of software development tasks on GitLab/GitHub. It follows a strictly prioritized workflow to ensure existing work is maintained before starting new tasks.

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
      "UserName": "agent-bot",
      "UserEmail": "bot@aiursoft.com",
      "ContributionBranch": "users/gemini/auto-fix-issue",
      "Token": "",
      "OnlyUpdate": false
    }
  ],
  "AgentBot": {
    "Engine": "Claude",
    "Model": "deepseek-v4-pro",
    "ApiKey": "sk-xxx",
    "ApiEndpoint": "https://api.deepseek.com/anthropic",
    "Reviewer": "senior-dev"
  }
}
```

### Configuration reference

| Key | Env var | Required | Description |
|-----|---------|----------|-------------|
| `Engine` | `AgentBot__Engine` | No | AI backend: `Gemini` (default) or `Claude` |
| `Model` | `AgentBot__Model` | Yes | Model name (e.g. `gemini-3-pro-preview`, `deepseek-v4-pro`) |
| `ApiKey` | `AgentBot__ApiKey` | Yes* | API key for the AI provider |
| `ApiEndpoint` | `AgentBot__ApiEndpoint` | Claude only | Custom API base URL for Anthropic-compatible endpoints |
| `WorkspaceFolder` | `AgentBot__WorkspaceFolder` | No | Temp directory for cloned repos (default: OS temp) |
| `AiTimeout` | `AgentBot__AiTimeout` | No | CLI timeout (default: `00:35:00`) |
| `Reviewer` | `AgentBot__Reviewer` | No | GitLab username to auto-assign as reviewer on new MRs (GitLab only) |
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
AgentBot__WorkspaceFolder=/workspace
AgentBot__Model=gemini-3.1-pro-preview
AgentBot__ApiKey=AIza...
AgentBot__Reviewer=senior-dev
Servers__0__Provider=GitLab
Servers__0__EndPoint=https://gitlab.aiursoft.com
Servers__0__PushEndPoint=https://{0}@gitlab.aiursoft.com
Servers__0__DisplayName=Agent Bot
Servers__0__UserName=agent-bot
Servers__0__UserEmail=gemini@aiursoft.com
Servers__0__ContributionBranch=users/gemini/auto-fix-issue
Servers__0__Token=glpat-...
```

## Installation

Requirements:
1. [.NET 10 SDK](http://dot.net/)

```bash
dotnet tool install --global Aiursoft.AgentBot
```

## Local run

```bash
agent-bot
```

## Docker Deployment

The container runs silently in the background via cron. No ports exposed.

### Docker Run

```bash
docker run -d \
  --name agent-bot \
  -e AgentBot__Engine=Claude \
  -e AgentBot__Model=deepseek-v4-pro \
  -e AgentBot__ApiKey=sk-xxx \
  -e AgentBot__ApiEndpoint=https://api.deepseek.com/anthropic \
  -e AgentBot__Reviewer=senior-dev \
  -e Servers__0__Provider=GitLab \
  -e Servers__0__EndPoint=https://gitlab.aiursoft.com \
  -e Servers__0__PushEndPoint="https://{0}@gitlab.aiursoft.com" \
  -e Servers__0__DisplayName="Bot" \
  -e Servers__0__UserName=agent-bot \
  -e Servers__0__UserEmail=bot@aiursoft.com \
  -e Servers__0__ContributionBranch=users/gemini/auto-fix-issue \
  -e Servers__0__Token=glpat-xxx \
  hub.aiursoft.com/aiursoft/agentbot
```

### Docker Compose

```yaml
version: "3.8"
services:
  agent-bot:
    image: hub.aiursoft.com/aiursoft/agentbot
    restart: unless-stopped
    environment:
      AgentBot__Engine: Claude
      AgentBot__Model: deepseek-v4-pro
      AgentBot__ApiKey: sk-xxx
      AgentBot__ApiEndpoint: https://api.deepseek.com/anthropic
      AgentBot__Reviewer: senior-dev
      Servers__0__Provider: GitLab
      Servers__0__EndPoint: https://gitlab.aiursoft.com
      Servers__0__PushEndPoint: "https://{0}@gitlab.aiursoft.com"
      Servers__0__DisplayName: Bot
      Servers__0__UserName: agent-bot
      Servers__0__UserEmail: bot@aiursoft.com
      Servers__0__ContributionBranch: users/gemini/auto-fix-issue
      Servers__0__Token: glpat-xxx
    volumes:
      - agent-bot-workspace:/workspace
      - agent-bot-logs:/logs

volumes:
  agent-bot-workspace:
  agent-bot-logs:
```

### Kubernetes (CronJob)

```yaml
apiVersion: batch/v1
kind: CronJob
metadata:
  name: agent-bot
spec:
  schedule: "0,30 * * * *"
  jobTemplate:
    spec:
      template:
        spec:
          containers:
            - name: agent-bot
              image: hub.aiursoft.com/aiursoft/agentbot
              env:
                - name: AgentBot__Engine
                  value: Claude
                - name: AgentBot__Model
                  value: deepseek-v4-pro
                - name: AgentBot__ApiKey
                  valueFrom:
                    secretKeyRef:
                      name: agent-bot-secrets
                      key: api-key
                - name: AgentBot__ApiEndpoint
                  value: https://api.deepseek.com/anthropic
                - name: AgentBot__Reviewer
                  value: senior-dev
                - name: Servers__0__Provider
                  value: GitLab
                - name: Servers__0__EndPoint
                  value: https://gitlab.aiursoft.com
                - name: Servers__0__PushEndPoint
                  value: "https://{0}@gitlab.aiursoft.com"
                - name: Servers__0__DisplayName
                  value: Bot
                - name: Servers__0__UserName
                  value: agent-bot
                - name: Servers__0__UserEmail
                  value: bot@aiursoft.com
                - name: Servers__0__ContributionBranch
                  value: users/gemini/auto-fix-issue
                - name: Servers__0__Token
                  valueFrom:
                    secretKeyRef:
                      name: agent-bot-secrets
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
  name: agent-bot-secrets
type: Opaque
stringData:
  api-key: sk-xxx
  gitlab-token: glpat-xxx
```

### Docker Swarm

```bash
docker service create \
  --name agent-bot \
  --restart-condition any \
  -e AgentBot__Engine=Claude \
  -e AgentBot__Model=deepseek-v4-pro \
  -e AgentBot__ApiKey=sk-xxx \
  -e AgentBot__ApiEndpoint=https://api.deepseek.com/anthropic \
  -e AgentBot__Reviewer=senior-dev \
  -e Servers__0__Provider=GitLab \
  -e Servers__0__EndPoint=https://gitlab.aiursoft.com \
  -e Servers__0__PushEndPoint="https://{0}@gitlab.aiursoft.com" \
  -e Servers__0__DisplayName="Bot" \
  -e Servers__0__UserName=agent-bot \
  -e Servers__0__UserEmail=bot@aiursoft.com \
  -e Servers__0__ContributionBranch=users/gemini/auto-fix-issue \
  -e Servers__0__Token=glpat-xxx \
  hub.aiursoft.com/aiursoft/agentbot
```

## Run in Microsoft Visual Studio

1. Open the `.sln` file in the project path.
2. Press `F5`.

## How to contribute

There are many ways to contribute to the project: logging bugs, submitting pull requests, reporting issues, and creating suggestions.

Even if you with push rights on the repository, you should create a personal fork and create feature branches there when you need them. This keeps the main repository clean and your workflow cruft out of sight.

We're also interested in your feedback on the future of this project. You can submit a suggestion or feature request through the issue tracker. To make this process more effective, we're asking that these include more information to help define them more clearly.

FROM hub.aiursoft.com/aiursoft/internalimages/ubuntu

# Set environment variables to ensure Python runs in unbuffered mode and pip does not cache packages or break system packages.
ENV PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_BREAK_SYSTEM_PACKAGES=1

RUN useradd -m bot && \
    echo "bot ALL=(ALL) NOPASSWD: ALL" >> /etc/sudoers.d/bot

# Install regctl for OCI registry interactions, with a fallback mirror in case of issues with GitHub.
RUN (curl -m 50 -L https://github.com/regclient/regclient/releases/latest/download/regctl-linux-amd64 -o regctl || \
    (rm -f regctl && curl -L https://git.aiursoft.com/PublicVault/regclient/releases/download/mirror/regctl-linux-amd64 -o regctl)) && \
    chmod 755 regctl && \
    mv regctl /usr/local/bin/

# Install Node.js, .NET SDK, and other necessary tools. Use the official NodeSource setup script for Node.js installation.
RUN curl -fsSL https://deb.nodesource.com/setup_24.x | bash - && \
    apt-get update && \
    apt-get install -y --no-install-recommends \
      nodejs libgdiplus build-essential bc ffmpeg zip unzip tar gzip \
      iputils-ping net-tools git jq sudo python3-pip python3-venv python3-dev \
      shellcheck iproute2 dotnet10 tmux ripgrep fd-find tree curl wget \
      postgresql-client redis-tools sqlite3 libsqlite3-dev && \
    ln -sf /usr/bin/python3 /usr/local/bin/python && \
    ln -sf /usr/bin/pip3 /usr/local/bin/pip && \
    rm -rf /var/lib/apt/lists/*

# Install Python dependencies commonly needed by AI coding tasks.
RUN pip install PyYAML requests httpx rich python-dotenv

# Set npm registry to a reliable mirror and install necessary global npm packages for TypeScript development and AI CLI tools.
RUN npm config set registry https://npm.aiursoft.com && \
    npm install -g typescript ts-node npm yarn @anthropic-ai/claude-code @google/gemini-cli --loglevel verbose

RUN mkdir -p /workspace /logs && chown bot:bot /workspace /logs && \
    printf 'export HOME=/home/bot\n\
export DOTNET_CLI_HOME=/home/bot/.dotnet\n\
export PATH="$HOME/.dotnet/tools:$PATH"\n\
' > /home/bot/.bashrc && chown bot:bot /home/bot/.bashrc

WORKDIR /app
COPY . .
RUN dotnet build -maxcpucount:1 --configuration Release --no-self-contained *.sln && \
    dotnet pack -maxcpucount:1 --configuration Release *.sln || echo "Some packaging failed!"

RUN dotnet tool install --global Aiursoft.AgentBot --add-source /app/src/Aiursoft.AgentBot/bin/Release/ && \
    dotnet tool install --global dotnet-ef --add-source https://nuget.aiursoft.com/v3/index.json && \
    dotnet tool install --global JetBrains.ReSharper.GlobalTools --add-source https://nuget.aiursoft.com/v3/index.json && \
    dotnet tool install --global dotnet-reportgenerator-globaltool --add-source https://nuget.aiursoft.com/v3/index.json && \
    dotnet tool install --global Aiursoft.Dotlang --add-source https://nuget.aiursoft.com/v3/index.json && \
    dotnet tool install --global Aiursoft.NugetNinja --add-source https://nuget.aiursoft.com/v3/index.json && \
    cp -r /root/.dotnet /home/bot/ && chown -R bot:bot /home/bot/.dotnet

ENV PATH="/home/bot/.dotnet/tools:${PATH}"

# /start.sh — tmux-based launcher, same pattern as the ms.local server.
# tmux session acts as both concurrency guard and attachable debug console.
RUN printf '#!/bin/bash\n\
SESSION_NAME="agent-bot-session"\n\
LOG_DIR="/logs"\n\
LOG_FILE="$LOG_DIR/$(date +%%Y-%%m-%%d_%%H-%%M-%%S).log"\n\
if tmux has-session -t "$SESSION_NAME" 2>/dev/null; then\n\
  echo "$(date): Session $SESSION_NAME already exists. Skipping." >> "$LOG_DIR/cron-skipper.log"\n\
  exit 0\n\
fi\n\
tmux new-session -d -s "$SESSION_NAME" "bash --login -c '\''sudo -E -u bot env HOME=/home/bot DOTNET_CLI_HOME=/home/bot/.dotnet /home/bot/.dotnet/tools/agent-bot 2>&1 | tee $LOG_FILE; echo Bot finished at \$(date)'\''"\n\
echo "$(date): Started tmux session $SESSION_NAME, log: $LOG_FILE" >> "$LOG_DIR/launcher.log"\n\
' > /start.sh && chmod +x /start.sh

# Schedule the bot to run every 30 minutes via cron. /start.sh handles logging via tmux internally.
RUN echo "0,30 * * * * root /start.sh" > /etc/cron.d/agent-bot && \
    chmod 0644 /etc/cron.d/agent-bot

VOLUME /workspace /logs

ENTRYPOINT ["sh", "-c", "printenv | grep -v \"NO_PROXY\" >> /etc/environment && cron -f -L 15"]

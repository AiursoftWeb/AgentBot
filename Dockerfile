FROM hub.aiursoft.com/aiursoft/internalimages/anduinos-internal:latest AS agent-base

ARG TARGETARCH

# Set environment variables to ensure Python runs in unbuffered mode and pip does not cache packages or break system packages.
ENV PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_BREAK_SYSTEM_PACKAGES=1

RUN useradd -m bot && \
    printf 'bot ALL=(ALL) NOPASSWD: ALL\n' > /etc/sudoers.d/bot && \
    chmod 0440 /etc/sudoers.d/bot

# Install regctl for OCI registry interactions, with a fallback mirror in case of issues with GitHub.
RUN (curl --fail --show-error --location --retry 3 --connect-timeout 20 --max-time 120 \
      "https://github.com/regclient/regclient/releases/latest/download/regctl-linux-${TARGETARCH}" \
      --output /usr/local/bin/regctl || \
    curl --fail --show-error --location --retry 3 --connect-timeout 20 --max-time 120 \
      "https://git.aiursoft.com/PublicVault/regclient/releases/download/mirror/regctl-linux-${TARGETARCH}" \
      --output /usr/local/bin/regctl) && \
    chmod 0755 /usr/local/bin/regctl

# Upgrade the base system, install Node.js 24, and add the build, diagnostics,
# multimedia, container, and Linux desktop development tools used by agents.
RUN apt-get update && \
    apt-get upgrade -y && \
    apt-get install -y --no-install-recommends ca-certificates apt-transport-https && \
    curl --fail --show-error --location --retry 3 --connect-timeout 20 \
      https://deb.nodesource.com/setup_24.x --output /tmp/nodesource-setup.sh && \
    bash /tmp/nodesource-setup.sh && \
    rm /tmp/nodesource-setup.sh && \
    apt-get update && \
    apt-get install -y --no-install-recommends \
      nodejs libgdiplus build-essential bc ffmpeg zip unzip tar gzip gcc cmake pkg-config \
      iputils-ping net-tools git jq sudo python3-pip python3-venv python3-dev shellcheck \
      gettext wget apt-utils dpkg appstream librsvg2-bin sassc iproute2 cargo rustc \
      libglib2.0-dev libgtk-4-dev libpcap-dev libvulkan-dev libclang-dev libclang-21-dev \
      glslc spirv-headers libadwaita-1-dev docker.io docker-buildx qemu-user-binfmt-hwe \
      dotnet10 tmux ripgrep fd-find tree curl postgresql-client redis-tools sqlite3 \
      libsqlite3-dev && \
    ln -sf /usr/bin/python3 /usr/local/bin/python && \
    ln -sf /usr/bin/pip3 /usr/local/bin/pip && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/* /var/cache/apt/archives/*

# Install ARM64 target libraries only in the AMD64 worker image. Native ARM64
# image builds do not need a foreign architecture configured.
RUN if [ "$TARGETARCH" = "amd64" ]; then \
      dpkg --add-architecture arm64 && \
      apt-get update && \
      apt-get install -y --no-install-recommends \
        gcc-aarch64-linux-gnu \
        g++-aarch64-linux-gnu \
        libvulkan-dev:arm64 \
        libglib2.0-dev:arm64 \
        libgtk-4-dev:arm64 \
        libadwaita-1-dev:arm64 \
        libpcap-dev:arm64 \
        libstd-rust-dev:arm64 && \
      apt-get clean && \
      rm -rf /var/lib/apt/lists/* /var/cache/apt/archives/*; \
    fi

# Install Python dependencies commonly needed by AI coding tasks.
RUN pip install PyYAML requests httpx rich python-dotenv

# Set npm registry to a reliable mirror and install necessary global npm packages for TypeScript development and AI CLI tools.
RUN npm config set registry https://npm.aiursoft.com && \
    npm install -g typescript ts-node npm yarn @anthropic-ai/claude-code @openai/codex --loglevel verbose && \
    npm cache clean --force && \
    rm -rf /root/.npm

RUN mkdir -p /workspace /logs /data /home/bot/.codex && \
    chmod 0777 /data && \
    chown bot:bot /workspace /logs /home/bot/.codex && \
    printf 'export HOME=/home/bot\n\
export CODEX_HOME=/home/bot/.codex\n\
export DOTNET_CLI_HOME=/home/bot/.dotnet\n\
export PATH="$HOME/.dotnet/tools:$PATH"\n\
' > /home/bot/.bashrc && chown bot:bot /home/bot/.bashrc

FROM agent-base AS app-build

WORKDIR /src
COPY . .
RUN dotnet pack -maxcpucount:1 --configuration Release *.sln

FROM agent-base AS final

ENV HOME=/home/bot \
    CODEX_HOME=/home/bot/.codex \
    DOTNET_CLI_HOME=/home/bot/.dotnet \
    PATH="/home/bot/.dotnet/tools:${PATH}"

COPY --from=app-build /src/src/Aiursoft.AgentBot/bin/Release/*.nupkg /tmp/agentbot-packages/

RUN dotnet tool install --tool-path /home/bot/.dotnet/tools Aiursoft.AgentBot --add-source /tmp/agentbot-packages/ && \
    dotnet tool install --tool-path /home/bot/.dotnet/tools dotnet-ef --add-source https://nuget.aiursoft.com/v3/index.json && \
    printf '%s\n' '<?xml version="1.0" encoding="utf-8"?>' \
      '<configuration><packageSources><clear /><add key="Aiursoft" value="https://nuget.aiursoft.com/v3/index.json" /></packageSources></configuration>' \
      > /tmp/agent-tools.config && \
    dotnet tool install --tool-path /home/bot/.dotnet/tools JetBrains.ReSharper.GlobalTools --configfile /tmp/agent-tools.config -v d && \
    dotnet tool install --tool-path /home/bot/.dotnet/tools dotnet-reportgenerator-globaltool --configfile /tmp/agent-tools.config -v d && \
    dotnet tool install --tool-path /home/bot/.dotnet/tools aiursoft.apkg.client --configfile /tmp/agent-tools.config -v d && \
    dotnet tool install --tool-path /home/bot/.dotnet/tools Aiursoft.Dotlang --configfile /tmp/agent-tools.config -v d && \
    dotnet tool install --tool-path /home/bot/.dotnet/tools Aiursoft.NugetNinja --configfile /tmp/agent-tools.config -v d && \
    rm -rf /tmp/agent-tools.config /tmp/agentbot-packages \
      /home/bot/.nuget /home/bot/.local /root/.nuget /root/.dotnet && \
    chown -R bot:bot /home/bot/.dotnet

WORKDIR /app

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
tmux new-session -d -s "$SESSION_NAME" "bash --login -c '\''sudo -E -u bot env HOME=/home/bot CODEX_HOME=/home/bot/.codex DOTNET_CLI_HOME=/home/bot/.dotnet /home/bot/.dotnet/tools/agent-bot 2>&1 | tee $LOG_FILE; echo Bot finished at \$(date)'\''"\n\
echo "$(date): Started tmux session $SESSION_NAME, log: $LOG_FILE" >> "$LOG_DIR/launcher.log"\n\
' > /start.sh && chmod +x /start.sh

# Schedule the bot to run every 5 minutes via cron. /start.sh handles logging via tmux internally.
RUN echo "*/5 * * * * root /start.sh" > /etc/cron.d/agent-bot && \
    chmod 0644 /etc/cron.d/agent-bot

VOLUME /workspace /logs /home/bot/.codex

ENTRYPOINT ["sh", "-c", "printenv | grep -v \"NO_PROXY\" >> /etc/environment && cron -f -L 15"]

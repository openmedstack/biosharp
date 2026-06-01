# BioSharp Dev Container Dockerfile
# Compatible with both VS Code and JetBrains Rider dev containers
# JetBrains IDEs connect via SSH (sshd feature), VS Code via its remote server

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble

# Avoid interactive prompts during package install
ENV DEBIAN_FRONTEND=noninteractive

# Install dependencies for development and bioinformatics workflows
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    wget \
    ca-certificates \
    git \
    vim \
    less \
    jq \
    make \
     && rm -rf /var/lib/apt/lists/*

# Create vscode user (UID 1000) with sudo access
# Shared between VS Code (remoteUser) and JetBrains (SSH auth)
RUN groupadd -g 1000 vscode && \
    useradd -m -u 1000 -g vscode -s /bin/bash vscode && \
    echo "vscode ALL=(ALL) NOPASSWD:ALL" >> /etc/sudoers

# Map local user (UID/GID on host) to the container vscode user
# so mounted files keep correct ownership. Defaults to 1000 if unset.
ARG HOST_UID=1000
ARG HOST_GID=1000
RUN groupmod -g ${HOST_GID} vscode 2>/dev/null || true && \
    usermod -u ${HOST_UID} vscode

# Set working directory
WORKDIR /workspaces/biosharp

# Do NOT set USER here — root stays the default so sshd (from the
# devcontainer sshd feature) can accept connections for JetBrains.
# VS Code drops to 'vscode' via remoteUser; JetBrains SSHs in as vscode.

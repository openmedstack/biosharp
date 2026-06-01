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

# The base image may already have user 1000 or a "vscode" user.
# Only create the user/group if they don't already exist.
# sshd feature will use whichever user exists, and devcontainer config
# remoteUser tells VS Code which user to drop into on connect.
ARG HOST_UID=1000
ARG HOST_GID=1000
RUN if ! id vscode &>/dev/null; then \
        groupadd -g ${HOST_GID} vscode; \
        useradd -m -u ${HOST_UID} -g vscode -s /bin/bash vscode; \
    fi && \
    echo "vscode ALL=(ALL) NOPASSWD:ALL" >> /etc/sudoers

# Set working directory
WORKDIR /workspaces/biosharp

# Do NOT set USER here — root stays the default so sshd (from the
# devcontainer sshd feature) can accept connections for JetBrains.
# VS Code drops to 'vscode' via remoteUser; JetBrains SSHs in as vscode.

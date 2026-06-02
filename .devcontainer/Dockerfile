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

# The base image may already have a "vscode" user (e.g. ubuntu with UID 1000).
# groupadd without -g auto-assigns a free GID to avoid conflicts.
# useradd without -u lets it auto-assign a free UID if 1000 is taken.
# The ARG hosts are available for IDEs to use as defaults when 1000 is free.
ARG HOST_UID=1000
ARG HOST_GID=1000
RUN (getent group vscode >/dev/null 2>&1 || groupadd vscode) && \
     (id vscode >/dev/null 2>&1 || useradd -m -g vscode -s /bin/bash vscode) && \
    echo "vscode ALL=(ALL) NOPASSWD:ALL" >> /etc/sudoers

# Set working directory
WORKDIR /workspaces/biosharp

# Do NOT set USER here — root stays the default so sshd (from the
# devcontainer sshd feature) can accept connections for JetBrains.
# VS Code drops to 'vscode' via remoteUser; JetBrains SSHs in as vscode.

# Build Bento
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish Bento.csproj -c Release -o /app

# Runtime and Tooling
FROM mcr.microsoft.com/dotnet/sdk:10.0
RUN apt-get update && apt-get install -y --no-install-recommends git git-lfs p7zip-full jq curl ca-certificates sshpass openssh-client rclone xz-utils && apt-get clean && rm -rf /var/lib/apt/lists/*

# The pre-4.1 launcher's SPT.Build project shells out to pwsh in a PostBuild target. Pinned since 7.7.0 is published
# before its powershell.linux-x64 package, which fails the install.
RUN dotnet tool install --tool-path /usr/local/bin PowerShell --version 7.6.10

# For older 3.x builds, install their node versions
RUN set -eux; \
    for version in 20.11.1 22.12.0; do \
        curl -fsSL "https://nodejs.org/dist/v${version}/node-v${version}-linux-x64.tar.xz" -o /tmp/node.tar.xz; \
        mkdir -p "/opt/node/${version}"; \
        tar -xJf /tmp/node.tar.xz -C "/opt/node/${version}" --strip-components=1; \
        rm /tmp/node.tar.xz; \
    done
COPY --from=build /app /opt/bento

# Run. The DOTNET_RUNNING_IN_CONTAINER env variable (set by the base image) switches bento to fully-flagged mode.
ENTRYPOINT ["/opt/bento/bento"]

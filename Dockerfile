# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /source

# Copy solution and projects
COPY FileSync.sln .
COPY FileSync.Common/ FileSync.Common/
COPY FileSync.Server/ FileSync.Server/

# Restore dependencies
RUN dotnet restore FileSync.Server/FileSync.Server.csproj

# Build and publish
RUN dotnet publish FileSync.Server/FileSync.Server.csproj -c Release -o /app/out --no-restore

# Run Stage
FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /app/out .

# Expose server port
EXPOSE 32111

# Define volumes for persistence
VOLUME ["/app/Config", "/app/Data", "/app/Storage"]

ENTRYPOINT ["dotnet", "FileSync.Server.dll"]

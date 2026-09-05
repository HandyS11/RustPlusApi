# Build context is the repository root:
#   docker build -f apps/RustPlusApi.CredentialsWeb/Dockerfile -t rustplusapi-credentials .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# .editorconfig carries the dotnet_diagnostic.* severities that pare the strict analyzer set
# down to what CLAUDE.md documents as "clean" — without it, this publish fails on rules that are
# deliberately silenced (see .editorconfig) rather than absent from the codebase.
COPY Directory.Build.props Directory.Packages.props NuGet.config .editorconfig ./
COPY src/ src/
COPY apps/ apps/

RUN dotnet publish apps/RustPlusApi.CredentialsWeb/RustPlusApi.CredentialsWeb.csproj \
    --configuration Release \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The aspnet image ships a non-root user; APP_UID is its id.
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app .
ENTRYPOINT ["dotnet", "RustPlusApi.CredentialsWeb.dll"]

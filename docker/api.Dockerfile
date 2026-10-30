# Build context: repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so package downloads are cached until a project file changes.
COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/CertCoach.Domain/CertCoach.Domain.csproj src/CertCoach.Domain/
COPY src/CertCoach.Application/CertCoach.Application.csproj src/CertCoach.Application/
COPY src/CertCoach.Infrastructure/CertCoach.Infrastructure.csproj src/CertCoach.Infrastructure/
COPY src/CertCoach.Api/CertCoach.Api.csproj src/CertCoach.Api/
RUN dotnet restore src/CertCoach.Api/CertCoach.Api.csproj

COPY src/ src/
# The OpenAPI document is committed and checked in CI; no need to regenerate it in the image.
RUN dotnet publish src/CertCoach.Api/CertCoach.Api.csproj -c Release -o /app --no-restore -p:OpenApiGenerateDocuments=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Data Protection keys and uploaded PDFs live in volumes, which take this ownership when first created.
RUN mkdir -p /keys /data/uploads && chown $APP_UID /keys /data/uploads

COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "CertCoach.Api.dll"]

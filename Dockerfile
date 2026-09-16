# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY . .

RUN dotnet restore
RUN dotnet publish \
    -c Release \
    -o /app/publish \
    --no-restore

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Trust the Aetheric Forge internal root CA so outbound calls to internal-only services (Keycloak,
# MongoDB, etc.) don't fail with "PartialChain" - .NET on Linux uses the system OpenSSL trust
# store, so this covers HttpClient/SslStream the same way update-ca-certificates does for curl/etc.
COPY docker/certs/aetheric-forge-ca.crt /usr/local/share/ca-certificates/aetheric-forge-ca.crt
RUN update-ca-certificates

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "AethericAdmin.Web.dll"]

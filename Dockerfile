FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/PlayCricket.FixtureReports/PlayCricket.FixtureReports.csproj .
RUN dotnet restore
COPY src/PlayCricket.FixtureReports/ .
RUN dotnet publish -c Release -o /app

# Playwright's .NET image ships .NET runtime + Chromium and all its OS deps,
# pinned to the same version as the Microsoft.Playwright package reference.
FROM mcr.microsoft.com/playwright/dotnet:v1.61.0-noble
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "PlayCricket.FixtureReports.dll"]
CMD ["--upload"]

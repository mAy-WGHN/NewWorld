# Build-Stage: Veröffentlichung des Dienstes (das Testprojekt ist per <Compile Remove> ausgeschlossen)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY BC-Anbindung.csproj ./
RUN dotnet restore BC-Anbindung.csproj
COPY . .
RUN dotnet publish BC-Anbindung.csproj -c Release -o /app/publish --no-restore

# Runtime-Stage: ASP.NET Core hört im Container auf Port 8080
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "BC-Anbindung.dll"]

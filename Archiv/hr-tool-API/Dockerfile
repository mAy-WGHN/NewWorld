# Build-Stage: Veröffentlichung der API (das Testprojekt ist per <Compile Remove> ausgeschlossen)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY HrTool-API.csproj ./
RUN dotnet restore HrTool-API.csproj
COPY . .
RUN dotnet publish HrTool-API.csproj -c Release -o /app/publish --no-restore

# Runtime-Stage: ASP.NET Core hört im Container auf Port 8080
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "HrTool-API.dll"]

# Build-Stage: Veröffentlichung der API (das Testprojekt ist per <Compile Remove> ausgeschlossen)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Portal.csproj ./
RUN dotnet restore Portal.csproj
COPY . .
RUN dotnet publish Portal.csproj -c Release -o /app/publish --no-restore

# Runtime-Stage: ASP.NET Core hört im Container auf Port 8080
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

# FAQ-Bilder liegen außerhalb des Webroots; für persistenten Speicher ein Volume nach /app/var/uploads mounten
# (oder Uploads__FaqPath setzen, z. B. /home/uploads/faq im Azure App Service).
RUN mkdir -p /app/var/uploads/faq && chown -R $APP_UID /app/var
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Portal.dll"]

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/NovaLabMCP.Api/NovaLabMCP.Api.csproj src/NovaLabMCP.Api/
RUN dotnet restore src/NovaLabMCP.Api/NovaLabMCP.Api.csproj
COPY src/NovaLabMCP.Api/ src/NovaLabMCP.Api/
WORKDIR /src/src/NovaLabMCP.Api
RUN dotnet publish NovaLabMCP.Api.csproj --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "NovaLabMCP.Api.dll"]

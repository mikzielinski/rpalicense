FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY api/Ops.License.Api/Ops.License.Api.csproj api/Ops.License.Api/
RUN dotnet restore api/Ops.License.Api/Ops.License.Api.csproj
COPY api/Ops.License.Api/ api/Ops.License.Api/
RUN dotnet publish api/Ops.License.Api/Ops.License.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Ops.License.Api.dll"]

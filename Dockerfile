FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Lumiere/Lumiere.csproj Lumiere/
RUN dotnet restore Lumiere/Lumiere.csproj
COPY Lumiere/ Lumiere/
RUN dotnet publish Lumiere/Lumiere.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Production
USER $APP_UID
EXPOSE 10000
ENTRYPOINT ["dotnet", "Lumiere.dll"]

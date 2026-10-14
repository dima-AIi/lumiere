# ---------- Этап 1: сборка ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Сначала копируем только .csproj и качаем зависимости (слой кэшируется).
COPY Lumiere/Lumiere.csproj ./Lumiere/
RUN dotnet restore Lumiere/Lumiere.csproj

# Копируем остальной код и публикуем в Release.
COPY . .
RUN dotnet publish Lumiere/Lumiere.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---------- Этап 2: рантайм ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish ./

# Render/Fly передают порт через переменную PORT — приложение слушает его (см. Program.cs).
# Локально/при отсутствии PORT слушаем 8080.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Lumiere.dll"]

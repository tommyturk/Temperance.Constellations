# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /source 

# --- Restore ---
# Copy solution and all project files first for layer caching.
# Paths are relative to the build context root (TRADINGBOT.CONDUCTOR).
# Destinations are relative to WORKDIR (/source).
COPY TradingBot.Constellations.sln .
COPY TradingBot.Constellations.csproj .
COPY TradingBot.Data/TradingBot.Data.csproj ./TradingBot.Data/
COPY TradingBot.Services/TradingBot.Services.csproj ./TradingBot.Services/
COPY TradingBot.Utilities/TradingBot.Utilities.csproj ./TradingBot.Utilities/
COPY TradingBot.Settings/TradingBot.Settings.csproj ./TradingBot.Settings/

# Add COPY lines here if you have other referenced projects

# Restore the entire solution - this finds all project references correctly
RUN dotnet restore TradingBot.Constellations.sln

# --- Build ---
# Copy the rest of the source code. Source is the build context (TRADINGBOT.Constellations folder)
COPY . .

# Publish the specific project (TradingBot.Constellations.csproj is directly in /source)
# WORKDIR is still /source
ARG BUILD_CONFIG=Release
# Specify the project file explicitly
RUN dotnet publish TradingBot.Constellations.csproj --no-restore -c $BUILD_CONFIG -o /app/publish

# --- Runtime Image ---
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
EXPOSE 8081
# The DLL name typically matches the assembly name defined in the csproj, usually the project name.
ENTRYPOINT ["dotnet", "TradingBot.Constellations.dll"]
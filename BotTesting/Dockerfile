FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["BotTesting/BotTesting.csproj", "BotTesting/"]
RUN dotnet restore "BotTesting/BotTesting.csproj"
COPY . .
WORKDIR "/src/BotTesting"
RUN dotnet build "BotTesting.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BotTesting.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BotTesting.dll"]

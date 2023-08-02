FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["PieceTableGeneration/PieceTableGeneration.csproj", "PieceTableGeneration/"]
RUN dotnet restore "PieceTableGeneration/PieceTableGeneration.csproj"
COPY . .
WORKDIR "/src/PieceTableGeneration"
RUN dotnet build "PieceTableGeneration.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PieceTableGeneration.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PieceTableGeneration.dll"]

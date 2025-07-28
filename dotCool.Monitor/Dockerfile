FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
#EXPOSE 8080
#EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["dotCool.Monitor/dotCool.Monitor.csproj", "dotCool.Monitor/"]
RUN dotnet restore "dotCool.Monitor/dotCool.Monitor.csproj"
COPY . .
WORKDIR "/src/dotCool.Monitor"
RUN dotnet build "dotCool.Monitor.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "dotCool.Monitor.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "dotCool.Monitor.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["NCM3/NCM3.csproj", "NCM3/"]
RUN dotnet restore "NCM3/NCM3.csproj"
COPY . .
WORKDIR "/src/NCM3"
RUN dotnet build "NCM3.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "NCM3.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
RUN useradd -M --uid 1000 ncmuser && \
    mkdir -p /app/logs && \
    chown -R ncmuser:ncmuser /app

USER ncmuser
ENTRYPOINT ["dotnet", "NCM3.dll"]

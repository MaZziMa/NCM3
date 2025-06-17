@echo off
REM NCM3 Setup Script for Windows
REM This script installs all necessary dependencies and extensions for NCM3

echo ===============================================================
echo                NCM3 - Setup ^& Installation                    
echo ===============================================================
echo.

REM Check for .NET SDK
echo Checking for .NET SDK...
where dotnet >nul 2>nul
IF %ERRORLEVEL% NEQ 0 (
    echo .NET SDK not found. Please install .NET 8.0 SDK from: https://dotnet.microsoft.com/download
    echo After installing .NET, please run this script again.
    pause
    exit /b 1
) ELSE (
    dotnet --version > version.txt
    set /p DOTNET_VERSION=<version.txt
    del version.txt
    echo .NET SDK found (Version: %DOTNET_VERSION%)
    REM Check if version is at least 8.0
    IF "%DOTNET_VERSION:~0,1%" LSS "8" (
        echo Warning: NCM3 requires .NET 8.0 or higher. Current version: %DOTNET_VERSION%
        echo Consider upgrading your .NET SDK.
    )
)

REM Install Entity Framework Tools
echo Installing Entity Framework Core tools...
dotnet tool install --global dotnet-ef || dotnet tool update --global dotnet-ef
echo Entity Framework Core tools installed.

REM Install SQL Server support packages
echo Installing SQL Server packages...
dotnet add NCM3\NCM3.csproj package Microsoft.EntityFrameworkCore.SqlServer
dotnet add NCM3\NCM3.csproj package Microsoft.EntityFrameworkCore.Design
echo SQL Server packages installed.

REM Install SNMP packages
echo Installing SNMP libraries...
dotnet add NCM3\NCM3.csproj package SnmpSharpNet
echo SNMP libraries installed.

REM Install AWS SDK
echo Installing AWS SDK...
dotnet add NCM3\NCM3.csproj package AWSSDK.S3
dotnet add NCM3\NCM3.csproj package AWSSDK.Extensions.NETCore.Setup
echo AWS SDK installed.

REM Setup configuration files
echo Setting up configuration files...
IF NOT EXIST NCM3\appsettings.secrets.json (
    copy NCM3\appsettings.json NCM3\appsettings.secrets.json
    echo Created appsettings.secrets.json template.
    echo Please update appsettings.secrets.json with your actual credentials.
) ELSE (
    echo appsettings.secrets.json already exists.
)

REM Restore and build project
echo Restoring and building NCM3...
dotnet restore
dotnet build
IF %ERRORLEVEL% NEQ 0 (
    echo Build failed. See error messages above.
) ELSE (
    echo NCM3 built successfully.
)

REM Setup database
echo Setting up database...
echo Note: Make sure your connection string is correctly set in appsettings.secrets.json
set /p CHOICE="Do you want to create/update the database now? (Y/N): "
IF /I "%CHOICE%" EQU "Y" (
    dotnet ef database update --project NCM3\NCM3.csproj
    IF %ERRORLEVEL% NEQ 0 (
        echo Database setup failed. Check your connection string.
    ) ELSE (
        echo Database setup completed successfully.
    )
) ELSE (
    echo Database setup skipped. You'll need to run 'dotnet ef database update' manually.
)

REM Check for Docker
echo Checking for Docker...
where docker >nul 2>nul
IF %ERRORLEVEL% EQU 0 (
    echo Docker detected. Setting up Docker environment...
    
    REM Create Dockerfile if it doesn't exist
    IF NOT EXIST Dockerfile (
        echo FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base> Dockerfile
        echo WORKDIR /app>> Dockerfile
        echo EXPOSE 80>> Dockerfile
        echo EXPOSE 443>> Dockerfile
        echo.>> Dockerfile
        echo FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build>> Dockerfile
        echo WORKDIR /src>> Dockerfile
        echo COPY ["NCM3/NCM3.csproj", "NCM3/"]>> Dockerfile
        echo RUN dotnet restore "NCM3/NCM3.csproj">> Dockerfile
        echo COPY . .>> Dockerfile
        echo WORKDIR "/src/NCM3">> Dockerfile
        echo RUN dotnet build "NCM3.csproj" -c Release -o /app/build>> Dockerfile
        echo.>> Dockerfile
        echo FROM build AS publish>> Dockerfile
        echo RUN dotnet publish "NCM3.csproj" -c Release -o /app/publish>> Dockerfile
        echo.>> Dockerfile
        echo FROM base AS final>> Dockerfile
        echo WORKDIR /app>> Dockerfile
        echo COPY --from=publish /app/publish .>> Dockerfile
        echo ENTRYPOINT ["dotnet", "NCM3.dll"]>> Dockerfile
        echo Dockerfile created.
    )
    
    REM Create docker-compose.yml if it doesn't exist
    IF NOT EXIST docker-compose.yml (
        echo version: '3.8'> docker-compose.yml
        echo.>> docker-compose.yml
        echo services:>> docker-compose.yml
        echo   ncm3:>> docker-compose.yml
        echo     build: .>> docker-compose.yml
        echo     container_name: ncm3>> docker-compose.yml
        echo     ports:>> docker-compose.yml
        echo       - "8080:80">> docker-compose.yml
        echo     environment:>> docker-compose.yml
        echo       - ASPNETCORE_ENVIRONMENT=Production>> docker-compose.yml
        echo     volumes:>> docker-compose.yml
        echo       - ./NCM3/appsettings.secrets.json:/app/appsettings.secrets.json>> docker-compose.yml
        echo       - ./Logs:/app/Logs>> docker-compose.yml
        echo       - ./ConfigBackups:/app/ConfigBackups>> docker-compose.yml
        echo     restart: unless-stopped>> docker-compose.yml
        echo Docker Compose file created.
    )
    
    echo To build and run with Docker, use:
    echo   docker-compose build
    echo   docker-compose up -d
) ELSE (
    echo Docker not found. Skipping Docker setup.
)

echo.
echo ===============================================================
echo                NCM3 Setup Completed                          
echo ===============================================================
echo.
echo You can now run the project using:
echo   dotnet run --project NCM3\NCM3.csproj
echo.
echo Remember to update appsettings.secrets.json with your credentials.
echo.
echo Thank you for using NCM3!
echo.

pause

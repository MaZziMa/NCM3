@echo off
echo Starting NCM3 Application on port 5500...
cd %~dp0
dotnet run --project NCM3.csproj --urls="http://localhost:5500"
# NCM3 Run Script for Windows PowerShell
# This script provides various commands to run and manage NCM3

# Variables
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path -Path $scriptPath -ChildPath "NCM3\NCM3.csproj"
$logDir = Join-Path -Path $scriptPath -ChildPath "Logs"
$pidFile = Join-Path -Path $env:TEMP -ChildPath "ncm3.pid"

# Show banner
function Show-Banner {
    Clear-Host
    Write-Host "============================================================" -ForegroundColor Blue
    Write-Host "                NCM3 - Management Console                   " -ForegroundColor Blue
    Write-Host "============================================================" -ForegroundColor Blue
    Write-Host ""
}

# Show usage help
function Show-Help {
    Write-Host "Usage: .\run.ps1 [command]"
    Write-Host ""
    Write-Host "Commands:"
    Write-Host "  start        Start NCM3 (default)" -ForegroundColor Green
    Write-Host "  stop         Stop the running NCM3 instance" -ForegroundColor Green
    Write-Host "  restart      Restart NCM3" -ForegroundColor Green
    Write-Host "  status       Check if NCM3 is running" -ForegroundColor Green
    Write-Host "  logs         View logs" -ForegroundColor Green
    Write-Host "  test         Run tests" -ForegroundColor Green
    Write-Host "  update       Update database to latest migration" -ForegroundColor Green
    Write-Host "  docker       Run with Docker" -ForegroundColor Green
    Write-Host "  help         Show this help" -ForegroundColor Green
    Write-Host ""
}

# Check if NCM3 is running
function Test-IsRunning {    if (Test-Path $pidFile) {
        $processId = Get-Content $pidFile
        try {
            # Just test if the process exists, no need to store it
            Get-Process -Id $processId -ErrorAction Stop | Out-Null
            return $true
        }
        catch {
            # Clean up stale PID file
            Remove-Item $pidFile -Force
            return $false
        }
    }
    return $false
}

# Start NCM3
function Start-Ncm3 {
    Write-Host "Starting NCM3..." -ForegroundColor Blue
      # Check if already running
    if (Test-IsRunning) {
        $processId = Get-Content $pidFile
        Write-Host "NCM3 is already running with PID $processId" -ForegroundColor Yellow
        return
    }
    
    # Make sure log directory exists
    if (-not (Test-Path $logDir)) {
        New-Item -Path $logDir -ItemType Directory -Force | Out-Null
    }
    
    # Check environment
    if (-not $env:ASPNETCORE_ENVIRONMENT) {
        $env:ASPNETCORE_ENVIRONMENT = "Production"
    }
    
    # Set the console log file path
    $consoleLogPath = Join-Path -Path $logDir -ChildPath "ncm3-console.log"
    
    # Start application in background and save PID
    $process = Start-Process -FilePath "dotnet" -ArgumentList "run --project `"$projectPath`" --urls=`"http://localhost:5000`"" -PassThru -RedirectStandardOutput $consoleLogPath -RedirectStandardError $consoleLogPath -NoNewWindow
    
    # Save PID to file
    $process.Id | Out-File $pidFile -Force
    
    Write-Host "NCM3 started with PID $($process.Id) (Environment: $env:ASPNETCORE_ENVIRONMENT)" -ForegroundColor Green
    Write-Host "Console output is being logged to $consoleLogPath"
    
    # Give it a few seconds and check if still running
    Start-Sleep -Seconds 3
    if (Test-IsRunning) {
        Write-Host "NCM3 is now running on http://localhost:5000" -ForegroundColor Green
    }
    else {
        Write-Host "NCM3 failed to start! Check logs for details." -ForegroundColor Red
        Get-Content $consoleLogPath
    }
}

# Stop NCM3
function Stop-Ncm3 {
    Write-Host "Stopping NCM3..." -ForegroundColor Blue
      if (Test-IsRunning) {
        $processId = Get-Content $pidFile
        Write-Host "Stopping NCM3 process with PID $processId"
        
        # Try graceful shutdown first
        Stop-Process -Id $processId -Force
        
        # Remove PID file
        Remove-Item $pidFile -Force
        Write-Host "NCM3 stopped" -ForegroundColor Green
    }
    else {
        Write-Host "NCM3 is not running" -ForegroundColor Yellow
    }
}

# Check status
function Get-Ncm3Status {    if (Test-IsRunning) {
        $processId = Get-Content $pidFile
        Write-Host "NCM3 is running with PID $processId" -ForegroundColor Green
        
        try {
            # Get runtime info
            $process = Get-Process -Id $processId -ErrorAction Stop
            $uptime = (Get-Date) - $process.StartTime
            $memoryMB = [math]::Round($process.WorkingSet / 1MB, 2)
            
            Write-Host "Uptime: $($uptime.Days) days, $($uptime.Hours):$($uptime.Minutes):$($uptime.Seconds)"
            Write-Host "Memory usage: $memoryMB MB"
            Write-Host "URL: http://localhost:5000"
            
            # Check database connection if EF tools are installed
            try {
                Write-Host "Checking database connection..."
                Push-Location $scriptPath
                $connectionStatus = Invoke-Expression "dotnet ef database verify -p `"$projectPath`" --no-build" 2>&1
                if ($LASTEXITCODE -eq 0) {
                    Write-Host "Database connection successful" -ForegroundColor Green
                }
                else {
                    Write-Host "Database connection failed" -ForegroundColor Red
                    Write-Host $connectionStatus
                }
                Pop-Location
            }
            catch {
                Write-Host "Database check failed: $_" -ForegroundColor Red
            }
        }
        catch {
            Write-Host "Error getting process details: $_" -ForegroundColor Red
        }
    }
    else {
        Write-Host "NCM3 is not running" -ForegroundColor Red
    }
}

# Show logs
function Show-Logs {
    Write-Host "Recent logs:" -ForegroundColor Blue
    
    # Check if console log exists
    $consoleLogPath = Join-Path -Path $logDir -ChildPath "ncm3-console.log"
    if (Test-Path $consoleLogPath) {
        Write-Host "=== Console Log ===" -ForegroundColor Yellow
        Get-Content $consoleLogPath -Tail 50
    }
    else {
        Write-Host "No console log found" -ForegroundColor Red
    }
    
    Write-Host ""
    
    # Check for notification logs
    $notificationsDir = Join-Path -Path $logDir -ChildPath "notifications"
    if (Test-Path $notificationsDir) {
        $latestLogFile = Get-ChildItem -Path $notificationsDir -Filter "*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        
        if ($latestLogFile) {
            Write-Host "=== Latest Notification Log ($($latestLogFile.Name)) ===" -ForegroundColor Yellow
            Get-Content $latestLogFile.FullName -Tail 50
        }
    }
    
    Write-Host ""
    Write-Host "Complete logs are available in: $logDir"
}

# Run tests
function Start-Tests {
    Write-Host "Running tests..." -ForegroundColor Blue
    
    Push-Location $scriptPath
    dotnet test Tests/NCM3.Tests.csproj -v n
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "All tests passed!" -ForegroundColor Green
    }
    else {
        Write-Host "Some tests failed. Check the output above for details." -ForegroundColor Red
    }
    Pop-Location
}

# Update database
function Update-Database {
    Write-Host "Updating database..." -ForegroundColor Blue
    
    Push-Location $scriptPath
    dotnet ef database update -p "$projectPath"
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Database updated successfully" -ForegroundColor Green
    }
    else {
        Write-Host "Database update failed. See errors above." -ForegroundColor Red
    }
    Pop-Location
}

# Run with Docker
function Start-Docker {
    Write-Host "Managing Docker containers..." -ForegroundColor Blue
    
    # Check if docker is installed
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Write-Host "Docker is not installed!" -ForegroundColor Red
        return
    }
    
    # Check if docker-compose.yml exists
    $dockerCompose = Join-Path -Path $scriptPath -ChildPath "docker-compose.yml"
    if (-not (Test-Path $dockerCompose)) {
        Write-Host "docker-compose.yml not found!" -ForegroundColor Red
        Write-Host "Run './setup.bat' first to create Docker files" -ForegroundColor Yellow
        return
    }
    
    # Docker submenu
    Write-Host "Select Docker operation:"
    Write-Host "  1) Start containers" -ForegroundColor Green
    Write-Host "  2) Stop containers" -ForegroundColor Green
    Write-Host "  3) View logs" -ForegroundColor Green
    Write-Host "  4) Rebuild and restart" -ForegroundColor Green
    Write-Host "  0) Back" -ForegroundColor Green
    
    $dockerChoice = Read-Host "Enter your choice"
    
    switch ($dockerChoice) {
        "1" {
            Write-Host "Starting Docker containers..."
            docker-compose -f $dockerCompose up -d
        }
        "2" {
            Write-Host "Stopping Docker containers..."
            docker-compose -f $dockerCompose down
        }
        "3" {
            Write-Host "Showing Docker logs (press Ctrl+C to exit)..."
            docker-compose -f $dockerCompose logs -f
        }
        "4" {
            Write-Host "Rebuilding and restarting..."
            docker-compose -f $dockerCompose build
            docker-compose -f $dockerCompose up -d
        }
        "0" {
            return
        }
        default {
            Write-Host "Invalid option" -ForegroundColor Red
        }
    }
}

# Main function
function Start-Main {
    Show-Banner
    
    $command = $args[0]
    if (-not $command) {
        $command = "start"
    }
    
    switch ($command.ToLower()) {
        "start" {
            Start-Ncm3
        }
        "stop" {
            Stop-Ncm3
        }
        "restart" {
            Stop-Ncm3
            Start-Sleep -Seconds 2
            Start-Ncm3
        }
        "status" {
            Get-Ncm3Status
        }
        "logs" {
            Show-Logs
        }
        "test" {
            Start-Tests
        }
        "update" {
            Update-Database
        }
        "docker" {
            Start-Docker
        }
        "help" {
            Show-Help
        }
        default {
            Write-Host "Unknown command: $command" -ForegroundColor Red
            Show-Help
            exit 1
        }
    }
}

# Execute main function with all arguments
Start-Main $args[0]

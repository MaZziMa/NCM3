# Thiết lập thông tin task
$taskName = "NCM3_Database_Backup"
$taskDescription = "Tự động sao lưu cơ sở dữ liệu NCM3 hàng ngày"
$scriptPath = "e:\NCM3\backup-database.bat"

# Kiểm tra quyền admin
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdmin = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "Script này cần quyền Administrator để tạo Scheduled Task" -ForegroundColor Red
    Write-Host "Vui lòng chạy PowerShell với quyền Administrator và thử lại"
    exit 1
}

# Kiểm tra script backup có tồn tại
if (-not (Test-Path $scriptPath)) {
    Write-Host "Không tìm thấy script backup tại: $scriptPath" -ForegroundColor Red
    exit 1
}

try {
    # Tạo action để chạy script
    $action = New-ScheduledTaskAction -Execute "cmd.exe" -Argument "/c $scriptPath"
    
    # Tạo trigger để chạy hàng ngày lúc 2 giờ sáng
    $trigger = New-ScheduledTaskTrigger -Daily -At 2am
    
    # Thiết lập các settings
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -RunOnlyIfNetworkAvailable
    
    # Thiết lập quyền chạy (SYSTEM account để đảm bảo đủ quyền)
    $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
    
    # Đăng ký task
    Register-ScheduledTask -TaskName $taskName `
                         -Action $action `
                         -Trigger $trigger `
                         -Settings $settings `
                         -Principal $principal `
                         -Description $taskDescription `
                         -Force

    Write-Host "Task '$taskName' đã được tạo thành công!" -ForegroundColor Green
    Write-Host "- Chạy hàng ngày lúc 2:00 AM"
    Write-Host "- Sử dụng SYSTEM account"
    Write-Host "- Script path: $scriptPath"
    
    # Hiển thị task vừa tạo
    Get-ScheduledTask -TaskName $taskName | Format-List State, TaskPath, TaskName
}
catch {
    Write-Host "Lỗi khi tạo Scheduled Task: $($_.Exception.Message)" -ForegroundColor Red
}

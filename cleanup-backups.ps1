# Thiết lập thông số
$backupPath = "e:\NCM3\Backups"
$daysToKeep = 30
$minimumBackups = 5  # Số lượng backup tối thiểu cần giữ lại

# Kiểm tra thư mục backup có tồn tại
if (-not (Test-Path $backupPath)) {
    Write-Host "Thư mục backup không tồn tại: $backupPath" -ForegroundColor Red
    exit 1
}

try {
    # Lấy danh sách file backup
    $backupFiles = Get-ChildItem -Path $backupPath -Filter "*.bak" | Sort-Object CreationTime
    
    # Kiểm tra số lượng file backup
    if ($backupFiles.Count -le $minimumBackups) {
        Write-Host "Số lượng file backup ($($backupFiles.Count)) ít hơn hoặc bằng số lượng tối thiểu ($minimumBackups). Không xóa file nào."
        exit 0
    }
    
    # Tính ngày giới hạn
    $limitDate = (Get-Date).AddDays(-$daysToKeep)
    
    # Lọc các file cũ hơn giới hạn
    $filesToDelete = $backupFiles | Where-Object { 
        $_.CreationTime -lt $limitDate -and
        ($backupFiles.Count - $minimumBackups) -gt 0
    }
    
    if ($filesToDelete.Count -eq 0) {
        Write-Host "Không có file backup nào cần xóa"
        exit 0
    }
    
    # Xóa các file
    foreach ($file in $filesToDelete) {
        Remove-Item $file.FullName -Force
        Write-Host "Đã xóa: $($file.Name)"
    }
    
    Write-Host "Đã xóa $($filesToDelete.Count) file backup cũ"
    Write-Host "Còn lại: $((Get-ChildItem -Path $backupPath -Filter *.bak).Count) file backup"
}
catch {
    Write-Host "Lỗi khi dọn dẹp backup: $($_.Exception.Message)" -ForegroundColor Red
}

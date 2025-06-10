@echo off
set backupDir=E:\NCM3\Backups
set backupFile=NCM3_Backup_%date:~-4,4%%date:~-7,2%%date:~-10,2%_%time:~0,2%%time:~3,2%%time:~6,2%.bak
set sqlServer=DESKTOP-4CMFFTN
set dbName=NCM2

echo Creating backup directory if it does not exist...
if not exist "%backupDir%" mkdir "%backupDir%"

echo Creating backup of %dbName% database...
sqlcmd -S %sqlServer% -E -Q "BACKUP DATABASE [%dbName%] TO DISK = N'%backupDir%\%backupFile%' WITH NOFORMAT, NOINIT, NAME = N'%dbName% Full Backup', SKIP, NOREWIND, NOUNLOAD, STATS = 10"

if %ERRORLEVEL% EQU 0 (
    echo.
    echo Backup completed successfully!
    echo Backup file: %backupDir%\%backupFile%
) else (
    echo.
    echo Error during backup! Please check SQL Server connection and permissions.
)

echo.
pause

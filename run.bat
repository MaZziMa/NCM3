@echo off
REM NCM3 Run Script for Windows Command Prompt
REM This is a wrapper for the PowerShell script

echo Starting NCM3 Management Console...

REM Launch the PowerShell script with the provided arguments
powershell.exe -ExecutionPolicy Bypass -File "%~dp0run.ps1" %*

REM If running manually, keep the window open
if not defined CI pause

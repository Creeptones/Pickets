@echo off
if not exist "%~dp0recovery-exit.txt" exit /b 1
set /p PICKETS_TEST_EXIT=<"%~dp0recovery-exit.txt"
exit /b %PICKETS_TEST_EXIT%

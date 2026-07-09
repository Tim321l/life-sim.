@echo off
echo Stopping Blazor WebAssembly Development Server on port 5030...

:: Find the PID of the process listening on port 5030 and kill it
for /f "tokens=5" %%a in ('netstat -aon ^| findstr :5030 ^| findstr LISTENING') do (
    if not "%%a" == "" (
        echo Found dev server process with PID %%a. Terminating...
        taskkill /F /PID %%a >nul 2>&1
    )
)

echo Done. Server stopped.
pause

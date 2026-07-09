@echo off
echo Starting Blazor WebAssembly Development Server on port 5030...
start "HKLifeSim_DevServer" dotnet run --project src/HKLifeSim.Web --urls http://localhost:5030
echo Server starting in a separate window. Access it at http://localhost:5030
pause

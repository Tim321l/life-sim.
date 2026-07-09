@echo off
echo ================================================================
echo           Syncing HK Life Sim to GitHub...
echo ================================================================
echo.

:: 1. Stage all changes
echo [1/3] Staging files...
git add .

:: 2. Commit files with a timestamped message
echo [2/3] Committing files...
git commit -m "Auto-sync: Update game files (%date% %time%)"

:: 3. Push to GitHub
echo [3/3] Pushing to GitHub...
git push origin main

echo.
echo ================================================================
echo Sync complete! Your online code is up to date.
echo ================================================================
echo.
pause

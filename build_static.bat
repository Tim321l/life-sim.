@echo off
echo Building Blazor WebAssembly static assets in Release mode...
dotnet publish src/HKLifeSim.Web/HKLifeSim.Web.csproj -c Release -o publish

:: Create .nojekyll to support GitHub Pages serving _framework assets
type null > publish\wwwroot\.nojekyll

:: Copy index.html to 404.html to support SPA routing fallbacks on static hosts
copy publish\wwwroot\index.html publish\wwwroot\404.html >nul

echo.
echo ================================================================
echo Build complete! Your static site files are in: publish\wwwroot
echo ================================================================
echo.
echo === Recommended Online Hosting Platforms ===
echo.
echo Option A: Vercel (easiest and works out-of-the-box)
echo 1. Create a free account on Vercel (https://vercel.com).
echo 2. Link your GitHub account and import your repository.
echo 3. Vercel will automatically build and host the app using the root `vercel.json`!
echo    (It installs .NET 10, builds the app, and configures routing automatically).
echo.
echo Option B: GitHub Pages
echo 1. Push the contents of the "publish/wwwroot" folder to your gh-pages branch.
echo 2. Note: If your site is hosted at "username.github.io/repo-name/", you must
echo    edit "index.html" and change "<base href="/" />" to "<base href="/repo-name/" />".
echo.
pause

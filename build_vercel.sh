#!/bin/bash
set -e

echo "Downloading .NET install script..."
curl -sSL -O https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh

echo "Installing .NET SDK from global.json..."
./dotnet-install.sh --jsonfile global.json --install-dir ./.dotnet

echo "Configuring environment variables..."
export DOTNET_ROOT=$(pwd)/.dotnet
export PATH=$PATH:$(pwd)/.dotnet
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

echo "Building Blazor WebAssembly static assets..."
dotnet publish src/HKLifeSim.Web/HKLifeSim.Web.csproj -c Release -o publish

echo "Vercel Build finished successfully!"



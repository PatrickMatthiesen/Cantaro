#!/bin/bash
set -e

echo "🔧 Installing Cantaro development dependencies..."

# Install Aspire CLI
echo "📦 Installing Aspire CLI..."
curl -sSL https://aspire.dev/install.sh | bash
echo 'export PATH="$HOME/.aspire/bin:$PATH"' >> ~/.bashrc

# Install EF Core tools
echo "📦 Installing EF Core tools..."
dotnet tool install --global dotnet-ef

# Restore .NET dependencies
echo "📦 Restoring .NET dependencies..."
dotnet restore

# Install Node.js dependencies for the web frontend
echo "📦 Installing Node.js dependencies..."
cd src/Cantaro.Web
npm install
cd ../..

# Install Node.js dependencies for the browser extension
echo "📦 Installing browser extension dependencies..."
cd src/Cantaro.BrowserExtension
npm install
cd ../..

echo "✅ Development environment setup complete!"
echo "🚀 Run 'cd src/Cantaro.AppHost && dotnet run' to start Cantaro with Aspire"

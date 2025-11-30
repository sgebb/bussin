#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build script for bussin - builds both client-js and .NET projects
.PARAMETER Target
    Build target: 'js', 'dotnet', or 'all' (default)
.PARAMETER Configuration
    Build configuration for .NET: 'Debug' or 'Release' (default: Release)
#>

param(
    [ValidateSet('js', 'dotnet', 'all')]
    [string]$Target = 'all',
    
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

function Build-ClientJs {
    Write-Host "🔨 Building client-js (TypeScript → JavaScript)..." -ForegroundColor Cyan
    
    Push-Location client-js
    try {
        # Check if node_modules exists
        if (-not (Test-Path "node_modules")) {
            Write-Host "📦 Installing npm dependencies..." -ForegroundColor Yellow
            npm install --ignore-scripts
        }
        
        Write-Host "🏗️  Building with Vite..." -ForegroundColor Yellow
        npm run build
        
        Write-Host "✅ client-js build complete!" -ForegroundColor Green
        Write-Host "   Output: src/wwwroot/js/servicebus-api.js" -ForegroundColor Gray
    }
    finally {
        Pop-Location
    }
}

function Build-DotNet {
    Write-Host "🔨 Building .NET Blazor WebAssembly..." -ForegroundColor Cyan
    
    Write-Host "🏗️  Running dotnet publish..." -ForegroundColor Yellow
    dotnet publish src/ServiceBusExplorer.Blazor.csproj -c $Configuration
    
    Write-Host "✅ .NET build complete!" -ForegroundColor Green
    Write-Host "   Output: src/bin/$Configuration/net10.0/publish/wwwroot" -ForegroundColor Gray
}

# Main build logic
try {
    $startTime = Get-Date
    
    Write-Host "🚀 Building bussin" -ForegroundColor Magenta
    Write-Host "   Target: $Target" -ForegroundColor Gray
    Write-Host "   Configuration: $Configuration" -ForegroundColor Gray
    Write-Host ""
    
    switch ($Target) {
        'js' {
            Build-ClientJs
        }
        'dotnet' {
            Build-DotNet
        }
        'all' {
            Build-ClientJs
            Write-Host ""
            Build-DotNet
        }
    }
    
    $elapsed = (Get-Date) - $startTime
    Write-Host ""
    Write-Host "🎉 Build completed successfully in $([math]::Round($elapsed.TotalSeconds, 1))s" -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "❌ Build failed: $_" -ForegroundColor Red
    exit 1
}

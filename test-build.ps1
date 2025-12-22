# اسکریپت تست Build محلی
# این اسکریپت تمام نسخه‌ها را build می‌کند بدون نیاز به GitHub Actions

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  تست Build محلی - Ntk.Mikrotik.Tools" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# بررسی وجود .NET SDK
Write-Host "بررسی .NET SDK..." -ForegroundColor Yellow
$dotnetVersion = dotnet --version
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ .NET SDK یافت نشد! لطفاً .NET 8.0 SDK را نصب کنید." -ForegroundColor Red
    exit 1
}
Write-Host "✅ .NET SDK $dotnetVersion یافت شد" -ForegroundColor Green
Write-Host ""

# Restore dependencies
Write-Host "بازگردانی dependencies..." -ForegroundColor Yellow
dotnet restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ خطا در restore dependencies" -ForegroundColor Red
    exit 1
}
Write-Host "✅ Dependencies بازگردانی شد" -ForegroundColor Green
Write-Host ""

# Build پروژه
Write-Host "Build پروژه..." -ForegroundColor Yellow
dotnet build -c Release --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ خطا در build پروژه" -ForegroundColor Red
    exit 1
}
Write-Host "✅ Build موفق بود" -ForegroundColor Green
Write-Host ""

# خواندن version از .csproj
Write-Host "خواندن version از پروژه..." -ForegroundColor Yellow
$csprojContent = Get-Content -Path "Ntk.Mikrotik.Tools.csproj" -Raw
$versionMatch = [regex]::Match($csprojContent, '<Version>(.*?)</Version>')
if ($versionMatch.Success) {
    $projectVersion = $versionMatch.Groups[1].Value
    Write-Host "✅ Version: $projectVersion" -ForegroundColor Green
} else {
    $projectVersion = "1.0.0"
    Write-Host "⚠️ Version یافت نشد، استفاده از پیش‌فرض: $projectVersion" -ForegroundColor Yellow
}
Write-Host ""

# پاک کردن پوشه تست قبلی
if (Test-Path "./test-publish") {
    Write-Host "پاک کردن پوشه تست قبلی..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force "./test-publish"
}

# ایجاد پوشه تست
New-Item -ItemType Directory -Force -Path "./test-publish" | Out-Null

# Build هر نسخه
$builds = @(
    @{Runtime="win-x86"; SelfContained=$true; Name="win-x86-self-contained"},
    @{Runtime="win-x64"; SelfContained=$true; Name="win-x64-self-contained"},
    @{Runtime="win-x86"; SelfContained=$false; Name="win-x86-framework-dependent"},
    @{Runtime="win-x64"; SelfContained=$false; Name="win-x64-framework-dependent"}
)

$successCount = 0
$failCount = 0

foreach ($build in $builds) {
    $outputPath = "./test-publish/$($build.Name)"
    $selfContainedFlag = if ($build.SelfContained) { "--self-contained true" } else { "--self-contained false" }
    
    Write-Host "Build $($build.Name)..." -ForegroundColor Yellow
    Write-Host "  Runtime: $($build.Runtime)" -ForegroundColor Gray
    Write-Host "  Self-contained: $($build.SelfContained)" -ForegroundColor Gray
    Write-Host "  Output: $outputPath" -ForegroundColor Gray
    
    $publishCommand = "dotnet publish -c Release -r $($build.Runtime) $selfContainedFlag -p:PublishSingleFile=false -o `"$outputPath`""
    Invoke-Expression $publishCommand
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✅ Build موفق بود" -ForegroundColor Green
        
        # بررسی وجود فایل exe
        $exePath = Join-Path $outputPath "Ntk.Mikrotik.Tools.exe"
        if (Test-Path $exePath) {
            $fileSize = (Get-Item $exePath).Length / 1MB
            Write-Host "  📦 فایل: $exePath ($([math]::Round($fileSize, 2)) MB)" -ForegroundColor Cyan
        }
        
        $successCount++
    } else {
        Write-Host "  ❌ Build ناموفق" -ForegroundColor Red
        $failCount++
    }
    Write-Host ""
}

# خلاصه نتایج
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  خلاصه نتایج" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Version: $projectVersion" -ForegroundColor White
Write-Host "موفق: $successCount / $($builds.Count)" -ForegroundColor $(if ($successCount -eq $builds.Count) { "Green" } else { "Yellow" })
Write-Host "ناموفق: $failCount" -ForegroundColor $(if ($failCount -eq 0) { "Green" } else { "Red" })
Write-Host ""

if ($successCount -eq $builds.Count) {
    Write-Host "✅ تمام build ها موفق بودند!" -ForegroundColor Green
    Write-Host ""
    Write-Host "فایل‌های build شده در پوشه './test-publish' قرار دارند:" -ForegroundColor Cyan
    Get-ChildItem -Path "./test-publish" -Directory | ForEach-Object {
        Write-Host "  📁 $($_.Name)" -ForegroundColor White
    }
    Write-Host ""
    Write-Host "برای تست Release واقعی، به شاخه 'publish' push کنید:" -ForegroundColor Yellow
    Write-Host "  git checkout publish" -ForegroundColor Gray
    Write-Host "  git push origin publish" -ForegroundColor Gray
} else {
    Write-Host "❌ برخی build ها ناموفق بودند. لطفاً خطاها را بررسی کنید." -ForegroundColor Red
    exit 1
}


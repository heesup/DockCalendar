$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "DockCalendar\DockCalendar.csproj"
$tests = Join-Path $PSScriptRoot "DockCalendar.Tests\DockCalendar.Tests.csproj"

dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }

dotnet build $project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

dotnet test $tests -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet test failed" }

Write-Host "Build and tests completed successfully."

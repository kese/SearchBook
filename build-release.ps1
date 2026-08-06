param(
    [switch]$SkipTests,
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = $PSScriptRoot
$Dotnet = Join-Path $ProjectRoot '.tools\dotnet\dotnet.exe'

if (-not (Test-Path $Dotnet)) {
    throw 'The project-local .NET SDK is missing from .tools\dotnet.'
}

$BuildDirectories = '.dotnet_cli', '.packages', '.tmp', '.appdata', '.localappdata', 'dist'
foreach ($Directory in $BuildDirectories) {
    New-Item -ItemType Directory -Force -Path (Join-Path $ProjectRoot $Directory) | Out-Null
}

$env:DOTNET_CLI_HOME = Join-Path $ProjectRoot '.dotnet_cli'
$env:NUGET_PACKAGES = Join-Path $ProjectRoot '.packages'
$env:TEMP = Join-Path $ProjectRoot '.tmp'
$env:TMP = Join-Path $ProjectRoot '.tmp'
$env:APPDATA = Join-Path $ProjectRoot '.appdata'
$env:LOCALAPPDATA = Join-Path $ProjectRoot '.localappdata'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

if ($SelfContained) {
    & $Dotnet restore (Join-Path $ProjectRoot 'SearchBook.csproj') --runtime win-x64 --source 'https://api.nuget.org/v3/index.json' --disable-parallel --nologo
} else {
    & $Dotnet restore (Join-Path $ProjectRoot 'SearchBook.csproj') --configfile (Join-Path $ProjectRoot 'NuGet.Config') --ignore-failed-sources --nologo
}
if ($LASTEXITCODE -ne 0) { throw 'App restore failed.' }

& $Dotnet restore (Join-Path $ProjectRoot 'Tests\SearchBook.Tests.csproj') --configfile (Join-Path $ProjectRoot 'NuGet.Config') --ignore-failed-sources --nologo
if ($LASTEXITCODE -ne 0) { throw 'Test restore failed.' }

if (-not $SkipTests) {
    & $Dotnet run --project (Join-Path $ProjectRoot 'Tests\SearchBook.Tests.csproj') -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

$PublishDirectory = if ($SelfContained) {
    Join-Path $ProjectRoot 'dist\SearchBook-self-contained-win-x64'
} else {
    Join-Path $ProjectRoot 'dist\SearchBook'
}

$ResolvedDistDirectory = [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot 'dist')) + [System.IO.Path]::DirectorySeparatorChar
$ResolvedPublishDirectory = [System.IO.Path]::GetFullPath($PublishDirectory)
if (-not $ResolvedPublishDirectory.StartsWith($ResolvedDistDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean publish directory outside dist: $ResolvedPublishDirectory"
}
if (Test-Path -LiteralPath $ResolvedPublishDirectory) {
    Remove-Item -LiteralPath $ResolvedPublishDirectory -Recurse -Force
}

if ($SelfContained) {
    & $Dotnet publish (Join-Path $ProjectRoot 'SearchBook.csproj') -c Release --no-restore -o $PublishDirectory --nologo -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false
} else {
    & $Dotnet publish (Join-Path $ProjectRoot 'SearchBook.csproj') -c Release --no-restore -o $PublishDirectory --nologo -p:DebugType=None -p:DebugSymbols=false
}
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$SensitivePublishFiles = @(Get-ChildItem -LiteralPath $PublishDirectory -Recurse -Force -File |
    Where-Object { $_.Name -eq '.env' })
if ($SensitivePublishFiles.Count -ne 0) {
    throw "Refusing to package sensitive .env file: $($SensitivePublishFiles[0].FullName)"
}

New-Item -ItemType Directory -Force -Path (Join-Path $PublishDirectory 'results') | Out-Null

$ZipPath = if ($SelfContained) {
    Join-Path $ProjectRoot 'dist\SearchBook-self-contained-win-x64.zip'
} else {
    Join-Path $ProjectRoot 'dist\SearchBook-win-x64.zip'
}
Compress-Archive -Path (Join-Path $PublishDirectory '*') -DestinationPath $ZipPath -CompressionLevel Optimal -Force
Write-Host "Release ZIP: $ZipPath"
Write-Host "Publish complete: $PublishDirectory"

param(
    [string]$GitHubRepository = '',
    [string]$InnoCompiler = '',
    [string]$NuGetSource = 'https://api.nuget.org/v3/index.json'
)

$ErrorActionPreference = 'Stop'
$installerRoot = $PSScriptRoot
$projectRoot = (Resolve-Path (Join-Path $installerRoot '..')).Path
$updaterRoot = (Resolve-Path (Join-Path $installerRoot '..\Updater')).Path
$stageRoot = Join-Path $installerRoot 'stage'
$outputRoot = Join-Path $installerRoot 'dist'

if (-not $GitHubRepository) {
    $GitHubRepository = (Get-Content -LiteralPath (Join-Path $projectRoot 'update-source.json') -Raw | ConvertFrom-Json).githubRepository
}

if ($GitHubRepository -and $GitHubRepository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw 'GitHubRepository must have the form owner/repository.'
}

if (-not $InnoCompiler) {
    $possible = @(
        'C:\Program Files\Inno Setup 7\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 7\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )
    $InnoCompiler = $possible | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler)) {
    throw 'ISCC.exe was not found. Install the free Inno Setup compiler or pass -InnoCompiler.'
}
$webViewBootstrapper = Join-Path $installerRoot 'deps\MicrosoftEdgeWebview2Setup.exe'
if (-not (Test-Path -LiteralPath $webViewBootstrapper)) {
    New-Item -ItemType Directory -Force -Path (Split-Path $webViewBootstrapper) | Out-Null
    Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $webViewBootstrapper
}
$signature = Get-AuthenticodeSignature -LiteralPath $webViewBootstrapper
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
    throw 'The WebView2 bootstrapper is not signed by Microsoft.'
}

$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Mailcario.csproj') -Raw)).Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'The project version must be major.minor.patch.' }
New-Item -ItemType Directory -Force -Path $stageRoot, $outputRoot | Out-Null
$resolvedStage = [IO.Path]::GetFullPath($stageRoot)
$expectedStage = [IO.Path]::GetFullPath((Join-Path $installerRoot 'stage'))
if ($resolvedStage -ne $expectedStage -or -not $resolvedStage.StartsWith([IO.Path]::GetFullPath($installerRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Unexpected staging path.'
}
Get-ChildItem -LiteralPath $stageRoot -Force | Remove-Item -Recurse -Force

dotnet restore (Join-Path $projectRoot 'Mailcario.csproj') -r win-x64 -s $NuGetSource -p:IncludeLocalOAuthConfig=false
if ($LASTEXITCODE -ne 0) { throw 'Mailcario restore failed.' }
dotnet publish (Join-Path $projectRoot 'Mailcario.csproj') -c Release -r win-x64 --self-contained true --no-restore -p:IncludeLocalOAuthConfig=false -p:PublishSingleFile=false -p:PublishDir="$stageRoot\"
if ($LASTEXITCODE -ne 0) { throw 'Mailcario publish failed.' }
Get-ChildItem -LiteralPath $stageRoot -Filter '*.pdb' -File | Remove-Item -Force

$updaterStage = Join-Path $installerRoot 'updater-stage'
New-Item -ItemType Directory -Force -Path $updaterStage | Out-Null
$resolvedUpdater = [IO.Path]::GetFullPath($updaterStage)
if (-not $resolvedUpdater.StartsWith([IO.Path]::GetFullPath($installerRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected updater staging path.' }
Get-ChildItem -LiteralPath $updaterStage -Force | Remove-Item -Recurse -Force
dotnet restore (Join-Path $updaterRoot 'MailcarioUpdater.csproj') -r win-x64 -s $NuGetSource
if ($LASTEXITCODE -ne 0) { throw 'Update helper restore failed.' }
dotnet publish (Join-Path $updaterRoot 'MailcarioUpdater.csproj') -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishDir="$updaterStage\"
if ($LASTEXITCODE -ne 0) { throw 'Update helper publish failed.' }
Copy-Item -LiteralPath (Join-Path $updaterStage 'Mailcario.Updater.exe') -Destination $stageRoot

@{ githubRepository = $GitHubRepository } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stageRoot 'update-source.json') -Encoding utf8
if (Test-Path -LiteralPath (Join-Path $stageRoot 'oauth-providers.local.json')) { throw 'Private OAuth configuration was included in the installer staging directory.' }

& $InnoCompiler "/DAppVersion=$version" "/DStageDir=$stageRoot" "/DOutputDir=$outputRoot" (Join-Path $installerRoot 'Mailcario.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
$setup = Join-Path $outputRoot "Mailcario-Setup-$version.exe"
if (-not (Test-Path -LiteralPath $setup)) { throw 'The installer was not produced.' }
Get-FileHash -LiteralPath $setup -Algorithm SHA256 | Format-List Path, Hash

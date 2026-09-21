#Requires -Version 7.4
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Documentation.psm1') -Force

$repoRoot = Split-Path (Split-Path $PSScriptRoot)
$readmePath = Join-Path $repoRoot 'README.md'
$sampleDirectory = Join-Path $repoRoot 'src\Valleysoft.DockerfileModel.Samples'
$sampleProject = Join-Path $sampleDirectory 'Valleysoft.DockerfileModel.Samples.csproj'
$libraryProject = Join-Path $repoRoot 'src\Valleysoft.DockerfileModel\Valleysoft.DockerfileModel.csproj'
# MSBuild's assembly resolution can reject package paths over MAX_PATH on Windows.
$workDirectory = Join-Path ([IO.Path]::GetTempPath()) "dfm-docs-$([Guid]::NewGuid().ToString('N'))"
$packageDirectory = Join-Path $workDirectory 'feed'
$packageCache = Join-Path $workDirectory 'packages'
$consumerDirectory = Join-Path $workDirectory 'consumer'

function Invoke-Dotnet {
    param([string[]] $Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$null = New-Item -ItemType Directory -Path $packageDirectory, $consumerDirectory -Force
try {
    Invoke-Dotnet @('pack', $libraryProject, '-c', 'Release', '--no-build', '-o', $packageDirectory)
    $packages = @(Get-ChildItem -LiteralPath $packageDirectory -Filter 'Valleysoft.DockerfileModel.*.nupkg')
    if ($packages.Count -ne 1) {
        throw 'Expected exactly one freshly packed Valleysoft.DockerfileModel package.'
    }
    $package = $packages[0].FullName
    $version = Test-PackageDocumentation $package $readmePath
    & (Join-Path $PSScriptRoot 'Test-Documentation.ps1') -PackagePath $package

    # Copy the actual sample sources to avoid sharing project-reference restore or build state.
    Copy-Item -LiteralPath $sampleProject -Destination $consumerDirectory
    Copy-Item -LiteralPath (Join-Path $repoRoot 'global.json') -Destination $workDirectory
    Get-ChildItem -LiteralPath $sampleDirectory -Filter '*.cs' |
        Copy-Item -Destination $consumerDirectory
    $consumerProject = Join-Path $consumerDirectory 'Valleysoft.DockerfileModel.Samples.csproj'
    $configPath = Join-Path $consumerDirectory 'NuGet.Config'
    $escapedFeed = [Security.SecurityElement]::Escape($packageDirectory)
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$escapedFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="local"><package pattern="Valleysoft.DockerfileModel" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $configPath -Encoding utf8
    $properties = @("-p:DocumentationPackageVersion=$version", '-p:TreatWarningsAsErrors=true')
    Invoke-Dotnet (@('restore', $consumerProject, '--configfile', $configPath,
        '--packages', $packageCache, '--no-http-cache') + $properties)
    Test-ConsumerAssets (Join-Path $consumerDirectory 'obj\project.assets.json') $version $packageCache
    Invoke-Dotnet (@('test', $consumerProject, '-c', 'Release', '--no-restore', '-v', 'minimal',
        '--logger', 'trx', '--results-directory', (Join-Path $repoRoot 'src\test-results\package-samples')) + $properties)
    Write-Host 'Packaged README/XML and package-backed examples verified for both target scenarios.'
}
finally {
    Remove-Item -LiteralPath $workDirectory -Recurse -Force
}

#requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory,
    [string] $ExpectedVersion,
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string] $ExpectedCommit
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $true
$root = Split-Path (Split-Path $PSScriptRoot)
$packageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$packages = @(Get-ChildItem -LiteralPath $packageDirectory -Filter '*.nupkg' -File)
if ($packages.Count -ne 1) {
    throw 'Expected exactly one publication package.'
}
$archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
try {
    $nuspec = $archive.GetEntry('Valleysoft.DockerfileModel.nuspec')
    if ($null -eq $nuspec) {
        throw 'Package is missing its nuspec.'
    }
    $reader = [IO.StreamReader]::new($nuspec.Open())
    try {
        [xml] $metadata = $reader.ReadToEnd()
        $version = $metadata.package.metadata.version
    }
    finally {
        $reader.Dispose()
    }
}
finally {
    $archive.Dispose()
}
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'Package version must not be empty.'
}
if ($ExpectedVersion -and $version -cne $ExpectedVersion) {
    throw "Package version '$version' does not match expected version '$ExpectedVersion'."
}

$previousDirectory = $env:PACKAGE_DIRECTORY
$previousVersion = $env:PACKAGE_VERSION
$previousCommit = $env:PACKAGE_COMMIT
$consumerDirectory = Join-Path ([IO.Path]::GetTempPath()) "package-consumer-$([Guid]::NewGuid().ToString('N'))"
try {
    $env:PACKAGE_DIRECTORY = $packageDirectory
    $env:PACKAGE_VERSION = $version
    $env:PACKAGE_COMMIT = $ExpectedCommit
    dotnet test (Join-Path $root 'src/Valleysoft.DockerfileModel.Tests') -c Release --no-build --no-restore `
        --filter 'FullyQualifiedName~PackageContractTests' --logger 'console;verbosity=normal'

    New-Item -ItemType Directory -Path $consumerDirectory | Out-Null
    Copy-Item -Path (Join-Path $root '.github/package-consumer/*') -Destination $consumerDirectory
    Copy-Item -LiteralPath (Join-Path $root 'global.json') -Destination $consumerDirectory
    $feed = [Security.SecurityElement]::Escape($packageDirectory)
    $cache = Join-Path $consumerDirectory 'packages'
    $configPath = Join-Path $consumerDirectory 'NuGet.Config'
    @"
<configuration>
  <packageSources>
    <clear />
    <add key="publication" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="publication"><package pattern="Valleysoft.DockerfileModel" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $configPath -Encoding utf8
    $project = Join-Path $consumerDirectory 'Consumer.csproj'
    dotnet restore $project --configfile $configPath --packages $cache "-p:PackageVersion=$version"
    dotnet build $project -c Release --no-restore "-p:PackageVersion=$version"
    $installed = @(Get-ChildItem -LiteralPath (Join-Path $cache 'valleysoft.dockerfilemodel') -Recurse -Filter '*.nupkg')
    if ($installed.Count -ne 1 -or
        (Get-FileHash $installed[0].FullName).Hash -ne (Get-FileHash $packages[0].FullName).Hash) {
        throw 'Consumer did not restore the exact publication package.'
    }
}
finally {
    $env:PACKAGE_DIRECTORY = $previousDirectory
    $env:PACKAGE_VERSION = $previousVersion
    $env:PACKAGE_COMMIT = $previousCommit
    if (Test-Path -LiteralPath $consumerDirectory) {
        Remove-Item -LiteralPath $consumerDirectory -Recurse -Force
    }
}

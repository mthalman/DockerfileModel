#Requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PackagePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Documentation.psm1') -Force

$repoRoot = Split-Path (Split-Path $PSScriptRoot)
$readmePath = Join-Path $repoRoot 'README.md'
$testDirectory = Join-Path $repoRoot "artifacts\documentation-tests-$([Guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $testDirectory

function Assert-Rejected {
    param([string] $Name, [scriptblock] $Check, [string] $MessagePattern)

    try {
        & $Check | Out-Null
    }
    catch {
        if ($_.Exception.Message -notlike $MessagePattern) {
            throw "${Name}: unexpected failure: $($_.Exception.Message)"
        }
        return
    }
    throw "${Name}: invalid documentation was accepted."
}

function Set-ZipText {
    param([IO.Compression.ZipArchive] $Archive, [string] $Name, [string] $Text)

    $Archive.GetEntry($Name).Delete()
    $writer = [IO.StreamWriter]::new($Archive.CreateEntry($Name).Open())
    try {
        $writer.Write($Text)
    }
    finally {
        $writer.Dispose()
    }
}

function Test-ChangedPackage {
    param([string] $Name, [scriptblock] $Change, [string] $MessagePattern)

    $changedPath = Join-Path $testDirectory "$Name.nupkg"
    Copy-Item -LiteralPath $PackagePath -Destination $changedPath
    $archive = [IO.Compression.ZipFile]::Open($changedPath, [IO.Compression.ZipArchiveMode]::Update)
    try {
        & $Change $archive
    }
    finally {
        $archive.Dispose()
    }
    Assert-Rejected $Name { Test-PackageDocumentation $changedPath $readmePath } $MessagePattern
}

try {
    $null = Test-PackageDocumentation $PackagePath $readmePath

    foreach ($framework in @('netstandard2.0', 'net10.0')) {
        Test-ChangedPackage "missing-xml-$framework" {
            param($archive)
            $archive.GetEntry("lib/$framework/Valleysoft.DockerfileModel.xml").Delete()
        } '*Expected exactly one package entry*'
    }
    Test-ChangedPackage 'empty-api-docs' {
        param($archive)
        Set-ZipText $archive 'lib/net10.0/Valleysoft.DockerfileModel.xml' @'
<doc><assembly><name>Valleysoft.DockerfileModel</name></assembly><members><member name="T:Valleysoft.DockerfileModel.Dockerfile"><summary> </summary></member></members></doc>
'@
    } '*Missing documentation summary*'
    Test-ChangedPackage 'malformed-xml' {
        param($archive)
        Set-ZipText $archive 'lib/net10.0/Valleysoft.DockerfileModel.xml' '<doc>'
    } '*'
    Test-ChangedPackage 'missing-readme' {
        param($archive)
        $archive.GetEntry('README.md').Delete()
    } '*Expected exactly one package entry: README.md*'
    Test-ChangedPackage 'readme-content' {
        param($archive)
        Set-ZipText $archive 'README.md' 'Outdated README'
    } '*README bytes differ*'
    Test-ChangedPackage 'readme-metadata' {
        param($archive)
        $reader = [IO.StreamReader]::new($archive.GetEntry('Valleysoft.DockerfileModel.nuspec').Open())
        try {
            $nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
        Set-ZipText $archive 'Valleysoft.DockerfileModel.nuspec' $nuspec.Replace(
            '<readme>README.md</readme>', '<readme>Other.md</readme>')
    } '*Package metadata must identify*'
    Write-Host 'Documentation validation rejection checks passed.'
}
finally {
    Remove-Item -LiteralPath $testDirectory -Recurse -Force
}

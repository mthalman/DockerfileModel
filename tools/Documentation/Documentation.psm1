Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-PackageEntry {
    param([IO.Compression.ZipArchive] $Archive, [string] $Name)

    $entries = @($Archive.Entries | Where-Object { $_.FullName -ceq $Name })
    if ($entries.Count -ne 1) {
        throw "Expected exactly one package entry: $Name"
    }
    return $entries[0]
}

function Read-PackageText {
    param([IO.Compression.ZipArchive] $Archive, [string] $Name)

    $reader = [IO.StreamReader]::new((Get-PackageEntry $Archive $Name).Open())
    try {
        return $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}

function Test-PackageDocumentation {
    param(
        [Parameter(Mandatory)][string] $PackagePath,
        [Parameter(Mandatory)][string] $ReadmePath
    )

    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        [xml] $nuspec = Read-PackageText $archive 'Valleysoft.DockerfileModel.nuspec'
        $metadata = $nuspec.package.metadata
        $readme = $metadata.SelectSingleNode("*[local-name()='readme']")
        if ($metadata.id -cne 'Valleysoft.DockerfileModel' -or
            $null -eq $readme -or $readme.InnerText -cne 'README.md') {
            throw 'Package metadata must identify Valleysoft.DockerfileModel and README.md.'
        }
        $stream = (Get-PackageEntry $archive 'README.md').Open()
        $buffer = [IO.MemoryStream]::new()
        try {
            $stream.CopyTo($buffer)
            $packedHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($buffer.ToArray()))
            $sourceHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
                [IO.File]::ReadAllBytes($ReadmePath)))
            if ($packedHash -cne $sourceHash) {
                throw 'The packaged README bytes differ from the repository README.'
            }
        }
        finally {
            $stream.Dispose()
            $buffer.Dispose()
        }

        $requiredMembers = @(
            'T:Valleysoft.DockerfileModel.Dockerfile',
            'M:Valleysoft.DockerfileModel.Dockerfile.Parse(System.String)',
            'P:Valleysoft.DockerfileModel.Dockerfile.Items',
            'P:Valleysoft.DockerfileModel.ResolutionOptions.UpdateInline',
            'P:Valleysoft.DockerfileModel.ResolutionOptions.RemoveEscapeCharacters',
            'T:Valleysoft.DockerfileModel.DockerfileBuilder',
            'P:Valleysoft.DockerfileModel.DockerfileBuilder.DefaultNewLine',
            'P:Valleysoft.DockerfileModel.DockerfileBuilder.EscapeChar',
            'T:Valleysoft.DockerfileModel.StagesView',
            'P:Valleysoft.DockerfileModel.Stage.Items',
            'T:Valleysoft.DockerfileModel.ImageName',
            'P:Valleysoft.DockerfileModel.Tokens.AggregateToken.Tokens'
        )
        foreach ($framework in @('netstandard2.0', 'net10.0')) {
            $null = Get-PackageEntry $archive "lib/$framework/Valleysoft.DockerfileModel.dll"
            [xml] $documentation = Read-PackageText $archive "lib/$framework/Valleysoft.DockerfileModel.xml"
            if ($documentation.doc.assembly.name -cne 'Valleysoft.DockerfileModel') {
                throw "Unexpected XML documentation assembly for $framework."
            }
            $members = @($documentation.doc.members.member)
            foreach ($name in $requiredMembers) {
                $matching = @($members | Where-Object { $_.name -ceq $name })
                if ($matching.Count -ne 1) {
                    throw "Missing documentation summary for $framework member $name"
                }
                $summary = $matching[0].SelectSingleNode('summary')
                if ($null -eq $summary -or [string]::IsNullOrWhiteSpace($summary.InnerText)) {
                    throw "Missing documentation summary for $framework member $name"
                }
            }
            $resolutionMembers = @($members | Where-Object {
                $_.name.StartsWith('M:Valleysoft.DockerfileModel.Dockerfile.ResolveVariables',
                    [StringComparison]::Ordinal)
            })
            if ($resolutionMembers.Count -ne 2) {
                throw "Both Dockerfile.ResolveVariables overloads must be documented for $framework."
            }
            foreach ($member in $resolutionMembers) {
                foreach ($element in @('summary', 'returns', 'remarks')) {
                    $node = $member.SelectSingleNode($element)
                    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
                        throw "Missing $element for $($member.name) in $framework."
                    }
                }
            }
        }
        return [string] $metadata.version
    }
    finally {
        $archive.Dispose()
    }
}

function Test-ConsumerAssets {
    param(
        [Parameter(Mandatory)][string] $AssetsPath,
        [Parameter(Mandatory)][string] $Version,
        [Parameter(Mandatory)][string] $PackageCache
    )

    $assets = Get-Content -LiteralPath $AssetsPath -Raw | ConvertFrom-Json -AsHashtable
    $key = "Valleysoft.DockerfileModel/$Version"
    if (!$assets.libraries.ContainsKey($key) -or $assets.libraries[$key].type -cne 'package') {
        throw "The consumer did not resolve the expected package: $key"
    }
    if (@($assets.libraries.Values | Where-Object { $_.type -eq 'project' }).Count -ne 0) {
        throw 'Package-backed samples must not resolve any project references.'
    }
    $folders = @($assets.packageFolders.Keys)
    if ($folders.Count -ne 1 -or
        [IO.Path]::TrimEndingDirectorySeparator($folders[0]) -ne
        [IO.Path]::TrimEndingDirectorySeparator($PackageCache)) {
        throw 'The consumer must use only its isolated package cache.'
    }
    foreach ($framework in @('net8.0', 'net10.0')) {
        $assetFramework = if ($framework -eq 'net8.0') { 'netstandard2.0' } else { 'net10.0' }
        $expected = "lib/$assetFramework/Valleysoft.DockerfileModel.dll"
        foreach ($kind in @('compile', 'runtime')) {
            $paths = @($assets.targets[$framework][$key][$kind].Keys)
            if ($paths.Count -ne 1 -or $paths[0] -cne $expected) {
                throw "Unexpected $kind asset for ${framework}: $($paths -join ', ')"
            }
        }
    }
}

Export-ModuleMember -Function Test-PackageDocumentation, Test-ConsumerAssets

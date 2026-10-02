[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot,
    [switch]$SkipNegativeFixtures
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\verify-plan023'))
$packagePath = [System.IO.Path]::GetFullPath($PackageRoot)
$artifactPrefix = $artifactRoot.TrimEnd('\') + '\'
if (-not $packagePath.StartsWith($artifactPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw [System.IO.InvalidDataException]::new('PackageRoot must be under artifacts/verify-plan023.')
}
if (-not (Test-Path -LiteralPath $packagePath -PathType Container)) {
    throw [System.IO.FileNotFoundException]::new('PackageRoot does not exist.', $packagePath)
}

$sourceManifestSha256 = 'FABD411EA235BAD90AE1ADA128A8F2623E8E774202A0F2A86C958DF6171005CE'
$sourceManifestRows = 2135
$publishedInstallerSha256 = '0BA6607307F8C9F43A596FC879756FBB006B0D6DCF5777A0AEC7BAFFB9245A65'
$requiredFiles = @(
    'MinePack.Installer.exe', 'MinePack.Core.pdb', 'MinePack.Installer.pdb',
    'README.md', 'README.ru.md', 'README.zh-CN.md', 'LICENSE',
    'Assets/Monocraft-OFL.txt', 'releases/test-pack/README.md',
    'releases/vanilla-2-plus/README.md', 'third-party/README.md',
    'third-party/yungs-sources/source-snapshot-manifest.txt',
    'third-party/yungs-sources/artifact-mapping.txt',
    'third-party/yungs-sources/LICENSE-GPL-3.0.txt',
    'third-party/yungs-sources/LICENSE-LGPL-3.0.txt'
)

function Get-CanonicalChild([string]$Root, [string]$RelativePath) {
    $path = [System.IO.Path]::GetFullPath((Join-Path $Root ($RelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))))
    $prefix = [System.IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    if (-not $path.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw [System.IO.InvalidDataException]::new("Path escapes its package root: $RelativePath")
    }
    return $path
}

function Assert-RequiredFiles([string]$Root, [string[]]$Paths) {
    foreach ($relative in $Paths) {
        $path = Get-CanonicalChild $Root $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw [System.IO.InvalidDataException]::new("Required package file is missing: $relative")
        }
    }
}

function Assert-FileHash([string]$Path, [string]$Algorithm, [string]$Expected) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw [System.IO.InvalidDataException]::new("Hash input is missing: $Path")
    }
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm $Algorithm).Hash
    if ($actual -cne $Expected) {
        throw [System.IO.InvalidDataException]::new("$Algorithm mismatch: $Path")
    }
}

function Assert-ExactNames([string]$Directory, [string[]]$Expected) {
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
        throw [System.IO.InvalidDataException]::new("Required package directory is missing: $Directory")
    }
    $actual = @(Get-ChildItem -LiteralPath $Directory -Force | ForEach-Object Name)
    if ($actual.Count -ne $Expected.Count) {
        throw [System.IO.InvalidDataException]::new("Unexpected item count in package directory: $Directory")
    }
    $actualSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $actual) { [void]$actualSet.Add($name) }
    foreach ($name in $Expected) {
        if (-not $actualSet.Contains($name)) {
            throw [System.IO.InvalidDataException]::new("Unexpected or missing item in package directory: $Directory")
        }
    }
}

function Assert-SourceKit([string]$Root) {
    $sourceRoot = Get-CanonicalChild $Root 'third-party/yungs-sources'
    $manifestPath = Join-Path $sourceRoot 'source-snapshot-manifest.txt'
    Assert-FileHash $manifestPath 'SHA256' $sourceManifestSha256

    $expectedDirectories = @(
        'source-yungs-api', 'source-yungs-desert-temples', 'source-yungs-desert-temples-minepack2',
        'source-yungs-better-dungeons', 'source-yungs-better-jungle-temples',
        'source-yungs-better-mineshafts', 'source-yungs-better-fortresses',
        'source-yungs-better-strongholds', 'yungsapi-local-api-marker'
    )
    $expectedRootFiles = @('artifact-mapping.txt', 'LICENSE-GPL-3.0.txt', 'LICENSE-LGPL-3.0.txt', 'source-snapshot-manifest.txt')
    Assert-ExactNames $sourceRoot ($expectedDirectories + $expectedRootFiles)

    $allEntries = [System.Collections.Generic.List[string]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $lines = [System.IO.File]::ReadAllLines($manifestPath)
    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line.Split('|')
        if ($parts.Length -ne 3 -or $parts[0].Contains('\') -or $parts[0].StartsWith('/') -or
            $parts[0] -match '(^|/)\.\.(/|$)' -or $parts[0] -match '^[A-Za-z]:') {
            throw [System.IO.InvalidDataException]::new('Source manifest contains an invalid relative path row.')
        }
        $relative = $parts[0]
        if (-not $seen.Add($relative)) {
            throw [System.IO.InvalidDataException]::new("Duplicate source manifest path: $relative")
        }
        if ($relative -match '(?i)(^|/)(\.git|\.gradle|build|bin|obj|cache|caches|__pycache__)(/|$)|\.pdf$|(^|/)(credentials|secrets?)(/|$)') {
            throw [System.IO.InvalidDataException]::new("Forbidden generated/private path in source kit: $relative")
        }
        if ($parts[1] -notmatch '^\d+$' -or $parts[2] -notmatch '^[A-Fa-f0-9]{64}$') {
            throw [System.IO.InvalidDataException]::new("Invalid size or SHA-256 in source manifest row: $relative")
        }

        $path = Get-CanonicalChild $sourceRoot $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw [System.IO.InvalidDataException]::new("Source kit file is missing: $relative")
        }
        $item = Get-Item -LiteralPath $path -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or
            $item.Length -ne [long]$parts[1]) {
            throw [System.IO.InvalidDataException]::new("Source kit file type or size mismatch: $relative")
        }
        Assert-FileHash $path 'SHA256' $parts[2]
        $allEntries.Add($relative)
    }
    if ($allEntries.Count -ne $sourceManifestRows) {
        throw [System.IO.InvalidDataException]::new("Expected $sourceManifestRows source manifest rows; found $($allEntries.Count).")
    }

    $actualFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Force)
    $actualRelative = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($item in $actualFiles) {
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw [System.IO.InvalidDataException]::new('Source kit contains a reparse-point file.')
        }
        $relative = [System.IO.Path]::GetRelativePath($sourceRoot, $item.FullName).Replace('\', '/')
        [void]$actualRelative.Add($relative)
    }
    if ($actualFiles.Count -ne ($sourceManifestRows + 1) -or $actualRelative.Count -ne $actualFiles.Count) {
        throw [System.IO.InvalidDataException]::new('Source kit contains unexpected files or duplicate case-insensitive paths.')
    }
    foreach ($relative in $allEntries) {
        if (-not $actualRelative.Contains($relative)) {
            throw [System.IO.InvalidDataException]::new("Source manifest file is absent from actual inventory: $relative")
        }
    }
    if (-not $actualRelative.Contains('source-snapshot-manifest.txt')) {
        throw [System.IO.InvalidDataException]::new('Source snapshot manifest is absent from its own package inventory.')
    }

    $licensePaths = @(
        'source-yungs-api/LICENSE', 'source-yungs-desert-temples/LICENSE',
        'source-yungs-desert-temples-minepack2/LICENSE', 'source-yungs-better-dungeons/LICENSE',
        'source-yungs-better-jungle-temples/LICENSE', 'source-yungs-better-mineshafts/LICENSE',
        'source-yungs-better-fortresses/LICENSE', 'source-yungs-better-strongholds/LICENSE'
    )
    Assert-RequiredFiles $sourceRoot ($licensePaths + @('LICENSE-GPL-3.0.txt', 'LICENSE-LGPL-3.0.txt'))

    $mappingPath = Join-Path $sourceRoot 'artifact-mapping.txt'
    $mappingRows = [System.Collections.Generic.List[object]]::new()
    foreach ($line in [System.IO.File]::ReadAllLines($mappingPath)) {
        if ($line -match '^(?<snapshot>[^|]+)\|(?<artifact>[^|]+)\|(?<hash>[A-Fa-f0-9]{128})$') {
            $mappingRows.Add([pscustomobject]@{
                Snapshot = $Matches.snapshot
                Artifact = $Matches.artifact
                Sha512 = $Matches.hash.ToUpperInvariant()
            })
        }
    }
    $expectedSnapshots = @(
        'source-yungs-api', 'source-yungs-desert-temples', 'source-yungs-desert-temples-minepack2',
        'source-yungs-better-dungeons', 'source-yungs-better-jungle-temples',
        'source-yungs-better-mineshafts', 'source-yungs-better-fortresses', 'source-yungs-better-strongholds'
    )
    $actualSnapshots = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($row in $mappingRows) { [void]$actualSnapshots.Add($row.Snapshot) }
    if ($mappingRows.Count -ne 8 -or $actualSnapshots.Count -ne 8 -or
        @($expectedSnapshots | Where-Object { -not $actualSnapshots.Contains($_) }).Count -ne 0) {
        throw [System.IO.InvalidDataException]::new('Source-to-artifact mapping must contain exactly eight versioned snapshots.')
    }

    foreach ($row in $mappingRows) {
        $jarName = [System.IO.Path]::GetFileName($row.Artifact)
        if ($row.Snapshot -eq 'source-yungs-desert-temples-minepack2') {
            $archiveRelative = 'releases/vanilla-2-plus/vanilla-2-plus-0.19.7.mrpack'
            $entryName = "overrides/mods/$jarName"
        } else {
            $archiveRelative = 'releases/vanilla-2-plus/vanilla-2-plus-0.19.6.mrpack'
            $entryName = $row.Artifact
        }
        $archivePath = Get-CanonicalChild $Root $archiveRelative
        $fileStream = [System.IO.File]::OpenRead($archivePath)
        try {
            $archive = [System.IO.Compression.ZipArchive]::new($fileStream, [System.IO.Compression.ZipArchiveMode]::Read, $false)
            try {
                $entry = $archive.GetEntry($entryName)
                if ($null -eq $entry) {
                    throw [System.IO.InvalidDataException]::new("Mapped pinned JAR is absent from archive: $entryName")
                }
                $entryStream = $entry.Open()
                $sha = [System.Security.Cryptography.SHA512]::Create()
                try {
                    $entryHash = [Convert]::ToHexString($sha.ComputeHash($entryStream))
                } finally {
                    $entryStream.Dispose()
                    $sha.Dispose()
                }
                if ($entryHash -cne $row.Sha512) {
                    throw [System.IO.InvalidDataException]::new("Source-to-binary SHA-512 mismatch for $entryName")
                }
            } finally { $archive.Dispose() }
        } finally { $fileStream.Dispose() }
    }
    return $allEntries.Count
}

function Get-ReleaseArchives([string]$SourceFile, [string]$RelativeDirectory) {
    $text = [System.IO.File]::ReadAllText($SourceFile)
    $result = [System.Collections.Generic.List[object]]::new()
    $fileMatches = [System.Text.RegularExpressions.Regex]::Matches(
        $text, 'public const string (?<field>[A-Za-z0-9]*ArtifactFileName)\s*=\s*"(?<file>[^"]+)"\s*;')
    foreach ($match in $fileMatches) {
        $field = $match.Groups['field'].Value
        $hashField = $field -replace 'FileName$', 'Sha512'
        $hashPattern = 'public const string ' + [System.Text.RegularExpressions.Regex]::Escape($hashField) + '\s*=\s*"(?<hash>[A-Fa-f0-9]{128})"\s*;'
        $hashMatch = [System.Text.RegularExpressions.Regex]::Match($text, $hashPattern)
        if (-not $hashMatch.Success) {
            throw [System.IO.InvalidDataException]::new("Release source has no matching SHA-512 constant: $field")
        }
        $fileName = $match.Groups['file'].Value
        if ([System.IO.Path]::GetFileName($fileName) -cne $fileName) {
            throw [System.IO.InvalidDataException]::new("Release filename constant is not a simple filename: $fileName")
        }
        $result.Add([pscustomobject]@{
            RelativePath = "$RelativeDirectory/$fileName"
            Sha512 = $hashMatch.Groups['hash'].Value.ToUpperInvariant()
            Field = $field
        })
    }
    return $result
}

function Assert-LocalLinks([string]$Root, [string[]]$MarkdownPaths) {
    $linkPattern = '(?<!\!)\[[^\]]+\]\((?<target>[^)]+)\)'
    foreach ($relative in $MarkdownPaths) {
        $file = Get-CanonicalChild $Root $relative
        $content = [System.IO.File]::ReadAllText($file)
        foreach ($match in [System.Text.RegularExpressions.Regex]::Matches($content, $linkPattern)) {
            $target = $match.Groups['target'].Value.Trim()
            if ($target.Contains(' ')) { $target = ($target -split '\s+', 2)[0] }
            $target = $target.Trim('<', '>')
            if ($target -match '^(?i)(https?://|mailto:|tel:|#|//)') { continue }
            $target = ($target -split '[?#]', 2)[0]
            if ([string]::IsNullOrWhiteSpace($target)) { continue }
            $target = [System.Uri]::UnescapeDataString($target).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
            $documentDirectory = [System.IO.Path]::GetDirectoryName($file)
            $resolved = [System.IO.Path]::GetFullPath((Join-Path $documentDirectory $target))
            $rootPrefix = [System.IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
            if (-not $resolved.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw [System.IO.InvalidDataException]::new("Markdown link escapes package root: $relative")
            }
            if (-not (Test-Path -LiteralPath $resolved)) {
                throw [System.IO.InvalidDataException]::new("Local Markdown target is missing: $relative -> $target")
            }
        }
    }
}

function Assert-NoReparsePoints([string]$Root) {
    foreach ($item in Get-ChildItem -LiteralPath $Root -Recurse -Force) {
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw [System.IO.InvalidDataException]::new('Package contains a reparse point.')
        }
    }
}

function Test-NegativeFixtures([string]$ValidRoot) {
    $fixtureRoot = Join-Path $artifactRoot ('validator-selftests\' + [Guid]::NewGuid().ToString('N'))
    [void](New-Item -ItemType Directory -Path $fixtureRoot)
    $fixturePackage = Join-Path $fixtureRoot 'package'
    Copy-Item -LiteralPath $ValidRoot -Destination $fixturePackage -Recurse
    $pwshPath = (Get-Command pwsh -CommandType Application -ErrorAction Stop).Source
    $validatorPath = $PSCommandPath

    foreach ($case in @(
        @{ Relative = 'README.md'; Failure = 'Required package file is missing: README.md'; Label = 'missing README' },
        @{ Relative = 'LICENSE'; Failure = 'Required package file is missing: LICENSE'; Label = 'missing LICENSE' },
        @{ Relative = 'third-party/yungs-sources/source-snapshot-manifest.txt'; Failure = 'source-snapshot-manifest.txt'; Label = 'missing source manifest' }
    )) {
        $path = Join-Path $fixturePackage $case.Relative
        Remove-Item -LiteralPath $path
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new($pwshPath)
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        foreach ($argument in @('-NoProfile', '-File', $validatorPath, '-PackageRoot', $fixturePackage, '-SkipNegativeFixtures')) {
            [void]$startInfo.ArgumentList.Add($argument)
        }
        $process = [System.Diagnostics.Process]::Start($startInfo)
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $childOutput = $stdoutTask.GetAwaiter().GetResult() + $stderrTask.GetAwaiter().GetResult()
        $exitCode = $process.ExitCode
        $process.Dispose()
        if ($exitCode -eq 0 -or -not $childOutput.Contains($case.Failure, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw [System.InvalidOperationException]::new("CLI validator did not reject $($case.Label) as expected (exit $exitCode).")
        }
        Copy-Item -LiteralPath (Join-Path $ValidRoot $case.Relative) -Destination $path
        Write-Host "PASS: CLI validator rejects $($case.Label) (exit=$exitCode)"
    }

    $archiveRelative = 'releases/vanilla-2-plus/vanilla-2-plus-0.19.7.mrpack'
    $archivePath = Join-Path $fixturePackage $archiveRelative
    $stream = [System.IO.File]::Open($archivePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    try {
        $originalByte = $stream.ReadByte()
        $stream.Position = 0
        $stream.WriteByte([byte]($originalByte -bxor 0xFF))
        $stream.Flush($true)
    } finally { $stream.Dispose() }
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($pwshPath)
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @('-NoProfile', '-File', $validatorPath, '-PackageRoot', $fixturePackage, '-SkipNegativeFixtures')) {
        [void]$startInfo.ArgumentList.Add($argument)
    }
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $childOutput = $stdoutTask.GetAwaiter().GetResult() + $stderrTask.GetAwaiter().GetResult()
    $exitCode = $process.ExitCode
    $process.Dispose()
    if ($exitCode -eq 0 -or -not $childOutput.Contains('SHA512 mismatch', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw [System.InvalidOperationException]::new("CLI validator did not reject a pinned archive hash mismatch as expected (exit $exitCode).")
    }
    Write-Host "PASS: CLI validator rejects pinned archive hash mismatch (exit=$exitCode)"
}

Assert-NoReparsePoints $packagePath
Assert-RequiredFiles $packagePath $requiredFiles

$expectedRoot = @('Assets', 'LICENSE', 'MinePack.Core.pdb', 'MinePack.Installer.exe', 'MinePack.Installer.pdb', 'README.md', 'README.ru.md', 'README.zh-CN.md', 'releases', 'third-party')
Assert-ExactNames $packagePath $expectedRoot
Assert-ExactNames (Join-Path $packagePath 'Assets') @('Monocraft-OFL.txt')
Assert-ExactNames (Join-Path $packagePath 'releases') @('test-pack', 'vanilla-2-plus')
Assert-ExactNames (Join-Path $packagePath 'third-party') @('README.md', 'yungs-sources')

$releaseList = [System.Collections.Generic.List[object]]::new()
foreach ($release in Get-ReleaseArchives (Join-Path $repoRoot 'src\MinePack.Core\TestPackRelease.cs') 'releases/test-pack') { $releaseList.Add($release) }
foreach ($release in Get-ReleaseArchives (Join-Path $repoRoot 'src\MinePack.Core\Vanilla2PlusRelease.cs') 'releases/vanilla-2-plus') { $releaseList.Add($release) }
if ($releaseList.Count -ne 27) {
    throw [System.IO.InvalidDataException]::new("Expected 27 pinned historical/current archives; found $($releaseList.Count).")
}
$testPackNames = [System.Collections.Generic.List[string]]::new()
$testPackNames.Add('README.md')
$frontierNames = [System.Collections.Generic.List[string]]::new()
$frontierNames.Add('README.md')
foreach ($release in $releaseList) {
    if ($release.RelativePath.StartsWith('releases/test-pack/', [System.StringComparison]::OrdinalIgnoreCase)) {
        $testPackNames.Add([System.IO.Path]::GetFileName($release.RelativePath))
    } else {
        $frontierNames.Add([System.IO.Path]::GetFileName($release.RelativePath))
    }
}
Assert-ExactNames (Join-Path $packagePath 'releases/test-pack') $testPackNames.ToArray()
Assert-ExactNames (Join-Path $packagePath 'releases/vanilla-2-plus') $frontierNames.ToArray()
$seenArchives = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($release in $releaseList) {
    if (-not $seenArchives.Add($release.RelativePath)) {
        throw [System.IO.InvalidDataException]::new("Duplicate archive pin: $($release.RelativePath)")
    }
    $sourceArchive = Get-CanonicalChild $repoRoot $release.RelativePath
    $packageArchive = Get-CanonicalChild $packagePath $release.RelativePath
    Assert-FileHash $sourceArchive 'SHA512' $release.Sha512
    Assert-FileHash $packageArchive 'SHA512' $release.Sha512
}
$actualArchives = @(Get-ChildItem -LiteralPath (Join-Path $packagePath 'releases') -Filter '*.mrpack' -File -Recurse | ForEach-Object {
    [System.IO.Path]::GetRelativePath($packagePath, $_.FullName).Replace('\', '/')
})
if ($actualArchives.Count -ne 27 -or $actualArchives.Count -ne $seenArchives.Count) {
    throw [System.IO.InvalidDataException]::new('Package does not contain exactly the pinned 27 Modrinth archives.')
}
foreach ($path in $actualArchives) {
    if (-not $seenArchives.Contains($path)) {
        throw [System.IO.InvalidDataException]::new("Unexpected Modrinth archive in package: $path")
    }
}

$productVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $packagePath 'MinePack.Installer.exe')).ProductVersion
if ($productVersion -cne '1.5.1-audit.1') {
    throw [System.IO.InvalidDataException]::new("Expected installer ProductVersion 1.5.1-audit.1; found '$productVersion'.")
}

Assert-LocalLinks $packagePath @(
    'README.md', 'README.ru.md', 'README.zh-CN.md', 'releases/test-pack/README.md',
    'releases/vanilla-2-plus/README.md', 'third-party/README.md'
)
$sourceCount = Assert-SourceKit $packagePath

$publicArchive = Join-Path $repoRoot 'artifacts\MinePack-Installer-1.5.0-win-x64.zip'
Assert-FileHash $publicArchive 'SHA256' $publishedInstallerSha256
$publicZipInPackage = @(Get-ChildItem -LiteralPath $packagePath -Recurse -File -Filter 'MinePack-Installer-1.5.0-win-x64.zip')
if ($publicZipInPackage.Count -ne 0) {
    throw [System.IO.InvalidDataException]::new('Published Installer 1.5.0 archive must not be nested in the audit candidate.')
}

if (-not $SkipNegativeFixtures) { Test-NegativeFixtures $packagePath }

Write-Host 'result=PASS'
Write-Host "installer_product_version=$productVersion"
Write-Host "pinned_mrpack_count=$($releaseList.Count)"
Write-Host "source_manifest_sha256=$sourceManifestSha256 source_manifest_rows=$sourceCount"
Write-Host "published_150_zip_sha256=$publishedInstallerSha256"

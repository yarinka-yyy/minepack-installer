[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $InstallerVersion = '1.6.2'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$buildRoot = [IO.Path]::GetFullPath((Join-Path $artifactRoot "build-$InstallerVersion"))
$packageName = "MinePack-Installer-$InstallerVersion-win-x64"
$packageRoot = [IO.Path]::GetFullPath((Join-Path $artifactRoot $packageName))
$zipPath = [IO.Path]::GetFullPath((Join-Path $artifactRoot ($packageName + '.zip')))
$unpackedRoot = [IO.Path]::GetFullPath((Join-Path $buildRoot 'unpacked'))
$logRoot = Join-Path $buildRoot 'logs'
$logPath = Join-Path $logRoot 'Build-LocalInstaller.log'
$transcriptStarted = $false
$locationPushed = $false

function Trim-DirectorySeparator([string] $Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetPathRoot($full)
    if ($full.Equals($root, [StringComparison]::OrdinalIgnoreCase)) { return $full }
    return $full.TrimEnd([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar))
}

function Get-RelativePath([string] $BasePath, [string] $Path) {
    $base = (Trim-DirectorySeparator $BasePath) + [IO.Path]::DirectorySeparatorChar
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside expected root: $Path"
    }
    return $full.Substring($base.Length).Replace('\', '/')
}

function Assert-UnderArtifacts([string] $Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $prefix = (Trim-DirectorySeparator $artifactRoot) + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Output path is outside artifacts: $full"
    }
}

function Assert-NoReparseAncestors([string] $Path) {
    $current = [IO.Path]::GetFullPath($Path)
    $appPrefix = (Trim-DirectorySeparator $repoRoot) + [IO.Path]::DirectorySeparatorChar
    if (-not $current.Equals($repoRoot, [StringComparison]::OrdinalIgnoreCase) -and
        -not $current.StartsWith($appPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the app workspace: $current"
    }
    while ($true) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse point is not allowed in a package path: $current"
            }
        }
        if ($current.Equals($repoRoot, [StringComparison]::OrdinalIgnoreCase)) { break }
        $parent = [IO.Path]::GetDirectoryName($current)
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent.Equals($current, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Could not validate path ancestors: $Path"
        }
        $current = $parent
    }
}

function Get-FileHashValue([string] $Path, [string] $Algorithm) {
    return (Get-FileHash -LiteralPath $Path -Algorithm $Algorithm).Hash.ToUpperInvariant()
}

function Get-TreeInventory([string] $Root) {
    $inventory = @{}
    $stack = [Collections.Generic.Stack[string]]::new()
    $stack.Push([IO.Path]::GetFullPath($Root))
    while ($stack.Count -gt 0) {
        $directory = $stack.Pop()
        foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse point found in package: $($item.FullName)"
            }
            if ($item.PSIsContainer) {
                $stack.Push($item.FullName)
                continue
            }
            $relative = Get-RelativePath $Root $item.FullName
            if ($inventory.ContainsKey($relative)) { throw "Duplicate package path: $relative" }
            $inventory[$relative] = Get-FileHashValue $item.FullName 'SHA256'
        }
    }
    return ,$inventory
}

function Assert-InventoryEqual($Expected, $Actual, [string] $Description) {
    if ($Expected.Count -ne $Actual.Count) { throw "$Description file count differs." }
    foreach ($path in $Expected.Keys) {
        if (-not $Actual.ContainsKey($path) -or $Expected[$path] -cne $Actual[$path]) {
            throw "$Description differs at $path."
        }
    }
}

function Get-CSharpConstant([string] $Path, [string] $Name) {
    $source = Get-Content -LiteralPath $Path -Raw
    $pattern = 'public\s+const\s+string\s+' + [regex]::Escape($Name) + '\s*=\s*"([^"]+)"'
    $match = [regex]::Match($source, $pattern)
    if (-not $match.Success) { throw "Could not read $Name from $Path" }
    return $match.Groups[1].Value
}

function Assert-ProtectedReleaseZips {
    $expected = @{
        'MinePack-Installer-1.5.0-win-x64.zip' = '0BA6607307F8C9F43A596FC879756FBB006B0D6DCF5777A0AEC7BAFFB9245A65'
        'MinePack-Installer-audit-candidate-win-x64.zip' = 'EF2B62355ACEF6E57B37017B798D9C257217BEE3E6177AC2499C415DA0BC092A'
    }
    foreach ($name in $expected.Keys) {
        $path = Join-Path $artifactRoot $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-FileHashValue $path 'SHA256') -cne $expected[$name]) {
            throw "Protected release ZIP is missing or has changed: $path"
        }
    }
}

Assert-UnderArtifacts $packageRoot
Assert-UnderArtifacts $zipPath
Assert-UnderArtifacts $unpackedRoot
foreach ($path in @($packageRoot, $zipPath, $unpackedRoot, $buildRoot, $logRoot)) {
    Assert-NoReparseAncestors $path
}
foreach ($path in @($packageRoot, $zipPath, $unpackedRoot)) {
    if (Test-Path -LiteralPath $path) { throw "Refusing to overwrite existing output: $path" }
}
Assert-ProtectedReleaseZips
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
Start-Transcript -Path $logPath -Append | Out-Null
$transcriptStarted = $true

try {
    Push-Location $repoRoot
    $locationPushed = $true

    $projectPath = Join-Path $repoRoot 'src/MinePack.Installer/MinePack.Installer.csproj'
    [xml] $project = Get-Content -LiteralPath $projectPath -Raw
    $version = $project.Project.PropertyGroup.Version | Select-Object -First 1
    if ($version -ne $InstallerVersion) { throw "Installer version must be $InstallerVersion, got '$version'." }

    $sourceArchives = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'releases') -Recurse -File -Filter '*.mrpack' |
        Sort-Object FullName)
    if ($sourceArchives.Count -ne 29) { throw "Expected 29 pinned .mrpack archives, found $($sourceArchives.Count)." }
    $archiveLinks = @($project.Project.ItemGroup.Content | Where-Object {
        $_.Include -match '\.mrpack$'
    } | ForEach-Object { $_.Link.Replace('\', '/') } | Sort-Object -Unique)
    $archiveSources = @($sourceArchives | ForEach-Object { Get-RelativePath $repoRoot $_.FullName } | Sort-Object -Unique)
    if ($archiveLinks.Count -ne 29 -or $archiveSources.Count -ne 29 -or
        @(Compare-Object $archiveSources $archiveLinks).Count -ne 0) {
        throw 'The project content list does not match the 29 source .mrpack archives.'
    }

    $testPackPath = Join-Path $repoRoot 'src/MinePack.Core/TestPackRelease.cs'
    $frontierPath = Join-Path $repoRoot 'src/MinePack.Core/Vanilla2PlusRelease.cs'
    $currentPins = @(
        @{ RelativePath = "releases/test-pack/$(Get-CSharpConstant $testPackPath 'ArtifactFileName')"; Hash = (Get-CSharpConstant $testPackPath 'ArtifactSha512') }
        @{ RelativePath = "releases/vanilla-2-plus/$(Get-CSharpConstant $frontierPath 'ArtifactFileName')"; Hash = (Get-CSharpConstant $frontierPath 'ArtifactSha512') }
    )
    foreach ($pin in $currentPins) {
        $source = Join-Path $repoRoot ($pin.RelativePath -replace '/', '\')
        if (-not (Test-Path -LiteralPath $source -PathType Leaf) -or
            (Get-FileHashValue $source 'SHA512') -cne $pin.Hash.ToUpperInvariant()) {
            throw "Current pinned archive does not match its release constant: $($pin.RelativePath)"
        }
    }

    Write-Host "Publishing self-contained win-x64 single-file installer $InstallerVersion..."
    $publishArguments = @(
        'publish', $projectPath, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '--artifacts-path', $buildRoot, '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:PublishTrimmed=false',
        '-p:UseSharedCompilation=false', '-m:1', '-o', $packageRoot
    )
    & dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    Assert-NoReparseAncestors $packageRoot
    $exe = Join-Path $packageRoot 'MinePack.Installer.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Published installer EXE is missing.' }
    $productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
    if ([string]::IsNullOrWhiteSpace($productVersion) -or $productVersion -cne $InstallerVersion) {
        throw "Published EXE version is not ${InstallerVersion}: '$productVersion'"
    }

    $requiredFiles = @(
        'README.md', 'README.ru.md', 'README.zh-CN.md', 'LICENSE', 'Assets/Monocraft-OFL.txt',
        'releases/test-pack/README.md', 'releases/vanilla-2-plus/README.md', 'third-party/README.md'
    )
    foreach ($relative in $requiredFiles) {
        $path = Join-Path $packageRoot ($relative -replace '/', '\')
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required package file is missing: $relative" }
    }
    $fontAndIcon = @($project.Project.ItemGroup.Resource | ForEach-Object { $_.Include.Replace('\', '/') })
    if ($fontAndIcon -notcontains 'Assets/Monocraft.ttf' -or $fontAndIcon -notcontains 'Assets/MinePackIcon.ico') {
        throw 'Embedded font or application icon is missing from the project resource list.'
    }

    foreach ($archive in $sourceArchives) {
        $relative = Get-RelativePath $repoRoot $archive.FullName
        $published = Join-Path $packageRoot ($relative -replace '/', '\')
        if (-not (Test-Path -LiteralPath $published -PathType Leaf) -or
            (Get-FileHashValue $published 'SHA512') -cne (Get-FileHashValue $archive.FullName 'SHA512')) {
            throw "Published archive is missing or differs from source: $relative"
        }
    }
    $publishedArchives = @(Get-ChildItem -LiteralPath (Join-Path $packageRoot 'releases') -Recurse -File -Filter '*.mrpack')
    if ($publishedArchives.Count -ne 29) { throw "Published package contains $($publishedArchives.Count) .mrpack files, expected 29." }

    $sourceKitRoot = Join-Path $repoRoot 'third-party/yungs-sources'
    $publishedKitRoot = Join-Path $packageRoot 'third-party/yungs-sources'
    $excludedKitPath = '[\\/](?:\.git|\.gradle|build|bin|obj|cache|caches)(?:[\\/]|$)'
    $sourceKit = @(Get-ChildItem -LiteralPath $sourceKitRoot -Recurse -File | Where-Object {
        $_.FullName -notmatch $excludedKitPath -and $_.Extension -ine '.pdf'
    })
    $publishedKit = @(Get-ChildItem -LiteralPath $publishedKitRoot -Recurse -File)
    if ($sourceKit.Count -eq 0 -or $sourceKit.Count -ne $publishedKit.Count) {
        throw 'The third-party YUNG source kit is missing or has a different file count.'
    }
    foreach ($file in $sourceKit) {
        $relative = Get-RelativePath $sourceKitRoot $file.FullName
        $published = Join-Path $publishedKitRoot $relative
        if (-not (Test-Path -LiteralPath $published -PathType Leaf) -or
            (Get-FileHashValue $published 'SHA256') -cne (Get-FileHashValue $file.FullName 'SHA256')) {
            throw "Third-party source kit differs at $relative"
        }
    }

    $packageInventory = Get-TreeInventory $packageRoot
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipStream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $archive = [IO.Compression.ZipArchive]::new($zipStream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $packageRoot -Recurse -File) {
            $relative = Get-RelativePath $packageRoot $file.FullName
            $entryName = $packageName + '/' + $relative
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $archive.Dispose()
        $zipStream.Dispose()
    }

    $zipRead = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = @($zipRead.Entries)
        $roots = @($entries | ForEach-Object { $_.FullName.Split('/')[0] } | Sort-Object -Unique)
        if ($entries.Count -eq 0 -or $roots.Count -ne 1 -or $roots[0] -cne $packageName -or
            @($entries | Where-Object { -not $_.FullName.StartsWith($packageName + '/', [StringComparison]::Ordinal) }).Count -ne 0) {
            throw "The ZIP must contain files under one $packageName root folder."
        }
    }
    finally { $zipRead.Dispose() }

    New-Item -ItemType Directory -Path $unpackedRoot | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $unpackedRoot)
    $extractedRoot = Join-Path $unpackedRoot $packageName
    if (-not (Test-Path -LiteralPath $extractedRoot -PathType Container)) { throw 'The ZIP root folder is missing after extraction.' }
    $extractedInventory = Get-TreeInventory $extractedRoot
    Assert-InventoryEqual $packageInventory $extractedInventory 'Extracted ZIP and package folder'

    Assert-ProtectedReleaseZips
    Write-Host "PASS: installer version $productVersion"
    Write-Host "PASS: 29 .mrpack archives match source SHA-512; current pins match release constants"
    Write-Host "PASS: $($sourceKit.Count) YUNG source-kit files and required licenses/docs included"
    Write-Host "PASS: ZIP has one root folder and $($packageInventory.Count) files match the published folder"
    Write-Host "EXE: $exe"
    Write-Host "ZIP: $zipPath"
    Write-Host "ZIP SHA-256: $(Get-FileHashValue $zipPath 'SHA256')"
}
finally {
    if ($locationPushed) { Pop-Location }
    if ($transcriptStarted) { Stop-Transcript | Out-Null }
}

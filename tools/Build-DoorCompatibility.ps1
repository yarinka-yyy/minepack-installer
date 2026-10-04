$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = Split-Path -Parent $PSScriptRoot
$oldName = 'Remodeled-Doors-26.2-xalis-blockstates.1.zip'
$newName = 'Remodeled-Doors-26.2-xalis-blockstates.2.zip'
$expectedOldHash = 'AD8B48DD7CC7BD872EE8213B95ACD791D33A6D991D52FF0CB827CC13C9F6C090'
$sourceArchive = Join-Path $repoRoot "pack/test-pack/resourcepacks/$oldName"
$otherArchive = Join-Path $repoRoot "pack/vanilla-2-plus/resourcepacks/$oldName"
$targets = @(
    (Join-Path $repoRoot "pack/test-pack/resourcepacks/$newName"),
    (Join-Path $repoRoot "pack/vanilla-2-plus/resourcepacks/$newName")
)
$validationRoot = Join-Path $repoRoot 'artifacts/build-1.6.2/resource-validation'
$stagedArchive = Join-Path $validationRoot $newName
$aliasRoot = Join-Path $PSScriptRoot 'resource-overrides/remodeled-doors-26.2/assets/minecraft/models/block'
$aliases = @(
    'door_bottom.json',
    'door_top.json',
    'door_bottom_rh.json'
)

function Read-ZipPayloads([string] $Path) {
    $payloads = [System.Collections.Generic.Dictionary[string, byte[]]]::new([StringComparer]::Ordinal)
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        foreach ($entry in $archive.Entries) {
            if ($payloads.ContainsKey($entry.FullName)) { throw "Duplicate ZIP entry: $($entry.FullName)" }
            $stream = $entry.Open()
            $memory = [System.IO.MemoryStream]::new()
            try {
                $stream.CopyTo($memory)
                $payloads.Add($entry.FullName, $memory.ToArray())
            } finally {
                $memory.Dispose()
                $stream.Dispose()
            }
        }
    } finally {
        $archive.Dispose()
    }
    return ,$payloads
}

function Test-BytesEqual([byte[]] $Left, [byte[]] $Right) {
    if ($Left.Length -ne $Right.Length) { return $false }
    for ($index = 0; $index -lt $Left.Length; $index++) {
        if ($Left[$index] -ne $Right[$index]) { return $false }
    }
    return $true
}

foreach ($path in @($sourceArchive, $otherArchive)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing original compatibility archive: $path" }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($hash -cne $expectedOldHash) { throw "Original compatibility archive SHA-256 mismatch: $path ($hash)" }
}
foreach ($path in $targets) {
    if (Test-Path -LiteralPath $path) { throw "Refusing to overwrite an existing compatibility archive: $path" }
}
if (Test-Path -LiteralPath $stagedArchive) { throw "Refusing to overwrite an existing staged archive: $stagedArchive" }

$originalEntries = Read-ZipPayloads $sourceArchive
$otherEntries = Read-ZipPayloads $otherArchive
if ($originalEntries.Count -ne $otherEntries.Count) { throw 'The two original compatibility ZIPs differ in entry count.' }
foreach ($name in $originalEntries.Keys) {
    if (-not $otherEntries.ContainsKey($name) -or -not (Test-BytesEqual $originalEntries[$name] $otherEntries[$name])) {
        throw "The two original compatibility ZIPs differ at $name."
    }
}

$newEntries = [System.Collections.Generic.Dictionary[string, byte[]]]::new([StringComparer]::Ordinal)
$utf8 = [System.Text.UTF8Encoding]::new($false)
foreach ($alias in $aliases) {
    $sourcePath = Join-Path $aliasRoot $alias
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Missing compatibility source model: $sourcePath" }
    $text = [System.IO.File]::ReadAllText($sourcePath).Replace("`r`n", "`n").Replace("`r", "`n")
    $model = ConvertFrom-Json -InputObject $text -ErrorAction Stop
    $propertyNames = @($model.PSObject.Properties.Name)
    if ($propertyNames.Count -ne 2 -or -not ($propertyNames -ccontains 'parent') -or
        -not ($propertyNames -ccontains 'ambientocclusion') -or $model.parent -cne 'minecraft:block/block' -or
        $model.ambientocclusion -isnot [bool] -or $model.ambientocclusion) {
        throw "Compatibility source model must contain only the approved parent and ambientocclusion: $sourcePath"
    }
    $entryName = "assets/minecraft/models/block/$alias"
    if ($originalEntries.ContainsKey($entryName)) { throw "Compatibility model already exists in the original ZIP: $entryName" }
    $newEntries.Add($entryName, $utf8.GetBytes($text))
}

if (-not (Test-Path -LiteralPath $validationRoot -PathType Container)) {
    [void][System.IO.Directory]::CreateDirectory($validationRoot)
}
$names = [System.Collections.Generic.List[string]]::new()
foreach ($name in $originalEntries.Keys) { $names.Add($name) }
foreach ($name in $newEntries.Keys) { $names.Add($name) }
$names.Sort([StringComparer]::Ordinal)

$fileStream = [System.IO.File]::Open($stagedArchive, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
try {
    $archive = [System.IO.Compression.ZipArchive]::new($fileStream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($name in $names) {
            $entry = $archive.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $entry.ExternalAttributes = 0
            $payload = if ($originalEntries.ContainsKey($name)) { $originalEntries[$name] } else { $newEntries[$name] }
            $stream = $entry.Open()
            try { $stream.Write($payload, 0, $payload.Length) } finally { $stream.Dispose() }
        }
    } finally {
        $archive.Dispose()
    }
} finally {
    $fileStream.Dispose()
}

$builtEntries = Read-ZipPayloads $stagedArchive
if ($builtEntries.Count -ne ($originalEntries.Count + 3)) { throw 'Built compatibility ZIP must contain the original entries plus exactly three aliases.' }
foreach ($name in $originalEntries.Keys) {
    if (-not $builtEntries.ContainsKey($name) -or -not (Test-BytesEqual $originalEntries[$name] $builtEntries[$name])) {
        throw "The rebuilt compatibility ZIP changed an original entry: $name"
    }
}
foreach ($name in $newEntries.Keys) {
    if (-not $builtEntries.ContainsKey($name) -or -not (Test-BytesEqual $newEntries[$name] $builtEntries[$name])) {
        throw "The rebuilt compatibility ZIP has an invalid alias entry: $name"
    }
}

foreach ($target in $targets) { [System.IO.File]::Copy($stagedArchive, $target, $false) }
$newHash = (Get-FileHash -LiteralPath $stagedArchive -Algorithm SHA256).Hash
foreach ($target in $targets) {
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -cne $newHash) {
        throw "The new compatibility ZIP differs between pack trees: $target"
    }
}
Write-Output "Created ${newName}: $newHash ($($originalEntries.Count) original entries + 3 approved aliases; both pack trees match)."

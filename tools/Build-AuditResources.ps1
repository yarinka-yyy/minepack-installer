[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location $repo

function Assert-NewPath([string]$Path) {
    if (Test-Path -LiteralPath $Path) { throw "Refusing to overwrite existing planned output: $Path" }
}
function Get-BytesHash([byte[]]$Bytes, [string]$Algorithm) {
    $hasher = if ($Algorithm -eq 'SHA512') { [Security.Cryptography.SHA512]::Create() } else { [Security.Cryptography.SHA256]::Create() }
    try { return [Convert]::ToHexString($hasher.ComputeHash($Bytes)) } finally { $hasher.Dispose() }
}
function Read-ZipEntry([IO.Compression.ZipArchiveEntry]$Entry) {
    $stream = $Entry.Open()
    $memory = [IO.MemoryStream]::new()
    try { $stream.CopyTo($memory); return ,$memory.ToArray() }
    finally { $stream.Dispose(); $memory.Dispose() }
}
function Assert-RelativeEntry([string]$Name) {
    $normalized = $Name.Replace('\', '/')
    if ([IO.Path]::IsPathRooted($normalized) -or $normalized.Contains(':') -or
        @($normalized.Split('/') | Where-Object { $_ -eq '..' -or $_ -eq '.' }).Count -gt 0) {
        throw "Unsafe ZIP path in pinned input: $Name"
    }
}
function Replace-Once([string]$Text, [string]$Old, [string]$New, [string]$Label) {
    $count = 0
    $at = 0
    while (($at = $Text.IndexOf($Old, $at, [StringComparison]::Ordinal)) -ge 0) { $count++; $at += $Old.Length }
    if ($count -ne 1) { throw "$Label expected one exact occurrence, found $count" }
    return $Text.Replace($Old, $New)
}
function Replace-PropertyValue([string]$Text, [string]$Key, [string]$Old, [string]$New, [string]$Label) {
    return Replace-Once $Text "$Key=$Old" "$Key=$New" $Label
}
function Read-Utf8([byte[]]$Bytes) {
    $offset = if ($Bytes.Length -ge 3 -and $Bytes[0] -eq 239 -and $Bytes[1] -eq 187 -and $Bytes[2] -eq 191) { 3 } else { 0 }
    return [Text.UTF8Encoding]::new($false, $true).GetString($Bytes, $offset, $Bytes.Length - $offset)
}
function Convert-BiomeToken([string]$Token) {
    $normalized = $Token.Replace('\:', ':').Replace('\!', '!')
    $normalized = $normalized.TrimStart('!')
    if ($normalized.Contains(':')) { return $normalized }
    return "minecraft:$normalized"
}

$baseline = 'releases/vanilla-2-plus/vanilla-2-plus-0.19.6.mrpack'
$baselineSha512 = '5FEC73C5E023C251D35E20A32C181C0679669972D8945642D36725FFE575969473CA2F7AB75699D4E7ED91CB78AE57628570189A7548FE3898968BD663467D2B'
$inputsPath = 'artifacts/verify-plan021/pinned-release-inputs.txt'
$evidence = 'artifacts/verify-plan021'
$work = 'artifacts/verify-plan022'
$resourceWork = Join-Path $work 'resource-work'
$sourceWork = Join-Path $work 'source-work'
$bundle = 'third-party/yungs-sources'
$xaliOutput = 'pack/vanilla-2-plus/resourcepacks/xalis-enhanced-vanilla-26.2-minepack.2.zip'
$desertOutput = 'pack/vanilla-2-plus/mods/YungsBetterDesertTemples-26.2-Fabric-5.1.1-minepack.2.jar'
$newReleaseOutput = 'releases/vanilla-2-plus/vanilla-2-plus-0.19.7.mrpack'
$frozen = 'artifacts/verify-plan021/frozen-yungs-source'
$frozenManifest = 'artifacts/verify-plan021/source-snapshot-manifest.txt'

$fullBaseline = Join-Path $repo $baseline
$actualBaselineHash = (Get-FileHash -LiteralPath $fullBaseline -Algorithm SHA512).Hash
if ($actualBaselineHash -ne $baselineSha512) { throw "Immutable 0.19.6 input hash changed: $actualBaselineHash" }
if ((Get-FileHash -LiteralPath $frozenManifest -Algorithm SHA256).Hash -ne '71319FCB9C79F107ED7255DC26113F4C2DF4DB1E371B65BA10C7EF0C63E4A3B6') {
    throw 'Frozen YUNG source manifest identity changed.'
}
$frozenRows = @(Get-Content -LiteralPath $frozenManifest | Where-Object { $_ -match '^[^|]+\|\d+\|[0-9A-Fa-f]{64}$' })
if ($frozenRows.Count -ne 1787) { throw "Frozen source manifest must list 1,787 files; found $($frozenRows.Count)." }
$frozenEntries = @{}
foreach ($row in $frozenRows) {
    $fields = $row.Split('|')
    Assert-RelativeEntry $fields[0]
    if ($frozenEntries.ContainsKey($fields[0])) { throw "Duplicate frozen source manifest path: $($fields[0])" }
    $frozenEntries[$fields[0]] = [pscustomobject]@{ Size = [long]$fields[1]; Sha256 = $fields[2] }
    $sourcePath = Join-Path $frozen $fields[0]
    if (!(Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Frozen source file is missing: $($fields[0])" }
    $sourceFile = Get-Item -LiteralPath $sourcePath -Force
    if (($sourceFile.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $sourceFile.Length -ne $frozenEntries[$fields[0]].Size -or
        (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ne $frozenEntries[$fields[0]].Sha256) {
        throw "Frozen source file differs from its accepted manifest: $($fields[0])"
    }
}
$actualFrozenFiles = @(Get-ChildItem -LiteralPath $frozen -Recurse -File -Force)
if ($actualFrozenFiles.Count -ne $frozenEntries.Count) { throw "Frozen source inventory differs from manifest: expected $($frozenEntries.Count), found $($actualFrozenFiles.Count)." }
if (!(Test-Path -LiteralPath $bundle)) { throw "Source bundle directory is missing: $bundle" }
$bundlePrerequisites = @('LICENSE-GPL-3.0.txt','LICENSE-LGPL-3.0.txt')
$bundleNames = @(Get-ChildItem $bundle -Force | Select-Object -ExpandProperty Name | Sort-Object)
if (($bundleNames -join '|') -ne (@($bundlePrerequisites | Sort-Object) -join '|')) {
    throw "Source bundle must contain only the two exact GNU license texts before source staging: $bundle"
}
foreach ($path in @($xaliOutput, $desertOutput, $newReleaseOutput, $resourceWork, $sourceWork,
        (Join-Path $bundle 'source-snapshot-manifest.txt'))) { Assert-NewPath $path }
New-Item -ItemType Directory -Path $resourceWork,$sourceWork -Force | Out-Null

$ledgerRows = @(Get-Content -LiteralPath $inputsPath | Where-Object { $_ -match '^overrides/' })
if ($ledgerRows.Count -ne 8) { throw "Pinned input ledger must contain exactly eight archive entries; found $($ledgerRows.Count)." }
$ledger = @{}
foreach ($row in $ledgerRows) {
    $fields = $row.Split('|')
    if ($fields.Count -ne 3) { throw "Malformed pinned input ledger row: $row" }
    Assert-RelativeEntry $fields[0]
    if ($ledger.ContainsKey($fields[0])) { throw "Duplicate pinned entry: $($fields[0])" }
    $ledger[$fields[0]] = [pscustomobject]@{ Size = [long]$fields[1]; Sha512 = $fields[2] }
}

$baselineZip = [IO.Compression.ZipFile]::OpenRead($fullBaseline)
try {
    foreach ($name in $ledger.Keys) {
        $matches = @($baselineZip.Entries | Where-Object FullName -CEQ $name)
        if ($matches.Count -ne 1) { throw "Pinned archive entry count mismatch: $name ($($matches.Count))" }
        $bytes = Read-ZipEntry $matches[0]
        if ($bytes.Length -ne $ledger[$name].Size -or (Get-BytesHash $bytes 'SHA512') -ne $ledger[$name].Sha512) {
            throw "Pinned archive entry identity mismatch: $name"
        }
        $leaf = [IO.Path]::GetFileName($name)
        $target = Join-Path $resourceWork "pinned/$leaf"
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($target))) | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $repo $target), $bytes)
    }
}
finally { $baselineZip.Dispose() }

$xaliEntryName = 'overrides/resourcepacks/xalis-enhanced-vanilla-26.2-minepack.1.zip'
$xaliInput = Join-Path $resourceWork 'pinned/xalis-enhanced-vanilla-26.2-minepack.1.zip'
$caseRows = @(Get-Content -LiteralPath (Join-Path $evidence 'uppercase-resource-proof.txt') |
    Where-Object { $_ -match '^assets/.*\|assets/.*\|\d+\|[0-9A-Fa-f]{64}$' })
if ($caseRows.Count -ne 42) { throw "Expected 42 exact case-rename inputs; found $($caseRows.Count)." }
$caseMap = @{}
$lowerTargets = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($row in $caseRows) {
    $fields = $row.Split('|')
    if ($fields.Count -ne 4 -or !$lowerTargets.Add($fields[1])) { throw "Malformed or colliding case map: $row" }
    Assert-RelativeEntry $fields[0]
    Assert-RelativeEntry $fields[1]
    $caseMap[$fields[0]] = $fields[1]
}

$citRows = @(Get-Content -LiteralPath (Join-Path $evidence 'ctm-reference-mapping.txt') |
    Where-Object { $_.StartsWith('CITREF|', [StringComparison]::Ordinal) })
if ($citRows.Count -ne 37) { throw "Expected 37 exact CIT model mappings; found $($citRows.Count)." }
$citByPath = @{}
foreach ($row in $citRows) {
    $fields = $row.Split('|')
    $before = ($fields | Where-Object { $_.StartsWith('before=', [StringComparison]::Ordinal) }) -replace '^before=', ''
    $after = ($fields | Where-Object { $_.StartsWith('after=', [StringComparison]::Ordinal) }) -replace '^after=', ''
    if (!$citByPath.ContainsKey($fields[1])) { $citByPath[$fields[1]] = [Collections.Generic.List[object]]::new() }
    $citByPath[$fields[1]].Add([pscustomobject]@{ Before = $before; After = $after })
}

$tileChanges = @{
    'assets/minecraft/optifine/ctm/vine/vine0.properties' = @('vin0_alt_1', 'vine0_alt_1')
    'assets/minecraft/optifine/ctm/bookshelf/oak/side.properties' = @('oak_bookshelf\ 0-15', 'oak_bookshelf\ 1-15')
    'assets/minecraft/optifine/ctm/_overlays/snowy_grass/1.properties' = @('block/inv', 'inv')
    'assets/minecraft/optifine/ctm/_overlays/snowy_grass/tall_grass_bottom_inv.properties' = @('block/inv', 'inv')
    'assets/minecraft/optifine/ctm/_overlays/snowy_grass/tall_grass_top_inv.properties' = @('block/inv', 'inv')
}
$axolotlChanges = @{
    'assets/minecraft/optifine/cit/axolotl_bucket/blue.properties' = @('nbt.Variant=3', 'components.~axolotl/variant=cyan')
    'assets/minecraft/optifine/cit/axolotl_bucket/cyan.properties' = @('nbt.Variant=4', 'components.~axolotl/variant=blue')
    'assets/minecraft/optifine/cit/axolotl_bucket/gold.properties' = @('nbt.Variant=2', 'components.~axolotl/variant=gold')
    'assets/minecraft/optifine/cit/axolotl_bucket/wild.properties' = @('nbt.Variant=1', 'components.~axolotl/variant=wild')
}
$missingRows = @(Get-Content -LiteralPath (Join-Path $evidence 'candidate-biome-inventory.txt') |
    Where-Object { $_ -match '^MISSING_SOURCE\|minecraft:[^|]+\|assets/minecraft/optifine/ctm/' })
$invalidRows = @(Get-Content -LiteralPath (Join-Path $evidence 'candidate-biome-inventory.txt') |
    Where-Object { $_ -match '^INVALID\|assets/minecraft/optifine/ctm/.*\|biomes\|!snowy_taiga$' })
if ($missingRows.Count -ne 273 -or $invalidRows.Count -ne 3) { throw "Unexpected biome edit inventory: missing=$($missingRows.Count), invalid=$($invalidRows.Count)." }
$removeByPath = @{}
foreach ($row in $missingRows) {
    $f = $row.Split('|')
    if (!$removeByPath.ContainsKey($f[2])) { $removeByPath[$f[2]] = [Collections.Generic.List[string]]::new() }
    $removeByPath[$f[2]].Add($f[1])
}
$invalidByPath = @{}
foreach ($row in $invalidRows) {
    $f = $row.Split('|')
    if (!$invalidByPath.ContainsKey($f[1])) { $invalidByPath[$f[1]] = [Collections.Generic.List[string]]::new() }
    $invalidByPath[$f[1]].Add($f[3])
}

$sourceResource = [IO.Compression.ZipFile]::OpenRead($xaliInput)
$xaliBuild = Join-Path $resourceWork 'xalis-enhanced-vanilla-26.2-minepack.2.zip'
Assert-NewPath $xaliBuild
$outputStream = [IO.File]::Open((Join-Path $repo $xaliBuild), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
$outputZip = [IO.Compression.ZipArchive]::new($outputStream, [IO.Compression.ZipArchiveMode]::Create, $false)
$allOutputNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$fileCount = 0
$citChanged = 0
$tileChanged = 0
$axolotlChanged = 0
$biomeFilesChanged = 0
$biomeMissingRemoved = 0
$biomeInvalidRemoved = 0
try {
    foreach ($entry in $sourceResource.Entries) {
        $originalName = $entry.FullName.TrimEnd('/')
        Assert-RelativeEntry $originalName
        $mappedName = if ($caseMap.ContainsKey($originalName)) { $caseMap[$originalName] } else { $originalName }
        if ($entry.FullName.EndsWith('/')) { $mappedName += '/' }
        if (!$allOutputNames.Add($mappedName)) { throw "Output ZIP path collision: $mappedName" }
        $bytes = Read-ZipEntry $entry
        if (!$entry.FullName.EndsWith('/')) {
            $fileCount++
            if ($caseMap.ContainsKey($originalName)) {
                $caseRow = $caseRows | Where-Object { $_.StartsWith($originalName + '|', [StringComparison]::Ordinal) }
                $expected = $caseRow.Split('|')
                if ($bytes.Length -ne [long]$expected[2] -or (Get-BytesHash $bytes 'SHA256') -ne $expected[3]) {
                    throw "Case-renamed input payload mismatch: $originalName"
                }
            }
            if ($citByPath.ContainsKey($originalName)) {
                $text = Read-Utf8 $bytes
                foreach ($mapping in $citByPath[$originalName]) {
                    $text = Replace-Once $text ('"' + $mapping.Before + '"') ('"' + $mapping.After + '"') "CIT reference $originalName"
                    $citChanged++
                }
                $bytes = [Text.UTF8Encoding]::new($false).GetBytes($text)
            }
            if ($axolotlChanges.ContainsKey($originalName)) {
                $pair = $axolotlChanges[$originalName]
                $bytes = [Text.UTF8Encoding]::new($false).GetBytes((Replace-Once (Read-Utf8 $bytes) $pair[0] $pair[1] "Axolotl $originalName"))
                $axolotlChanged++
            }
            if ($tileChanges.ContainsKey($originalName)) {
                $pair = $tileChanges[$originalName]
                $bytes = [Text.UTF8Encoding]::new($false).GetBytes((Replace-Once (Read-Utf8 $bytes) $pair[0] $pair[1] "CTM tile $originalName"))
                $tileChanged++
            }
            if ($removeByPath.ContainsKey($originalName) -or $invalidByPath.ContainsKey($originalName)) {
                $text = Read-Utf8 $bytes
                $match = [regex]::Match($text, '(?m)^biomes=(.*?)(\r?)$')
                if (!$match.Success -or [regex]::Matches($text, '(?m)^biomes=').Count -ne 1) { throw "Expected one biomes line: $originalName" }
                $value = $match.Groups[1].Value
                $hasTrailingSeparator = $value.EndsWith('\ ', [StringComparison]::Ordinal)
                if ($hasTrailingSeparator) { $value = $value.Substring(0, $value.Length - 2) }
                $tokens = @($value -split '\\ ')
                $kept = [Collections.Generic.List[string]]::new()
                $negated = $tokens.Count -gt 0 -and ($tokens[0].StartsWith('\!', [StringComparison]::Ordinal) -or $tokens[0].StartsWith('!', [StringComparison]::Ordinal))
                foreach ($token in $tokens) {
                    if ($token.Length -eq 0) { continue }
                    $isInnerInvalid = $invalidByPath.ContainsKey($originalName) -and ($token -eq '\!snowy_taiga' -or $token -eq '!snowy_taiga')
                    $id = Convert-BiomeToken $token
                    $isMissing = $removeByPath.ContainsKey($originalName) -and $removeByPath[$originalName].Contains($id)
                    if ($isInnerInvalid) { $biomeInvalidRemoved++; continue }
                    if ($isMissing) { $biomeMissingRemoved++; continue }
                    if ($kept.Count -eq 0 -and ($token.StartsWith('\!', [StringComparison]::Ordinal) -or $token.StartsWith('!', [StringComparison]::Ordinal))) {
                        $token = $token.TrimStart('\','!')
                    }
                    $kept.Add($token)
                }
                if ($kept.Count -eq 0) { throw "Biome filter became empty: $originalName" }
                if ($negated) { $kept[0] = '\!' + $kept[0].TrimStart('\','!') }
                $newValue = [string]::Join('\ ', $kept)
                if ($hasTrailingSeparator) { $newValue += '\ ' }
                $newLine = 'biomes=' + $newValue + $match.Groups[2].Value
                $updated = $text.Substring(0, $match.Index) + $newLine + $text.Substring($match.Index + $match.Length)
                $bytes = [Text.UTF8Encoding]::new($false).GetBytes($updated)
                $biomeFilesChanged++
            }
        }
        $newEntry = $outputZip.CreateEntry($mappedName, [IO.Compression.CompressionLevel]::Optimal)
        if ($entry.LastWriteTime.Year -ge 1980 -and $entry.LastWriteTime.Year -le 2107) { $newEntry.LastWriteTime = $entry.LastWriteTime }
        $newEntry.ExternalAttributes = $entry.ExternalAttributes
        $stream = $newEntry.Open()
        try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
    }
    if ($fileCount -ne 2432 -or $citChanged -ne 37 -or $tileChanged -ne 5 -or $axolotlChanged -ne 4 -or
        $biomeFilesChanged -ne 60 -or $biomeMissingRemoved -ne 273 -or $biomeInvalidRemoved -ne 3) {
        throw "Transformation count mismatch: files=$fileCount CIT=$citChanged tiles=$tileChanged axolotl=$axolotlChanged biomeFiles=$biomeFilesChanged missing=$biomeMissingRemoved invalid=$biomeInvalidRemoved"
    }
    $notice = @'
MinePack resource changes, 2026-10-02
Derived only from the pinned Xali .minepack.1 archive entry in immutable Frontier 0.19.6.
Changes are limited to the documented 26.2 CIT axolotl predicates, 37 Continuity texture aliases, five CTM tile references, 276 CTM biome tokens, and 42 case-only resource-path fixes.
Original artwork, credits, and license notices are retained. This notice does not replace the included upstream attribution or license.
'@
    $noticeName = 'MINEPACK-CHANGES.txt'
    if (!$allOutputNames.Add($noticeName)) { throw "Xali notice path already exists: $noticeName" }
    $noticeEntry = $outputZip.CreateEntry($noticeName, [IO.Compression.CompressionLevel]::Optimal)
    $noticeBytes = [Text.UTF8Encoding]::new($false).GetBytes($notice + [Environment]::NewLine)
    $noticeStream = $noticeEntry.Open()
    try { $noticeStream.Write($noticeBytes, 0, $noticeBytes.Length) } finally { $noticeStream.Dispose() }
}
finally { $outputZip.Dispose(); $outputStream.Dispose(); $sourceResource.Dispose() }

$sourceNames = @(
    'source-yungs-api','source-yungs-desert-temples','source-yungs-better-dungeons',
    'source-yungs-better-jungle-temples','source-yungs-better-mineshafts',
    'source-yungs-better-fortresses','source-yungs-better-strongholds','yungsapi-local-api-marker'
)
foreach ($name in $sourceNames) {
    $source = Join-Path $frozen $name
    $destination = Join-Path $bundle $name
    if (!(Test-Path -LiteralPath $source -PathType Container)) { throw "Frozen source snapshot missing: $name" }
    Assert-NewPath $destination
    Copy-Item -LiteralPath $source -Destination $destination -Recurse
}
$desert2 = Join-Path $bundle 'source-yungs-desert-temples-minepack2'
Assert-NewPath $desert2
Copy-Item -LiteralPath (Join-Path $frozen 'source-yungs-desert-temples') -Destination $desert2 -Recurse
$desertProps = Join-Path $desert2 'gradle.properties'
$propsText = [IO.File]::ReadAllText($desertProps)
$propsVersionMatches = [regex]::Matches($propsText, '(?m)^version=.*$')
if ($propsVersionMatches.Count -ne 1) { throw 'Expected a single Desert Temples version property.' }
[IO.File]::WriteAllText($desertProps, [regex]::new('(?m)^version=.*$').Replace($propsText, 'version=5.1.1-minepack.2', 1), [Text.UTF8Encoding]::new($false))
$templeJson = Join-Path $desert2 'Common/src/main/resources/data/betterdeserttemples/advancement/temple_clear.json'
$templeText = [IO.File]::ReadAllText($templeJson)
$templeText = Replace-Once $templeText '"type": "minecraft:husk"' '"entity_type": "minecraft:husk"' 'Desert Temple advancement codec'
[IO.File]::WriteAllText($templeJson, $templeText, [Text.UTF8Encoding]::new($false))

$credentialPattern = '(?im)^(curseforgeApiKey|modrinthToken|ossrhToken|ossrhTokenPassword)\s*=\s*(.+)$'
foreach ($file in Get-ChildItem -LiteralPath $bundle -Recurse -File -Include 'gradle.properties','*.gradle','*.gradle.kts') {
    $secretKeyNames = [regex]::Matches([IO.File]::ReadAllText($file.FullName), $credentialPattern)
    if ($secretKeyNames.Count -gt 0) { throw "Nonempty publishing property found in source-kit file: $([IO.Path]::GetRelativePath((Resolve-Path $bundle).Path, $file.FullName))" }
}
foreach ($licenseName in @('LICENSE-GPL-3.0.txt','LICENSE-LGPL-3.0.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $bundle $licenseName) -PathType Leaf)) { throw "Required official license text is missing: $bundle/$licenseName" }
}
$sourceKitReadme = Join-Path $repo 'third-party/README.md'
if (!(Test-Path -LiteralPath $sourceKitReadme -PathType Leaf)) { throw 'third-party/README.md is required before manifest generation.' }
$manifestFile = Join-Path $bundle 'source-snapshot-manifest.txt'
$manifestLines = [Collections.Generic.List[string]]::new()
foreach ($file in Get-ChildItem -LiteralPath $bundle -Recurse -File | Sort-Object FullName) {
    if ($file.FullName -eq [IO.Path]::GetFullPath($manifestFile)) { continue }
    $relative = [IO.Path]::GetRelativePath([IO.Path]::GetFullPath($bundle), $file.FullName).Replace('\','/')
    if ($relative -match '(^|/)(\.git|\.gradle|build|cache)(/|$)') { throw "Forbidden generated content in source kit: $relative" }
    $manifestLines.Add("$relative|$($file.Length)|$((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)")
}
[IO.File]::WriteAllLines($manifestFile, $manifestLines, [Text.UTF8Encoding]::new($false))

$sourceBuild = Join-Path $sourceWork 'source-yungs-desert-temples-minepack2'
Copy-Item -LiteralPath $desert2 -Destination $sourceBuild -Recurse
Copy-Item -LiteralPath (Join-Path $bundle 'yungsapi-local-api-marker') -Destination (Join-Path $sourceWork 'yungsapi-local-api-marker') -Recurse
$env:JAVA_HOME = (Resolve-Path '../mods/_work/_tools/jdk-25.0.4.1+1').Path
$env:GRADLE_USER_HOME = (Resolve-Path 'artifacts/verify-plan021/gradle-home-seeded').Path
$gradle = '../mods/_work/_cache/gradle/wrapper/dists/gradle-9.4.1-bin/arn2x92ynaizyzdaamcbpbhtj/gradle-9.4.1/bin/gradle.bat'
$emptyPublishing = @('-PcurseforgeApiKey=', '-PmodrinthToken=', '-PossrhToken=', '-PossrhTokenPassword=')
& $gradle --offline --no-daemon --configure-on-demand -p $sourceBuild ':Fabric:jar' @emptyPublishing
if ($LASTEXITCODE -ne 0) { throw "Offline Desert Temples .2 source build failed with exit $LASTEXITCODE" }
$builtJar = Join-Path $sourceBuild 'Fabric/build/libs/YungsBetterDesertTemples-26.2-Fabric-5.1.1-minepack.2.jar'
if (!(Test-Path -LiteralPath $builtJar -PathType Leaf)) { throw "Expected exact .2 Gradle output is missing: $builtJar" }
Copy-Item -LiteralPath $builtJar -Destination $desertOutput
Copy-Item -LiteralPath (Join-Path $repo $xaliBuild) -Destination (Join-Path $repo $xaliOutput)

$artifactMap = [Collections.Generic.List[string]]::new()
$artifactMap.Add('Pinned historical source-to-binary inputs; SHA-512 values were read from the immutable 0.19.6 mrpack.')
$artifactMap.Add('source_snapshot|artifact_path|sha512')
$yungsMap = @(
    @('source-yungs-api','overrides/mods/YungsApi-26.2-Fabric-6.1.3-minepack.1.jar'),
    @('source-yungs-desert-temples','overrides/mods/YungsBetterDesertTemples-26.2-Fabric-5.1.1-minepack.1.jar'),
    @('source-yungs-better-dungeons','overrides/mods/YungsBetterDungeons-26.2-Fabric-6.1.1-minepack.1.jar'),
    @('source-yungs-better-jungle-temples','overrides/mods/YungsBetterJungleTemples-26.2-Fabric-4.1.1-minepack.1.jar'),
    @('source-yungs-better-mineshafts','overrides/mods/YungsBetterMineshafts-26.2-Fabric-6.1.1-minepack.1.jar'),
    @('source-yungs-better-fortresses','overrides/mods/YungsBetterNetherFortresses-26.2-Fabric-4.1.1-minepack.1.jar'),
    @('source-yungs-better-strongholds','overrides/mods/YungsBetterStrongholds-26.2-Fabric-6.1.1-minepack.1.jar')
)
foreach ($mapping in $yungsMap) { $artifactMap.Add("$($mapping[0])|$($mapping[1])|$($ledger[$mapping[1]].Sha512)") }
$artifactMap.Add("source-yungs-desert-temples-minepack2|$desertOutput|$((Get-FileHash -LiteralPath $desertOutput -Algorithm SHA512).Hash)")
$artifactMapPath = Join-Path $bundle 'artifact-mapping.txt'
Assert-NewPath $artifactMapPath
[IO.File]::WriteAllLines((Join-Path $repo $artifactMapPath), $artifactMap, [Text.UTF8Encoding]::new($false))

$manifestLines.Clear()
foreach ($file in Get-ChildItem -LiteralPath $bundle -Recurse -File | Sort-Object FullName) {
    if ($file.FullName -eq [IO.Path]::GetFullPath((Join-Path $repo $manifestFile))) { continue }
    $relative = [IO.Path]::GetRelativePath([IO.Path]::GetFullPath((Join-Path $repo $bundle)), $file.FullName).Replace('\','/')
    if ($relative -match '(^|/)(\.git|\.gradle|build|cache)(/|$)') { throw "Forbidden generated content in source kit: $relative" }
    $manifestLines.Add("$relative|$($file.Length)|$((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)")
}
[IO.File]::WriteAllLines((Join-Path $repo $manifestFile), $manifestLines, [Text.UTF8Encoding]::new($false))

@(
    "baseline_mrpack_sha512=$actualBaselineHash"
    "xali_input_sha512=$($ledger[$xaliEntryName].Sha512)"
    "xali_candidate_sha512=$((Get-FileHash -LiteralPath $xaliOutput -Algorithm SHA512).Hash)"
    "desert2_source_jar_sha256=$((Get-FileHash -LiteralPath $builtJar -Algorithm SHA256).Hash)"
    "desert2_authoring_jar_sha512=$((Get-FileHash -LiteralPath $desertOutput -Algorithm SHA512).Hash)"
    "source_kit_files=$($manifestLines.Count)"
    "source_kit_manifest_sha256=$((Get-FileHash -LiteralPath $manifestFile -Algorithm SHA256).Hash)"
    "xali_payload_files=$fileCount case_renames=$($caseRows.Count) cit_refs=$citChanged axolotl=$axolotlChanged tiles=$tileChanged biome_files=$biomeFilesChanged stale_biomes=$biomeMissingRemoved invalid_biomes=$biomeInvalidRemoved"
) | Set-Content -LiteralPath (Join-Path $work 'build-result.txt') -Encoding utf8
Get-Content -LiteralPath (Join-Path $work 'build-result.txt')

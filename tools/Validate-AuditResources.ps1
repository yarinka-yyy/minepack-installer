[CmdletBinding()]
param([switch]$RequireRelease)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location $repo

function Assert-Equal([object]$Actual, [object]$Expected, [string]$Label) {
    if ($Actual -cne $Expected) { throw "$Label mismatch: expected '$Expected', found '$Actual'" }
}
function Get-EntryBytes([IO.Compression.ZipArchive]$Zip, [string]$Name) {
    $entries = @($Zip.Entries | Where-Object { $_.FullName -ceq $Name })
    if ($entries.Count -ne 1) { throw "Expected one ZIP entry '$Name', found $($entries.Count)." }
    $stream = $entries[0].Open()
    $memory = [IO.MemoryStream]::new()
    try { $stream.CopyTo($memory); return ,$memory.ToArray() }
    finally { $stream.Dispose(); $memory.Dispose() }
}
function Open-ZipBytes([byte[]]$Bytes) {
    $memory = [IO.MemoryStream]::new($Bytes)
    try { return [pscustomobject]@{ Memory = $memory; Zip = [IO.Compression.ZipArchive]::new($memory, [IO.Compression.ZipArchiveMode]::Read, $false) } }
    catch { $memory.Dispose(); throw }
}
function Get-Utf8([byte[]]$Bytes) {
    $offset = if ($Bytes.Length -ge 3 -and $Bytes[0] -eq 239 -and $Bytes[1] -eq 187 -and $Bytes[2] -eq 191) { 3 } else { 0 }
    [Text.UTF8Encoding]::new($false, $true).GetString($Bytes, $offset, $Bytes.Length - $offset)
}
function Get-BytesHash([byte[]]$Bytes, [string]$Algorithm) {
    $hasher = if ($Algorithm -eq 'SHA512') { [Security.Cryptography.SHA512]::Create() } else { [Security.Cryptography.SHA256]::Create() }
    try { [Convert]::ToHexString($hasher.ComputeHash($Bytes)) } finally { $hasher.Dispose() }
}
function Assert-RelativeEntry([string]$Name) {
    $normalized = $Name.Replace('\', '/')
    if ([IO.Path]::IsPathRooted($normalized) -or $normalized.Contains(':') -or
        @($normalized.Split('/') | Where-Object { $_ -eq '..' -or $_ -eq '.' }).Count -gt 0) {
        throw "Unsafe archive path: $Name"
    }
}
function Replace-Once([string]$Text, [string]$Old, [string]$New, [string]$Label) {
    $count = 0
    $at = 0
    while (($at = $Text.IndexOf($Old, $at, [StringComparison]::Ordinal)) -ge 0) { $count++; $at += $Old.Length }
    if ($count -ne 1) { throw "$Label expected one exact input, found $count." }
    $Text.Replace($Old, $New)
}
function Convert-BiomeToken([string]$Token) {
    $normalized = $Token.Replace('\:', ':').Replace('\!', '!').TrimStart([char[]]@([char]33))
    if ($normalized.Contains(':')) { return $normalized }
    "minecraft:$normalized"
}
function Get-ExpectedBiomeText([string]$Text, [string]$Path, [hashtable]$RemoveByPath, [hashtable]$InvalidByPath) {
    $match = [regex]::Match($Text, '(?m)^biomes=(.*?)(\r?)$')
    if (!$match.Success -or [regex]::Matches($Text, '(?m)^biomes=').Count -ne 1) { throw "Expected one biomes property in $Path" }
    $value = $match.Groups[1].Value
    $trailing = $value.EndsWith('\ ', [StringComparison]::Ordinal)
    if ($trailing) { $value = $value.Substring(0, $value.Length - 2) }
    $tokens = @([regex]::Split($value, '\\ '))
    $negated = $tokens.Count -gt 0 -and ($tokens[0].StartsWith('\!', [StringComparison]::Ordinal) -or $tokens[0].StartsWith('!', [StringComparison]::Ordinal))
    $kept = [Collections.Generic.List[string]]::new()
    $removedMissing = 0
    $removedInvalid = 0
    foreach ($token in $tokens) {
        if ($token.Length -eq 0) { continue }
        if ($InvalidByPath.ContainsKey($Path) -and ($token -ceq '\!snowy_taiga' -or $token -ceq '!snowy_taiga')) { $removedInvalid++; continue }
        $id = Convert-BiomeToken $token
        if ($RemoveByPath.ContainsKey($Path) -and $RemoveByPath[$Path].Contains($id)) { $removedMissing++; continue }
        if ($kept.Count -eq 0 -and ($token.StartsWith('\!', [StringComparison]::Ordinal) -or $token.StartsWith('!', [StringComparison]::Ordinal))) {
            $token = $token.TrimStart([char[]]@([char]92,[char]33))
        }
        $kept.Add($token)
    }
    if ($kept.Count -eq 0) { throw "Biome rule would become empty: $Path" }
    if ($negated) { $kept[0] = '\!' + $kept[0].TrimStart([char[]]@([char]92,[char]33)) }
    $newValue = [string]::Join('\ ', $kept)
    if ($trailing) { $newValue += '\ ' }
    $line = 'biomes=' + $newValue + $match.Groups[2].Value
    $expected = $Text.Substring(0, $match.Index) + $line + $Text.Substring($match.Index + $match.Length)
    [pscustomobject]@{ Text = $expected; Missing = $removedMissing; Invalid = $removedInvalid }
}
function Get-JsonValueCount([string]$Text, [string]$Value) {
    $needle = '"' + $Value + '"'
    $count = 0
    $at = 0
    while (($at = $Text.IndexOf($needle, $at, [StringComparison]::Ordinal)) -ge 0) { $count++; $at += $needle.Length }
    $count
}

$baselinePath = 'releases/vanilla-2-plus/vanilla-2-plus-0.19.6.mrpack'
$baselineSha512 = '5FEC73C5E023C251D35E20A32C181C0679669972D8945642D36725FFE575969473CA2F7AB75699D4E7ED91CB78AE57628570189A7548FE3898968BD663467D2B'
$baselineFull = Join-Path $repo $baselinePath
Assert-Equal (Get-FileHash -LiteralPath $baselineFull -Algorithm SHA512).Hash $baselineSha512 'Immutable 0.19.6 archive SHA-512'
$publicZip = 'artifacts/MinePack-Installer-1.5.0-win-x64.zip'
Assert-Equal (Get-FileHash -LiteralPath (Join-Path $repo $publicZip) -Algorithm SHA256).Hash '0BA6607307F8C9F43A596FC879756FBB006B0D6DCF5777A0AEC7BAFFB9245A65' 'Immutable public ZIP SHA-256'

$work = 'artifacts/verify-plan022'
$evidence = 'artifacts/verify-plan021'
$bundle = 'third-party/yungs-sources'
$bundleManifest = Join-Path $bundle 'source-snapshot-manifest.txt'
$frozen = 'artifacts/verify-plan021/frozen-yungs-source'
$frozenManifest = 'artifacts/verify-plan021/source-snapshot-manifest.txt'
$inputsPath = 'artifacts/verify-plan021/pinned-release-inputs.txt'
$baselineZip = [IO.Compression.ZipFile]::OpenRead($baselineFull)
$xaliSource = $null
$xaliSourceMemory = $null
$releaseZip = $null
$releaseMrpack = $null
try {
    $ledger = @{}
    foreach ($row in Get-Content -LiteralPath $inputsPath | Where-Object { $_ -match '^overrides/' }) {
        $fields = $row.Split('|')
        if ($fields.Count -ne 3 -or $ledger.ContainsKey($fields[0])) { throw "Malformed or duplicate pinned entry: $row" }
        Assert-RelativeEntry $fields[0]
        $ledger[$fields[0]] = [pscustomobject]@{ Size = [long]$fields[1]; Sha512 = $fields[2] }
    }
    Assert-Equal $ledger.Count 8 'Pinned archive input count'
    foreach ($name in $ledger.Keys) {
        $bytes = Get-EntryBytes $baselineZip $name
        Assert-Equal $bytes.Length $ledger[$name].Size "Pinned size $name"
        Assert-Equal (Get-BytesHash $bytes 'SHA512') $ledger[$name].Sha512 "Pinned SHA-512 $name"
    }

    $xaliEntryName = 'overrides/resourcepacks/xalis-enhanced-vanilla-26.2-minepack.1.zip'
    $xaliSourceHandle = Open-ZipBytes (Get-EntryBytes $baselineZip $xaliEntryName)
    $xaliSourceMemory = $xaliSourceHandle.Memory
    $xaliSource = $xaliSourceHandle.Zip
    $xaliPath = 'pack/vanilla-2-plus/resourcepacks/xalis-enhanced-vanilla-26.2-minepack.2.zip'
    $xaliFull = Join-Path $repo $xaliPath
    $candidateXali = [IO.Compression.ZipFile]::OpenRead($xaliFull)

    $caseRows = @(Get-Content -LiteralPath (Join-Path $evidence 'uppercase-resource-proof.txt') | Where-Object { $_ -match '^assets/.*\|assets/.*\|\d+\|[0-9A-Fa-f]{64}$' })
    Assert-Equal $caseRows.Count 42 'Uppercase path mapping count'
    $caseMap = @{}
    $lowercasePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($row in $caseRows) {
        $f = $row.Split('|')
        if ($f.Count -ne 4 -or !$lowercasePaths.Add($f[1])) { throw "Case target collision or malformed row: $row" }
        $caseMap[$f[0]] = $f[1]
    }

    $citByPath = @{}
    $citRows = @(Get-Content -LiteralPath (Join-Path $evidence 'ctm-reference-mapping.txt') | Where-Object { $_.StartsWith('CITREF|', [StringComparison]::Ordinal) })
    Assert-Equal $citRows.Count 37 'CIT reference inventory count'
    foreach ($row in $citRows) {
        $f = $row.Split('|')
        $before = ($f | Where-Object { $_.StartsWith('before=', [StringComparison]::Ordinal) }) -replace '^before=', ''
        $after = ($f | Where-Object { $_.StartsWith('after=', [StringComparison]::Ordinal) }) -replace '^after=', ''
        $png = ($f | Where-Object { $_.StartsWith('png=', [StringComparison]::Ordinal) }) -replace '^png=', ''
        $registered = ($f | Where-Object { $_.StartsWith('registered=', [StringComparison]::Ordinal) }) -replace '^registered=', ''
        if (!$citByPath.ContainsKey($f[1])) { $citByPath[$f[1]] = [Collections.Generic.List[object]]::new() }
        $citByPath[$f[1]].Add([pscustomobject]@{ Before = $before; After = $after; Png = $png; Registered = $registered })
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
    $removeByPath = @{}
    foreach ($row in Get-Content -LiteralPath (Join-Path $evidence 'candidate-biome-inventory.txt') | Where-Object { $_ -match '^MISSING_SOURCE\|minecraft:[^|]+\|assets/minecraft/optifine/ctm/' }) {
        $f = $row.Split('|')
        if (!$removeByPath.ContainsKey($f[2])) { $removeByPath[$f[2]] = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal) }
        [void]$removeByPath[$f[2]].Add($f[1])
    }
    $invalidByPath = @{}
    foreach ($row in Get-Content -LiteralPath (Join-Path $evidence 'candidate-biome-inventory.txt') | Where-Object { $_ -match '^INVALID\|assets/minecraft/optifine/ctm/.*\|biomes\|!snowy_taiga$' }) {
        $f = $row.Split('|')
        $invalidByPath[$f[1]] = $true
    }
    $expectedContentFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in $citByPath.Keys) { [void]$expectedContentFiles.Add($path) }
    foreach ($path in $axolotlChanges.Keys) { [void]$expectedContentFiles.Add($path) }
    foreach ($path in $tileChanges.Keys) { [void]$expectedContentFiles.Add($path) }
    foreach ($path in $removeByPath.Keys) { [void]$expectedContentFiles.Add($path) }
    foreach ($path in $invalidByPath.Keys) { [void]$expectedContentFiles.Add($path) }
    $missingRemoved = 0
    $invalidRemoved = 0
    $changedFiles = 0
    $jsonCount = 0
    $seenCandidate = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($sourceEntry in $xaliSource.Entries) {
        if ($sourceEntry.FullName.EndsWith('/')) { throw 'Expected no directory entries in pinned Xali archive.' }
        Assert-RelativeEntry $sourceEntry.FullName
        $targetName = if ($caseMap.ContainsKey($sourceEntry.FullName)) { $caseMap[$sourceEntry.FullName] } else { $sourceEntry.FullName }
        if (!$seenCandidate.Add($targetName)) { throw "Candidate Xali path collision: $targetName" }
        $original = Get-EntryBytes $xaliSource $sourceEntry.FullName
        $expected = $original
        if ($citByPath.ContainsKey($sourceEntry.FullName)) {
            $text = Get-Utf8 $expected
            foreach ($mapping in $citByPath[$sourceEntry.FullName]) {
                $text = Replace-Once $text ('"' + $mapping.Before + '"') ('"' + $mapping.After + '"') "CIT $($sourceEntry.FullName)"
                if (!$candidateXali.GetEntry($mapping.Png)) { throw "Mapped CTM artwork is absent: $($mapping.Png)" }
                $assetBytes = Get-EntryBytes $candidateXali $mapping.Png
                if ((Get-BytesHash $assetBytes 'SHA256') -eq '') { throw 'Unexpected empty hash result.' }
            }
            $expected = [Text.UTF8Encoding]::new($false).GetBytes($text)
        }
        if ($axolotlChanges.ContainsKey($sourceEntry.FullName)) {
            $change = $axolotlChanges[$sourceEntry.FullName]
            $expected = [Text.UTF8Encoding]::new($false).GetBytes((Replace-Once (Get-Utf8 $expected) $change[0] $change[1] "Axolotl $($sourceEntry.FullName)"))
        }
        if ($tileChanges.ContainsKey($sourceEntry.FullName)) {
            $change = $tileChanges[$sourceEntry.FullName]
            $expected = [Text.UTF8Encoding]::new($false).GetBytes((Replace-Once (Get-Utf8 $expected) $change[0] $change[1] "Tile $($sourceEntry.FullName)"))
        }
        if ($removeByPath.ContainsKey($sourceEntry.FullName) -or $invalidByPath.ContainsKey($sourceEntry.FullName)) {
            $biome = Get-ExpectedBiomeText (Get-Utf8 $expected) $sourceEntry.FullName $removeByPath $invalidByPath
            $missingRemoved += $biome.Missing
            $invalidRemoved += $biome.Invalid
            $expected = [Text.UTF8Encoding]::new($false).GetBytes($biome.Text)
        }
        $actual = Get-EntryBytes $candidateXali $targetName
        if (![Linq.Enumerable]::SequenceEqual([byte[]]$expected, [byte[]]$actual)) { throw "Unexpected Xali payload change: $($sourceEntry.FullName) -> $targetName" }
        if (![Linq.Enumerable]::SequenceEqual([byte[]]$expected, [byte[]]$original)) { $changedFiles++ }
        if ($sourceEntry.FullName.EndsWith('.json', [StringComparison]::OrdinalIgnoreCase)) {
            $jsonCount++
            try { $doc = [Text.Json.JsonDocument]::Parse((Get-Utf8 $actual)); $doc.Dispose() } catch { throw "Candidate JSON is invalid: $targetName" }
        }
    }
    Assert-Equal $xaliSource.Entries.Count 2432 'Pinned Xali entry count'
    Assert-Equal $candidateXali.Entries.Count 2433 'Candidate Xali entry count including notice'
    Assert-Equal $jsonCount 438 'Xali all-namespace JSON count'
    Assert-Equal $missingRemoved 273 'Removed absent vanilla biome IDs'
    Assert-Equal $invalidRemoved 3 'Removed invalid inner biome tokens'
    Assert-Equal $changedFiles $expectedContentFiles.Count 'Changed original Xali file count'
    Assert-Equal $changedFiles 89 'Expected distinct changed original Xali file count'
    $notice = Get-Utf8 (Get-EntryBytes $candidateXali 'MINEPACK-CHANGES.txt')
    if ($notice -notmatch 'pinned Xali \.minepack\.1' -or $notice -notmatch '2026-10-02') { throw 'Xali modification notice is incomplete.' }
    foreach ($row in $citRows) {
        $f = $row.Split('|')
        $modelPath = $f[1]
        $entry = $candidateXali.GetEntry($modelPath)
        if (!$entry) { throw "Mapped CIT model is absent: $modelPath" }
        $text = Get-Utf8 (Get-EntryBytes $candidateXali $modelPath)
        $before = ($f | Where-Object { $_.StartsWith('before=', [StringComparison]::Ordinal) }) -replace '^before=', ''
        $after = ($f | Where-Object { $_.StartsWith('after=', [StringComparison]::Ordinal) }) -replace '^after=', ''
        if ((Get-JsonValueCount $text $before) -ne 0 -or (Get-JsonValueCount $text $after) -lt 1) { throw "CIT alias substitution did not survive in $modelPath" }
    }
    $candidateNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $candidateXali.Entries) {
        if (!$candidateNames.Add($entry.FullName)) { throw "Case-insensitive Xali candidate collision: $($entry.FullName)" }
        if ($entry.FullName -cne 'MINEPACK-CHANGES.txt' -and !$seenCandidate.Contains($entry.FullName)) { throw "Unexpected Xali candidate entry: $($entry.FullName)" }
    }
    $xaliSourceMemory.Dispose()
    $xaliSourceMemory = $null

    $sourceManifestHash = '71319FCB9C79F107ED7255DC26113F4C2DF4DB1E371B65BA10C7EF0C63E4A3B6'
    Assert-Equal (Get-FileHash -LiteralPath (Join-Path $repo $frozenManifest) -Algorithm SHA256).Hash $sourceManifestHash 'Frozen source manifest SHA-256'
    $frozenRows = @(Get-Content -LiteralPath (Join-Path $repo $frozenManifest) | Where-Object { $_ -match '^[^|]+\|\d+\|[0-9A-Fa-f]{64}$' })
    Assert-Equal $frozenRows.Count 1787 'Frozen source file count'
    $frozenMap = @{}
    foreach ($row in $frozenRows) {
        $f = $row.Split('|')
        if ($frozenMap.ContainsKey($f[0])) { throw "Duplicate frozen source path: $($f[0])" }
        $frozenMap[$f[0]] = [pscustomobject]@{ Size = [long]$f[1]; Sha256 = $f[2] }
        $frozenPath = Join-Path (Join-Path $repo $frozen) $f[0]
        if (!(Test-Path -LiteralPath $frozenPath -PathType Leaf)) { throw "Missing frozen source: $($f[0])" }
        $file = Get-Item -LiteralPath $frozenPath -Force
        if ($file.Length -ne $frozenMap[$f[0]].Size -or (Get-FileHash -LiteralPath $frozenPath -Algorithm SHA256).Hash -ne $frozenMap[$f[0]].Sha256) { throw "Frozen source identity mismatch: $($f[0])" }
        $kitPath = Join-Path (Join-Path $repo $bundle) $f[0]
        if (!(Test-Path -LiteralPath $kitPath -PathType Leaf)) { throw "Source kit omitted frozen file: $($f[0])" }
        $kitFile = Get-Item -LiteralPath $kitPath
        if ($kitFile.Length -ne $file.Length -or (Get-FileHash -LiteralPath $kitPath -Algorithm SHA256).Hash -ne $frozenMap[$f[0]].Sha256) { throw "Source kit changed frozen file: $($f[0])" }
    }
    Assert-Equal @(Get-ChildItem -LiteralPath (Join-Path $repo $frozen) -Recurse -File -Force).Count 1787 'Actual frozen source inventory count'
    $kitManifestRows = @(Get-Content -LiteralPath (Join-Path $repo $bundleManifest) | Where-Object { $_ -match '^[^|]+\|\d+\|[0-9A-Fa-f]{64}$' })
    if ($kitManifestRows.Count -lt 1787) { throw 'Source kit manifest omits frozen source entries.' }
    foreach ($line in $kitManifestRows) {
        $fields = $line.Split('|')
        $candidatePath = Join-Path (Join-Path $repo $bundle) $fields[0]
        if (!(Test-Path -LiteralPath $candidatePath -PathType Leaf)) { throw "Source kit manifest lists missing file: $($fields[0])" }
        $item = Get-Item -LiteralPath $candidatePath
        if ($item.Length -ne [long]$fields[1] -or (Get-FileHash -LiteralPath $candidatePath -Algorithm SHA256).Hash -ne $fields[2]) { throw "Source kit manifest mismatch: $($fields[0])" }
    }
    Assert-Equal @(Get-ChildItem -LiteralPath (Join-Path $repo $bundle) -Recurse -File -Force).Count ($kitManifestRows.Count + 1) 'Source kit exact file inventory count'
    $sourceDesertBase = Join-Path (Join-Path $repo $bundle) 'source-yungs-desert-temples'
    $sourceDesert2 = Join-Path (Join-Path $repo $bundle) 'source-yungs-desert-temples-minepack2'
    $baseDesertFiles = @(Get-ChildItem -LiteralPath $sourceDesertBase -Recurse -File -Force)
    $candidateDesertFiles = @(Get-ChildItem -LiteralPath $sourceDesert2 -Recurse -File -Force)
    Assert-Equal $candidateDesertFiles.Count $baseDesertFiles.Count 'Desert .2 source file count'
    foreach ($baseFile in $baseDesertFiles) {
        $relative = [IO.Path]::GetRelativePath($sourceDesertBase, $baseFile.FullName).Replace('\','/')
        $candidatePath = Join-Path $sourceDesert2 $relative
        if (!(Test-Path -LiteralPath $candidatePath -PathType Leaf)) { throw "Desert .2 source omitted $relative" }
        $baseBytes = [IO.File]::ReadAllBytes($baseFile.FullName)
        $candidateBytes = [IO.File]::ReadAllBytes($candidatePath)
        if ($relative -eq 'gradle.properties') {
            $expectedText = [regex]::new('(?m)^version=.*$').Replace((Get-Utf8 $baseBytes), 'version=5.1.1-minepack.2', 1)
            if (![Linq.Enumerable]::SequenceEqual([byte[]][Text.UTF8Encoding]::new($false).GetBytes($expectedText), [byte[]]$candidateBytes)) { throw 'Desert .2 source changed more than its version property.' }
        }
        elseif ($relative -eq 'Common/src/main/resources/data/betterdeserttemples/advancement/temple_clear.json') {
            $expectedText = Replace-Once (Get-Utf8 $baseBytes) '"type": "minecraft:husk"' '"entity_type": "minecraft:husk"' 'Desert source advancement edit'
            if (![Linq.Enumerable]::SequenceEqual([byte[]][Text.UTF8Encoding]::new($false).GetBytes($expectedText), [byte[]]$candidateBytes)) { throw 'Desert .2 source advancement contains an unexpected edit.' }
        }
        elseif (![Linq.Enumerable]::SequenceEqual([byte[]]$baseBytes, [byte[]]$candidateBytes)) { throw "Desert .2 source changed outside its version and advancement: $relative" }
    }
    foreach ($license in @('LICENSE-GPL-3.0.txt','LICENSE-LGPL-3.0.txt')) {
        if (!(Test-Path -LiteralPath (Join-Path (Join-Path $repo $bundle) $license) -PathType Leaf)) { throw "Source-kit license missing: $license" }
    }
    $prohibited = Get-ChildItem -LiteralPath (Join-Path $repo $bundle) -Recurse -Force | Where-Object { $_.FullName -match '[\\/](\.git|\.gradle|build|cache)([\\/]|$)' }
    if ($prohibited) { throw "Generated or cache content in source kit: $($prohibited[0].FullName)" }
    $credentialPattern = '(?im)^(curseforgeApiKey|modrinthToken|ossrhToken|ossrhTokenPassword)\s*=\s*(.+)$'
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repo $bundle) -Recurse -File -Include 'gradle.properties','*.gradle','*.gradle.kts') {
        if ([regex]::IsMatch([IO.File]::ReadAllText($file.FullName), $credentialPattern)) { throw "Nonempty publishing credential property found in $($file.Name)" }
    }

    $oldDesertName = 'overrides/mods/YungsBetterDesertTemples-26.2-Fabric-5.1.1-minepack.1.jar'
    $oldDesertHandle = Open-ZipBytes (Get-EntryBytes $baselineZip $oldDesertName)
    $oldDesert = $oldDesertHandle.Zip
    $desertPath = 'pack/vanilla-2-plus/mods/YungsBetterDesertTemples-26.2-Fabric-5.1.1-minepack.2.jar'
    $desertFull = Join-Path $repo $desertPath
    $newDesert = [IO.Compression.ZipFile]::OpenRead($desertFull)
    $expectedJarEntries = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($expectedName in @('META-INF/MANIFEST.MF','fabric.mod.json','data/betterdeserttemples/advancement/temple_clear.json')) { [void]$expectedJarEntries.Add($expectedName) }
    if ($oldDesert.Entries.Count -ne $newDesert.Entries.Count) { throw 'Desert JAR entry count changed.' }
    foreach ($entry in $oldDesert.Entries) {
        $oldBytes = Get-EntryBytes $oldDesert $entry.FullName
        $newBytes = Get-EntryBytes $newDesert $entry.FullName
        if ($expectedJarEntries.Contains($entry.FullName)) {
            if ([Linq.Enumerable]::SequenceEqual([byte[]]$oldBytes, [byte[]]$newBytes)) { throw "Expected Desert .2 change is absent: $($entry.FullName)" }
            if ($entry.FullName -eq 'data/betterdeserttemples/advancement/temple_clear.json') {
                $oldJson = Get-Utf8 $oldBytes
                $newJson = Get-Utf8 $newBytes
                Assert-Equal (Replace-Once $oldJson '"type": "minecraft:husk"' '"entity_type": "minecraft:husk"' 'Accepted advancement edit') $newJson 'Desert advancement JSON'
                $parsed = [Text.Json.JsonDocument]::Parse((Get-Utf8 $newBytes))
                try {
                    $predicate = $parsed.RootElement.GetProperty('criteria').GetProperty('pharaoh_killed').GetProperty('conditions').GetProperty('entity')[0].GetProperty('predicate')
                    Assert-Equal $predicate.GetProperty('entity_type').GetString() 'minecraft:husk' 'Desert entity type predicate'
                    if ($newJson -match '"type"\s*:\s*"minecraft:husk"') { throw 'Old unsupported predicate.type remains.' }
                } finally { $parsed.Dispose() }
            }
            elseif ($entry.FullName -eq 'fabric.mod.json') {
                $oldJson = Get-Utf8 $oldBytes
                $newJson = Get-Utf8 $newBytes
                $expectedJson = Replace-Once $oldJson '26.2-Fabric-5.1.1-minepack.1' '26.2-Fabric-5.1.1-minepack.2' 'Desert Fabric metadata version'
                Assert-Equal $expectedJson $newJson 'Desert Fabric metadata apart from version'
            }
            else {
                $oldManifest = Get-Utf8 $oldBytes
                $newManifest = Get-Utf8 $newBytes
                $oldNormalized = [regex]::Replace($oldManifest, '(?m)^(Specification-Version|Implementation-Version): .*$', '$1: VERSION_PLACEHOLDER')
                $newNormalized = [regex]::Replace($newManifest, '(?m)^(Specification-Version|Implementation-Version): .*$', '$1: VERSION_PLACEHOLDER')
                $oldNormalized = $oldNormalized.Replace('Fabric-Gradle-Version: 9.2.0','Fabric-Gradle-Version: GRADLE_PLACEHOLDER')
                $newNormalized = $newNormalized.Replace('Fabric-Gradle-Version: 9.4.1','Fabric-Gradle-Version: GRADLE_PLACEHOLDER')
                Assert-Equal $oldNormalized $newNormalized 'Desert JAR manifest except accepted version/build tool values'
            }
        }
        elseif (![Linq.Enumerable]::SequenceEqual([byte[]]$oldBytes, [byte[]]$newBytes)) { throw "Unexpected Desert JAR payload change: $($entry.FullName)" }
    }
    if ($expectedJarEntries.Count -ne 3) { throw 'Desert JAR change allowlist is not the accepted three-entry set.' }
    $oldDesertHandle.Memory.Dispose()

    $releasePath = 'releases/vanilla-2-plus/vanilla-2-plus-0.19.7.mrpack'
    $releaseFull = Join-Path $repo $releasePath
    if ($RequireRelease -and !(Test-Path -LiteralPath $releaseFull -PathType Leaf)) { throw "Required candidate release is missing: $releasePath" }
    $releaseSha512 = 'not-exported'
    $releaseOverrideCount = 0
    $indexFilesCount = 0
    if (Test-Path -LiteralPath $releaseFull -PathType Leaf) {
        $releaseSha512 = (Get-FileHash -LiteralPath $releaseFull -Algorithm SHA512).Hash
        $releaseMrpack = [IO.Compression.ZipFile]::OpenRead($releaseFull)
        $indexBytes = Get-EntryBytes $releaseMrpack 'modrinth.index.json'
        $index = [Text.Json.JsonDocument]::Parse((Get-Utf8 $indexBytes))
        try {
            $root = $index.RootElement
            Assert-Equal $root.GetProperty('name').GetString() 'MinePack Vanilla 2 Plus' 'Candidate pack name'
            Assert-Equal $root.GetProperty('versionId').GetString() '0.19.7' 'Candidate pack version'
            Assert-Equal $root.GetProperty('game').GetString() 'minecraft' 'Candidate game ID'
            Assert-Equal $root.GetProperty('dependencies').GetProperty('minecraft').GetString() '26.2' 'Candidate Minecraft version'
            Assert-Equal $root.GetProperty('dependencies').GetProperty('fabric-loader').GetString() '0.19.5' 'Candidate Fabric version'
            $files = $root.GetProperty('files')
            Assert-Equal $files.GetArrayLength() 67 'Candidate Modrinth indexed file count'
            $indexFilesCount = $files.GetArrayLength()
            foreach ($file in $files.EnumerateArray()) {
                if ($file.GetProperty('path').GetString() -match 'YungsBetterDesertTemples|xalis-enhanced-vanilla') { throw 'New Xali or Desert override was incorrectly emitted as an external Modrinth download.' }
            }
        } finally { $index.Dispose() }
        $releaseOverrideCount = @($releaseMrpack.Entries | Where-Object { $_.FullName.StartsWith('overrides/', [StringComparison]::Ordinal) -and !$_.FullName.EndsWith('/') }).Count
        $newXaliOverride = 'overrides/resourcepacks/xalis-enhanced-vanilla-26.2-minepack.2.zip'
        $newDesertOverride = 'overrides/mods/YungsBetterDesertTemples-26.2-Fabric-5.1.1-minepack.2.jar'
        if (!$releaseMrpack.GetEntry($newXaliOverride) -or !$releaseMrpack.GetEntry($newDesertOverride)) { throw 'Candidate mrpack lacks current .2 resource overrides.' }
        if ($releaseMrpack.GetEntry('overrides/resourcepacks/xalis-enhanced-vanilla-26.2-minepack.1.zip') -or
            $releaseMrpack.GetEntry('overrides/mods/YungsBetterDesertTemples-26.2-Fabric-5.1.1-minepack.1.jar')) { throw 'Candidate mrpack still includes superseded current .1 overrides.' }
        if (![Linq.Enumerable]::SequenceEqual([byte[]](Get-EntryBytes $releaseMrpack $newXaliOverride), [byte[]][IO.File]::ReadAllBytes($xaliFull)) -or
            ![Linq.Enumerable]::SequenceEqual([byte[]](Get-EntryBytes $releaseMrpack $newDesertOverride), [byte[]][IO.File]::ReadAllBytes($desertFull))) {
            throw 'Candidate mrpack override bytes differ from validated authoring artifacts.'
        }
        $oldIndex = [Text.Json.JsonDocument]::Parse((Get-Utf8 (Get-EntryBytes $baselineZip 'modrinth.index.json')))
        $newIndex = [Text.Json.JsonDocument]::Parse((Get-Utf8 $indexBytes))
        try {
            $oldIndexObject = (Get-Utf8 (Get-EntryBytes $baselineZip 'modrinth.index.json')) | ConvertFrom-Json -AsHashtable
            $newIndexObject = (Get-Utf8 $indexBytes) | ConvertFrom-Json -AsHashtable
            $oldIndexObject.versionId = 'VERSION_PLACEHOLDER'
            $newIndexObject.versionId = 'VERSION_PLACEHOLDER'
            Assert-Equal (ConvertTo-Json -InputObject $oldIndexObject -Depth 100 -Compress) (ConvertTo-Json -InputObject $newIndexObject -Depth 100 -Compress) 'Unchanged indexed dependency pins'
        } finally { $oldIndex.Dispose(); $newIndex.Dispose() }
        $allowedReplacedOverrides = @{
            'overrides/resourcepacks/xalis-enhanced-vanilla-26.2-minepack.1.zip' = 'overrides/resourcepacks/xalis-enhanced-vanilla-26.2-minepack.2.zip'
            'overrides/mods/YungsBetterDesertTemples-26.2-Fabric-5.1.1-minepack.1.jar' = 'overrides/mods/YungsBetterDesertTemples-26.2-Fabric-5.1.1-minepack.2.jar'
        }
        $oldOverrides = @($baselineZip.Entries | Where-Object { $_.FullName.StartsWith('overrides/', [StringComparison]::Ordinal) -and !$_.FullName.EndsWith('/') } | Select-Object -ExpandProperty FullName)
        $newOverrides = @($releaseMrpack.Entries | Where-Object { $_.FullName.StartsWith('overrides/', [StringComparison]::Ordinal) -and !$_.FullName.EndsWith('/') } | Select-Object -ExpandProperty FullName)
        $expectedOverrides = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($oldName in $oldOverrides) {
            if ($allowedReplacedOverrides.ContainsKey($oldName)) { [void]$expectedOverrides.Add($allowedReplacedOverrides[$oldName]) }
            else { [void]$expectedOverrides.Add($oldName) }
        }
        if ($expectedOverrides.Count -ne $newOverrides.Count -or @($newOverrides | Where-Object { !$expectedOverrides.Contains($_) }).Count -ne 0) {
            throw 'Candidate mrpack changed the override inventory outside the two approved resources.'
        }
        foreach ($oldName in $oldOverrides) {
            if (!$allowedReplacedOverrides.ContainsKey($oldName) -and
                ![Linq.Enumerable]::SequenceEqual([byte[]](Get-EntryBytes $baselineZip $oldName), [byte[]](Get-EntryBytes $releaseMrpack $oldName))) {
                throw "Unchanged override payload differs from 0.19.6: $oldName"
            }
        }
    }

    $output = @(
        'result=PASS'
        "historical_0196_sha512=$baselineSha512"
        "public_150_zip_sha256=$((Get-FileHash -LiteralPath (Join-Path $repo $publicZip) -Algorithm SHA256).Hash)"
        "candidate_xali_sha512=$((Get-FileHash -LiteralPath $xaliFull -Algorithm SHA512).Hash)"
        "candidate_desert_sha512=$((Get-FileHash -LiteralPath $desertFull -Algorithm SHA512).Hash)"
        "xali_regular_files=$($candidateXali.Entries.Count - 1) json_files=$jsonCount changed_original_files=$changedFiles cit_mappings=$($citRows.Count) biome_missing_removed=$missingRemoved biome_invalid_removed=$invalidRemoved"
        "frozen_source_files=$($frozenRows.Count) source_kit_manifest_sha256=$((Get-FileHash -LiteralPath (Join-Path $repo $bundleManifest) -Algorithm SHA256).Hash)"
        "candidate_mrpack_sha512=$releaseSha512 candidate_indexed_files=$indexFilesCount candidate_override_files=$releaseOverrideCount"
    )
    $validationPath = Join-Path $repo (Join-Path $work 'validation-result.txt')
    [IO.File]::WriteAllLines($validationPath, $output, [Text.UTF8Encoding]::new($false))
    $output
}
finally {
    if ($releaseMrpack) { $releaseMrpack.Dispose() }
    if ($candidateXali) { $candidateXali.Dispose() }
    if ($xaliSource) { $xaliSource.Dispose() }
    if ($baselineZip) { $baselineZip.Dispose() }
}

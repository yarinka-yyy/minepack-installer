[CmdletBinding()]
param([Parameter(Mandatory)][string] $InstallerZip)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$validator = Join-Path $PSScriptRoot '../../tools/Validate-InstallerZip.ps1'
$fixtures = Join-Path ([IO.Path]::GetTempPath()) ('minepack-zip-checks-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtures | Out-Null
try {
    foreach ($case in @(
        @{ Name = 'long-path'; Entry = 'MinePack/' + ('deep/' * 40) + 'SmallDungeonCeilingLampProcessor.java'; Error = 'path limits' },
        @{ Name = 'source-code'; Entry = 'MinePack/src/Program.cs'; Error = 'installer ZIP must' },
        @{ Name = 'traversal'; Entry = 'MinePack/../outside.txt'; Error = 'Unsafe Windows ZIP entry' },
        @{ Name = 'reserved-name'; Entry = 'MinePack/CON.txt'; Error = 'Unsafe Windows ZIP entry' },
        @{ Name = 'duplicate-case'; Entry = 'MinePack/minepack.installer.exe'; Error = 'Duplicate Windows ZIP entry' }
    )) {
        $path = Join-Path $fixtures ($case.Name + '.zip')
        $archive = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
        try {
            if ($case.Name -ne 'source-code') {
                $writer = [IO.StreamWriter]::new($archive.CreateEntry('MinePack/MinePack.Installer.exe').Open())
                try { $writer.Write('EXE fixture') } finally { $writer.Dispose() }
            }
            $writer = [IO.StreamWriter]::new($archive.CreateEntry($case.Entry).Open())
            try { $writer.Write('fixture') } finally { $writer.Dispose() }
        }
        finally { $archive.Dispose() }
        $failure = $null
        try { & $validator -ZipPath $path | Out-Null } catch { $failure = $_.Exception.Message }
        if ($null -eq $failure -or $failure -notmatch [regex]::Escape($case.Error)) {
            throw "ZIP validation did not reject $($case.Name) correctly: $failure"
        }
        Write-Output "PASS: rejected $($case.Name)"
    }
    & $validator -ZipPath $InstallerZip
}
finally { Remove-Item -LiteralPath $fixtures -Recurse -Force }

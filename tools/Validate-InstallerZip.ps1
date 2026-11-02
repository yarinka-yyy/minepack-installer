[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ZipPath,
    [string] $ExtractionDirectory = ('C:\Users\' + ('u' * 60) + '\OneDrive\Desktop\Downloads\' + [IO.Path]::GetFileNameWithoutExtension($ZipPath))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$destination = [IO.Path]::GetFullPath($ExtractionDirectory).TrimEnd('\')
$archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($ZipPath))
try {
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $roots = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $exeCount = 0
    $maximum = 0
    foreach ($entry in $archive.Entries) {
        $name = $entry.FullName.TrimEnd('/')
        $parts = $name.Split('/')
        if ($parts.Count -lt 2 -or @($parts | Where-Object {
            [string]::IsNullOrWhiteSpace($_) -or $_ -in @('.', '..') -or
            $_ -match '[\\<>:"|?*\x00-\x1f]' -or $_ -match '[. ]$' -or
            $_ -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)' -or $_.Length -gt 255
        }).Count -gt 0) { throw "Unsafe Windows ZIP entry: $($entry.FullName)" }
        if (-not $paths.Add($name)) { throw "Duplicate Windows ZIP entry: $name" }
        $null = $roots.Add($parts[0])
        $fullPath = $destination + '\' + $name.Replace('/', '\')
        $directory = if ($entry.FullName.EndsWith('/')) { $fullPath } else { [IO.Path]::GetDirectoryName($fullPath) }
        if ($fullPath.Length -ge 260 -or $directory.Length -gt 247) {
            throw "ZIP exceeds Windows Explorer path limits ($($fullPath.Length) characters): $name"
        }
        $maximum = [Math]::Max($maximum, $fullPath.Length)
        if ($parts.Count -eq 2 -and $parts[1] -ceq 'MinePack.Installer.exe' -and $entry.Length -gt 0) { $exeCount++ }
    }
    if ($roots.Count -ne 1 -or $exeCount -ne 1) { throw 'Installer ZIP must have one root folder and one non-empty MinePack.Installer.exe directly inside it.' }
    Write-Output "PASS: Windows Explorer path limits; $($archive.Entries.Count) entries, longest extraction path $maximum characters; installer EXE present."
}
finally { $archive.Dispose() }

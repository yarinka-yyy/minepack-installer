[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ZipPath,
    [Parameter(Mandatory)][string] $Destination
)

$ErrorActionPreference = 'Stop'
$ZipPath = [IO.Path]::GetFullPath($ZipPath)
$Destination = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $Destination) { throw "Use a new disposable extraction directory: $Destination" }
& (Join-Path $PSScriptRoot '../../tools/Validate-InstallerZip.ps1') -ZipPath $ZipPath -ExtractionDirectory $Destination
New-Item -ItemType Directory -Path $Destination | Out-Null
$shell = New-Object -ComObject Shell.Application
$source = $shell.NameSpace($ZipPath)
$target = $shell.NameSpace($Destination)
if ($null -eq $source -or $null -eq $target) { throw 'Windows compressed-folder namespace is unavailable.' }
$archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
try {
    # Silent copy through the same compressed-folder handler used by Explorer.
    $target.CopyHere($source.Items(), 1556)
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    do {
        $complete = $true
        foreach ($entry in $archive.Entries) {
            $path = Join-Path $Destination $entry.FullName.Replace('/', '\')
            if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $entry.Length) {
                $complete = $false
                break
            }
        }
        if (-not $complete) { Start-Sleep -Milliseconds 300 }
    } while (-not $complete -and [DateTime]::UtcNow -lt $deadline)
    if (-not $complete) { throw 'Windows Explorer extraction did not produce the complete package.' }
    foreach ($entry in $archive.Entries) {
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $expected = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $sha.Dispose(); $stream.Dispose() }
        $path = Join-Path $Destination $entry.FullName.Replace('/', '\')
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $expected) { throw "Extracted file differs: $path" }
    }
    Write-Output "PASS: Windows Explorer extracted all $($archive.Entries.Count) files with matching SHA-256 into $Destination"
}
finally {
    $archive.Dispose()
    foreach ($comObject in @($target, $source, $shell)) { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($comObject) | Out-Null }
}

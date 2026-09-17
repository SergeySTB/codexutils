param(
    [Parameter(Mandatory)] [string]$ExecutablePath,
    [Parameter(Mandatory)] [string]$IconPath,
    [int]$GroupResourceId = 3000,
    [int]$Language = 1033
)

$ErrorActionPreference = 'Stop'
$ExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$IconPath = (Resolve-Path -LiteralPath $IconPath).Path

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class NativeIconResources
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadLibraryEx(string file, IntPtr fileHandle, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindResourceEx(IntPtr module, IntPtr type, IntPtr name, ushort language);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr BeginUpdateResource(string file, bool deleteExistingResources);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool UpdateResource(IntPtr update, IntPtr type, IntPtr name,
        ushort language, byte[] data, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool EndUpdateResource(IntPtr update, bool discard);
}
'@

$module = [NativeIconResources]::LoadLibraryEx($ExecutablePath, [IntPtr]::Zero, 2)
if ($module -eq [IntPtr]::Zero) { throw [ComponentModel.Win32Exception]::new([Runtime.InteropServices.Marshal]::GetLastWin32Error()) }
try {
    if ([NativeIconResources]::FindResourceEx($module, [IntPtr]14, [IntPtr]$GroupResourceId, [uint16]$Language) -eq [IntPtr]::Zero) {
        throw "IExpress icon group $GroupResourceId ($Language) was not found"
    }
} finally {
    [void][NativeIconResources]::FreeLibrary($module)
}

$icon = [IO.File]::ReadAllBytes($IconPath)
$stream = [IO.MemoryStream]::new($icon, $false)
$reader = [IO.BinaryReader]::new($stream)
try {
    if ($reader.ReadUInt16() -ne 0 -or $reader.ReadUInt16() -ne 1) { throw 'Invalid ICO header' }
    $count = $reader.ReadUInt16()
    if ($count -lt 1) { throw 'ICO contains no images' }
    $entries = for ($index = 0; $index -lt $count; $index++) {
        [pscustomobject]@{
            Width = $reader.ReadByte(); Height = $reader.ReadByte(); Colors = $reader.ReadByte(); Reserved = $reader.ReadByte()
            Planes = $reader.ReadUInt16(); Bits = $reader.ReadUInt16(); Bytes = $reader.ReadUInt32(); Offset = $reader.ReadUInt32()
        }
    }
} finally {
    $reader.Dispose()
    $stream.Dispose()
}

$groupStream = [IO.MemoryStream]::new()
$groupWriter = [IO.BinaryWriter]::new($groupStream)
try {
    $groupWriter.Write([uint16]0)
    $groupWriter.Write([uint16]1)
    $groupWriter.Write([uint16]$entries.Count)
    for ($index = 0; $index -lt $entries.Count; $index++) {
        $entry = $entries[$index]
        if ([uint64]$entry.Offset + [uint64]$entry.Bytes -gt $icon.Length) { throw 'ICO image points outside the file' }
        $groupWriter.Write([byte]$entry.Width); $groupWriter.Write([byte]$entry.Height)
        $groupWriter.Write([byte]$entry.Colors); $groupWriter.Write([byte]$entry.Reserved)
        $groupWriter.Write([uint16]$entry.Planes); $groupWriter.Write([uint16]$entry.Bits)
        $groupWriter.Write([uint32]$entry.Bytes); $groupWriter.Write([uint16]($index + 1))
    }
    $group = $groupStream.ToArray()
} finally {
    $groupWriter.Dispose()
    $groupStream.Dispose()
}

$update = [NativeIconResources]::BeginUpdateResource($ExecutablePath, $false)
if ($update -eq [IntPtr]::Zero) { throw [ComponentModel.Win32Exception]::new([Runtime.InteropServices.Marshal]::GetLastWin32Error()) }
$committed = $false
try {
    for ($index = 0; $index -lt $entries.Count; $index++) {
        $entry = $entries[$index]
        $image = [byte[]]::new([int]$entry.Bytes)
        [Array]::Copy($icon, [int]$entry.Offset, $image, 0, $image.Length)
        if (-not [NativeIconResources]::UpdateResource($update, [IntPtr]3, [IntPtr]($index + 1), [uint16]$Language, $image, $image.Length)) {
            throw [ComponentModel.Win32Exception]::new([Runtime.InteropServices.Marshal]::GetLastWin32Error())
        }
    }
    if (-not [NativeIconResources]::UpdateResource($update, [IntPtr]14, [IntPtr]$GroupResourceId, [uint16]$Language, $group, $group.Length)) {
        throw [ComponentModel.Win32Exception]::new([Runtime.InteropServices.Marshal]::GetLastWin32Error())
    }
    if (-not [NativeIconResources]::EndUpdateResource($update, $false)) {
        throw [ComponentModel.Win32Exception]::new([Runtime.InteropServices.Marshal]::GetLastWin32Error())
    }
    $committed = $true
} finally {
    if (-not $committed) { [void][NativeIconResources]::EndUpdateResource($update, $true) }
}

Write-Output "Icon updated: $ExecutablePath"

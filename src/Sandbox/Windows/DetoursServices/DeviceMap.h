#pragma once

// Array of this structures describes drive mapping.
//   drive - a single letter drive name
//   path  - the path to be mapped
typedef struct
{
    wchar_t drive;
    LPCWSTR path;
} PathMapping;

// External function used to remap process devices based on the PathMapping.
// Can be called from managed code.
HANDLE
WINAPI
RemapDevices(
__in uint32_t mapCount,
__in PathMapping *mappings);

HANDLE CurrentMappingHandle();
bool ApplyMapping(HANDLE processHandle, HANDLE directoryHandle);
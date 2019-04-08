// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

#ifdef FEATURE_DEVICE_MAP
extern "C"
{
#include <mapper.h>
}
#endif

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

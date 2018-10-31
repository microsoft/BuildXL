#include "stdafx.h"

#include "buildXL_mem.h"
#include "DeviceMap.h"

HANDLE WINAPI RemapDevices(__in uint32_t, __in PathMapping *)
{
    return INVALID_HANDLE_VALUE;
}

HANDLE CurrentMappingHandle()
{
    return INVALID_HANDLE_VALUE;
}

bool ApplyMapping(HANDLE, HANDLE)
{
    return false;
}
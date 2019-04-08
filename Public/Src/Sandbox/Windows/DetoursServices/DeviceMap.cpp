// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "stdafx.h"

#include "DetoursHelpers.h"
#include "buildXL_mem.h"
#include "DeviceMap.h"

#ifdef FEATURE_DEVICE_MAP

using namespace std;

#ifdef _DEBUG
static void RealDbgSysError(DWORD err, LPCWSTR source)
{
    LPWSTR message = nullptr;
    FormatMessageW(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM,
        nullptr, err, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
        (LPWSTR)&message, 0, nullptr);
    Dbg(L"%s - Applying device map failed, error %08X\n:\t'%s'", source, (int)err, message);
    LocalFree(message);
}

#define DbgSysError(err, source) RealDbgSysError(err, source)
#else
#define DbgSysError(err, source)
#endif

// The \device\mup is the device used for the UNC shares
static const wchar_t cMupName[] = L"\\device\\mup\\";
static const size_t cMupNameSize = sizeof(cMupName) / sizeof(wchar_t);
static const int cMaxTranslatedPath = 4096;

// Wraps SINGLE_DEVICE_MAP structure
class SingleDeviceMap : public SINGLE_DEVICE_MAP
{
private:
    bool InitTarget(LPCWSTR path)
    {
        // Estimate the size for the translation. The path that starts with a drive will need
        // translating into the device name, which replaces the first two characters (drive), with
        // a string that can take up to cMaxTranslatedPath characters (an overkill,
        // of course, but can't estimate better). The UNC name will need a the mup name replacing
        // the starting double backslash.
        size_t len = wcslen(path);
        if (len <= 2)
        {
            // The path must have at least two character
            Dbg(L"DeviceMap::createMapping - Ignoring path that is too short: %s", path);
            return false;
        }

        // The first two characters will be replaced
        size_t size = len - 2;
        if (((*path >= L'a' && *path <= L'z') || (*path >= L'A' && *path <= L'Z')) && path[1] == L':')
        {
            // up to cMaxTranslatedPath characters will replace the two drive characters
            size += cMaxTranslatedPath;
        }
        else if (path[0] == L'\\' && path[1] == L'\\')
        {
            // mupSize characters will replace the two slash characters
            size += cMupNameSize;
        }
        else
        {
            // We do not support anything else -- need full path
            Dbg(L"DeviceMap::createMapping - Ignoring non-drive non UNC device path: %s", path);
            return false;
        }

        LPWSTR target = (LPWSTR)new wchar_t[size];
        wchar_t drive[3];
        drive[1] = L':';
        drive[2] = L'\0';
        size_t headLen = 0;

        if (path[1] == L':')
        {
            // If we have a drive call QueryDosDevice to convert the drive letter to NT path
            drive[0] = *path;
            DWORD charsCopied = QueryDosDeviceW(drive, target, cMaxTranslatedPath);
            if (charsCopied <= 0)
            {
                delete[] target;
                return false;
            }

            headLen = wcslen(target);
        }
        else
        {
            // Not a drive letter, network path
            wcscpy_s(target, cMupNameSize, cMupName);
            // Count the length without the terminating null character to append
            headLen = cMupNameSize - 1;
        }

        // Copy everything after the second character (including the terminating null)
        wcscpy_s(target + headLen, len - 1, path + 2);
        DeviceTarget = target;
        return true;
    }

    bool InitName(wchar_t driveLetter)
    {
        DeviceName = new wchar_t[3];
        DeviceName[0] = driveLetter;
        DeviceName[1] = L':';
        DeviceName[2] = '\0';
        return true;
    }

    void Clear()
    {

        if (DeviceName != nullptr)
        {
            delete[] DeviceName;
        }
        if (DeviceTarget != nullptr)
        {
            delete[] DeviceTarget;
        }
    }
public:
    SingleDeviceMap()
    {
        DeviceName = nullptr;
        DeviceTarget = nullptr;
    }

    ~SingleDeviceMap() { Clear(); }

    bool Init(wchar_t driveLetter, LPCWSTR path)
    {
        Clear();
        if (InitTarget(path))
        {
            InitName(driveLetter);
            return true;
        }

        return false;
    }
};

class Mapping
{
private:
    DEVICE_MAP _value;

    void Init()
    {
        _value.LinkHandles = nullptr;
        _value.MappedDirectory = INVALID_HANDLE_VALUE;
        _value.NumLinks = 0;
    }

public:
    Mapping() {Init();}
    ~Mapping() {Clear();}

    operator HANDLE() {return _value.MappedDirectory;}

    void Clear()
    {
        if (_value.MappedDirectory != INVALID_HANDLE_VALUE)
        {
            // Destroy current mapping
            CloseDeviceMap(&_value);
            Init();
        }
    }

    // Create mapping structures from an array of PathMapping
    HANDLE Create(uint32_t mapCount, PathMapping *mappings)
    {
        // Allocate structures
        unique_ptr<SingleDeviceMap[]> maps = make_unique<SingleDeviceMap[]>(mapCount);
        SingleDeviceMap *dmap = maps.get();

        for (; mapCount--; mappings++)
        {
            if (dmap->Init(mappings->drive, mappings->path))
            {
                dmap++;
            }
        }

        // Create mapping
        if (FAILED(BuildDeviceMap(static_cast<int>(dmap - maps.get()), maps.get(), &_value)))
        {
            DbgSysError(GetLastError(), L"Mapping::Create");
            return INVALID_HANDLE_VALUE;
        }
        else {
            Dbg(L"Mapping::Create - Generated new device map from the mappings");
            return _value.MappedDirectory;
        }
    }

    bool Apply(HANDLE processHandle = INVALID_HANDLE_VALUE)
    {
        return Apply(processHandle, _value.MappedDirectory);
    }

    static bool Apply(HANDLE processHandle, HANDLE mappedDirectory)
    {
        if (mappedDirectory == INVALID_HANDLE_VALUE)
        {
            Dbg(L"Mapping::Apply - Trying to apply invalid mapped directory");
            SetLastError(ERROR_INVALID_FUNCTION);
            return false;
        }

        if (processHandle == INVALID_HANDLE_VALUE)
        {
            processHandle = GetCurrentProcess();
        }

        if (SUCCEEDED(ApplyDeviceMapToProcess(processHandle, mappedDirectory)))
        {
            return true;
        }

        DWORD err = GetLastError();
        DbgSysError(err, L"Mapping::Apply");
        SetLastError(err);
        return false;
    }
};

static Mapping s_mapping;

bool ApplyMapping(HANDLE processHandle, HANDLE directoryHandle)
{
    DWORD lastError = GetLastError();
    if (lastError)
    {
        Dbg(L"LogMappingState: 0x%08X: 0x%08X", (int)lastError, directoryHandle);
    }

    return directoryHandle == INVALID_HANDLE_VALUE || s_mapping.Apply(processHandle, directoryHandle);
}

#ifdef BUILDXL_NATIVES_LIBRARY
// External function used to remap process devices based on the PathMapping
HANDLE WINAPI RemapDevices(__in uint32_t mapCount, __in PathMapping *mappings)
{
#ifdef _DEBUG
    assert(mapCount > 0);
    assert(mappings != nullptr);
#endif

    HANDLE result = s_mapping.Create(mapCount, mappings);

    if (result != INVALID_HANDLE_VALUE)
    {
        s_mapping.Apply();
    }

    return result;
}
#endif // BUILDXL_NATIVES_LIBRARY

HANDLE CurrentMappingHandle()
{
    return s_mapping;
}
#else
// When FEATURE_DEVICE_MAP is not set, we can't apply mapping and the
// handle for mapping is INVALID_HANDLE_VALUE.
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
#endif

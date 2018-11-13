#include "stdafx.h"
using namespace std;
#include "DetouredProcessInjector.h"
#include "DetoursHelpers.h"
#include "DeviceMap.h"
#include <iomanip>
#include "buildXL_mem.h"

// A flag that gets set for 64 bit processes
bool DetouredProcessInjector::s_is64BitProcess = sizeof(void *) == 8;

unsigned long g_injectionTimeoutInMinutes = 0;

// Address of the function that checks if a process is a Wow64 process. Not all
// versions of Windows have this function, so this value could be null. Casting
// from FARPROC causes warning, disable
#pragma warning( push )
#pragma warning( disable: 4191 ) // requiredSize is the result of a function call which may fail, but there is no other way to use that function
typedef BOOL(WINAPI *lp_IsWow64Process) (HANDLE, PBOOL);
static lp_IsWow64Process s_fnIsWow64Process =
    reinterpret_cast<lp_IsWow64Process>(GetProcAddress(GetModuleHandleW(L"kernel32"), "IsWow64Process"));
#pragma warning( pop )

// A flag that gets set if the current process is a Wow64 process
bool DetouredProcessInjector::s_isWow64Process =
    !DetouredProcessInjector::s_is64BitProcess && isWow64Process(GetCurrentProcess());

// Check if the given process is a Wow64 process
bool DetouredProcessInjector::isWow64Process(HANDLE processHandle)
{
    BOOL isWow64;
    return s_fnIsWow64Process != nullptr && s_fnIsWow64Process(processHandle, &isWow64) && isWow64;
}

void DetouredProcessInjector::Clear()
{
    _initialized = false;
    _mapDirectory.reset();
    _remoteInjectorPipe.reset();
    _reportPipe.reset();
    _payload.reset(nullptr);
    _payloadSize = 0;
    _otherHandles.clear();
    _dllX64.clear();
    _dllX86.clear();
}

// Initialize object with the payload wrapper that has the following data:
// uint32_t size - the size of the block
// uint32_t handleCount - the number of handles
// uint64_t handles - handles passed from the parent.
//                    There must be c_minHandleCount handles there.
// payload
bool DetouredProcessInjector::Init(const byte *payloadWrapper, std::wstring& errorMessage)
{
    errorMessage = L"";

    if (payloadWrapper == nullptr)
    {
        errorMessage = L"Payload is null";
        return false;
    }

    LockGuard lock(_injectorLock);

    // Each object can be initialized only once.
    if (_initialized)
    {
        return true;
    }

    const uint32_t *data = reinterpret_cast<const uint32_t *>(payloadWrapper);
    uint32_t size = *data;
    data++;

    // The data must at least contain the size, handle count, and the
    // minimum number of handles.
    if (!(size >= 2 * sizeof(uint32_t) + c_minHandleCount * sizeof(uint64_t))) 
    {
        errorMessage = L"Payload has incorrect size: ";
        errorMessage += std::to_wstring(size);

        return false;
    }

    assert(size >= 2 * sizeof(uint32_t) + c_minHandleCount * sizeof(uint64_t));
    size -= 2 * sizeof(uint32_t);

    // Copy known handles
    uint32_t handleCount = *data;
    data++;

    if (!(handleCount >= c_minHandleCount && size >= handleCount * sizeof(uint64_t)))
    {
        errorMessage = L"Payload has incorrect handle count or size: (handleCount: ";
        errorMessage += std::to_wstring(handleCount);
        errorMessage += L", size: ";
        errorMessage += std::to_wstring(size);
        errorMessage += L")";

        return false;
    }

    assert(handleCount >= c_minHandleCount && size >= handleCount * sizeof(uint64_t));
    const uint64_t *handles = reinterpret_cast<const uint64_t *>(data);

    // Compute the size remaining after the handles are copied
    size -= handleCount * sizeof(uint64_t);
    _mapDirectory.reset(Uint64ToHandle(*handles++));
    _remoteInjectorPipe.reset(Uint64ToHandle(*handles++));
    _reportPipe.reset(Uint64ToHandle(*handles++));

    handleCount -= c_minHandleCount;

    // Copy other handles
    if (handleCount == 0)
    {
        _otherHandles.clear();
    }
    else {
        while (handleCount--)
        {
            _otherHandles.push_back(Uint64ToHandle(*handles++));
        }
    }

    // Copy payload
    _payloadSize = size;
    _payload = make_unique<byte[]>(size);
    if (size == 0)
    {
        _payload.reset(nullptr);
    }
    else {
        byte *newPayload = new byte[size];
        memcpy_s(newPayload, size, handles, size);
        _payload.reset(newPayload);
    }

    _initialized = true;
    return true;
}

// Initialize object based on the explicit data. Note that the mapDirectory handle
// is not provided -- it's global to the process.
void DetouredProcessInjector::Init(
    HANDLE remoteInterjectorPipe, HANDLE reportPipe,
    uint32_t payloadSize, const byte *payload,
    uint32_t otherHandleCount, PHANDLE otherHandles)
{
    LockGuard lock(_injectorLock);

    // Each object can be initialized only once.
    if (_initialized)
    {
        return;
    }

    _mapDirectory.duplicate(CurrentMappingHandle());
    _remoteInjectorPipe.duplicate(remoteInterjectorPipe);
    _reportPipe.duplicate(reportPipe);
    _payloadSize = payloadSize;
    if (payloadSize == 0)
    {
        _payload.reset(nullptr);
    }
    else {
        _payload = make_unique<byte[]>(payloadSize);
        memcpy_s(_payload.get(), payloadSize, payload, payloadSize);
    }

    SetHandles(otherHandleCount, otherHandles);
    _initialized = true;
}


void DetouredProcessInjector::SetHandles(uint32_t otherHandleCount, PHANDLE otherHandles)
{
    if (otherHandleCount == 0)
    {
        _otherHandles.clear();
    }
    else {
        _otherHandles.assign(otherHandles, otherHandles + otherHandleCount);
    }
}

DWORD DetouredProcessInjector::LocalInjectProcess(HANDLE processHandle, bool inheritedHandles)
{
    LockGuard lock(_injectorLock);

    // Install detours
    LPCSTR dll = isWow64Process(processHandle) ? _dllX86.data() : _dllX64.data();
    if (!DetourUpdateProcessWithDll(processHandle, &dll, 1))
    {
        DWORD err = GetLastError();
        Dbg(L"DetouredProcessInjector::LocalInjectProcess - Failed to inject %S from %s process into %s process: 0x%08x",
              dll, s_isWow64Process ? L"WOW64" : L"Native", isWow64Process(processHandle) ? L"WOW64" : L"Native", (int)err);
        return err;
    }

    if (_mapDirectory.isValid() && !ApplyMapping(processHandle, _mapDirectory.get()))
    {
        DWORD err = GetLastError();
        Dbg(L"DetouredProcessInjector::LocalInjectProcess - Failed to apply mapping handle %d from %s to %s process: 0x%08x",
            (uint32_t)((intptr_t)_mapDirectory.get() & UINT32_MAX),
            s_isWow64Process ? L"WOW64" : L"Native",
            isWow64Process(processHandle) ? L"WOW64" : L"Native", (int)err);
        return err;
    }

    // Allocate space for the payload wrapper.
    uint32_t size = WrapperSize();
    std::unique_ptr<byte[]> payloadWrapper = make_unique<byte[]>(size);

    // Write sizes
    uint32_t *sizes = reinterpret_cast<uint32_t *>(payloadWrapper.get());
    *sizes++ = size;
    *sizes++ = static_cast<uint32_t>(c_minHandleCount + _otherHandles.size());

    // Write handles
    uint64_t *handles = reinterpret_cast<uint64_t *>(sizes);
    *handles++ = inheritedHandles ? HandleToUint64(_mapDirectory.get()) : DuplicateHandleToUint64(processHandle, _mapDirectory.get());
    *handles++ = inheritedHandles ? HandleToUint64(_remoteInjectorPipe.get()) : DuplicateHandleToUint64(processHandle, _remoteInjectorPipe.get());
    *handles++ = inheritedHandles ? HandleToUint64(_reportPipe.get()) : DuplicateHandleToUint64(processHandle, _reportPipe.get());

    if (!_otherHandles.empty())
    {
        for (auto i : _otherHandles)
        {
            *handles++ = inheritedHandles ? HandleToUint64(i) : DuplicateHandleToUint64(processHandle, i);
        }
    }

    // Copy payload
    errno_t memcpyerror = memcpy_s(handles, _payloadSize, _payload.get(), _payloadSize);
    if (memcpyerror != 0)
    {
        Dbg(L"DetouredProcessInjector::LocalInjectProcess - Failed to do memcpy: 0x%08x", (int)memcpyerror);
        return ERROR_PARTIAL_COPY;
    }

    if (!DetourCopyPayloadToProcess(processHandle, _payloadGuid, payloadWrapper.get(), size))
    {
        DWORD err = GetLastError();
        Dbg(L"DetouredProcessInjector::LocalInjectProcess - Failed to copy payload to process: 0x%08x", (int)err);
        return err;
    }

    return ERROR_SUCCESS;
}

DWORD DetouredProcessInjector::RemoteInjectProcess(HANDLE processHandle, bool inheritedHandles) const
{
    DWORD processId = GetProcessId(processHandle);

    if (processId == 0)
    {
        DWORD err = GetLastError();
        Dbg(L"DetouredProcessInjector::RemoteInjectProcess - Failed to get process id for a process: 0x%08x", (int)err);
        return err;
    }

    if (!_remoteInjectorPipe.isValid())
    {
        Dbg(L"DetouredProcessInjector::RemoteInjectProcess - override pipe is invalid, process will not be injected");
        return ERROR_INVALID_FUNCTION;
    }

    LARGE_INTEGER counter = { 0 };
    long long unsigned timeValue = QueryPerformanceCounter(&counter) ? counter.QuadPart : GetTickCount64();

    // The event name is 'Global\xxxxxxxx-yyyyyyyyyyyyyyyy-z', where:
    // xxxxxxxx         - process id
    // yyyyyyyyyyyyyyyy - timer part
    // z                - since we need two events -- success ends with S and failure ends with F.
    // The length of the result is 7 characters for the head (Global\), 8 hex digits for the id,
    // 16 hex digits for the tick count, one character for F OR S, and two characters for the dashes.
    // And the null character makes 7 + 8 + 1 + 16 + 1 + 1 + 1 = 35.
    wchar_t nameSuccess[35];

    int retPrintf = swprintf_s(nameSuccess, 35, L"Global\\%08lx-%016llx-S", processId, timeValue);
    UNREFERENCED_PARAMETER(retPrintf);
    assert(retPrintf != -1);

    wchar_t nameFailure[35];
    wcscpy_s(nameFailure, 35, nameSuccess);
    nameFailure[33] = L'F';
    
    // The remote injection request contains:
    // - the success event name (34 characters)
    // - the failure event name (34 characters)
    // - True when inherited handles, False otherwise (5 character)
    // - process id as a hex number (8 characters)
    // The fields are separated by commas and terminated (<eventSuccess>,<eventFailure>,<True/False>,<processID>\r\n),
    // making the total length 34+1+34+1+5+1+8+4=88 characters with the terminating null.
    wchar_t request[88];
    int charsWritten = swprintf_s(request, 88, L"%s,%s,%s,%08lx\r\n", nameSuccess, nameFailure, inheritedHandles ? L"True" : L"False", processId);

    assert(charsWritten != -1);

    // Create the event
    unique_handle<> eventSuccess(CreateEventW(nullptr, FALSE, FALSE, nameSuccess));
    if (!eventSuccess.isValid())
    {
        DWORD err = GetLastError();
        Dbg(L"DetouredProcessInjector::RemoteInjectProcess - Failed creating event %s: 0x%08x", nameSuccess, (int)err);
        return err;
    }

    unique_handle<> eventFailure(CreateEventW(nullptr, FALSE, FALSE, nameFailure));
    if (!eventFailure.isValid())
    {
        DWORD err = GetLastError();
        Dbg(L"DetouredProcessInjector::RemoteInjectProcess - Failed creating event %s: 0x%08x", nameFailure, (int)err);
        return err;
    }

    // Send it
    //
    // write to report stream
    //
    OVERLAPPED overlapped;
    ZeroMemory(&overlapped, sizeof(OVERLAPPED));
    overlapped.Offset = 0xFFFFFFFF;
    overlapped.OffsetHigh = 0xFFFFFFFF;
    DWORD bytesWritten;
    
    if (!WriteFile(_remoteInjectorPipe.get(), request, charsWritten * sizeof(wchar_t), &bytesWritten, &overlapped))
    {
        DWORD err = GetLastError();
        Dbg(L"DetouredProcessInjector::RemoteInjectProcess - Failed writing to pipe requesting process injection for process id %d: 0x%08x", (int)processId, (int)err);
        wprintf(L"Error: DetouredProcessInjector::RemoteInjectProcess - Failed writing to pipe requesting process injection for process id %d: 0x%08x.", (int)processId, (int)err);
        fwprintf(stderr, L"Error: DetouredProcessInjector::RemoteInjectProcess - Failed writing to pipe requesting process injection for process id %d: 0x%08x.", (int)processId, (int)err);
        HandleDetoursInjectionAndCommunicationErrors(DETOURS_PIPE_WRITE_ERROR_3, L"Failure writing message to pipe: exit(-45).", DETOURS_WINDOWS_LOG_MESSAGE_3);
    }

    // Wait for any of the events to fire
    HANDLE events[2] = { eventSuccess.get(), eventFailure.get() };

    // If for some reason there is no timeout passed using the FileAccessManifest, set it to 10 min.
    if (g_injectionTimeoutInMinutes < 10)
    {
        g_injectionTimeoutInMinutes = 10;
    }

    ULONGLONG startWait = GetTickCount64();
    DWORD result = WaitForMultipleObjects(2, events, FALSE, g_injectionTimeoutInMinutes * 60000); // Convert to ms.
    ULONGLONG endWait = GetTickCount64();
    if (((endWait - startWait) / 60000) > (g_injectionTimeoutInMinutes - 1))
    {
        Dbg(L"DetouredProcessInjector::RemoteInjectProcess - Wait time > %d min. - %d min.", g_injectionTimeoutInMinutes, (int)((endWait - startWait) / 60000));
    }

    if (result == WAIT_TIMEOUT)
    {
        Dbg(L"DetouredProcessInjector::RemoteInjectProcess - Timeout requesting process injection for process id %d", (int)processId);
    }
    else if (result == WAIT_OBJECT_0 + 1)
    {
        Dbg(L"DetouredProcessInjector::RemoteInjectProcess - Remote injection failed for process id %d, result: %ld, error: 0x%08X", (int)processId, (int)result, (int)GetLastError());
        result = ERROR_INVALID_FUNCTION;
    }
    else if (result != WAIT_OBJECT_0)
    {
        Dbg(L"DetouredProcessInjector::RemoteInjectProcess - Failed waiting for request for process injection for process id %d: 0x%08x", (int)processId, (int)result);
    }
    else {
        return ERROR_SUCCESS;
    }

    return result;
}

DetouredProcessInjector *WINAPI DetouredProcessInjector_Create(const GUID &payloadGuid,
    HANDLE remoteInterjectorPipe, HANDLE reportPipe,
    LPCSTR dllX86, LPCSTR dllX64,
    uint32_t payloadSize, const byte *payload)
{
    DetouredProcessInjector *injector = new DetouredProcessInjector(payloadGuid);
    injector->Init(remoteInterjectorPipe, reportPipe, payloadSize, payload, 0, nullptr);
    injector->SetDlls(dllX86, dllX64);
    return injector;
}

void WINAPI DetouredProcessInjector_Destroy(DetouredProcessInjector *injector)
{
    if (injector != nullptr && injector->IsValid())
    {
        delete injector;
    }
    else {
        Dbg(L"DetouredProcessInjector_Destroy: injector is not valid");
    }
}

DWORD WINAPI DetouredProcessInjector_Inject(DetouredProcessInjector *injector, DWORD pid, bool)
{
    if (!injector->IsValid()) {
        Dbg(L"DetouredProcessInjector_Inject: injector is not valid");
        return ERROR_INVALID_FUNCTION;
    }

    if (injector == nullptr)
    {
        Dbg(L"DetouredProcessInjector_Inject: injector is null");
        return ERROR_SUCCESS;
    }

    unique_handle<nullptr> processHandle (OpenProcess(PROCESS_ALL_ACCESS, FALSE, pid));

    if (!processHandle.isValid())
    {
        Dbg(L"DetouredProcessInjector_Inject: process handle is not valid");
        return GetLastError();
    }

    return injector->LocalInjectProcess(processHandle.get(), false);
}

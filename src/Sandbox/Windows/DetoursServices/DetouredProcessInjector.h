#pragma once
#include "UniqueHandle.h"
#include "DebuggingHelpers.h"

using std::unique_ptr;
using std::vector;
using std::string;

// This class does drive mapping and injection of payload and DLL into
// a process. It may do it directly or remotely. The remote injection
// is required when a WOW64 process creates a child. Exact conditions
// are in NeedRemoteInjection method. Do do it, just send a request
// via an inherited pip to the top-of-the-process-tree server.
//
// The class can be created by C# code with the data to be used, or
// it can be initialized from the previously injected payload by
// during child process startup.
class DetouredProcessInjector
{
private:
    uint32_t _tag;            // this value is a sanity check to make sure that we are looking at a valid object

    // A flag set for wow64 process
    static bool s_isWow64Process;

    // A flag set for 64 bit process
    static bool s_is64BitProcess;

    // Minimum number of handles required
    static const uint32_t c_minHandleCount = 3;

    static const uint32_t c_injectorTag = 0xD031B09E;

    // We own these handles
    unique_handle<INVALID_HANDLE_VALUE> _mapDirectory;
    unique_handle<INVALID_HANDLE_VALUE> _remoteInjectorPipe;
    unique_handle<INVALID_HANDLE_VALUE> _reportPipe;
    unique_ptr<byte[]> _payload = nullptr;
    uint32_t _payloadSize = 0;
    vector<HANDLE> _otherHandles;
    string _dllX86;
    string _dllX64;
    GUID _payloadGuid;
    bool _initialized = false;

    CRITICAL_SECTION _injectorLock;

    class LockGuard
    {
    private:
        CRITICAL_SECTION &_lock;
    public:
        LockGuard(CRITICAL_SECTION &lock) : _lock(lock) { EnterCriticalSection(&_lock); }
        ~LockGuard() { LeaveCriticalSection(&_lock); }
        LockGuard() = delete;
        LockGuard(const LockGuard&) = delete;
        LockGuard& operator=(LockGuard const &) = delete;
    };

    // Convert uint64 to HANDLE
    static inline HANDLE Uint64ToHandle(uint64_t value)
    {
        if (s_is64BitProcess)
        {
            return reinterpret_cast<HANDLE>(value);
        }
        else {
#pragma warning( push )
#pragma warning( disable: 4312 )
            return reinterpret_cast<HANDLE>(static_cast<uint32_t>(value & UINT32_MAX));
#pragma warning( pop )
        }
    }

#pragma warning( push )
#pragma warning( disable: 4302 4310 4311 4826 )
    // Convert a handle to uint64_t
    static inline uint64_t HandleToUint64(HANDLE value)
    {
        if (s_is64BitProcess)
        {
            return static_cast<uint64_t>(reinterpret_cast<int64_t>(value));
        }
        else {
            // Generally we don't want to sign extend the handle, only in case of the INVALID_HANDLE_VALUE. The compiler
            // helpfully provides a warning when sign-extending pointers, therefore disable above warnings
            return value == INVALID_HANDLE_VALUE ? (uint64_t)(int64_t)(int32_t)INVALID_HANDLE_VALUE : (uint64_t)value;
        }
    }
#pragma warning( pop )

    // Duplicate handle for the specified process and convert the new handle to uint64
    static inline uint64_t DuplicateHandleToUint64(HANDLE processHandle, HANDLE value)
    {
        HANDLE targetValue;
        if (value == INVALID_HANDLE_VALUE ||
                !DuplicateHandle(GetCurrentProcess(), value, processHandle, &targetValue, 0, TRUE, DUPLICATE_SAME_ACCESS)) {
            targetValue = INVALID_HANDLE_VALUE;
        }

        return HandleToUint64(targetValue);
    }

    // Check if the injection can only happen remotely.
    // We should be able to do all we need in all cases, except WOW64 to Native 64 bit
    // process. For now we have an additional condition for WOW64 to WOW64 bit process
    // when we need to do drive mapping due to a kernel thunk bug in WOW64.
    //
    // Disable the warning about processHandle being unused and
    // change the check to the one commented out when the bug is fixed.
#pragma warning( push )
#pragma warning( disable: 4100 )
    inline bool NeedRemoteInjection(HANDLE processHandle)
    {
        return s_isWow64Process && (_mapDirectory.isValid() || !isWow64Process(processHandle));
        //// return s_isWow64Process && !isWow64Process(processHandle);
    }
#pragma warning( pop )

    // Given all data, compute the size of the wrapped payload
    uint32_t inline WrapperSize() const
    {
        // The data must contain the size, handle count, the handles, and the payload
        return static_cast<uint32_t>(2 * sizeof(uint32_t) + (c_minHandleCount + _otherHandles.size()) * sizeof(uint64_t) + _payloadSize);
    }


    // Clear the object (free memory, etc.)
    void Clear();

public:
    // Check if the process is wow64
    static bool isWow64Process(HANDLE processHandle);
    // The only constructor requires the payload GUID
    DetouredProcessInjector(const GUID &payloadGuid) : _payloadGuid(payloadGuid), _tag(c_injectorTag)
    {
        InitializeCriticalSection(&_injectorLock);
    }

    ~DetouredProcessInjector()
    {
        DeleteCriticalSection(&_injectorLock);
    }

    // Populate the data from the serialized wrapper.
    bool Init(LPCBYTE payloadWrapper, std::wstring& errorMessage);
    void Init(HANDLE remoteInterjectorPipe, HANDLE reportPipe,
        uint32_t payloadSize, LPCBYTE payload,
        uint32_t otherHandleCount, PHANDLE otherHandles);

    // Set the dll paths to be injected
    void inline SetDlls(LPCSTR dllX86, LPCSTR dllX64)
    {
        _dllX86 = dllX86;
        _dllX64 = dllX64;
    }

    // Set "other" handles. These are duplicated if needed.
    void SetHandles(uint32_t otherHandleCount, PHANDLE otherHandles);

    inline bool IsValid() const
    {
#ifdef _DEBUG
        assert(_tag == c_injectorTag);
#endif
        return _tag == c_injectorTag && _initialized;
    }

    // Getters
    HANDLE MapDirectory() const { return _mapDirectory.get(); }
    HANDLE RemoteInjectorPipe() const { return _remoteInjectorPipe.get(); }
    HANDLE ReportPipe() const { return _reportPipe.get(); }
    LPCBYTE Payload() const { return _payload.get(); }
    uint32_t PayloadSize() const { return _payloadSize; }
    uint32_t OtherHandleCount() const { return static_cast<uint32_t>(_otherHandles.size()); }
    const HANDLE *OtherHandles() const { return _otherHandles.data(); }
    bool IsInitialized() { return _initialized; }

    // This method will inject the data stored in the object into the specified process.
    //   processHandle - the process to inject
    //   inheritedHandles - when true, all handles are inherited.
    //                      When false, none or only some handles
    //                      are inherited. The handles stored in
    //                      the object need to be duplicated.
    DWORD LocalInjectProcess(HANDLE processHandle, bool inheritedHandles);
    // This method will ask for the remote injection
    DWORD RemoteInjectProcess(HANDLE processHandle, bool inheritedHandles) const;

    // Do either local or remote injection, depending on bitness of the
    // injector and injectee processes.
    DWORD InjectProcess(HANDLE processHandle, bool inheritedHandles)
    {
        return NeedRemoteInjection(processHandle) ? RemoteInjectProcess(processHandle, inheritedHandles) :
            LocalInjectProcess(processHandle, inheritedHandles);
    }

    // No default constructor, no copies
    DetouredProcessInjector() = delete;
    DetouredProcessInjector(const DetouredProcessInjector &) = delete;
    DetouredProcessInjector& operator=(DetouredProcessInjector const &) = delete;
};


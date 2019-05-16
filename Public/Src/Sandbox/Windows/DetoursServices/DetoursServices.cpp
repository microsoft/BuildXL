// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// DetoursServices.cpp : Defines the exported functions for the DLL application.

// Adapted from MidBuild

#pragma warning( disable : 4710 4820 4350 4668)

#include "stdafx.h"

#ifdef _DLL
#error DetoursServices must be statically linked with a native runtime. Linking to DLL native runtime results in it loading into processes we detour, which unsafely assumes that they can find it.
#endif

#include <crtdbg.h>

#include "DataTypes.h"
#include "DebuggingHelpers.h"
#include "DetouredFunctions.h"
#include "DetouredFunctionTypes.h"
#include "DetoursHelpers.h"
#include "DetoursServices.h"
#include "FileAccessHelpers.h"
#include "globals.h"
#include "buildXL_mem.h"
#include "DetouredScope.h"
#include "StringOperations.h"
#include "HandleOverlay.h"
#include "DetouredProcessInjector.h"
#include "SendReport.h"
#include <Psapi.h>

#define BUILDXL_DETOURS_CREATE_PROCESS_RETRY_COUNT 5
#define BUILDXL_DETOURS_MS_TO_SLEEP 10
#define BUILDXL_PRELOADED_DLLS_MAX_PATH 65536

extern "C" {
    NTSTATUS NTAPI ZwSetInformationFile(
        _In_  HANDLE                 FileHandle,
        _Out_ PIO_STATUS_BLOCK       IoStatusBlock,
        _In_  PVOID                  FileInformation,
        _In_  ULONG                  Length,
        _In_  FILE_INFORMATION_CLASS FileInformationClass);

    NTSTATUS NTAPI ZwCreateFile(
        _Out_    PHANDLE            FileHandle,
        _In_     ACCESS_MASK        DesiredAccess,
        _In_     POBJECT_ATTRIBUTES ObjectAttributes,
        _Out_    PIO_STATUS_BLOCK   IoStatusBlock,
        _In_opt_ PLARGE_INTEGER     AllocationSize,
        _In_     ULONG              FileAttributes,
        _In_     ULONG              ShareAccess,
        _In_     ULONG              CreateDisposition,
        _In_     ULONG              CreateOptions,
        _In_opt_ PVOID              EaBuffer,
        _In_     ULONG              EaLength);

    NTSTATUS NTAPI ZwOpenFile(
        _Out_ PHANDLE            FileHandle,
        _In_  ACCESS_MASK        DesiredAccess,
        _In_  POBJECT_ATTRIBUTES ObjectAttributes,
        _Out_ PIO_STATUS_BLOCK   IoStatusBlock,
        _In_  ULONG              ShareAccess,
        _In_  ULONG              OpenOptions);
}

#if MEASURE_DETOURED_NT_CLOSE_IMPACT
volatile LONG g_msTimeToPopulatePoolList = 0;
volatile ULONGLONG g_pipExecutionStart = 0;
volatile LONG g_ntCloseHandeCount = 0;
volatile LONG g_maxClosedListCount = 0;
volatile LONG g_msTimeInAddClosedList = 0;
volatile LONG g_msTimeInRemoveClosedList = 0;
#endif // #if MEASURE_DETOURED_NT_CLOSE_IMPACT

extern "C" {
    NTSTATUS NTAPI NtQueryDirectoryFile(
        _In_     HANDLE                 FileHandle,
        _In_opt_ HANDLE                 Event,
        _In_opt_ PIO_APC_ROUTINE        ApcRoutine,
        _In_opt_ PVOID                  ApcContext,
        _Out_    PIO_STATUS_BLOCK       IoStatusBlock,
        _Out_    PVOID                  FileInformation,
        _In_     ULONG                  Length,
        _In_     FILE_INFORMATION_CLASS FileInformationClass,
        _In_     BOOLEAN                ReturnSingleEntry,
        _In_opt_ PUNICODE_STRING        FileName,
        _In_     BOOLEAN                RestartScan);

    NTSTATUS NTAPI ZwQueryDirectoryFile(
        _In_     HANDLE                 FileHandle,
        _In_opt_ HANDLE                 Event,
        _In_opt_ PIO_APC_ROUTINE        ApcRoutine,
        _In_opt_ PVOID                  ApcContext,
        _Out_    PIO_STATUS_BLOCK       IoStatusBlock,
        _Out_    PVOID                  FileInformation,
        _In_     ULONG                  Length,
        _In_     FILE_INFORMATION_CLASS FileInformationClass,
        _In_     BOOLEAN                ReturnSingleEntry,
        _In_opt_ PUNICODE_STRING        FileName,
        _In_     BOOLEAN                RestartScan);
}

#pragma warning( disable : 4711)

/*

This project implements monitoring and enforcement of environment-facing application interactions for the BuildXL build system.

The BuildXL build system uses Detours to inject this library into processes executed by
the build system.  The build system also communicates a payload to this library, which
describes the access rules to use, in particular for the file system.

For more information, look in $(ROOT)\BuildXL.Processes

All of the setup for this library occurs when this DLL is loaded into the target process,
and occurs within the DllMain DLL_PROCESS_ATTACH handler.  The setup code uses the Detours
API to find the payload, then it parses the payload and sets up several global variables
with the parsed form of the payload.  After initialization, these global data structures
do not change, and so there is no need for any synchronization when accessing them.

When this library initializes (during DllMain / DLL_PROCESS_ATTACH), it uses the Detours
API to locate a "detours services manifest", which was provided by the process that created
the current process.  (Typically, the creating process is an instance of the BuildXL build
system.)  The manifest specifies which files this process may access.  The manifest consists
of a list of directories (scopes) and policies that apply to them, and a list of specific
filenames and the policies that apply to them.

The manifest is encoded as a single, NUL-terminated UTF-16 string.  The string consists of
lines, and each line is terminated by a \r\n pair.  Each line begins with a keyword, which
indicates the type of line (directive).  The complete list of keywords:

* file - specifies a policy that applies to a specific file
* scope - specifies a policy that applies to a directory and its contents
* end - specifies the end of the entire manifest
* flags - specifies flags that control the behavior of the DetouredFileServices.dll
library
* report - specifies the absolute path to a report file (output); this directive is optional,
and may be specified at most once.  If any 'scope' directive specifies Report=1,
then the 'report' directive is required.


The 'file' directive has this form:

    file,<pathid>,<path>,<policy>

    where:
    * <pathid> is a short identifier of the file
    * <path> is an absolute, drive-based path which identifies (or will identify) a file
    * <policy> is a decimal integer which specifies the policy to apply to this file.
        The policy must be exactly one of the following values:
            1 - AllowRead.  Read-only access to the file is allowed.
            2 - AllowReadWrite.  Read and write access to the file is allowed.


The 'scope' directive has this form:

    scope,<pathid>,<flags>,<path>,<policy>

    where:
    * <pathid> is a short identifier of the file
    * <flags> is a set of flags (FileAccessScopeFlag) expressed in hexadecimal (with no hex prefix)
    * <path> is an absolute, drive-based path which identifies (or will identify) a directory
    * <policy> is a decimal integer which specifies the policy to apply to this directory and
    all of its contents (including subdirectories) (DetoursFileScopePolicy).

    The assigned meanings of bits of <flags> are:
        bit 0: IsRecursive
        bit 1: AllowSilentFailureonFileNotFound
        bit 2: Report - If set, then all accesses will be reported to the report file.
        (all other bits must be zero)

    <policy> must be exactly one of the following values:
        1 - AllowAll
        2 - DenyAll
        3 - RequireFileAccessEntry


The 'report' directive can have two forms:

    report,#<handle>
    report,<path>

    The <handle> form specifies the unsigned, decimal value of a file HANDLE.
    The <path> form specifies a path to a file.  The file is opened with FILE_SHARE_READ | FILE_SHARE_WRITE.

The report information is written as unbuffered UTF-8 text.  Each line is written in a single WriteFile() call.
This allows multiple processes to write to the same file object concurrently; the Windows storage stack guarantees
that the contents (the individual bytes) of the writes will not interleave, and when append-mode writes are used,
that none of the appends will be lost.

*/

using std::string;
using std::vector;
using std::unique_ptr;
using std::make_unique;

// ----------------------------------------------------------------------------
// GLOBALS
// ----------------------------------------------------------------------------

_locale_t g_invariantLocale;

// Not referenced, but useful during debugging.
PVOID g_manifestPtr = nullptr;
PDWORD g_manifestSizePtr = 0;
DWORD g_currentProcessId;
PCWSTR g_currentProcessCommandLine = nullptr;
DWORD g_parentProcessId = 0;

FileAccessManifestFlag g_fileAccessManifestFlags;

FileAccessManifestExtraFlag g_fileAccessManifestExtraFlags;
uint64_t g_FileAccessManifestPipId;

PCManifestRecord g_manifestTreeRoot;

PManifestTranslatePathsStrings g_manifestTranslatePathsStrings;
vector<TranslatePathTuple*>* g_pManifestTranslatePathTuples = nullptr;

PManifestInternalDetoursErrorNotificationFileString g_manifestInternalDetoursErrorNotificationFileString;
LPCTSTR g_internalDetoursErrorNotificationFile = nullptr;

HANDLE g_messageCountSemaphore = INVALID_HANDLE_VALUE;

HANDLE g_reportFileHandle;

bool g_BreakOnAccessDenied;

LPCSTR g_lpDllNameX86;
LPCSTR g_lpDllNameX64;

wchar_t *g_substituteProcessExecutionShimPath = nullptr;
bool g_ProcessExecutionShimAllProcesses;
vector<ShimProcessMatch*>* g_pShimProcessMatches = nullptr;

DetouredProcessInjector* g_pDetouredProcessInjector = nullptr;

HANDLE g_hPrivateHeap = nullptr;

// Peak Detours allocated memory. It is allocated in a private heap.
volatile LONG64 g_detoursMaxAllocatedMemoryInBytes = 0;

// Running allocated memory by Detours in its private heap.
volatile LONG64 g_detoursHeapAllocatedMemoryInBytes = 0;

// The number of entries allocated in the no-lock, concurrent list for use by NtClose.
volatile LONG g_detoursAllocatedNoLockConcurentPoolEntries = 0;

// The max number of entries in the HandleHeapMap hash table. Allocated in private heap.
volatile LONG64 g_detoursMaxHandleHeapEntries = 0;

// Currently allocated entries in the HandleHeapMap hash table. Allocated in private heap.
volatile LONG64 g_detoursHandleHeapEntries = 0;\

//
// Real Windows API function pointers
//

CreateProcessW_t Real_CreateProcessW;
CreateProcessA_t Real_CreateProcessA;
CreateFileW_t Real_CreateFileW;

RtlFreeHeap_t Real_RtlFreeHeap;
RtlAllocateHeap_t Real_RtlAllocateHeap;
RtlReAllocateHeap_t Real_RtlReAllocateHeap;
VirtualAlloc_t Real_VirtualAlloc;

CreateFileA_t Real_CreateFileA;
GetVolumePathNameW_t Real_GetVolumePathNameW;
GetFileAttributesA_t Real_GetFileAttributesA;
GetFileAttributesW_t Real_GetFileAttributesW;
GetFileAttributesExW_t Real_GetFileAttributesExW;
GetFileAttributesExA_t Real_GetFileAttributesExA;
CloseHandle_t Real_CloseHandle;

CopyFileW_t Real_CopyFileW;
CopyFileA_t Real_CopyFileA;
CopyFileExW_t Real_CopyFileExW;
CopyFileExA_t Real_CopyFileExA;
MoveFileW_t Real_MoveFileW;
MoveFileA_t Real_MoveFileA;
MoveFileExW_t Real_MoveFileExW;
MoveFileExA_t Real_MoveFileExA;
MoveFileWithProgressW_t Real_MoveFileWithProgressW;
MoveFileWithProgressA_t Real_MoveFileWithProgressA;
ReplaceFileW_t Real_ReplaceFileW;
ReplaceFileA_t Real_ReplaceFileA;
DeleteFileA_t Real_DeleteFileA;
DeleteFileW_t Real_DeleteFileW;

CreateHardLinkW_t Real_CreateHardLinkW;
CreateHardLinkA_t Real_CreateHardLinkA;
CreateSymbolicLinkW_t Real_CreateSymbolicLinkW;
CreateSymbolicLinkA_t Real_CreateSymbolicLinkA;
FindFirstFileW_t Real_FindFirstFileW;
FindFirstFileA_t Real_FindFirstFileA;
FindFirstFileExW_t Real_FindFirstFileExW;
FindFirstFileExA_t Real_FindFirstFileExA;
FindNextFileW_t Real_FindNextFileW;
FindNextFileA_t Real_FindNextFileA;
FindClose_t Real_FindClose;
GetFileInformationByHandleEx_t Real_GetFileInformationByHandleEx;
GetFileInformationByHandle_t Real_GetFileInformationByHandle;
SetFileInformationByHandle_t Real_SetFileInformationByHandle;
OpenFileMappingW_t Real_OpenFileMappingW;
OpenFileMappingA_t Real_OpenFileMappingA;
GetTempFileNameW_t Real_GetTempFileNameW;
GetTempFileNameA_t Real_GetTempFileNameA;
CreateDirectoryW_t Real_CreateDirectoryW;
CreateDirectoryA_t Real_CreateDirectoryA;
CreateDirectoryExW_t Real_CreateDirectoryExW;
CreateDirectoryExA_t Real_CreateDirectoryExA;
RemoveDirectoryW_t Real_RemoveDirectoryW;
RemoveDirectoryA_t Real_RemoveDirectoryA;
DecryptFileW_t Real_DecryptFileW;
DecryptFileA_t Real_DecryptFileA;
EncryptFileW_t Real_EncryptFileW;
EncryptFileA_t Real_EncryptFileA;
OpenEncryptedFileRawW_t Real_OpenEncryptedFileRawW;
OpenEncryptedFileRawA_t Real_OpenEncryptedFileRawA;
OpenFileById_t Real_OpenFileById;
GetFinalPathNameByHandleW_t Real_GetFinalPathNameByHandleW;
GetFinalPathNameByHandleA_t Real_GetFinalPathNameByHandleA;

NtClose_t Real_NtClose;
NtCreateFile_t Real_NtCreateFile;
NtOpenFile_t Real_NtOpenFile;
ZwCreateFile_t Real_ZwCreateFile;
ZwOpenFile_t Real_ZwOpenFile;
NtQueryDirectoryFile_t Real_NtQueryDirectoryFile;
ZwQueryDirectoryFile_t Real_ZwQueryDirectoryFile;
ZwSetInformationFile_t Real_ZwSetInformationFile;

// Value used to signal the the exit code of the current process cannot be retrieved
#define PROCESS_EXIT_CODE_CANNOT_BE_RETRIEVED 0xFFFFFF9A

// Value used to as an exit code when terminating the current process because the detouring process has failed.
#define PROCESS_DETOURING_FAILED_EXIT_CODE 0xFFFFFF9B

// ----------------------------------------------------------------------------
// FUNCTION DEFINITIONS
// ----------------------------------------------------------------------------

EXTERN_C IMAGE_DOS_HEADER __ImageBase;

static void SetEventLogSource(const std::wstring& a_name)
{
    const std::wstring key_path(L"SYSTEM\\CurrentControlSet\\Services\\"
        L"EventLog\\Application\\" + a_name);

    HKEY key;

    LSTATUS last_error = RegCreateKeyEx(HKEY_LOCAL_MACHINE,
        key_path.c_str(),
        0,
        0,
        REG_OPTION_NON_VOLATILE,
        KEY_SET_VALUE,
        0,
        &key,
        nullptr);

    if (ERROR_SUCCESS == last_error)
    {
        WCHAR   DllPath[MAX_PATH] = { 0 };
        GetModuleFileNameW((HINSTANCE)&__ImageBase, DllPath, _countof(DllPath));
        const DWORD types_supported = EVENTLOG_ERROR_TYPE |
            EVENTLOG_WARNING_TYPE |
            EVENTLOG_INFORMATION_TYPE;

        const char* DominoDetoursServices = "DominoDetoursServices.dll";

        last_error = RegSetValueEx(key,
            L"EventMessageFile",
            0,
            REG_SZ,
            (BYTE*)DominoDetoursServices,
            sizeof(DominoDetoursServices));

        if (ERROR_SUCCESS == last_error)
        {
            last_error = RegSetValueEx(key,
                L"TypesSupported",
                0,
                REG_DWORD,
                (LPBYTE)&types_supported,
                sizeof(types_supported));
        }

        RegCloseKey(key);
    }
}

static void UnsetEventLogSource(const std::wstring& a_name)
{
    const std::wstring key_path(L"SYSTEM\\CurrentControlSet\\Services\\"
        L"EventLog\\Application\\" + a_name);

    RegDeleteKey(HKEY_LOCAL_MACHINE,
        key_path.c_str());
}

void LogEventLogMessage(const std::wstring& a_msg,
    const WORD a_type,
    const WORD eventId,
    const std::wstring& a_name)
{
    SetEventLogSource(a_name);

    HANDLE h_event_log = RegisterEventSource(0, a_name.c_str());

    if (0 != h_event_log)
    {
        LPCTSTR message = a_msg.c_str();

        ReportEvent(h_event_log,
            a_type,
            0,
            eventId,
            0,
            1,
            0,
            &message,
            0);

        DeregisterEventSource(h_event_log);
    }

    UnsetEventLogSource(a_name);
}

//
// Code to create a detoured process
//
// This code is just to create the initial detoured process,
// and it will also be used to create detoured nested processes.
// The pfCreateProcessW function pointer points at the CreateProcessW
// function we should run.  When called within a detour of CreateProcessW
// it will point at the prior CreateProcessW entry point.  When called
// from outside (not within the detour of CreateProcessW) it will be
// passed the normal public CreateProcessW entry point.
//

CreateDetouredProcessStatus
WINAPI
InternalCreateDetouredProcess(
    LPCWSTR lpApplicationName,
    LPWSTR lpCommandLine,
    LPSECURITY_ATTRIBUTES lpProcessAttributes,
    LPSECURITY_ATTRIBUTES lpThreadAttributes,
    BOOL bInheritHandles,
    DWORD dwCreationFlags,
    LPVOID lpEnvironment,
    LPCWSTR lpcwWorkingDirectory,
    LPSTARTUPINFOW lpStartupInfo,
    HANDLE hJob,
    DetouredProcessInjector *pInjector,
    LPPROCESS_INFORMATION lpProcessInformation,
    CreateProcessW_t pfCreateProcessW)
{
    // No detours should be called recursively from here.
    DetouredScope scope;

    DWORD error = ERROR_SUCCESS;
    BOOL fProcCreated = FALSE;
    BOOL fProcDetoured = FALSE;
    CreateDetouredProcessStatus status = CreateDetouredProcessStatus::Succeeded;
    DWORD creationFlags = dwCreationFlags;
    unsigned nRetryCount = 0;

    bool disabledDetours = DisableDetours();
    bool needInjection = pInjector != nullptr && pInjector->IsValid() && !disabledDetours;

    if ((needInjection || hJob != 0) && !disabledDetours)
    {
        creationFlags |= CREATE_SUSPENDED;
    }

    if (LogProcessDetouringStatus())
    {
        ReportProcessDetouringStatus(
            ProcessDetouringStatus_Starting,
            lpApplicationName,
            lpCommandLine,
            needInjection,
            INVALID_HANDLE_VALUE,
            disabledDetours,
            creationFlags,
            fProcDetoured,
            error,
            status);
    }

    // It appears the AV might hold exclusive read lock while scaning and this can fail create process.
    // Inject some retries.
    while (true)
    {
        // Create the process as requested, but make sure it's suspended
        fProcCreated = pfCreateProcessW(
            lpApplicationName,
            lpCommandLine,
            lpProcessAttributes,
            lpThreadAttributes,
            bInheritHandles,
            creationFlags,
            lpEnvironment,
            lpcwWorkingDirectory,
            lpStartupInfo,
            lpProcessInformation);

        if (fProcCreated == 0) // Failed
        {
            if (GetLastError() == ERROR_ACCESS_DENIED)
            {
                if (nRetryCount < BUILDXL_DETOURS_CREATE_PROCESS_RETRY_COUNT)
                {
                    Sleep(BUILDXL_DETOURS_MS_TO_SLEEP + (nRetryCount * BUILDXL_DETOURS_MS_TO_SLEEP));
                    nRetryCount++;
                    continue;
                }
            }
        }

        break;
    }

    if (!fProcCreated)
    {
        error = GetLastError();
    }
    else if (needInjection)
    {
        // Check if all handles are inherited. While extended attributes are not necessarily about
        // handle inheritance, the structure is undocumented, so we assume that if the extended
        // attributes are preset, we are inheriting specific handles. The flag, when not set
        // will cause the injection function to duplicate required handles. When set, we assume
        // all handles are inherited and there is no need for duplication.
        bool fullInheritHandles = bInheritHandles == TRUE && !(dwCreationFlags & EXTENDED_STARTUPINFO_PRESENT);
        error = pInjector->InjectProcess(lpProcessInformation->hProcess, fullInheritHandles);
        fProcDetoured = error == ERROR_SUCCESS;
    }

    if ((fProcDetoured || !needInjection) && fProcCreated) {
        status = CreateDetouredProcessStatus::Succeeded;

        if (hJob != 0 && !AssignProcessToJobObject(hJob, lpProcessInformation->hProcess)) {
            status = CreateDetouredProcessStatus::JobAssignmentFailed;
            error = GetLastError();
            Dbg(L"Assigning to job failed, error: %08X", (int)error);
        }
    }
    else if (fProcCreated) {
        status = CreateDetouredProcessStatus::DetouringFailed;
    }
    else {
        status = CreateDetouredProcessStatus::ProcessCreationFailed;
    }
    
    if (status == CreateDetouredProcessStatus::Succeeded &&
        !(dwCreationFlags & CREATE_SUSPENDED) &&
        dwCreationFlags != creationFlags &&
        ResumeThread(lpProcessInformation->hThread) == -1) {

        status = CreateDetouredProcessStatus::ProcessResumeFailed;
        error = GetLastError();
    }

    if (status != CreateDetouredProcessStatus::Succeeded) {
        // clean-up
        if (fProcCreated) {
            Dbg(L"Detouring failed. Application name: '%s' Command line: '%s' Error: 0x%08X",
                lpApplicationName, lpCommandLine, (int)error);
            // the process never ran any code, as the main thread was initially suspended; so let's just kill it again
            BOOL terminatedProcess = TerminateProcess(lpProcessInformation->hProcess, PROCESS_DETOURING_FAILED_EXIT_CODE);
            if (terminatedProcess) {
                CloseHandle(lpProcessInformation->hProcess);
                lpProcessInformation->hProcess = 0;
                CloseHandle(lpProcessInformation->hThread);
                lpProcessInformation->hThread = 0;
                lpProcessInformation->dwProcessId = 0;
            }
            else {
                DWORD terminateProcessError = GetLastError();
                Dbg(L"Termination of undetoured process failed. Application name: '%s' Command line: '%s' Error: %08X",
                    lpApplicationName, lpCommandLine, (int)terminateProcessError);
            }
        }
    }

    if (LogProcessDetouringStatus())
    {
        ReportProcessDetouringStatus(
            ProcessDetouringStatus_Done,
            lpApplicationName,
            lpCommandLine,
            needInjection,
            INVALID_HANDLE_VALUE,
            disabledDetours,
            creationFlags,
            fProcDetoured,
            error,
            status);
    }

    SetLastError(error);

    if (status == CreateDetouredProcessStatus::DetouringFailed ||
        status == CreateDetouredProcessStatus::JobAssignmentFailed ||
        status == CreateDetouredProcessStatus::HandleInheritanceFailed ||
        status == CreateDetouredProcessStatus::ProcessResumeFailed ||
        status == CreateDetouredProcessStatus::PayloadCopyFailed)
    {
        fwprintf(stderr, L"Failure in CreateProcess. LastError: %d, Status: %d. Exiting with code -47.", (int)error, (int)status);
        HandleDetoursInjectionAndCommunicationErrors(DETOURS_CREATE_PROCESS_ERROR_5, L"Failure in CreateProcess.Exiting with code -47.", DETOURS_WINDOWS_LOG_MESSAGE_5);
    }
    return status;
}

struct ProcessCreationAttributes {
    struct AttrListDeleter {
        void operator()(LPPROC_THREAD_ATTRIBUTE_LIST p) { DeleteProcThreadAttributeList(p); dd_free((void*)p); }
    };

    typedef unique_ptr<_PROC_THREAD_ATTRIBUTE_LIST, AttrListDeleter> attrlist_ptr;

    ProcessCreationAttributes(HANDLE job) : hJob{ job } {}
	ProcessCreationAttributes(const ProcessCreationAttributes&) = delete;
	ProcessCreationAttributes& operator=(const ProcessCreationAttributes&) = delete;

	ProcessCreationAttributes(ProcessCreationAttributes&& other)
        : attrList(move(other.attrList)), handles(move(other.handles)) 
    { 
        hJob = other.hJob;
    }

	ProcessCreationAttributes& operator=(ProcessCreationAttributes&& other) 
    {
        attrList = move(other.attrList);
        handles = move(other.handles);
        hJob = other.hJob;

        return *this;
    }

    HANDLE hJob;
    attrlist_ptr attrList;
    vector<HANDLE> handles;
};

/** Initializes the list of attributes based on whether the process needs to be added to a silo
*/
static bool InitializeAttributeList(ProcessCreationAttributes& attr, bool addProcessToSilo) {
	// There is always at least one attribute for the explicit handle inheritance. There are two
	// if the process needs to be created inside a silo
	DWORD attributeCount = addProcessToSilo ? 2ul : 1ul;

	// First we establish the required allocation size.
	SIZE_T requiredSize = 0;
	if (!InitializeProcThreadAttributeList(NULL, attributeCount, /*flags*/ 0, &requiredSize) &&
		GetLastError() != ERROR_INSUFFICIENT_BUFFER) {
		return false;
	}

	assert(requiredSize > 0);

	attr.attrList = ProcessCreationAttributes::attrlist_ptr(
		static_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(dd_malloc(requiredSize)));

	assert(attr.attrList.get() != nullptr);

	if (!InitializeProcThreadAttributeList(attr.attrList.get(), attributeCount, /*flags*/ 0, &requiredSize)) {
		return false;
	}

	return true;
}

/** Populates an LPPROC_THREAD_ATTRIBUTE_LIST that specifies whitelisted inheritance of the given handles.
- At least one handle must be provided (an empty whitelist is not represented; just leave off the attribute list).
- Upon successful return (true), `attr` is populated with an LPPROC_THREAD_ATTRIBUTE_LIST and the underlying handle array.
- On failure (false), the contents of `attr` are undefined (though some members may need to destruct).
*/
#pragma warning( push )
#pragma warning( disable: 6102 ) // requiredSize is the result of a function call which may fail, but there is no other way to use that function

static bool CreateProcAttributesForExplicitHandleInheritance(
	/*in opt */ HANDLE hStdInput,
	/*in opt */ HANDLE hStdOutput,
	/*in opt */ HANDLE hStdError,
	/*out    */ ProcessCreationAttributes& attr
) {

	if (hStdInput != INVALID_HANDLE_VALUE) {
		attr.handles.push_back(hStdInput);
	}

	if (hStdOutput != INVALID_HANDLE_VALUE) {
		attr.handles.push_back(hStdOutput);
	}

	if (hStdError != INVALID_HANDLE_VALUE &&
		hStdError != hStdOutput) { /* A common case for duplicate handle values. */
		attr.handles.push_back(hStdError);
	}

	assert(attr.handles.size() > 0);

	if (!UpdateProcThreadAttribute(attr.attrList.get(), /*flags*/ 0,
		PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
		&attr.handles[0], sizeof(HANDLE) * attr.handles.size(),
		/*prev value*/ NULL, /*return size*/ NULL)) {

		return false;
	}

	return true;
}

#pragma warning( pop )

static bool CreateProcAttributeForAddingProcessToSilo(
	/*in out*/ ProcessCreationAttributes& attr) {

	if (!UpdateProcThreadAttribute(attr.attrList.get(), /*flags*/ 0,
		PROC_THREAD_ATTRIBUTE_JOB_LIST,
		&attr.hJob, sizeof(HANDLE),
		/*prev value*/ NULL, /*return size*/ NULL)) {

		return false;
	}

	return true;
}

/** Creates a ProcessCreationAttributes to handle:
- Explicit handle inheritance
- Optionally, adding process to silo
*/
static CreateDetouredProcessStatus CreateProcessAttributes(
	/*in opt */ HANDLE hStdInput,
	/*in opt */ HANDLE hStdOutput,
	/*in opt */ HANDLE hStdError,
	/*in     */ LPCWSTR lpcwCommandLine,
	/*in     */ DWORD dwCreationFlags,
    /*in     */ bool addProcessToSilo,
	/*out    */ ProcessCreationAttributes& processCreationAttributes) {

	if (!InitializeAttributeList(processCreationAttributes, addProcessToSilo))
	{
		Dbg(L"Failed initializing attribute list");
		fwprintf(stderr, L"Failure in CreateProcessAttributes initializing attribute list. LastError: %d, Status: %d. Exiting with code -62.", (int)GetLastError(), (int)CreateDetouredProcessStatus::CreateProcessAttributeListFailed);
		HandleDetoursInjectionAndCommunicationErrors(DETOURS_CREATE_PROCESS_ATTRIBUTE_LIST_21, L"Failure in CreateDetouredProcess. Exiting with code -63.", DETOURS_WINDOWS_LOG_MESSAGE_21);

		if (LogProcessDetouringStatus())
		{
			ReportProcessDetouringStatus(
				ProcessDetouringStatus_Done,
				L"",
				(LPWSTR)lpcwCommandLine,
				0,
				INVALID_HANDLE_VALUE,
				0,
				dwCreationFlags,
				false,
				GetLastError(),
				CreateDetouredProcessStatus::CreateProcessAttributeListFailed);
		}

		return CreateDetouredProcessStatus::CreateProcessAttributeListFailed;
	}

	if (!CreateProcAttributesForExplicitHandleInheritance(
		hStdInput, 
		hStdOutput, 
		hStdError,
		/*out*/ processCreationAttributes)) {
		Dbg(L"Failed creating extended attributes");
		fwprintf(stderr, L"Failure in CreateDetouredProcess creating ProcAttributes for explicit handle inheritance. LastError: %d, Status: %d. Exiting with code -49.", (int)GetLastError(), (int)CreateDetouredProcessStatus::HandleInheritanceFailed);
		HandleDetoursInjectionAndCommunicationErrors(DETOURS_INHERIT_HANDLES_ERROR_7, L"Failure in CreateDetouredProcess. Exiting with code -49.", DETOURS_WINDOWS_LOG_MESSAGE_7);

		if (LogProcessDetouringStatus())
		{
			ReportProcessDetouringStatus(
				ProcessDetouringStatus_Done,
				L"",
				(LPWSTR)lpcwCommandLine,
				0,
				INVALID_HANDLE_VALUE,
				0,
				dwCreationFlags,
				false,
				GetLastError(),
				CreateDetouredProcessStatus::HandleInheritanceFailed);
		}

		return CreateDetouredProcessStatus::HandleInheritanceFailed;
	}

	if (addProcessToSilo)
	{
		if (!CreateProcAttributeForAddingProcessToSilo(
			/*in out*/ processCreationAttributes)) {
			Dbg(L"Failed adding process to silo");
			fwprintf(stderr, L"Failure in CreateDetouredProcess adding process to a silo. LastError: %d, Status: %d. Exiting with code -61.", (int)GetLastError(), (int)CreateDetouredProcessStatus::AddProcessToSiloFailed);
			HandleDetoursInjectionAndCommunicationErrors(DETOURS_ADD_TO_SILO_ERROR_20, L"Failure in CreateDetouredProcess. Exiting with code -62.", DETOURS_WINDOWS_LOG_MESSAGE_20);

			if (LogProcessDetouringStatus())
			{
				ReportProcessDetouringStatus(
					ProcessDetouringStatus_Done,
					L"",
					(LPWSTR)lpcwCommandLine,
					0,
					INVALID_HANDLE_VALUE,
					0,
					dwCreationFlags,
					false,
					GetLastError(),
					CreateDetouredProcessStatus::AddProcessToSiloFailed);
			}

			return CreateDetouredProcessStatus::AddProcessToSiloFailed;
		}
	}
	
	return CreateDetouredProcessStatus::Succeeded;
}

CreateDetouredProcessStatus
WINAPI
CreateDetouredProcess(
    LPCWSTR lpcwCommandLine,
    DWORD dwCreationFlags,
    LPVOID lpEnvironment,
    LPCWSTR lpcwWorkingDirectory,
    HANDLE hStdInput, HANDLE hStdOutput, HANDLE hStdError,
    HANDLE hJob,
    DetouredProcessInjector *injector,
    bool addProcessToSilo,
    HANDLE* phProcess, HANDLE* phThread, DWORD* pdwProcessId
)
{
    // No detours should be called recursively from here.
    DetouredScope scope;

    size_t nBuffer = wcslen(lpcwCommandLine) + 1;
    unique_ptr<wchar_t[]> buffer(new wchar_t[nBuffer]);
    assert(buffer.get());
    wcscpy_s(buffer.get(), nBuffer, lpcwCommandLine); // CreateProcess wants a mutable string

    STARTUPINFOEXW si;
    ZeroMemory(&si, sizeof(si));

    si.StartupInfo.cb = sizeof(si);
    si.StartupInfo.hStdInput = hStdInput;
    si.StartupInfo.hStdOutput = hStdOutput;
    si.StartupInfo.hStdError = hStdError;
    si.StartupInfo.dwFlags = STARTF_USESTDHANDLES;

    PROCESS_INFORMATION pi;
    ZeroMemory(&pi, sizeof(pi));

	ProcessCreationAttributes processCreationAttributes = ProcessCreationAttributes(hJob);
    
	CreateDetouredProcessStatus createAttributesStatus = CreateProcessAttributes(
		hStdInput, 
		hStdOutput, 
		hStdError,
		lpcwCommandLine,
        dwCreationFlags,
        addProcessToSilo,
		/*in out*/ processCreationAttributes);

	if (createAttributesStatus != CreateDetouredProcessStatus::Succeeded)
	{
		return createAttributesStatus;
	}

    si.lpAttributeList = processCreationAttributes.attrList.get();

    // Here we pass in the public CreateProcessW entry point as we are not within the
    // detour of CreateProcessW but rather doing one of our own.
    CreateDetouredProcessStatus status = InternalCreateDetouredProcess(
        /* lpApplicationName */ NULL,
        /* lpCommandLine */ buffer.get(),
        /* lpProcessAttributes */ NULL,
        /* lpThreadAttributes */ NULL,
        /* bInheritHandles */ TRUE,
        dwCreationFlags | EXTENDED_STARTUPINFO_PRESENT,
        lpEnvironment,
        lpcwWorkingDirectory,
        /* lpStartupInfo */ (STARTUPINFOW*)&si,
        processCreationAttributes.hJob,
        injector,
        &pi,
        CreateProcessW);

    *phProcess = pi.hProcess;
    *phThread = pi.hThread;
    *pdwProcessId = pi.dwProcessId;

    return status;
}

//
// Code that runs in detoured process
//

#pragma warning( push )
#pragma warning( disable: 4100 ) // Unreferenced parameters

// Debug hook for CRT-sourced failures, e.g. heap corruption detection.
// Versus the default handling, this one triggers a post-mortem debugger,
// if configured, via debugbreak exceptions. This replaces the default behavior
// of showing an Abort / Retry / Ignore dialog.
static int __cdecl CrtDebugHook(int nReportType, wchar_t* szMsg, int* pnRet) {
    RaiseFailFastException(nullptr, nullptr, FAIL_FAST_GENERATE_EXCEPTION_ADDRESS);
    return FALSE;
}
#pragma warning( pop )

#ifdef DETOURS_SERVICES_NATIVES_LIBRARY

static bool DllProcessDetach()
{
    if (ShouldLogProcessData())
    {
        FILETIME creationTime;
        FILETIME exitTime;
        FILETIME kernelTime;
        FILETIME userTime;
        IO_COUNTERS counters;
        DWORD exitCode = PROCESS_EXIT_CODE_CANNOT_BE_RETRIEVED;
        HANDLE const currentProcess = GetCurrentProcess();

        if (GetProcessIoCounters(currentProcess, &counters) == 0)
        {
            Dbg(L"DllProcessDetach failed GetProcessIoConters with GLE=%d.", GetLastError());
            return TRUE;
        }

        if (GetProcessTimes(currentProcess, &creationTime, &exitTime, &kernelTime, &userTime) == 0)
        {
            Dbg(L"DllProcessDetach failed GetProcessTimes with GLE=%d.", GetLastError());
            return TRUE;
        }

        // The exitCode will be PROCESS_EXIT_CODE_CANNOT_BE_RETRIEVED when GetExitCodeProcess fails.
        if (GetExitCodeProcess(currentProcess, &exitCode) == 0)
        {
            Dbg(L"DllProcessDetach failed GetExitCodeProcess with GLE=%d.", GetLastError());
        }

        // The time reported by GetSystemTimeAsFileTime is in UTC format. It is also just
        // a read of the system clock (no calculations are performed), so it is quick
        // to retrieve. The time is read in the detour rather than in the processing of the
        // report in BuildXL to reduce the time difference between the time the report
        // is generated, and handling of the report message.
        GetSystemTimeAsFileTime(&exitTime);
        ReportProcessData(counters, creationTime, exitTime, kernelTime, userTime, exitCode, g_parentProcessId, (LONG64)g_detoursMaxAllocatedMemoryInBytes);
    }

#if MEASURE_DETOURED_NT_CLOSE_IMPACT	
    // Do some statistical information logging for different measurements
    Dbg(L"Populate NtClose pool list entries time: %d ms.", g_msTimeToPopulatePoolList);
    Dbg(L"Pip execution time: %d ms.", (LONG)(GetTickCount64() - g_pipExecutionStart));
    Dbg(L"NtCloseHandle call times: %d", g_ntCloseHandeCount);
    Dbg(L"Maxinum closed list count: %d", g_maxClosedListCount);
    Dbg(L"Time adding to closed list: %d ms.", g_msTimeInAddClosedList);
    Dbg(L"Time removing from closed list: %d ms.", g_msTimeInRemoveClosedList);
#endif // MEASURE_DETOURED_NT_CLOSE_IMPACT

    return TRUE;
}

#elif defined(BUILDXL_NATIVES_LIBRARY) 
static bool DllProcessDetach()
{
    if (g_pManifestTranslatePathTuples != nullptr)
    {
        delete g_pManifestTranslatePathTuples;
    }

    if (g_pDetouredProcessInjector != nullptr)
    {
        delete g_pDetouredProcessInjector;
    }

    if (g_hPrivateHeap != nullptr)
    {
        HeapDestroy(g_hPrivateHeap);
    }

    return true;
}
#else
#error BUILDXL_NATIVES_LIBRARY or DETOURS_SERVICES_NATIVES_LIBRARY must be defined.
#endif // DETOURS_SERVICES_NATIVES_LIBRARY

/*

This function runs during DLL process attach, DllMain executing with DLL_PROCESS_ATTACH.
Special restrictions apply when running within this context; please take great care
not to violate these restrictions. For more info, see DllMain in MSDN. Specifically,
all forms of dynamic library binding (LoadLibrary and friends) are forbidden.

The purpose of this function is to use the Detours API, which has been statically linked
into this PE/COFF image, to detour several important Windows file access APIs. "Detour"
here, when used as a verb, refers to intercepting calls to functions, usually functions
exported by DLLs, to invoke a different implementation. The functions which detour
the file access APIs implement the file access monitoring functionality of this library,
including potentially denying access to files, based on the contents of the file access
manifest that was provided by the creator of this process.

This function also handles locating and parsing the file access manifest.
The file access manifest specifies which files and directories this process may access,
and specifies what action to take when the process violates the file access manifest
(by requesting access to files that are outside of the manifest).  The actions taken
(independently) may include:
* printing diagnostic messages on stderr,
* allowing or prohibiting the file access request.

If this function fails in the presence of a payload, it returns false, and the caller,
DllMain, also returns false. This prevents the DLL from attaching to the process,
which usually has the effect of causing the process into which this DLL was injected
to fail to load. This is the desired behavior. If the file access APIs cannot be detoured,
then the process cannot execute with the desired behavior (of enforcing file access).

*/
#ifdef DETOURS_SERVICES_NATIVES_LIBRARY

// Flipped to true when DllProcessAttach has completed for the Detouring case.
bool g_isAttached = false;

static bool DllProcessAttach()
{
#if MEASURE_DETOURED_NT_CLOSE_IMPACT
    g_pipExecutionStart = GetTickCount64();
#endif // MEASURE_DETOURED_NT_CLOSE_IMPACT

    // One-time init for the Detours library.
    DetourInit();

    // Debug hook for CRT-sourced failures, e.g. heap corruption detection.
    // Causes a debugger break (or post-mortem launch) instead of showing a modal dialog.
    _CrtSetReportHookW2(_CRT_RPTHOOK_INSTALL, &CrtDebugHook);

    g_hPrivateHeap = HeapCreate(0, 40960, 0); // Commit initially 40k of memory for the private heap.
    if (g_hPrivateHeap == nullptr)
    {
        Dbg(L"Failure creating private heap. Last Error: %d", (int)GetLastError());
        return false;
    }

    g_pManifestTranslatePathTuples = new vector<TranslatePathTuple*>();
    g_pDetouredProcessInjector = new DetouredProcessInjector(g_manifestGuid);

    int error;

    if (!LocateAndParseFileAccessManifest()) {
        // When DetoursServices.dll is loaded, there always must be a valid FileAccess manifest.
        // Otherwise it is an error.
        return false;
    }

    // Retrieve the id of the current processe's parent process
    if (ShouldLogProcessData())
    {
        RetrieveParentProcessId();
    }

    g_invariantLocale = _wcreate_locale(LC_CTYPE, L"");
    InitProcessKind();
    InitializeHandleOverlay();

#define ATTACH(Name) \
    Real_##Name = ::Name; \
    error = DetourAttach((PVOID*)&Real_##Name, Detoured_##Name); \
    if (error != ERROR_SUCCESS) { \
        Dbg(L"Failed to attach to function: " L#Name); \
        failed = true; \
    }
// end #define ATTACH

    bool failed = false;

    error = DetourTransactionBegin();
    if (error != NO_ERROR) {
        Dbg(L"DetourTransactionBegin() failed.  Cannot detour file access.");
        return false;
    }

    // Next, attach to (detour) each API function of interest.
    if (!DisableDetours())
    {
        ATTACH(CreateProcessA);
        ATTACH(CreateProcessW);

        if (GetProcessKind() != SpecialProcessKind::WinDbg) {
            ATTACH(CreateFileW);
            ATTACH(CreateFileA);
       
            ATTACH(GetVolumePathNameW);
            ATTACH(GetFileAttributesA);
            ATTACH(GetFileAttributesW);
            ATTACH(GetFileAttributesExW);
            ATTACH(GetFileAttributesExA);

            ATTACH(GetFileInformationByHandle);
            ATTACH(GetFileInformationByHandleEx);
            ATTACH(SetFileInformationByHandle);

            ATTACH(CopyFileW);
            ATTACH(CopyFileA);
            ATTACH(CopyFileExW);
            ATTACH(CopyFileExA);
            ATTACH(MoveFileW);
            ATTACH(MoveFileA);
            ATTACH(MoveFileExW);
            ATTACH(MoveFileExA);
            ATTACH(MoveFileWithProgressW);
            ATTACH(MoveFileWithProgressA);
            ATTACH(ReplaceFileW);
            ATTACH(ReplaceFileA);
            ATTACH(DeleteFileA);
            ATTACH(DeleteFileW);

            ATTACH(CreateHardLinkW);
            ATTACH(CreateHardLinkA);
            ATTACH(CreateSymbolicLinkW);
            ATTACH(CreateSymbolicLinkA);
            ATTACH(FindFirstFileW);
            ATTACH(FindFirstFileA);
            ATTACH(FindFirstFileExW);
            ATTACH(FindFirstFileExA);
            ATTACH(FindNextFileW);
            ATTACH(FindNextFileA);
            ATTACH(FindClose);
            ATTACH(OpenFileMappingW);
            ATTACH(OpenFileMappingA);
            ATTACH(GetTempFileNameW);
            ATTACH(GetTempFileNameA);
            ATTACH(CreateDirectoryW);
            ATTACH(CreateDirectoryA);
            ATTACH(CreateDirectoryExW);
            ATTACH(CreateDirectoryExA);
            ATTACH(RemoveDirectoryW);
            ATTACH(RemoveDirectoryA);
            ATTACH(DecryptFileW);
            ATTACH(DecryptFileA);
            ATTACH(EncryptFileW);
            ATTACH(EncryptFileA);
            ATTACH(OpenEncryptedFileRawW);
            ATTACH(OpenEncryptedFileRawA);
            ATTACH(OpenFileById);
            ATTACH(GetFinalPathNameByHandleW);
            ATTACH(GetFinalPathNameByHandleA);

            ATTACH(NtCreateFile);
            ATTACH(NtOpenFile);
            ATTACH(ZwCreateFile);
            ATTACH(ZwOpenFile);
            ATTACH(NtQueryDirectoryFile);
            ATTACH(ZwQueryDirectoryFile);
            // See comments in DetorsFunctions.cpp
            // on the Detoured_NtClose for more information 
            // on this function.
            ATTACH(NtClose);
            ATTACH(ZwSetInformationFile);
        }
        else {
            Dbg(L"File detours are disabled while running inside of WinDbg. Child processes will still be detoured.");
        }
    }

    if (failed) {
        DetourTransactionAbort();
        Dbg(L"The Detours package could not be initialized.  Failed to attach to one or more functions.");
        return false;
    }

    error = DetourTransactionCommit();

    if (error != ERROR_SUCCESS) {
        DetourTransactionAbort();
        Dbg(L"The Detours package could not be initialized.  The transaction could not be committed.");
        return false;
    }

    //
    // File APIs successfully detoured.
    //

    g_BreakOnAccessDenied = (g_fileAccessManifestFlags & FileAccessManifestFlag::BreakOnAccessDenied) != FileAccessManifestFlag::None;
    WCHAR envvar[0x20 + 1];
    DWORD length = GetEnvironmentVariable(L"DetouredFileServices_BreakOnAccessDenied", envvar, 0x20);
    if (length != 0 && length < 0x20 && _wcsicmp(envvar, L"true") == 0) {
        g_BreakOnAccessDenied = true;
    }

#undef ATTACH

    g_isAttached = true;

    if (!IgnorePreloadedDlls())
    {
        HMODULE hMods[1024];
        HANDLE hProcess;
        DWORD cbNeeded;
        unsigned int i;
        bool failedInitPolicy = false;

        hProcess = GetCurrentProcess();
        // Get a list of all the modules in this process.
        wchar_t* szModName = new wchar_t[BUILDXL_PRELOADED_DLLS_MAX_PATH];

        if (EnumProcessModules(hProcess, hMods, sizeof(hMods), &cbNeeded))
        {
            for (i = 0; i < (cbNeeded / sizeof(HMODULE)); i++)
            {
                // Get the full path to the module's file.

                if (GetModuleFileName(hMods[i], szModName,
                    sizeof(szModName) / sizeof(wchar_t)) != 0)
                {
                    // Print the module name and handle value.
                    FileOperationContext fileOperationContext = FileOperationContext::CreateForRead(L"CreateFile", szModName);

                    PolicyResult policyResult;
                    if (!policyResult.Initialize(szModName)) {
                        policyResult.ReportIndeterminatePolicyAndSetLastError(fileOperationContext);
                        failedInitPolicy = true;
                    }

                    if (!failedInitPolicy)
                    {
                        // Now we can make decisions based on the file's existence and type.
                        DWORD attributes = GetFileAttributesW(szModName);
                        DWORD errorProbe = ERROR_SUCCESS;
                        if (attributes == INVALID_FILE_ATTRIBUTES) {
                            errorProbe = GetLastError();
                        }

                        if (errorProbe == ERROR_SUCCESS)
                        {
                            assert(attributes != INVALID_FILE_ATTRIBUTES);
                            FileReadContext fileReadContext;
                            fileReadContext.InferExistenceFromError(errorProbe);
                            fileReadContext.OpenedDirectory = ((attributes & FILE_ATTRIBUTE_DIRECTORY) != 0);
                            if (!fileReadContext.OpenedDirectory) {
                                AccessCheckResult accessCheck = policyResult.CheckReadAccess(RequestedReadAccess::Read, fileReadContext);
                                ReportIfNeeded(accessCheck, fileOperationContext, policyResult, 0);
                            }
                        }
                    }
                }
            }
        }

        delete[] szModName;

        // Release the handle to the process.

        CloseHandle(hProcess);
    }

    return true;
}
#elif defined(BUILDXL_NATIVES_LIBRARY) 
static bool DllProcessAttach()
{
    g_hPrivateHeap = HeapCreate(0, 40960, 0); // Commit initially 40k of memory for the private heap.
    if (g_hPrivateHeap == nullptr)
    {
        Dbg(L"Failure creating private heap. Last Error: %d", (int)GetLastError());
        return false;
    }

    g_pManifestTranslatePathTuples = new vector<TranslatePathTuple*>();
    g_pDetouredProcessInjector = new DetouredProcessInjector(g_manifestGuid);

    return true;
}
#else
#error BUILDXL_NATIVES_LIBRARY or DETOURS_SERVICES_NATIVES_LIBRARY must be defined.
#endif // DETOURS_SERVICES_NATIVES_LIBRARY


void RetrieveParentProcessId()
{
    PROCESS_BASIC_INFORMATION processBasicInformation;
    ULONG structSize = 0;

    HANDLE const currentProcess = GetCurrentProcess();

    if ((NtQueryInformationProcess(currentProcess, ProcessBasicInformation, &processBasicInformation, sizeof(processBasicInformation), &structSize) >= 0) &&
        (structSize == sizeof(processBasicInformation)))
    {
        g_parentProcessId = ((ULONG_PTR)processBasicInformation.Reserved3) & 0xFFFFFFFF;
    }
    else
    {
        g_parentProcessId = 0;
    }
}

#pragma warning( disable : 4100)

BOOL
WINAPI
DllMain(
_In_ HINSTANCE instance,
_In_ ULONG reason,
_In_ PVOID reserved)
{
    switch (reason) {
    case DLL_PROCESS_ATTACH:
        if (DllProcessAttach()) {
            return TRUE;
        }
#ifdef DETOURS_SERVICES_NATIVES_LIBRARY
        DebuggerOutputDebugString(L"DllProcessAttach() failed.\r\n", true);
#endif // DETOURS_SERVICES_NATIVES_LIBRARY
        return FALSE;

    case DLL_PROCESS_DETACH:
        if (DllProcessDetach()) {
            return TRUE;
        }
#ifdef DETOURS_SERVICES_NATIVES_LIBRARY
#if MEASURE_DETOURED_NT_CLOSE_IMPACT
        DebuggerOutputDebugString(L"DllProcessAttach() failed.\r\n", true);
#endif // MEASURE_DETOURED_NT_CLOSE_IMPACT
#endif // DETOURS_SERVICES_NATIVES_LIBRARY
        return FALSE;

    default:
        return TRUE;
    }
}

#ifdef BUILDXL_NATIVES_LIBRARY
bool
WINAPI
IsDetoursDebug()
{
#ifdef _DEBUG
    return true;
#else // !_DEBUG
    return false;
#endif // _DEBUG
}

enum class CreateDetachedProcessStatus : int {
    Succeeded = 0,
    ProcessCreationFailed = 1,
    JobBreakawayFailed = 2
};

// This is a CreateProcess wrapper suitable for spawning off long-lived server processes.
// In particular:
// - The new process does not inherit any handles (TODO: If needed, one could allow explicit handle inheritance here).
// - The new process is detached from the current job, if any (CREATE_BREAKAWAY_FROM_JOB)
//   (note that process creation fails if breakwaway is not allowed).
// - The new process gets a new (invisible) console (CREATE_NO_WINDOW).
// Note that lpEnvironment is assumed to be a unicode environment block.
CreateDetachedProcessStatus
WINAPI
CreateDetachedProcess(
    LPCWSTR lpcwCommandLine,
    LPVOID lpEnvironment,
    LPCWSTR lpcwWorkingDirectory,
    DWORD* pdwProcessId)
{
    // No detours should be called recursively from here.
    DetouredScope scope;

    size_t nBuffer = wcslen(lpcwCommandLine) + 1;
    unique_ptr<wchar_t[]> buffer(new wchar_t[nBuffer]);
    assert(buffer.get());
    wcscpy_s(buffer.get(), nBuffer, lpcwCommandLine); // CreateProcess wants a mutable string

    STARTUPINFOW si;
    ZeroMemory(&si, sizeof(si));

    PROCESS_INFORMATION pi;
    ZeroMemory(&pi, sizeof(pi));

    BOOL created = CreateProcessW(
        /* lpApplicationName */ NULL,
        /* lpCommandLine */ buffer.get(),
        /* lpProcessAttributes */ NULL,
        /* lpThreadAttributes */ NULL,
        /* bInheritHandles */ FALSE, // This is important to prevent accidentally grabbing e.g. pipe handles from the parent.
        CREATE_BREAKAWAY_FROM_JOB | CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT,
        lpEnvironment,
        lpcwWorkingDirectory,
        /* lpStartupInfo */ &si,
        &pi);

    if (created)
    {
        CloseHandle(pi.hProcess);
        CloseHandle(pi.hThread);
        *pdwProcessId = pi.dwProcessId;

        return CreateDetachedProcessStatus::Succeeded;
    }
    else
    {
        *pdwProcessId = 0;

        DWORD error = GetLastError();
        if (error == ERROR_ACCESS_DENIED)
        {
            // Unfortunately, failure to breakaway looks like ERROR_ACCESS_DENIED (though that is kind of ambiguous.)
            return CreateDetachedProcessStatus::JobBreakawayFailed;
        }
        else
        {
            return CreateDetachedProcessStatus::ProcessCreationFailed;
        }
    }
}

#endif // BUILDXL_NATIVES_LIBRARY

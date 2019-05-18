// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// Main.cpp : Defines the entry point of this test application.

#include "stdafx.h"

#pragma warning( disable : 4711) // ... selected for inline expansion

using namespace std;

#include "Logging.h"
#include "ReadExclusive.h"
#include "ShortNames.h"
#include "SymLinkTests.h"
#include "Tests.h"
#include "Timestamps.h"
#include "Utils.h"

// ----------------------------------------------------------------------------
// DEFINES
// ----------------------------------------------------------------------------

#define ERROR_WRONG_NUM_ARGS    1
#define ERROR_INVALID_COMMAND   2

typedef enum _FILE_INFORMATION_CLASS_EXTRA {
    FileFullDirectoryInformation = 2,
    FileBothDirectoryInformation,
    FileBasicInformation,
    FileStandardInformation,
    FileInternalInformation,
    FileEaInformation,
    FileAccessInformation,
    FileNameInformation,
    FileRenameInformation,
    FileLinkInformation,
    FileNamesInformation,
    FileDispositionInformation,
    FilePositionInformation,
    FileFullEaInformation,
    FileModeInformation,
    FileAlignmentInformation,
    FileAllInformation,
    FileAllocationInformation,
    FileEndOfFileInformation,
    FileAlternateNameInformation,
    FileStreamInformation,
    FilePipeInformation,
    FilePipeLocalInformation,
    FilePipeRemoteInformation,
    FileMailslotQueryInformation,
    FileMailslotSetInformation,
    FileCompressionInformation,
    FileObjectIdInformation,
    FileCompletionInformation,
    FileMoveClusterInformation,
    FileQuotaInformation,
    FileReparsePointInformation,
    FileNetworkOpenInformation,
    FileAttributeTagInformation,
    FileTrackingInformation,
    FileIdBothDirectoryInformation,
    FileIdFullDirectoryInformation,
    FileValidDataLengthInformation,
    FileShortNameInformation,
    FileIoCompletionNotificationInformation,
    FileIoStatusBlockRangeInformation,
    FileIoPriorityHintInformation,
    FileSfioReserveInformation,
    FileSfioVolumeInformation,
    FileHardLinkInformation,
    FileProcessIdsUsingFileInformation,
    FileNormalizedNameInformation,
    FileNetworkPhysicalNameInformation,
    FileIdGlobalTxDirectoryInformation,
    FileIsRemoteDeviceInformation,
    FileUnusedInformation,
    FileNumaNodeInformation,
    FileStandardLinkInformation,
    FileRemoteProtocolInformation,
    FileRenameInformationBypassAccessCheck,
    FileLinkInformationBypassAccessCheck,
    FileVolumeNameInformation,
    FileIdInformation,
    FileIdExtdDirectoryInformation,
    FileReplaceCompletionInformation,
    FileHardLinkFullIdInformation,
    FileIdExtdBothDirectoryInformation,
    FileDispositionInformationEx,
    FileRenameInformationEx,
    FileRenameInformationExBypassAccessCheck,
    FileDesiredStorageClassInformation,
    FileStatInformation,
    FileMemoryPartitionInformation,
    FileStatLxInformation,
    FileCaseSensitiveInformation,
    FileLinkInformationEx,
    FileLinkInformationExBypassAccessCheck,
    FileStorageReserveIdInformation,
    FileCaseSensitiveInformationForceAccessCheck,
    FileMaximumInformation
} FILE_INFORMATION_CLASS_EXTRA, *PFILE_INFORMATION_CLASS_EXTRA;

typedef struct _FILE_LINK_INFORMATION {
    BOOLEAN ReplaceIfExists;
    HANDLE  RootDirectory;
    ULONG   FileNameLength;
    WCHAR   FileName[1];
} FILE_LINK_INFORMATION, *PFILE_LINK_INFORMATION;

typedef struct _FILE_LINK_INFORMATION_EX {
    union {
        BOOLEAN ReplaceIfExists;
        ULONG Flags;
    };
    HANDLE  RootDirectory;
    ULONG   FileNameLength;
    WCHAR   FileName[1];
} FILE_LINK_INFORMATION_EX, *PFILE_LINK_INFORMATION_EX;

typedef struct _FILE_RENAME_INFORMATION {
    BOOLEAN ReplaceIfExists;
    HANDLE RootDirectory;
    ULONG FileNameLength;
    WCHAR FileName[1];
} FILE_RENAME_INFORMATION, *PFILE_RENAME_INFORMATION;

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

    NTSTATUS NTAPI ZwClose(_In_ HANDLE FileHandle);
}

static BOOL SetRenameFileByHandle(HANDLE hFile, const wstring& target)
{
    size_t targetLength = target.length();
    size_t targetLengthInBytes = targetLength * sizeof(WCHAR);
    size_t bufferSize = sizeof(FILE_RENAME_INFO) + targetLengthInBytes;
    auto const buffer = make_unique<char[]>(bufferSize);
    auto const fri = reinterpret_cast<PFILE_RENAME_INFO>(buffer.get());
    fri->ReplaceIfExists = TRUE;
    fri->FileNameLength = (ULONG)targetLengthInBytes;
    fri->RootDirectory = nullptr;
    wmemcpy(fri->FileName, target.c_str(), targetLength);

    return SetFileInformationByHandle(
        hFile,
        FILE_INFO_BY_HANDLE_CLASS::FileRenameInfo,
        fri,
        (DWORD)bufferSize);
}

static NTSTATUS ZwSetRenameFileByHandle(HANDLE hFile, LPCWSTR targetName)
{
    wstring target;
    if (!TryGetNtFullPath(targetName, target))
    {
        return (NTSTATUS)STATUS_INVALID_HANDLE;
    }

    size_t targetLength = target.length();
    size_t targetLengthInBytes = targetLength * sizeof(WCHAR);
    size_t bufferSize = sizeof(FILE_RENAME_INFORMATION) + targetLengthInBytes;
    auto const buffer = make_unique<char[]>(bufferSize);
    auto const fri = reinterpret_cast<PFILE_RENAME_INFORMATION>(buffer.get());
    fri->ReplaceIfExists = TRUE;
    fri->FileNameLength = (ULONG)targetLengthInBytes;
    fri->RootDirectory = nullptr;
    wmemcpy(fri->FileName, target.c_str(), targetLength);

    IO_STATUS_BLOCK ioStatusBlock;
    return ZwSetInformationFile(
        hFile,
        &ioStatusBlock,
        fri,
        (ULONG)bufferSize,
        (FILE_INFORMATION_CLASS)FILE_INFORMATION_CLASS_EXTRA::FileRenameInformation);
}

// Generic tests.
int CallPipeTest()
{
    HANDLE hPipe = CreateNamedPipe(
        L"\\\\.\\pipe\\foo\\bar", // pipe name 
        PIPE_ACCESS_DUPLEX,       // read/write access 
        PIPE_TYPE_MESSAGE |       // message type pipe 
        PIPE_READMODE_MESSAGE |   // message-read mode 
        PIPE_WAIT,                // blocking mode 
        PIPE_UNLIMITED_INSTANCES, // max. instances  
        512,                      // output buffer size 
        512,                      // input buffer size 
        0,                        // client time-out 
        NULL);                    // default security attribute 

    HANDLE hFile = CreateFileW(
        L"\\\\.\\pipe\\foo\\bar",
        GENERIC_READ | GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    CloseHandle(hFile);
    CloseHandle(hPipe);

    return (int)GetLastError();
}

int CallDirectoryEnumerationTest()
{
    WIN32_FIND_DATA ffd;
    HANDLE hFind = INVALID_HANDLE_VALUE;
    DWORD dwError = 0;

    hFind = FindFirstFile(L"input\\*", &ffd);

    if (INVALID_HANDLE_VALUE == hFind)
    {
        return 21;
    }

    // List all the files
    while (FindNextFile(hFind, &ffd) != 0);

    dwError = GetLastError();
    if (dwError == ERROR_NO_MORE_FILES)
    {
        dwError = ERROR_SUCCESS;
    }

    FindClose(hFind);
    return (int)dwError;
}

int CallDeleteFileTest()
{
    DeleteFile(L"input\\Test1.txt");
    return (int)GetLastError();
}

int CallDeleteFileStdRemoveTest()
{
    std::remove("input\\Test1.txt");
    return (int)GetLastError();
}

int CallDeleteDirectoryTest()
{
    RemoveDirectory(L"input\\");
    return (int)GetLastError();
}

int CallCreateDirectoryTest()
{
    CreateDirectory(L"input\\", 0);
    return (int)GetLastError();
}

int CallDetouredZwCreateFile()
{
    OBJECT_ATTRIBUTES objAttribs = { 0 };

    wstring fullPath;
    if (!TryGetNtFullPath(L"input\\ZwCreateFileTest1.txt", fullPath))
    {
        return (int)GetLastError();
    }

    UNICODE_STRING unicodeString;
    RtlInitUnicodeString(&unicodeString, fullPath.c_str());

    InitializeObjectAttributes(&objAttribs, &unicodeString, OBJ_CASE_INSENSITIVE, NULL, NULL);
    LARGE_INTEGER largeInteger = { 0 };
    IO_STATUS_BLOCK ioStatusBlock = { 0 };
    HANDLE handle = INVALID_HANDLE_VALUE;

    NTSTATUS status = ZwCreateFile(
        &handle,
        GENERIC_WRITE,
        &objAttribs,
        &ioStatusBlock,
        &largeInteger,
        FILE_ATTRIBUTE_NORMAL, 
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        FILE_OPEN_IF,
        0,
        NULL,
        0);

    if (handle != INVALID_HANDLE_VALUE)
    {
        ZwClose(handle);
    }

    return (int)RtlNtStatusToDosError(status);
}

int CallDetouredZwOpenFile()
{
    OBJECT_ATTRIBUTES objAttribs = { 0 };

    wstring fullPath;
    if (!TryGetNtFullPath(L"input\\ZwOpenFileTest2.txt", fullPath))
    {
        return (int)GetLastError();
    }

    UNICODE_STRING unicodeString;
    RtlInitUnicodeString(&unicodeString, fullPath.c_str());

    InitializeObjectAttributes(&objAttribs, &unicodeString, OBJ_CASE_INSENSITIVE, NULL, NULL);
    IO_STATUS_BLOCK ioStatusBlock = { 0 };
    HANDLE handle = INVALID_HANDLE_VALUE;
    
    NTSTATUS status = ZwOpenFile(
        &handle,
        GENERIC_READ,
        &objAttribs,
        &ioStatusBlock,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        0L /* No special create option is needed. */);

    if (handle != INVALID_HANDLE_VALUE)
    {
        ZwClose(handle);
    }
    
    return (int)RtlNtStatusToDosError(status);
}

PVOID CreateFileLinkInformation(const wstring& targetFileName, size_t targetFileNameLength, size_t targetFileNameLengthInBytes, const unique_ptr<char[]>& buffer) {
    auto const fli = reinterpret_cast<PFILE_LINK_INFORMATION>(buffer.get());
    fli->ReplaceIfExists = TRUE;
    fli->FileNameLength = (ULONG)targetFileNameLengthInBytes;
    fli->RootDirectory = nullptr;
    wmemcpy(fli->FileName, targetFileName.c_str(), targetFileNameLength);

    return fli;
}

PVOID CreateFileLinkInformationEx(const wstring& targetFileName, size_t targetFileNameLength, size_t targetFileNameLengthInBytes, const unique_ptr<char[]>& buffer) {
    auto const fli = reinterpret_cast<PFILE_LINK_INFORMATION_EX>(buffer.get());
    fli->ReplaceIfExists = TRUE;
    fli->FileNameLength = (ULONG)targetFileNameLengthInBytes;
    fli->RootDirectory = nullptr;
    wmemcpy(fli->FileName, targetFileName.c_str(), targetFileNameLength);

    return fli;
}

int InternalCallDetouredSetFileInformationFileLink(BOOL useExtendedFileInfo)
{
    HANDLE hFile = CreateFileW(
        L"input\\SetFileInformationFileLinkTest2.txt",
        GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_DELETE | FILE_SHARE_WRITE,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    wstring target;

    if (!TryGetNtFullPath(L"input\\SetFileInformationFileLinkTest1.txt", target)) 
    {
        return (int)GetLastError();
    }

    size_t targetLength = target.length();
    size_t targetLengthInBytes = targetLength * sizeof(WCHAR);
    size_t bufferSize = (useExtendedFileInfo ? sizeof(FILE_LINK_INFORMATION_EX) : sizeof(FILE_LINK_INFORMATION)) + targetLengthInBytes;
    auto const buffer = make_unique<char[]>(bufferSize);

    PVOID fli = useExtendedFileInfo 
        ? CreateFileLinkInformationEx(target, targetLength, targetLengthInBytes, buffer)
        : CreateFileLinkInformation(target, targetLength, targetLengthInBytes, buffer);

    IO_STATUS_BLOCK ioStatusBlock;
    NTSTATUS status = ZwSetInformationFile(
        hFile,
        &ioStatusBlock,
        fli,
        (ULONG)bufferSize,
        useExtendedFileInfo 
            ? (FILE_INFORMATION_CLASS)FILE_INFORMATION_CLASS_EXTRA::FileLinkInformationEx
            : (FILE_INFORMATION_CLASS)FILE_INFORMATION_CLASS_EXTRA::FileLinkInformation);

    CloseHandle(hFile);

    return (int)RtlNtStatusToDosError(status);
}

int CallDetouredSetFileInformationFileLink()
{
    return InternalCallDetouredSetFileInformationFileLink(FALSE);
}

int CallDetouredSetFileInformationFileLinkEx()
{
    return InternalCallDetouredSetFileInformationFileLink(TRUE);
}

int CallDetouredSetFileInformationByHandle()
{
    HANDLE hFile = CreateFileW(
        L"input\\SetFileInformationByHandleTest2.txt",
        DELETE,
        FILE_SHARE_READ | FILE_SHARE_DELETE | FILE_SHARE_WRITE,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    wstring target;
    if (!TryGetFullPath(L"input\\SetFileInformationByHandleTest1.txt", target))
    {
        return (int)GetLastError();
    }

    SetRenameFileByHandle(hFile, target);
    CloseHandle(hFile);

    return (int)GetLastError();
}

int CallDetouredGetFinalPathNameByHandle() 
{
    // input\GetFinalPathNameByHandleTest.txt points to inputTarget\GetFinalPathNameByHandleTest.txt
    HANDLE hFile = CreateFileW(
        L"input\\GetFinalPathNameByHandleTest.txt",
        GENERIC_READ,
        FILE_SHARE_READ,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    wchar_t buffer[MAX_PATH];
    DWORD err = GetFinalPathNameByHandleW(hFile, buffer, MAX_PATH, FILE_NAME_NORMALIZED);

    if (err == 0) 
    {
        err = GetLastError();
        CloseHandle(hFile);
        return (int)err;
    }

    CloseHandle(hFile);
    
    if (err < MAX_PATH) 
    {
        wstring finalPath;
        if (!TryGetFullPath(buffer, finalPath))
        {
            return (int)GetLastError();
        }

        if (finalPath.find(L"inputTarget\\") != std::wstring::npos)
        {
            // If inputTarget occurs, then the translation doesn't happen in GetFinalPathNameByHandleW.
            return -1;
        }

        HANDLE hFile2 = CreateFileW(buffer, GENERIC_READ, FILE_SHARE_READ, 0, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
        
        if (hFile2 == INVALID_HANDLE_VALUE)
        {
            return (int)GetLastError();
        }

        CloseHandle(hFile2);
        return 0;
    }

    return -1;
}

int CallProbeForDirectory()
{
    HANDLE hFile = INVALID_HANDLE_VALUE;
    OBJECT_ATTRIBUTES objAttribs = { 0 };

    wstring fullPath;
    if (!TryGetNtFullPath(L"input.txt\\", fullPath)) 
    {
        return (int)GetLastError();
    }
    
    UNICODE_STRING unicodeString;
    RtlInitUnicodeString(&unicodeString, fullPath.c_str());

    InitializeObjectAttributes(&objAttribs, &unicodeString, OBJ_CASE_INSENSITIVE, NULL, NULL);
    LARGE_INTEGER largeInteger = { 0 };
    IO_STATUS_BLOCK ioStatusBlock = { 0 };
    NTSTATUS status = NtCreateFile(
        &hFile, 
        FILE_READ_ATTRIBUTES | SYNCHRONIZE, 
        &objAttribs, 
        &ioStatusBlock, 
        &largeInteger,
        FILE_ATTRIBUTE_NORMAL | FILE_ATTRIBUTE_DIRECTORY, 
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, 
        FILE_OPEN, 
        FILE_DIRECTORY_FILE, 
        NULL, 
        NULL);

    if (hFile != INVALID_HANDLE_VALUE) 
    { 
        wprintf(L"Closing");
        CloseHandle(hFile); 
    }

    return (int)RtlNtStatusToDosError(status);
}

int CallGetAttributeQuestion()
{
    GetFileAttributes(L"D:\\OffRel\\dev\\intl\\stryclientres\\ja-jp.hashid\\?");
    return 0;
}

int CallFileAttributeOnFileWithPipeChar()
{
    GetFileAttributes(L"N:\\src\\fiotml\\tml\\win\\ents\\ace_odbchklm.wxs*15><field>Ace_OdbcHKLM<\\field><field>{BA03E98E-7C33-49A9-A46B-1040D619F13D}<\\field><field>MSOFolder<\\field><field>260<\\field><field\\><field>reg2AF77D6A6AFBF009DFDFEFAABFFE44D7<\\field><\\row><row sectionId=wix.section.365 \
sourceLineNumber=n:\\src\\otools\\inc\\msiclient\\ScopedComponents\\Ace_OdbcCurrentUser.wxi*17|n:\\src\\otools\\inc\\msiclient\\WWHelperOutsideProduct.wxi*17|N:\\src\\msiclientlink\\x-none\\LyncWW\\LyncWW.wxs*32><field>Ace_OdbcCurrentUser<\\field><field>{E84F6924-4371-4B24-89C8-170D49785330}<\\field>\
<field>MSOFolder<\\field><field>260<\\field><field\\><field>reg71AE7F8CA7772860FE08285C871D8878<\\field><\\row><row sectionId=packlet.component.Clview_Core sourceLineNumber=N:\\src\\msiclient\\Global\\Components\\Clview_Core.wxs*13><field>Clview_Core<\\field><field>{10C0DCE8-7881-4A25-8111-108D8530A93C}\
<\\field><field>OfficeFolder<\\field><field>256<\\field><field\\><field>CLVIEW.EXE<\\field><\\row><row sectionId=packlet.component.Global_DocumentImaging_OcrCore sourceLineNumber=N:\\src\\msiclient\\Global\\Components\\Global_DocumentImaging_OcrCore.wxs*15><field>Global_DocumentImaging_OcrCore<\\field>\
<field>{0A13F7A9-A212-4554-84B2-10C6D4B8DDCD}<\\field><field>OfficeFolder<\\field><field>256<\\field><field\\><field>MSOCR.DLL<\\field><\\row><row sectionId=packlet.component.Global_GraphicsFilters_Path sourceLineNumber=N:\\src\\msiclient\\Global\\Components\\Global_GraphicsFilters_Path.wxs*15><field>Global_GraphicsFilters_Path\
<\\field><field>{520AB055-E065-48A6-9FC3-1A05601CD5E8}<\\field><field>GraphicsFiltersFolder<\\field><field>264<\\field><field\\><field\\><\\row><row sectionId=wix.section.375 sourceLineNumber=N:\\src\\msiclient\\Global\\Components\\Global_IMN_Core.wxs*13><field>Global_IMN_Core.x64<\\field><field>\
{2685B225-5F7D-4602-A194-1D5200091AE7}<\\field><field>OfficeFolder<\\field><field>256<\\field><field\\><field>NAME.DLL.x64<\\field><\\row><row sectionId=wix.section.376 sourceLineNumber=N:\\src\\msiclient\\Global\\Components\\NameControl_ProxyComponent.wxs*13><field>NameControl_ProxyComponent.x64<\\field><field>\
{7097BF6F-306B-40D5-B3D3-11F8CF58637F}<\\field><field>OfficeFolder<\\field><field>256<\\field><field\\><field>NAMECONTROLPROXY.DLL.x64<\\field><\\row><row sectionId=packlet.component.NameControl_ServerComponent sourceLineNumber=N:\\src\\msiclient\\Global\\Components\\NameControl_ServerComponent.wxs*14>\
<field>NameControl_ServerComponent<\\field><field>{52BAA893-55E6-49A5-A8A4-1D572BD3F2AB}<\\field><field>OfficeFolder<\\field><field>256<\\field><field\\><field>NAMECONTROLSERVER.EXE<\\field><\\row><row sectionId=packlet.component.Global_Fonts_Century sourceLineNumber=N:\\src\\msishared\\Global\\Components\\Global_Fonts_Century.wxs*13>\
<field>Global_Fonts_Century<\\field><field>{558B23E2-4DE3-4CAF-92CD-1E5052EC6273}<\\field><field>FontsFolder<\\field><field>272<\\field><field\\><field>CENTURY.TTF<\\field><\\row>\
<row sectionId=packlet.component.Global_ProofingTools_LADEngine sourceLineNumber=N:\\src\\msiclient\\Global\\Components\\Global_ProofingTools_LADEngine.wxs*14><field>Global_ProofingTools_LADEngine<\\field><field>{9422D6BD-92EE-403A-9615-1C7B832AEFCB}<\\field>\
<field>SharedProofFolder<\\field><field>256<\\field><field\\><field>MSLID.DLL<\\field><\\row><row sectionId=packlet.component.Global_AWSWeb_ClientSideTCDControlRegistration sourceLineNumber=N:\\src\\msiclient\\Global\\Components\\Global_AWSWeb_ClientSideTCDControlRegistration.wxs*15>\
<field>Global_AWSWeb_ClientSideTCDControlRegistration.x64<\\field><field>{BE2892D4-F8A5-413C-B164-1DEEE6501DE3}<\\field><field>OfficeFolder<\\field><field>260<\\field><field\\><field>regD6858819707C4520BFE2AC2ADBFF4F23<\\field><\\row>\
<row sectionId=packlet.component.Global_TextConverters_ConvertersCore sourceLineNumber=N:\\src\\msiclient\\Global\\Components\\Global_TextConverters_ConvertersCore.wxs*13><field>Global_TextConverters_ConvertersCore<\\field><field>{0EC9CCCD-0E53-4");
    return 0;
}

int CallAccessNetworkDrive()
{
    HANDLE hFile = CreateFileW(
        L"\\\\daddev\\office\\16.0\\7923.1000\\shadow\\store\\X64\\Debug\\airspace\\x-none\\inc\\airspace.etw.man",
        GENERIC_READ,
        FILE_SHARE_READ,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile != INVALID_HANDLE_VALUE)
    {
        CloseHandle(hFile);
    }

    SetLastError(ERROR_SUCCESS);

    return (int)GetLastError();
}

int CallAccessInvalidFile()
{
    HANDLE hFile = CreateFileW(
        L"@:\\office\\16.0\\7923.1000\\shadow\\store\\X64\\Debug\\airspace\\x-none\\inc\\airspace.etw.man",
        GENERIC_READ,
        FILE_SHARE_READ,
        0,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile != INVALID_HANDLE_VALUE)
    {
        CloseHandle(hFile);
    }

    return (int)GetLastError();
}

int CallGetAttributeNonExistent()
{
    wstring file;
    if (!TryGetFullPath(L"GetAttributeNonExistent.txt", file)) 
    {
        return (int)GetLastError();
    }

    GetFileAttributes(file.c_str());
    return 0;
}

int CallGetAttributeNonExistentInDepDirectory()
{
    wstring file;
    if (!TryGetFullPath(L"input\\GetAttributeNonExistent.txt", file))
    {
        return (int)GetLastError();
    }

    GetFileAttributes(file.c_str());
    return 0;
}

int CallDetouredCreateFileWWithGenericAllAccess()
{
    HANDLE hFile = CreateFileW(
        L"input\\CreateFileWWithGenericAllAccess1.txt",
        GENERIC_ALL,
        FILE_SHARE_DELETE,
        0,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    CloseHandle(hFile);
    
    return (int)GetLastError();
}

int CallDetouredCreateFileWForProbingOnly()
{
    HANDLE hFile = CreateFileW(
        L"input\\CreateFileWForProbingOnly.lnk",
        0,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, 
        NULL, 
        OPEN_EXISTING, 
        FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, 
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    CloseHandle(hFile);

    hFile = CreateFileW(
        L"input\\CreateFileWForProbingOnly.lnk",
        FILE_READ_ATTRIBUTES | FILE_READ_EA,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        NULL,
        OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
        NULL);

    if (hFile == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    CloseHandle(hFile);

    return (int)GetLastError();
}

int CallDetouredMoveFileExWForRenamingDirectory()
{
    DWORD attributes = GetFileAttributesW(L"OutputDirectory\\NewDirectory");
    if (attributes != INVALID_FILE_ATTRIBUTES && ((attributes & FILE_ATTRIBUTE_DIRECTORY) != 0)) 
    {
        // If target directory exists, move first to TempDirectory.
        MoveFileExW(L"OutputDirectory\\NewDirectory", L"TempDirectory", MOVEFILE_COPY_ALLOWED);
    }

    MoveFileExW(L"OldDirectory", L"OutputDirectory\\NewDirectory", MOVEFILE_COPY_ALLOWED);

    return (int)GetLastError();
}

int CallDetouredSetFileInformationByHandleForRenamingDirectory()
{
    DWORD attributes = GetFileAttributesW(L"OutputDirectory\\NewDirectory");

    if (attributes != INVALID_FILE_ATTRIBUTES && ((attributes & FILE_ATTRIBUTE_DIRECTORY) != 0))
    {
        // If target directory exists, move first to TempDirectory.

        HANDLE hNewDirectory = CreateFileW(
            L"OutputDirectory\\NewDirectory",
            GENERIC_READ | GENERIC_WRITE | DELETE,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            NULL,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            NULL);

        if (hNewDirectory == INVALID_HANDLE_VALUE)
        {
            return (int)GetLastError();
        }

        SetRenameFileByHandle(hNewDirectory, L"TempDirectory");

        CloseHandle(hNewDirectory);
    }

    HANDLE hOldDirectory = CreateFileW(
        L"OldDirectory",
        GENERIC_READ | GENERIC_WRITE | DELETE,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        NULL,
        OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS,
        NULL);

    if (hOldDirectory == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    SetRenameFileByHandle(hOldDirectory, L"OutputDirectory\\NewDirectory");

    CloseHandle(hOldDirectory);

    return (int)GetLastError();
}

int CallDetouredZwSetFileInformationByHandleForRenamingDirectory()
{
    DWORD attributes = GetFileAttributesW(L"OutputDirectory\\NewDirectory");

    if (attributes != INVALID_FILE_ATTRIBUTES && ((attributes & FILE_ATTRIBUTE_DIRECTORY) != 0))
    {
        // If target directory exists, move first to TempDirectory.

        HANDLE hNewDirectory = CreateFileW(
            L"OutputDirectory\\NewDirectory",
            GENERIC_READ | GENERIC_WRITE | DELETE,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            NULL,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            NULL);

        if (hNewDirectory == INVALID_HANDLE_VALUE)
        {
            return (int)GetLastError();
        }
        
        ZwSetRenameFileByHandle(hNewDirectory, L"TempDirectory");

        CloseHandle(hNewDirectory);
    }

    HANDLE hOldDirectory = CreateFileW(
        L"OldDirectory",
        GENERIC_READ | GENERIC_WRITE | DELETE,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        NULL,
        OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS,
        NULL);

    if (hOldDirectory == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    ZwSetRenameFileByHandle(hOldDirectory, L"OutputDirectory\\NewDirectory");

    CloseHandle(hOldDirectory);

    return (int)GetLastError();
}

int CallDetouredCreateFileWWrite()
{
    HANDLE hFile = CreateFileW(
        L"CreateFile",
        GENERIC_WRITE,
        FILE_SHARE_DELETE,
        0,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile != INVALID_HANDLE_VALUE)
    {
        CloseHandle(hFile);
    }

    return (int)GetLastError();
}

int CallCreateFileWithZeroAccessOnDirectory()
{
    HANDLE hFile = CreateFile(
        L"input",
        0,
        0,
        NULL,
        OPEN_EXISTING,
        0,
        NULL);

    if (hFile != INVALID_HANDLE_VALUE)
    {
        CloseHandle(hFile);
    }

    return (int)GetLastError();
}

// Tests reading a path starting with \\?\.
// The path read is named 'input', in the current working directory.
int CallCreateFileOnNtEscapedPath()
{
    wstring fullPath;
    
    if (!TryGetNtEscapedFullPath(L"input", fullPath)) 
    {
        return (int)GetLastError();
    }

    HANDLE hFile = CreateFile(
        fullPath.c_str(),
        GENERIC_READ,
        FILE_SHARE_READ,
        NULL,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        NULL);

    if (hFile != INVALID_HANDLE_VALUE)
    {
        CloseHandle(hFile);
    }

    return (int)GetLastError();
}

NTSTATUS OpenFileWithNtCreateFile(
    PHANDLE FileHandle, 
    LPCWSTR path, 
    HANDLE rootDirectory, 
    ACCESS_MASK DesiredAccess, 
    ULONG FileAttributes, 
    ULONG ShareAccess, 
    ULONG CreateDisposition, 
    ULONG CreateOptions)
{
    _NtCreateFile NtCreateFile = GetNtCreateFile();
    _RtlInitUnicodeString RtlInitUnicodeString = GetRtlInitUnicodeString();

    OBJECT_ATTRIBUTES objAttribs = { 0 };

    UNICODE_STRING unicodeString;
    RtlInitUnicodeString(&unicodeString, path);

    InitializeObjectAttributes(&objAttribs, &unicodeString, OBJ_CASE_INSENSITIVE, rootDirectory, NULL);

    const int allocSize = 2048;
    LARGE_INTEGER largeInteger = { 0 };
    largeInteger.QuadPart = allocSize;

    IO_STATUS_BLOCK ioStatusBlock = { 0 };
    NTSTATUS status = NtCreateFile(
        FileHandle,
        DesiredAccess,
        &objAttribs,
        &ioStatusBlock,
        &largeInteger,
        FileAttributes,
        ShareAccess,
        CreateDisposition,
        CreateOptions,
        NULL,
        NULL);

    return status;
}

HANDLE OpenFileByIndexForRead(HANDLE hVolume, DWORDLONG FileId)
{
    _NtCreateFile NtCreateFile = GetNtCreateFile();

    WCHAR Buffer[8] = { 0, 0, 0, 0, 0, 0, 0, 0 };

    *(DWORDLONG UNALIGNED*)Buffer = FileId;

    UNICODE_STRING PathStr;

    PathStr.Length = 8;
    PathStr.MaximumLength = 8;
    PathStr.Buffer = Buffer;

    OBJECT_ATTRIBUTES ObjectAttributes;

    InitializeObjectAttributes(
        &ObjectAttributes,
        &PathStr,
        OBJ_CASE_INSENSITIVE,
        hVolume,
        nullptr
    );

    HANDLE FileHandle = nullptr;
    IO_STATUS_BLOCK IoStatusBlock;
    NTSTATUS status;

    status = NtCreateFile(
        &FileHandle,
        FILE_GENERIC_READ,
        &ObjectAttributes,
        &IoStatusBlock,
        nullptr,
        FILE_ATTRIBUTE_NORMAL,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        FILE_OPEN,
        FILE_OPEN_BY_FILE_ID,
        nullptr,
        0);

    return !NT_SUCCESS(status) ? INVALID_HANDLE_VALUE : FileHandle;
}

int CallOpenFileById()
{
    _NtClose NtClose = GetNtClose();

    // D refers to directory Directory.
    // D\f refers to D\fileF.txt
    // D\g refers to D\fileG.txt

    // Get a handle to D, which will register this handle to Detours' handle cache.
    wstring directoryFullPath;
    if (!TryGetNtFullPath(L"Directory", directoryFullPath))
    {
        return (int)GetLastError();
    }

    HANDLE hDirectory;
    NTSTATUS status = OpenFileWithNtCreateFile(
        &hDirectory,
        directoryFullPath.c_str(),
        NULL,
        GENERIC_READ,
        FILE_ATTRIBUTE_NORMAL,
        FILE_SHARE_DELETE | FILE_SHARE_READ | FILE_SHARE_WRITE,
        FILE_OPEN,
        FILE_DIRECTORY_FILE);

    if (!NT_SUCCESS(status))
    {
        return (int)RtlNtStatusToDosError(status);
    }

    if (hDirectory == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    // Get D's index.
    BY_HANDLE_FILE_INFORMATION fileInfo;

    if (!GetFileInformationByHandle(hDirectory, &fileInfo))
    {
        NtClose(hDirectory);
        return (int)GetLastError();
    }

    DWORDLONG directoryIndex = ((DWORDLONG)fileInfo.nFileIndexHigh << 32) | fileInfo.nFileIndexLow;

    // Get D\f's handle.
    HANDLE hFile;
    status = OpenFileWithNtCreateFile(
        &hFile,
        L"fileF.txt",
        hDirectory,
        GENERIC_READ,
        FILE_ATTRIBUTE_NORMAL,
        FILE_SHARE_DELETE | FILE_SHARE_READ | FILE_SHARE_WRITE,
        FILE_OPEN,
        FILE_NON_DIRECTORY_FILE);

    if (!NT_SUCCESS(status))
    {
        NtClose(hDirectory);
        return (int)RtlNtStatusToDosError(status);
    }

    // Close handle to D 
    NtClose(hDirectory);

    // Reopen handle to D via D\f's handle and D's index.
    hDirectory = OpenFileByIndexForRead(hFile, directoryIndex);

    if (hDirectory == INVALID_HANDLE_VALUE)
    {
        NtClose(hFile);
        return (int)GetLastError();
    }

    // Close handle to D\f.
    NtClose(hFile);

    // Open D\g for write with D as root directory.
    status = OpenFileWithNtCreateFile(
        &hFile,
        L"fileG.txt",
        hDirectory,
        GENERIC_WRITE,
        FILE_ATTRIBUTE_NORMAL,
        FILE_SHARE_DELETE,
        FILE_CREATE,
        FILE_NON_DIRECTORY_FILE);

    NtClose(hDirectory);
    if (NT_SUCCESS(status))
    {
        NtClose(hFile);
    }

    return (int)RtlNtStatusToDosError(status);
}

int CallDeleteWithoutSharing()
{
    HANDLE hFile1 = CreateFile(
        L"untracked.txt",
        DELETE,
        0,
        NULL,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_RANDOM_ACCESS,
        NULL);

    if (hFile1 == INVALID_HANDLE_VALUE)
    {
        return (int)GetLastError();
    }

    HANDLE hFile2 = CreateFile(
        L"untracked.txt",
        DELETE,
        FILE_SHARE_DELETE,
        NULL,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_RANDOM_ACCESS,
        NULL);

    int lastError = (int)GetLastError();

    if (hFile2 != INVALID_HANDLE_VALUE)
    {
        CloseHandle(hFile2);
    }

    CloseHandle(hFile1);

    return lastError;
}

// ----------------------------------------------------------------------------
// STATIC FUNCTION DEFINITIONS
// ----------------------------------------------------------------------------
static void GenericTests(const string& verb)
{
#define IF_COMMAND1(NAME)   { if (verb == #NAME) { exit(NAME()); } }
#define IF_COMMAND2(NAME)   { if (verb == ("2" ## #NAME)) { NAME(); NAME(); exit(ERROR_SUCCESS); } }

#define IF_COMMAND(NAME)    { IF_COMMAND1(NAME); IF_COMMAND2(NAME); }

    IF_COMMAND(CallDetouredZwCreateFile);
    IF_COMMAND(CallDetouredZwOpenFile);
    IF_COMMAND(CallDetouredSetFileInformationFileLink);
    IF_COMMAND(CallDetouredSetFileInformationFileLinkEx);
    IF_COMMAND(CallDetouredSetFileInformationByHandle);
    IF_COMMAND(CallGetAttributeQuestion);
    IF_COMMAND(CallGetAttributeNonExistent);
    IF_COMMAND(CallGetAttributeNonExistentInDepDirectory);
    IF_COMMAND(CallAccessNetworkDrive);
    IF_COMMAND(CallProbeForDirectory);
    IF_COMMAND(CallFileAttributeOnFileWithPipeChar);
    IF_COMMAND(CallDetouredGetFinalPathNameByHandle);
    IF_COMMAND(CallAccessInvalidFile);
    IF_COMMAND(CallDetouredCreateFileWWithGenericAllAccess);
    IF_COMMAND(CallDetouredCreateFileWForProbingOnly);
    IF_COMMAND(CallDetouredMoveFileExWForRenamingDirectory);
    IF_COMMAND(CallDetouredSetFileInformationByHandleForRenamingDirectory);
    IF_COMMAND(CallDetouredZwSetFileInformationByHandleForRenamingDirectory);
    IF_COMMAND(CallDetouredCreateFileWWrite);
    IF_COMMAND(CallCreateFileWithZeroAccessOnDirectory);
    IF_COMMAND(CallCreateFileOnNtEscapedPath);
    IF_COMMAND(CallOpenFileById);
    IF_COMMAND(CallDeleteWithoutSharing);

#undef IF_COMMAND1
#undef IF_COMMAND2
#undef IF_COMMAND
}

static void SymlinkTests(const string& verb)
{
#define IF_COMMAND1(NAME)   { if (verb == #NAME) { exit(NAME()); } }
#define IF_COMMAND2(NAME)   { if (verb == ("2" ## #NAME)) { NAME(); NAME(); exit(ERROR_SUCCESS); } }

#define IF_COMMAND(NAME)    { IF_COMMAND1(NAME); IF_COMMAND2(NAME); }

    IF_COMMAND(CallDetouredFileCreateWithSymlink);
    IF_COMMAND(CallDetouredFileCreateWithNoSymlink);
    IF_COMMAND(CallCreateSymLinkOnFiles);
    IF_COMMAND(CallCreateSymLinkOnDirectories);
    IF_COMMAND(CallAccessSymLinkOnFiles);
    IF_COMMAND(CallCreateAndDeleteSymLinkOnFiles);
    IF_COMMAND(CallMoveSymLinkOnFilesNotEnforceChainSymLinkAccesses);
    IF_COMMAND(CallAccessSymLinkOnDirectories);
    IF_COMMAND(CallDetouredFileCreateThatAccessesChainOfSymlinks);
    IF_COMMAND(CallDetouredCopyFileFollowingChainOfSymlinks);
    IF_COMMAND(CallDetouredCopyFileNotFollowingChainOfSymlinks);
    IF_COMMAND(CallDetouredNtCreateFileThatAccessesChainOfSymlinks);
    IF_COMMAND(CallAccessNestedSiblingSymLinkOnFiles);
    IF_COMMAND(CallAccessJunctionSymlink_Real);
    IF_COMMAND(CallAccessJunctionSymlink_Junction);
    IF_COMMAND(CallAccessOnChainOfJunctions);
    IF_COMMAND(CallDetouredAccessesCreateSymlinkForQBuild);
    
#undef IF_COMMAND1
#undef IF_COMMAND2
#undef IF_COMMAND
}

static void LoggingTests(const string& verb)
{
#define IF_COMMAND1(NAME)   { if (verb == #NAME) { exit(NAME()); } }
#define IF_COMMAND2(NAME)   { if (verb == ("2" ## #NAME)) { NAME(); NAME(); exit(ERROR_SUCCESS); } }
#define IF_COMMAND(NAME)    { IF_COMMAND1(NAME); IF_COMMAND2(NAME); }

    IF_COMMAND(CreateProcessWLogging);
    IF_COMMAND(CreateProcessWLogging);
    IF_COMMAND(CreateProcessALogging);
    IF_COMMAND(CreateFileWLogging);
    IF_COMMAND(CreateFileALogging);
    IF_COMMAND(GetVolumePathNameWLogging);
    IF_COMMAND(GetFileAttributesALogging);
    IF_COMMAND(GetFileAttributesWLogging);
    IF_COMMAND(GetFileAttributesExWLogging);
    IF_COMMAND(GetFileAttributesExALogging);
    IF_COMMAND(CopyFileWLogging);
    IF_COMMAND(CopyFileALogging);
    IF_COMMAND(CopyFileExWLogging);
    IF_COMMAND(CopyFileExALogging);
    IF_COMMAND(MoveFileWLogging);
    IF_COMMAND(MoveFileALogging);
    IF_COMMAND(MoveFileExWLogging);
    IF_COMMAND(MoveFileExALogging);
    IF_COMMAND(MoveFileWithProgressWLogging);
    IF_COMMAND(MoveFileWithProgressALogging);
    IF_COMMAND(ReplaceFileWLogging);
    IF_COMMAND(ReplaceFileALogging);
    IF_COMMAND(DeleteFileWLogging);
    IF_COMMAND(DeleteFileALogging);
    IF_COMMAND(CreateHardLinkWLogging);
    IF_COMMAND(CreateHardLinkALogging);
    IF_COMMAND(CreateSymbolicLinkWLogging);
    IF_COMMAND(CreateSymbolicLinkALogging);
    IF_COMMAND(FindFirstFileWLogging);
    IF_COMMAND(FindFirstFileALogging);
    IF_COMMAND(FindFirstFileExWLogging);
    IF_COMMAND(FindFirstFileExALogging);
    IF_COMMAND(GetFileInformationByHandleExLogging);
    IF_COMMAND(SetFileInformationByHandleLogging);
    IF_COMMAND(OpenFileMappingWLogging);
    IF_COMMAND(OpenFileMappingALogging);
    IF_COMMAND(GetTempFileNameWLogging);
    IF_COMMAND(GetTempFileNameALogging);
    IF_COMMAND(CreateDirectoryWLogging);
    IF_COMMAND(CreateDirectoryALogging);
    IF_COMMAND(CreateDirectoryExWLogging);
    IF_COMMAND(CreateDirectoryExALogging);
    IF_COMMAND(RemoveDirectoryWLogging);
    IF_COMMAND(RemoveDirectoryALogging);
    IF_COMMAND(DecryptFileWLogging);
    IF_COMMAND(DecryptFileALogging);
    IF_COMMAND(EncryptFileWLogging);
    IF_COMMAND(EncryptFileALogging);
    IF_COMMAND(OpenEncryptedFileRawWLogging);
    IF_COMMAND(OpenEncryptedFileRawALogging);
    IF_COMMAND(OpenFileByIdLogging);
    IF_COMMAND(CallDirectoryEnumerationTest);
    IF_COMMAND(CallPipeTest);
    IF_COMMAND(CallDeleteFileTest);
    IF_COMMAND(CallDeleteDirectoryTest);
    IF_COMMAND(CallDeleteFileStdRemoveTest);
    IF_COMMAND(CallCreateDirectoryTest);

#undef IF_COMMAND1
#undef IF_COMMAND2
#undef IF_COMMAND
}

// ----------------------------------------------------------------------------
// FUNCTION DEFINITIONS
// ----------------------------------------------------------------------------

int main(int argc, char **argv)
{
    if (argc != 2)
    {
        exit(ERROR_WRONG_NUM_ARGS);
    }

    string verb(argv[1]);

#define IF_COMMAND(NAME)   { if (verb == #NAME) { exit(NAME()); } }

    IF_COMMAND(ReadExclusive);
    IF_COMMAND(TimestampsNoNormalize);
    IF_COMMAND(TimestampsNormalize);
    IF_COMMAND(ShortNames);

    LoggingTests(verb);
    SymlinkTests(verb);
    GenericTests(verb);
#undef IF_COMMAND

    exit(ERROR_INVALID_COMMAND);
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

#include "DetoursServices.h"
#include "FileAccessHelpers.h"
#include "PolicyResult.h"
#include "globals.h"

// ----------------------------------------------------------------------------
// Error codes and strings
// ----------------------------------------------------------------------------
#define DETOURS_PIPE_WRITE_ERROR_1                  -43
#define DETOURS_PIPE_WRITE_ERROR_2                  -44
#define DETOURS_PIPE_WRITE_ERROR_3                  -45
#define DETOURS_PIPE_WRITE_ERROR_4                  -46
#define DETOURS_CREATE_PROCESS_ERROR_5              -47
#define DETOURS_SEMAPHOREOPEN_ERROR_6               -48
#define DETOURS_INHERIT_HANDLES_ERROR_7             -49
#define DETOURS_NO_PAYLOAD_FOUND_8                  -50
#define DETOURS_PAYLOAD_PARSE_FAILED_9              -51
#define DETOURS_PAYLOAD_PARSE_FAILED_10             -52
#define DETOURS_PAYLOAD_PARSE_FAILED_11             -53
#define DETOURS_PAYLOAD_PARSE_FAILED_12             -54
#define DETOURS_PAYLOAD_PARSE_FAILED_13             -55
#define DETOURS_PAYLOAD_PARSE_FAILED_14             -56
#define DETOURS_PAYLOAD_PARSE_FAILED_15             -57
#define DETOURS_PAYLOAD_PARSE_FAILED_16             -58
#define DETOURS_PAYLOAD_PARSE_FAILED_17             -59
#define DETOURS_UNICODE_CONVERSION_18               -60
#define DETOURS_PAYLOAD_PARSE_FAILED_19             -61
#define DETOURS_ADD_TO_SILO_ERROR_20                -62
#define DETOURS_CREATE_PROCESS_ATTRIBUTE_LIST_21    -63

#define DETOURS_WINDOWS_LOG_MESSAGE_1  L"DominoDetoursService:1"
#define DETOURS_WINDOWS_LOG_MESSAGE_2  L"DominoDetoursService:2"
#define DETOURS_WINDOWS_LOG_MESSAGE_3  L"DominoDetoursService:3"
#define DETOURS_WINDOWS_LOG_MESSAGE_4  L"DominoDetoursService:4"
#define DETOURS_WINDOWS_LOG_MESSAGE_5  L"DominoDetoursService:5"
#define DETOURS_WINDOWS_LOG_MESSAGE_6  L"DominoDetoursService:6"
#define DETOURS_WINDOWS_LOG_MESSAGE_7  L"DominoDetoursService:7"
#define DETOURS_WINDOWS_LOG_MESSAGE_8  L"DominoDetoursService:8"
#define DETOURS_WINDOWS_LOG_MESSAGE_9  L"DominoDetoursService:9"
#define DETOURS_WINDOWS_LOG_MESSAGE_10 L"DominoDetoursService:10"
#define DETOURS_WINDOWS_LOG_MESSAGE_11 L"DominoDetoursService:11"
#define DETOURS_WINDOWS_LOG_MESSAGE_12 L"DominoDetoursService:12"
#define DETOURS_WINDOWS_LOG_MESSAGE_13 L"DominoDetoursService:13"
#define DETOURS_WINDOWS_LOG_MESSAGE_14 L"DominoDetoursService:14"
#define DETOURS_WINDOWS_LOG_MESSAGE_15 L"DominoDetoursService:15"
#define DETOURS_WINDOWS_LOG_MESSAGE_16 L"DominoDetoursService:16"
#define DETOURS_WINDOWS_LOG_MESSAGE_17 L"DominoDetoursService:17"
#define DETOURS_UNICODE_LOG_MESSAGE_18 L"DominoDetoursService:18"
#define DETOURS_WINDOWS_LOG_MESSAGE_19 L"DominoDetoursService:19"
#define DETOURS_WINDOWS_LOG_MESSAGE_20 L"DominoDetoursService:20"
#define DETOURS_WINDOWS_LOG_MESSAGE_21 L"DominoDetoursService:21"
// ----------------------------------------------------------------------------
// INLINE FUNCTION DEFINITIONS
// ----------------------------------------------------------------------------

inline SpecialProcessKind GetProcessKind() { return g_ProcessKind; }

// ----------------------------------------------------------------------------
// FUNCTION DECLARATIONS
// ----------------------------------------------------------------------------

void HandleDetoursInjectionAndCommunicationErrors(int errorCode, LPCWSTR eventLogMsgPtr, LPCWSTR eventLogMsgId);

// Indicates if the path matches a special-case rule and if so sets a policy to use.
// Note that the given path has been canonicalized so that it does not have a prefix like \\?\, \\.\, or \??\.
bool GetSpecialCaseRulesForCoverageAndSpecialDevices(
    __in  PCWSTR absolutePath,
    __in  size_t absolutePathLength,
    __in PathType pathType,
    __out FileAccessPolicy& policy);

bool GetSpecialCaseRulesForSpecialTools(
    __in  PCWSTR absolutePath,
    __in  size_t absolutePathLength,
    __out FileAccessPolicy& policy);

bool WantsWriteAccess(DWORD access);
bool WantsReadAccess(DWORD access);
bool WantsReadOnlyAccess(DWORD access);
bool WantsProbeOnlyAccess(DWORD access);

bool PathContainsWildcard(LPCWSTR path);

bool ParseUInt64Arg(
    __inout PCWSTR& pos,
    int radix,
    __out ulong& value);

bool LocateFileAccessManifest(
    __out const void*& manifest,
    __out DWORD& manifestSize);

bool ParseFileAccessManifest(
    const void* payload,
    DWORD payloadSize);

bool LocateAndParseFileAccessManifest();

void WriteToInternalErrorsFile(PCWSTR format, ...);

void InitProcessKind();

void TranslateFilePath(_In_ const std::wstring& inFileName, _Out_ std::wstring& outFileName, _In_ bool debug);

void ReportIfNeeded(
    AccessCheckResult const& checkResult, 
    FileOperationContext const& context, 
    PolicyResult const& policyResult, 
    DWORD error, 
    USN usn = -1, 
    wchar_t const* filter = nullptr);

bool EnumerateDirectory(
    const std::wstring& directoryPath,
    const std::wstring& filter,
    bool recursive,
    bool treatReparsePointAsFile,
    _Inout_ std::vector<std::pair<std::wstring, DWORD>>& filesAndDirectories);

bool ExistsAsFile(_In_ PCWSTR path);

class ReportData
{
private:

    AccessCheckResult m_accessCheckResult;
    FileOperationContext m_fileOperationContext;
    PolicyResult m_policyResult;

public:

    ReportData(
        AccessCheckResult const& checkResult,
        FileOperationContext const& context,
        PolicyResult const& policyResult)
        : m_accessCheckResult(checkResult), m_fileOperationContext(context), m_policyResult(policyResult)
    {
    }

    const AccessCheckResult& GetAccessCheckResult() const { return m_accessCheckResult; }
    const FileOperationContext& GetFileOperationContext() const { return m_fileOperationContext; }
    const PolicyResult& GetPolicyResult() const { return m_policyResult; }
};

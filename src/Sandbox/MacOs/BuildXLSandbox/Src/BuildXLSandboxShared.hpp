//
//  BuildXLSandboxShared.hpp
//  DominoSandboxShared
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef BuildXLSandboxshared_hpp
#define BuildXLSandboxshared_hpp

#include <os/log.h>
#include <sys/param.h>

#include "stdafx.h"
#include "DataTypes.h"

#pragma mark Custom data types

const unsigned int kDominoMaxOperationLength = 64;

const unsigned int kProcessNameBufferSize = MAXPATHLEN;

typedef long long pipid_t;

typedef enum
{
    CreateAlways     = CREATE_ALWAYS,
    CreateNew        = CREATE_NEW,
    OpenAlways       = OPEN_ALWAYS,
    OpenExisting     = OPEN_EXISTING,
    TruncateExisting = TRUNCATE_EXISTING,
} CreationDisposition;

typedef enum {
    kDominoSandboxActionSendPipStarted,
    kDominoSandboxActionSendPipProcessTerminated,
    kDominoSandboxActionSendClientAttached,
} DominoSandboxAction;

typedef enum {
    kIpcActionPipStateChanged,
    kIpcActionDebugCheck,
    kIpcActionSetReportQueueSize,
    kIpcActionForceVerboseLogging,
    kIpcActionSetupFailureNotificationHandler,
    kDominoSandboxMethodCount
} IpcAction;

typedef struct {
    pipid_t pipId;
    pid_t processId;
    pid_t clientPid;
    mach_vm_address_t payload;
    mach_vm_size_t payloadLength;
    DominoSandboxAction action;
} IpcData;

typedef enum {
    FileAccessReporting,
} ReportQueueType;

typedef struct {
    DWORD type;
    char operation[kDominoMaxOperationLength];
    pid_t pid;
    pid_t rootPid;
    DWORD requestedAccess;
    DWORD status;
    uint reportExplicitly;
    DWORD error;
    pipid_t pipId;
    DWORD desiredAccess;
    DWORD shareMode;
    DWORD disposition;
    DWORD flagsAndAttributes;
    DWORD pathId;
    char path[MAXPATHLEN];
} AccessReport; // 1152 bytes

#pragma mark Macros and defines

#define kDominoBundleIdentifier "com.microsoft.domino.sandbox"
#define kDominoSandboxClassName "com_microsoft_domino_Sandbox"
#define kDominoSandboxClientClassName "com_microsoft_domino_SandboxClient"

static os_log_t logger = os_log_create(kDominoBundleIdentifier, "Logger");

#define log(format, ...) os_log(logger, "[[ %s ]] %s: " #format "\n", kDominoSandboxClassName, __func__, __VA_ARGS__)
#define log_error(format, ...) os_log_error(logger, "[[ %s ]][ERROR] %s: " #format "\n", kDominoSandboxClassName, __func__, __VA_ARGS__)

#if DEBUG
#define log_debug(format, ...) log(format, __VA_ARGS__)
#define log_verbose(isEnabled, format, ...) log(format, __VA_ARGS__)
#else
#define log_debug(format, ...)
#define log_verbose(isEnabled, format, ...) if (isEnabled) log(format, __VA_ARGS__)
#endif

#define log_error_or_debug(isEnabled, isError, format, ...) \
do {                                             \
    if (isError) log_error(format, __VA_ARGS__); \
    else         log_verbose(isEnabled, format, __VA_ARGS__); \
} while(0)

#endif /* BuildXLSandboxshared_hpp */

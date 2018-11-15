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
#include "Kauth/OpNames.hpp"

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
    kBuildXLSandboxActionSendPipStarted,
    kBuildXLSandboxActionSendPipProcessTerminated,
    kBuildXLSandboxActionSendClientAttached,
} SandboxAction;

typedef enum {
    kIpcActionPipStateChanged,
    kIpcActionDebugCheck,
    kIpcActionSetReportQueueSize,
    kIpcActionForceVerboseLogging,
    kIpcActionSetupFailureNotificationHandler,
    kIpcActionIntrospect,
    kSandboxMethodCount
} IpcAction;

typedef struct {
    pipid_t pipId;
    pid_t processId;
    pid_t clientPid;
    mach_vm_address_t payload;
    mach_vm_size_t payloadLength;
    SandboxAction action;
} PipStateChangedRequest;

#define kMaxReportedPips 50
#define kMaxReportedChildProcesses 20

typedef struct {
    int8_t placeholder;
} IntrospectRequest;

typedef struct {
    pid_t pid;
} ProcessInfo;

typedef struct {
    pid_t pid;
    pid_t clientPid;
    pipid_t pipId;
    uint32_t numCacheHits;
    uint32_t numCacheMisses;
    uint32_t cacheSize;
    int32_t treeSize;
    int8_t numReportedChildren;
    ProcessInfo children[kMaxReportedChildProcesses];
} PipInfo;

typedef struct {
    uint numAttachedClients;
    uint numTrackedProcesses;
    uint numReportedPips;
    PipInfo pips[kMaxReportedPips];
} IntrospectResponse;

typedef enum {
    FileAccessReporting,
} ReportQueueType;

typedef struct {
    uint64_t creationTime;
    uint64_t enqueueTime;
    uint64_t dequeueTime;
} AccessReportStatistics;

typedef struct {
    FileOperation operation;
    pid_t pid;
    pid_t rootPid;
    DWORD requestedAccess;
    DWORD status;
    uint reportExplicitly;
    DWORD error;
    pipid_t pipId;
    char path[MAXPATHLEN];
    AccessReportStatistics stats;
} AccessReport; // 1152 bytes

#pragma mark Macros and defines

#define kBuildXLBundleIdentifier "com.microsoft.domino.sandbox"
#define kBuildXLSandboxClassName "com_microsoft_domino_Sandbox"
#define kBuildXLSandboxClientClassName "com_microsoft_domino_SandboxClient"

extern os_log_t logger;

#define log(format, ...) os_log(logger, "[[ %s ]] %s: " #format "\n", kBuildXLSandboxClassName, __func__, __VA_ARGS__)
#define log_error(format, ...) os_log_error(logger, "[[ %s ]][ERROR] %s: " #format "\n", kBuildXLSandboxClassName, __func__, __VA_ARGS__)

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

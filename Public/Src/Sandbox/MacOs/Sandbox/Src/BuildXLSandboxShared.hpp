// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef BuildXLSandboxshared_hpp
#define BuildXLSandboxshared_hpp

#include <os/log.h>
#include <sys/param.h>

#if MAC_OS_SANDBOX
#include <IOKit/IOLib.h>
#include "SysCtl.hpp"
#endif

#include "stdafx.h"
#include "DataTypes.h"
#include "Kauth/OpNames.hpp"

#pragma mark Custom data types

const unsigned int kBuildXLMaxOperationLength = 64;

const unsigned int kProcessNameBufferSize = MAXPATHLEN;

typedef long long pipid_t;
typedef struct { uint value; } percent;
typedef struct { uint value; } basis_points;
typedef struct { uint value; } megabyte;

typedef enum {
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
    kIpcActionConfigure,
    kIpcActionUpdateResourceUsage,
    kIpcActionSetupFailureNotificationHandler,
    kIpcActionIntrospect,
    kSandboxMethodCount
} IpcAction;

class Timespan
{
private:
    const uint64_t nanos_;

    Timespan(uint64_t nanoseconds) : nanos_(nanoseconds) {}

public:

    uint64_t nanos()  { return nanos_; }
    uint64_t micros() { return nanos() / 1000; }
    uint64_t millis() { return micros() / 1000; }

    static Timespan fromNanoseconds(uint64_t nanoseconds)   { return Timespan(nanoseconds); }
    static Timespan fromMicroseconds(uint64_t microseconds) { return Timespan(microseconds * 1000); }
};

typedef struct Counter {
private:
    uint32_t count_;

public:
    Counter() : count_(0) {}
    Counter(uint32_t cnt) : count_(cnt) {}

    uint32_t count()
    {
        return count_;
    }

    void operator++ (int)
    {
#if MAC_OS_SANDBOX
        if (g_bxl_enable_counters)
            OSIncrementAtomic(&count_);
#else
        ++count_;
#endif
    }

    void operator-- (int)
    {
#if MAC_OS_SANDBOX
        if (g_bxl_enable_counters)
            OSDecrementAtomic(&count_);
#else
        --count_;
#endif
    }
} Counter;

typedef struct DurationCounter {
    uint32_t count_;
    uint64_t durationUs_;

    uint32_t count()    { return count_; }
    Timespan duration() { return Timespan::fromMicroseconds(durationUs_); }

    void operator+= (Timespan timespan)
    {
        AddMicroseconds(timespan.micros());
    }

private:

    void AddMicroseconds(uint64_t durationUs)
    {
#if MAC_OS_SANDBOX
        if (g_bxl_enable_counters)
        {
            OSIncrementAtomic(&count_);
            OSAddAtomic64(durationUs, &durationUs_);
        }
#else
        ++count_;
        durationUs_ += durationUs;
#endif
    }
} DurationCounter;

typedef struct {
    pipid_t pipId;
    pid_t processId;
    pid_t clientPid;
    mach_vm_address_t payload;
    mach_vm_size_t payloadLength;
    SandboxAction action;
} PipStateChangedRequest;

typedef struct {
    basis_points cpuUsage;
    uint availableRamMB;
    uint numTrackedProcesses;
    uint numBlockedProcesses;
} ResourceCounters;

typedef struct {
    Counter totalNumSent;
    Counter numQueued;
    Counter freeListNodeCount;
    double freeListSizeMB;
    Counter numCoalescedReports;
} ReportCounters;

typedef struct {
    DurationCounter findTrackedProcess;
    DurationCounter setLastLookedUpPath;
    DurationCounter checkPolicy;
    DurationCounter cacheLookup;
    DurationCounter getClientInfo;
    DurationCounter reportFileAccess;
    DurationCounter accessHandler;
    ResourceCounters resourceCounters;
    ReportCounters reportCounters;
    Counter numHardLinkRetries;
    Counter numForks;
    Counter numCacheHits;
    Counter numCacheMisses;
    uint numUintTrieNodes;
    uint numPathTrieNodes;
    double uintTrieSizeMB;
    double pathTrieSizeMB;
} AllCounters;

typedef struct {
    percent cpuUsageBlock;
    percent cpuUsageWakeup;
    uint minAvailableRamMB;

    percent GetCpuUsageForWakeup() const
    {
        return cpuUsageWakeup.value > 0 ? cpuUsageWakeup : cpuUsageBlock;
    }
} ResourceThresholds;

typedef struct {
    uint reportQueueSizeMB;
    bool enableReportBatching;
    ResourceThresholds resourceThresholds;
} KextConfig;

#define kMaxReportedPips 30
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
    uint64_t cacheSize;
    int32_t treeSize;
    AllCounters counters;
    int8_t numReportedChildren;
    ProcessInfo children[kMaxReportedChildProcesses];
} PipInfo;

// this struct must not exceed certain size
typedef struct {
    uint numAttachedClients;
    AllCounters counters;
    KextConfig kextConfig;
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
} AccessReport;

inline bool HasAnyFlags(const int source, const int bitMask)
{
    return (source & bitMask) != 0;
}

template<typename T>
inline bool HasAllFlags(const T source, const T bitMask)
{
    return (source & bitMask) == bitMask;
}

#pragma mark Macros and defines

#ifndef BXL_BUNDLE_IDENTIFIER
static_assert(false, "BXL_BUNDLE_IDENTIFIER not defined (shold be something like: com.microsoft.buildxl.sandbox)");
#endif

#ifndef BXL_CLASS_PREFIX
static_assert(false, "BXL_CLASS_PREFIX not defined (shold be something like: com_microsoft_buildxl_)");
#endif

#define CONCAT(prefix, name) prefix ## name
#define XCONCAT(macro, name) CONCAT(macro, name)
#define BXL_CLASS(name)      XCONCAT(BXL_CLASS_PREFIX, name)

#define STR(s) #s
#define XSTR(macro) STR(macro)

#define BuildXLSandbox           BXL_CLASS(Sandbox)
#define kBuildXLSandboxClassName XSTR(BuildXLSandbox)
#define kBuildXLBundleIdentifier XSTR(BXL_BUNDLE_IDENTIFIER)

extern os_log_t logger;

#define log(format, ...) os_log(logger, "[[ %s ]] %s: " #format "\n", kBuildXLSandboxClassName, __func__, __VA_ARGS__)
#define log_error(format, ...) os_log_error(logger, "[[ %s ]][ERROR] %s: " #format "\n", kBuildXLSandboxClassName, __func__, __VA_ARGS__)

#if DEBUG
#define log_debug(format, ...) log(format, __VA_ARGS__)
#else
#define log_debug(format, ...)
#endif

#define log_verbose(isEnabled, format, ...) if (isEnabled) log(format, __VA_ARGS__)

#define log_error_or_debug(isEnabled, isError, format, ...) \
do {                                             \
    if (isError) log_error(format, __VA_ARGS__); \
    else         log_verbose(isEnabled, format, __VA_ARGS__); \
} while(0)

#endif /* BuildXLSandboxshared_hpp */

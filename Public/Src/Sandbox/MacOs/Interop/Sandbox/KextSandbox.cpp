// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <IOKit/kext/KextManager.h>
#include "KextSandbox.h"

class AutoRelease
{
private:
    io_object_t obj_;
public:
    AutoRelease(io_object_t obj) : obj_(obj)
    {}

    ~AutoRelease()
    {
        if (obj_ != IO_OBJECT_NULL)
        {
            IOObjectRelease(obj_);
        }
    }
};

extern "C"
{
#pragma mark Private forward declarations

    bool SendClientAttached(KextConnectionInfo info);

#pragma mark IOKit Service and Connection initialization

    static kern_return_t openMacSanboxIOKitService(io_connect_t *connect)
    {
        io_iterator_t iterator;
        kern_return_t result = IOServiceGetMatchingServices(kIOMasterPortDefault, IOServiceMatching(kBuildXLSandboxClassName), &iterator);
        AutoRelease _i(iterator);

        if (result != KERN_SUCCESS)
        {
            log_error("No matching IOKit service has been found for: %s", kBuildXLSandboxClassName);
            return kIOReturnInvalid;
        }

        io_service_t service = IOIteratorNext(iterator);
        AutoRelease _s(service);

        if (service == IO_OBJECT_NULL)
        {
            log_error("No matching IOKit service has been found for: %s", kBuildXLSandboxClassName);
            return kIOReturnInvalid;
        }

        return IOServiceOpen(service, mach_task_self(), 0, connect);
    }

    void InitializeKextConnection(KextConnectionInfo *info, long infoSize)
    {
        if (sizeof(KextConnectionInfo) != infoSize)
        {
            log_error("Wrong size of the KextConnectionInfo buffer: expected %ld, received %ld",
                      sizeof(KextConnectionInfo), infoSize);
            info->error = KEXT_WRONG_BUFFER_SIZE;
            return;
        }

        io_connect_t connection; // deinitialized in DeinitializeKextConnection
        kern_return_t result = openMacSanboxIOKitService(&connection);
        if (result != KERN_SUCCESS)
        {
            log_error("Failed connecting to service with error code: %#X", result);
            info->error = KEXT_SERVICE_COULD_NOT_OPEN;
            return;
        }

        info->connection = connection;
        info->port = IONotificationPortCreate(kIOMasterPortDefault);
        info->error = 0;

        // We need a dedicated CFRunLoop for the async notification delivery to work, thus we dispatch a block
        // into GCD to keep checking for notification messages from the KEXT
        dispatch_async(dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_DEFAULT, 0), ^()
        {
            CFRunLoopAddSource(CFRunLoopGetCurrent(), IONotificationPortGetRunLoopSource(info->port), kCFRunLoopDefaultMode);
            CFRunLoopRun();
        });
    }

    void InitializeKextSharedMemory(KextSharedMemoryInfo *memoryInfo, long memoryInfoSize, KextConnectionInfo info)
    {
        if (sizeof(KextSharedMemoryInfo) != memoryInfoSize)
        {
            log_error("Wrong size of the KextSharedMemoryInfo buffer: expected %ld, received %ld",
                      sizeof(KextSharedMemoryInfo), memoryInfoSize);
            memoryInfo->error = KEXT_WRONG_BUFFER_SIZE;
            return;
        }

        if (info.connection == IO_OBJECT_NULL)
        {
            memoryInfo->error = KEXT_SERVICE_NOT_FOUND;
            return;
        }

        do
        {
            if (!SendClientAttached(info))
            {
                log_error("%s", "Failed sending BuildXL launch signal to kernel extension");
                memoryInfo->error = KEXT_BUILDXL_LAUNCH_SIGNAL_FAIL;
                continue;
            }

            mach_port_t port = IODataQueueAllocateNotificationPort();
            if (port == MACH_PORT_NULL)
            {
                log_error("%s", "Failed allocating notification port for shared memory region");
                memoryInfo->error = KEXT_MACH_PORT_CREATION_ERROR;
                continue;
            }
            memoryInfo->port = port;

            kern_return_t result = IOConnectSetNotificationPort(info.connection, FileAccessReporting, port, 0);
            if (result != KERN_SUCCESS)
            {
                log_error("%s", "Failed allocating notification port for shared memory region");
                memoryInfo->error = KEXT_NOTIFICATION_PORT_ERROR;
                continue;
            }

            mach_vm_size_t size = 0;
            mach_vm_address_t address = 0;
            result = IOConnectMapMemory(info.connection, FileAccessReporting, mach_task_self(), &address, &size, kIOMapAnywhere);
            if (result != KERN_SUCCESS)
            {
                log_error("%s", "Failed mapping shared memory region");
                memoryInfo->error = KEXT_SHARED_MEMORY_CREATION_ERROR;
                continue;
            }
            memoryInfo->address = address;
        }
        while(false);

        if (memoryInfo->error != KERN_SUCCESS)
        {
            if (MACH_PORT_VALID(memoryInfo->port)) mach_port_destroy(mach_task_self(), memoryInfo->port);
        }
    }

    void DeinitializeKextConnection(KextConnectionInfo info)
    {
        log_debug("%s", "Freeing and closing service connection");

        if (info.port != NULL) IONotificationPortDestroy(info.port);
        if (info.connection != IO_OBJECT_NULL) IOServiceClose(info.connection);
    }

    void DeinitializeKextSharedMemory(KextSharedMemoryInfo memoryInfo, KextConnectionInfo info)
    {
        if (info.connection == IO_OBJECT_NULL)
        {
            return;
        }

        log_debug("%s", "Freeing mapped memory, mach port for shared data queue");
        if (memoryInfo.address != 0)
        {
            IOConnectUnmapMemory(info.connection, FileAccessReporting, memoryInfo.port, memoryInfo.address);
        }

        if (MACH_PORT_VALID(memoryInfo.port))
        {
            mach_port_destroy(mach_task_self(), memoryInfo.port);
        }
    }

#pragma mark Async notification facilities

    bool SetFailureNotificationHandler(FailureNotificationCallback callback, KextConnectionInfo info)
    {
        if (info.connection == IO_OBJECT_NULL)
        {
            return false;
        }

        io_async_ref64_t async;
        async[kIOAsyncCalloutFuncIndex] = (uint64_t)callback;
        async[kIOAsyncCalloutRefconIndex] = (uint64_t)callback;

        mach_port_t port = IONotificationPortGetMachPort(info.port);

        kern_return_t result = IOConnectCallAsyncScalarMethod(info.connection,
                                                              kIpcActionSetupFailureNotificationHandler,
                                                              port,
                                                              async,
                                                              kIOAsyncCalloutCount,
                                                              NULL, 0,
                                                              NULL, NULL);

        return result == KERN_SUCCESS;
    }

#pragma mark Kext versioning

    typedef struct {
        char *versionPtr;
        int length;
    } CFContext;

    void GetCurrentKextVersion(const void* key, const void* value, void* context)
    {
        CFDictionaryRef valueDict = (CFDictionaryRef) value;
        CFStringRef cfBundleVersion = CFStringCreateWithCString(kCFAllocatorDefault, "CFBundleVersion", kCFStringEncodingASCII);
        CFStringRef bundleVersion = (CFStringRef) CFDictionaryGetValue(valueDict, cfBundleVersion);

        CFContext *c = (CFContext *)context;
        CFStringGetCString(bundleVersion, c->versionPtr, c->length, kCFStringEncodingUTF8);
    }

    void KextVersionString(char *version, int size)
    {
        CFStringRef kext_bundle_ids[1];
        kext_bundle_ids[0] = CFSTR(kBuildXLBundleIdentifier);
        CFArrayRef query = CFArrayCreate(kCFAllocatorDefault, (const void **)kext_bundle_ids, 1, &kCFTypeArrayCallBacks);
        CFDictionaryRef kextInfo = KextManagerCopyLoadedKextInfo(query, nullptr);

        CFContext c = { .versionPtr = version, .length = size };
        CFDictionaryApplyFunction(kextInfo, GetCurrentKextVersion, &c);
    }

#pragma mark SendPipStatus functions

    static bool SendPipStatus(const pid_t processId, pipid_t pipId, const char *const payload, int payloadLength,
                              SandboxAction action, KextConnectionInfo info)
    {
        if (info.connection == IO_OBJECT_NULL)
        {
            return false;
        }

        PipStateChangedRequest data =
        {
            .pipId         = pipId,
            .processId     = processId,
            .clientPid     = getpid(),
            .payload       = payload != NULL ? (uintptr_t) payload : 0,
            .payloadLength = (uint64_t) payloadLength,
            .action        = action
        };

        kern_return_t result = IOConnectCallStructMethod(info.connection, kIpcActionPipStateChanged, &data, sizeof(PipStateChangedRequest), NULL, NULL);
        if (result != KERN_SUCCESS)
        {
            log_error("Failed calling SendPipStatus through IPC interface with error code: %#X for action: %d", result, data.action);
            return false;
        }

        log_debug("SendPipStatus succeeded for action: %d", data.action);
        return true;
    }

    bool CheckForDebugMode(bool *isDebugModeEnabled, KextConnectionInfo info)
    {
        if (info.connection == IO_OBJECT_NULL)
        {
            return false;
        }

        uint64_t output;
        uint32_t outputCount = 1;

        kern_return_t result = IOConnectCallScalarMethod(info.connection, kIpcActionDebugCheck, NULL, 0, &output, &outputCount);
        if (result != KERN_SUCCESS)
        {
            log_error("Failed calling CheckForDebugMode through IPC interface with error code: %#X", result);
            return false;
        }

        *isDebugModeEnabled = ((output == 1) ? true : false);
        log_debug("CheckForDebugMode succeeded, got isDebugModeEnabled == %s", *isDebugModeEnabled ? "true" : "false");

        return true;
    }

    bool Configure(KextConfig config, KextConnectionInfo info)
    {
        if (info.connection == IO_OBJECT_NULL)
        {
            return false;
        }

        kern_return_t status = IOConnectCallStructMethod(info.connection, kIpcActionConfigure,
                                                         &config, sizeof(KextConfig), NULL, NULL);
        return status == KERN_SUCCESS;
    }

    bool UpdateCurrentResourceUsage(uint cpuUsageBasisPoints, uint ramUsageBasisPoints, KextConnectionInfo info)
    {
        if (info.connection == IO_OBJECT_NULL)
        {
            return false;
        }

        uint64_t usages[2] = { cpuUsageBasisPoints, ramUsageBasisPoints };
        kern_return_t status = IOConnectCallScalarMethod(info.connection, kIpcActionUpdateResourceUsage,
                                                         usages, 2, NULL, NULL);
        return status == KERN_SUCCESS;
    }

    bool SendClientAttached(KextConnectionInfo info)
    {
        log_debug("Indicating client launching with PID (%d)", getpid());
        return SendPipStatus(getpid(), 0, NULL, 0, kBuildXLSandboxActionSendClientAttached, info);
    }

#pragma mark Monitoring

    bool IntrospectKernelExtension(KextConnectionInfo info, IntrospectResponse *result)
    {
        if (info.connection == IO_OBJECT_NULL)
        {
            return false;
        }

        IntrospectRequest request;
        size_t resultSize = sizeof(IntrospectResponse);
        kern_return_t status = IOConnectCallStructMethod(info.connection, kIpcActionIntrospect,
                                                         &request, sizeof(IntrospectRequest),
                                                         result, &resultSize);
        return status == KERN_SUCCESS;
    }

#pragma mark IOSharedDataQueue consumer code

    /**
     * Call this function once only from a dedicated thread and pass a valid C# delegate callback, the address to
     * the shared memory region and a valid mach port.
     */
    __cdecl void ListenForFileAccessReports(AccessReportCallback callback, long accessReportSize, mach_vm_address_t address, mach_port_t port)
    {
        if (sizeof(AccessReport) != accessReportSize)
        {
            log_error("Wrong size of the AccessReport buffer: expected %ld, received %ld",
                      sizeof(AccessReport), accessReportSize);
            if (callback != NULL) callback(AccessReport{}, KEXT_WRONG_BUFFER_SIZE);
            return;
        }

        if (callback == NULL || address == 0 || !MACH_PORT_VALID(port))
        {
            if (callback != NULL)
            {
                callback(AccessReport{}, REPORT_QUEUE_CONNECTION_ERROR);
            }
            return;
        }

        log_debug("Listening for data on shared queue from process: %d", getpid());

        IODataQueueMemory *queue = (IODataQueueMemory *)address;
        do
        {
            while (IODataQueueDataAvailable(queue))
            {
                AccessReport report;
                uint32_t reportSize = sizeof(report);

                kern_return_t result = IODataQueueDequeue(queue, &report, &reportSize);

                if (result != kIOReturnSuccess)
                {
                    log_error("Received bogus access report: PID(%d) PIP(%#llX) Error Code: %#X", report.rootPid, report.pipId, result);
                    callback(AccessReport{}, REPORT_QUEUE_DEQUEUE_ERROR);
                    return;
                }

                if (reportSize != sizeof(report))
                {
                    log_error("AccessReport size mismatch :: reported: %d, expected: %ld", reportSize, sizeof(report));
                    callback(AccessReport{}, REPORT_QUEUE_DEQUEUE_ERROR);
                    continue;
                }

                report.stats.dequeueTime = GetMachAbsoluteTime();
                callback(report, REPORT_QUEUE_SUCCESS);
            }
        }
        while (IODataQueueWaitForAvailableData(queue, port) == kIOReturnSuccess);

        log_debug("Exiting ListenForFileAccessReports for PID (%d)", getpid());
    }

    uint64_t GetMachAbsoluteTime()
    {
        return mach_absolute_time();
    }
};

bool KEXT_SendPipStarted(const pid_t processId, pipid_t pipId, const char *const famBytes, int famBytesLength, KextConnectionInfo info)
{
    return SendPipStatus(processId, pipId, famBytes, famBytesLength, kBuildXLSandboxActionSendPipStarted, info);
}

bool KEXT_SendPipProcessTerminated(pipid_t pipId, pid_t processId, KextConnectionInfo info)
{
    return SendPipStatus(processId, pipId, NULL, 0, kBuildXLSandboxActionSendPipProcessTerminated, info);
}

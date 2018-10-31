//
//  Sandbox.cpp
//  Interop
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include <IOKit/IOKitLib.h>
#include <IOKit/IODataQueueClient.h>
#include <IOKit/kext/KextManager.h>

#include <signal.h>

#include "Sandbox.h"
#include "StringOperations.h"

extern "C"
{
#pragma mark Private forward declarations

    bool SendClientAttached();

#pragma mark IOKit Service and Connection initialization

    io_service_t findDominoSandboxIOKitService()
    {
        io_iterator_t iterator;

        kern_return_t result = IOServiceGetMatchingServices(kIOMasterPortDefault, IOServiceMatching(kBuildXLSandboxClassName), &iterator);
        if (result != KERN_SUCCESS)
        {
            return IO_OBJECT_NULL;
        }

        io_service_t service;
        if ((service = IOIteratorNext(iterator)) == IO_OBJECT_NULL)
        {
            log_error("No matching IOKit service has been found for: %s", kBuildXLSandboxClassName);
        }
        else
        {
            log_debug("Found DominoSandbox IOKit service at port: %u", service);
        }

        IOObjectRelease(iterator);
        return service;
    }

    kern_return_t openMacSanboxIOKitService(io_service_t service, io_connect_t *connect)
    {
        return IOServiceOpen(service, mach_task_self(), 0, connect);

    }

    static KextConnectionInfoCallback GetKextConnectionInfo = NULL;

    void InitializeKextConnectionInfoCallback(KextConnectionInfoCallback callback)
    {
        if (callback == NULL)
        {
            return;
        }

        GetKextConnectionInfo = callback;
    }

    void InitializeKextConnection(KextConnectionInfo *info)
    {
        if (info == NULL)
        {
            return;
        }

        do
        {
            io_service_t service = findDominoSandboxIOKitService();
            if (service == IO_OBJECT_NULL)
            {
                log_error("%s", "Failed getting BuildXL Sandbox IOService");
                info->error = KEXT_SERVICE_NOT_FOUND;
                continue;
            }

            io_connect_t connection;
            kern_return_t result = openMacSanboxIOKitService(service, &connection);
            if (result != KERN_SUCCESS)
            {
                log_error("Failed connecting to service with error code: %#X", result);
                info->error = KEXT_SERVICE_COULD_NOT_OPEN;
                continue;
            }

            info->connection = connection;
            info->port = IONotificationPortCreate(kIOMasterPortDefault);

            // We need a dedicated CFRunLoop for the async notification delivery to work, thus we dispatch a block
            // into GCD to keep checking for notification messages from the KEXT
            dispatch_async(dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_DEFAULT, 0), ^()
            {
                CFRunLoopAddSource(CFRunLoopGetCurrent(), IONotificationPortGetRunLoopSource(info->port), kCFRunLoopDefaultMode);
                CFRunLoopRun();
            });
        }
        while(false);
    }

    void InitializeKextSharedMemory(KextSharedMemoryInfo *memoryInfo)
    {
        if (memoryInfo == NULL)
        {
            return;
        }

        KextConnectionInfo info = GetKextConnectionInfo();
        if (info.connection == IO_OBJECT_NULL)
        {
            memoryInfo->error = KEXT_SERVICE_NOT_FOUND;
            return;
        }

#if DEBUG
        uint64_t input = 1;
#else
        const char *verboseLoggingEnabled = getenv(DOMINO_VERBOSE_LOG);
        uint64_t input = verboseLoggingEnabled != NULL ? 1 : 0;
#endif
        uint32_t inputCount = 1;

        kern_return_t result = IOConnectCallScalarMethod(info.connection, kIpcActionForceVerboseLogging, &input, inputCount, NULL, NULL);
        if (result != KERN_SUCCESS)
        {
            log_debug("Failed setting verbose logging through IPC interface with error code: %#X", result);
        }

        do
        {
            if (!SendClientAttached())
            {
                log_error("%s", "Failed sending BuildXL launch signal to kernel extension");
                memoryInfo->error = KEXT_DOMINO_LAUNCH_SIGNAL_FAIL;
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

            result = IOConnectSetNotificationPort(info.connection, FileAccessReporting, port, 0);
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

    void DeinitializeKextConnection()
    {
        KextConnectionInfo info = GetKextConnectionInfo();
        if (info.connection == IO_OBJECT_NULL)
        {
            return;
        }

        log_debug("%s", "Freeing and closing service connection");

        if (info.port != NULL) IONotificationPortDestroy(info.port);
        if (info.connection != IO_OBJECT_NULL) IOServiceClose(info.connection);
        GetKextConnectionInfo = NULL;
    }

    void DeinitializeKextSharedMemory(KextSharedMemoryInfo *memoryInfo)
    {
        KextConnectionInfo info = GetKextConnectionInfo();
        if (info.connection == IO_OBJECT_NULL || memoryInfo == NULL)
        {
            return;
        }

        log_debug("%s", "Freeing mapped memory, mach port for shared data queue");
        if (memoryInfo->address != 0) IOConnectUnmapMemory(info.connection, FileAccessReporting, memoryInfo->port, memoryInfo->address);
        if (MACH_PORT_VALID(memoryInfo->port)) mach_port_destroy(mach_task_self(), memoryInfo->port);
    }

#pragma mark Async notification facilities

    bool SetFailureNotificationHandler(FailureNotificationCallback callback)
    {
        KextConnectionInfo info = GetKextConnectionInfo();
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
        char *kextVersion = (char *) calloc(size, sizeof(char));
        CFStringRef kext_bundle_ids[1];
        kext_bundle_ids[0] = CFSTR(kBuildXLBundleIdentifier);
        CFArrayRef query = CFArrayCreate(kCFAllocatorDefault, (const void **)kext_bundle_ids, 1, &kCFTypeArrayCallBacks);
        CFDictionaryRef kextInfo = KextManagerCopyLoadedKextInfo(query, nullptr);

        CFContext c = { .versionPtr = kextVersion, .length = size };
        CFDictionaryApplyFunction(kextInfo, GetCurrentKextVersion, &c);
        strncpy(version, kextVersion, size);
        free(kextVersion);
    }

#pragma mark Exported interop functions

    int NormalizeAndHashPath(BYTE *pPath, BYTE *pBuffer, int nBufferLength)
    {
        return NormalizeAndHashPath((PCPathChar)pPath, pBuffer, nBufferLength);
    }

#pragma mark SendPipStatus functions

    bool SendPipStatus(const pid_t processId, pipid_t pipId, const char *const payload, int payloadLength, DominoSandboxAction action)
    {
        KextConnectionInfo info = GetKextConnectionInfo();
        if (info.connection == IO_OBJECT_NULL)
        {
            return false;
        }

        IpcData data =
        {
            .pipId = pipId,
            .processId = processId,
            .clientPid = getpid(),
            .payload = payload != NULL ? (uintptr_t) payload : 0,
            .payloadLength = (uint64_t) payloadLength,
            .action = action
        };

        kern_return_t result = IOConnectCallMethod(info.connection, kIpcActionPipStateChanged, NULL, 0, &data, sizeof(IpcData),
                                                   NULL, NULL, NULL, NULL);
        if (result != KERN_SUCCESS)
        {
            log_error("Failed calling SendPipStatus through IPC interface with error code: %#X for action: %d", result, data.action);
            return false;
        }

        log_debug("SendPipStatus succeeded for action: %d", data.action);
        return true;
    }

    bool SendPipStarted(const pid_t processId, pipid_t pipId, const char *const famBytes, int famBytesLength)
    {
        return SendPipStatus(processId, pipId, famBytes, famBytesLength, kBuildXLSandboxActionSendPipStarted);
    }

    bool SendPipProcessTerminated(pipid_t pipId, pid_t processId)
    {
        return SendPipStatus(processId, pipId, NULL, 0, kBuildXLSandboxActionSendPipProcessTerminated);
    }

    bool CheckForDebugMode(bool *isDebugModeEnabled)
    {
        KextConnectionInfo info = GetKextConnectionInfo();
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

    bool SetReportQueueSize(uint64_t reportQueueSizeMB)
    {
        KextConnectionInfo info = GetKextConnectionInfo();
        if (info.connection == IO_OBJECT_NULL)
        {
            return false;
        }

        uint32_t inputCount = 1;

        kern_return_t result = IOConnectCallScalarMethod(info.connection, kIpcActionSetReportQueueSize, &reportQueueSizeMB, inputCount, NULL, NULL);
        if (result != KERN_SUCCESS)
        {
            log_debug("Failed setting report queue sized with error: %#X, sandbox kernel extension will fallback to default size.", result);
            return false;
        }

        return true;
    }

    bool SendClientAttached()
    {
        log_debug("Indicating client launching with PID (%d)", getpid());
        return SendPipStatus(getpid(), 0, NULL, 0, kBuildXLSandboxActionSendClientAttached);
    }

#pragma mark IOSharedDataQueue consumer code

    /**
     * Call this function once only from a dedicated thread and pass a valid C# delegate callback, the address to
     * the shared memory region and a valid mach port.
     */
    __cdecl void ListenForFileAccessReports(AccessReportCallback callback, mach_vm_address_t address, mach_port_t port)
    {
        if (callback == NULL || address == 0 || !MACH_PORT_VALID(port))
        {
            callback(AccessReport{}, REPORT_QUEUE_CONNECTION_ERROR);
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

                callback(report, REPORT_QUEUE_SUCCESS);
            }
        }
        while (IODataQueueWaitForAvailableData(queue, port) == kIOReturnSuccess);

        log_debug("Exiting ListenForFileAccessReports for PID (%d)", getpid());
    }
}

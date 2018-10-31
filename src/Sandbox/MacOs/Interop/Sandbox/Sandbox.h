//
//  Sandbox.h
//  Interop
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef Sandbox_h
#define Sandbox_h

#import "BuildXLSandboxShared.hpp"

#define DOMINO_VERBOSE_LOG                        "DOMINO_VERBOSE_LOG"

#define KEXT_SERVICE_NOT_FOUND                    0x1
#define KEXT_SERVICE_COULD_NOT_OPEN               0x2
#define KEXT_MACH_PORT_CREATION_ERROR             0x4
#define KEXT_NOTIFICATION_PORT_ERROR              0x8
#define KEXT_SHARED_MEMORY_CREATION_ERROR         0x10
#define KEXT_DOMINO_LAUNCH_SIGNAL_FAIL            0x20
#define KEXT_DOMINO_CONNECTION_INFO_CALLBACK_FAIL 0x40
#define KEXT_THREAD_ID_ERROR                      0x80

#define REPORT_QUEUE_SUCCESS                      0x1000
#define REPORT_QUEUE_CONNECTION_ERROR             0x1001
#define REPORT_QUEUE_DEQUEUE_ERROR                0x1002

extern "C"
{
    int NormalizeAndHashPath(BYTE *pPath, BYTE *pBuffer, int nBufferLength);

    typedef struct {
        int error;
        io_connect_t connection;
        IONotificationPortRef port;
    } KextConnectionInfo;

    typedef struct {
        int error;
        mach_vm_address_t address;
        mach_port_t port;
    } KextSharedMemoryInfo;

    typedef KextConnectionInfo (__cdecl *KextConnectionInfoCallback)();
    void InitializeKextConnectionInfoCallback(KextConnectionInfoCallback callback);
    void InitializeKextConnection(KextConnectionInfo *info);
    void InitializeKextSharedMemory(KextSharedMemoryInfo *memoryInfo);

    void DeinitializeKextConnection();
    void DeinitializeKextSharedMemory(KextSharedMemoryInfo *memoryInfo);

    bool SendPipStarted(const pid_t processId, pipid_t pipId, const char *const famBytes, int famBytesLength);
    bool SendPipCompleted(pipid_t pipId, pid_t processId);
    bool CheckForDebugMode(bool *isDebugModeEnabled);
    bool SetReportQueueSize(uint64_t reportQueueSizeMB);

    typedef void (__cdecl *FailureNotificationCallback)(void *, IOReturn);
    bool SetFailureNotificationHandler(FailureNotificationCallback callback);

    __cdecl void KextVersionString(char *version, int size);

    typedef void (__cdecl *AccessReportCallback)(AccessReport, int);
    __cdecl void ListenForFileAccessReports(AccessReportCallback callback, mach_vm_address_t address, mach_port_t port);
}

#endif /* sandbox_h */

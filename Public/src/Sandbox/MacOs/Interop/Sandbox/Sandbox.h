//
//  Sandbox.h
//  Interop
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef Sandbox_h
#define Sandbox_h

#import "BuildXLSandboxShared.hpp"

#define BUILDXL_VERBOSE_LOG                       "BUILDXL_VERBOSE_LOG"

#define KEXT_SERVICE_NOT_FOUND                     0x1
#define KEXT_SERVICE_COULD_NOT_OPEN                0x2
#define KEXT_MACH_PORT_CREATION_ERROR              0x4
#define KEXT_NOTIFICATION_PORT_ERROR               0x8
#define KEXT_SHARED_MEMORY_CREATION_ERROR          0x10
#define KEXT_BUILDXL_LAUNCH_SIGNAL_FAIL            0x20
#define KEXT_BUILDXL_CONNECTION_INFO_CALLBACK_FAIL 0x40
#define KEXT_THREAD_ID_ERROR                       0x80

#define REPORT_QUEUE_SUCCESS                      0x1000
#define REPORT_QUEUE_CONNECTION_ERROR             0x1001
#define REPORT_QUEUE_DEQUEUE_ERROR                0x1002

extern "C"
{
    void SetLogger(os_log_t newLogger);
    
    io_service_t findBuildXLSandboxIOKitService();

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

    void InitializeKextConnection(KextConnectionInfo *info);
    void InitializeKextSharedMemory(KextSharedMemoryInfo *memoryInfo, KextConnectionInfo info);

    void DeinitializeKextConnection(KextConnectionInfo info);
    void DeinitializeKextSharedMemory(KextSharedMemoryInfo *memoryInfo, KextConnectionInfo info);

    bool SendPipStarted(const pid_t processId, pipid_t pipId, const char *const famBytes, int famBytesLength, KextConnectionInfo info);
    bool SendPipProcessTerminated(pipid_t pipId, pid_t processId, KextConnectionInfo info);
    bool CheckForDebugMode(bool *isDebugModeEnabled, KextConnectionInfo info);
    bool SetReportQueueSize(uint64_t reportQueueSizeMB, KextConnectionInfo info);

    typedef void (__cdecl *FailureNotificationCallback)(void *, IOReturn);
    bool SetFailureNotificationHandler(FailureNotificationCallback callback, KextConnectionInfo info);

    typedef void (__cdecl *AccessReportCallback)(AccessReport, int);
    __cdecl void ListenForFileAccessReports(AccessReportCallback callback, mach_vm_address_t address, mach_port_t port);

    uint64_t GetMachAbsoluteTime(void);
    __cdecl void KextVersionString(char *version, int size);
    
    bool IntrospectKernelExtension(KextConnectionInfo info, IntrospectResponse *result);
}

#endif /* sandbox_h */

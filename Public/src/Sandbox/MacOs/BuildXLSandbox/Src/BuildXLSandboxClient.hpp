//
//  BuildXLSandboxClient.hpp
//  BuildXLSandboxClient
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef BuildXLSandboxClient_hpp
#define BuildXLSandboxClient_hpp

#include <IOKit/IOService.h>
#include <IOKit/IOUserClient.h>
#include <IOKit/IOSharedDataQueue.h>

#include "BuildXLSandbox.hpp"
#include "BuildXLSandboxShared.hpp"

#define BuildXLSandboxClient com_microsoft_buildXL_SandboxClient

class BuildXLSandboxClient : public IOUserClient
{
    OSDeclareDefaultStructors(BuildXLSandboxClient)

private:

    BuildXLSandbox *sandbox_;
    task_t task_;

public:

    bool initWithTask(task_t owningTask,
                      void *securityToken,
                      UInt32 type) override;

    void stop(IOService *provider) override;
    bool start(IOService *provider) override;

    IOReturn clientClose(void) override;
    IOReturn clientDied(void) override;

    static const IOExternalMethodDispatch ipcMethods[kBuildXLSandboxMethodCount];

    IOReturn registerNotificationPort(mach_port_t port,
                                      UInt32 type,
                                      UInt32 refCon) override;

    IOReturn clientMemoryForType(UInt32 type,
                                 IOOptionBits *options,
                                 IOMemoryDescriptor **memory) override;

protected:

    IOReturn externalMethod(uint32_t selector,
                            IOExternalMethodArguments *arguments,
                            IOExternalMethodDispatch *dispatch,
                            OSObject *target,
                            void *reference) override;

    // MacSanboxClient IPC function pairs for ipcMethods dispatch table

    static IOReturn sPipStateChanged(BuildXLSandboxClient *target,
                                     void *reference,
                                     IOExternalMethodArguments *arguments);

    static IOReturn sDebugCheck(BuildXLSandboxClient *target,
                                void *reference,
                                IOExternalMethodArguments *arguments);

    static IOReturn sSetReportQueueSize(BuildXLSandboxClient *target,
                                        void *reference,
                                        IOExternalMethodArguments *arguments);

    static IOReturn sToggleVerboseLogging(BuildXLSandboxClient *target,
                                void *reference,
                                IOExternalMethodArguments *arguments);

    static IOReturn sSetFailureNotificationHandler(BuildXLSandboxClient *target,
                                                   void *reference,
                                                   IOExternalMethodArguments *arguments);

    IOReturn PipStateChanged(IpcData *data);
    IOReturn ProcessPipStarted(IpcData *data);
    IOReturn ProcessPipTerminated(IpcData *data);
    IOReturn ProcessClientLaunched(IpcData *data);
    IOReturn ProcessClientWillExit(IpcData *data);
    IOReturn SetReportQueueSize(UInt32 reportQueueSize);
    IOReturn ToggleVerboseLogging(bool enabled);
    IOReturn SetFailureNotificationHandler(OSAsyncReference64 ref);

public:
    
    IOReturn SendAsyncResult(OSAsyncReference64 ref, IOReturn result);
};

#endif /* BuildXLSandboxClient_hpp */

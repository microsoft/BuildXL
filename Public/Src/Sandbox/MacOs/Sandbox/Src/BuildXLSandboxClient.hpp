// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef BuildXLSandboxClient_hpp
#define BuildXLSandboxClient_hpp

#include <IOKit/IOService.h>
#include <IOKit/IOUserClient.h>
#include <IOKit/IOSharedDataQueue.h>

#include "BuildXLSandbox.hpp"
#include "BuildXLSandboxShared.hpp"

#define BuildXLSandboxClient BXL_CLASS(SandboxClient)

class BuildXLSandboxClient : public IOUserClient
{
    OSDeclareDefaultStructors(BuildXLSandboxClient)

private:

    BuildXLSandbox *sandbox_;
    task_t task_;
    int detached_;

    static IOExternalMethodDispatch ipcMethods[kSandboxMethodCount];

public:

    bool initWithTask(task_t owningTask,
                      void *securityToken,
                      UInt32 type) override;

    bool start(IOService *provider) override;
    void detach(IOService *provider) override;

    IOReturn clientClose(void) override;
    IOReturn clientDied(void) override;

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

    static IOReturn sPipStateChanged              (BuildXLSandboxClient *target, void *ref, IOExternalMethodArguments *args);
    static IOReturn sDebugCheck                   (BuildXLSandboxClient *target, void *ref, IOExternalMethodArguments *args);
    static IOReturn sConfigure                    (BuildXLSandboxClient *target, void *ref, IOExternalMethodArguments *args);
    static IOReturn sUpdateResourceUsage          (BuildXLSandboxClient *target, void *ref, IOExternalMethodArguments *args);
    static IOReturn sSetFailureNotificationHandler(BuildXLSandboxClient *target, void *ref, IOExternalMethodArguments *args);
    static IOReturn sIntrospectHandler            (BuildXLSandboxClient *target, void *ref, IOExternalMethodArguments *args);

    IOReturn PipStateChanged(PipStateChangedRequest *data);
    IOReturn ProcessPipStarted(PipStateChangedRequest *data);
    IOReturn ProcessPipTerminated(PipStateChangedRequest *data);
    IOReturn ProcessClientLaunched(PipStateChangedRequest *data);
    IOReturn SetFailureNotificationHandler(OSAsyncReference64 ref);

public:

    IOReturn SendAsyncResult(OSAsyncReference64 ref, IOReturn result);
};

#endif /* BuildXLSandboxClient_hpp */

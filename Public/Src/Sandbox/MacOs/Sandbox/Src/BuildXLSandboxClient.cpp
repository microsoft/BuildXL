// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <IOKit/IOLib.h>

#include "AccessHandler.hpp"
#include "Buffer.hpp"
#include "TrustedBsdHandler.hpp"
#include "BuildXLSandboxClient.hpp"
#include "SandboxedPip.hpp"

#define LogVerbose(format, ...) log_verbose(g_bxl_verbose_logging, format, __VA_ARGS__)

#define super IOUserClient

OSDefineMetaClassAndStructors(BuildXLSandboxClient, IOUserClient)

#pragma mark BuildXLSandbox client life-cycle

#define kNotDetached 0
#define kDetached    1

bool BuildXLSandboxClient::initWithTask(task_t owningTask,
                                    void *securityToken,
                                    UInt32 type)
{
    task_     = owningTask;
    sandbox_  = nullptr;
    detached_ = kNotDetached;

    return super::initWithTask(owningTask, securityToken, type);
}

// Called in response to 'IOServiceOpen' from user space
bool BuildXLSandboxClient::start(IOService *provider)
{
    // Verify that the provider is the BuildXLSandbox, otherwise fail!
    sandbox_ = OSDynamicCast(BuildXLSandbox, provider);
    if (sandbox_ == nullptr)
    {
        return false;
    }

    return super::start(provider);
}

// IMPORTANT: not called implicitly when the client exits, which is why we call
//            this explicitly upon both 'clientClose' and 'clientDied'.
//
// When clients are not explicitly detached when they exit, they stay
// registered with the service until the service is closed; after the
// number of registered clients for a service exceeds 1021 the service
// stops accepting new clients with error message "stalling for detach from <name>"
void BuildXLSandboxClient::detach(IOService *provider)
{
    if (OSCompareAndSwap(kNotDetached, kDetached, &detached_))
    {
        pid_t clientPid = proc_selfpid();
        LogVerbose("Releasing resources for client PID(%d)", clientPid);
        sandbox_->DeallocateClient(clientPid);
        super::detach(provider);
    }
}

// Called in response to 'IOServiceClose' from user space
IOReturn BuildXLSandboxClient::clientClose(void)
{
    detach(sandbox_);
    return super::clientClose();
}

// Always called as soon as the user-space client ceases to exist
IOReturn BuildXLSandboxClient::clientDied(void)
{
    detach(sandbox_);
    return super::clientDied();
}

#pragma mark Fetching memory and data queue notifications

IOReturn BuildXLSandboxClient::registerNotificationPort(mach_port_t port, UInt32 type, UInt32 ref)
{
    if (port == MACH_PORT_NULL)
    {
        return kIOReturnError;
    }

    // Extend this to add additional shared data queues later, e.g. logging
    switch(type)
    {
        case FileAccessReporting:
        {
            pid_t pid = proc_selfpid();
            IOReturn result = sandbox_->SetReportQueueNotificationPort(port, pid);
            if (result != kIOReturnSuccess)
            {
                log_error("%s", "Failed setting the notifacation port!");
                return result;
            }

            LogVerbose("Registered port for pid (%d)", pid);
            break;
        }
        default:
            return kIOReturnBadArgument;
    }

    return kIOReturnSuccess;
}

// Called in response to IOConnectMapMemory from user space
IOReturn BuildXLSandboxClient::clientMemoryForType(UInt32 type, IOOptionBits *options, IOMemoryDescriptor **memory)
{
    switch (type)
    {
        case FileAccessReporting:
        {
            pid_t pid = proc_selfpid();
            *options = 0;
            // NOTE: GetReportQueueMemoryDescriptor allocates a new object that must be released;
            //       here we are assigning that value to '*memory' which is as an "out argument",
            //       so the caller is responsible for releasing it.  Concretely, the caller is
            //       the super class IOUserClient, which indeed releases this object appropriately.
            *memory = sandbox_->GetReportQueueMemoryDescriptor(pid);
            if (*memory == nullptr)
            {
                log_error("%s", "Descriptor creation failed!");
                return kIOReturnVMError;
            }

            LogVerbose("Descriptor set for pid (%d)", pid);
            return kIOReturnSuccess;
        }
        default:
            return kIOReturnBadArgument;
    }
}

#pragma mark IPC implementation

IOExternalMethodDispatch BuildXLSandboxClient::ipcMethods[kSandboxMethodCount] =
{
    // kIpcActionPipStateChanged
    {
        .function                 = (IOExternalMethodAction) &BuildXLSandboxClient::sPipStateChanged,
        .checkScalarInputCount    = 0,
        .checkStructureInputSize  = sizeof(PipStateChangedRequest),
        .checkScalarOutputCount   = 0,
        .checkStructureOutputSize = 0
    },
    // kIpcActionDebugCheck
    {
        .function                 = (IOExternalMethodAction) &BuildXLSandboxClient::sDebugCheck,
        .checkScalarInputCount    = 0,
        .checkStructureInputSize  = 0,
        .checkScalarOutputCount   = 1,
        .checkStructureOutputSize = 0
    },
    // kIpcActionConfigure
    {
        .function                 = (IOExternalMethodAction) &BuildXLSandboxClient::sConfigure,
        .checkScalarInputCount    = 0,
        .checkStructureInputSize  = sizeof(KextConfig),
        .checkScalarOutputCount   = 0,
        .checkStructureOutputSize = 0
    },
    // kIpcActionUpdateResourceUsage
    {
        .function                 = (IOExternalMethodAction) &BuildXLSandboxClient::sUpdateResourceUsage,
        .checkScalarInputCount    = 2,
        .checkStructureInputSize  = 0,
        .checkScalarOutputCount   = 0,
        .checkStructureOutputSize = 0
    },
    // kIpcActionSetupFailureNotificationHandler
    {
        .function                 = (IOExternalMethodAction) &BuildXLSandboxClient::sSetFailureNotificationHandler,
        .checkScalarInputCount    = 0,
        .checkStructureInputSize  = 0,
        .checkScalarOutputCount   = 0,
        .checkStructureOutputSize = 0
    },
    // kIpcActionIntrospect
    {
        .function                 = (IOExternalMethodAction) &BuildXLSandboxClient::sIntrospectHandler,
        .checkScalarInputCount    = 0,
        .checkStructureInputSize  = sizeof(IntrospectRequest),
        .checkScalarOutputCount   = 0,
        .checkStructureOutputSize = sizeof(IntrospectResponse)
    },
};

IOReturn BuildXLSandboxClient::externalMethod(uint32_t selector, IOExternalMethodArguments *arguments,
                                          IOExternalMethodDispatch *dispatch,
                                          OSObject *target,
                                          void *reference)
{
    if (selector < (uint32_t) kSandboxMethodCount)
    {
        return super::externalMethod(selector, arguments, &ipcMethods[selector], this, reference);
    }
    else
    {
        return super::externalMethod(selector, arguments, dispatch, target, reference);
    }
}

IOReturn BuildXLSandboxClient::sDebugCheck(BuildXLSandboxClient *target, void *reference, IOExternalMethodArguments *arguments)
{
    // This method is defined to only allow for one scalar output in its IPC interface, so it's safe to
    // derefernce it as an array with one element and ingore scalarOutputCount for bounds checking.
    uint64_t *debugModeEnabled = &arguments->scalarOutput[0];

#if DEBUG
    *debugModeEnabled = 1;
#else
    *debugModeEnabled = 0;
#endif

    return kIOReturnSuccess;
}

IOReturn BuildXLSandboxClient::sConfigure(BuildXLSandboxClient *target, void *reference, IOExternalMethodArguments *arguments)
{
    KextConfig *config = (KextConfig *) arguments->structureInput;
    target->sandbox_->Configure(config);
    return kIOReturnSuccess;
}

IOReturn BuildXLSandboxClient::sUpdateResourceUsage(BuildXLSandboxClient *target, void *reference, IOExternalMethodArguments *arguments)
{
    target->sandbox_->ResourceManger()->UpdateCpuUsage({ .value = (uint)arguments->scalarInput[0] });
    target->sandbox_->ResourceManger()->UpdateAvailableRam((uint)arguments->scalarInput[1]);
    return kIOReturnSuccess;
}

IOReturn BuildXLSandboxClient::sSetFailureNotificationHandler(BuildXLSandboxClient *target, void *reference, IOExternalMethodArguments *arguments)
{
    return target->SetFailureNotificationHandler(arguments->asyncReference);
}

IOReturn BuildXLSandboxClient::sIntrospectHandler(BuildXLSandboxClient *target, void *ref, IOExternalMethodArguments *args)
{
    IOMemoryDescriptor *outMemDesc = args->structureOutputDescriptor;

    IOReturn prepared = outMemDesc->prepare();
    if (prepared != kIOReturnSuccess)
    {
        return kIOReturnNoMemory;
    }

    IntrospectResponse result = target->sandbox_->Introspect();
    IOByteCount bytesWritten = outMemDesc->writeBytes(0, &result, sizeof(result));

    outMemDesc->complete();

    return bytesWritten == sizeof(result) ? kIOReturnSuccess : kIOReturnError;
}

IOReturn BuildXLSandboxClient::sPipStateChanged(BuildXLSandboxClient *target, void *reference, IOExternalMethodArguments *arguments)
{
    return target->PipStateChanged((PipStateChangedRequest *)arguments->structureInput);
}

IOReturn BuildXLSandboxClient::PipStateChanged(PipStateChangedRequest *data)
{
    if (data == nullptr)
    {
        return kIOReturnBadArgument;
    }

    IOReturn error = kIOReturnSuccess;

    switch (data->action)
    {
        case kBuildXLSandboxActionSendPipStarted:
        {
            error = ProcessPipStarted(data);
            break;
        }
        case kBuildXLSandboxActionSendPipProcessTerminated:
        {
            error = ProcessPipTerminated(data);
            break;
        }
        case kBuildXLSandboxActionSendClientAttached:
        {
            error = ProcessClientLaunched(data);
            break;
        }
        default:
            error = kIOReturnBadArgument;
    }

    return error;
}

IOReturn BuildXLSandboxClient::ProcessPipStarted(PipStateChangedRequest *data)
{
    mach_vm_address_t clientAddr = data->payload;
    mach_vm_size_t size = data->payloadLength;

    // Allocate buffer for storing the pip payload
    Buffer *ioBuffer = Buffer::create(size);
    AutoRelease _b(ioBuffer);
    if (ioBuffer == nullptr)
    {
        log_error("%s", "Failed to allocate IOBuffer for storing the pip payload");
        return kIOReturnNoMemory;
    }

    // Create memory descriptor
    IOMemoryDescriptor *memDesc = IOMemoryDescriptor::withAddressRange(clientAddr, size, kIODirectionOutIn, task_);
    AutoRelease _m(memDesc);
    if (!memDesc)
    {
        log_error("%s", "IOMemoryDescriptor::withAddressRange failed");
        return kIOReturnVMError;
    }

    // Prepare the descriptor for reading. Must call complete() if prepare() succeeds (we do it right after readBytes).
    IOReturn status = memDesc->prepare(kIODirectionOutIn);
    if (status != kIOReturnSuccess)
    {
        log_error("IOMemoryDescriptor::prepare failed, returning %#x", status);
        return status;
    }

    // Copy the bytes over
    IOByteCount bytesRead = memDesc->readBytes(0, ioBuffer->getBytes(), size);
    memDesc->complete();
    if (bytesRead != size)
    {
        log_error("Couldn't read %lld bytes from memory descriptor; bytes read: %lld", size, bytesRead);
        return kIOReturnVMError;
    }

    // Create a SandboxedPip
    SandboxedPip *pip = SandboxedPip::create(data->clientPid, data->processId, ioBuffer);
    AutoRelease _p(pip);
    if (pip == nullptr)
    {
        log_error("%s", "Could not create SandboxedPip (either FAM is invalid or we're out of memory)");
        return kIOReturnInvalid;
    }

    bool success = sandbox_->TrackRootProcess(pip);

    log_error_or_debug(g_bxl_verbose_logging, !success,
                       "Tracking root process %d for pip '%llX' and ClientPID(%d): %s",
                       pip->getProcessId(), pip->getPipId(), pip->getClientPid(), success ? "succeeded" : "failed");

    return success ? kIOReturnSuccess : kIOReturnError;
}

IOReturn BuildXLSandboxClient::ProcessPipTerminated(PipStateChangedRequest *data)
{
    pid_t pid = data->processId;
    pipid_t pipId = data->pipId;
    LogVerbose("Pip with PipId = %#llX, PID = %d terminated", pipId, pid);
    TrustedBsdHandler handler = TrustedBsdHandler(sandbox_);
    if (handler.TryInitializeWithTrackedProcess(pid) && handler.GetPipId() == pipId)
    {
#if DEBUG
        char name[kProcessNameBufferSize];
        proc_name(data->processId, name, sizeof(name));
        log_debug("Killing process %s(%d)", name, pid);
#endif
        handler.HandleProcessUntracked(pid);
        proc_signal(pid, SIGTERM);
    }

    return kIOReturnSuccess;
}

IOReturn BuildXLSandboxClient::ProcessClientLaunched(PipStateChangedRequest *data)
{
#if DEBUG
    char name[kProcessNameBufferSize];
    proc_name(data->processId, name, sizeof(name));
    LogVerbose("Client (%s) launched with PID(%d)", name, data->processId);
#endif
    return sandbox_->AllocateNewClient(data->processId);
}

IOReturn BuildXLSandboxClient::SetFailureNotificationHandler(OSAsyncReference64 ref)
{
    sandbox_->SetFailureNotificationHandlerForClientPid(proc_selfpid(), ref, this);
    return kIOReturnSuccess;
}

IOReturn BuildXLSandboxClient::SendAsyncResult(OSAsyncReference64 ref, IOReturn result)
{
    // We can extend this method and the actual call to pass along more context if needed later
    return sendAsyncResult64(ref, result, NULL, 0);
}

#undef super

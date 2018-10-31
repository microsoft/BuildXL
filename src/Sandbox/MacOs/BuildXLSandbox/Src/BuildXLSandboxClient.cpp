//
//  BuildXLSandboxClient.cpp
//  DominoSandboxClient
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include <IOKit/IOLib.h>

#include "AccessHandler.hpp"
#include "BuildXLSandboxClient.hpp"
#include "ProcessObject.hpp"

#define super IOUserClient

OSDefineMetaClassAndStructors(DominoSandboxClient, IOUserClient)

#pragma mark DominoSandbox client life-cycle

bool DominoSandboxClient::initWithTask(task_t owningTask,
                                    void *securityToken,
                                    UInt32 type)
{
    bool success = super::initWithTask(owningTask, securityToken, type) ?: false;
    sandbox_ = nullptr;
    task_ = owningTask;

    return success;
}

bool DominoSandboxClient::start(IOService *provider)
{
    // Verify that the provider is the DominoSandbox, otherwise fail!
    sandbox_ = OSDynamicCast(DominoSandbox, provider);
    bool success = (sandbox_ != NULL);
    if (success)
    {
        success = super::start(provider);
    }

    return success;
}

void DominoSandboxClient::stop(IOService *provider)
{
    super::stop(provider);
}

IOReturn DominoSandboxClient::clientClose(void)
{
    return kIOReturnSuccess;
}

IOReturn DominoSandboxClient::clientDied(void)
{
    // Always called as soon as the user-space client ceases to exist
    log_verbose(sandbox_->verboseLoggingEnabled, "%s", "Releasing resources...");
    sandbox_->FreeReportQueuesForClientProcess(proc_selfpid());
    return super::clientDied();
}

#pragma mark Fetching memory and data queue notifications

IOReturn DominoSandboxClient::registerNotificationPort(mach_port_t port, UInt32 type, UInt32 ref)
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

            log_verbose(sandbox_->verboseLoggingEnabled, "Registered port for pid (%d)", pid);
            break;
        }
        default:
            return kIOReturnBadArgument;
    }

    return kIOReturnSuccess;
}

IOReturn DominoSandboxClient::clientMemoryForType(UInt32 type, IOOptionBits *options, IOMemoryDescriptor **memory)
{
    switch (type)
    {
        case FileAccessReporting:
        {
            pid_t pid = proc_selfpid();
            *options = 0;
            *memory = sandbox_->GetReportQueueMemoryDescriptor(pid);
            if (*memory == nullptr)
            {
                log_error("%s", "Descriptor creation failed!");
                return kIOReturnVMError;
            }

            log_verbose(sandbox_->verboseLoggingEnabled, "Descriptor set for pid (%d)", pid);
            break;
        }
        default:
            return kIOReturnBadArgument;
    }

    // We retain the memory descriptor for every consumer, the release happens when the consumer exits
    // and can be found in Sandbox.cpp
    (*memory)->retain();

    return kIOReturnSuccess;
}

#pragma mark IPC implementation

const IOExternalMethodDispatch DominoSandboxClient::ipcMethods[kBuildXLSandboxMethodCount] =
{
    // kIpcActionPipStateChanged
    {
        (IOExternalMethodAction) &DominoSandboxClient::sPipStateChanged,
        0,
        sizeof(IpcData),
        0,
        0
    },
    // kIpcActionDebugCheck
    {
        (IOExternalMethodAction) &DominoSandboxClient::sDebugCheck,
        0,
        0,
        1,
        0
    },
    // kIpcActionSetReportQueueSize
    {
        (IOExternalMethodAction) &DominoSandboxClient::sSetReportQueueSize,
        1,
        0,
        0,
        0
    },
    // kIpcActionForceVerboseLogging
    {
        (IOExternalMethodAction) &DominoSandboxClient::sToggleVerboseLogging,
        1,
        0,
        0,
        0
    },
    // kIpcActionSetupFailureNotificationHandler
    {
        (IOExternalMethodAction) &DominoSandboxClient::sSetFailureNotificationHandler,
        0,
        0,
        0,
        0
    },
};

IOReturn DominoSandboxClient::externalMethod(uint32_t selector, IOExternalMethodArguments *arguments,
                                          IOExternalMethodDispatch *dispatch,
                                          OSObject *target,
                                          void *reference)
{
    if (selector < (uint32_t) kBuildXLSandboxMethodCount)
    {
        dispatch = (IOExternalMethodDispatch *) &ipcMethods[selector];

        if (!target)
        {
            target = this;
        }
    }

    return super::externalMethod(selector, arguments, dispatch, target, reference);
}

IOReturn DominoSandboxClient::sDebugCheck(DominoSandboxClient *target, void *reference, IOExternalMethodArguments *arguments)
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

IOReturn DominoSandboxClient::sSetReportQueueSize(DominoSandboxClient *target, void *reference, IOExternalMethodArguments *arguments)
{
    const uint64_t *reportQueueSize = &arguments->scalarInput[0];
    return target->SetReportQueueSize((uint32_t)*reportQueueSize);
}

IOReturn DominoSandboxClient::sToggleVerboseLogging(DominoSandboxClient *target, void *reference, IOExternalMethodArguments *arguments)
{
    const uint64_t *enabled = &arguments->scalarInput[0];
    return target->ToggleVerboseLogging((*enabled) == 1);
}

IOReturn DominoSandboxClient::sSetFailureNotificationHandler(DominoSandboxClient *target, void *reference, IOExternalMethodArguments *arguments)
{
    return target->SetFailureNotificationHandler(arguments->asyncReference);
}

IOReturn DominoSandboxClient::sPipStateChanged(DominoSandboxClient *target, void *reference, IOExternalMethodArguments *arguments)
{
    return target->PipStateChanged((IpcData *)arguments->structureInput);
}

IOReturn DominoSandboxClient::PipStateChanged(IpcData *data)
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

IOReturn DominoSandboxClient::ProcessPipStarted(IpcData *data)
{
    IOReturn status = kIOReturnSuccess;

    mach_vm_address_t clientAddr = data->payload;
    mach_vm_size_t size = data->payloadLength;

    IOMemoryDescriptor *memDesc = nullptr;
    IOMemoryMap *memMap = nullptr;
    bool memPrepared = false;
    ProcessObject *process = nullptr;
    do
    {
        memDesc = IOMemoryDescriptor::withAddressRange(clientAddr, size, kIODirectionNone, task_);
        if (!memDesc)
        {
            status = kIOReturnVMError;
            log_error("IOMemoryDescriptor::withAddressRange failed, returning %#x", status);
            continue;
        }

        status = memDesc->prepare(kIODirectionOutIn);
        if (status != kIOReturnSuccess)
        {
            log_error("IOMemoryDescriptor::prepare failed, returning %#x", status);
            continue;
        }

        memPrepared = true;
        memMap = memDesc->map();
        if (!memMap)
        {
            status = kIOReturnVMError;
            log_error("IOMemoryDescriptor::map failed, returning %#x", status);
            continue;
        }

        // 'kernelBuffer' is either released by ProcessObject (when it gets destroyed) or immedialty on failure below
        // TODO: consider wrapping kernelBuffer in some kind of OSObject to ensure proper reference counting
        char *kernelBuffer = IONew(char, size);
        if (kernelBuffer == nullptr)
        {
            status = kIOReturnVMError;
            log_error("Failed allocating buffer for pip payload, returning %#x", status);
            continue;
        }

        // Vet this code later, probably we have to make it more secure besides __builtin___memcpy_chk.
        memcpy(kernelBuffer, (char *)memMap->getVirtualAddress(), size);

        int pid = (int)data->processId;

        // If 'process' is successfully created, it becomes responsible for freeing 'kernelBuffer'
        // (so we don't worry about it); otherwise, we have to release 'kernelBuffer' here.
        if (!(process = ProcessObject::withPayload(data->clientPid, pid, kernelBuffer, (uint)size)))
        {
            IODelete(kernelBuffer, char, size);
            status = kIOReturnNoMemory;
            log_error("IODelete failed, returning %#x", status);
            continue;
        }

        if (!sandbox_->TrackRootProcess(process))
        {
            status = kIOReturnNoMemory;
            log_error("Tracking root process failed, returning %#x", status);
            continue;
        }
    } while(false);

    if (process && status == kIOReturnSuccess)
    {
        log_verbose(sandbox_->verboseLoggingEnabled, "Registered ProcessObject (PID = %d) for pip %llX and ClientPID(%d)",
                    process->getProcessId(), process->getPipId(), process->getClientPid());
    }

    // Release on IOMemoryMap calls unmap() explicitly
    OSSafeReleaseNULL(memMap);

    // Must not call 'complete' on memDesc unless 'prepare' succeeded
    if (memDesc && memPrepared) memDesc->complete( kIODirectionOutIn );

    // Done with the I/O now.
    OSSafeReleaseNULL(memDesc);

    // Done with process
    OSSafeReleaseNULL(process);

    return status;
}

IOReturn DominoSandboxClient::ProcessPipTerminated(IpcData *data)
{
    pid_t pid = data->processId;
    pipid_t pipId = data->pipId;
    log_verbose(sandbox_->verboseLoggingEnabled, "Pip with PipId = %#llX, PID = %d terminated", pipId, pid);
    if (sandbox_->UntrackProcess(pid, pipId))
    {
#if DEBUG
        char name[kProcessNameBufferSize];
        proc_name(data->processId, name, sizeof(name));
        log_debug("Killing process %s(%d)", name, pid);
#endif
        proc_signal(pid, SIGTERM);
    }

    return kIOReturnSuccess;
}

IOReturn DominoSandboxClient::ProcessClientLaunched(IpcData *data)
{
#if DEBUG
    char name[kProcessNameBufferSize];
    proc_name(data->processId, name, sizeof(name));
    log_verbose(sandbox_->verboseLoggingEnabled, "Client (%s) launched with PID(%d)", name, data->processId);
#endif
    return sandbox_->AllocateReportQueueForClientProcess(data->processId);
}

IOReturn DominoSandboxClient::SetReportQueueSize(UInt32 reportQueueSize)
{
    sandbox_->SetReportQueueSize(reportQueueSize);
    return kIOReturnSuccess;
}

IOReturn DominoSandboxClient::ToggleVerboseLogging(bool enabled)
{
    sandbox_->verboseLoggingEnabled = enabled;
    return kIOReturnSuccess;
}

IOReturn DominoSandboxClient::SetFailureNotificationHandler(OSAsyncReference64 ref)
{
    sandbox_->SetFailureNotificationHandlerForClientPid(proc_selfpid(), ref, this);
    return kIOReturnSuccess;
}

IOReturn DominoSandboxClient::SendAsyncResult(OSAsyncReference64 ref, IOReturn result)
{
    // We can extend this method and the actual call to pass along more context if needed later
    return sendAsyncResult64(ref, result, NULL, 0);
}

#undef super

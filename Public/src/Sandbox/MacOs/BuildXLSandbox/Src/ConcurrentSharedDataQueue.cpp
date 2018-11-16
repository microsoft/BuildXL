//
//  ConcurrentSharedDataQueue.cpp
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include <IOKit/IOMemoryDescriptor.h>
#include <IOKit/IODataQueueShared.h>
#include "BuildXLSandboxClient.hpp"
#include "ConcurrentSharedDataQueue.hpp"

#define super OSObject

OSDefineMetaClassAndStructors(ConcurrentSharedDataQueue, OSObject)

ConcurrentSharedDataQueue* ConcurrentSharedDataQueue::withEntries(UInt32 numEntries, UInt32 entrySize)
{
    auto *instance = new ConcurrentSharedDataQueue;
    if (instance)
    {
        bool initialized = instance->init(numEntries, entrySize);
        if (!initialized)
        {
            instance->release();
            instance = nullptr;
        }
    }
    
    if (!instance)
    {
        log_error("Failed to initialize shared data queue with %d entries of size %d", numEntries, entrySize);
    }
    
    return instance;
}

bool ConcurrentSharedDataQueue::init(UInt32 numEntries, UInt32 entrySize)
{
    if (!super::init())
    {
        return false;
    }

    lock_ = IORecursiveLockAlloc();
    queue_ = IOSharedDataQueue::withCapacity((entrySize + DATA_QUEUE_ENTRY_HEADER_SIZE) * numEntries);

    // These resources are released on the client side
    port_ = MACH_PORT_NULL;
    descriptor_ = nullptr;
    async_ = nullptr;

    return lock_ && queue_;
}

void ConcurrentSharedDataQueue::free()
{
    OSSafeReleaseNULL(queue_);
    if (descriptor_ != nullptr)
    {
        descriptor_->complete();
        OSSafeReleaseNULL(descriptor_);
    }

    if (async_ != nullptr)
    {
        async_->userClient = nullptr;
        IODelete(async_, ClientAsyncHandle, 1);
    }

    if (lock_)
    {
        IORecursiveLockFree(lock_);
        lock_ = nullptr;
    }
    
    super::free();
}

bool ConcurrentSharedDataQueue::enqueue(void *data, UInt32 dataSize)
{
    EnterMonitor
    return queue_->enqueue(data, dataSize);
}

void ConcurrentSharedDataQueue::setNotificationPort(mach_port_t port)
{
    EnterMonitor
    port_ = port;
    queue_->setNotificationPort(port);
}

bool ConcurrentSharedDataQueue::isNotificationPortValid()
{
    EnterMonitor
    return IPC_PORT_VALID(port_);
}

IOMemoryDescriptor* ConcurrentSharedDataQueue::getMemoryDescriptor()
{
    EnterMonitor
    descriptor_ = queue_->getMemoryDescriptor();
    return descriptor_;
}

bool ConcurrentSharedDataQueue::isDescriptorValid()
{
    EnterMonitor
    return descriptor_ != nullptr;
}

void ConcurrentSharedDataQueue::setClientAsyncHandle(OSAsyncReference64 ref, OSObject* client)
{
    EnterMonitor

    async_ = IONew(ClientAsyncHandle, 1);
    if (async_ != nullptr)
    {
        bcopy(ref, async_->ref, sizeof(OSAsyncReference64));
        async_->userClient = client;
    }
}

IOReturn ConcurrentSharedDataQueue::InvokeAsyncHandle(IOReturn status)
{
    EnterMonitor
    
    if (async_ != nullptr)
    {
        DominoSandboxClient *client = OSDynamicCast(DominoSandboxClient, async_->userClient);
        return client->SendAsyncResult(async_->ref, status);
    }

    return kIOReturnError;
}

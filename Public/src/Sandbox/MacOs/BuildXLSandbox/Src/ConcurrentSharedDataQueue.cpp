// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
    asyncFailureHandle_ = nullptr;

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

    if (asyncFailureHandle_ != nullptr)
    {
        asyncFailureHandle_->userClient = nullptr;
        IODelete(asyncFailureHandle_, ClientAsyncHandle, 1);
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

void ConcurrentSharedDataQueue::setClientAsyncFailureHandle(OSAsyncReference64 ref, OSObject* client)
{
    EnterMonitor

    asyncFailureHandle_ = IONew(ClientAsyncHandle, 1);
    if (asyncFailureHandle_ != nullptr)
    {
        bcopy(ref, asyncFailureHandle_->ref, sizeof(OSAsyncReference64));
        asyncFailureHandle_->userClient = client;
    }
}

IOReturn ConcurrentSharedDataQueue::InvokeAsyncFailureHandle(IOReturn status)
{
    EnterMonitor
    
    if (asyncFailureHandle_ != nullptr)
    {
        BuildXLSandboxClient *client = OSDynamicCast(BuildXLSandboxClient, asyncFailureHandle_->userClient);
        return client->SendAsyncResult(asyncFailureHandle_->ref, status);
    }

    return kIOReturnError;
}

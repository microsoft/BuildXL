//
//  ConcurrentMultiplexingQueue.cpp
//  BuildXLSandbox
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include "ConcurrentMultiplexingQueue.hpp"
#include "ConcurrentSharedDataQueue.hpp"

#define super OSObject

OSDefineMetaClassAndStructors(ConcurrentMultiplexingQueue, OSObject)

ConcurrentMultiplexingQueue* ConcurrentMultiplexingQueue::Create()
{
    auto *instance = new ConcurrentMultiplexingQueue;
    if (instance)
    {
        bool initialized = instance->init();
        if (!initialized)
        {
            instance->release();
            instance = nullptr;
        }
    }

    if (!instance)
    {
        log_error("%s", "Failed to initialize multiplexing queue");
    }

    return instance;
}

bool ConcurrentMultiplexingQueue::init()
{
    if (!super::init())
    {
        return false;
    }

    lock_ = IORecursiveLockAlloc();

    reportQueueMappings_ = ConcurrentDictionary::withCapacity(kSharedDataQueueCount, "ReportQueues");
    if (!reportQueueMappings_)
    {
        return false;
    }

    return lock_ && reportQueueMappings_;
}

void ConcurrentMultiplexingQueue::free()
{
    OSSafeReleaseNULL(reportQueueMappings_);

    if (lock_)
    {
        IORecursiveLockFree(lock_);
        lock_ = nullptr;
    }

    super::free();
}

bool ConcurrentMultiplexingQueue::insertQueue(const OSSymbol *key, const ConcurrentSharedDataQueue *queue)
{
    EnterMonitor

    bool success = false;
    OSArray *container = reportQueueMappings_->getAs<OSArray>(key);
    if (container == nullptr)
    {
        // Add an array bucket into the dictionary as we're adding the very first queue
        container = OSArray::withCapacity(1);
        container->setObject(0, queue);
        success = reportQueueMappings_->insert(key, container);
        OSSafeReleaseNULL(container);
    }
    else
    {
        // Add the new qeueu to the end of its appropriate bucket
        success = container->setObject(queue);
    }

    return success;
}

bool ConcurrentMultiplexingQueue::removeQueues(const OSSymbol *key)
{
    EnterMonitor

    OSArray *container = nullptr;
    if ((container = reportQueueMappings_->getAs<OSArray>(key)) == nullptr)
    {
        log_debug("No report queue(s) found for PID %s in any bucket", key->getCStringNoCopy());
        return false;
    }
    else
    {
        unsigned int count = container->getCount();
        for (int i = 0; i < count; i++)
        {
            container->removeObject(i);
        }

        return reportQueueMappings_->remove(key);
    }
}

uint ConcurrentMultiplexingQueue::getBucketCount()
{
    return reportQueueMappings_->getCount();
}

bool ConcurrentMultiplexingQueue::setNotifactonPortForNextQueue(const OSSymbol *key, mach_port_t port)
{
    EnterMonitor

    OSArray *container = reportQueueMappings_->getAs<OSArray>(key);
    if (container == nullptr)
    {
        log_error("No queue(s) found in bucket for PID %s", key->getCStringNoCopy());
        return false;
    }

    OSCollectionIterator *iterator = OSCollectionIterator::withCollection(container);
    iterator->reset();

    bool success = false;
    ConcurrentSharedDataQueue *queue;
    while ((queue = OSDynamicCast(ConcurrentSharedDataQueue, iterator->getNextObject())))
    {
        if (!queue->isNotificationPortValid())
        {
            queue->setNotificationPort(port);
            success = true;
            break;
        }
    }

    iterator->release();
    return success;
}

IOMemoryDescriptor* ConcurrentMultiplexingQueue::getMemoryDescriptorForNextQueue(const OSSymbol *key)
{
    EnterMonitor

    IOMemoryDescriptor *descriptor = nullptr;
    OSArray *container = reportQueueMappings_->getAs<OSArray>(key);
    if (container == nullptr)
    {
        log_error("No queue(s) found in bucket for PID %s", key->getCStringNoCopy());
        return descriptor;
    }

    OSCollectionIterator *iterator = OSCollectionIterator::withCollection(container);
    iterator->reset();

    ConcurrentSharedDataQueue *queue;
    while ((queue = OSDynamicCast(ConcurrentSharedDataQueue, iterator->getNextObject())))
    {
        if (!queue->isDescriptorValid())
        {
            descriptor = queue->getMemoryDescriptor();
            break;
        }
    }

    iterator->release();
    return descriptor;
}

bool ConcurrentMultiplexingQueue::setFailureNotificationHandlerForAllQueues(const OSSymbol *key, OSAsyncReference64 ref, OSObject *client)
{
    EnterMonitor

    OSArray *container = reportQueueMappings_->getAs<OSArray>(key);
    if (container == nullptr)
    {
        log_error("No queue(s) found in bucket for PID %s", key->getCStringNoCopy());
        return false;
    }

    OSCollectionIterator *iterator = OSCollectionIterator::withCollection(container);
    iterator->reset();

    ConcurrentSharedDataQueue *queue;
    while ((queue = OSDynamicCast(ConcurrentSharedDataQueue, iterator->getNextObject())))
    {
        queue->setClientAsyncFailureHandle(ref, client);
    }

    iterator->release();
    return true;
}

bool ConcurrentMultiplexingQueue::enqueueDataForAllQueuesInContainer(OSArray *container, void *data, UInt32 size)
{
    EnterMonitor

    OSCollectionIterator *iterator = OSCollectionIterator::withCollection(container);
    iterator->reset();

    bool success = true;
    ConcurrentSharedDataQueue *queue;
    while ((queue = OSDynamicCast(ConcurrentSharedDataQueue, iterator->getNextObject())))
    {
        success = success && queue->enqueue(data, size);
    }

    iterator->release();
    return success;
}

bool ConcurrentMultiplexingQueue::enqueueDataForContainerAndRoundRob(OSArray *container, void *data, UInt32 size)
{
    EnterMonitor

    ConcurrentSharedDataQueue *queue = OSDynamicCast(ConcurrentSharedDataQueue, container->getLastObject());
    if (queue)
    {
        if (queue->enqueue(data, size))
        {
            // Rotate queues, we don't expect to ever store more than kSharedDataQueueCount queues so this is fine!
            // Pseudo round robbin, sharing the load between the number of queus in a given bucket
            queue->retain();
            container->removeObject(container->getCount() - 1);
            container->setObject(0, queue); // Implicitly shifts all other entries to the right from index
            queue->release();

            return true;
        }
    }
    else
    {
        log_error("%s", "Could not get a valid queue from the supplied bucket");
    }

    return false;
}

bool ConcurrentMultiplexingQueue::enqueueData(const OSSymbol *key, void *data, UInt32 size, bool roundRobbing)
{
    EnterMonitor

    if (unrecoverableFailureOccurred_)
    {
        return false;
    }

    OSArray *container = reportQueueMappings_->getAs<OSArray>(key);
    if (!container)
    {
        log_error("No queue(s) found in bucket for PID %s", key->getCStringNoCopy());
        return false;
    }

    container->retain();
    ConcurrentSharedDataQueue *queue = OSDynamicCast(ConcurrentSharedDataQueue, container->getLastObject());

    // All queues of a given container belong to the same user client with a specific pid, calling the async
    // handler callback on any of them will notify the client user-space code of the failure and let it handle it.
    // The user client itself releases all of its used resources once the 'clientDied' method is invoked from IOKit.
    bool success = roundRobbing ? enqueueDataForContainerAndRoundRob(container, data, size):
                                  enqueueDataForAllQueuesInContainer(container, data, size);

    if (!success)
    {
        unrecoverableFailureOccurred_ = true;
        queue->InvokeAsyncFailureHandle(kIOReturnNoSpace);
    }

    container->release();
    return success;
}

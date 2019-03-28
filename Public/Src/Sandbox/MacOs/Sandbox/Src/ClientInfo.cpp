// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "ClientInfo.hpp"

#define super OSObject

OSDefineMetaClassAndStructors(ClientInfo, OSObject)

ClientInfo* ClientInfo::create(const InitArgs& args)
{
    auto *instance = new ClientInfo;
    if (instance)
    {
        bool initialized = instance->init(args);
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

bool ClientInfo::init(const InitArgs& args)
{
    if (!super::init())
    {
        return false;
    }

    frozen_          = false;
    reportCounters_  = args.counters;

    queue_ = ConcurrentSharedDataQueue::create(args);
    if (queue_ == nullptr)
    {
        return false;
    }

    lock_ = IORecursiveLockAlloc();
    if (lock_ == nullptr)
    {
        return false;
    }

    return true;
}

void ClientInfo::free()
{
    OSSafeReleaseNULL(queue_);

    if (lock_)
    {
        IORecursiveLockFree(lock_);
        lock_ = nullptr;
    }

    super::free();
}

bool ClientInfo::createQueue(uint32_t entryCount, uint32_t entrySize, bool enableBatching)
{
    EnterMonitor

    if (frozen_ || queue_ != nullptr) return false;

    queue_ = ConcurrentSharedDataQueue::create(
    {
        .entryCount = entryCount,
        .entrySize  = entrySize,
        .enableBatching = enableBatching,
        .counters = reportCounters_
    });

    return queue_ != nullptr;
}

bool ClientInfo::setNotifactonPort(mach_port_t port)
{
    EnterMonitor

    if (frozen_ || queue_ == nullptr) return false;

    queue_->setNotificationPort(port);
    return true;
}

IOMemoryDescriptor* ClientInfo::getMemoryDescriptor()
{
    EnterMonitor

    return !frozen_ && queue_
        ? queue_->getMemoryDescriptor()
        : nullptr;
}

bool ClientInfo::setFailureNotificationHandler(OSAsyncReference64 ref, OSObject *client)
{
    EnterMonitor

    if (frozen_ || queue_ == nullptr) return false;

    queue_->setClientAsyncFailureHandle(ref, client);
    return true;
}

bool ClientInfo::enqueueReport(const EnqueueArgs &args)
{
    frozen_ = true;

    return queue_ && queue_->enqueueReport(args);
}

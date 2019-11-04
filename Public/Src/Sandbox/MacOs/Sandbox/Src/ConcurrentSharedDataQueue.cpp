// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <IOKit/IOMemoryDescriptor.h>
#include <IOKit/IODataQueueShared.h>
#include "Alloc.hpp"
#include "BuildXLSandboxClient.hpp"
#include "ConcurrentSharedDataQueue.hpp"

#define super OSObject

OSDefineMetaClassAndStructors(ConcurrentSharedDataQueue, OSObject)

typedef ConcurrentSharedDataQueue::ElemPayload ElemPayload;

static ElemPayload* getValue(QueueElem *e)            { return (ElemPayload*)LFDS711_QUEUE_UMM_GET_VALUE_FROM_ELEMENT(*e); }
static void setValue(QueueElem *e, ElemPayload *p)    { LFDS711_QUEUE_UMM_SET_VALUE_IN_ELEMENT(*e, p); }

static ElemPayload* getValue(FreeListElem *e)         { return (ElemPayload*)LFDS711_FREELIST_GET_VALUE_FROM_ELEMENT(*e); }
static void setValue(FreeListElem *e, ElemPayload *p) { LFDS711_FREELIST_SET_VALUE_IN_ELEMENT(*e, p); }

static void deallocateFreeListElem(FreeListElem *elem)
{
    ElemPayload *payload = getValue(elem);
    Alloc::Delete<QueueElem>(payload->queueElem, 1);
    Alloc::Delete<ElemPayload>(payload, 1);
}

QueueElem* ConcurrentSharedDataQueue::allocateElem(const EnqueueArgs &args)
{
    // try and get an element from the free list
    ElemPayload *payload = nullptr;

    FreeListElem *elem = nullptr;
    bool found = lfds711_freelist_pop(freeList_, &elem, nullptr);
    if (found)
    {
        payload = getValue(elem);
    }
    else
    {
        reportCounters_->freeListNodeCount++;
        payload = Alloc::New<ElemPayload>(1);
        if (payload == nullptr)
        {
            return nullptr;
        }

        payload->queueElem = Alloc::New<QueueElem>(1);
        if (payload->queueElem == nullptr)
        {
            Alloc::Delete<ElemPayload>(payload, 1);
            return nullptr;
        }

        setValue(&payload->freeListElem, payload);
    }

    // make sure the queue element is pointing to this payload
    setValue(payload->queueElem, payload);

    // set the actual payload bytes
    payload->report = args.report;
    payload->cacheRecord = args.cacheRecord;
    if (payload->cacheRecord)
    {
        payload->cacheRecord->retain();
    }

    return payload->queueElem;
}

void ConcurrentSharedDataQueue::releaseElem(QueueElem *elem)
{
    ElemPayload *payload = getValue(elem);
    payload->queueElem = elem;
    OSSafeReleaseNULL(payload->cacheRecord);
    lfds711_freelist_push(freeList_, &payload->freeListElem, nullptr);
}

ConcurrentSharedDataQueue* ConcurrentSharedDataQueue::create(const InitArgs& args)
{
    auto *instance = new ConcurrentSharedDataQueue;
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
        log_error("Failed to initialize shared data queue with %d entries of size %d", args.entryCount, args.entrySize);
    }

    return instance;
}

bool ConcurrentSharedDataQueue::init(const InitArgs& args)
{
    if (!super::init())
    {
        return false;
    }

    drainingDone_                 = false;
    unrecoverableFailureOccurred_ = false;
    reportCounters_               = args.counters;
    enableBatching_               = args.enableBatching;

    lock_ = IORecursiveLockAlloc();
    if (lock_ == nullptr)
    {
        return false;
    }

    queue_ = IOSharedDataQueue::withCapacity((args.entrySize + DATA_QUEUE_ENTRY_HEADER_SIZE) * args.entryCount);
    if (queue_ == nullptr)
    {
        return false;
    }

    freeList_ = Alloc::New<FreeList>(1);
    if (freeList_ == nullptr)
    {
        return false;
    }

    pendingReports_  = Alloc::New<Queue>(1);
    if (pendingReports_ == nullptr)
    {
        return false;
    }

    QueueElem *dummy = Alloc::New<QueueElem>(1); // this is dealocated in lfds711_queue_umm_cleanup()
    if (dummy == nullptr)
    {
        return false;
    }

    // init lock-free queue and free list
    lfds711_queue_umm_init_valid_on_current_logical_core(pendingReports_, dummy, nullptr);
    lfds711_freelist_init_valid_on_current_logical_core(freeList_, nullptr, 0, nullptr);

    // init consumer thread
    consumerThread_ = Thread::create(this, [](void *me, wait_result_t result)
                                    {
                                        static_cast<ConcurrentSharedDataQueue*>(me)->drainQueue();
                                    });
    if (consumerThread_ == nullptr)
    {
        return false;
    }

    // start the consumer thread
    consumerThread_->start();

    // These resources are released on the client side
    asyncFailureHandle_ = nullptr;

    return true;
}

void ConcurrentSharedDataQueue::free()
{
    drainingDone_ = true;

    // wait for consumer thread to finish
    if (consumerThread_ != nullptr)
    {
        consumerThread_->join();
    }

    LFDS711_MISC_MAKE_VALID_ON_CURRENT_LOGICAL_CORE_INITS_COMPLETED_BEFORE_NOW_ON_ANY_OTHER_LOGICAL_CORE;

    // purge any left over elements in the queue (can happen if the client exits abnormally)
    if (pendingReports_ != nullptr)
    {
        QueueElem *e;
        while (lfds711_queue_umm_dequeue(pendingReports_, &e)) releaseElem(e);
        lfds711_queue_umm_cleanup(pendingReports_, [](Queue *q, QueueElem *e, lfds711_misc_flag flag)
                                  {
                                      Alloc::Delete<QueueElem>(e, 1);
                                  });

        Alloc::Delete<Queue>(pendingReports_, 1);
        pendingReports_ = nullptr;
    }

    // cleanup free list
    if (freeList_ != nullptr)
    {
        lfds711_freelist_cleanup(freeList_, [](FreeList *l, FreeListElem *e)
                                 {
                                     deallocateFreeListElem(e);
                                 });

        Alloc::Delete<FreeList>(freeList_, 1);
        freeList_ = nullptr;
    }

    if (asyncFailureHandle_ != nullptr)
    {
        asyncFailureHandle_->userClient = nullptr;
        Alloc::Delete<ClientAsyncHandle>(asyncFailureHandle_, 1);
        asyncFailureHandle_ = nullptr;
    }

    if (lock_ != nullptr)
    {
        IORecursiveLockFree(lock_);
        lock_ = nullptr;
    }

    OSSafeReleaseNULL(consumerThread_);
    OSSafeReleaseNULL(queue_);

    super::free();
}

long long ConcurrentSharedDataQueue::getCount() const
{
    LFDS711_MISC_MAKE_VALID_ON_CURRENT_LOGICAL_CORE_INITS_COMPLETED_BEFORE_NOW_ON_ANY_OTHER_LOGICAL_CORE;

    long long count;
    lfds711_queue_umm_query(pendingReports_, LFDS711_QUEUE_UMM_QUERY_SINGLETHREADED_GET_COUNT, NULL, &count);
    return count;
}

void ConcurrentSharedDataQueue::setNotificationPort(mach_port_t port)
{
    EnterMonitor

    queue_->setNotificationPort(port);
}

IOMemoryDescriptor* ConcurrentSharedDataQueue::getMemoryDescriptor()
{
    EnterMonitor

    return queue_->getMemoryDescriptor();
}

void ConcurrentSharedDataQueue::setClientAsyncFailureHandle(OSAsyncReference64 ref, OSObject* client)
{
    EnterMonitor

    asyncFailureHandle_ = Alloc::New<ClientAsyncHandle>(1);
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

bool ConcurrentSharedDataQueue::enqueueReport(const EnqueueArgs &args)
{
    if (unrecoverableFailureOccurred_)
    {
        return false;
    }

    return enableBatching_
        ? enqueueWithBatching(args)
        : enqueueWithLocking(args);
}

bool ConcurrentSharedDataQueue::enqueueWithLocking(const EnqueueArgs &args)
{
    EnterMonitor

    return sendReport(args.report);
}

bool ConcurrentSharedDataQueue::sendReport(const AccessReport &report)
{
    bool sent = queue_->enqueue((void*)&report, sizeof(AccessReport));
    if (!sent)
    {
        log_error("Could not send data to shared queue from TID(%lld)", thread_tid(current_thread()));
        drainingDone_ = true;
        unrecoverableFailureOccurred_ = true;
        InvokeAsyncFailureHandle(kIOReturnNoMemory);
    }
    else
    {
        reportCounters_->totalNumSent++;
    }

    return sent;
}

bool ConcurrentSharedDataQueue::enqueueWithBatching(const EnqueueArgs &args)
{
    LFDS711_MISC_MAKE_VALID_ON_CURRENT_LOGICAL_CORE_INITS_COMPLETED_BEFORE_NOW_ON_ANY_OTHER_LOGICAL_CORE;

    QueueElem *elem = allocateElem(args);
    if (elem == nullptr)
    {
        return false;
    }

    lfds711_queue_umm_enqueue(pendingReports_, elem);
    reportCounters_->numQueued++;

    return true;
}

static uint s_backoffIntervalsMs[] = {1, 2, 4, 8, 16, 32, 64};
static uint s_backoffIntervalsLen = sizeof(s_backoffIntervalsMs) / sizeof(s_backoffIntervalsMs[0]);

void ConcurrentSharedDataQueue::drainQueue()
{
    if (!enableBatching_)
    {
        return;
    }

    LFDS711_MISC_MAKE_VALID_ON_CURRENT_LOGICAL_CORE_INITS_COMPLETED_BEFORE_NOW_ON_ANY_OTHER_LOGICAL_CORE;

    uint backoffCounter = 0;
    while (!drainingDone_)
    {
        QueueElem *elem;
        if (!lfds711_queue_umm_dequeue(pendingReports_, &elem) || elem == nullptr)
        {
            uint backoffIndex = backoffCounter < s_backoffIntervalsLen ? backoffCounter : s_backoffIntervalsLen - 1;
            IOSleep(/*milliseconds*/ s_backoffIntervalsMs[backoffIndex]);
            ++backoffCounter;
            continue;
        }

        backoffCounter = 0;
        reportCounters_->numQueued--;
        ElemPayload *payload = getValue(elem);

        if (payload->cacheRecord == nullptr)
        {
            sendReport(payload->report);
        }
        else if (payload->cacheRecord->HasStrongerRequestedAccess((RequestedAccess)payload->report.requestedAccess))
        {
            reportCounters_->numCoalescedReports++;
        }
        else
        {
            sendReport(payload->report);
        }

        releaseElem(elem);
    }
}

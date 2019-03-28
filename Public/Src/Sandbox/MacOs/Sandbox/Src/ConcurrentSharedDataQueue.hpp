// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef ConcurrentSharedDataQueue_hpp
#define ConcurrentSharedDataQueue_hpp

#include <IOKit/IOLib.h>
#include <IOKit/IOSharedDataQueue.h>
#include <IOKit/OSMessageNotification.h>
#include "BuildXLSandboxShared.hpp"
#include "Thread.hpp"

extern "C" {
#include "liblfds711.h"
}

typedef lfds711_queue_umm_state   Queue;
typedef lfds711_queue_umm_element QueueElem;
typedef lfds711_freelist_state    FreeList;
typedef lfds711_freelist_element  FreeListElem;

#define MAX_DATA_SIZE sizeof(AccessReport)

typedef struct{
    OSObject* userClient;
    OSAsyncReference64 ref;
} ClientAsyncHandle;

/*!
 * A straightforward wrapper around IOSharedDataQueue to provide a thread-safe way of enqueuing entries.
 */
class ConcurrentSharedDataQueue : public OSObject
{
    OSDeclareDefaultStructors(ConcurrentSharedDataQueue);

public:

    typedef struct {
        uint entryCount;
        uint entrySize;
        bool enableBatching;
        ReportCounters *counters;
    } InitArgs;

    typedef struct {
        /*! Must always be provided */
        const AccessReport &report;

        /*! May be NULL */
        const CacheRecord *cacheRecord;
    } EnqueueArgs;

    typedef struct {
        QueueElem *queueElem;
        FreeListElem freeListElem;
        AccessReport report;
        const CacheRecord *cacheRecord;
    } ElemPayload;

private:

    /*! Backing queue */
    IOSharedDataQueue *queue_;

    /*! Recursive lock used for synchronization */
    IORecursiveLock *lock_;

    /*! A pointer to an async failure handle */
    ClientAsyncHandle *asyncFailureHandle_;

    /*!
     * Various counters about reports sent to clients.
     *
     * IMPORTANT: This struct is shared between all connected clients,
     * so only atomic operations should be used to update its fields.
     */
    ReportCounters *reportCounters_;

    /*!
     * Whether or not batching is enabled.
     *
     * When enabled, all reports are first added to a lock-free queue ('pendingReports_')
     * and a dedicated thread is used to drain it.  Otherwise, reports are added directly
     * to a shared IO queue ('queue_') (which is done in a critical section, since the
     * shared IO queue is not thread-safe).
     */
    bool enableBatching_;

    /*!
     * A free list for keeping/reusing Queue elements.  The main reason for using this is
     * because Queue elements must not be deallocated before the Queue is freed (even
     * after an element has been dequeued, it must not be deallocated until we are done
     * using the queue).
     */
    FreeList *freeList_;

    /*!
     * A lock-free queue where reports are batched before being sent to the client.
     * This is used only if batching is enabled (see 'enableBatching_').
     */
    Queue *pendingReports_;

    /*!
     * A dedicated thread for draining 'pendingReports_'.
     * If batching is not enabled, this thread immediately finished without doing any work.
     */
    Thread *consumerThread_;

    /*!
     * An indicator for 'consumerThread_' letting it know that it's time to finish.
     */
    volatile bool drainingDone_;

    void drainQueue();

    /*!
     * Indicates if an unrecoverable error has occured. This happens when the sandbox was not able to successfully
     * enqueue an access report message. There is no logic to recover from this and mostly indicates that either a)
     * the report queue size is to small for the amount of transfered reports or b) the number of connections to the
     * sandbox kernel connection and with it the number of threads draining the report queues in user space are not
     * sufficient. After this occures, the extension has to be reloaded!
     */
    volatile bool unrecoverableFailureOccurred_;

    QueueElem* allocateElem(const EnqueueArgs &args);
    void releaseElem(QueueElem *elem);

    /*! * Enqueues the data to a lock-free queue ('pendingReports_') without entering the critical section. */
    bool enqueueWithBatching(const EnqueueArgs &args);

    /*! Enters the critical section and then calls 'send' (which enqueues the data to the shared IO queue). */
    bool enqueueWithLocking(const EnqueueArgs &args);

    /*!
     * Enqueues the data to the shared IO queue.
     *
     * IMPORTANT: the IO queue is not thread-safe and this method does not ensure synchronization;
     * ensuring proper synchronization is the responsibility of the callers.
     */
    bool sendReport(const AccessReport &report);

    /*!
     * Initializes this object, following the OSObject pattern.
     *
     * @result True if successful, False otherwise.
     */
    bool init(const InitArgs& args);

public:

    /*!
     * Releases held resources, following the OSObject pattern.
     */
    void free() override;

    /*!
     * Enters monitor then delegates to IOSharedDataQueue::enqueue.
     */
    bool enqueueReport(const EnqueueArgs &args);

    /*!
     * Returns the number of currently enqueued elements.
     */
    long long getCount() const;

    /*!
     * Enters monitor then delegates to IOSharedDataQueue::setNotificationPort
     */
    void setNotificationPort(mach_port_t port);

    /*!
     * Enters monitor then delegates to IOSharedDataQueue::getMemoryDescriptor
     */
    IOMemoryDescriptor *getMemoryDescriptor();

    /*!
     * Enters monitor then tries to set an async failure handle for the client owning the queue
     */
    void setClientAsyncFailureHandle(OSAsyncReference64 ref, OSObject* client);

    /*!
     * Enters monitor then checks for a valid async failure handle, invoking it if present
     */
    IOReturn InvokeAsyncFailureHandle(IOReturn status);

#pragma mark Static Methods

    /*!
     * Factory method, following the OSObject pattern.
     *
     * First creates an object (by calling 'new'), then invokes 'init' on the newly create object.
     *
     * If either of the steps fails, nullptr is returned.
     *
     * When object creation succeeds but initialization fails, 'release' is called on the created
     * object and nullptr is returned.
     */
    static ConcurrentSharedDataQueue* create(const InitArgs& args);
};

#endif /* ConcurrentSharedDataQueue_hpp */

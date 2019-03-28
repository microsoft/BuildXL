// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef ClientInfo_hpp
#define ClientInfo_hpp

#include "BuildXLSandboxShared.hpp"
#include "CacheRecord.hpp"
#include "ConcurrentSharedDataQueue.hpp"
#include "Monitor.hpp"

typedef ConcurrentSharedDataQueue::EnqueueArgs EnqueueArgs;
typedef ConcurrentSharedDataQueue::InitArgs InitArgs;

/*!
 * Various information associated with a connected client.
 */
class ClientInfo : public OSObject
{
    OSDeclareDefaultStructors(ClientInfo);

private:

    /*! Recursive lock used for synchronization */
    IORecursiveLock *lock_;

    /*!
     * Shared counters (with all other clients) for counting the number of enqueued/sent reports.
     *
     * NOTE: must use atomic increments to update these counters.
     */
    ReportCounters *reportCounters_;

    /*!
     * A wrapper around IOSharedDataQueue.
     */
    ConcurrentSharedDataQueue *queue_;

    /*!
     * A client becomes frozen after the first call to 'enqueueData'.
     *
     * Once frozen, all calls that mutate the queue (e.g., 'setNotifactonPort', etc.) are denied.
     */
    bool frozen_;

    /*!
     * Initializes this object, following the OSObject pattern.
     *
     * @result indicates success.
     */
    bool init(const InitArgs& args);

public:

    /*!
     * Releases held resources, following the OSObject pattern.
     */
    void free() override;

    /*!
     * Creates a shared data queue for this client.  May only be called once.
     *
     * @result indicates success (it's False, e.g., if a queue had already been created).
     */
    bool createQueue(uint32_t entryCount, uint32_t entrySize, bool enableBatching);

    /*!
     * Sets the notification port for the underlying shared data queue.
     *
     * 'createQueue' must be called prior to calling this method.
     *
     * @result indicates success (it's False, e.g., if 'createQueue' hadn't been called first).
     */
    bool setNotifactonPort(mach_port_t port);

    /*!
     * Returns the memory descriptor of the underlying shared data queue.
     *
     * @result a newly allocated memory descriptor.  The caller is responsible for releasing it.
     */
    IOMemoryDescriptor* getMemoryDescriptor();

    /*!
     * Sets the failure notification async callback handle for the underlying shared data queue.
     *
     * @result indicates success.
     */
    bool setFailureNotificationHandler(OSAsyncReference64 ref, OSObject *client);

    /*!
     * Enqueues a report into the underlying shared data queue.
     *
     * @result indicates success.
     */
    bool enqueueReport(const EnqueueArgs &args);

#pragma mark Static Methods

    /*! Static factory method, following the OSObject pattern */
    static ClientInfo* create(const InitArgs& args);
};

#endif /* ClientInfo_hpp */

//
//  ConcurrentMultiplexingQueue.hpp
//  BuildXLSandbox
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef ConcurrentMultiplexingQueue_hpp
#define ConcurrentMultiplexingQueue_hpp

#include "ConcurrentDictionary.hpp"
#include "ConcurrentSharedDataQueue.hpp"
#include "BuildXLSandboxShared.hpp"

#define kSharedDataQueueCount 5

/*!
 * ConcurrentMultiplexingQueue offers an abstraction around buckets of ConcurrentSharedDataQueue helping with setup
 * and data enqueuing.
 */
class ConcurrentMultiplexingQueue : public OSObject
{
    OSDeclareDefaultStructors(ConcurrentMultiplexingQueue);

private:

    /*! Recursive lock used for synchronization */
    IORecursiveLock *lock_;

    /*!
     * Keeps the PID --> [ConcurrentSharedDataQueue*, ...] mapping for all currently attached clients inside an
     * array bucket.
     *
     * When a new client is attached (by virtue of calling the 'Sandbox::ListenForFileAccessReports'
     * function, a new queue is created and added to the end of the bucket that belongs to the PID
     * When a client is about to disconnect (e.g., by virtue of that process exiting)
     * the corresponding queue is released and removed from the appropriate bucket.
     */
    ConcurrentDictionary  *reportQueueMappings_;

    bool enqueueDataForContainerAndRoundRob(OSArray *container, void *data, UInt32 size);
    bool enqueueDataForAllQueuesInContainer(OSArray *container, void *data, UInt32 size);

public:

    /*!
     * Initializes this object, following the OSObject pattern.
     *
     * @result True if successful, False otherwise.
     */
    bool init() override;

    /*!
     * Releases held resources, following the OSObject pattern.
     */
    void free() override;

    /*!
     * Thread-safe version for inserting ConcurrentSharedDataQueues into a bucket inxed by PID key.
     *
     * @result is True when a new entry is inserted and False on failure.
     */
    bool insertQueue(const OSSymbol *key, const ConcurrentSharedDataQueue *queue);

    /*!
     * Thread-safe version for removing ConcurrentSharedDataQueues from a bucket by PID key.
     *
     * @result is True when an entry is removed and False on failure.
     */
    bool remove(const OSSymbol *key);

    /*!
     * Thread-safe version for querying the current bucket count.
     *
     * @result is True when an entry is removed and False on failure.
     */
    uint getBucketCount();

    /*!
     * Thread-safe version for removing all ConcurrentSharedDataQueues from a bucket index by PID key.
     *
     * @result is True when an entry is removed and False on failure.
     */
    bool removeQueues(const OSSymbol *key);

    /*!
     * Thread-safe version for setting the notification port of the next valid queue without a notifaction port.
     *
     * @result is True when an entry is removed and False on failure.
     */
    bool setNotifactonPortForNextQueue(const OSSymbol *key, mach_port_t port);

    /*!
     * Thread-safe version for setting the notification port of the next valid queue without a notifaction port.
     *
     * @result is True when an entry is removed and False on failure.
     */
    IOMemoryDescriptor* getMemoryDescriptorForNextQueue(const OSSymbol *key);

    /*!
     * Thread-safe version for setting the failure notification async callback handle for all queues inside of
     * a bucket retrieved by key.
     */
    bool setFailureNotificationHandlerForAllQueues(const OSSymbol *key, OSAsyncReference64 ref, OSObject *client);

    /*!
     * Enters monitor then delegates to IOSharedDataQueue::enqueue, iterating over all queues in a particular bucket
     * in an easy round robbin manner by default, otherwise enqueues the data into all queues of a given bucket by key
     * if roundRobbing is false.
     *
     * @result is True when an entry is removed and False on failure.
     */
    bool enqueueData(const OSSymbol *key, void *data, UInt32 size, bool roundRobbing = true);

#pragma mark Static Methods

    static ConcurrentMultiplexingQueue* Create();
};

#endif /* ConcurrentMultiplexingQueue_hpp */

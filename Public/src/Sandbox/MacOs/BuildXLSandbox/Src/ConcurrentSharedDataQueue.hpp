//
//  ConcurrentSharedDataQueue.hpp
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef ConcurrentSharedDataQueue_hpp
#define ConcurrentSharedDataQueue_hpp

#include <IOKit/IOLib.h>
#include <IOKit/IOSharedDataQueue.h>
#include <IOKit/OSMessageNotification.h>
#include "Monitor.hpp"
#include "BuildXLSandboxShared.hpp"

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

private:

    /*! Backing queue */
    IOSharedDataQueue *queue_;

    /*! A handle to the queues notification port */
    mach_port_t port_;

    /*! A handle to the queues memory descriptor */
    IOMemoryDescriptor *descriptor_;

    /*! Recursive lock used for synchronization */
    IORecursiveLock *lock_;

    /*! A pointer to an async failure handle */
    ClientAsyncHandle *asyncFailureHandle_;

public:

    /*!
     * Initializes this object, following the OSObject pattern.
     *
     * @result True if successful, False otherwise.
     */
    bool init(UInt32 numEntries, UInt32 entrySize);

    /*!
     * Releases held resources, following the OSObject pattern.
     */
    void free() override;

    /*!
     * Enters monitor then delegates to IOSharedDataQueue::enqueue.
     */
    bool enqueue(void *data, UInt32 dataSize);

    /*!
     * Enters monitor then delegates to IOSharedDataQueue::setNotificationPort
     */
    void setNotificationPort(mach_port_t port);

    /*!
     * Enters monitor then checks if a valid notification port has already been set for the internal queue
     */
    bool isNotificationPortValid();

    /*!
     * Enters monitor then delegates to IOSharedDataQueue::getMemoryDescriptor
     */
    IOMemoryDescriptor *getMemoryDescriptor();

    /*!
     * Enters monitor then checks if a valid memory descriptor has already been set for the internal queue
     */
    bool isDescriptorValid();

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
    static ConcurrentSharedDataQueue* withEntries(UInt32 numEntries, UInt32 entrySize);
};

#endif /* ConcurrentSharedDataQueue_hpp */

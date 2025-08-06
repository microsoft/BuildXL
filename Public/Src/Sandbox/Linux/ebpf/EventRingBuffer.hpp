// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#ifndef BUILDXL_SANDBOX_EBPF_EVENT_RINGBUFFER_HPP
#define BUILDXL_SANDBOX_EBPF_EVENT_RINGBUFFER_HPP

#include <functional>
#include <mutex>

#include <signal.h>
#include "bpf/libbpf.h"
#include "bpf/bpf.h"
#include "ebpfcommon.h"

#include "ConcurrentQueue.h"
#include "ebpfcommon.h"
#include "bxl_observer.hpp"

namespace buildxl {
namespace linux {
namespace ebpf {

class OverflowEventRingBuffer;

/**
 * Base class for a ring buffer that can be used to poll events from the kernel and send them to the main event queue.
 * The expected usage is that a single instance of this class is created for the regular ebpf kernel event consumption. If the associated ring buffer never becomes full, no other instances are created.
 * The lifetime of the instance is managed by the caller. After construction, the flow is as follows: Initialize() -> NotifyDeactivated() -> NotifyDeactivated() -> WaitForInactive().
 * A callback is provided to notify when the capacity has been exceeded, so that a new ring buffer can be created. The caller should create a new instance of OverflowEventRingBuffer to handle the overflow and 
 * notify the previous buffer that it has been deactivated.
 * In the context of this class the term 'main queue' refers to the queue that is used to send events coming from the ring buffer. This queue is drained by the runner process (sandbox.cpp - outside of this class) and the 
 * events are processed by the BxlObserver. A regular EventRingBuffer instance just sends the events to the main queue. On the other hand, when an OverflowEventRingBuffer instance is created, there is always a previous 
 * buffer instance that is currently handling events. Since the order of the events that arrive to the main queue needs to be preserved, an OverflowEventRingBuffer instance will temporarily place events coming from its ring buffer
 * into an overflow queue. Once the previous instance becomes inactive, the overflow queue is drained into the main queue and new events are sent to the main queue directly.
 */
class EventRingBuffer {
public:
    /**
     * The given root process exit signal and stop signal are managed from the caller
     * The event queue is used to send events from the ring buffer to the main event queue.
     * The capacityExceededCallback is called when the ring buffer capacity is exceeded.
     */
    EventRingBuffer(
        BxlObserver *bxl, 
        volatile sig_atomic_t *rootProcessExited,
        volatile sig_atomic_t *stopSignal,
        buildxl::common::ConcurrentQueue<ebpf_event *> &eventQueue,
        std::function<void(EventRingBuffer *)> capacityExceededCallback);

    ~EventRingBuffer(); 

    int GetId() const { return m_Id; }
    virtual int Initialize() { 
        LogDebug("[Event ring buffer %d] Initialize()", m_Id);
        return Initialize(SendEventToMainQueue); 
    }
    const size_t GetMinimumAvailableSpace() const { return m_minAvailableSpace.load(); }
    const int GetRingBufferFd() const { return m_ringBufferFd; }
    EventRingBuffer* GetPrevious() { return m_previous; }
    const size_t GetRingBufferSize() const { return m_ringBufferSize; }
    
    /** Get the ring buffer available space */
    int GetAvailableSpace() const {
        // ring__avail_data_size returns the number of bytes not yet consumed - the opposite of the available space
        return m_ringBufferSize - ring__avail_data_size(ring_buffer__ring(m_ringBufferManager, 0));
    }

    /**
     * Returns the capacity threshold for this buffer.
     * This is the threshold at which the capacity exceeded callback is called.
     */
    int GetCapacityThreshold() const { return m_capacityThreshold; }

    /**
     * Notifies that the ring buffer has been placed on kernel side and it is ready to be polled.
     * Received events will be send to the main concurrent queue.
     * This method does not block.
     */
    virtual int NotifyActivated();

    /**
     * Notifies that the ring buffer has been removed from the kernel side.
     * The ring buffer may still receive leftover events until all ebpf programs using it are done, so this method
     * will wait for a grace period before releasing the ring buffer and moving the events from the overflow queue to the main event queue.
     * This method does not block and returns immediately.
     */
    virtual int NotifyDeactivated();

    /**
     * Blocks until the buffer is inactive
     */
    void WaitForInactive();

    /**
     * Whether this buffer has been notified to be deactivated, the grace period has elapsed and all the events in the overflow queue have been moved to the main event queue.
     */
    bool IsInactive() const { return m_bufferInactive; }

    /**
     * Terminates the buffer. Equivalent to NotifyDeactivated() followed by WaitForInactive(), but without the grace period.
     * This can be called when the buffer is no longer needed and the grace period is not required. Typically called by the runner when it is exiting, after the process tree has finished.
     */
    inline void Terminate() { 
        LogDebug("[Event ring buffer %d] Terminate()", m_Id);
        return Terminate(false); 
    }

private:
    static void *GracePeriodThread(void * arg);
    // The root process exit signal. This is flagged from the runner
    volatile sig_atomic_t *m_rootProcessExited;
    // The stop signal. This is flagged from the runner
    volatile sig_atomic_t *m_stopSignal;
    // Guards the state of the buffer when waiting for it to be inactive.
    std::mutex m_waitForBufferInactiveMutex;

protected:
    EventRingBuffer(
        int id,
        BxlObserver *bxl, 
        volatile sig_atomic_t *rootProcessExited,
        volatile sig_atomic_t *stopSignal,
        buildxl::common::ConcurrentQueue<ebpf_event *> &eventQueue,
        std::function<void(EventRingBuffer *)> capacityExceededCallback,
        EventRingBuffer *previous);

    /**
     * Stops polling, optionally waits for the grace period to be over, frees the ring buffer manager and the ring buffer file descriptor, 
     * and flags the buffer as inactive.
     */
    virtual void Terminate(bool useGracePeriod);

    /* Consume any remaining items in the ring buffers */
    void FlushRingBufferEvents();

    /**
     * Create a new ring buffer and its associated ring buffer manager.
     */
    int Initialize(ring_buffer_sample_fn sampleCallback);

    /**
     * Consume one event from the ring buffer and send it to the main event queue.
     */
    static int SendEventToMainQueue(void *ctx, void *data, size_t data_sz);

    /**
     * Poll the ring buffer until the stop signal is set or an error occurs.
     */
    static void *PollingThread(void *arg);

    /** Log an error message. */
    int LogError(const char *fmt, ...);

    /** 
     * Log a debug message. 
     * After the root process has exited, this method does not log anything, since the FIFO might be closed by the time we try to log it.
     * */
    void LogDebug(const char *fmt, ...) const;

    /** 
     * Log an info message. 
     * After the root process has exited, this method does not log anything, since the FIFO might be closed by the time we try to log it.
     * */
    void LogInfo(const char *fmt, ...) const;

    /**
     * Update the minimum available space in the ring buffer for telemetry purposes.
     * Returns the available space in the ring buffer.
     */
    size_t UpdateMinimumRingbufferAvailableSpace();

    int RestorePollingThreadPriority();

    // The minimum available space in the ring buffer for the lifetime of this ring buffer
    std::atomic<size_t> m_minAvailableSpace = -1;
    // The ring buffer file descriptor
    int m_ringBufferFd = -1;
    // The associated ring buffer manager
    ring_buffer *m_ringBufferManager = nullptr;
    BxlObserver *m_bxl = nullptr;
    // The thread that polls the ring buffer for events
    pthread_t m_pollingThread = 0;
    // The main event queue to which the events will be sent
    buildxl::common::ConcurrentQueue<ebpf_event *> &m_eventQueue;
    // Whether the capacity exceeded callback has been called. This happens only once per buffer, when the ring buffer capacity is exceeded.
    bool m_isCapacityExceededCallbackCalled = false;
    // Callback to be called when the buffer capacity exceeds the threshold
    std::function<void(EventRingBuffer *)> m_capacityExceededCallback;
    // Thread that waits for the grace period to be over, after the buffer is deactivated, and does proper cleanup.
    pthread_t m_gracePeriodThread = 0;
    // Whether this buffer is inactive.
    std::atomic<bool> m_bufferInactive = false;
    // The previous buffer in the chain. For this class, this is always nullptr. For an overflow buffer, this is the previous one.
    EventRingBuffer *m_previous = nullptr;
    // The ID, used for logging purposes. Every instance gets an increasingly unique ID.
    int m_Id = 0;
    // Size of the ring buffer in bytes. This avoid querying the ring buffer over and over again.
    unsigned long m_ringBufferSize;
    // The minimum available space in the ring buffer that triggers the capacity exceeded callback.
    unsigned long m_capacityThreshold;
};

/**
 * Overflow ring buffer that is used when the ring buffer capacity is exceeded.
 * Multiple instances of this class can be created as needed, each one handling the overflow of the previous one.
 * Instances send events to an overflow queue until the previous instance is inactive, at which point the overflow is flushed and they start sending events to the main event queue. This
 * is so event order is preserved.
 * The lifetime of the instance follows the same flow as the base class
 * Instances are created from outside of this class, but each instance will free the previous one when it becomes inactive.
 */
class OverflowEventRingBuffer final : public EventRingBuffer {
public:
    OverflowEventRingBuffer(
        BxlObserver *bxl, 
        volatile sig_atomic_t *rootProcessExited, 
        buildxl::common::ConcurrentQueue<ebpf_event *> &eventQueue,
        std::function<void(EventRingBuffer *)> capacityExceededCallback,
        EventRingBuffer *previous);

    ~OverflowEventRingBuffer();

    int Initialize() override { 
        LogDebug("[Event ring buffer %d - overflow] Initialize()", m_Id);
        return EventRingBuffer::Initialize(SendEventToActiveQueue); 
    }

    /**
     * In addition to the base class implementation, after the grace period is over, the previous instance is waited on until it is inactive. After which point the
     * overflow ring buffer is freed and the events in the overflow queue are moved to the main event queue. The previous buffer is deleted.
     * This method does not block
     */
    int NotifyDeactivated() override;

    /**
     * In addition to the base class implementation, this method starts a thread that waits for the previous buffer to be inactive, and afterwards, drains the overflow queue and moves the events to the main event queue.
     */
    int NotifyActivated() override;

private:
    /**
     * See NotifyDeactivated() for details.
     */
    static void *OverflowGracePeriodThread(void *arg);

    /**
     * See NotifyDeactivated() for details.
     */
    static void *DrainOverflowThread(void *arg);
    
    /**
     * Stops polling, optionally waits for the grace period to be over, frees the ring buffer manager and the ring buffer file descriptor, waits for the previous buffer to be inactive,
     * and moves the events from the overflow queue to the main event queue.
     * If the grace period is not used, the polling thread is cancelled immediately.
     */
    void Terminate(bool useGracePeriod) override;

    /**
     * Sends an event to the overflow queue or main queue, depending on the available space in the ring buffer.
     * The instance starts by sending events to the overflow queue. This is because such instance is created when the previous buffer has reached the overflow threshold, and it still has events to be flushed.
     * If the available ring buffer space is above the overflow threshold, the capacity exceeded callback is called, which repeats the process of creating a new overflow buffer.
     * Once the previous buffer becomes inactive - meaning all their events have been processed - events are moved directly to the main event queue.
     */
    static int SendEventToActiveQueue(void *ctx, void *data, size_t data_sz);

    // The local stop signal for this buffer. This is used to stop the polling thread and the grace period thread.
    volatile sig_atomic_t m_localStopSignal;
    // When this buffer is created, it is because the previous buffer has reached the overflow threshold. So this buffer
    // starts in overflow mode. After the previous buffer is inactive, this buffer will start sending events to the main event queue.
    bool m_inOverflowMode = true;
    // When this overflow buffer is created, and still in "overflow mode", events arriving into the ring buffer get sent to this overflow queue. 
    // When the previous ring buffer is deactivated, m_inOverflowMode is disabled and the events accumulated here are moved to the main event queue: after that, any new events that follow will be pushed to the main queue.
    buildxl::common::ConcurrentQueue<ebpf_event *> m_overflowEventQueue;

    pthread_t m_drainOverflowThread = 0;
};


} // ebpf
} // linux
} // buildxl

#endif
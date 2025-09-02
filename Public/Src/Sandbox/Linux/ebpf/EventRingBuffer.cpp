// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include "EventRingBuffer.hpp"
#include <math.h>
#include <numa.h>
#include <sched.h>
#include <errno.h>

// 10 milliseconds grace period for EBPF programs to continue using the original ring buffer after a new ring buffer is created
#define GRACE_PERIOD_MS 10

namespace buildxl {
namespace linux {
namespace ebpf {

EventRingBuffer::EventRingBuffer(
        BxlObserver *bxl, 
        volatile sig_atomic_t *rootProcessExited,
        volatile sig_atomic_t *stopSignal,
        buildxl::common::ConcurrentQueue<ebpf_event *> &eventQueue,
        std::function<void(EventRingBuffer *)> capacityExceededCallback,
        int ringBufferSizeMultiplier)
        // The initial ring buffer always has id 0
        : EventRingBuffer(/* id */ 0, bxl, rootProcessExited, stopSignal, eventQueue, capacityExceededCallback, /* previous buffer */ nullptr, ringBufferSizeMultiplier) {
    }

EventRingBuffer::EventRingBuffer(
    int id,
    BxlObserver *bxl, 
    volatile sig_atomic_t *rootProcessExited,
    volatile sig_atomic_t *stopSignal,
    buildxl::common::ConcurrentQueue<ebpf_event *> &eventQueue,
    std::function<void(EventRingBuffer *)> capacityExceededCallback,
    EventRingBuffer *previous,
    int ringBufferSizeMultiplier)
    : m_bxl(bxl), 
        m_rootProcessExited(rootProcessExited), 
        m_stopSignal(stopSignal), 
        m_eventQueue(eventQueue), 
        m_capacityExceededCallback(capacityExceededCallback), 
        m_previous(previous),
        m_Id(id),
        // Every new buffer gets a larger ring buffer. The size needs to be a power of 2, so we use the buffer ID to determine the size.
        // The first buffer has a size of FILE_ACCESS_RINGBUFFER_SIZE, the second buffer has a size of 2 * FILE_ACCESS_RINGBUFFER_SIZE, and so on.
        // Even though EBPF claims that inner maps need to be of the same size (unless BPF_F_INNER_MAP is set), the ringbuffer case seems to behave differently and
        // 1) BPF_F_INNER_MAP cannot be set for ring buffers and 2) inner ringbuffer maps can be of different sizes.
        // The multiplier is typically 1 and just added as a servicing option
        m_ringBufferSize(previous == nullptr ? (FILE_ACCESS_RINGBUFFER_SIZE * ringBufferSizeMultiplier) : previous->GetRingBufferSize() * 2) {

    // The capacity exceeded threshold is 30% of the ring buffer size.
    m_capacityThreshold = m_ringBufferSize * 3 / 10; 
}

EventRingBuffer::~EventRingBuffer() {
    if (m_ringBufferManager != nullptr) {
        // We shouldn't have any unconsumed events in the ring buffer at this point. Let's make sure we have consumed them all.
        // This is just a safety measure to make sure we don't silently drop events and we surface this as an error. At this point it is too late
        // to do anything about it, just because event order cannot be altered and the assumption is that the grace period is enough to guarantee that.
        int res = ring_buffer__consume(m_ringBufferManager);
        // We expect 0 (no events left) or some negative number (error).
        if (res > 0){
            LogError("[Event ring buffer %d] There are %d unconsumed events in the ring buffer. This is unexpected and may lead to data loss.", m_Id, res);
        }
        // Free the ring buffer manager
        ring_buffer__free(m_ringBufferManager);
        m_ringBufferManager = nullptr;
    }
    if (m_ringBufferFd != -1) {
        close(m_ringBufferFd);
        m_ringBufferFd = -1;
    }
    m_bufferInactive = true;
}

int EventRingBuffer::NotifyActivated() {
    pthread_attr_t attr;
    struct sched_param param;
    
    // We want the polling thread to have the highest priority to ensure it can consume events from the ring buffer as quickly as possible.
    // Initialize thread attributes
    if (pthread_attr_init(&attr) != 0) {
        LogError("[Event ring buffer %d] Failed to initialize thread attributes: %s\n", m_Id, strerror(errno));
        return -1;
    }
    
    // Set the inherit scheduler attribute to PTHREAD_EXPLICIT_SCHED
    // This ensures the thread uses the scheduling attributes we set, rather than inheriting from parent
    if (pthread_attr_setinheritsched(&attr, PTHREAD_EXPLICIT_SCHED) != 0) {
        LogError("[Event ring buffer %d] Failed to set inherit scheduler attribute: %s\n", m_Id, strerror(errno));
        pthread_attr_destroy(&attr);
        return -1;
    }

    // Set scheduling policy to SCHED_FIFO (real-time, first-in-first-out)
    if (pthread_attr_setschedpolicy(&attr, SCHED_FIFO) != 0) {
        LogError("[Event ring buffer %d] Failed to set scheduling policy: %s\n", m_Id, strerror(errno));
        pthread_attr_destroy(&attr);
        return -1;
    }
    
    // Set maximum priority for the SCHED_FIFO policy
    param.sched_priority = sched_get_priority_max(SCHED_FIFO);
    if (pthread_attr_setschedparam(&attr, &param) != 0) {
        LogError("[Event ring buffer %d] Failed to set thread priority: %s\n", m_Id, strerror(errno));
        pthread_attr_destroy(&attr);
        return -1;
    }
    
    // Create the thread with the configured attributes
    if (pthread_create(&m_pollingThread, &attr, PollingThread, this) != 0) {
        LogError("[Event ring buffer %d] Process exit monitoring thread failed to start %s\n", m_Id, strerror(errno));
        pthread_attr_destroy(&attr);
        return -1;
    }

    // Pin the polling thread to the same CPU whose NUMA node the ring buffer was set to
    // This improves cache and NUMA locality.
    cpu_set_t cpuset;
    CPU_ZERO(&cpuset);
    CPU_SET(m_pollingThreadCpuId, &cpuset);
    if (pthread_setaffinity_np(m_pollingThread, sizeof(cpuset), &cpuset) != 0) {
        LogError("[Event ring buffer %d] Failed to set affinity: %s", m_Id, strerror(errno));
        pthread_attr_destroy(&attr);
        return -1;
    }

    // Clean up the attributes
    pthread_attr_destroy(&attr);

    LogDebug("[Event ring buffer %d] NotifyActivated()", m_Id);
    return 0;
}

int EventRingBuffer::NotifyDeactivated() {
    // Let's lower the priority of the polling thread so it doesn't compete with other potentially active entries.
    // After the buffer is deactivated, we shouldn't need a high priority thread to poll the ring buffer.
    if (RestorePollingThreadPriority())
    {
        return -1;
    }

    if (pthread_create(&m_gracePeriodThread, NULL, GracePeriodThread, this) != 0) {
        LogError("[Event ring buffer %d] Grace period thread failed to start %s\n", m_Id, strerror(errno));
        return -1;
    }

    LogDebug("[Event ring buffer %d] NotifyDeactivated()", m_Id);

    return 0;
}

int EventRingBuffer::RestorePollingThreadPriority() {
    struct sched_param param;
    
    // Set to default scheduling policy (SCHED_OTHER) with priority 0
    param.sched_priority = 0;
    
    if (pthread_setschedparam(m_pollingThread, SCHED_OTHER, &param) != 0) {
        LogError("[Event ring buffer %d] Failed to restore polling thread priority: %s\n", m_Id, strerror(errno));
        return -1;
    }
    
    return 0;
}

void EventRingBuffer::Terminate(bool useGracePeriod) {
    if (useGracePeriod) {
        // Wait for the grace period to be over
        usleep(GRACE_PERIOD_MS * 1000);  // usleep takes microseconds, so multiply by 1000
        LogDebug("[Event ring buffer %d] Terminate(): grace period is over", m_Id);
    }

    // Cancel the polling thread
    pthread_cancel(m_pollingThread);
    pthread_join(m_pollingThread, NULL);
    
    // Flush any remaining events in the ring buffer and flag the buffer as inactive
    FlushRingBufferEvents();

     // Free the ring buffer manager and free the file access ring buffer
    ring_buffer__free(m_ringBufferManager);
    m_ringBufferManager = nullptr;
    close(m_ringBufferFd); 
    m_ringBufferFd = -1;

    m_bufferInactive = true;

    LogDebug("[Event ring buffer %d] Terminate(): buffer is inactive", m_Id);
}

void *EventRingBuffer::GracePeriodThread(void * arg) {
    auto *self = static_cast<EventRingBuffer *>(arg);

    self->LogDebug("[Event ring buffer %d] Grace period thread started", self->m_Id);

    self->Terminate(true);

    self->LogDebug("[Event ring buffer %d] Grace period thread finished", self->m_Id);

    return nullptr;
}

/* Consumes any remaining items in the ring buffers */
void EventRingBuffer::FlushRingBufferEvents()
{
    // Let's account for interrupted system calls
    // and retry until we consume everything
    int res = 0;
    do {
        res = ring_buffer__consume(m_ringBufferManager);
    } while (res == -EINTR);
}

int EventRingBuffer::Initialize(ring_buffer_sample_fn sampleCallback) {
    // Allocate the ring buffer on the same numa node where the polling thread will be located
    // We use the CPU we're currently running on (safe, best-effort).
    LIBBPF_OPTS(bpf_map_create_opts, file_access_options);
    file_access_options.map_flags = BPF_F_NUMA_NODE;
    m_pollingThreadCpuId = sched_getcpu(); // Update the CPU we used so we can use it later for the polling thread
    file_access_options.numa_node = numa_node_of_cpu(m_pollingThreadCpuId);

    m_ringBufferFd =  bpf_map_create(BPF_MAP_TYPE_RINGBUF, "file_access_ring_buffer", 0, 0, m_ringBufferSize, &file_access_options);
    if (m_ringBufferFd < 0)
    {
        LogError("[Event ring buffer %d] Failed to create temporary ring buffer: %s\n", m_Id, strerror(errno));
        return -1;
    }

    m_ringBufferManager = ring_buffer__new(m_ringBufferFd, sampleCallback, /* ctx */ this, /* opts */ NULL);
    if (!m_ringBufferManager) {
        LogError("[Event ring buffer %d] Failed to create ring buffer manager: %s\n", m_Id, strerror(errno));
        close(m_ringBufferFd);
        m_ringBufferFd = -1;
        m_ringBufferManager = nullptr;
        return -1;
    }

    return 0;
}

int EventRingBuffer::SendEventToMainQueue(void *ctx, void *data, size_t data_sz) {
    auto *self = static_cast<EventRingBuffer *>(ctx);
    
    auto availableSpace = self->UpdateMinimumRingbufferAvailableSpace();
    
    // If we reached the availability threshold (30% available space), we call the capacity exceeded callback.
    if (availableSpace < self->m_capacityThreshold && !self->m_isCapacityExceededCallbackCalled) 
    {
        self->LogInfo("[Event ring buffer %d] Capacity exceeded, available space: %zu. Threshold: %zu. Calling capacity exceeded callback", self->m_Id, availableSpace, self->m_capacityThreshold);
        self->m_isCapacityExceededCallbackCalled = true;
        // Notify that the buffer capacity has been exceeded
        self->m_capacityExceededCallback(self);
    }

    // Copy event data to local queue to free space from the shared ring buffer for more kernel events.
    ebpf_event *new_event = (ebpf_event *)malloc(data_sz);

    if (!new_event) {
        self->LogError("[Event ring buffer %d] Failed to allocate memory for event\n", self->m_Id);
        return -1;
    }

    memcpy(new_event, data, data_sz);

    self->m_eventQueue.Enqueue(new_event);

    return 0;
}

/**
 * Polls the ring buffer until the stop signal is set or an error occurs.
 */
void *EventRingBuffer::PollingThread(void *arg) {
    EventRingBuffer *self = static_cast<EventRingBuffer *>(arg);

    int err = 0;
    while (!*(self->m_stopSignal)) {
        // Process Events
        // When the ring buffer is empty, poll will block for the specified timeout
        // If the timeout is hit, poll will return 0
        err = ring_buffer__poll(self->m_ringBufferManager, /* timeout_ms */ 100);
        // We might get back an EINTR if the process gets any signal. But in this
        // case we should keep polling. If any of those signals actually means that
        // the process has exited, we are controlling that from WaitForRootProcessToExit,
        // and that thread will set g_stop accordingly
        if (err < 0 && err != -EINTR) {
            self->LogError("[Event ring buffer %d] Error polling ring buffer %d\n", err, self->m_Id);
            break;
        }

        // After the root process exits, make sure we periodically flush the ring buffers to ensure we do not miss any events.
        // Under some circumstances we force a no wake up flag on the ringbuffer (based on ring buffer free space) so there might
        // be a tail of events left unconsumed. On the other hand, we can't just do this once after the pip is done, since in the case
        // of orphaned processes we rely on the proper exit events to reach the syscall handler to determine when we should be done waiting
        if (*(self->m_rootProcessExited)) {
            self->FlushRingBufferEvents();
        }
    }

    return nullptr;
}

int EventRingBuffer::LogError(const char *fmt, ...) {
    va_list args;
    va_start(args, fmt);
    m_bxl->LogErrorArgList(getpid(), fmt, args);
    va_end(args);

    return 1;
}

void EventRingBuffer::LogDebug(const char *fmt, ...) const {
    // If the root process has exited, we should not log debug messages because the FIFO might be closed by the time we try to log it.
    // Not the most elegant solution, but this is *too* easy to forget and logging a debug message after the FIFO is closed hangs the process.
    if (*m_rootProcessExited) {
        return;
    }

    if (m_bxl->LogDebugEnabled())
    {
        va_list args;
        va_start(args, fmt);
        m_bxl->LogDebugMessage(getpid(), buildxl::linux::DebugEventSeverity::kDebug, fmt, args);
        va_end(args);
    }
}

void EventRingBuffer::LogInfo(const char *fmt, ...) const {
    // If the root process has exited, we should not log debug messages because the FIFO might be closed by the time we try to log it.
    // Not the most elegant solution, but this is *too* easy to forget and logging a debug message after the FIFO is closed hangs the process.
    if (*m_rootProcessExited) {
        return;
    }

    va_list args;
    va_start(args, fmt);
    m_bxl->LogDebugMessage(getpid(), buildxl::linux::DebugEventSeverity::kInfo, fmt, args);
    va_end(args);
}

size_t EventRingBuffer::UpdateMinimumRingbufferAvailableSpace() {
    // Compute the minimum available space in the ring buffer for the given runner. This is computed for telemetry purposes.
    size_t min_availability = m_minAvailableSpace.load(std::memory_order_relaxed);
    size_t availability = GetAvailableSpace();
    if (min_availability == -1 || availability < min_availability)
    {
        m_minAvailableSpace.store(availability, std::memory_order_relaxed);
    }

    return availability;
}

void EventRingBuffer::WaitForInactive() {
    LogDebug("[Event ring buffer %d] WaitForInactive()", m_Id);

    // We may have more than one thread waiting for the buffer to be inactive: the grace period thread (from an upper buffer) and the draining overflow thread.
    // The grace period thread for this buffer should only be joined once. So guard the state with a mutex.
    std::lock_guard<std::mutex> lock(m_waitForBufferInactiveMutex);

    if (!m_bufferInactive) {
        // The grace period thread may have not been created yet if a new buffer was created after this one and immediately started
        // waiting for the previous buffer to be inactive before this one is deactivated.
        // In this case, we wait for the grace period thread to be created and then join it.
        while (m_gracePeriodThread == 0) {
            usleep(1000); // Sleep for 1 millisecond to avoid busy waiting
        }

        LogDebug("[Event ring buffer %d] WaitForInactive(): Waiting for grace period thread", m_Id);
        pthread_join(m_gracePeriodThread, NULL);
        assert(m_bufferInactive);
    }

    LogDebug("[Event ring buffer %d] WaitForInactive() done", m_Id);
}


OverflowEventRingBuffer::OverflowEventRingBuffer(
    BxlObserver *bxl, 
    volatile sig_atomic_t *rootProcessExited, 
    buildxl::common::ConcurrentQueue<ebpf_event *> &eventQueue,
    std::function<void(EventRingBuffer *)> capacityExceededCallback,
    EventRingBuffer *previous)
    // The overflow buffer gets an ID that is one larger than the previous buffer's ID.
    // For an overflow ring buffer, the size is always double the previous buffer's size: the multiplier is always 1, since that only affects the first buffer
    : EventRingBuffer(previous->GetId() + 1, bxl, rootProcessExited, &m_localStopSignal, eventQueue, capacityExceededCallback, previous, /* ringBufferSizeMultiplier */ 1),
      m_localStopSignal(0) {
}

OverflowEventRingBuffer::~OverflowEventRingBuffer() {
    // Just being defensive, NotifyDeactivated should be called before the destructor
    m_localStopSignal = 1;
    m_previous = nullptr;
}
    
int OverflowEventRingBuffer::NotifyDeactivated() {
    // Let's lower the priority of the polling thread so it doesn't compete with other potentially active entries.
    // After the buffer is deactivated, we shouldn't need a high priority thread to poll the ring buffer.
    if (RestorePollingThreadPriority())
    {
        return -1;
    }

    if (pthread_create(&m_gracePeriodThread, NULL, OverflowGracePeriodThread, this) != 0) {
        LogError("[Event ring buffer %d] Grace period thread failed to start %s\n", m_Id, strerror(errno));
        return -1;
    }

    LogDebug("[Event ring buffer %d - overflow] NotifyDeactivated()", m_Id);

    return 0;
}

void OverflowEventRingBuffer::Terminate(bool useGracePeriod) {
    if (useGracePeriod) {
        // Wait for the grace period to be over
        usleep(GRACE_PERIOD_MS * 1000);
        LogDebug("[Event ring buffer %d - overflow] Terminate(): grace period is over", m_Id);
    }

    if (useGracePeriod) {
        // Signal the polling thread to stop, wait for it to finish processing events, and then clean up the ring buffer.
        m_localStopSignal = 1;
        pthread_join(m_pollingThread, NULL);
    }
    else
    {
        // If we are not using the grace period, we cancel the polling thread immediately.
        pthread_cancel(m_pollingThread);
        pthread_join(m_pollingThread, NULL);
    }

    LogDebug("[Event ring buffer %d - overflow] Terminate(): polling thread done", m_Id);

    FlushRingBufferEvents();

    // Free the ring buffer manager and free the file access ring buffer
    ring_buffer__free(m_ringBufferManager);
    m_ringBufferManager = nullptr;
    close(m_ringBufferFd);
    m_ringBufferFd = -1;

    auto *previous = GetPrevious();

    LogDebug("[Event ring buffer %d - overflow] Terminate(): waiting for previous buffer %d to be inactive", m_Id, previous->GetId());

    // Wait for the previous buffer to be inactive before moving the events from the overflow queue to the main event queue
    previous->WaitForInactive();

    LogDebug("[Event ring buffer %d - overflow] Terminate(): previous buffer is inactive", m_Id);

    // Wait for the drain overflow thread to finish processing events
    pthread_join(m_drainOverflowThread, NULL);

    LogDebug("[Event ring buffer %d - overflow] Terminate(): previous buffer is inactive [done]", m_Id);

    delete previous;
    m_previous = nullptr;

    LogDebug("[Event ring buffer %d - overflow] Terminate(): previous buffer deleted", m_Id);

    m_bufferInactive = true;

    LogDebug("[Event ring buffer %d - overflow] Terminate(): buffer is inactive", m_Id);
}

void *OverflowEventRingBuffer::OverflowGracePeriodThread(void *arg) {
    auto *self = static_cast<OverflowEventRingBuffer *>(arg);

    self->LogDebug("[Event ring buffer %d - overflow] Grace period thread started", self->m_Id);

    self->Terminate(true);
    
    self->LogDebug("[Event ring buffer %d - overflow] Grace period thread finished", self->m_Id);

    return nullptr;
}

int OverflowEventRingBuffer::NotifyActivated() {
    auto res = EventRingBuffer::NotifyActivated();
    if (res != 0) {
        return res;
    }

    if (pthread_create(&m_drainOverflowThread, NULL, DrainOverflowThread, this) != 0) {
        LogError("[Event ring buffer %d] Draining overflow thread failed to start %s\n", m_Id, strerror(errno));
        return -1;
    }

    return 0;
}

void *OverflowEventRingBuffer::DrainOverflowThread(void *arg) {
    auto *self = static_cast<OverflowEventRingBuffer *>(arg);

    self->LogDebug("[Event ring buffer %d - overflow] Drain overflow thread started", self->m_Id);
    self->GetPrevious()->WaitForInactive();
    auto movedCount = self->m_overflowEventQueue.MoveToAndDeactivate(self->m_eventQueue);
    self->LogDebug("[Event ring buffer %d - overflow] Drain overflow thread done: %d overflow events moved to the main event queue", self->m_Id, movedCount);

    return nullptr;
}

int OverflowEventRingBuffer::SendEventToActiveQueue(void *ctx, void *data, size_t data_sz) {
    auto *self = static_cast<OverflowEventRingBuffer *>(ctx);
    size_t availableSpace = self->UpdateMinimumRingbufferAvailableSpace();
    
    // If we reached the availability threshold, we call the capacity exceeded callback.
    if (availableSpace < self->m_capacityThreshold && !self->m_isCapacityExceededCallbackCalled) 
    {
        self->LogInfo("[Event ring buffer %d] Capacity exceeded, available space: %zu. Threshold: %zu. Calling capacity exceeded callback", self->m_Id, availableSpace, self->m_capacityThreshold);
        self->m_isCapacityExceededCallbackCalled = true;
        // Notify that the buffer capacity has been exceeded
        self->m_capacityExceededCallback(self);
    }

    // Copy event data to local queue to free space from the shared ring buffer for more kernel events.
    ebpf_event *new_event = (ebpf_event *)malloc(data_sz);

    if (!new_event) {
        self->LogError("[Event ring buffer %d] Failed to allocate memory for event\n", self->m_Id);
        return -1;
    }

    memcpy(new_event, data, data_sz);

    if (self->m_inOverflowMode) {
        // If we are in overflow mode, we try to enqueue the event to the overflow queue.
        if (!self->m_overflowEventQueue.Enqueue(new_event)) {
            // If the overflow queue is inactive, we enqueue the event in the main queue.
            self->m_eventQueue.Enqueue(new_event);
            // Switch the mode so we don't try to enqueue to the overflow queue again.
            self->m_inOverflowMode = false;
        }
    }
    else {
        self->m_eventQueue.Enqueue(new_event);
    }

    return 0;
}

} // ebpf
} // linux
} // buildxl
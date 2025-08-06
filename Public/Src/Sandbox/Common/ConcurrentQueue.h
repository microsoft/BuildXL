// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#ifndef __PUBLIC_SRC_SANDBOX_LINUX_EBPF_CONCURRENTQUEUE_H
#define __PUBLIC_SRC_SANDBOX_LINUX_EBPF_CONCURRENTQUEUE_H

#include <queue>
#include <mutex>
#include <condition_variable>
#include <atomic>

namespace buildxl {
namespace common {

/**
 * Multiple Producer Multiple Consumer Queue with a blocking pop.
 */
template<typename T>
class ConcurrentQueue {
public:
    // Default constructor & destructor.
    ConcurrentQueue() = default;
    ~ConcurrentQueue() = default;

    // Deactivate the queue, preventing further enqueue operations.
    void Deactivate()
    {
        std::lock_guard<std::mutex> lock(mtx);
        isActive = false;
    }

    // Move all items from this queue into another queue and deactivate this queue.
    // Returns the number of items moved.
    int MoveToAndDeactivate(ConcurrentQueue<T> &other) {
        std::lock_guard<std::mutex> lock(mtx);
        
        isActive = false;
        auto size = q.size();
        
        while (!q.empty()) {
            other.Enqueue(q.front());
            q.pop();
        }

        return size;
    }

    // Push an item into the queue.
    // Returns true if the item was successfully enqueued, false if the queue is inactive.
    bool Enqueue(const T &item) {
        // Check if the queue is active before enqueueing outside the lock.
        // This is to avoid blocking if the queue is already inactive.
        if (!isActive) {
            // Queue is inactive, do not enqueue.
            return false; 
        }

        {
            std::lock_guard<std::mutex> lock(mtx);
            // Check again inside the lock to ensure thread safety.
            if (!isActive) {
               // Queue is inactive, do not enqueue.
                return false; 
            }
            q.push(item);
        }
        cv.notify_one();
        return true;
    }

    // Blocking pop: waits until an item is available or the queue is inactive.
    bool Dequeue(T &item) {
        std::unique_lock<std::mutex> lock(mtx);
        cv.wait(lock, [this]() {
            return !q.empty() || !isActive;
        });
        
        // Queue is inactive and empty, return false.
        if (!isActive && q.empty()) {
            return false; 
        }

        item = q.front();
        q.pop();
        
        return true;
    }

    // Returns the number of items in the queue.
    int Size() {
        std::lock_guard<std::mutex> lock(mtx);
        return q.size();
    }

private:
    std::queue<T> q;
    std::mutex mtx;
    std::condition_variable cv;
    std::atomic<bool> isActive = true;
};

} // namespace buildxl
} // namespace common

#endif // __PUBLIC_SRC_SANDBOX_LINUX_EBPF_CONCURRENTQUEUE_H
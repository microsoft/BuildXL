// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#ifndef __PUBLIC_SRC_SANDBOX_LINUX_EBPF_SPSCQUEUE_H
#define __PUBLIC_SRC_SANDBOX_LINUX_EBPF_SPSCQUEUE_H

#include <queue>
#include <mutex>
#include <condition_variable>

namespace buildxl {
namespace common {

/**
 * Single Producer Single Consumer Queue with a blocking pop.
 */
template<typename T>
class SPSCQueue {
public:
    // Default constructor & destructor.
    SPSCQueue() = default;
    ~SPSCQueue() = default;

    // Push an item into the queue.
    bool Enqueue(const T &item) {
        {
            std::lock_guard<std::mutex> lock(mtx);
            q.push(item);
        }
        cv.notify_one();
        return true;
    }

    // Blocking pop: waits until an item is available.
    bool Dequeue(T &item) {
        std::unique_lock<std::mutex> lock(mtx);
        cv.wait(lock, [this]() {
            return !q.empty(); 
        });
        
        item = q.front();
        q.pop();
        
        return true;
    }

private:
    std::queue<T> q;
    std::mutex mtx;
    std::condition_variable cv;
};

} // namespace buildxl
} // namespace common

#endif // __PUBLIC_SRC_SANDBOX_LINUX_EBPF_SPSCQUEUE_H
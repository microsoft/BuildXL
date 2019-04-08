// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "Thread.hpp"
#include "BuildXLSandboxShared.hpp"

#define super OSObject

OSDefineMetaClassAndStructors(Thread, OSObject)

Thread* Thread::create(void *funcArgs, thread_continue_t func)
{
    Thread *instance = new Thread;
    if (instance)
    {
        bool initialized = instance->init(funcArgs, func);
        if (!initialized)
        {
            instance->release();
            instance = nullptr;
        }
    }

    if (!instance)
    {
        log_error("%s", "Failed to initialize Thread");
    }

    return instance;
}

bool Thread::init(void *funcArgs, thread_continue_t func)
{
    if (!super::init())
    {
        return false;
    }

    if (func == nullptr)
    {
        return false;
    }

    lock_ = IOLockAlloc();
    if (lock_ == nullptr)
    {
        return false;
    }

    started_     = false;
    finished_    = false;
    runFunc_     = func;
    runFuncArgs_ = funcArgs;

    return true;
}

void Thread::free()
{
    if (lock_ != nullptr)
    {
        IOLockFree(lock_);
        lock_ = nullptr;
    }

    runFunc_     = nullptr;
    runFuncArgs_ = nullptr;

    super::free();
}

void Thread::start()
{
    started_ = true;

    thread_t thr;
    kernel_thread_start([](void *me, wait_result_t result)
                        {
                            static_cast<Thread*>(me)->run(result);
                        },
                        this, &thr);
    thread_deallocate(thr);
}

void Thread::run(wait_result_t result)
{
    tid_ = thread_tid(current_thread());

    log_debug("Thread %lld started", tid_);

    runFunc_(runFuncArgs_, result);

    IOLockLock(lock_);
    {
        finished_ = true;
        IOLockWakeup(lock_, &finished_, false);
    }
    IOLockUnlock(lock_);

    log_debug("Thread %lld exited", tid_);
}

void Thread::join()
{
    IOLockLock(lock_);
    {
        while (!finished_)
        {
            IOLockSleep(lock_, &finished_, THREAD_INTERRUPTIBLE);
        }
    }
    IOLockUnlock(lock_);
}

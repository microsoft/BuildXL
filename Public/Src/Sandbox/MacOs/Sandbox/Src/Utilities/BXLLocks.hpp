// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef BXLLocks_hpp
#define BXLLocks_hpp

#include <IOKit/IOLib.h>

#ifdef DEBUG_LOCKS

/*!
* A wrapper around IOKit locks that makes tesing easier.
*/

class BXLRecursiveLock
{
private:
    
    IORecursiveLock *rLock_;
    
public:
    
    BXLRecursiveLock() = delete;
    
    BXLRecursiveLock(IORecursiveLock *rl)
    {
        rLock_ = rl;
    }
    
    ~BXLRecursiveLock()
    {
        if (rLock_ != nullptr)
        {
            IORecursiveLockFree(rLock_);
        }
    }
    
    inline void Lock()
    {
        if (rLock_ != nullptr)
        {
            IORecursiveLockLock(rLock_);
        }
    }
    
    inline void Unlock()
    {
        if (rLock_ != nullptr)
        {
            IORecursiveLockUnlock(rLock_);
        }
    }
};

static inline BXLRecursiveLock *BXLRecursiveLockAlloc()
{
    IORecursiveLock *rl = IORecursiveLockAlloc();
    if (rl != nullptr)
    {
        return new BXLRecursiveLock(rl);
    }
    
    return nullptr;
}

static inline void BXLRecursiveLockFree(BXLRecursiveLock *rl)
{
    delete rl;
}

static inline void BXLRecursiveLockLock(BXLRecursiveLock *rl)
{
    rl->Lock();
}

static inline void BXLRecursiveLockUnlock(BXLRecursiveLock *rl)
{
    rl->Unlock();
}

class BXLLock
{
private:
    
    IOLock *lock_;
    
public:
    
    BXLLock() = delete;
    
    BXLLock(IOLock *l)
    {
        lock_ = l;
    }
    
    ~BXLLock()
    {
        if (lock_ != nullptr)
        {
            IOLockFree(lock_);
        }
    }
    
    inline void Lock()
    {
        if (lock_ != nullptr)
        {
            IOLockLock(lock_);
        }
    }
    
    inline void Unlock()
    {
        if (lock_ != nullptr)
        {
            IOLockUnlock(lock_);
        }
    }
    
    inline void Sleep(void *event, UInt32 interType)
    {
        if (lock_ != nullptr)
        {
            IOLockSleep(lock_, event, interType);
        }
    }
    
    inline void Wakeup(void *event, bool oneThread)
    {
        if (lock_ != nullptr)
        {
            IOLockWakeup(lock_, event, oneThread);
        }
    }
};

static inline BXLLock *BXLLockAlloc()
{
    IOLock *l = IOLockAlloc();
    if (l != nullptr)
    {
        return new BXLLock(l);
    }
    
    return nullptr;
}

static inline void BXLLockFree(BXLLock *l)
{
    delete l;
}

static inline void BXLLockLock(BXLLock *l)
{
    l->Lock();
}

static inline void BXLLockUnlock(BXLLock *l)
{
    l->Unlock();
}

static inline void BXLLockSleep(BXLLock *l, void *event, UInt32 interType)
{
    l->Sleep(event, interType);
}

static inline void BXLLockWakeup(BXLLock *l, void *event, bool oneThread)
{
    l->Wakeup(event, oneThread);
}

#else

#define BXLRecursiveLock        IORecursiveLock
#define BXLRecursiveLockAlloc   IORecursiveLockAlloc
#define BXLRecursiveLockFree    IORecursiveLockFree
#define BXLRecursiveLockLock    IORecursiveLockLock
#define BXLRecursiveLockUnlock  IORecursiveLockUnlock

#define BXLLock                 IOLock
#define BXLLockAlloc            IOLockAlloc
#define BXLLockFree             IOLockFree
#define BXLLockLock             IOLockLock
#define BXLLockUnlock           IOLockUnlock
#define BXLLockSleep            IOLockSleep
#define BXLLockWakeup           IOLockWakeup

#endif

#endif /* BXLLocks_hpp */

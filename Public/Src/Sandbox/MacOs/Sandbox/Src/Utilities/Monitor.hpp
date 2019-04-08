// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef Monitor_hpp
#define Monitor_hpp

#include <IOKit/IOLib.h>

enum LockKind
{
    kLockRead,
    kLockWrite
};

/*!
 * Can be used to turn instance methods into monitors by allocating a stack variable of this type,
 * passing a lock to its constructor; the constructor acquires the lock while the destructor releases it.
 * Once the stack variable goes out of scope, its destructor is automatically called.
 */
class Monitor
{
private:
    
    IORecursiveLock *lock_;
    IORWLock *rwLock_;
    
public:
    
    /*!
     * Constructor: aquires a given recursive lock
     * @param rwLock Lock to acquire
     */
    Monitor(IORecursiveLock *lock)
    {
        lock_   = lock;
        rwLock_ = nullptr;

        if (lock_)
        {
            IORecursiveLockLock(lock_);
        }
    }
    
    /*!
     * Constructor: aquires a given read-write lock.
     * @param rwLock Lock to acquire
     * @param lockKind Determines whether to acquire read or write
     */
    Monitor(IORWLock *rwLock, LockKind lockKind) : rwLock_(rwLock), lock_(nullptr)
    {
        lock_   = nullptr;
        rwLock_ = rwLock;

        if (rwLock_)
        {
            if (lockKind == kLockRead)
            {
                IORWLockRead(rwLock_);
            }
            else
            {
                IORWLockWrite(rwLock_);
            }
        }
    }
    
    /*!
     * Destructor: releases the lock supplied to the constructor
     */
    ~Monitor()
    {
        if (lock_)
        {
            IORecursiveLockUnlock(lock_);
        }
        
        if (rwLock_)
        {
            IORWLockUnlock(rwLock_);
        }
    }
};

/*
 * Macros for declaring a local variable of type Monitor which aquires 'lock_' on construction
 * and releases it on destruction (automatically when the variable goes out of scope).
 */

#define EnterMonitor      Monitor __monitor_local_var(lock_);
#define EnterReadMonitor  Monitor __monitor_local_var(rwLock_, kLockRead);
#define EnterWriteMonitor Monitor __monitor_local_var(rwLock_, kLockWrite);

#endif /* Monitor_hpp */

//
//  Monitor.hpp
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef Monitor_hpp
#define Monitor_hpp

#include <IOKit/IOLib.h>

/*!
 * Can be used to turn instance methods into monitors by allocating a stack variable of this type,
 * passing an IORecursiveLock to its constructor; the constructor acquires the lock (by calling
 * IORecursiveLockLock, while the destructor releases the lock (by calling IORecursiveLockUnlock).
 * Once a stack variable goes out of scope, its destructor is automatically called.
 */
class Monitor
{
private:
    
    IORecursiveLock *lock_;
    
public:
    
    /*!
     * Constructor: aquires a given recursive lock
     */
    Monitor(IORecursiveLock *lock) : lock_(lock)
    {
        IORecursiveLockLock(lock_);
    }
    
    /*!
     * Destructor: releases the lock supplied to the constructor
     */
    ~Monitor()
    {
        IORecursiveLockUnlock(lock_);
    }
};

/*!
 * Declares a local variable of type Monitor which aquires 'lock_' on construction and
 * releases it on destruction (automatically when the variable goes out of scope).
 */
#define EnterMonitor Monitor __monitor_local_var__(lock_);

#endif /* Monitor_hpp */

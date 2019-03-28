// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef Thread_hpp
#define Thread_hpp

#include <IOKit/IOService.h>
#include <IOKit/IOLib.h>

/*!
 * A thin abstraction around kernel threads.
 */
class Thread : public OSObject
{
    OSDeclareDefaultStructors(Thread);

private:

    IOLock *lock_;
    uint64_t tid_;

    thread_continue_t runFunc_;
    void *runFuncArgs_;

    bool started_;
    bool finished_;

    /*!
     * Initializes this object, following the OSObject pattern.
     *
     * @result True if successful, False otherwise.
     */
    bool init(void *funcArgs, thread_continue_t func);

    void run(wait_result_t result);

protected:

    /*!
     * Releases held resources, following the OSObject pattern.
     */
    void free() override;

public:

    /*! Blocks until this thread completes */
    void join();

    /*! Starts executing the thread (i.e., executing the 'runFunc_`). */
    void start();

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
    static Thread* create(void *funcArgs, thread_continue_t func);
};

#endif /* Thread_hpp */

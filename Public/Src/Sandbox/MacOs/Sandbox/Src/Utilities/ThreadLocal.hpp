// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef ThreadLocal_hpp
#define ThreadLocal_hpp

#include <IOKit/IOService.h>
#include <IOKit/IOLib.h>
#include "Trie.hpp"

#define ThreadLocal BXL_CLASS(ThreadLocal)

/*!
 * A straightforward wrapper around a concurrent dictionary that uses current thread's id as the implicit key.
 */
class ThreadLocal : public OSObject
{
    OSDeclareDefaultStructors(ThreadLocal);

private:

    /* backing dictionary */
    Trie *dict_;

    static uint64_t self_tid()
    {
        return thread_tid(current_thread());
    }

protected:

    /*!
     * Initializes this object, following the OSObject pattern.
     *
     * @result True if successful, False otherwise.
     */
    bool init() override;

    /*!
     * Releases held resources, following the OSObject pattern.
     */
    void free() override;

public:

    /*!
     * @return Number of entries in this collection
     */
    uint getCount() { return dict_->getCount(); }

    /*!
     * @return Number of nodes in the underlying dictionary.
     */
    uint getNodeCount() { return dict_->getNodeCount(); }

    /*!
     * @return Size in bytes of each node in the underlying dictionary.
     */
    uint getNodeSize() { return dict_->getNodeSize(); }

    /*!
     * Associates 'value' with current thread.
     *
     * @param value Value to associate with the current thread.
     * @result is True when no previous value was associated with the current thread
     *         and False when an existing value is updated to point to the new value.
     */
    bool insert(const OSObject *value);

    /*!
     * Removes the value currently associated with the current thread (if any).
     *
     * @result is True when there was a previous value was associated with the current
     *         thread and False when there wasn't.
     */
    bool remove();

    /*!
     * @result the value currently associated with the current thread (if any).
     */
    OSObject* get() const;
    
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
    static ThreadLocal* create();
};

#endif /* ThreadLocal_hpp */

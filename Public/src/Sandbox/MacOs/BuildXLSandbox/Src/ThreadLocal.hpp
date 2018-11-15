//
//  ConcurrentDictionary.hpp
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef ThreadLocal_hpp
#define ThreadLocal_hpp

#include <IOKit/IOService.h>
#include <IOKit/IOLib.h>
#include "ConcurrentDictionary.hpp"

/*!
 * A straightforward wrapper around ConcurrentDictionary that uses current thread's id as the implicit key.
 */
class ThreadLocal : public OSObject
{
    OSDeclareDefaultStructors(ThreadLocal);
    
private:

    /* backing dictionary */
    ConcurrentDictionary *dict_;
    
public:

    /*!
     * Initializes this object, following the OSObject pattern.
     *
     * @result True if successful, False otherwise.
     */
    bool init(uint capacity, const char *name);
    
    /*!
     * Releases held resources, following the OSObject pattern.
     */
    void free() override;
    
    /*!
     * @return Number of entries in this collection
     */
    uint getCount();
    
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
    static ThreadLocal* withCapacity(uint capacity, const char *name);
};

#endif /* ThreadLocal_hpp */

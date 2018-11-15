//
//  ConcurrentDictionary.hpp
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef ConcurrentProcessDictionary_hpp
#define ConcurrentProcessDictionary_hpp

#include <IOKit/IOService.h>
#include <IOKit/IOLib.h>
#include "Monitor.hpp"

typedef void (*for_each_fn)(void *data, const int index, const OSSymbol *key, const OSObject *value);

// forward declaration
class ProcessObject;

/*!
 * A straightforward wrapper around OSDictionary to provide a thread-safe implementation of a dictionary.
 */
class ConcurrentDictionary : public OSObject
{
    OSDeclareDefaultStructors(ConcurrentDictionary);

private:

    /*! Arbitrary name for descriptive purposes */
    const char *name_;

    /*! Backing dictinoary */
    OSDictionary *dict_;

    /*!
     * RW lock used for synchronization.
     *
     * IMPORTANT: this lock is not recursive!
     */
    IORWLock *rwLock_;

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
     * Invokes a given callback for every given entry in this dictionary.
     * The entire process is done in a single monitor.
     *
     * @param data Arbitrary pointer passed to 'callback'
     * @param callback Callback function which for each entry in this dictionary receives
     *                 (1) data, (2) const OSSymbol *key, and (3) const OSMetaClassBase *value.
     */
    void forEach(void *data, for_each_fn callback);

#pragma mark Inline Methods

    /*!
     * @result Number of entries in this dictionary
     */
    uint getCount() const { return dict_->getCount(); }

    /*!
     * Same as 'get' except the result is cast to T (using OSDynamicCast)
     */
    template <class T> T* getAs(const OSSymbol *key) const { return OSDynamicCast(T, get(key)); }

#pragma mark Generic insert/remove/get Methods

    /*!
     * Thread-safe version of OSDictionary::setObject
     *
     * @result is True when a new entry is inserted (i.e., no entry for key 'key' existed)
     *         and False when an existing entry is updated to point to the new value.
     */
    bool insert(const OSSymbol *key, const OSObject *value);

    /*!
     * Thread-safe version of OSDictionary::removeObject
     *
     * @result is True when an entry is removed (i.e., an entry for key 'key' is found and removed)
     *         and False when no existing entry is found for the given key.
     */
    bool remove(const OSSymbol *key);

    /*! Thread-safe version of OSDictionary::flushCollection. */
    void flushCollection();

    /*! Thread-safe version of OSDictionary::getObject */
    OSObject* get(const OSSymbol *key) const;

#pragma mark Convenience Methods for inserting/removing/getting ProcessObject objects

    /*! Uses process->getHashCode() as key, then calls 'insert' */
    bool insertProcess(const ProcessObject *process);

    /*! Uses ProcessObject::computePidHashCode as key, then calls 'remove' */
    bool removeProcess(pid_t pid);

    /*! Looks up a ProcessObject for process 'pid' */
    ProcessObject* getProcess(pid_t pid);

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
    static ConcurrentDictionary* withCapacity(uint capacity, const char *name);
};

#endif /* ConcurrentProcessDictionary_hpp */

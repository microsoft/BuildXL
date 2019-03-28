// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef ConcurrentProcessDictionary_hpp
#define ConcurrentProcessDictionary_hpp

#include <IOKit/IOService.h>
#include <IOKit/IOLib.h>
#include "Monitor.hpp"

typedef void (*for_each_fn)(void *data, const int index, const OSSymbol *key, const OSObject *value);
typedef void (*on_change_fn)(void *data, const int oldCount, const int newCount);

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
    
    /*! Callback function (and associated payload) to call whenever count changes. */
    void *onChangeData_;
    on_change_fn onChangeCallback_;
    
    /*! Invokes the 'onChangeCallback_' if it's set and 'newCount' is different from 'oldCount' */
    void TriggerOnChange(int oldCount, int newCount) const;

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
     * @result Number of entries in this dictionary
     */
    uint getCount() const;

    /*!
     * Invokes a given callback for every given entry in this dictionary.
     * The entire process is done in a single monitor.
     *
     * @param data Arbitrary pointer passed to 'callback'
     * @param callback Callback function which for each entry in this dictionary receives
     *                 (1) data, (2) const OSSymbol *key, and (3) const OSMetaClassBase *value.
     */
    void forEach(void *data, for_each_fn callback) const;
    
    /*!
     * Installs a callback to be called whenever the count changes.
     *
     * This method may only be called once, i.e., multiple callbacks are not supported.
     *
     * @result Indicates whether the callback was successfully installed.
     */
    bool onChange(void *data, on_change_fn callback);

#pragma mark Inline Methods

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

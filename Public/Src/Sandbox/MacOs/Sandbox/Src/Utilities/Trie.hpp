// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef Trie_hpp
#define Trie_hpp

#include "TrieNode.hpp"
#include "SysCtl.hpp"

#define Trie BXL_CLASS(Trie)

/*!
 * A thread-safe dictionary, implementated as a trie tree.
 *
 * Only 2 types of keys are allowed: (1) an unsigned integer, and (2) an ascii path.
 *
 * Additionally, two different implementations are provided: fast and light.  The former
 * is lock-free and fast but has a potentially huge memory footprint; the latter has a
 * much smaller memory footprint, is not lock-free, but still has good performance.
 *
 * Each node in a tree can be assigned a record which must be a pointer to an arbitrary OSObject.
 * Once an OSObject is added to a trie, it is automatically retained by the trie; once it is removed,
 * it is automatically released by this trie; this is analogous to how OSDictionary works.
 *
 * Paths are considered case-insensitive.  Attempting to add a path with a non-ascii
 * character will fail gracefully by returning 'kTrieResultFailure'.
 *
 * Thread-safe.
 */
class Trie : public OSObject
{
    OSDeclareDefaultStructors(Trie);

public:

    typedef OSObject* (*factory_fn)(void *data);
    typedef void (*on_change_fn)(void *data, const int oldCount, const int newCount);
    typedef void (*for_each_fn)(void *data, uint64_t key, const OSObject *value);
    typedef bool (*filter_fn)(void *data, const OSObject *value);

    typedef enum {
        kTrieResultInserted,
        kTrieResultReplaced,
        kTrieResultRemoved,
        kTrieResultAlreadyEmpty,
        kTrieResultAlreadyExists,
        kTrieResultRace,
        kTrieResultFailure,
    } TrieResult;

private:

    const uint kKindBitMask = 1;
    const uint kImplBitMask = 1 << 1;

    typedef enum { kUintTrie = 0, kPathTrie = 1 } TrieKind;
    typedef enum { kFastTrie = 0, kLightTrie = 1 } TrieImpl;

    uint mergeKindAndImpl(TrieKind knd, TrieImpl impl) { return kKindBitMask * knd + kImplBitMask * impl; }

    bool isUintTrie()  { return (kind_ & kKindBitMask) == kUintTrie; }
    bool isPathTrie()  { return !isUintTrie(); }
    bool isFastTrie()  { return (kind_ & kImplBitMask) == kFastTrie; }
    bool isLightTrie() { return !isFastTrie(); }

    /*! The root of the tree. */
    Node *root_;

    /*! Encodes the kind (see 'TreeKind') and implementation (see 'TreeImpl') */
    uint kind_;

    /*! The size of the tree (i.e., number of records stored) and not the number of nodes in the tree. */
    uint size_;

    /*! Callback function to call whenever the size of the tree changes. */
    on_change_fn onChangeCallback_;

    /*! Payload for the 'onChangeCallback_' function */
    void *onChangeData_;

    /*! Used only when modifying a node's list of children */
    IORecursiveLock *lock_;

    /*! Initialized a new Trie.  The return value indicates the success of the operation. */
    bool init(TrieKind kind);

    /*! Invokes the 'onChangeCallback_' if it's set and 'newCount' is different from 'oldCount' */
    void triggerOnChange(int oldCount, int newCount) const;

    /*!
     * Ensures that 'node' has its 'record_' field set to a non-null value.
     * If not already set, uses the 'factory' function to create a new value and assign it to the 'record_' field.
     *
     * @param node The node that must become a sentinel.
     * @param factoryArgs Arguments to pass to the 'factory' function
     * @param factory Function to call to create a record to assign to 'node' in the case when no record has already been assigned.
     * @result
     *    - kTrieResultAlreadyExists : if 'node' already has a record (MUST NOT assume that 'factory' wasn't called in this case)
     *    - kTrieResultInserted      : if a new record was created and assigned to 'node' (SAFE to assume that 'factory' was called).
     */
    TrieResult makeSentinel(Node *node, void *factoryArgs, factory_fn factory);

    /*!
     * Returns the record already assigned to 'node' or, if no record is assigned to it, creates a new one by invoking
     * 'factory', assigns it to 'node', and returns it.
     *
     * IMPORTANT: The caller must not assume that 'factory' is invoked IFF its return value is assigned to 'node'.
     *            It is possible that 'factory' is invoked, but due to a race some other record gets assigned to this
     *            node first; in that case, the OSObject returned by 'factory' is released and simply ignored.
     *
     * @result The record associated with 'node' or NULL if 'node' is NULL.
     */
    OSObject* getOrAdd(Node *node, void *factoryArgs, factory_fn factory, TrieResult *result);

    /*!
     * Returns the record associated with 'node' or NULL if either 'node' is NULL or no record is associated with it.
     */
    OSObject* get(Node *node);

    /*!
     * Attempts to associate 'value' with 'node', even if there is already a value associated with 'node'.
     *
     * If either 'node' or 'value' is NULL, the result is 'kTrieResultFailure'.
     *
     * If 'value' has been associated with 'node' and there wasn't a record previously associated with 'node':
     * retains 'value', increments size, and returns 'kTrieResultInserted'.
     *
     * If 'value' has been associated with 'node' and there was a record previously associated with 'node':
     * retains 'value', releases the previous record, and returns 'kTrieResultReplaced'.
     *
     * If 'value' has not beed associated with 'node' because there was a race (i.e., someone else associated a
     * different record first), it does nothing and returns 'kTrieResultRace'.  The caller should decide whether to
     * retry or accept the existing outcome.
     *
     * @result kTrieResultInserted, kTrieResultReplaced, kTrieResultRace, or kTrieResultFailure
     */
    TrieResult replace(Node *node, const OSObject *value);

    /*!
     * Attempts to associate 'value' with 'node', ONLY if no value is already associated with 'node'.
     *
     * If either 'node' or 'value' is NULL, the result is 'kTrieResultFailure'.
     *
     * @result kTrieResultInserted, kTrieResultAlreadyExists, or kTrieResultFailure
     */
    TrieResult insert(Node *node, const OSObject *value);

    /*!
     * Attempts to remove any record currently associated with 'node'.
     *
     * If 'node' is NULL, returns 'kTrieResultFailure'.
     *
     * If no record is currently associated with 'node', returns 'kTrieResultAlreadyEmpty'.
     *
     * If there is a record currently associated with 'node', it releases that record, decrements the size, and
     * returns 'kTrieResultRemoved'.
     *
     * If there is a record currently associated with 'node' but it gets changed in the middle of this method,
     * this method simply returns 'kTrieResultRace'; the caller should decide whether to retry the operation or accept
     * the existing outcome.
     */
    TrieResult remove(Node *node);

    /*! Calls 'callback' for every node in the trie during a pre-order traversal. */
    void traverse(bool computeKey, void *callbackArgs, traverse_fn callback);

    /*!
     * When 'createIfMissing' is true:
     *   traverses the trie until it gets to the node corresponding to the given 'key', creating new nodes as necessary
     * else:
     *   returns the node corresponding to the given 'key' IFF such node already exists, or NULL otherwise.
     */
    Node* findUintNode(uint64_t key, bool createIfMissing);

    /*! Calls 'findUintNode' with 'createIfMissing' set to true. */
    Node* findOrCreateNodeForUint(uint64_t key) { return findUintNode(key, true); }

    /*! Calls 'findUintNode' with 'createIfMissing' set to false. */
    Node* findExistingNodeForUint(uint64_t key) { return findUintNode(key, false); }

    /*!
     * When 'createIfMissing' is true:
     *   traverses the trie until it gets to the node corresponding to the given 'key', creating new nodes as necessary
     * else:
     *   returns the node corresponding to the given 'key' IFF such node already exists, or NULL otherwise.
     *
     * NULL is also returned when the key is invalid (contains non-ascii characters) or the system is out of memory.
     */
    Node* findPathNode(const char *key, bool createIfMissing);

    /*! Calls 'findPathNode' with 'createIfMissing' set to true. */
    Node* findOrCreateNodeForPath(const char *key) { return findPathNode(key, true); }

    /*! Calls 'findPathNode' with 'createIfMissing' set to false. */
    Node* findExistingNodeForPath(const char *key) { return findPathNode(key, false); }

    /*! Creates either a Uint or a Path node, based on the kind of this trie. */
    Node* createNode(uint key)
    {
        return isLightTrie() ? (Node*)NodeLight::create(key) :
               isUintTrie()  ? (Node*)NodeFast::createUintNode() :
               isPathTrie()  ? (Node*)NodeFast::createPathNode() :
               nullptr;
    }

    /*!
     * Static factory method.  The caller is responsible for releasing it by calling 'release()'.
     */
    static Trie* create(TrieKind kind);

protected:
    void free() override;

public:

    /*!
     * Returns the size of the tree (i.e., the number of values stored).
     */
    inline uint getCount() { return size_; }

    /*!
     * Callback to be invoked every time the size of this tree changes.
     */
    bool onChange(void *callbackArgs, on_change_fn callback);

    /*!
     * Invokes a given callback for every entry in this dictionary.
     *
     * @param callbackArgs Arbitrary pointer passed to 'callback'
     * @param callback Callback function to call for each entry in this dictionary
     */
    void forEach(void *callbackArgs, for_each_fn callback);

    /*!
     * Removes all the entries matching a given filter.
     */
    void removeMatching(void *filterArgs, filter_fn filter);

#pragma mark Methods for 'path' keys

    OSObject* get(const char *path)
    {
        return get(findExistingNodeForPath(path));
    }

    template<typename T>
    T* getAs(const char *key)
    {
        return OSDynamicCast(T, get(key));
    }

    /*!
     * If 'path' hasn't been seen before: creates a new value (using the supplied factory function),
     * associates it with 'path', and returns it; otherwise, returns the 'OSObject' object previously
     * associated with 'path'.
     *
     * Paths are considered case-insensitive.
     *
     * NOTE: The current implementation only paths containig only ASCII characters; for all other paths
     *       nullptr is returned indicating that the path couldn't be added.
     */
    OSObject* getOrAdd(const char *path, void *factoryArgs, factory_fn factory, TrieResult *result = nullptr)
    {
        return getOrAdd(findOrCreateNodeForPath(path), factoryArgs, factory, result);
    }

    TrieResult replace(const char *path, const OSObject *value)
    {
        return replace(findOrCreateNodeForPath(path), value);
    }

    TrieResult insert(const char *path, const OSObject *value)
    {
        return insert(findOrCreateNodeForPath(path), value);
    }

    TrieResult remove(const char *key)
    {
        return remove(findExistingNodeForPath(key));
    }

#pragma mark Methods for 'uint' keys

    OSObject* get(uint64_t key)
    {
        return get(findExistingNodeForUint(key));
    }

    template<typename T>
    T* getAs(uint64_t key)
    {
        return OSDynamicCast(T, get(key));
    }

    OSObject* getOrAdd(uint64_t key, void *factoryArgs, factory_fn factory, TrieResult *result = nullptr)
    {
        return getOrAdd(findOrCreateNodeForUint(key), factoryArgs, factory, result);
    }

    TrieResult replace(uint64_t key, const OSObject *value)
    {
        return replace(findOrCreateNodeForUint(key), value);
    }

    TrieResult insert(uint64_t key, const OSObject *value)
    {
        return insert(findOrCreateNodeForUint(key), value);
    }

    TrieResult remove(uint64_t key)
    {
        return remove(findExistingNodeForUint(key));
    }

#pragma mark Static factory methods

    static Trie* createUintTrie() { return create(kUintTrie); }
    static Trie* createPathTrie() { return create(kPathTrie); }
};

#endif /* Trie_hpp */

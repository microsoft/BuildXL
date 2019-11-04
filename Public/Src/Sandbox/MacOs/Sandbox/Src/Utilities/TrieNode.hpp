// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef TrieNode_hpp
#define TrieNode_hpp

#include <IOKit/IOLib.h>
#include <IOKit/IOService.h>
#include "BuildXLSandboxShared.hpp"

#define Node BXL_CLASS(Node)
#define NodeLight BXL_CLASS(NodeLight)
#define NodeFast BXL_CLASS(NodeFast)
#define Trie BXL_CLASS(Trie)

class Node;
typedef void (*traverse_fn)(void*, uint64_t key, Node*);

/*!
 * A node in a Trie.
 * Only accessible to its friend class Trie.
 */
class Node : public OSObject
{
    OSDeclareAbstractStructors(Node);

    friend class Trie;

protected:

    static uint s_numUintNodes;
    static uint s_numPathNodes;

    /*!
     * The value 65 is chosen so that all ASCII characters between 32 (' ') and 122 ('z')
     * get a unique entry in the 'children_' array.  The formula for mapping a character
     * ch to an array index is:
     *
     *   toupper(ch) - 32
     */
    static const uint s_pathNodeMaxKey = 65;

    /*! For 10 digits */
    static const uint s_uintNodeMaxKey = 10;

    /*! Arbitrary value */
    OSObject *record_;

    /*!
     * Checks if a child node of 'node' exists at position 'idx'.
     * If no such child node exists and 'createIfMissing' is true,
     * a new child node is created and saved at position 'idx'.
     *
     * @param key Must be between 0 (inclusive) and 'node.maxKey_' (exclusive); otherwise this method returns NULL
     * @param createIfMissing When true, this method creates a new child node at position 'idx' if one doesn't already exist.
     * @param lock A lock unique to the parent tree to use when needed.
     * @result True IFF this node contains a child node with key 'key' after this method returns.
     */
    virtual Node* findChild(uint key, bool createIfMissing, IORecursiveLock *lock, bool *outNewNodeCreated) = 0;

    /*! Calls 'callback' for every node in the tree rooted in this node (the traversal is pre-order) */
    virtual void traverse(bool computeKey, void *callbackArgs, traverse_fn callback) = 0;

    bool init() override
    {
        record_ = nullptr;
        return OSObject::init();
    }

    void free() override
    {
        OSSafeReleaseNULL(record_);
        OSObject::free();
    }
};

/* =================== class NodeLight ====================== */

class NodeLight : public Node
{
    OSDeclareDefaultStructors(NodeLight);

private:
    /*! The key by which the parent can find this node */
    uint key_;

    /*! Pointer to the next sibling. */
    NodeLight *next_;

    /*! Pointer to the first child node */
    NodeLight *children_;

    NodeLight* findChild(uint key,
                         bool createIfMissing,
                         IORecursiveLock *maybeNulllock,
                         IORecursiveLock *nonNullLock,
                         bool *outNewNodeCreated);

    bool init(uint key);

public:

    static NodeLight* create(uint key);

protected:

    Node* findChild(uint key, bool createIfMissing, IORecursiveLock *lock, bool *outNewNodeCreated) override
    {
        return findChild(key, createIfMissing, nullptr, lock, outNewNodeCreated);
    }

    void traverse(bool computeKey, void *callbackArgs, traverse_fn callback) override;

    void free() override;
};

/* =================== class NodeFast ====================== */

class NodeFast : public Node
{
    OSDeclareDefaultStructors(NodeFast);

private:
    /*! The length of the 'children_' array (i.e., the the number of allocated nodes) */
    uint childrenLength_;

    /*! Pre-allocated pointers to all possible children nodes. */
    NodeFast **children_;

    uint length()     const { return childrenLength_; }
    NodeFast** children() const { return children_; }

    bool init(uint key);

    static NodeFast* create(uint numChildren);

public:

    static NodeFast* createUintNode() { return create(s_uintNodeMaxKey); }
    static NodeFast* createPathNode() { return create(s_pathNodeMaxKey); }

protected:

    Node* findChild(uint key, bool createIfMissing, IORecursiveLock *lock, bool *outNewNodeCreated) override;
    void traverse(bool computeKey, void *callbackArgs, traverse_fn callback) override;
    void free() override;
};

#endif /* TrieNode_hpp */

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef TrieNode_hpp
#define TrieNode_hpp

#include <IOKit/IOLib.h>
#include <IOKit/IOService.h>
#include "BuildXLSandboxShared.hpp"

#define Node BXL_CLASS(Node)
#define Trie BXL_CLASS(Trie)

class Node;

typedef void (*traverse_fn)(void*, uint64_t key, Node*);

/*!
 * A node in a Trie.
 * Only accessible to its friend class Trie.
 */
class Node : public OSObject
{
    OSDeclareDefaultStructors(Node);

private:

    friend class Trie;

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

    /*! The length of the 'children_' array (i.e., the the number of allocated nodes) */
    uint maxKey_;

    /*! The key by which the parent can find this node */
    uint key_;

    /*! Used only when modifying this node's list of children */
    IORecursiveLock *lock_;

    /*! Pointer to the next sibling. */
    Node *next_;

    /*! Pointer to the first child node */
    Node *children_;

    /*!
     * Checks if a child node of 'node' exists at position 'idx'.
     * If no such child node exists and 'createIfMissing' is true,
     * a new child node is created and saved at position 'idx'.
     *
     * @param key Must be between 0 (inclusive) and 'node.maxKey_' (exclusive); otherwise this method returns NULL
     * @param createIfMissing When true, this method creates a new child node at position 'idx' if one doesn't already exist.
     * @result True IFF this node contains a child node with key 'key' after this method returns.
     */
    Node* findChild(uint key, bool createIfMissing, IORecursiveLock *lock = nullptr);

    bool init(uint numChildren, uint key);
    static Node* create(uint numChildren, uint key);

    static Node* createUintNode(uint key) { return create(s_uintNodeMaxKey, key); }
    static Node* createPathNode(uint key) { return create(s_pathNodeMaxKey, key); }

    /*! Calls 'callback' for every node in the trie during a pre-order traversal. */
    void traverse(bool computeKey, void *callbackArgs, traverse_fn callback);

protected:

    void free() override;
};

#endif /* TrieNode_hpp */

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "Monitor.hpp"
#include "TrieNode.hpp"

#define super OSObject

OSDefineMetaClassAndStructors(Node, OSObject)

uint Node::s_numUintNodes = 0;
uint Node::s_numPathNodes = 0;

Node* Node::create(uint maxKey, uint key)
{
    Node *instance = new Node;
    if (instance != nullptr)
    {
        if (maxKey == s_uintNodeMaxKey)      OSIncrementAtomic(&s_numUintNodes);
        else if (maxKey == s_pathNodeMaxKey) OSIncrementAtomic(&s_numPathNodes);

        if (!instance->init(maxKey, key))
        {
            OSSafeReleaseNULL(instance);
        }
    }

    return instance;
}

bool Node::init(uint maxKey, uint key)
{
    if (!super::init())
    {
        return false;
    }

    assert(key < maxKey);

    key_    = key;
    maxKey_ = maxKey;
    record_ = nullptr;

    next_     = nullptr;
    children_ = nullptr;

    lock_ = IORecursiveLockAlloc();
    if (!lock_)
    {
        return false;
    }

    return true;
}

void Node::free()
{
    // intentionally not calling OSSafeReleaseNULL on children_ and next_
    // because Trie is responsible for releasing all its nodes
    next_ = nullptr;
    children_ = nullptr;

    OSSafeReleaseNULL(record_);
    IORecursiveLockFree(lock_);

    if (maxKey_ == s_uintNodeMaxKey)      OSDecrementAtomic(&s_numUintNodes);
    else if (maxKey_ == s_pathNodeMaxKey) OSDecrementAtomic(&s_numPathNodes);

    super::free();
}

Node* Node::findChild(uint key, bool createIfMissing, IORecursiveLock *lock)
{
    if (key < 0 || key >= maxKey_)
    {
        return nullptr;
    }

    Monitor __monitor(lock); // this will only acquire the lock if lock is not null

    Node *prev = nullptr;
    Node *curr = children_;
    while (curr != nullptr && curr->key_ != key)
    {
        prev = curr;
        curr = curr->next_;
    }

    if (curr != nullptr)
    {
        // found it
        assert(curr->key_ == key);
        return curr;
    }
    else if (!createIfMissing)
    {
        // didn't find it and shouldn't create it
        return nullptr;
    }
    else if (lock == nullptr)
    {
        // didn't find it and didn't acquire lock --> must do it all over again with a lock
        return findChild(key, createIfMissing, lock_);
    }
    else
    {
        // didn't find it and we are holding the lock -> create a new node and link it
        Node *newNode = Node::create(maxKey_, key);
        if (prev != nullptr)
        {
            prev->next_ = newNode;
        }
        else
        {
            assert(children_ == nullptr);
            children_ = newNode;
        }
        return newNode;
    }
}

typedef struct Stack {
    Node *node;
    Stack *next;
    uint32_t depth;
    uint64_t key;
} Stack;

static void push(Stack **stack, Node *node, uint64_t path, uint32_t depth)
{
    if (node == nullptr) return;

    Stack *top = IONew(Stack, 1);
    top->node  = node;
    top->next  = *stack;
    top->key   = path;
    top->depth = depth;

    *stack = top;
}

static Node* pop(Stack **stack)
{
    Stack *top = *stack;
    *stack = top->next;
    Node *node = top->node;
    IODelete(top, Stack, 1);
    return node;
}

static uint64_t s_pow10[] =
{
    1,
    10,
    100,
    1000,
    10000,
    100000,
    1000000,
    10000000,
    100000000,
    1000000000,
    10000000000,
    100000000000,
    1000000000000
};

static int s_powLen = sizeof(s_pow10)/sizeof(s_pow10[0]);

static uint64_t pow10(int exp)
{
    if (exp < s_powLen) return s_pow10[exp];
    uint64_t result = 1;
    while (--exp >= 0) result *= 10;
    return result;
}

void Node::traverse(bool computeKey, void *callbackArgs, traverse_fn callback)
{
    Stack *stack = nullptr;
    push(&stack, this, /*key*/ 0, /*depth*/ 0);
    while (stack != nullptr)
    {
        uint64_t key = stack->key;
        uint32_t depth = stack->depth;

        Node *toVisit = pop(&stack);
        Node *curr = toVisit->children_;
        while (curr)
        {
            push(&stack, curr, computeKey ? (curr->key_ * pow10(depth) + key) : 0, depth + 1);
            curr = curr->next_;
        }

        // the callback may deallocate 'curr' node, hence this must be the last statement in this loop
        callback(callbackArgs, key, toVisit);
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "Monitor.hpp"
#include "TrieNode.hpp"

OSDefineMetaClassAndStructors(Node, OSObject)
OSDefineMetaClassAndStructors(NodeLight, Node)

uint Node::s_numUintNodes = 0;
uint Node::s_numPathNodes = 0;
uint Node::s_numLightNodes = 0;

bool Node::init()
{
    if (!OSObject::init())
    {
        return false;
    }

    record_ = nullptr;
    return true;
}

NodeLight* NodeLight::create(uint maxKey, uint key)
{
    NodeLight *instance = new NodeLight;
    if (instance == nullptr)
    {
        goto error;
    }

    if (!instance->init(maxKey, key))
    {
        goto error;
    }

    OSIncrementAtomic(&s_numLightNodes);
    return instance;

error:
    OSSafeReleaseNULL(instance);
    return nullptr;
}

bool NodeLight::init(uint maxKey, uint key)
{
    if (!Node::init())
    {
        return false;
    }

    key_    = key;
    maxKey_ = maxKey;

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
    OSSafeReleaseNULL(record_);
    OSObject::free();
}

void NodeLight::free()
{
    // intentionally not calling OSSafeReleaseNULL on children_ and next_
    // because Trie is responsible for releasing all its nodes
    next_ = nullptr;
    children_ = nullptr;

    OSDecrementAtomic(&s_numLightNodes);

    Node::free();
}

NodeLight* NodeLight::findChild(uint key, bool createIfMissing, IORecursiveLock *lock)
{
    Monitor __monitor(lock); // this will only acquire the lock if lock is not null

    NodeLight *prev = nullptr;
    NodeLight *curr = children_;
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
        NodeLight *newNode = NodeLight::create(maxKey_, key);
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

void NodeLight::traverse(bool computeKey, void *callbackArgs, traverse_fn callback)
{
    Stack *stack = nullptr;
    push(&stack, this, /*key*/ 0, /*depth*/ 0);
    while (stack != nullptr)
    {
        uint64_t key = stack->key;
        uint32_t depth = stack->depth;

        NodeLight *toVisit = (NodeLight*)pop(&stack);
        NodeLight *curr = toVisit->children_;
        while (curr)
        {
            push(&stack, curr, computeKey ? (curr->key_ * pow10(depth) + key) : 0, depth + 1);
            curr = curr->next_;
        }

        // the callback may deallocate 'curr' node, hence this must be the last statement in this loop
        callback(callbackArgs, key, toVisit);
    }
}

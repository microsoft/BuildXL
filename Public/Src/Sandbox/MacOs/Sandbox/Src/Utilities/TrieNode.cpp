// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "Monitor.hpp"
#include "TrieNode.hpp"

OSDefineMetaClassAndStructors(Node, OSObject)
OSDefineMetaClassAndStructors(NodeLight, Node)
OSDefineMetaClassAndStructors(NodeFast, Node)

uint Node::s_numUintNodes = 0;
uint Node::s_numPathNodes = 0;
uint Node::s_numLightNodes = 0;

NodeLight* NodeLight::create(uint key)
{
    NodeLight *instance = new NodeLight;
    if (instance == nullptr)
    {
        goto error;
    }

    if (!instance->init(key))
    {
        goto error;
    }

    OSIncrementAtomic(&s_numLightNodes);
    return instance;

error:
    OSSafeReleaseNULL(instance);
    return nullptr;
}

NodeFast* NodeFast::create(uint numChildren)
{
    NodeFast *instance = new NodeFast;
    if (instance == nullptr)
    {
        goto error;
    }

    if (!instance->init(numChildren))
    {
        goto error;
    }

    if (numChildren == s_uintNodeMaxKey)      OSIncrementAtomic(&s_numUintNodes);
    else if (numChildren == s_pathNodeMaxKey) OSIncrementAtomic(&s_numPathNodes);
    return instance;

error:
    OSSafeReleaseNULL(instance);
    return nullptr;
}

bool Node::init()
{
    if (!OSObject::init())
    {
        return false;
    }

    record_ = nullptr;
    return true;
}

bool NodeLight::init(uint key)
{
    if (!Node::init())
    {
        return false;
    }

    key_    = key;

    next_     = nullptr;
    children_ = nullptr;

    lock_ = IORecursiveLockAlloc();
    if (!lock_)
    {
        return false;
    }

    return true;
}

bool NodeFast::init(uint numChildren)
{
    if (!Node::init())
    {
        return false;
    }

    childrenLength_ = numChildren;
    children_ = IONew(NodeFast*, numChildren);

    for (int i = 0; i < childrenLength_; i++)
    {
        children_[i] = nullptr;
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

void NodeFast::free()
{
    for (int i = 0; i < childrenLength_; i++)
    {
        children_[i] = nullptr;
    }

    IODelete(children_, NodeFast*, childrenLength_);
    children_ = nullptr;

    if (length() == s_uintNodeMaxKey)      OSDecrementAtomic(&s_numUintNodes);
    else if (length() == s_pathNodeMaxKey) OSDecrementAtomic(&s_numPathNodes);

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
        NodeLight *newNode = NodeLight::create(key);
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

Node* NodeFast::findChild(uint key, bool createIfMissing)
{
    if (key < 0 || key >= length())
    {
        return nullptr;
    }

    NodeFast *childNode = children()[key];
    if (childNode != nullptr)
    {
        return childNode;
    }

    // child is missing
    if (!createIfMissing)
    {
        return nullptr;
    }

    NodeFast* newNode = NodeFast::create(length());

    // This should never happen except if we run out of memory.
    if (newNode == nullptr)
    {
        return nullptr;
    }

    if (!OSCompareAndSwapPtr(nullptr, newNode, &children()[key]))
    {
        // someone else created this child node before us --> release 'newNode' that we created for nothing
        OSSafeReleaseNULL(newNode);
    }

    return children()[key];
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

void NodeFast::traverse(bool computeKey, void *callbackArgs, traverse_fn callback)
{
    Stack *stack = nullptr;
    push(&stack, this, /*key*/ 0, /*depth*/ 0);
    while (stack != nullptr)
    {
        uint64_t key = stack->key;
        uint32_t depth = stack->depth;

        NodeFast *curr = (NodeFast*)pop(&stack);
        for (int i = 0; i < curr->length(); ++i)
        {
            push(&stack, curr->children()[i], computeKey ? (i * pow10(depth) + key) : 0, depth + 1);
        }

        // the callback may deallocate 'curr' node, hence this must be the last statement in this loop
        callback(callbackArgs, key, curr);
    }
}

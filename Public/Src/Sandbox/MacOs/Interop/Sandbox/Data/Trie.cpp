// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <limits.h>

#include "BuildXLException.hpp"
#include "Trie.hpp"
#include "SandboxedProcess.hpp"

template <typename T>
_Atomic uint Node<T>::s_numUintNodes = 0;

template <typename T>
_Atomic uint Node<T>::s_numPathNodes = 0;

template <typename T>
Node<T>::Node(uint numChildren)
{
    if (numChildren == s_uintNodeChildrenCount) ++s_numUintNodes;
    else if (numChildren == s_pathNodeChildrenCount) ++s_numPathNodes;

    record_ = nullptr;
    childrenLength_ = numChildren;
    
    children_ = (Node **) malloc(numChildren * sizeof(Node *));
    if (children_)
    {
        for (int i = 0; i < childrenLength_; i++)
        {
            children_[i] = nullptr;
        }
    }
}

template <typename T>
Node<T>::~Node()
{
    if (children_)
    {
        for (int i = 0; i < childrenLength_; i++)
        {
            children_[i] = nullptr;
        }

        free(children_);
        children_ = nullptr;
    }
    
    if (record_ != nullptr) record_.reset();

    if (length() == s_uintNodeChildrenCount) --s_numUintNodes;
    else if (length() == s_pathNodeChildrenCount) --s_numPathNodes;
}

// ================================== class Trie ==================================

template <typename T>
Trie<T>::Trie(TrieKind kind)
{
    kind_ = kind;
    root_ = createNode();
    if (root_->children_ == nullptr)
    {
        throw BuildXLException("Trie creation failed as no root node could be allocated!");
    }
}

template <typename T>
Trie<T>::~Trie()
{
    traverse(/*computeKey*/ false, /*callbackArgs*/ nullptr, [](Trie<T>*, void*, uint64_t, Node<T> *node)
    {
        delete node;
    });

    root_ = nullptr;
    size_ = 0;
}

template <typename T>
bool Trie<T>::findChildNode(Node<T> *node, int idx, bool createIfMissing)
{
    if (idx < 0 || idx >= node->length())
    {
        return false;
    }

    bool childNodeExists = node->children()[idx] != nullptr;

    if (childNodeExists)
    {
        return true;
    }

    // child is missing
    if (!createIfMissing)
    {
        return false;
    }

    Node<T>* newNode = createNode();

    // This should never happen except if we run out of memory.
    if (newNode == nullptr)
    {
        return false;
    }
    
    // TODO: Make thread safe!
    node->children()[idx] = newNode;

    return true;
}

template <typename T>
TrieResult Trie<T>::makeSentinel(Node<T> *node, std::shared_ptr<T> record)
{
    // if this is a sentinel node --> nothing to do
    if (node->record_ != nullptr)
    {
        return kTrieResultAlreadyExists;
    }

    std::shared_ptr<T> newRecord = record;
    if (newRecord == nullptr)
    {
        return kTrieResultAlreadyExists;
    }
    
    if (node->record_ != nullptr)
    {
        newRecord.reset();
        return kTrieResultAlreadyExists;
    }
    
    // TODO: Make thread safe!
    node->record_ = newRecord;
    int oldCount = (++size_);
    triggerOnChange(oldCount, oldCount + 1);
    
    return kTrieResultInserted;
}

template <typename T>
std::shared_ptr<T> Trie<T>::get(Node<T> *node)
{
    return node != nullptr ? node->record_ : nullptr;
}

template <typename T>
std::shared_ptr<T> Trie<T>::getOrAdd(Node<T> *node, std::shared_ptr<T> record, TrieResult *result)
{
    if (node == nullptr) return nullptr;
    auto sentinelResult = makeSentinel(node, record);
    if (result) *result = sentinelResult;
 
    return node->record_;
}

template <typename T>
TrieResult Trie<T>::replace(Node<T> *node, const std::shared_ptr<T> value)
{
    if (node == nullptr || value == nullptr)
    {
        return kTrieResultFailure;
    }

    std::shared_ptr<T> previousValue = node->record_;
    
    if (previousValue != nullptr)
    {
        node->record_ = value;
        previousValue.reset();
        
        return kTrieResultReplaced;
    }
    else
    {
        node->record_ = value;
        
        int oldCount = (++size_);
        triggerOnChange(oldCount, oldCount + 1);
        
        return kTrieResultInserted;
    }
}

template <typename T>
TrieResult Trie<T>::insert(Node<T> *node, const std::shared_ptr<T> value)
{
    if (node == nullptr || value == nullptr)
    {
        return kTrieResultFailure;
    }
    
    if (node->record_ != nullptr) return kTrieResultAlreadyExists;
    
    node->record_ = value;
    int oldCount = (++size_);
    triggerOnChange(oldCount, oldCount + 1);
    
    return kTrieResultInserted;
}

template <typename T>
TrieResult Trie<T>::remove(Node<T> *node)
{
    if (node == nullptr || node->record_ == nullptr)
    {
        return kTrieResultAlreadyEmpty;
    }


    if (node->record_ != nullptr)
    {
        node->record_.reset();
        
        int oldCount = (--size_);
        triggerOnChange(oldCount, oldCount - 1);
        
        return kTrieResultRemoved;
    }
    
    return kTrieResultFailure;
}

/*
 * Code used to generate this array:

 printf("static int s_char2idx[] = \n");
 printf("{\n");
 for (int ch = 0; ch < 256; ch++)
 {
     int idx = toupper(ch) - 32;
     if (idx < 0 || idx >= 65) idx = -1;
     printf("    %2d, // '%c' (\\%2d)\n", idx, ch < 32 || ch > 126 ? 0 : ch, ch);
 }
 printf("};\n");
 */
template <typename T>
static int s_char2idx[256] =
{
    -1, // '' (\0)
    -1, // '' (\1)
    -1, // '' (\2)
    -1, // '' (\3)
    -1, // '' (\4)
    -1, // '' (\5)
    -1, // '' (\6)
    -1, // '' (\7)
    -1, // '' (\8)
    -1, // '' (\9)
    -1, // '' (\10)
    -1, // '' (\11)
    -1, // '' (\12)
    -1, // '' (\13)
    -1, // '' (\14)
    -1, // '' (\15)
    -1, // '' (\16)
    -1, // '' (\17)
    -1, // '' (\18)
    -1, // '' (\19)
    -1, // '' (\20)
    -1, // '' (\21)
    -1, // '' (\22)
    -1, // '' (\23)
    -1, // '' (\24)
    -1, // '' (\25)
    -1, // '' (\26)
    -1, // '' (\27)
    -1, // '' (\28)
    -1, // '' (\29)
    -1, // '' (\30)
    -1, // '' (\31)
    0, // ' ' (\32)
    1, // '!' (\33)
    2, // '"' (\34)
    3, // '#' (\35)
    4, // '$' (\36)
    5, // '%' (\37)
    6, // '&' (\38)
    7, // ''' (\39)
    8, // '(' (\40)
    9, // ')' (\41)
    10, // '*' (\42)
    11, // '+' (\43)
    12, // ',' (\44)
    13, // '-' (\45)
    14, // '.' (\46)
    15, // '/' (\47)
    16, // '0' (\48)
    17, // '1' (\49)
    18, // '2' (\50)
    19, // '3' (\51)
    20, // '4' (\52)
    21, // '5' (\53)
    22, // '6' (\54)
    23, // '7' (\55)
    24, // '8' (\56)
    25, // '9' (\57)
    26, // ':' (\58)
    27, // ';' (\59)
    28, // '<' (\60)
    29, // '=' (\61)
    30, // '>' (\62)
    31, // '?' (\63)
    32, // '@' (\64)
    33, // 'A' (\65)
    34, // 'B' (\66)
    35, // 'C' (\67)
    36, // 'D' (\68)
    37, // 'E' (\69)
    38, // 'F' (\70)
    39, // 'G' (\71)
    40, // 'H' (\72)
    41, // 'I' (\73)
    42, // 'J' (\74)
    43, // 'K' (\75)
    44, // 'L' (\76)
    45, // 'M' (\77)
    46, // 'N' (\78)
    47, // 'O' (\79)
    48, // 'P' (\80)
    49, // 'Q' (\81)
    50, // 'R' (\82)
    51, // 'S' (\83)
    52, // 'T' (\84)
    53, // 'U' (\85)
    54, // 'V' (\86)
    55, // 'W' (\87)
    56, // 'X' (\88)
    57, // 'Y' (\89)
    58, // 'Z' (\90)
    59, // '[' (\91)
    60, // '\' (\92)
    61, // ']' (\93)
    62, // '^' (\94)
    63, // '_' (\95)
    64, // '`' (\96)
    33, // 'a' (\97)
    34, // 'b' (\98)
    35, // 'c' (\99)
    36, // 'd' (\100)
    37, // 'e' (\101)
    38, // 'f' (\102)
    39, // 'g' (\103)
    40, // 'h' (\104)
    41, // 'i' (\105)
    42, // 'j' (\106)
    43, // 'k' (\107)
    44, // 'l' (\108)
    45, // 'm' (\109)
    46, // 'n' (\110)
    47, // 'o' (\111)
    48, // 'p' (\112)
    49, // 'q' (\113)
    50, // 'r' (\114)
    51, // 's' (\115)
    52, // 't' (\116)
    53, // 'u' (\117)
    54, // 'v' (\118)
    55, // 'w' (\119)
    56, // 'x' (\120)
    57, // 'y' (\121)
    58, // 'z' (\122)
    -1, // '{' (\123)
    -1, // '|' (\124)
    -1, // '}' (\125)
    -1, // '~' (\126)
    -1, // '' (\127)
    -1, // '' (\128)
    -1, // '' (\129)
    -1, // '' (\130)
    -1, // '' (\131)
    -1, // '' (\132)
    -1, // '' (\133)
    -1, // '' (\134)
    -1, // '' (\135)
    -1, // '' (\136)
    -1, // '' (\137)
    -1, // '' (\138)
    -1, // '' (\139)
    -1, // '' (\140)
    -1, // '' (\141)
    -1, // '' (\142)
    -1, // '' (\143)
    -1, // '' (\144)
    -1, // '' (\145)
    -1, // '' (\146)
    -1, // '' (\147)
    -1, // '' (\148)
    -1, // '' (\149)
    -1, // '' (\150)
    -1, // '' (\151)
    -1, // '' (\152)
    -1, // '' (\153)
    -1, // '' (\154)
    -1, // '' (\155)
    -1, // '' (\156)
    -1, // '' (\157)
    -1, // '' (\158)
    -1, // '' (\159)
    -1, // '' (\160)
    -1, // '' (\161)
    -1, // '' (\162)
    -1, // '' (\163)
    -1, // '' (\164)
    -1, // '' (\165)
    -1, // '' (\166)
    -1, // '' (\167)
    -1, // '' (\168)
    -1, // '' (\169)
    -1, // '' (\170)
    -1, // '' (\171)
    -1, // '' (\172)
    -1, // '' (\173)
    -1, // '' (\174)
    -1, // '' (\175)
    -1, // '' (\176)
    -1, // '' (\177)
    -1, // '' (\178)
    -1, // '' (\179)
    -1, // '' (\180)
    -1, // '' (\181)
    -1, // '' (\182)
    -1, // '' (\183)
    -1, // '' (\184)
    -1, // '' (\185)
    -1, // '' (\186)
    -1, // '' (\187)
    -1, // '' (\188)
    -1, // '' (\189)
    -1, // '' (\190)
    -1, // '' (\191)
    -1, // '' (\192)
    -1, // '' (\193)
    -1, // '' (\194)
    -1, // '' (\195)
    -1, // '' (\196)
    -1, // '' (\197)
    -1, // '' (\198)
    -1, // '' (\199)
    -1, // '' (\200)
    -1, // '' (\201)
    -1, // '' (\202)
    -1, // '' (\203)
    -1, // '' (\204)
    -1, // '' (\205)
    -1, // '' (\206)
    -1, // '' (\207)
    -1, // '' (\208)
    -1, // '' (\209)
    -1, // '' (\210)
    -1, // '' (\211)
    -1, // '' (\212)
    -1, // '' (\213)
    -1, // '' (\214)
    -1, // '' (\215)
    -1, // '' (\216)
    -1, // '' (\217)
    -1, // '' (\218)
    -1, // '' (\219)
    -1, // '' (\220)
    -1, // '' (\221)
    -1, // '' (\222)
    -1, // '' (\223)
    -1, // '' (\224)
    -1, // '' (\225)
    -1, // '' (\226)
    -1, // '' (\227)
    -1, // '' (\228)
    -1, // '' (\229)
    -1, // '' (\230)
    -1, // '' (\231)
    -1, // '' (\232)
    -1, // '' (\233)
    -1, // '' (\234)
    -1, // '' (\235)
    -1, // '' (\236)
    -1, // '' (\237)
    -1, // '' (\238)
    -1, // '' (\239)
    -1, // '' (\240)
    -1, // '' (\241)
    -1, // '' (\242)
    -1, // '' (\243)
    -1, // '' (\244)
    -1, // '' (\245)
    -1, // '' (\246)
    -1, // '' (\247)
    -1, // '' (\248)
    -1, // '' (\249)
    -1, // '' (\250)
    -1, // '' (\251)
    -1, // '' (\252)
    -1, // '' (\253)
    -1, // '' (\254)
    -1, // '' (\255)
};

static_assert(CHAR_BIT == 8, "char is not 8 bits long");
static_assert(UCHAR_MAX == 255, "max unsigned char is not 255");

template <typename T>
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

template <typename T>
static int s_powLen = sizeof(s_pow10<T>)/sizeof(s_pow10<T>[0]);

template <typename T>
static uint64_t pow10(int exp)
{
    if (exp < s_powLen<T>) return s_pow10<T>[exp];
    uint64_t result = 1;
    while (--exp >= 0) result *= 10;
    return result;
}

template <typename T>
Node<T>* Trie<T>::findPathNode(const char *path, bool createIfMissing)
{
    Node<T> *currNode = root_;
    unsigned char ch;
    while ((ch = *path++) != '\0')
    {
        int idx = s_char2idx<T>[ch];
        if (!findChildNode(currNode, idx, createIfMissing))
        {
            return nullptr;
        }
        currNode = currNode->children()[idx];
    }

    return currNode;
}

template <typename T>
Node<T>* Trie<T>::findUintNode(uint64_t key, bool createIfMissing)
{
    Node<T> *currNode = root_;
    
    while (true)
    {
        assert(currNode->length() == 10);

        int lsd = key % 10;

        if (!findChildNode(currNode, lsd, createIfMissing))
        {
            return nullptr;
        }

        currNode = currNode->children()[lsd];

        if (key < 10)
        {
            break;
        }

        key = key / 10;
    }

    return currNode;
}

template <typename T>
bool Trie<T>::onChange(void *callbackArgs, on_change_fn callback)
{
    if (onChangeCallback_) return false;
    
    onChangeData_ = callbackArgs;
    onChangeCallback_ = callback;
    return true;
}

template <typename T>
void Trie<T>::triggerOnChange(int oldCount, int newCount) const
{
    if (onChangeCallback_) return;
    
    if (onChangeCallback_ && oldCount != newCount)
    {
        onChangeCallback_(onChangeData_, oldCount, newCount);
    }
}

template <typename T>
void Trie<T>::forEach(void *callbackArgs, for_each_fn callback)
{
    typedef struct { for_each_fn callback; void *args; } State;
    State state = { .callback = callback, .args = callbackArgs };
    traverse(/*computeKey*/ kind_ == kUintTrie, /*callbackArgs*/ &state, [](Trie<T> *me, void *s, uint64_t key, Node<T> *node)
    {
        State *state = (State*)s;
        std::shared_ptr<T> record = node->record_;
        if (record)
        {
            state->callback(state->args, key, record);
        }
    });
}

template <typename T>
void Trie<T>::removeMatching(void *filterArgs, filter_fn filter)
{
    typedef struct { filter_fn filter; void *args; } State;
    State state = { .filter = filter, .args = filterArgs };
    traverse(/*computeKey*/ false, /*callbackArgs*/ &state, [](Trie<T> *me, void *s, uint64_t, Node<T> *node)
    {
        State *state = (State*)s;
        std::shared_ptr<T> record = node->record_;
        if (record)
        {
            if (state->filter(state->args, record))
            {
                me->remove(node);
            }
        }
    });
}

template <typename T>
struct Stack {
    Node<T> *node;
    Stack *next;
    uint32_t depth;
    uint64_t key;
};

template <typename T>
static void push(Stack<T> **stack, Node<T> *node, uint64_t path, uint32_t depth)
{
    if (node == nullptr) return;

    Stack<T> *top = new Stack<T>();
    top->node  = node;
    top->next  = *stack;
    top->key   = path;
    top->depth = depth;

    *stack = top;
}

template <typename T>
static Node<T>* pop(Stack<T> **stack)
{
    Stack<T> *top = *stack;
    *stack = top->next;
    Node<T> *node = top->node;
    delete top;
    return node;
}

template <typename T>
void Trie<T>::traverse(bool computeKey, void *callbackArgs, traverse_fn callback)
{
    Stack<T> *stack = nullptr;
    push(&stack, root_, /*key*/ 0, /*depth*/ 0);
    while (stack != nullptr)
    {
        uint64_t key = stack->key;
        uint32_t depth = stack->depth;

        Node<T> *curr = pop(&stack);
        for (int i = 0; i < curr->length(); ++i)
        {
            push(&stack, curr->children()[i], computeKey ? (i * pow10<T>(depth) + key) : 0, depth + 1);
        }

        // the callback may deallocate 'curr' node, hence this must be the last statement in this loop
        callback(this, callbackArgs, key, curr);
    }
}

template class Trie<SandboxedProcess>;

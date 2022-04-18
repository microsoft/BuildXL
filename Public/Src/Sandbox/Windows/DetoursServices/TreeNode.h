// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

#pragma warning(disable: 4710 5045)

#if defined(_DO_NOT_EXPORT)
#define EXPORT  
#else
#define EXPORT __declspec(dllexport)
#endif

#include <unordered_map>
#include <vector>
#include <functional>
#include "UtilityHelpers.h"

struct TreeNode;

// The threshold is defined based on profiling sessions
#define TREE_NODE_CHILDREN_THRESHOLD 100U

// The children of a TreeNode. Exposes a mutable associative collection of wstring to TreeNode*.
// In most cases a TreeNode do not have too many children, so the class is optimized to deal with the case of a lower
// number of children.
// The implementation uses a vector as the underlying initial container and switches to an unordered map after the threshold capacity is met. The rationale
// is that a vector behaves better (and has lower footprint) than a map for a low number of elements
// The class assumes a relatively low number of deletions: once the threshold is reached the map is used for the remainding lifetime of the instance
// All comparisons againt the key are case insensitive, following the functionality of PathTree
// This class is not thread safe
class TreeNodeChildren
{
public:
    EXPORT inline TreeNodeChildren() :
        m_map(NULL),
        m_vector(new std::vector<std::pair<std::wstring, TreeNode*>>())
    { 
    }

    EXPORT inline ~TreeNodeChildren()
    {
        if (m_vector != NULL)
        {
            delete m_vector;
        }
        else
        {
            delete m_map;
        }
    }

    TreeNodeChildren(const TreeNodeChildren& obj) = default;
    TreeNodeChildren& operator=(const TreeNodeChildren&) = default;

    // Finds a key in the collection. Returns whether it was found
    // The given out value is populated with the result.
    EXPORT bool find(const std::wstring& key, std::pair<std::wstring, TreeNode*>& value);

    // Emplaces a key value association in the collection
    EXPORT void emplace(const std::wstring& key, TreeNode*& value);
    
    // Erases the given key, if present, from the collection
    EXPORT void erase(const std::wstring& key);

    // The current size of the collection
    EXPORT inline size_t size()
    {
        return m_map != NULL ? (size_t)m_map->size() : (size_t)m_vector->size();
    }

    // Removes all elements from the collection
    EXPORT inline void clear()
    {
        m_map != NULL ? m_map->clear() : m_vector->clear();
    }

    // Applies the given function to each element of the collection
    EXPORT void forEach(std::function<void(std::pair<std::wstring, TreeNode*>*)> function);

private:
    std::unordered_map<std::wstring, TreeNode*, CaseInsensitiveStringHasher, CaseInsensitiveStringComparer>* m_map;
    std::vector<std::pair<std::wstring, TreeNode*>>* m_vector;
    static CaseInsensitiveStringComparer s_comparer;
};

// A node in a PathTree
struct TreeNode {
    // Edges to children, with the path atom that leads to it
    TreeNodeChildren children;
    // Whether the node is an intermediate node or it represents a path that was explicitly inserted
    bool intermediate;
};
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

#pragma warning(disable: 4710 5045)

#if defined(_DO_NOT_EXPORT)
#define EXPORT
#else
#define EXPORT __declspec(dllexport)
#endif

#include "UtilityHelpers.h"
#include "TreeNode.h"

// An n-ary tree where nodes are path atoms. Drive letters are at the root and traces in the tree represent paths.
// This class is not thread safe
class PathTree {
public:
    // Adds a path to the tree. Returns whether the provided path could be properly interpreted.
    EXPORT bool TryInsert(const std::wstring& path);

    // Adds all explicitly inserted descendants of the given path into the given vector
    // All descendants are removed from this tree
    // E.g. after doing the following on an empty tree:
    // TryInsert(a\path\to\file.txt)
    // TryInsert(a\path\to\another-file.txt)
    // The result of RetrieveAndRemoveAllDescendants(a\path, desc) is such that
    // desc = ['a\path\to\file.txt', 'a\path\to\another-file.txt']
    // Check Public\Src\Sandbox\Windows\UnitTests\PathTreeTests.cpp for additional examples and expected behavior
    EXPORT void RetrieveAndRemoveAllDescendants(const std::wstring& path, std::vector<std::wstring>& descendants);

    EXPORT PathTree();
    EXPORT ~PathTree();

    // Returns a string representation of the content of the tree. For debugging purposes only.
    EXPORT std::wstring DumpTree();

    PathTree(const PathTree&) = delete;
    PathTree& operator=(const PathTree&) = delete;

private:
    // Adds an edge from the given node with the provided atom
    TreeNode* Append(const std::wstring& atom, TreeNode* node, bool isIntermediate);

    // Tries to find the provided path in the current tree. On success, returns the trace in the tree that leads to the
    // path final atom
    bool TryFind(const std::wstring& path, std::vector<std::pair<std::wstring, TreeNode*>>& nodeTrace);

    // Removes all descendants from the given node and builds the descendants collection using the given path as a prefix
    void RetrieveAndRemoveAllDescendants(const std::wstring& path, TreeNode* lastNode, std::vector<std::wstring>& descendants);

    // Removes all descendants from the given node
    void RemoveAllDescendants(TreeNode* node);

    // Debugging facility
    std::wstring ToDebugString(TreeNode* node = nullptr, std::wstring ident = L"");

    TreeNode* m_root;
};
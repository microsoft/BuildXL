#include "PathTree.h"

// If we are building this for tests, we don't want to use the builxl private heap that only exists when running under detours
#ifndef TEST
    #include "stdafx.h"
    #include "DataTypes.h"
    #include "DetoursHelpers.h"
    #include "buildXL_mem.h"
#endif
#include "StringOperations.h"

// When compiled for tests Dbg is not defined, so let's provide a mock for it
#ifdef TEST
void Dbg(PCWSTR, ...) {}
#endif

PathTree::PathTree()
{
    m_root = new TreeNode();
    // The root is never a final path
    m_root->intermediate = true;
}

PathTree::~PathTree()
{
    RemoveAllDescendants(m_root);
    delete m_root;
}

bool PathTree::TryInsert(const std::wstring& path)
{
    std::vector<std::wstring> elements;
    int err = TryDecomposePath(path, elements);
    if (err != 0)
    {
        Dbg(L"PathTree::TryInsert: TryDecomposePath failed, not resolving path: %d", err);
        return false;
    }

    TreeNode* currentNode = m_root;

    for (unsigned int i = 0; i < elements.size(); i++)
    {
        // Only the last element is a final node
        bool isIntermediate = (i != (elements.size() - 1));
        currentNode = Append(elements[i], currentNode, isIntermediate);
    }

    return true;
}

TreeNode* PathTree::Append(const std::wstring& atom, TreeNode* node, bool isIntermediate)
{
    // First check if the node is already there
    std::pair<std::wstring, TreeNode*> search;
    if (node->children.find(atom, search))
    {
        // If the node being appended is not an intermediate, that overrides the existing node flag
        (search.second)->intermediate &= isIntermediate;
        return search.second;
    }

    // It is not there. Create it and add it as a child of the given node
    TreeNode* newNode = new TreeNode();
    newNode->intermediate = isIntermediate;
    node->children.emplace(atom, newNode);

    return newNode;
}

void PathTree::RetrieveAndRemoveAllDescendants(const std::wstring& path, std::vector<std::wstring>& descendants)
{
    // Find the trace in the tree that matches the path
    std::vector<std::pair<std::wstring, TreeNode*>> nodeTrace;
    if (!TryFind(path, nodeTrace))
    {
        return;
    }

    // Let's build the given path again based on the resulting trace so casing is preserved
    // Observe there is always at least one element (the root of the tree), which we skip
    auto it = nodeTrace.begin();
    it++;

    std::wstring normalizedPath;
    if (it != nodeTrace.end())
    {
        normalizedPath.append(it->first);
        it++;
    }

    for (; it != nodeTrace.end(); it++)
    {
        normalizedPath.append(L"\\");
        normalizedPath.append(it->first);
    }

    // Pop all the descendants of the leaf node and build the descendant collection
    RetrieveAndRemoveAllDescendants(normalizedPath, nodeTrace.back().second, descendants);

    // Let's walk upwards, towards the root, removing all intermediates with no branching
    // The presence of these nodes won't affect future computation of descendants but it can slow
    // down searches
    while (!nodeTrace.empty())
    {
        std::pair<std::wstring, TreeNode*> pair = nodeTrace.back();
        TreeNode* node = pair.second;

        nodeTrace.pop_back();

        // We don't want to delete the root. And we should only remove intermediates with no children
        // (no children after removing the last edge)
        if (node != m_root && node->intermediate && node->children.size() == 0)
        {
            TreeNode* predecesor = nodeTrace.back().second;
            predecesor->children.erase(pair.first);
            delete node;
        }
        else
        {
            break;
        }
    }
}

void PathTree::RetrieveAndRemoveAllDescendants(const std::wstring& path, TreeNode* node, std::vector<std::wstring>& descendants)
{
    std::vector<TreeNode*> nodesToDelete;

    auto retrieve = [this, &path, node, &descendants, &nodesToDelete](std::pair<std::wstring, TreeNode*>* iter)
    {
        // Add the path atom to the path.
        std::wstring descendant(path);
        if (node != m_root)
        {
            descendant.append(L"\\");
            descendant.append(iter->first);
        }

        // Add it to the collection only if it is a final path
        if (!(iter->second->intermediate))
        {
            descendants.push_back(descendant);
        }

        RetrieveAndRemoveAllDescendants(descendant, iter->second, descendants);

        nodesToDelete.push_back(iter->second);

    };

    node->children.forEach(retrieve);
    
    for (auto iter = nodesToDelete.begin(); iter != nodesToDelete.end(); iter++)
    {
        delete *iter;
    }

    node->children.clear();
}

void PathTree::RemoveAllDescendants(TreeNode* node)
{
    auto remove = [this](std::pair<std::wstring, TreeNode*>* iter)
    {
        RemoveAllDescendants(iter->second);

        delete iter->second;
    };

    node->children.forEach(remove);

    node->children.clear();
}

bool PathTree::TryFind(const std::wstring& path, std::vector<std::pair<std::wstring, TreeNode*>>& nodeTrace)
{
    std::vector<std::wstring> elements;
    int err = TryDecomposePath(path, elements);
    if (err != 0)
    {
        Dbg(L"PathTree::TryFind: TryDecomposePath failed, not resolving path: %d", err);
        return false;
    }

    auto currentNode = m_root;

    nodeTrace.push_back(std::make_pair(L"", m_root));

    for (unsigned int i = 0; i < elements.size(); i++)
    {
        std::pair<std::wstring, TreeNode*> search;
        if (!currentNode->children.find(elements[i], search))
        {
            return false;
        }

        nodeTrace.push_back(search);
        currentNode = search.second;
    }

    return true;
}

std::wstring PathTree::DumpTree()
{
    return ToDebugString();
}

std::wstring PathTree::ToDebugString(TreeNode* node, std::wstring indent)
{
    std::wstring result;

    if (node == nullptr)
    {
        node = m_root;
    }

    auto append = [&result, &indent, this](std::pair<std::wstring, TreeNode*>* iter)
    {
        result.append(indent + iter->first + (!iter->second->intermediate ? L"*" : L"") + L"\r\n");
        result.append(ToDebugString(iter->second, indent + L"\t"));
    };

    node->children.forEach(append);

    return result;
}
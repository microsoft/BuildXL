// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "TreeNode.h"
#include "UtilityHelpers.h"

CaseInsensitiveStringComparer TreeNodeChildren::s_comparer;

void TreeNodeChildren::forEach(std::function<void(std::pair<std::wstring, TreeNode*>*)> function)
{
    if (m_map != NULL)
    {
        for (auto it = m_map->begin(); it != m_map->end(); it++)
        {
            std::pair<std::wstring, TreeNode*> pair = *it;
            function(&pair);
        }
    }
    else
    {
        for (auto it = m_vector->begin(); it != m_vector->end(); it++)
        {
            function(it.operator->());
        }
    }
}

void TreeNodeChildren::erase(const std::wstring& key)
{
    if (m_map != NULL)
    {
        m_map->erase(key);
    }
    else
    {

        for (auto it = m_vector->begin(); it != m_vector->end(); it++)
        {
            if (s_comparer(key, it->first))
            {
                m_vector->erase(it);
                return;
            }
        }
    }
}

void TreeNodeChildren::emplace(const std::wstring& key, TreeNode*& value)
{
    // If the vector is in use an we haven't reached the capacity threshold, add it
    // to the vector
    if (m_vector != NULL && m_vector->size() <= TREE_NODE_CHILDREN_THRESHOLD)
    {
        std::pair<const std::wstring&, TreeNode*&> pair(key, value);
        m_vector->emplace(m_vector->begin(), pair);
    }
    // If the map is in use that means we already reached the threshold and we are using the map
    else if (m_map != NULL)
    {
        m_map->emplace(key, value);
    }
    else
    {
        // Otherwise, we reached the threshold. Create the map, copy the vector content over to the map and delete the vector
        m_map = new std::unordered_map<std::wstring, TreeNode*, CaseInsensitiveStringHasher, CaseInsensitiveStringComparer>();
        for (auto it = m_vector->begin(); it != m_vector->end(); it++)
        {
            (*m_map)[it->first] = it->second;
        }

        m_map->emplace(key, value);

        delete m_vector;
        m_vector = NULL;
    }
}

bool TreeNodeChildren::find(const std::wstring& key, std::pair<std::wstring, TreeNode*>& value)
{
    if (m_vector != NULL)
    {
        for (auto it = m_vector->begin(); it != m_vector->end(); it++)
        {
            if (s_comparer(key, it->first))
            {
                value = std::make_pair(it->first, it->second);
                return true;
            }
        }
    }
    else
    {
        auto it = m_map->find(key);
        if (it != m_map->end())
        {
            value = std::make_pair(it->first, it->second);
            return true;
        }
    }

    return false;
}
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <TreeNode.h>
#include <vector>

BOOST_AUTO_TEST_SUITE(TreeNodeTests)

void TestBasicFunctionality(std::vector<std::wstring>& elementsToEmplace)
{
    TreeNodeChildren children;
    TreeNode* dummy = NULL;
    for (auto it = elementsToEmplace.begin(); it != elementsToEmplace.end(); it++)
    {
        children.emplace(*it, dummy);
    }

    BOOST_CHECK_EQUAL(elementsToEmplace.size(), children.size());

    std::pair<std::wstring, TreeNode*> result;
    
    bool t1 = children.find(std::wstring(L"test1"), result);
    BOOST_CHECK(t1);
    BOOST_CHECK_EQUAL(L"test1", result.first.c_str());

    // Search should be case insensitive
    t1 = children.find(std::wstring(L"TEST1"), result);
    BOOST_CHECK(t1);
    BOOST_CHECK_EQUAL(L"test1", result.first.c_str());

    // Validate that erase actually removes the element
    children.erase(std::wstring(L"test1"));
    BOOST_CHECK_EQUAL(elementsToEmplace.size() - 1, children.size());
    t1 = children.find(std::wstring(L"test1"), result);
    BOOST_CHECK(!t1);

    // Validate for each
    std::vector<std::wstring> collection;
    auto testForEach = [&collection](std::pair<std::wstring, TreeNode*>* iter)
    {
        std::wstring elem(iter->first);
        collection.emplace(collection.begin(), elem);
    };
    children.forEach(testForEach);
    BOOST_CHECK_EQUAL(elementsToEmplace.size() - 1, collection.size());

    // validate clear
    children.clear();
    BOOST_CHECK_EQUAL(0, children.size());   
}

BOOST_AUTO_TEST_CASE( TreeNodeUnderThreshold )
{
    std::vector<std::wstring> elements;
    elements.emplace(elements.begin(), std::wstring(L"test1"));
    elements.emplace(elements.begin(), std::wstring(L"test2"));
    elements.emplace(elements.begin(), std::wstring(L"test3"));

    TestBasicFunctionality(elements);
}

BOOST_AUTO_TEST_CASE( TreeNodeBeyondThreshold )
{
    
    TreeNodeChildren children;
    std::vector<std::wstring> elements;
    for(int i = 0 ; i < TREE_NODE_CHILDREN_THRESHOLD * 2; i++)
    {
        elements.emplace(elements.begin(), std::wstring(L"test" + std::to_wstring(i)));
    }
    
    TestBasicFunctionality(elements);
}

BOOST_AUTO_TEST_SUITE_END()

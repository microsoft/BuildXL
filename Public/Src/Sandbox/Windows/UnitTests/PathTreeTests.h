// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// warning C26426: Global initializer calls a non-constexpr function 'boost::unit_test::decorator::collector_t::instance' (i.22).
#pragma warning( disable : 26426 26440 )

#include <PathTree.h>

BOOST_AUTO_TEST_SUITE(PathTreeTests)

bool contains(std::vector<std::wstring>& collection, const std::wstring& element) noexcept;

// warning C26496: The variable 'success' does not change after construction, mark it as const (con.4).
#pragma warning( disable : 26496 )

BOOST_AUTO_TEST_CASE( WellFormedPaths )
{
    PathTree t;
    bool success = t.TryInsert(L"C:\\a\\path");
    BOOST_CHECK(success);

    success = t.TryInsert(L"C:\\");
    BOOST_CHECK(success);
}

BOOST_AUTO_TEST_CASE( SimpleDescendants )
{
    PathTree t;
    t.TryInsert(L"C:\\a\\path");
    t.TryInsert(L"C:\\a\\another-path");

    std::vector<std::wstring> desc;
    t.RetrieveAndRemoveAllDescendants(L"C:\\a", desc);
    
    // We should get exactly two descendants
    BOOST_CHECK_EQUAL(2, desc.size());
    BOOST_CHECK(contains(desc, L"C:\\a\\path"));
    BOOST_CHECK(contains(desc, L"C:\\a\\another-path"));
}

BOOST_AUTO_TEST_CASE( IntermediatesAreNotReturned )
{
    PathTree t;
    t.TryInsert(L"C:\\a\\path\\to\\something");

    std::vector<std::wstring> desc;
    t.RetrieveAndRemoveAllDescendants(L"C:\\a", desc);
    
    // We shouldn't get any intermediate node as a descendant
    BOOST_CHECK_EQUAL(1, desc.size());
    BOOST_CHECK(contains(desc, L"C:\\a\\path\\to\\something"));
}

BOOST_AUTO_TEST_CASE( IntermediateTurnedIntoFinal )
{
    PathTree t;
    t.TryInsert(L"C:\\a\\path\\to\\something");
    // This insertion should make the last node a final one
    t.TryInsert(L"C:\\a\\path");

    std::vector<std::wstring> desc;
    t.RetrieveAndRemoveAllDescendants(L"C:\\", desc);
    
    BOOST_CHECK_EQUAL(2, desc.size());
}

BOOST_AUTO_TEST_CASE( RetrieveAndRemoveAllDescendantssCleanUp )
{
    PathTree t;
    t.TryInsert(L"C:\\a\\");
    t.TryInsert(L"C:\\a\\path\\to");
    t.TryInsert(L"C:\\a\\path\\to\\something");
    t.TryInsert(L"C:\\a\\path\\to\\something-else");
    t.TryInsert(L"C:\\b\\");

    std::vector<std::wstring> desc;
    t.RetrieveAndRemoveAllDescendants(L"C:\\a", desc);
    
    desc.clear();
    t.RetrieveAndRemoveAllDescendants(L"C:\\a", desc);

    // We shouldn't get anything since we already removed all
    BOOST_CHECK(desc.empty());
}

BOOST_AUTO_TEST_CASE( RetrieveAndRemoveAllDescendantsBranching )
{
    PathTree t;
    t.TryInsert(L"C:\\a\\path\\to\\branch");
    t.TryInsert(L"C:\\a\\path\\to\\branch\\something");
    t.TryInsert(L"C:\\a\\path\\to\\branch\\something-else");
    t.TryInsert(L"C:\\a\\path\\from\\something");

    // This should remove all C:\a\path\to\* paths
    std::vector<std::wstring> desc;
    t.RetrieveAndRemoveAllDescendants(L"C:\\a\\path\\to", desc);
    
    BOOST_CHECK_EQUAL(3, desc.size());

    desc.clear();
    // And this should remove the remaining C:\a\path\from\something
    t.RetrieveAndRemoveAllDescendants(L"C:\\a", desc);

    BOOST_CHECK_EQUAL(1, desc.size());
    BOOST_CHECK(contains(desc, L"C:\\a\\path\\from\\something"));
}

BOOST_AUTO_TEST_CASE( CaseInsensitivePaths )
{
    PathTree t;
    t.TryInsert(L"C:\\a\\path\\to\\something");

    std::vector<std::wstring> desc;
    t.RetrieveAndRemoveAllDescendants(L"C:\\A", desc);
    
    // We should get the descendant regardless of casing
    BOOST_CHECK_EQUAL(1, desc.size());
}

BOOST_AUTO_TEST_CASE( CasePreservingPaths )
{
    PathTree t;
    t.TryInsert(L"C:\\a\\path\\to\\something");
    t.TryInsert(L"C:\\a\\path\\to\\SOMETHING");
    t.TryInsert(L"C:\\a\\path\\to\\ELSE");
    t.TryInsert(L"C:\\a\\path\\TO\\something");

    std::vector<std::wstring> desc;
    t.RetrieveAndRemoveAllDescendants(L"C:\\A", desc);

    // We should get 2 descendants preserving the casing that wins the race
    BOOST_CHECK_EQUAL(2, desc.size());
    BOOST_CHECK(contains(desc, L"C:\\a\\path\\to\\something"));
    BOOST_CHECK(contains(desc, L"C:\\a\\path\\to\\ELSE"));
}

bool contains(std::vector<std::wstring>& collection, const std::wstring& element) noexcept
{
    for (auto iter = collection.begin(); iter != collection.end(); iter++)
    {
        if ((*iter) == element)
        {
            return true;
        }
    }

    return false;
}

BOOST_AUTO_TEST_SUITE_END()

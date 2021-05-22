// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <ResolvedPathCache.h>

BOOST_AUTO_TEST_SUITE(ResolvedPathCacheTests)

BOOST_AUTO_TEST_CASE( TryInsertPath )
{
    ResolvedPathCache cache;

    auto findResult = cache.GetResolvedPaths(L"C:\\a\\path", true);
    BOOST_CHECK(!findResult.Found);

    std::shared_ptr<std::vector<std::wstring>> order = std::make_shared<std::vector<std::wstring>>();
    std::shared_ptr<std::map<std::wstring, ResolvedPathType, CaseInsensitiveStringLessThan>> resolvedPaths = std::make_shared<std::map<std::wstring, ResolvedPathType, CaseInsensitiveStringLessThan>>();
    
    order->push_back(L"C:\\b\\path");
    resolvedPaths->emplace(L"C:\\b\\path", ResolvedPathType::Intermediate);

    bool success = cache.InsertResolvedPaths(L"C:\\a\\path", true, order, resolvedPaths);
    BOOST_CHECK(success);

    findResult = cache.GetResolvedPaths(L"C:\\a\\path", true);
    BOOST_CHECK(findResult.Found);

    BOOST_CHECK(findResult.Value.first->size() == 1);
    BOOST_CHECK(findResult.Value.first->at(0) == L"C:\\b\\path");
    BOOST_CHECK(findResult.Value.second->size() == 1);

    // This shows that the back pointers in the cache work, because when you remove a path, everything that points to that path must get erased.
    cache.Invalidate(L"C:\\b\\path", false);
    findResult = cache.GetResolvedPaths(L"C:\\a\\path", true);
    BOOST_CHECK(!findResult.Found);
}

BOOST_AUTO_TEST_SUITE_END()

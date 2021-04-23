// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <StringOperations.h>

BOOST_AUTO_TEST_SUITE(StringOperationsTests)

BOOST_AUTO_TEST_CASE(NormalizeRelativePath)
{
    std::wstring input(L"A\\B\\C");
    std::wstring result = NormalizePath(input);
    BOOST_CHECK_EQUAL(input.c_str(), result.c_str());
}

BOOST_AUTO_TEST_CASE(NormalizeShortPath)
{
    std::wstring input(L"C:\\A\\B\\C");
    std::wstring result = NormalizePath(input);
    BOOST_CHECK_EQUAL(input.c_str(), result.c_str());
}

BOOST_AUTO_TEST_CASE(NormalizePathWithDottedSegments)
{
    std::wstring input(L"C:\\A\\..\\C");
    std::wstring result = NormalizePath(input);
    BOOST_CHECK_EQUAL(L"C:\\C", result.c_str());
}

BOOST_AUTO_TEST_CASE(NormalizeShortPathWithNtLongPrefix)
{
    std::wstring input(L"\\\\?\\C:\\A\\B\\C");
    std::wstring result = NormalizePath(input);
    BOOST_CHECK_EQUAL(L"C:\\A\\B\\C", result.c_str());
}

BOOST_AUTO_TEST_CASE(NormalizeShortPathWithNtLongPrefixWithDottedSegments)
{
    std::wstring input(L"\\\\?\\C:\\A\\..\\C");
    std::wstring result = NormalizePath(input);
    BOOST_CHECK_EQUAL(L"C:\\C", result.c_str());
}

BOOST_AUTO_TEST_CASE(NormalizeShortNtObjectPrefixPath)
{
    std::wstring input(L"\\??\\C:\\A\\B\\C");
    std::wstring result = NormalizePath(input);
    BOOST_CHECK_EQUAL(input.c_str(), result.c_str());
}

BOOST_AUTO_TEST_CASE(NormalizeShortNtObjectPrefixPathWithDottedSegments)
{
    std::wstring input(L"\\??\\C:\\A\\..\\C");
    std::wstring result = NormalizePath(input);
    BOOST_CHECK_EQUAL(L"\\??\\C:\\C", result.c_str());
}

BOOST_AUTO_TEST_CASE(NormalizeUncPathAsIs)
{
    std::wstring input(L"\\\\server\\A\\B\\C\\D");
    std::wstring result = NormalizePath(input);
    BOOST_CHECK_EQUAL(input.c_str(), result.c_str());
}

BOOST_AUTO_TEST_CASE(NormalizeUncPathWithDottedSegments)
{
    std::wstring input(L"\\\\server\\A\\B\\..\\D");
    std::wstring result = NormalizePath(input);
    BOOST_CHECK_EQUAL(L"\\\\server\\A\\D", result.c_str());
}

BOOST_AUTO_TEST_CASE(NormalizeUncShortPathWithUncPrefix)
{
    std::wstring input(L"\\\\?\\UNC\\server\\A\\B\\C\\D");
    std::wstring result = NormalizePath(input);
    BOOST_CHECK_EQUAL(L"\\\\server\\A\\B\\C\\D", result.c_str());
}

BOOST_AUTO_TEST_CASE(NormalizeUncShortPathWithDottedSegments)
{
    std::wstring input(L"\\\\?\\UNC\\server\\A\\B\\..\\D");
    std::wstring result = NormalizePath(input);
    BOOST_CHECK_EQUAL(L"\\\\server\\A\\D", result.c_str());
}

BOOST_AUTO_TEST_CASE(NormalizeLongPath)
{
    std::wstring input(L"C:\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\foo");
    std::wstring result = NormalizePath(input);
    std::wstring expected(L"\\\\?\\");
    expected.append(input);
    BOOST_CHECK_EQUAL(expected.c_str(), result.c_str());
}

BOOST_AUTO_TEST_CASE(NormalizeLongPathWithDottedSegment)
{
    std::wstring input(L"C:\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\foo\\..\\bar");
    std::wstring result = NormalizePath(input);
    std::wstring expected(L"\\\\?\\");
    expected.append(L"C:\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\bar");
    BOOST_CHECK_EQUAL(expected.c_str(), result.c_str());
}

BOOST_AUTO_TEST_CASE(NormalizeLongPathWithNtLongPrefix)
{
    std::wstring input(L"\\\\?\\C:\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\foo");
    std::wstring result = NormalizePath(input);
    BOOST_CHECK_EQUAL(input.c_str(), result.c_str());
}

BOOST_AUTO_TEST_CASE(NormalizeLongPathWithNtLongPrefixDottedSegment)
{
    std::wstring input(L"\\\\?\\C:\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\foo\\..\\bar");
    std::wstring result = NormalizePath(input);
    std::wstring expected(L"\\\\?\\C:\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\bar");
    BOOST_CHECK_EQUAL(expected.c_str(), result.c_str());
}

BOOST_AUTO_TEST_CASE(NormalizeLongPathWithNtObjectPrefix)
{
    std::wstring input(L"\\??\\C:\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\foo");
    std::wstring result = NormalizePath(input);
    BOOST_CHECK_EQUAL(input.c_str(), result.c_str());
}

BOOST_AUTO_TEST_CASE(NormalizeLongPathWithNtObjectPrefixDottedSegment)
{
    std::wstring input(L"\\??\\C:\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\foo\\..\\bar");
    std::wstring result = NormalizePath(input);
    std::wstring expected(L"\\??\\C:\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\bar");
    BOOST_CHECK_EQUAL(expected.c_str(), result.c_str());
}

BOOST_AUTO_TEST_CASE(NormalizeLongUncPath)
{
    std::wstring input(L"\\\\server\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\foo");
    std::wstring result = NormalizePath(input);
    std::wstring expected(L"\\\\?\\UNC\\server\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\foo");
    BOOST_CHECK_EQUAL(expected.c_str(), result.c_str());
}

BOOST_AUTO_TEST_CASE(NormalizeLongUncPathWithDottedSegment)
{
    std::wstring input(L"\\\\server\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\foo\\..\\bar");
    std::wstring result = NormalizePath(input);
    std::wstring expected(L"\\\\?\\UNC\\server\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\bar");
    BOOST_CHECK_EQUAL(expected.c_str(), result.c_str());
}

BOOST_AUTO_TEST_CASE(NormalizeLongUncPathWithUncPrefix)
{
    std::wstring input(L"\\\\?\\UNC\\server\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\foo");
    std::wstring result = NormalizePath(input);
    BOOST_CHECK_EQUAL(input.c_str(), result.c_str());
}

BOOST_AUTO_TEST_CASE(NormalizeLongUncPathWithUncPrefixDottedSegment)
{
    std::wstring input(L"\\\\?\\UNC\\server\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\foo\\..\\bar");
    std::wstring result = NormalizePath(input);
    std::wstring expected(L"\\\\?\\UNC\\server\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\abc1\\abc2\\abc3\\abc4\\abcdef5\\abcdefghi6\\bar");
    BOOST_CHECK_EQUAL(expected.c_str(), result.c_str());
}

BOOST_AUTO_TEST_SUITE_END()

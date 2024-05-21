// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#define BOOST_TEST_MODULE LinuxSandboxTest
#define _DO_NOT_EXPORT

#include <boost/test/included/unit_test.hpp>
#include <limits.h>
#include <string.h>
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>

using namespace std;

BOOST_AUTO_TEST_SUITE(ReadlinkAbsentPath)

BOOST_AUTO_TEST_CASE(TestReadlinkAbsentPath)
{
    // Create a absent file path under current working directory
    char cwd[PATH_MAX] = { 0 };
    char *res = getcwd(cwd, PATH_MAX);   
    string newPath(res);
    newPath.append("/absentFile.o");

    // Call readlink on this absent file path
    char buf[PATH_MAX] = { 0 };
    auto read = readlink(newPath.c_str(), buf, PATH_MAX);
    // readlink return -1 on absent path and error number is ENOENT
    BOOST_CHECK(read == -1);
    BOOST_CHECK_EQUAL(errno, ENOENT);
}

BOOST_AUTO_TEST_SUITE_END()
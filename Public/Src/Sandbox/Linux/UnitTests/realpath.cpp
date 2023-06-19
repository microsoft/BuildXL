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

BOOST_AUTO_TEST_SUITE(RealPath)

BOOST_AUTO_TEST_CASE(TestRealPathAccesses)
{
    // .
    // {root}
    //   `-- symlink1 [->real1]
    //   `-- real1
    //         `-- symlink2 [->real2]
    //   `-- real2
    //         `-- file.txt
    //         `-- symlink4.txt [-> real3]
    //   `-- real3.txt
    //   `-- symlink3 [->real2]
    char buf[PATH_MAX];

    // Should report access on intermediates symink1 and symlink2
    char* res = realpath("symlink1/symlink2/file.txt", buf);
    BOOST_CHECK(res);
    // Sanity check path was fully resolved
    BOOST_CHECK(strstr(res, "symlink") == nullptr); 
    BOOST_CHECK(strstr(res, "real2/file.txt"));

    // Check that if the full path is a symlink we report it too
    // Should report access on symink3
    res = realpath("real2/symlink4.txt", buf);
    BOOST_CHECK(res);
    BOOST_CHECK(strstr(res, "symlink") == nullptr); 
    BOOST_CHECK(strstr(res, "real3.txt"));

    // Access a non-existent path through symlinks
    // Even though the call fails, the intermediate symlink should be reported
    res = realpath("symlink3/nonexistentfile.txt", buf);
    int err = errno;
    BOOST_CHECK(res == nullptr);   
    BOOST_CHECK_EQUAL(err, ENOENT);   
}

BOOST_AUTO_TEST_SUITE_END()
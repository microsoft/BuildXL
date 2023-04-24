// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#define BOOST_TEST_MODULE LinuxSandboxTest
#define _DO_NOT_EXPORT

#include <boost/test/included/unit_test.hpp>
#include <observer_utilities.hpp>

using namespace std;

BOOST_AUTO_TEST_SUITE(BxlObserverTests)

BOOST_AUTO_TEST_CASE(TestEnvVarResolution)
{
    mode_t mode = 0;
    std::string path;

    resolve_filename_with_env("sh", mode, path);
    BOOST_CHECK_EQUAL(path.c_str(), "/usr/bin/sh");
}

BOOST_AUTO_TEST_SUITE_END();
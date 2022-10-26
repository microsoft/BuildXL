// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#define BOOST_TEST_MODULE LinuxSandboxTest
#define _DO_NOT_EXPORT

#include <boost/test/included/unit_test.hpp>
#include <iostream>
#include <fstream>
#include <fcntl.h>

using namespace std;

BOOST_AUTO_TEST_SUITE(InterposeTests)

BOOST_AUTO_TEST_CASE(TestCopyFileRange)
{
    int data_len = 100;
    int copy_len = 50;
    string input = "input.txt";
    string output = "output.txt";

    string data(data_len, 'd');
    ofstream in_stream("input.txt");
    in_stream << data;
    in_stream.close();

    int fdIn = open(input.c_str(), O_RDONLY);
    int fdOut = open(output.c_str(), O_CREAT | O_WRONLY | O_TRUNC, 0644);
    int copied = copy_file_range(fdIn, 0, fdOut, 0, copy_len, 0);
    BOOST_CHECK_EQUAL(copied, copy_len);
    close(fdIn);
    close(fdOut);
}

BOOST_AUTO_TEST_SUITE_END()
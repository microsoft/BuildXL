// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#define BOOST_TEST_MODULE DetoursUnitTests
// TODO: Directly including the .hpp makes compiling the test unit infra a little bit time consuming
// If these tests become more widespread, consider integrating it with the corresponding boost .lib, also distributed as part of the framework
#include <boost/test/included/unit_test.hpp>

// This is currently a hack to organize tests into multiple files/suites.
// Ideally, the separation should be like 
//   https://www.boost.org/doc/libs/1_69_0/libs/test/doc/html/boost_test/adv_scenarios/single_header_customizations/multiple_translation_units.html
// However, such a separation currently causes Linker issue in Windows
//   LINK : fatal error LNK1104: cannot open file 'libboost_unit_test_framework-vc141-mt-gd-x64-1_71.lib'
// The below includes basically make all the separated test suites into a single translation unit.
#include "PathTreeTests.h"
#include "StringOperationsTests.h"
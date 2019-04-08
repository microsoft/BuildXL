// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef ps_hpp
#define ps_hpp

#import <string>
#import <set>

using namespace std;

extern set<string> ps_keywords;
string exec(const char *cmd);
string ps(pid_t pid, const string &cols);

#endif /* ps_hpp */

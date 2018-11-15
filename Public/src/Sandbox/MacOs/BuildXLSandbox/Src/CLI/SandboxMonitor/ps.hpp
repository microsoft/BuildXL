//
//  ps.hpp
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#ifndef ps_hpp
#define ps_hpp

#import <string>

using namespace std;

string exec(const char *cmd);
string ps(pid_t pid, const string &cols);

#endif /* ps_hpp */

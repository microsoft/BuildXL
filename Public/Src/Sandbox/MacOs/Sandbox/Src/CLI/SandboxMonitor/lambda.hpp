// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef lambda_hpp
#define lambda_hpp

#include <iostream>
#include <vector>
#include <algorithm>
#include <map>

template <typename Collection, typename UnaryOp>
void for_each(Collection &col, UnaryOp op){
    std::for_each(col.begin(), col.end(), op);
}

template <typename Collection, typename UnaryOp>
int sum(const Collection *col, UnaryOp op)
{
    int result = 0;
    for (auto it = col->begin(); it != col->end(); ++it)
        result += op(*it);
    return result;
}

template <typename Collection, typename TElem>
vector<TElem> flatten(const Collection *col, TElem e)
{
    vector<TElem> result;
    for (auto i = col->begin(); i != col->end(); ++i)
    {
        for (auto j = i->begin(); j != i->end(); ++j)
        {
            result.push_back(*j);
        }
    }
    return result;
}

template <typename TElem, typename TKey>
map<TKey, vector<TElem>> group_by(const vector<TElem> *arr, function<TKey(TElem)> op)
{
    map<TKey, vector<TElem>> result;
    for (auto it = arr->begin(); it != arr->end(); ++it)
    {
        auto key = op(*it);
        auto val = result.find(key);
        if (val == result.end())
        {
            auto vec = vector<TElem>();
            vec.push_back(*it);
            result[key] = vec;
        }
        else
        {
            val->second.push_back(*it);
        }
    };
    return result;
}

#endif /* lambda_hpp */

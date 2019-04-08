// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef render_hpp
#define render_hpp

#include <iostream>
#include <vector>
#include "lambda.hpp"

using namespace std;

template <typename Tuple>
class HeaderColumn
{
public:
    int width;
    string title;
    function<string(Tuple)> render;
};

template <typename Tuple>
class Renderer
{
private:
    typedef HeaderColumn<Tuple> HeaderCol;
    typedef vector<HeaderCol> Header;

    const bool renderStacked_;
    const string columnSeparator_;
    const vector<Header> *stackedHeaders_;
    
    string renderRow(int startHeaderIndex, function<string(const HeaderCol&)> renderer) const
    {
        stringstream result;
        int indent = 0;
        for (int i = 0; i < stackedHeaders_->size(); i++)
        {
            const Header *header = &stackedHeaders_->at(i);
            if (i < startHeaderIndex)
            {
                indent += sum(header, [](HeaderCol h) { return h.width; }); // all col widths
                indent += header->size() * columnSeparator_.length();       // all separator widths
            }
            else
            {
                if (i == startHeaderIndex)
                {
                    result << setw(indent) << "";
                }
                else
                {
                    result << columnSeparator_;
                }
                
                for (auto i = header->begin(); i != header->end(); ++i)
                {
                    if (i != header->begin()) result << columnSeparator_;
                    result << setw(i->width) << renderer(*i);
                }
            }
        }
        return result.str();
    }

public:

    Renderer(const string columnSeparator, const vector<Header> *stackedHeaders, bool renderStacked)
        : columnSeparator_(columnSeparator), stackedHeaders_(stackedHeaders), renderStacked_(renderStacked)
    {
    }
    
    string RenderHeader() const
    {
        return renderRow(
            0,
            [](const HeaderColumn<Tuple> &h) { return h.title; });
    }
    
    string RenderTuple(int startHeaderIdx, Tuple tuple) const
    {
        return renderRow(
            renderStacked_ ? startHeaderIdx : 0,
            [tuple](const HeaderColumn<Tuple> &h) { return h.render(tuple); });
    }
};

#endif /* render_hpp */

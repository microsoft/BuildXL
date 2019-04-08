/* -*- mode: c; c-basic-offset: 2; tab-width: 2; indent-tabs-mode: nil -*- */
/*
 *  Copyright (c) 2018 Steven G. Johnson, Jiahao Chen, Peter Colberg, Tony Kelman, Scott P. Jones, and other contributors.
 *  Copyright (c) 2009 Public Software Group e. V., Berlin, Germany
 *
 *  Permission is hereby granted, free of charge, to any person obtaining a
 *  copy of this software and associated documentation files (the "Software"),
 *  to deal in the Software without restriction, including without limitation
 *  the rights to use, copy, modify, merge, publish, distribute, sublicense,
 *  and/or sell copies of the Software, and to permit persons to whom the
 *  Software is furnished to do so, subject to the following conditions:
 *
 *  The above copyright notice and this permission notice shall be included in
 *  all copies or substantial portions of the Software.
 *
 *  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 *  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 *  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 *  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 *  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 *  FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 *  DEALINGS IN THE SOFTWARE.
 */

/*
 *  This library contains derived data from a modified version of the
 *  Unicode data files.
 *
 *  The original data files are available at
 *  http://www.unicode.org/Public/UNIDATA/
 *
 *  Please notice the copyright statement in the file "utf8proc_data.c".
 */


/*
 *  File name:    utf8proc.c
 *
 *  Description:
 *  Implementation of libutf8proc.
 */


#include "utf8proc.h"

#ifndef SSIZE_MAX
#define SSIZE_MAX ((size_t)SIZE_MAX/2)
#endif
#ifndef UINT16_MAX
#  define UINT16_MAX 65535U
#endif

#include "utf8proc_data.c"

/* internal "unsafe" version that does not check whether uc is in range */
static const utf8proc_property_t *unsafe_get_property(utf8proc_int32_t uc) {
    /* ASSERT: uc >= 0 && uc < 0x110000 */
    return utf8proc_properties + (utf8proc_stage2table[utf8proc_stage1table[uc >> 8] + (uc & 0xFF)]);
}

UTF8PROC_DLLEXPORT const utf8proc_property_t *utf8proc_get_property(utf8proc_int32_t uc) {
    return uc < 0 || uc >= 0x110000 ? utf8proc_properties : unsafe_get_property(uc);
}

static utf8proc_int32_t seqindex_decode_entry(const utf8proc_uint16_t **entry)
{
    utf8proc_int32_t entry_cp = **entry;
    if ((entry_cp & 0xF800) == 0xD800) {
        *entry = *entry + 1;
        entry_cp = ((entry_cp & 0x03FF) << 10) | (**entry & 0x03FF);
        entry_cp += 0x10000;
    }
    return entry_cp;
}

static utf8proc_int32_t seqindex_decode_index(const utf8proc_uint32_t seqindex)
{
    const utf8proc_uint16_t *entry = &utf8proc_sequences[seqindex];
    return seqindex_decode_entry(&entry);
}

UTF8PROC_DLLEXPORT utf8proc_int32_t utf8proc_tolower(utf8proc_int32_t c)
{
    utf8proc_int32_t cl = utf8proc_get_property(c)->lowercase_seqindex;
    return cl != UINT16_MAX ? seqindex_decode_index(cl) : c;
}

UTF8PROC_DLLEXPORT utf8proc_int32_t utf8proc_toupper(utf8proc_int32_t c)
{
    utf8proc_int32_t cu = utf8proc_get_property(c)->uppercase_seqindex;
    return cu != UINT16_MAX ? seqindex_decode_index(cu) : c;
}

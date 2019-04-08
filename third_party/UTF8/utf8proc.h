/*
 * Copyright (c) 2018 Steven G. Johnson, Jiahao Chen, Peter Colberg, Tony Kelman, Scott P. Jones, and other contributors.
 * Copyright (c) 2009 Public Software Group e. V., Berlin, Germany
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 */


/**
 * @mainpage
 *
 * utf8proc is a free/open-source (MIT/expat licensed) C library
 * providing Unicode normalization, case-folding, and other operations
 * for strings in the UTF-8 encoding, supporting Unicode version
 * 9.0.0.  See the utf8proc home page (http://julialang.org/utf8proc/)
 * for downloads and other information, or the source code on github
 * (https://github.com/JuliaLang/utf8proc).
 *
 * For the utf8proc API documentation, see: @ref utf8proc.h
 *
 * The features of utf8proc include:
 *
 * - Transformation of strings (@ref utf8proc_map) to:
 *    - decompose (@ref UTF8PROC_DECOMPOSE) or compose (@ref UTF8PROC_COMPOSE) Unicode combining characters (http://en.wikipedia.org/wiki/Combining_character)
 *    - canonicalize Unicode compatibility characters (@ref UTF8PROC_COMPAT)
 *    - strip "ignorable" (@ref UTF8PROC_IGNORE) characters, control characters (@ref UTF8PROC_STRIPCC), or combining characters such as accents (@ref UTF8PROC_STRIPMARK)
 *    - case-folding (@ref UTF8PROC_CASEFOLD)
 * - Unicode normalization: @ref utf8proc_NFD, @ref utf8proc_NFC, @ref utf8proc_NFKD, @ref utf8proc_NFKC
 * - Detecting grapheme boundaries (@ref utf8proc_grapheme_break and @ref UTF8PROC_CHARBOUND)
 * - Character-width computation: @ref utf8proc_charwidth
 * - Classification of characters by Unicode category: @ref utf8proc_category and @ref utf8proc_category_string
 * - Encode (@ref utf8proc_encode_char) and decode (@ref utf8proc_iterate) Unicode codepoints to/from UTF-8.
 */

/** @file */

#ifndef UTF8PROC_H
#define UTF8PROC_H

/** @name API version
 *
 * The utf8proc API version MAJOR.MINOR.PATCH, following
 * semantic-versioning rules (http://semver.org) based on API
 * compatibility.
 *
 * This is also returned at runtime by @ref utf8proc_version; however, the
 * runtime version may append a string like "-dev" to the version number
 * for prerelease versions.
 *
 * @note The shared-library version number in the Makefile
 *       (and CMakeLists.txt, and MANIFEST) may be different,
 *       being based on ABI compatibility rather than API compatibility.
 */

#if !MAC_OS_SANDBOX
#include <stdlib.h>
#include <limits.h>
#endif

#if defined(_MSC_VER) && _MSC_VER < 1800
// MSVC prior to 2013 lacked stdbool.h and inttypes.h
typedef signed char utf8proc_int8_t;
typedef unsigned char utf8proc_uint8_t;
typedef short utf8proc_int16_t;
typedef unsigned short utf8proc_uint16_t;
typedef int utf8proc_int32_t;
typedef unsigned int utf8proc_uint32_t;
#  ifdef _WIN64
typedef __int64 utf8proc_ssize_t;
typedef unsigned __int64 utf8proc_size_t;
#  else
typedef int utf8proc_ssize_t;
typedef unsigned int utf8proc_size_t;
#  endif
#  ifndef __cplusplus
// emulate C99 bool
typedef unsigned char utf8proc_bool;
#    ifndef __bool_true_false_are_defined
#      define false 0
#      define true 1
#      define __bool_true_false_are_defined 1
#    endif
#  else
typedef bool utf8proc_bool;
#  endif
#else
#  include <stddef.h>
#  include <stdbool.h>
#if MAC_OS_SANDBOX
#  include <stdint.h>
#else
#  include <inttypes.h>
#endif
typedef int8_t utf8proc_int8_t;
typedef uint8_t utf8proc_uint8_t;
typedef int16_t utf8proc_int16_t;
typedef uint16_t utf8proc_uint16_t;
typedef int32_t utf8proc_int32_t;
typedef uint32_t utf8proc_uint32_t;
typedef size_t utf8proc_size_t;
typedef ptrdiff_t utf8proc_ssize_t;
typedef bool utf8proc_bool;
#endif

#ifdef UTF8PROC_STATIC
#  define UTF8PROC_DLLEXPORT
#else
#  ifdef _WIN32
#    ifdef UTF8PROC_EXPORTS
#      define UTF8PROC_DLLEXPORT __declspec(dllexport)
#    else
#      define UTF8PROC_DLLEXPORT __declspec(dllimport)
#    endif
#  elif __GNUC__ >= 4
#    define UTF8PROC_DLLEXPORT __attribute__ ((visibility("default")))
#  else
#    define UTF8PROC_DLLEXPORT
#  endif
#endif

#ifdef __cplusplus
extern "C" {
#endif

    /**
     * Option flags used by several functions in the library.
     */
    typedef enum {
        /** The given UTF-8 input is NULL terminated. */
        UTF8PROC_NULLTERM  = (1<<0),
        /** Unicode Versioning Stability has to be respected. */
        UTF8PROC_STABLE    = (1<<1),
        /** Compatibility decomposition (i.e. formatting information is lost). */
        UTF8PROC_COMPAT    = (1<<2),
        /** Return a result with decomposed characters. */
        UTF8PROC_COMPOSE   = (1<<3),
        /** Return a result with decomposed characters. */
        UTF8PROC_DECOMPOSE = (1<<4),
        /** Strip "default ignorable characters" such as SOFT-HYPHEN or ZERO-WIDTH-SPACE. */
        UTF8PROC_IGNORE    = (1<<5),
        /** Return an error, if the input contains unassigned codepoints. */
        UTF8PROC_REJECTNA  = (1<<6),
        /**
         * Indicating that NLF-sequences (LF, CRLF, CR, NEL) are representing a
         * line break, and should be converted to the codepoint for line
         * separation (LS).
         */
        UTF8PROC_NLF2LS    = (1<<7),
        /**
         * Indicating that NLF-sequences are representing a paragraph break, and
         * should be converted to the codepoint for paragraph separation
         * (PS).
         */
        UTF8PROC_NLF2PS    = (1<<8),
        /** Indicating that the meaning of NLF-sequences is unknown. */
        UTF8PROC_NLF2LF    = (UTF8PROC_NLF2LS | UTF8PROC_NLF2PS),
        /** Strips and/or convers control characters.
         *
         * NLF-sequences are transformed into space, except if one of the
         * NLF2LS/PS/LF options is given. HorizontalTab (HT) and FormFeed (FF)
         * are treated as a NLF-sequence in this case.  All other control
         * characters are simply removed.
         */
        UTF8PROC_STRIPCC   = (1<<9),
        /**
         * Performs unicode case folding, to be able to do a case-insensitive
         * string comparison.
         */
        UTF8PROC_CASEFOLD  = (1<<10),
        /**
         * Inserts 0xFF bytes at the beginning of each sequence which is
         * representing a single grapheme cluster (see UAX#29).
         */
        UTF8PROC_CHARBOUND = (1<<11),
        /** Lumps certain characters together.
         *
         * E.g. HYPHEN U+2010 and MINUS U+2212 to ASCII "-". See lump.md for details.
         *
         * If NLF2LF is set, this includes a transformation of paragraph and
         * line separators to ASCII line-feed (LF).
         */
        UTF8PROC_LUMP      = (1<<12),
        /** Strips all character markings.
         *
         * This includes non-spacing, spacing and enclosing (i.e. accents).
         * @note This option works only with @ref UTF8PROC_COMPOSE or
         *       @ref UTF8PROC_DECOMPOSE
         */
        UTF8PROC_STRIPMARK = (1<<13),
        /**
         * Strip unassigned codepoints.
         */
        UTF8PROC_STRIPNA    = (1<<14),
    } utf8proc_option_t;

    /** @name Error codes
     * Error codes being returned by almost all functions.
     */
    /** @{ */
    /** Memory could not be allocated. */
#define UTF8PROC_ERROR_NOMEM -1
    /** The given string is too long to be processed. */
#define UTF8PROC_ERROR_OVERFLOW -2
    /** The given string is not a legal UTF-8 string. */
#define UTF8PROC_ERROR_INVALIDUTF8 -3
    /** The @ref UTF8PROC_REJECTNA flag was set and an unassigned codepoint was found. */
#define UTF8PROC_ERROR_NOTASSIGNED -4
    /** Invalid options have been used. */
#define UTF8PROC_ERROR_INVALIDOPTS -5
    /** @} */

    /* @name Types */

    /** Holds the value of a property. */
    typedef utf8proc_int16_t utf8proc_propval_t;

    /** Struct containing information about a codepoint. */
    typedef struct utf8proc_property_struct {
        /**
         * Unicode category.
         * @see utf8proc_category_t.
         */
        utf8proc_propval_t category;
        utf8proc_propval_t combining_class;
        /**
         * Bidirectional class.
         * @see utf8proc_bidi_class_t.
         */
        utf8proc_propval_t bidi_class;
        /**
         * @anchor Decomposition type.
         * @see utf8proc_decomp_type_t.
         */
        utf8proc_propval_t decomp_type;
        utf8proc_uint16_t decomp_seqindex;
        utf8proc_uint16_t casefold_seqindex;
        utf8proc_uint16_t uppercase_seqindex;
        utf8proc_uint16_t lowercase_seqindex;
        utf8proc_uint16_t titlecase_seqindex;
        utf8proc_uint16_t comb_index;
        unsigned bidi_mirrored:1;
        unsigned comp_exclusion:1;
        /**
         * Can this codepoint be ignored?
         *
         * Used by @ref utf8proc_decompose_char when @ref UTF8PROC_IGNORE is
         * passed as an option.
         */
        unsigned ignorable:1;
        unsigned control_boundary:1;
        /** The width of the codepoint. */
        unsigned charwidth:2;
        unsigned pad:2;
        /**
         * Boundclass.
         * @see utf8proc_boundclass_t.
         */
        unsigned boundclass:8;
    } utf8proc_property_t;

    /** Unicode categories. */
    typedef enum {
        UTF8PROC_CATEGORY_CN  = 0, /**< Other, not assigned */
        UTF8PROC_CATEGORY_LU  = 1, /**< Letter, uppercase */
        UTF8PROC_CATEGORY_LL  = 2, /**< Letter, lowercase */
        UTF8PROC_CATEGORY_LT  = 3, /**< Letter, titlecase */
        UTF8PROC_CATEGORY_LM  = 4, /**< Letter, modifier */
        UTF8PROC_CATEGORY_LO  = 5, /**< Letter, other */
        UTF8PROC_CATEGORY_MN  = 6, /**< Mark, nonspacing */
        UTF8PROC_CATEGORY_MC  = 7, /**< Mark, spacing combining */
        UTF8PROC_CATEGORY_ME  = 8, /**< Mark, enclosing */
        UTF8PROC_CATEGORY_ND  = 9, /**< Number, decimal digit */
        UTF8PROC_CATEGORY_NL = 10, /**< Number, letter */
        UTF8PROC_CATEGORY_NO = 11, /**< Number, other */
        UTF8PROC_CATEGORY_PC = 12, /**< Punctuation, connector */
        UTF8PROC_CATEGORY_PD = 13, /**< Punctuation, dash */
        UTF8PROC_CATEGORY_PS = 14, /**< Punctuation, open */
        UTF8PROC_CATEGORY_PE = 15, /**< Punctuation, close */
        UTF8PROC_CATEGORY_PI = 16, /**< Punctuation, initial quote */
        UTF8PROC_CATEGORY_PF = 17, /**< Punctuation, final quote */
        UTF8PROC_CATEGORY_PO = 18, /**< Punctuation, other */
        UTF8PROC_CATEGORY_SM = 19, /**< Symbol, math */
        UTF8PROC_CATEGORY_SC = 20, /**< Symbol, currency */
        UTF8PROC_CATEGORY_SK = 21, /**< Symbol, modifier */
        UTF8PROC_CATEGORY_SO = 22, /**< Symbol, other */
        UTF8PROC_CATEGORY_ZS = 23, /**< Separator, space */
        UTF8PROC_CATEGORY_ZL = 24, /**< Separator, line */
        UTF8PROC_CATEGORY_ZP = 25, /**< Separator, paragraph */
        UTF8PROC_CATEGORY_CC = 26, /**< Other, control */
        UTF8PROC_CATEGORY_CF = 27, /**< Other, format */
        UTF8PROC_CATEGORY_CS = 28, /**< Other, surrogate */
        UTF8PROC_CATEGORY_CO = 29, /**< Other, private use */
    } utf8proc_category_t;

    /** Bidirectional character classes. */
    typedef enum {
        UTF8PROC_BIDI_CLASS_L     = 1, /**< Left-to-Right */
        UTF8PROC_BIDI_CLASS_LRE   = 2, /**< Left-to-Right Embedding */
        UTF8PROC_BIDI_CLASS_LRO   = 3, /**< Left-to-Right Override */
        UTF8PROC_BIDI_CLASS_R     = 4, /**< Right-to-Left */
        UTF8PROC_BIDI_CLASS_AL    = 5, /**< Right-to-Left Arabic */
        UTF8PROC_BIDI_CLASS_RLE   = 6, /**< Right-to-Left Embedding */
        UTF8PROC_BIDI_CLASS_RLO   = 7, /**< Right-to-Left Override */
        UTF8PROC_BIDI_CLASS_PDF   = 8, /**< Pop Directional Format */
        UTF8PROC_BIDI_CLASS_EN    = 9, /**< European Number */
        UTF8PROC_BIDI_CLASS_ES   = 10, /**< European Separator */
        UTF8PROC_BIDI_CLASS_ET   = 11, /**< European Number Terminator */
        UTF8PROC_BIDI_CLASS_AN   = 12, /**< Arabic Number */
        UTF8PROC_BIDI_CLASS_CS   = 13, /**< Common Number Separator */
        UTF8PROC_BIDI_CLASS_NSM  = 14, /**< Nonspacing Mark */
        UTF8PROC_BIDI_CLASS_BN   = 15, /**< Boundary Neutral */
        UTF8PROC_BIDI_CLASS_B    = 16, /**< Paragraph Separator */
        UTF8PROC_BIDI_CLASS_S    = 17, /**< Segment Separator */
        UTF8PROC_BIDI_CLASS_WS   = 18, /**< Whitespace */
        UTF8PROC_BIDI_CLASS_ON   = 19, /**< Other Neutrals */
        UTF8PROC_BIDI_CLASS_LRI  = 20, /**< Left-to-Right Isolate */
        UTF8PROC_BIDI_CLASS_RLI  = 21, /**< Right-to-Left Isolate */
        UTF8PROC_BIDI_CLASS_FSI  = 22, /**< First Strong Isolate */
        UTF8PROC_BIDI_CLASS_PDI  = 23, /**< Pop Directional Isolate */
    } utf8proc_bidi_class_t;

    /** Decomposition type. */
    typedef enum {
        UTF8PROC_DECOMP_TYPE_FONT      = 1, /**< Font */
        UTF8PROC_DECOMP_TYPE_NOBREAK   = 2, /**< Nobreak */
        UTF8PROC_DECOMP_TYPE_INITIAL   = 3, /**< Initial */
        UTF8PROC_DECOMP_TYPE_MEDIAL    = 4, /**< Medial */
        UTF8PROC_DECOMP_TYPE_FINAL     = 5, /**< Final */
        UTF8PROC_DECOMP_TYPE_ISOLATED  = 6, /**< Isolated */
        UTF8PROC_DECOMP_TYPE_CIRCLE    = 7, /**< Circle */
        UTF8PROC_DECOMP_TYPE_SUPER     = 8, /**< Super */
        UTF8PROC_DECOMP_TYPE_SUB       = 9, /**< Sub */
        UTF8PROC_DECOMP_TYPE_VERTICAL = 10, /**< Vertical */
        UTF8PROC_DECOMP_TYPE_WIDE     = 11, /**< Wide */
        UTF8PROC_DECOMP_TYPE_NARROW   = 12, /**< Narrow */
        UTF8PROC_DECOMP_TYPE_SMALL    = 13, /**< Small */
        UTF8PROC_DECOMP_TYPE_SQUARE   = 14, /**< Square */
        UTF8PROC_DECOMP_TYPE_FRACTION = 15, /**< Fraction */
        UTF8PROC_DECOMP_TYPE_COMPAT   = 16, /**< Compat */
    } utf8proc_decomp_type_t;

    /** Boundclass property. (TR29) */
    typedef enum {
        UTF8PROC_BOUNDCLASS_START              =  0, /**< Start */
        UTF8PROC_BOUNDCLASS_OTHER              =  1, /**< Other */
        UTF8PROC_BOUNDCLASS_CR                 =  2, /**< Cr */
        UTF8PROC_BOUNDCLASS_LF                 =  3, /**< Lf */
        UTF8PROC_BOUNDCLASS_CONTROL            =  4, /**< Control */
        UTF8PROC_BOUNDCLASS_EXTEND             =  5, /**< Extend */
        UTF8PROC_BOUNDCLASS_L                  =  6, /**< L */
        UTF8PROC_BOUNDCLASS_V                  =  7, /**< V */
        UTF8PROC_BOUNDCLASS_T                  =  8, /**< T */
        UTF8PROC_BOUNDCLASS_LV                 =  9, /**< Lv */
        UTF8PROC_BOUNDCLASS_LVT                = 10, /**< Lvt */
        UTF8PROC_BOUNDCLASS_REGIONAL_INDICATOR = 11, /**< Regional indicator */
        UTF8PROC_BOUNDCLASS_SPACINGMARK        = 12, /**< Spacingmark */
        UTF8PROC_BOUNDCLASS_PREPEND            = 13, /**< Prepend */
        UTF8PROC_BOUNDCLASS_ZWJ                = 14, /**< Zero Width Joiner */

        /* the following are no longer used in Unicode 11, but we keep
         the constants here for backward compatibility */
        UTF8PROC_BOUNDCLASS_E_BASE             = 15, /**< Emoji Base */
        UTF8PROC_BOUNDCLASS_E_MODIFIER         = 16, /**< Emoji Modifier */
        UTF8PROC_BOUNDCLASS_GLUE_AFTER_ZWJ     = 17, /**< Glue_After_ZWJ */
        UTF8PROC_BOUNDCLASS_E_BASE_GAZ         = 18, /**< E_BASE + GLUE_AFTER_ZJW */

        /* the Extended_Pictographic property is used in the Unicode 11
         grapheme-boundary rules, so we store it in the boundclass field */
        UTF8PROC_BOUNDCLASS_EXTENDED_PICTOGRAPHIC = 19,
        UTF8PROC_BOUNDCLASS_E_ZWG = 20, /* UTF8PROC_BOUNDCLASS_EXTENDED_PICTOGRAPHIC + ZWJ */
    } utf8proc_boundclass_t;
    
    /**
     * Look up the properties for a given codepoint.
     *
     * @param codepoint The Unicode codepoint.
     *
     * @returns
     * A pointer to a (constant) struct containing information about
     * the codepoint.
     * @par
     * If the codepoint is unassigned or invalid, a pointer to a special struct is
     * returned in which `category` is 0 (@ref UTF8PROC_CATEGORY_CN).
     */
    UTF8PROC_DLLEXPORT const utf8proc_property_t *utf8proc_get_property(utf8proc_int32_t codepoint);

    /**
     * Given a codepoint `c`, return the codepoint of the corresponding
     * lower-case character, if any; otherwise (if there is no lower-case
     * variant, or if `c` is not a valid codepoint) return `c`.
     */
    UTF8PROC_DLLEXPORT utf8proc_int32_t utf8proc_tolower(utf8proc_int32_t c);

    /**
     * Given a codepoint `c`, return the codepoint of the corresponding
     * upper-case character, if any; otherwise (if there is no upper-case
     * variant, or if `c` is not a valid codepoint) return `c`.
     */
    UTF8PROC_DLLEXPORT utf8proc_int32_t utf8proc_toupper(utf8proc_int32_t c);

#ifdef __cplusplus
}
#endif

#endif

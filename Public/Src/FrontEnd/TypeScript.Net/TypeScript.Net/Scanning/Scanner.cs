// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Numerics;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Utilities;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.Extensions;
using TypeScript.Net.Types;
using static TypeScript.Net.Core.CoreUtilities;

namespace TypeScript.Net.Scanning
{
    /// <summary>
    /// Callback for error notification.
    /// </summary>
    public delegate void ErrorCallback(IDiagnosticMessage message, int length = 0);

    /// <nodoc/>
    public sealed class Scanner
    {
        private int m_lineStart;
        private readonly List<int> m_lineMap;
        private TextBuilder m_textBuilder;

        private static readonly Dictionary<string, SyntaxKind> s_textToToken = new Dictionary<string, SyntaxKind>()
        {
            { "abstract", SyntaxKind.AbstractKeyword },
            { "any", SyntaxKind.AnyKeyword },
            { "as", SyntaxKind.AsKeyword },
            { "boolean", SyntaxKind.BooleanKeyword },
            { "break", SyntaxKind.BreakKeyword },
            { "case", SyntaxKind.CaseKeyword },
            { "catch", SyntaxKind.CatchKeyword },
            { "class", SyntaxKind.ClassKeyword },
            { "continue", SyntaxKind.ContinueKeyword },
            { "const", SyntaxKind.ConstKeyword },
            { "constructor", SyntaxKind.ConstructorKeyword },
            { "debugger", SyntaxKind.DebuggerKeyword },
            { "declare", SyntaxKind.DeclareKeyword },
            { "default", SyntaxKind.DefaultKeyword },
            { "delete", SyntaxKind.DeleteKeyword },
            { "do", SyntaxKind.DoKeyword },
            { "else", SyntaxKind.ElseKeyword },
            { "enum", SyntaxKind.EnumKeyword },
            { "export", SyntaxKind.ExportKeyword },
            { "extends", SyntaxKind.ExtendsKeyword },
            { "false", SyntaxKind.FalseKeyword },
            { "finally", SyntaxKind.FinallyKeyword },
            { "for", SyntaxKind.ForKeyword },
            { "from", SyntaxKind.FromKeyword },
            { "function", SyntaxKind.FunctionKeyword },
            { "get", SyntaxKind.GetKeyword },
            { "if", SyntaxKind.IfKeyword },
            { "implements", SyntaxKind.ImplementsKeyword },
            { "import", SyntaxKind.ImportKeyword },
            { "in", SyntaxKind.InKeyword },
            { "instanceof", SyntaxKind.InstanceOfKeyword },
            { "interface", SyntaxKind.InterfaceKeyword },
            { "is", SyntaxKind.IsKeyword },
            { "let", SyntaxKind.LetKeyword },
            { "module", SyntaxKind.ModuleKeyword },
            { "namespace", SyntaxKind.NamespaceKeyword },
            { "new", SyntaxKind.NewKeyword },
            { "null", SyntaxKind.NullKeyword },
            { "number", SyntaxKind.NumberKeyword },
            { "package", SyntaxKind.PackageKeyword },
            { "private", SyntaxKind.PrivateKeyword },
            { "protected", SyntaxKind.ProtectedKeyword },
            { "public", SyntaxKind.PublicKeyword },
            { "readonly", SyntaxKind.ReadonlyKeyword },
            { "require", SyntaxKind.RequireKeyword },
            { "return", SyntaxKind.ReturnKeyword },
            { "set", SyntaxKind.SetKeyword },
            { "static", SyntaxKind.StaticKeyword },
            { "string", SyntaxKind.StringKeyword },
            { "super", SyntaxKind.SuperKeyword },
            { "switch", SyntaxKind.SwitchKeyword },
            { "symbol", SyntaxKind.SymbolKeyword },
            { "this", SyntaxKind.ThisKeyword },
            { "throw", SyntaxKind.ThrowKeyword },
            { "true", SyntaxKind.TrueKeyword },
            { "try", SyntaxKind.TryKeyword },
            { "type", SyntaxKind.TypeKeyword },
            { "typeof", SyntaxKind.TypeOfKeyword },
            { "var", SyntaxKind.VarKeyword },
            { "void", SyntaxKind.VoidKeyword },
            { "while", SyntaxKind.WhileKeyword },
            { "with", SyntaxKind.WithKeyword },
            { "yield", SyntaxKind.YieldKeyword },
            { "async", SyntaxKind.AsyncKeyword },
            { "await", SyntaxKind.AwaitKeyword },
            { "of", SyntaxKind.OfKeyword },
            { "{", SyntaxKind.OpenBraceToken },
            { "}", SyntaxKind.CloseBraceToken },
            { "(", SyntaxKind.OpenParenToken },
            { ")", SyntaxKind.CloseParenToken },
            { "[", SyntaxKind.OpenBracketToken },
            { "]", SyntaxKind.CloseBracketToken },
            { ".", SyntaxKind.DotToken },
            { "...", SyntaxKind.DotDotDotToken },
            { ";", SyntaxKind.SemicolonToken },
            { ",", SyntaxKind.CommaToken },
            { "<", SyntaxKind.LessThanToken },
            { ">", SyntaxKind.GreaterThanToken },
            { "<=", SyntaxKind.LessThanEqualsToken },
            { ">=", SyntaxKind.GreaterThanEqualsToken },
            { "==", SyntaxKind.EqualsEqualsToken },
            { "!=", SyntaxKind.ExclamationEqualsToken },
            { "===", SyntaxKind.EqualsEqualsEqualsToken },
            { "!==", SyntaxKind.ExclamationEqualsEqualsToken },
            { "=>", SyntaxKind.EqualsGreaterThanToken },
            { "+", SyntaxKind.PlusToken },
            { "-", SyntaxKind.MinusToken },
            { "**", SyntaxKind.AsteriskAsteriskToken },
            { "*", SyntaxKind.AsteriskToken },
            { "/", SyntaxKind.SlashToken },
            { "%", SyntaxKind.PercentToken },
            { "++", SyntaxKind.PlusPlusToken },
            { "--", SyntaxKind.MinusMinusToken },
            { "<<", SyntaxKind.LessThanLessThanToken },
            { "</", SyntaxKind.LessThanSlashToken },
            { ">>", SyntaxKind.GreaterThanGreaterThanToken },
            { ">>>", SyntaxKind.GreaterThanGreaterThanGreaterThanToken },
            { "&", SyntaxKind.AmpersandToken },
            { "|", SyntaxKind.BarToken },
            { "^", SyntaxKind.CaretToken },
            { "!", SyntaxKind.ExclamationToken },
            { "~", SyntaxKind.TildeToken },
            { "&&", SyntaxKind.AmpersandAmpersandToken },
            { "||", SyntaxKind.BarBarToken },
            { "?", SyntaxKind.QuestionToken },
            { ":", SyntaxKind.ColonToken },
            { "=", SyntaxKind.EqualsToken },
            { "+=", SyntaxKind.PlusEqualsToken },
            { "-=", SyntaxKind.MinusEqualsToken },
            { "*=", SyntaxKind.AsteriskEqualsToken },
            { "**=", SyntaxKind.AsteriskAsteriskEqualsToken },
            { "/=", SyntaxKind.SlashEqualsToken },
            { "%=", SyntaxKind.PercentEqualsToken },
            { "<<=", SyntaxKind.LessThanLessThanEqualsToken },
            { ">>=", SyntaxKind.GreaterThanGreaterThanEqualsToken },
            { ">>>=", SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken },
            { "&=", SyntaxKind.AmpersandEqualsToken },
            { "|=", SyntaxKind.BarEqualsToken },
            { "^=", SyntaxKind.CaretEqualsToken },
            { string.Empty, SyntaxKind.EndOfFileToken },

            // Currently there is no difference between real decorators and ambient decorators.
            // TypeScript team is considering to add ambient decorators with @@ syntax that would not have
            // any runtime semantic.
            { "@@", SyntaxKind.AtToken },
            { "@", SyntaxKind.AtToken },
        };

        /// <nodoc/>
        public static readonly Dictionary<SyntaxKind, string> TokenStrings = s_textToToken.ToDictionarySafe(
            kvp => kvp.Value,
            kvp => kvp.Key);

        /*
            As per ECMAScript Language Specification 3th Edition, Section 7.6: Identifiers
            IdentifierStart ::
                Can contain Unicode 3.0.0  categories:
                Uppercase letter (Lu),
                Lowercase letter (Ll),
                Titlecase letter (Lt),
                Modifier letter (Lm),
                Other letter (Lo), or
                Letter number (Nl).
            IdentifierPart :: =
                Can contain IdentifierStart + Unicode 3.0.0  categories:
                Non-spacing mark (Mn),
                Combining spacing mark (Mc),
                Decimal number (Nd), or
                Connector punctuation (Pc).

            Codepoint ranges for ES3 Identifiers are extracted from the Unicode 3.0.0 specification at:
            http://www.unicode.org/Public/3.0-Update/UnicodeData-3.0.0.txt
        */

        private static readonly int[] UnicodeEs3IdentifierStart =
        {
            170, 170, 181, 181, 186, 186, 192, 214, 216, 246, 248, 543, 546,
            563, 592, 685, 688, 696, 699, 705, 720, 721, 736, 740, 750, 750, 890, 890, 902, 902, 904, 906, 908, 908, 910, 929, 931,
            974, 976, 983, 986, 1011, 1024, 1153, 1164, 1220, 1223, 1224, 1227, 1228, 1232, 1269, 1272, 1273, 1329, 1366, 1369,
            1369, 1377, 1415, 1488, 1514, 1520, 1522, 1569, 1594, 1600, 1610, 1649, 1747, 1749, 1749, 1765, 1766, 1786, 1788, 1808,
            1808, 1810, 1836, 1920, 1957, 2309, 2361, 2365, 2365, 2384, 2384, 2392, 2401, 2437, 2444, 2447, 2448, 2451, 2472, 2474,
            2480, 2482, 2482, 2486, 2489, 2524, 2525, 2527, 2529, 2544, 2545, 2565, 2570, 2575, 2576, 2579, 2600, 2602, 2608, 2610,
            2611, 2613, 2614, 2616, 2617, 2649, 2652, 2654, 2654, 2674, 2676, 2693, 2699, 2701, 2701, 2703, 2705, 2707, 2728, 2730,
            2736, 2738, 2739, 2741, 2745, 2749, 2749, 2768, 2768, 2784, 2784, 2821, 2828, 2831, 2832, 2835, 2856, 2858, 2864, 2866,
            2867, 2870, 2873, 2877, 2877, 2908, 2909, 2911, 2913, 2949, 2954, 2958, 2960, 2962, 2965, 2969, 2970, 2972, 2972, 2974,
            2975, 2979, 2980, 2984, 2986, 2990, 2997, 2999, 3001, 3077, 3084, 3086, 3088, 3090, 3112, 3114, 3123, 3125, 3129, 3168,
            3169, 3205, 3212, 3214, 3216, 3218, 3240, 3242, 3251, 3253, 3257, 3294, 3294, 3296, 3297, 3333, 3340, 3342, 3344, 3346,
            3368, 3370, 3385, 3424, 3425, 3461, 3478, 3482, 3505, 3507, 3515, 3517, 3517, 3520, 3526, 3585, 3632, 3634, 3635, 3648,
            3654, 3713, 3714, 3716, 3716, 3719, 3720, 3722, 3722, 3725, 3725, 3732, 3735, 3737, 3743, 3745, 3747, 3749, 3749, 3751,
            3751, 3754, 3755, 3757, 3760, 3762, 3763, 3773, 3773, 3776, 3780, 3782, 3782, 3804, 3805, 3840, 3840, 3904, 3911, 3913,
            3946, 3976, 3979, 4096, 4129, 4131, 4135, 4137, 4138, 4176, 4181, 4256, 4293, 4304, 4342, 4352, 4441, 4447, 4514, 4520,
            4601, 4608, 4614, 4616, 4678, 4680, 4680, 4682, 4685, 4688, 4694, 4696, 4696, 4698, 4701, 4704, 4742, 4744, 4744, 4746,
            4749, 4752, 4782, 4784, 4784, 4786, 4789, 4792, 4798, 4800, 4800, 4802, 4805, 4808, 4814, 4816, 4822, 4824, 4846, 4848,
            4878, 4880, 4880, 4882, 4885, 4888, 4894, 4896, 4934, 4936, 4954, 5024, 5108, 5121, 5740, 5743, 5750, 5761, 5786, 5792,
            5866, 6016, 6067, 6176, 6263, 6272, 6312, 7680, 7835, 7840, 7929, 7936, 7957, 7960, 7965, 7968, 8005, 8008, 8013, 8016,
            8023, 8025, 8025, 8027, 8027, 8029, 8029, 8031, 8061, 8064, 8116, 8118, 8124, 8126, 8126, 8130, 8132, 8134, 8140, 8144,
            8147, 8150, 8155, 8160, 8172, 8178, 8180, 8182, 8188, 8319, 8319, 8450, 8450, 8455, 8455, 8458, 8467, 8469, 8469, 8473,
            8477, 8484, 8484, 8486, 8486, 8488, 8488, 8490, 8493, 8495, 8497, 8499, 8505, 8544, 8579, 12293, 12295, 12321, 12329,
            12337, 12341, 12344, 12346, 12353, 12436, 12445, 12446, 12449, 12538, 12540, 12542, 12549, 12588, 12593, 12686, 12704,
            12727, 13312, 19893, 19968, 40869, 40960, 42124, 44032, 55203, 63744, 64045, 64256, 64262, 64275, 64279, 64285, 64285,
            64287, 64296, 64298, 64310, 64312, 64316, 64318, 64318, 64320, 64321, 64323, 64324, 64326, 64433, 64467, 64829, 64848,
            64911, 64914, 64967, 65008, 65019, 65136, 65138, 65140, 65140, 65142, 65276, 65313, 65338, 65345, 65370, 65382, 65470,
            65474, 65479, 65482, 65487, 65490, 65495, 65498, 65500,
        };

        private static readonly int[] UnicodeEs3IdentifierPart =
        {
            170, 170, 181, 181, 186, 186, 192, 214, 216, 246, 248, 543, 546,
            563, 592, 685, 688, 696, 699, 705, 720, 721, 736, 740, 750, 750, 768, 846, 864, 866, 890, 890, 902, 902, 904, 906, 908,
            908, 910, 929, 931, 974, 976, 983, 986, 1011, 1024, 1153, 1155, 1158, 1164, 1220, 1223, 1224, 1227, 1228, 1232, 1269,
            1272, 1273, 1329, 1366, 1369, 1369, 1377, 1415, 1425, 1441, 1443, 1465, 1467, 1469, 1471, 1471, 1473, 1474, 1476, 1476,
            1488, 1514, 1520, 1522, 1569, 1594, 1600, 1621, 1632, 1641, 1648, 1747, 1749, 1756, 1759, 1768, 1770, 1773, 1776, 1788,
            1808, 1836, 1840, 1866, 1920, 1968, 2305, 2307, 2309, 2361, 2364, 2381, 2384, 2388, 2392, 2403, 2406, 2415, 2433, 2435,
            2437, 2444, 2447, 2448, 2451, 2472, 2474, 2480, 2482, 2482, 2486, 2489, 2492, 2492, 2494, 2500, 2503, 2504, 2507, 2509,
            2519, 2519, 2524, 2525, 2527, 2531, 2534, 2545, 2562, 2562, 2565, 2570, 2575, 2576, 2579, 2600, 2602, 2608, 2610, 2611,
            2613, 2614, 2616, 2617, 2620, 2620, 2622, 2626, 2631, 2632, 2635, 2637, 2649, 2652, 2654, 2654, 2662, 2676, 2689, 2691,
            2693, 2699, 2701, 2701, 2703, 2705, 2707, 2728, 2730, 2736, 2738, 2739, 2741, 2745, 2748, 2757, 2759, 2761, 2763, 2765,
            2768, 2768, 2784, 2784, 2790, 2799, 2817, 2819, 2821, 2828, 2831, 2832, 2835, 2856, 2858, 2864, 2866, 2867, 2870, 2873,
            2876, 2883, 2887, 2888, 2891, 2893, 2902, 2903, 2908, 2909, 2911, 2913, 2918, 2927, 2946, 2947, 2949, 2954, 2958, 2960,
            2962, 2965, 2969, 2970, 2972, 2972, 2974, 2975, 2979, 2980, 2984, 2986, 2990, 2997, 2999, 3001, 3006, 3010, 3014, 3016,
            3018, 3021, 3031, 3031, 3047, 3055, 3073, 3075, 3077, 3084, 3086, 3088, 3090, 3112, 3114, 3123, 3125, 3129, 3134, 3140,
            3142, 3144, 3146, 3149, 3157, 3158, 3168, 3169, 3174, 3183, 3202, 3203, 3205, 3212, 3214, 3216, 3218, 3240, 3242, 3251,
            3253, 3257, 3262, 3268, 3270, 3272, 3274, 3277, 3285, 3286, 3294, 3294, 3296, 3297, 3302, 3311, 3330, 3331, 3333, 3340,
            3342, 3344, 3346, 3368, 3370, 3385, 3390, 3395, 3398, 3400, 3402, 3405, 3415, 3415, 3424, 3425, 3430, 3439, 3458, 3459,
            3461, 3478, 3482, 3505, 3507, 3515, 3517, 3517, 3520, 3526, 3530, 3530, 3535, 3540, 3542, 3542, 3544, 3551, 3570, 3571,
            3585, 3642, 3648, 3662, 3664, 3673, 3713, 3714, 3716, 3716, 3719, 3720, 3722, 3722, 3725, 3725, 3732, 3735, 3737, 3743,
            3745, 3747, 3749, 3749, 3751, 3751, 3754, 3755, 3757, 3769, 3771, 3773, 3776, 3780, 3782, 3782, 3784, 3789, 3792, 3801,
            3804, 3805, 3840, 3840, 3864, 3865, 3872, 3881, 3893, 3893, 3895, 3895, 3897, 3897, 3902, 3911, 3913, 3946, 3953, 3972,
            3974, 3979, 3984, 3991, 3993, 4028, 4038, 4038, 4096, 4129, 4131, 4135, 4137, 4138, 4140, 4146, 4150, 4153, 4160, 4169,
            4176, 4185, 4256, 4293, 4304, 4342, 4352, 4441, 4447, 4514, 4520, 4601, 4608, 4614, 4616, 4678, 4680, 4680, 4682, 4685,
            4688, 4694, 4696, 4696, 4698, 4701, 4704, 4742, 4744, 4744, 4746, 4749, 4752, 4782, 4784, 4784, 4786, 4789, 4792, 4798,
            4800, 4800, 4802, 4805, 4808, 4814, 4816, 4822, 4824, 4846, 4848, 4878, 4880, 4880, 4882, 4885, 4888, 4894, 4896, 4934,
            4936, 4954, 4969, 4977, 5024, 5108, 5121, 5740, 5743, 5750, 5761, 5786, 5792, 5866, 6016, 6099, 6112, 6121, 6160, 6169,
            6176, 6263, 6272, 6313, 7680, 7835, 7840, 7929, 7936, 7957, 7960, 7965, 7968, 8005, 8008, 8013, 8016, 8023, 8025, 8025,
            8027, 8027, 8029, 8029, 8031, 8061, 8064, 8116, 8118, 8124, 8126, 8126, 8130, 8132, 8134, 8140, 8144, 8147, 8150, 8155,
            8160, 8172, 8178, 8180, 8182, 8188, 8255, 8256, 8319, 8319, 8400, 8412, 8417, 8417, 8450, 8450, 8455, 8455, 8458, 8467,
            8469, 8469, 8473, 8477, 8484, 8484, 8486, 8486, 8488, 8488, 8490, 8493, 8495, 8497, 8499, 8505, 8544, 8579, 12293,
            12295, 12321, 12335, 12337, 12341, 12344, 12346, 12353, 12436, 12441, 12442, 12445, 12446, 12449, 12542, 12549, 12588,
            12593, 12686, 12704, 12727, 13312, 19893, 19968, 40869, 40960, 42124, 44032, 55203, 63744, 64045, 64256, 64262, 64275,
            64279, 64285, 64296, 64298, 64310, 64312, 64316, 64318, 64318, 64320, 64321, 64323, 64324, 64326, 64433, 64467, 64829,
            64848, 64911, 64914, 64967, 65008, 65019, 65056, 65059, 65075, 65076, 65101, 65103, 65136, 65138, 65140, 65140, 65142,
            65276, 65296, 65305, 65313, 65338, 65343, 65343, 65345, 65370, 65381, 65470, 65474, 65479, 65482, 65487, 65490, 65495,
            65498, 65500,
        };

        /*
            As per ECMAScript Language Specification 5th Edition, Section 7.6: ISyntaxToken Names and Identifiers
            IdentifierStart ::
                Can contain Unicode 6.2  categories:
                Uppercase letter (Lu),
                Lowercase letter (Ll),
                Titlecase letter (Lt),
                Modifier letter (Lm),
                Other letter (Lo), or
                Letter number (Nl).
            IdentifierPart ::
                Can contain IdentifierStart + Unicode 6.2  categories:
                Non-spacing mark (Mn),
                Combining spacing mark (Mc),
                Decimal number (Nd),
                Connector punctuation (Pc),
                <ZWNJ>, or
                <ZWJ>.

            Codepoint ranges for ES5 Identifiers are extracted from the Unicode 6.2 specification at:
            http://www.unicode.org/Public/6.2.0/ucd/UnicodeData.txt
        */

        private static readonly int[] UnicodeEs5IdentifierStart =
        {
            170, 170, 181, 181, 186, 186, 192, 214, 216, 246, 248, 705, 710,
            721, 736, 740, 748, 748, 750, 750, 880, 884, 886, 887, 890, 893, 902, 902, 904, 906, 908, 908, 910, 929, 931, 1013,
            1015, 1153, 1162, 1319, 1329, 1366, 1369, 1369, 1377, 1415, 1488, 1514, 1520, 1522, 1568, 1610, 1646, 1647, 1649, 1747,
            1749, 1749, 1765, 1766, 1774, 1775, 1786, 1788, 1791, 1791, 1808, 1808, 1810, 1839, 1869, 1957, 1969, 1969, 1994, 2026,
            2036, 2037, 2042, 2042, 2048, 2069, 2074, 2074, 2084, 2084, 2088, 2088, 2112, 2136, 2208, 2208, 2210, 2220, 2308, 2361,
            2365, 2365, 2384, 2384, 2392, 2401, 2417, 2423, 2425, 2431, 2437, 2444, 2447, 2448, 2451, 2472, 2474, 2480, 2482, 2482,
            2486, 2489, 2493, 2493, 2510, 2510, 2524, 2525, 2527, 2529, 2544, 2545, 2565, 2570, 2575, 2576, 2579, 2600, 2602, 2608,
            2610, 2611, 2613, 2614, 2616, 2617, 2649, 2652, 2654, 2654, 2674, 2676, 2693, 2701, 2703, 2705, 2707, 2728, 2730, 2736,
            2738, 2739, 2741, 2745, 2749, 2749, 2768, 2768, 2784, 2785, 2821, 2828, 2831, 2832, 2835, 2856, 2858, 2864, 2866, 2867,
            2869, 2873, 2877, 2877, 2908, 2909, 2911, 2913, 2929, 2929, 2947, 2947, 2949, 2954, 2958, 2960, 2962, 2965, 2969, 2970,
            2972, 2972, 2974, 2975, 2979, 2980, 2984, 2986, 2990, 3001, 3024, 3024, 3077, 3084, 3086, 3088, 3090, 3112, 3114, 3123,
            3125, 3129, 3133, 3133, 3160, 3161, 3168, 3169, 3205, 3212, 3214, 3216, 3218, 3240, 3242, 3251, 3253, 3257, 3261, 3261,
            3294, 3294, 3296, 3297, 3313, 3314, 3333, 3340, 3342, 3344, 3346, 3386, 3389, 3389, 3406, 3406, 3424, 3425, 3450, 3455,
            3461, 3478, 3482, 3505, 3507, 3515, 3517, 3517, 3520, 3526, 3585, 3632, 3634, 3635, 3648, 3654, 3713, 3714, 3716, 3716,
            3719, 3720, 3722, 3722, 3725, 3725, 3732, 3735, 3737, 3743, 3745, 3747, 3749, 3749, 3751, 3751, 3754, 3755, 3757, 3760,
            3762, 3763, 3773, 3773, 3776, 3780, 3782, 3782, 3804, 3807, 3840, 3840, 3904, 3911, 3913, 3948, 3976, 3980, 4096, 4138,
            4159, 4159, 4176, 4181, 4186, 4189, 4193, 4193, 4197, 4198, 4206, 4208, 4213, 4225, 4238, 4238, 4256, 4293, 4295, 4295,
            4301, 4301, 4304, 4346, 4348, 4680, 4682, 4685, 4688, 4694, 4696, 4696, 4698, 4701, 4704, 4744, 4746, 4749, 4752, 4784,
            4786, 4789, 4792, 4798, 4800, 4800, 4802, 4805, 4808, 4822, 4824, 4880, 4882, 4885, 4888, 4954, 4992, 5007, 5024, 5108,
            5121, 5740, 5743, 5759, 5761, 5786, 5792, 5866, 5870, 5872, 5888, 5900, 5902, 5905, 5920, 5937, 5952, 5969, 5984, 5996,
            5998, 6000, 6016, 6067, 6103, 6103, 6108, 6108, 6176, 6263, 6272, 6312, 6314, 6314, 6320, 6389, 6400, 6428, 6480, 6509,
            6512, 6516, 6528, 6571, 6593, 6599, 6656, 6678, 6688, 6740, 6823, 6823, 6917, 6963, 6981, 6987, 7043, 7072, 7086, 7087,
            7098, 7141, 7168, 7203, 7245, 7247, 7258, 7293, 7401, 7404, 7406, 7409, 7413, 7414, 7424, 7615, 7680, 7957, 7960, 7965,
            7968, 8005, 8008, 8013, 8016, 8023, 8025, 8025, 8027, 8027, 8029, 8029, 8031, 8061, 8064, 8116, 8118, 8124, 8126, 8126,
            8130, 8132, 8134, 8140, 8144, 8147, 8150, 8155, 8160, 8172, 8178, 8180, 8182, 8188, 8305, 8305, 8319, 8319, 8336, 8348,
            8450, 8450, 8455, 8455, 8458, 8467, 8469, 8469, 8473, 8477, 8484, 8484, 8486, 8486, 8488, 8488, 8490, 8493, 8495, 8505,
            8508, 8511, 8517, 8521, 8526, 8526, 8544, 8584, 11264, 11310, 11312, 11358, 11360, 11492, 11499, 11502, 11506, 11507,
            11520, 11557, 11559, 11559, 11565, 11565, 11568, 11623, 11631, 11631, 11648, 11670, 11680, 11686, 11688, 11694, 11696,
            11702, 11704, 11710, 11712, 11718, 11720, 11726, 11728, 11734, 11736, 11742, 11823, 11823, 12293, 12295, 12321, 12329,
            12337, 12341, 12344, 12348, 12353, 12438, 12445, 12447, 12449, 12538, 12540, 12543, 12549, 12589, 12593, 12686, 12704,
            12730, 12784, 12799, 13312, 19893, 19968, 40908, 40960, 42124, 42192, 42237, 42240, 42508, 42512, 42527, 42538, 42539,
            42560, 42606, 42623, 42647, 42656, 42735, 42775, 42783, 42786, 42888, 42891, 42894, 42896, 42899, 42912, 42922, 43000,
            43009, 43011, 43013, 43015, 43018, 43020, 43042, 43072, 43123, 43138, 43187, 43250, 43255, 43259, 43259, 43274, 43301,
            43312, 43334, 43360, 43388, 43396, 43442, 43471, 43471, 43520, 43560, 43584, 43586, 43588, 43595, 43616, 43638, 43642,
            43642, 43648, 43695, 43697, 43697, 43701, 43702, 43705, 43709, 43712, 43712, 43714, 43714, 43739, 43741, 43744, 43754,
            43762, 43764, 43777, 43782, 43785, 43790, 43793, 43798, 43808, 43814, 43816, 43822, 43968, 44002, 44032, 55203, 55216,
            55238, 55243, 55291, 63744, 64109, 64112, 64217, 64256, 64262, 64275, 64279, 64285, 64285, 64287, 64296, 64298, 64310,
            64312, 64316, 64318, 64318, 64320, 64321, 64323, 64324, 64326, 64433, 64467, 64829, 64848, 64911, 64914, 64967, 65008,
            65019, 65136, 65140, 65142, 65276, 65313, 65338, 65345, 65370, 65382, 65470, 65474, 65479, 65482, 65487, 65490, 65495,
            65498, 65500,
        };

        private static readonly int[] UnicodeEs5IdentifierPart =
        {
            170, 170, 181, 181, 186, 186, 192, 214, 216, 246, 248, 705, 710,
            721, 736, 740, 748, 748, 750, 750, 768, 884, 886, 887, 890, 893, 902, 902, 904, 906, 908, 908, 910, 929, 931, 1013,
            1015, 1153, 1155, 1159, 1162, 1319, 1329, 1366, 1369, 1369, 1377, 1415, 1425, 1469, 1471, 1471, 1473, 1474, 1476, 1477,
            1479, 1479, 1488, 1514, 1520, 1522, 1552, 1562, 1568, 1641, 1646, 1747, 1749, 1756, 1759, 1768, 1770, 1788, 1791, 1791,
            1808, 1866, 1869, 1969, 1984, 2037, 2042, 2042, 2048, 2093, 2112, 2139, 2208, 2208, 2210, 2220, 2276, 2302, 2304, 2403,
            2406, 2415, 2417, 2423, 2425, 2431, 2433, 2435, 2437, 2444, 2447, 2448, 2451, 2472, 2474, 2480, 2482, 2482, 2486, 2489,
            2492, 2500, 2503, 2504, 2507, 2510, 2519, 2519, 2524, 2525, 2527, 2531, 2534, 2545, 2561, 2563, 2565, 2570, 2575, 2576,
            2579, 2600, 2602, 2608, 2610, 2611, 2613, 2614, 2616, 2617, 2620, 2620, 2622, 2626, 2631, 2632, 2635, 2637, 2641, 2641,
            2649, 2652, 2654, 2654, 2662, 2677, 2689, 2691, 2693, 2701, 2703, 2705, 2707, 2728, 2730, 2736, 2738, 2739, 2741, 2745,
            2748, 2757, 2759, 2761, 2763, 2765, 2768, 2768, 2784, 2787, 2790, 2799, 2817, 2819, 2821, 2828, 2831, 2832, 2835, 2856,
            2858, 2864, 2866, 2867, 2869, 2873, 2876, 2884, 2887, 2888, 2891, 2893, 2902, 2903, 2908, 2909, 2911, 2915, 2918, 2927,
            2929, 2929, 2946, 2947, 2949, 2954, 2958, 2960, 2962, 2965, 2969, 2970, 2972, 2972, 2974, 2975, 2979, 2980, 2984, 2986,
            2990, 3001, 3006, 3010, 3014, 3016, 3018, 3021, 3024, 3024, 3031, 3031, 3046, 3055, 3073, 3075, 3077, 3084, 3086, 3088,
            3090, 3112, 3114, 3123, 3125, 3129, 3133, 3140, 3142, 3144, 3146, 3149, 3157, 3158, 3160, 3161, 3168, 3171, 3174, 3183,
            3202, 3203, 3205, 3212, 3214, 3216, 3218, 3240, 3242, 3251, 3253, 3257, 3260, 3268, 3270, 3272, 3274, 3277, 3285, 3286,
            3294, 3294, 3296, 3299, 3302, 3311, 3313, 3314, 3330, 3331, 3333, 3340, 3342, 3344, 3346, 3386, 3389, 3396, 3398, 3400,
            3402, 3406, 3415, 3415, 3424, 3427, 3430, 3439, 3450, 3455, 3458, 3459, 3461, 3478, 3482, 3505, 3507, 3515, 3517, 3517,
            3520, 3526, 3530, 3530, 3535, 3540, 3542, 3542, 3544, 3551, 3570, 3571, 3585, 3642, 3648, 3662, 3664, 3673, 3713, 3714,
            3716, 3716, 3719, 3720, 3722, 3722, 3725, 3725, 3732, 3735, 3737, 3743, 3745, 3747, 3749, 3749, 3751, 3751, 3754, 3755,
            3757, 3769, 3771, 3773, 3776, 3780, 3782, 3782, 3784, 3789, 3792, 3801, 3804, 3807, 3840, 3840, 3864, 3865, 3872, 3881,
            3893, 3893, 3895, 3895, 3897, 3897, 3902, 3911, 3913, 3948, 3953, 3972, 3974, 3991, 3993, 4028, 4038, 4038, 4096, 4169,
            4176, 4253, 4256, 4293, 4295, 4295, 4301, 4301, 4304, 4346, 4348, 4680, 4682, 4685, 4688, 4694, 4696, 4696, 4698, 4701,
            4704, 4744, 4746, 4749, 4752, 4784, 4786, 4789, 4792, 4798, 4800, 4800, 4802, 4805, 4808, 4822, 4824, 4880, 4882, 4885,
            4888, 4954, 4957, 4959, 4992, 5007, 5024, 5108, 5121, 5740, 5743, 5759, 5761, 5786, 5792, 5866, 5870, 5872, 5888, 5900,
            5902, 5908, 5920, 5940, 5952, 5971, 5984, 5996, 5998, 6000, 6002, 6003, 6016, 6099, 6103, 6103, 6108, 6109, 6112, 6121,
            6155, 6157, 6160, 6169, 6176, 6263, 6272, 6314, 6320, 6389, 6400, 6428, 6432, 6443, 6448, 6459, 6470, 6509, 6512, 6516,
            6528, 6571, 6576, 6601, 6608, 6617, 6656, 6683, 6688, 6750, 6752, 6780, 6783, 6793, 6800, 6809, 6823, 6823, 6912, 6987,
            6992, 7001, 7019, 7027, 7040, 7155, 7168, 7223, 7232, 7241, 7245, 7293, 7376, 7378, 7380, 7414, 7424, 7654, 7676, 7957,
            7960, 7965, 7968, 8005, 8008, 8013, 8016, 8023, 8025, 8025, 8027, 8027, 8029, 8029, 8031, 8061, 8064, 8116, 8118, 8124,
            8126, 8126, 8130, 8132, 8134, 8140, 8144, 8147, 8150, 8155, 8160, 8172, 8178, 8180, 8182, 8188, 8204, 8205, 8255, 8256,
            8276, 8276, 8305, 8305, 8319, 8319, 8336, 8348, 8400, 8412, 8417, 8417, 8421, 8432, 8450, 8450, 8455, 8455, 8458, 8467,
            8469, 8469, 8473, 8477, 8484, 8484, 8486, 8486, 8488, 8488, 8490, 8493, 8495, 8505, 8508, 8511, 8517, 8521, 8526, 8526,
            8544, 8584, 11264, 11310, 11312, 11358, 11360, 11492, 11499, 11507, 11520, 11557, 11559, 11559, 11565, 11565, 11568,
            11623, 11631, 11631, 11647, 11670, 11680, 11686, 11688, 11694, 11696, 11702, 11704, 11710, 11712, 11718, 11720, 11726,
            11728, 11734, 11736, 11742, 11744, 11775, 11823, 11823, 12293, 12295, 12321, 12335, 12337, 12341, 12344, 12348, 12353,
            12438, 12441, 12442, 12445, 12447, 12449, 12538, 12540, 12543, 12549, 12589, 12593, 12686, 12704, 12730, 12784, 12799,
            13312, 19893, 19968, 40908, 40960, 42124, 42192, 42237, 42240, 42508, 42512, 42539, 42560, 42607, 42612, 42621, 42623,
            42647, 42655, 42737, 42775, 42783, 42786, 42888, 42891, 42894, 42896, 42899, 42912, 42922, 43000, 43047, 43072, 43123,
            43136, 43204, 43216, 43225, 43232, 43255, 43259, 43259, 43264, 43309, 43312, 43347, 43360, 43388, 43392, 43456, 43471,
            43481, 43520, 43574, 43584, 43597, 43600, 43609, 43616, 43638, 43642, 43643, 43648, 43714, 43739, 43741, 43744, 43759,
            43762, 43766, 43777, 43782, 43785, 43790, 43793, 43798, 43808, 43814, 43816, 43822, 43968, 44010, 44012, 44013, 44016,
            44025, 44032, 55203, 55216, 55238, 55243, 55291, 63744, 64109, 64112, 64217, 64256, 64262, 64275, 64279, 64285, 64296,
            64298, 64310, 64312, 64316, 64318, 64318, 64320, 64321, 64323, 64324, 64326, 64433, 64467, 64829, 64848, 64911, 64914,
            64967, 65008, 65019, 65024, 65039, 65056, 65062, 65075, 65076, 65101, 65103, 65136, 65140, 65142, 65276, 65296, 65305,
            65313, 65338, 65343, 65343, 65345, 65370, 65382, 65470, 65474, 65479, 65482, 65487, 65490, 65495, 65498, 65500,
        };

        // All conflict markers consist of the same character repeated seven times.  If it is
        // a <<<<<<< or >>>>>>> marker then it is also followd by a space.
        private static readonly int MergeConflictMarkerLength = "<<<<<<<".Length;
        private int m_pos;
        private int m_end;
        private int m_startPos;
        private int m_tokenPos;
        private SyntaxKind m_token;
        private bool m_needSkipTrivia;
        private bool m_allowBackslashesInPathInterpolation;

        // Trivial to collect and associate with nodes when m_needSkipTrivia is false (i.e. preserveTrivia)
        private int m_newLineTriviaCount;
        private readonly List<Trivia.Comment> m_comments;
        private INode m_lastNodeForTrivia;

        private TextSource m_text;
        private string m_tokenValue;
        private bool m_precedingLineBreak;
        private bool m_hasExtendedUnicodeEscape;
        private bool m_tokenIsUnterminated;
        private ErrorCallback m_onError;

        private ScriptTarget m_languageVersion;
        private LanguageVariant m_languageVariant;
        private readonly bool m_preserveComments;

        private ISourceFile m_sourceFile;

        /// <summary>
        /// Internal constructor used for pooling the scanner instances.
        /// </summary>
        internal Scanner()
        {
            m_comments = new List<Trivia.Comment>();
            m_lineMap = new List<int>();
        }

        /// <nodoc/>
        public Scanner(
            ScriptTarget languageVersion,
            bool preserveTrivia,
            bool allowBackslashesInPathInterpolation,
            LanguageVariant languageVariant = LanguageVariant.Standard,
            TextSource text = null,
            ErrorCallback onError = null,
            int? start = null,
            int? length = null,
            TextBuilder textBuilder = null,
            bool preserveComments = false)
        {
            m_languageVersion = languageVersion;
            m_needSkipTrivia = !preserveTrivia;
            m_allowBackslashesInPathInterpolation = allowBackslashesInPathInterpolation;
            m_languageVariant = languageVariant;
            m_onError = onError;
            m_textBuilder = textBuilder ?? new TextBuilder();
            m_preserveComments = preserveComments;

            m_comments = new List<Trivia.Comment>();
            m_lineMap = new List<int>();
            SetText(text, start, length);
        }

        /// <nodoc/>
        public static Scanner CreateScanner(
            ScriptTarget languageVersion,
            bool preserveTrivia,
            bool allowBackslashesInPathInterpolation,
            LanguageVariant languageVariant = LanguageVariant.Standard,
            TextSource text = null,
            ErrorCallback onError = null,
            int? start = null,
            int? length = null)
        {
            return new Scanner(languageVersion, preserveTrivia, allowBackslashesInPathInterpolation, languageVariant, text, onError, start, length);
        }

        private static PooledObjectWrapper<Scanner> CreateScanner(TextSource text, bool allowBackslashesInPathInterpolation, int startPosition, PooledObjectWrapper<TextBuilder> textBuilderWrapper)
        {
            var scanner = Utilities.Pools.ScannerPool.GetInstance();

            scanner.Instance.InitPooledInstance(
                ScriptTarget.Latest,
                preserveTrivia: false,
                allowBackslashesInPathInterpolation: allowBackslashesInPathInterpolation,
                text: text,
                start: startPosition,
                textBuilder: textBuilderWrapper.Instance);

            return scanner;
        }

        private void InitPooledInstance(
            ScriptTarget languageVersion,
            bool preserveTrivia,
            bool allowBackslashesInPathInterpolation,
            LanguageVariant languageVariant = LanguageVariant.Standard,
            TextSource text = null,
            ErrorCallback onError = null,
            int? start = null,
            int? length = null,
            TextBuilder textBuilder = null)
        {
            m_languageVersion = languageVersion;
            m_needSkipTrivia = !preserveTrivia;
            m_allowBackslashesInPathInterpolation = allowBackslashesInPathInterpolation;
            m_languageVariant = languageVariant;
            m_onError = onError;
            m_textBuilder = textBuilder ?? new TextBuilder();

            SetText(text, start, length);
        }

        /// <summary>
        /// Source text.
        /// </summary>
        /// <remarks>
        /// Used only by syntax scanner.
        /// </remarks>
        public TextSource Source => m_text;

        /// <summary>
        /// Sets the current source file.
        /// </summary>
        public void SetSourceFile(ISourceFile sourceFile) => m_sourceFile = sourceFile;

        /// <nodoc/>
        public List<int> LineMap => m_lineMap;

        /// <nodoc/>
        public int StartPos => m_startPos;

        /// <nodoc/>
        public SyntaxKind Token => m_token;

        /// <nodoc/>
        public int TextPos => m_pos;

        /// <nodoc/>
        public int TokenPos => m_tokenPos;

        internal CharacterCodes CurrentCharacter => m_text.CharCodeAt(TokenPos);

        internal CharacterCodes NextCharacter => TokenPos + 1 < m_text.Length ? m_text.CharCodeAt(TokenPos + 1) : CharacterCodes.NullCharacter;

        /// <nodoc/>
        public string TokenText => m_text.SubstringFromTo(m_tokenPos, m_pos);

        /// <nodoc/>
        public string TokenValue => m_tokenValue;

        /// <nodoc/>
        public bool HasExtendedUnicodeEscape => m_hasExtendedUnicodeEscape;

        /// <nodoc/>
        public bool HasPrecedingLineBreak => m_precedingLineBreak;

        /// <nodoc/>
        public bool IsIdentifier => m_token == SyntaxKind.Identifier || m_token > SyntaxKind.LastReservedWord;

        /// <nodoc/>
        public bool IsReservedWord => m_token >= SyntaxKind.FirstReservedWord && m_token <= SyntaxKind.LastReservedWord;

        /// <nodoc/>
        public bool IsUnterminated => m_tokenIsUnterminated;

        /// <nodoc/>
        /* @internal */
        public static bool IsOctalDigit(int ch)
        {
            return ch >= (int)CharacterCodes._0 && ch <= (int)CharacterCodes._7;
        }

        /// <nodoc/>
        public static string TokenToString(SyntaxKind t)
        {
            return TokenStrings[t];
        }

        /// <nodoc/>
        public static ICommentRange[] GetCommentRanges(TextSource text, int pos, bool trailing)
        {
            var result = new List<ICommentRange>();
            var collecting = trailing || pos == 0;
            while (pos < text.Length)
            {
                var ch = text.CharCodeAt(pos);
                switch (ch)
                {
                    case CharacterCodes.CarriageReturn:
                        if (text.CharCodeAt(pos + 1) == CharacterCodes.LineFeed)
                        {
                            pos++;
                        }

                        goto case CharacterCodes.LineFeed;
                    case CharacterCodes.LineFeed:
                        pos++;
                        if (trailing)
                        {
                            return result.ToArray();
                        }

                        collecting = true;
                        if (result.Count != 0)
                        {
                            result.Last().HasTrailingNewLine = true;
                        }

                        continue;
                    case CharacterCodes.Tab:
                    case CharacterCodes.VerticalTab:
                    case CharacterCodes.FormFeed:
                    case CharacterCodes.Space:
                        pos++;
                        continue;
                    case CharacterCodes.Slash:
                        var nextChar = text.CharCodeAt(pos + 1);
                        var hasTrailingNewLine = false;
                        if (nextChar == CharacterCodes.Slash || nextChar == CharacterCodes.Asterisk)
                        {
                            var kind = nextChar == CharacterCodes.Slash
                                ? SyntaxKind.SingleLineCommentTrivia
                                : SyntaxKind.MultiLineCommentTrivia;
                            var startPos = pos;
                            pos += 2;
                            if (nextChar == CharacterCodes.Slash)
                            {
                                while (pos < text.Length)
                                {
                                    if (IsLineBreak(text.CharCodeAt(pos)))
                                    {
                                        hasTrailingNewLine = true;
                                        break;
                                    }

                                    pos++;
                                }
                            }
                            else
                            {
                                while (pos < text.Length)
                                {
                                    if (text.CharCodeAt(pos) == CharacterCodes.Asterisk &&
                                        text.CharCodeAt(pos + 1) == CharacterCodes.Slash)
                                    {
                                        pos += 2;
                                        break;
                                    }

                                    pos++;
                                }
                            }

                            if (collecting)
                            {
                                result.Add(new CommentRange
                                {
                                    Pos = startPos,
                                    End = pos,
                                    HasTrailingNewLine = hasTrailingNewLine,
                                    Kind = kind,
                                });
                            }

                            continue;
                        }

                        break;
                    default:
                        if (ch > CharacterCodes.MaxAsciiCharacter && (IsWhiteSpace(ch) || IsLineBreak(ch)))
                        {
                            if (result.Count != 0 && IsLineBreak(ch))
                            {
                                result.Last().HasTrailingNewLine = true;
                            }

                            pos++;
                            continue;
                        }

                        break;
                }

                return result.ToArray();
            }

            return result.ToArray();
        }

        /// <nodoc/>
        public static LineAndColumn GetLineAndCharacterOfPosition(ISourceFile sourceFile, int position)
        {
            return ExpensiveComputeLineAndCharacterOfPositionSeeTask646652(sourceFile.LineMap.Map, position);
        }

        /// <summary>
        /// Gets line and column of a given position, but stripping trivia first.
        /// </summary>
        /// <remarks>
        /// This function is not originally part of the scanner, and it is used by DScript for AstConversion
        /// </remarks>
        public static LineAndColumn GetLineAndCharacterOfPositionSkippingTrivia(ISourceFile sourceFile, int position)
        {
            position = SkipOverTrivia(sourceFile, position);
            return ExpensiveComputeLineAndCharacterOfPositionSeeTask646652(sourceFile.LineMap.Map, position);
        }

        /// <nodoc/>
        public static ICommentRange[] GetLeadingCommentRanges(TextSource text, int pos)
        {
            return GetCommentRanges(text, pos, /*trailing*/ false);
        }

        /// <nodoc/>
        public static ICommentRange[] GetTrailingCommentRanges(TextSource text, int pos)
        {
            return GetCommentRanges(text, pos, /*trailing*/ true);
        }

        /// <nodoc/>
        public SyntaxKind RescanGreaterToken()
        {
            if (m_token == SyntaxKind.GreaterThanToken)
            {
                if (m_text.CharCodeAt(m_pos) == CharacterCodes.GreaterThan)
                {
                    if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.GreaterThan)
                    {
                        if (m_text.CharCodeAt(m_pos + 2) == CharacterCodes.equals)
                        {
                            m_pos += 3;
                            m_token = SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken;
                            return m_token;
                        }

                        m_pos += 2;
                        m_token = SyntaxKind.GreaterThanGreaterThanGreaterThanToken;
                        return m_token;
                    }

                    if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.equals)
                    {
                        m_pos += 2;
                        m_token = SyntaxKind.GreaterThanGreaterThanEqualsToken;
                        return m_token;
                    }

                    m_pos++;
                    m_token = SyntaxKind.GreaterThanGreaterThanToken;
                    return m_token;
                }

                if (m_text.CharCodeAt(m_pos) == CharacterCodes.equals)
                {
                    m_pos++;
                    m_token = SyntaxKind.GreaterThanEqualsToken;
                    return m_token;
                }
            }

            return m_token;
        }

        /// <nodoc/>
        public SyntaxKind RescanSlashToken()
        {
            if (m_token == SyntaxKind.SlashToken || m_token == SyntaxKind.SlashEqualsToken)
            {
                var p = m_tokenPos + 1;
                var inEscape = false;
                var inCharacterClass = false;
                while (true)
                {
                    // If we reach the end of a file, or hit a newline, then this is an unterminated
                    // regex.  Report error and return what we have so far.
                    if (p >= m_end)
                    {
                        m_tokenIsUnterminated = true;
                        Error(Errors.Unterminated_regular_expression_literal);
                        break;
                    }

                    var ch = m_text.CharCodeAt(p);
                    if (IsLineBreak(ch))
                    {
                        m_tokenIsUnterminated = true;
                        Error(Errors.Unterminated_regular_expression_literal);
                        break;
                    }

                    if (inEscape)
                    {
                        // Parsing an escape character;
                        // reset the flag and just advance to the next char.
                        inEscape = false;
                    }
                    else if (ch == CharacterCodes.Slash && !inCharacterClass)
                    {
                        // A slash within a character class is permissible,
                        // but in general it signals the end of the regexp literal.
                        p++;
                        break;
                    }
                    else if (ch == CharacterCodes.OpenBracket)
                    {
                        inCharacterClass = true;
                    }
                    else if (ch == CharacterCodes.Backslash)
                    {
                        inEscape = true;
                    }
                    else if (ch == CharacterCodes.CloseBracket)
                    {
                        inCharacterClass = false;
                    }

                    p++;
                }

                while (p < m_end && IsIdentifierPart(m_text.CharCodeAt(p), m_languageVersion))
                {
                    p++;
                }

                m_pos = p;
                m_tokenValue = m_text.SubstringFromTo(m_tokenPos, m_pos);
                m_token = SyntaxKind.RegularExpressionLiteral;
            }

            return m_token;
        }

        /// <summary>
        /// Unconditionally back up and scan a template expression portion.
        /// </summary>
        public SyntaxKind RescanTemplateToken(bool backslashesAreAllowed)
        {
            Contract.Assert(m_token == SyntaxKind.CloseBraceToken, "'reScanTemplateToken' should only be called on a '}'");
            m_pos = m_tokenPos;
            return m_token = ScanTemplateAndSetTokenValue(backslashesAreAllowed);
        }

        /// <summary>
        /// Scans a JSX identifier; these differ from normal identifiers in that
        /// they allow dashes
        /// </summary>
        public SyntaxKind ScanJsxIdentifier()
        {
            if (m_token.IsIdentifierOrKeyword())
            {
                var firstCharPosition = m_pos;
                while (m_pos < m_end)
                {
                    var ch = m_text.CharCodeAt(m_pos);
                    if (ch == CharacterCodes.Minus ||
                        (firstCharPosition == m_pos
                            ? IsIdentifierStart(ch, m_languageVersion)
                            : IsIdentifierPart(ch, m_languageVersion)))
                    {
                        m_pos++;
                    }
                    else
                    {
                        break;
                    }
                }

                m_tokenValue += m_text.SubstringFromTo(firstCharPosition, m_pos - firstCharPosition);
            }

            return m_token;
        }

        /// <summary>
        /// Adds the line ending into the line ending map.
        /// </summary>
        /// <remarks>
        /// The method makes sure that the line ending map is a list of ever growing numbers with no duplicates.
        /// Due to error recovery the parser can rescan some portion of the code and the scanner can see a line ending more than once.
        /// This function makes sure that the line map has no duplicates.
        /// </remarks>
        private static void AddLineEnding(List<int> lineMap, int lineStart)
        {
            if (lineMap.Count == 0 || lineMap[lineMap.Count - 1] < lineStart)
            {
                // The map is empty or the previous element is less than the new one.
                lineMap.Add(lineStart);
            }

            // Doing nothing, apparently, rescanning is happening.
        }

        /// <nodoc/>
        public SyntaxKind Scan()
        {
            m_startPos = m_pos;
            m_hasExtendedUnicodeEscape = false;
            m_precedingLineBreak = false;
            m_tokenIsUnterminated = false;
            while (true)
            {
                m_tokenPos = m_pos;
                if (m_pos >= m_end)
                {
                    AddLineEnding(m_lineMap, m_lineStart);

                    return m_token = SyntaxKind.EndOfFileToken;
                }

                var ch = m_text.CharCodeAt(m_pos);

                // Special handling for shebang
                if (ch == CharacterCodes.Hash && m_pos == 0 && IsShebangTrivia(m_text, m_pos))
                {
                    m_pos = ScanShebangTrivia(m_text, m_pos);
                    if (m_needSkipTrivia)
                    {
                        continue;
                    }

                    return m_token = SyntaxKind.ShebangTrivia;
                }

                if (ch > CharacterCodes.MaxAsciiCharacter && IsLineBreak(ch))
                {
                    AddLineEnding(m_lineMap, m_lineStart);
                    m_lineStart = m_pos + 1;
                }

                // Pos is always called pointing to the next char after c
                void CheckCrLf(CharacterCodes c, int pos)
                {
                    switch (c)
                    {
                        case CharacterCodes.CarriageReturn:
                            AddLineEnding(m_lineMap, m_lineStart);

                            // In the case of CrLf, we record both altogether
                            if (m_text.CharCodeAt(pos) == CharacterCodes.LineFeed)
                            {
                                m_lineStart = pos + 1;
                            }
                            else
                            {
                                m_lineStart = pos;
                            }

                            break;
                        case CharacterCodes.LineFeed:
                            // The scanner will reach this case only for multiline comments
                            // But in that case we check that we haven't considered both CrLf already
                            if (pos < 2 || m_text.CharCodeAt(pos - 2) != CharacterCodes.CarriageReturn)
                            {
                                AddLineEnding(m_lineMap, m_lineStart);
                                m_lineStart = pos;
                            }

                            break;
                    }
                }

                switch (ch)
                {
                    case CharacterCodes.CarriageReturn:
                    case CharacterCodes.LineFeed:
                        CheckCrLf(ch, m_pos + 1);

                        m_precedingLineBreak = true;
                        if (m_needSkipTrivia)
                        {
                            m_pos++;
                            continue;
                        }

                        if (ch == CharacterCodes.CarriageReturn && m_pos + 1 < m_end &&
                            m_text.CharCodeAt(m_pos + 1) == CharacterCodes.LineFeed)
                        {
                            // consume both CR and LF
                            m_pos += 2;
                        }
                        else
                        {
                            m_pos++;
                        }

                        m_newLineTriviaCount++;
                        AssociateTrailingCommentsWithLastTrivia();

                        return m_token = SyntaxKind.NewLineTrivia;
                    case CharacterCodes.Tab:
                    case CharacterCodes.VerticalTab:
                    case CharacterCodes.FormFeed:
                    case CharacterCodes.Space:
                        if (m_needSkipTrivia)
                        {
                            m_pos++;
                            continue;
                        }

                        while (m_pos < m_end && IsWhiteSpace(m_text.CharCodeAt(m_pos)))
                        {
                            m_pos++;
                        }

                        return m_token = SyntaxKind.WhitespaceTrivia;
                    case CharacterCodes.Exclamation:
                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.equals)
                        {
                            if (m_text.CharCodeAt(m_pos + 2) == CharacterCodes.equals)
                            {
                                m_pos += 3;
                                return m_token = SyntaxKind.ExclamationEqualsEqualsToken;
                            }

                            m_pos += 2;
                            return m_token = SyntaxKind.ExclamationEqualsToken;
                        }

                        m_pos++;
                        return m_token = SyntaxKind.ExclamationToken;
                    case CharacterCodes.DoubleQuote:
                    case CharacterCodes.SingleQuote:
                        m_tokenValue = ScanString();
                        return m_token = SyntaxKind.StringLiteral;
                    case CharacterCodes.Backtick:
                        {
                            // DScript-specific. We retrieve the factory name from the last parsed
                            // token. Note that this can be null (e.g. FirstToken). And we know
                            // it can only be a factory name if it is an identifier.
                            var factoryName = m_token == SyntaxKind.Identifier ? m_tokenValue : null;

                            // Backslashes are allowed if it is a DScript path-like interpolation factory and the general configuration flag
                            // allows them
                            var backslashesAreAllowed = m_allowBackslashesInPathInterpolation && IsPathLikeInterpolationFactory(factoryName);

                            return m_token = ScanTemplateAndSetTokenValue(backslashesAreAllowed);
                        }

                    case CharacterCodes.Percent:
                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.equals)
                        {
                            m_pos += 2;
                            return m_token = SyntaxKind.PercentEqualsToken;
                        }

                        m_pos++;
                        return m_token = SyntaxKind.PercentToken;
                    case CharacterCodes.Ampersand:
                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.Ampersand)
                        {
                            m_pos += 2;
                            return m_token = SyntaxKind.AmpersandAmpersandToken;
                        }

                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.equals)
                        {
                            m_pos += 2;
                            return m_token = SyntaxKind.AmpersandEqualsToken;
                        }

                        m_pos++;
                        return m_token = SyntaxKind.AmpersandToken;
                    case CharacterCodes.OpenParen:
                        m_pos++;
                        return m_token = SyntaxKind.OpenParenToken;
                    case CharacterCodes.CloseParen:
                        m_pos++;
                        return m_token = SyntaxKind.CloseParenToken;
                    case CharacterCodes.Asterisk:
                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.equals)
                        {
                            m_pos += 2;
                            return m_token = SyntaxKind.AsteriskEqualsToken;
                        }

                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.Asterisk)
                        {
                            if (m_text.CharCodeAt(m_pos + 2) == CharacterCodes.equals)
                            {
                                m_pos += 3;
                                return m_token = SyntaxKind.AsteriskAsteriskEqualsToken;
                            }

                            m_pos += 2;
                            return m_token = SyntaxKind.AsteriskAsteriskToken;
                        }

                        m_pos++;
                        return m_token = SyntaxKind.AsteriskToken;
                    case CharacterCodes.Plus:
                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.Plus)
                        {
                            m_pos += 2;
                            return m_token = SyntaxKind.PlusPlusToken;
                        }

                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.equals)
                        {
                            m_pos += 2;
                            return m_token = SyntaxKind.PlusEqualsToken;
                        }

                        m_pos++;
                        return m_token = SyntaxKind.PlusToken;
                    case CharacterCodes.Comma:
                        m_pos++;
                        return m_token = SyntaxKind.CommaToken;
                    case CharacterCodes.Minus:
                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.Minus)
                        {
                            m_pos += 2;
                            return m_token = SyntaxKind.MinusMinusToken;
                        }

                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.equals)
                        {
                            m_pos += 2;
                            return m_token = SyntaxKind.MinusEqualsToken;
                        }

                        m_pos++;
                        return m_token = SyntaxKind.MinusToken;
                    case CharacterCodes.Dot:
                        if (IsDigit(m_text.CharCodeAt(m_pos + 1)))
                        {
                            m_tokenValue = ScanNumber();
                            return m_token = SyntaxKind.NumericLiteral;
                        }

                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.Dot &&
                            m_text.CharCodeAt(m_pos + 2) == CharacterCodes.Dot)
                        {
                            m_pos += 3;
                            return m_token = SyntaxKind.DotDotDotToken;
                        }

                        m_pos++;
                        return m_token = SyntaxKind.DotToken;
                    case CharacterCodes.Slash:
                        // Single-line comment
                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.Slash)
                        {
                            m_pos += 2;

                            while (m_pos < m_end)
                            {
                                if (IsLineBreak(m_text.CharCodeAt(m_pos)))
                                {
                                    break;
                                }

                                m_pos++;
                            }

                            if (!m_needSkipTrivia)
                            {
                                m_comments.Add(new Trivia.Comment(TokenText, isMultiLine: false));
                            }

                            if (m_preserveComments)
                            {
                                return m_token = SyntaxKind.SingleLineCommentTrivia;
                            }

                            continue;
                        }

                        // Multi-line comment
                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.Asterisk)
                        {
                            m_pos += 2;

                            var commentClosed = false;
                            while (m_pos < m_end)
                            {
                                var ch0 = m_text.CharCodeAt(m_pos);

                                if (ch0 == CharacterCodes.Asterisk && m_text.CharCodeAt(m_pos + 1) == CharacterCodes.Slash)
                                {
                                    m_pos += 2;
                                    commentClosed = true;
                                    break;
                                }

                                if (IsLineBreak(ch0))
                                {
                                    CheckCrLf(ch0, m_pos + 1);
                                    m_precedingLineBreak = true;
                                }

                                m_pos++;
                            }

                            if (!commentClosed)
                            {
                                Error(Errors.Asterisk_Slash_expected);
                            }

                            if (!m_needSkipTrivia)
                            {
                                m_comments.Add(new Trivia.Comment(TokenText, isMultiLine: true));
                                m_tokenIsUnterminated = !commentClosed;
                            }

                            if (m_preserveComments)
                            {
                                return m_token = SyntaxKind.MultiLineCommentTrivia;
                            }

                            continue;
                        }

                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.equals)
                        {
                            m_pos += 2;
                            return m_token = SyntaxKind.SlashEqualsToken;
                        }

                        m_pos++;

                        return m_token = SyntaxKind.SlashToken;

                    case CharacterCodes._0:
                        if (m_pos + 2 < m_end &&
                            (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.X || m_text.CharCodeAt(m_pos + 1) == CharacterCodes.x))
                        {
                            m_pos += 2;
                            var value = ScanMinimumNumberOfHexDigits(1);
                            if (value < 0)
                            {
                                Error(Errors.Hexadecimal_digit_expected);
                                value = 0;
                            }

                            m_tokenValue = value.ToString();
                            return m_token = SyntaxKind.NumericLiteral;
                        }

                        if (m_pos + 2 < m_end &&
                            (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.B ||
                             m_text.CharCodeAt(m_pos + 1) == CharacterCodes.b))
                        {
                            m_pos += 2;
                            var value = ScanBinaryOrOctalDigits(/* base */ 2);
                            if (value < 0)
                            {
                                Error(Errors.Binary_digit_expected);
                                value = 0;
                            }

                            m_tokenValue = value.ToString();
                            return m_token = SyntaxKind.NumericLiteral;
                        }

                        if (m_pos + 2 < m_end &&
                            (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.O ||
                             m_text.CharCodeAt(m_pos + 1) == CharacterCodes.o))
                        {
                            m_pos += 2;
                            var value = ScanBinaryOrOctalDigits(/* base */ 8);
                            if (value < 0)
                            {
                                Error(Errors.Octal_digit_expected);
                                value = 0;
                            }

                            m_tokenValue = value.ToString();
                            return m_token = SyntaxKind.NumericLiteral;
                        }

                        // Try to parse as an octal
                        if (m_pos + 1 < m_end && IsOctalDigit(m_text.CharCodeAt(m_pos + 1)))
                        {
                            m_tokenValue = ScanOctalDigits().ToString();
                            return m_token = SyntaxKind.NumericLiteral;
                        }

                        // This fall-through is a deviation from the EcmaScript grammar. The grammar says that a leading zero
                        // can only be followed by an octal digit, a dot, or the end of the int literal. However, we are being
                        // permissive and allowing decimal digits of the form 08* and 09* (which many browsers also do).
                        goto case CharacterCodes._1;
                    case CharacterCodes._1:
                    case CharacterCodes._2:
                    case CharacterCodes._3:
                    case CharacterCodes._4:
                    case CharacterCodes._5:
                    case CharacterCodes._6:
                    case CharacterCodes._7:
                    case CharacterCodes._8:
                    case CharacterCodes._9:
                        m_tokenValue = ScanNumber();
                        return m_token = SyntaxKind.NumericLiteral;
                    case CharacterCodes.Colon:
                        m_pos++;
                        return m_token = SyntaxKind.ColonToken;
                    case CharacterCodes.Semicolon:
                        m_pos++;
                        return m_token = SyntaxKind.SemicolonToken;
                    case CharacterCodes.LessThan:
                        if (IsConflictMarkerTrivia(m_text, m_pos))
                        {
                            m_pos = ScanConflictMarkerTrivia(m_text, m_pos, Error);
                            if (m_needSkipTrivia)
                            {
                                continue;
                            }

                            return m_token = SyntaxKind.ConflictMarkerTrivia;
                        }

                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.LessThan)
                        {
                            if (m_text.CharCodeAt(m_pos + 2) == CharacterCodes.equals)
                            {
                                m_pos += 3;
                                return m_token = SyntaxKind.LessThanLessThanEqualsToken;
                            }

                            m_pos += 2;
                            return m_token = SyntaxKind.LessThanLessThanToken;
                        }

                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.equals)
                        {
                            m_pos += 2;
                            return m_token = SyntaxKind.LessThanEqualsToken;
                        }

                        if (m_languageVariant == LanguageVariant.Jsx &&
                            m_text.CharCodeAt(m_pos + 1) == CharacterCodes.Slash &&
                            m_text.CharCodeAt(m_pos + 2) != CharacterCodes.Asterisk)
                        {
                            m_pos += 2;
                            return m_token = SyntaxKind.LessThanSlashToken;
                        }

                        m_pos++;
                        return m_token = SyntaxKind.LessThanToken;
                    case CharacterCodes.equals:
                        if (IsConflictMarkerTrivia(m_text, m_pos))
                        {
                            m_pos = ScanConflictMarkerTrivia(m_text, m_pos, Error);
                            if (m_needSkipTrivia)
                            {
                                continue;
                            }

                            return m_token = SyntaxKind.ConflictMarkerTrivia;
                        }

                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.equals)
                        {
                            if (m_text.CharCodeAt(m_pos + 2) == CharacterCodes.equals)
                            {
                                m_pos += 3;
                                return m_token = SyntaxKind.EqualsEqualsEqualsToken;
                            }

                            m_pos += 2;
                            return m_token = SyntaxKind.EqualsEqualsToken;
                        }

                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.GreaterThan)
                        {
                            m_pos += 2;
                            return m_token = SyntaxKind.EqualsGreaterThanToken;
                        }

                        m_pos++;
                        return m_token = SyntaxKind.EqualsToken;
                    case CharacterCodes.GreaterThan:
                        if (IsConflictMarkerTrivia(m_text, m_pos))
                        {
                            m_pos = ScanConflictMarkerTrivia(m_text, m_pos, Error);
                            if (m_needSkipTrivia)
                            {
                                continue;
                            }

                            return m_token = SyntaxKind.ConflictMarkerTrivia;
                        }

                        m_pos++;
                        return m_token = SyntaxKind.GreaterThanToken;
                    case CharacterCodes.Question:
                        m_pos++;
                        return m_token = SyntaxKind.QuestionToken;
                    case CharacterCodes.OpenBracket:
                        m_pos++;
                        return m_token = SyntaxKind.OpenBracketToken;
                    case CharacterCodes.CloseBracket:
                        m_pos++;
                        return m_token = SyntaxKind.CloseBracketToken;
                    case CharacterCodes.Caret:
                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.equals)
                        {
                            m_pos += 2;
                            return m_token = SyntaxKind.CaretEqualsToken;
                        }

                        m_pos++;
                        return m_token = SyntaxKind.CaretToken;
                    case CharacterCodes.OpenBrace:
                        m_pos++;
                        return m_token = SyntaxKind.OpenBraceToken;
                    case CharacterCodes.Bar:
                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.Bar)
                        {
                            m_pos += 2;
                            return m_token = SyntaxKind.BarBarToken;
                        }

                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.equals)
                        {
                            m_pos += 2;
                            return m_token = SyntaxKind.BarEqualsToken;
                        }

                        m_pos++;
                        return m_token = SyntaxKind.BarToken;
                    case CharacterCodes.CloseBrace:
                        m_pos++;
                        return m_token = SyntaxKind.CloseBraceToken;
                    case CharacterCodes.Tilde:
                        m_pos++;
                        return m_token = SyntaxKind.TildeToken;
                    case CharacterCodes.At:
                        // DS: in DScript ambient decorators could be used with @@ syntax.
                        if (m_text.CharCodeAt(m_pos + 1) == CharacterCodes.At)
                        {
                            m_pos += 2;
                            return m_token = SyntaxKind.AtToken;
                        }

                        m_pos++;
                        return m_token = SyntaxKind.AtToken;
                    case CharacterCodes.Backslash:
                        var cookedChar = (CharacterCodes)PeekUnicodeEscape();
                        if (cookedChar >= 0 && IsIdentifierStart(cookedChar, m_languageVersion))
                        {
                            m_pos += 6;
                            m_tokenValue = cookedChar.FromCharCode() + ScanIdentifierParts();
                            return m_token = GetIdentifierToken();
                        }

                        Error(Errors.Invalid_character);
                        m_pos++;
                        return m_token = SyntaxKind.Unknown;
                    default:
                        if (IsIdentifierStart(ch, m_languageVersion))
                        {
                            m_pos++;
                            while (m_pos < m_end && IsIdentifierPart(ch = m_text.CharCodeAt(m_pos), m_languageVersion))
                            {
                                m_pos++;
                            }

                            m_tokenValue = m_text.SubstringFromTo(m_tokenPos, m_pos);
                            if (ch == CharacterCodes.Backslash)
                            {
                                m_tokenValue += ScanIdentifierParts();
                            }

                            return m_token = GetIdentifierToken();
                        }

                        if (IsWhiteSpace(ch))
                        {
                            m_pos++;
                            continue;
                        }

                        if (IsLineBreak(ch))
                        {
                            m_newLineTriviaCount++;
                            m_precedingLineBreak = true;
                            m_pos++;
                            continue;
                        }

                        Error(Errors.Invalid_character);
                        m_pos++;
                        return m_token = SyntaxKind.Unknown;
                }
            }
        }

        /// <summary>
        /// Sets the text for the scanner to scan.  An optional subrange starting point and length
        /// can be provided to have the scanner only scan a portion of the text.
        /// </summary>
        public void SetText(TextSource newText, int? start = null, int? length = null)
        {
            m_text = newText ?? new StringBasedTextSource(string.Empty);
            m_end = length == null ? m_text.Length : start.GetValueOrDefault() + length.Value;
            SetTextPos(start.GetValueOrDefault());
        }

        /// <nodoc/>
        public void SetOnError(ErrorCallback onError)
        {
            m_onError = onError;
        }

        /// <nodoc/>
        public void SetScriptTarget(ScriptTarget scriptTarget)
        {
            m_languageVersion = scriptTarget;
        }

        /// <nodoc/>
        public void SetLanguageVariant(LanguageVariant variant)
        {
            m_languageVariant = variant;
        }

        /// <nodoc/>
        public void SetTextPos(int textPos)
        {
            Contract.Requires(textPos >= 0);

            m_pos = textPos;
            m_startPos = textPos;
            m_tokenPos = textPos;
            m_token = SyntaxKind.Unknown;
            m_precedingLineBreak = false;

            m_tokenValue = null;
            m_hasExtendedUnicodeEscape = false;
            m_tokenIsUnterminated = false;
        }

        /// <summary>
        /// Invokes the provided callback then unconditionally restores the scanner to the state it
        /// was in immediately prior to invoking the callback.  The result of invoking the callback
        /// is returned from this function.
        /// </summary>
        public TRestult LookAhead<TState, TRestult>(TState state, Func<TState, TRestult> callback)
        {
            return SpeculationHelper(state, callback, isLookahead: true);
        }

        /// <summary>
        /// Invokes the provided callback.  If the callback returns something falsy, then it restores
        /// the scanner to the state it was in immediately prior to invoking the callback.  If the
        /// callback returns something truthy, then the scanner state is not rolled back.  The result
        /// of invoking the callback is returned from this function.
        /// </summary>
        public TResult TryScan<TState, TResult>(TState state, Func<TState, TResult> callback)
        {
            return SpeculationHelper(state, callback, isLookahead: false);
        }

        /// <nodoc/>
        public TResult SpeculationHelper<TState, TResult>(TState state, Func<TState, TResult> callback, bool isLookahead)
        {
            var savePos = m_pos;
            var saveStartPos = m_startPos;
            var saveTokenPos = m_tokenPos;
            var saveToken = m_token;
            var saveTokenValue = m_tokenValue;
            var savePrecedingLineBreak = m_precedingLineBreak;
            var result = callback(state);

            // If our callback returned something 'falsy' or we're just looking ahead,
            // then unconditionally restore us to where we were.
            if (isLookahead || IsFalsy(result))
            {
                m_pos = savePos;
                m_startPos = saveStartPos;
                m_tokenPos = saveTokenPos;
                m_token = saveToken;
                m_tokenValue = saveTokenValue;
                m_precedingLineBreak = savePrecedingLineBreak;
            }

            return result;
        }

        /// <summary>
        /// Determines whether a factoryName (as in factoryName`some${Interpolated}String`) is a DScript path-like
        /// interpolation.
        /// </summary>
        public static bool IsPathLikeInterpolationFactory(string factoryName)
        {
            if (factoryName == null || factoryName.Length != 1)
            {
                return false;
            }

            var c = factoryName[0];

            return
                c == Names.PathInterpolationFactory ||
                c == Names.DirectoryInterpolationFactory ||
                c == Names.FileInterpolationFactory ||
                c == Names.RelativePathInterpolationFactory ||
                c == Names.PathAtomInterpolationFactory;
        }

        private void Error(IDiagnosticMessage message, int length = 0)
        {
            m_onError?.Invoke(message, length);
        }

        /// <nodoc />
        public static bool IsIdentifierPart(CharacterCodes ch, ScriptTarget languageVersion)
        {
            return (ch >= CharacterCodes.A && ch <= CharacterCodes.Z) || (ch >= CharacterCodes.a && ch <= CharacterCodes.z) ||
                   (ch >= CharacterCodes._0 && ch <= CharacterCodes._9) || ch == CharacterCodes.Dollar || ch == CharacterCodes._ ||
                   (ch > CharacterCodes.MaxAsciiCharacter && IsUnicodeIdentifierPart(ch, languageVersion));
        }

        /// <summary>
        /// Sets the current 'tokenValue' and returns a NoSubstitutionTemplateLiteral or
        /// a literal component of a TemplateExpression.
        /// </summary>
        private SyntaxKind ScanTemplateAndSetTokenValue(bool backslashesAreAllowed)
        {
            var startedWithBacktick = m_text.CharCodeAt(m_pos) == CharacterCodes.Backtick;

            m_textBuilder.Clear();

            m_pos++;
            var start = m_pos;
            SyntaxKind resultingToken;

            while (true)
            {
                if (m_pos >= m_end)
                {
                    m_textBuilder += m_text.SubstringFromTo(start, m_pos);
                    m_tokenIsUnterminated = true;
                    Error(Errors.Unterminated_template_literal);
                    resultingToken = startedWithBacktick ? SyntaxKind.NoSubstitutionTemplateLiteral : SyntaxKind.TemplateTail;
                    break;
                }

                var currChar = m_text.CharCodeAt(m_pos);

                // '`'
                if (currChar == CharacterCodes.Backtick)
                {
                    // DScript-specific. If backslashes are allowed, then double backtick
                    // is the way to escape backtick
                    if (backslashesAreAllowed && m_pos + 1 < m_end && m_text.CharCodeAt(m_pos + 1) == CharacterCodes.Backtick)
                    {
                        m_textBuilder += m_text.SubstringFromTo(start, m_pos + 1);
                        m_pos += 2;
                        start = m_pos;
                        continue;
                    }

                    m_textBuilder += m_text.SubstringFromTo(start, m_pos);

                    m_pos++;
                    resultingToken = startedWithBacktick ? SyntaxKind.NoSubstitutionTemplateLiteral : SyntaxKind.TemplateTail;
                    break;
                }

                // '${'
                // DScript-specific: observe that $ does not need escaping because it is not a valid character
                // for path-like literals. So we do nothing here at this regards.
                if (currChar == CharacterCodes.Dollar && m_pos + 1 < m_end &&
                    m_text.CharCodeAt(m_pos + 1) == CharacterCodes.OpenBrace)
                {
                    m_textBuilder += m_text.SubstringFromTo(start, m_pos);

                    m_pos += 2;
                    resultingToken = startedWithBacktick ? SyntaxKind.TemplateHead : SyntaxKind.TemplateMiddle;
                    break;
                }

                // Escape character
                // DScript-specific. Regular backslash escaping does not happen if backslashes are allowed
                if (currChar == CharacterCodes.Backslash && !backslashesAreAllowed)
                {
                    m_textBuilder += m_text.SubstringFromTo(start, m_pos);
                    m_textBuilder += ScanEscapeSequence();
                    start = m_pos;
                    continue;
                }

                // Speculated ECMAScript 6 Spec 11.8.6.1:
                // <CR><LF> and <CR> LineTerminatorSequences are normalized to <LF> for Template Values
                if (currChar == CharacterCodes.CarriageReturn)
                {
                    m_textBuilder += m_text.SubstringFromTo(start, m_pos);
                    m_pos++;

                    if (m_pos < m_end && m_text.CharCodeAt(m_pos) == CharacterCodes.LineFeed)
                    {
                        m_pos++;
                    }

                    m_textBuilder += "\n";
                    start = m_pos;
                    continue;
                }

                m_pos++;
            }

            Contract.Assert(resultingToken != SyntaxKind.Unknown, null);

            m_tokenValue = m_textBuilder.ToString();
            return resultingToken;
        }

        private string ScanEscapeSequence()
        {
            m_pos++;
            if (m_pos >= m_end)
            {
                Error(Errors.Unexpected_end_of_text);
                return string.Empty;
            }

            var ch = m_text.CharCodeAt(m_pos++);
            switch (ch)
            {
                case CharacterCodes._0:
                    return "\0";
                case CharacterCodes.b:
                    return "\b";
                case CharacterCodes.t:
                    return "\t";
                case CharacterCodes.n:
                    return "\n";
                case CharacterCodes.v:
                    return "\v";
                case CharacterCodes.f:
                    return "\f";
                case CharacterCodes.r:
                    return "\r";
                case CharacterCodes.SingleQuote:
                    return "\'";
                case CharacterCodes.DoubleQuote:
                    return "\"";
                case CharacterCodes.u:
                    // '\u{DDDDDDDD}'
                    if (m_pos < m_end && m_text.CharCodeAt(m_pos) == CharacterCodes.OpenBrace)
                    {
                        m_hasExtendedUnicodeEscape = true;
                        m_pos++;
                        return ScanExtendedUnicodeEscape();
                    }

                    // '\uDDDD'
                    return ScanHexadecimalEscape(/*numDigits*/ 4);

                case CharacterCodes.x:
                    // '\xDD'
                    return ScanHexadecimalEscape(/*numDigits*/ 2);

                // when encountering a LineContinuation (i.e., a backslash and a line terminator sequence),
                // the line terminator is interpreted to be "the empty code unit sequence".
                case CharacterCodes.CarriageReturn:
                    if (m_pos < m_end && m_text.CharCodeAt(m_pos) == CharacterCodes.LineFeed)
                    {
                        m_pos++;
                    }

                    // fall through
                    goto case CharacterCodes.LineFeed;
                case CharacterCodes.LineFeed:
                case CharacterCodes.LineSeparator:
                case CharacterCodes.ParagraphSeparator:
                    return string.Empty;
                default:
                    return ch.FromCharCode();
            }
        }

        private string ScanHexadecimalEscape(int numDigits)
        {
            var escapedValue = ScanExactNumberOfHexDigits(numDigits);

            if (escapedValue >= 0)
            {
                return ((CharacterCodes)(int)escapedValue).FromCharCode();
            }
            else
            {
                Error(Errors.Hexadecimal_digit_expected);
                return string.Empty;
            }
        }

        private string ScanExtendedUnicodeEscape()
        {
            var escapedValue = ScanMinimumNumberOfHexDigits(1);
            var isInvalidExtendedEscape = false;

            // Validate the value of the digit
            if (escapedValue < 0)
            {
                Error(Errors.Hexadecimal_digit_expected);
                isInvalidExtendedEscape = true;
            }
            else if (escapedValue > 0x10FFFF)
            {
                Error(Errors.An_extended_Unicode_escape_value_must_be_between_0x0_and_0x10FFFF_inclusive);
                isInvalidExtendedEscape = true;
            }

            if (m_pos >= m_end)
            {
                Error(Errors.Unexpected_end_of_text);
                isInvalidExtendedEscape = true;
            }
            else if (m_text.CharCodeAt(m_pos) == CharacterCodes.CloseBrace)
            {
                // Only swallow the following character up if it's a '}'.
                m_pos++;
            }
            else
            {
                Error(Errors.Unterminated_Unicode_escape_sequence);
                isInvalidExtendedEscape = true;
            }

            if (isInvalidExtendedEscape)
            {
                return string.Empty;
            }

            return Utf16EncodeAsString((int)escapedValue);
        }

        // Derived from the 10.1.1 UTF16Encoding of the ES6 Spec.
        private static string Utf16EncodeAsString(int codePoint)
        {
#pragma warning disable SA1131 // Use readable conditions
            Contract.Requires(0x0 <= codePoint && codePoint <= 0x10FFFF);
#pragma warning restore SA1131 // Use readable conditions

            if (codePoint <= 65535)
            {
                return ((CharacterCodes)codePoint).FromCharCode();
            }

            var codeUnit1 = (int)Math.Floor((double)(codePoint - 65536) / 1024) + 0xD800;
            var codeUnit2 = ((codePoint - 65536) % 1024) + 0xDC00;

            throw PlaceHolder.NotImplemented();

            // return StringEx.fromCharCode(codeUnit1, codeUnit2);
        }

        /// <summary>
        /// Scans the given int of hexadecimal digits in the m_text,
        /// returning -1 if the given int is unavailable.
        /// </summary>
        private BigInteger ScanExactNumberOfHexDigits(int count)
        {
            return ScanHexDigits(/*minCount*/ count, /*scanAsManyAsPossible*/ false);
        }

        /// <summary>
        /// Scans as many hexadecimal digits as are available in the m_text,
        /// returning -1 if the given int of digits was unavailable.
        /// </summary>
        private BigInteger ScanMinimumNumberOfHexDigits(int count)
        {
            return ScanHexDigits(/*minCount*/ count, /*scanAsManyAsPossible*/ true);
        }

        private BigInteger ScanHexDigits(int minCount, bool scanAsManyAsPossible)
        {
            var digits = 0;
            BigInteger value = 0;
            while (digits < minCount || scanAsManyAsPossible)
            {
                var ch = m_text.CharCodeAt(m_pos);
                if (ch >= CharacterCodes._0 && ch <= CharacterCodes._9)
                {
                    value = (value * 16) + (ch - CharacterCodes._0);
                }
                else if (ch >= CharacterCodes.A && ch <= CharacterCodes.F)
                {
                    value = (value * 16) + (ch - CharacterCodes.A + 10);
                }
                else if (ch >= CharacterCodes.a && ch <= CharacterCodes.f)
                {
                    value = (value * 16) + (ch - CharacterCodes.a + 10);
                }
                else
                {
                    break;
                }

                m_pos++;
                digits++;
            }

            if (digits < minCount)
            {
                value = -1;
            }

            return value;
        }

        private string ScanString()
        {
            m_textBuilder.Clear();
            var quote = m_text.CharCodeAt(m_pos++);
            var start = m_pos;

            while (true)
            {
                if (m_pos >= m_end)
                {
                    m_textBuilder += m_text.SubstringFromTo(start, m_pos);
                    m_tokenIsUnterminated = true;
                    Error(Errors.Unterminated_string_literal);
                    break;
                }

                var ch = m_text.CharCodeAt(m_pos);

                if (ch == quote)
                {
                    m_textBuilder += m_text.SubstringFromTo(start, m_pos);
                    m_pos++;
                    break;
                }

                if (ch == CharacterCodes.Backslash)
                {
                    m_textBuilder += m_text.SubstringFromTo(start, m_pos);
                    m_textBuilder += ScanEscapeSequence();
                    start = m_pos;
                    continue;
                }

                if (IsLineBreak(ch))
                {
                    m_textBuilder += m_text.SubstringFromTo(start, m_pos);
                    m_tokenIsUnterminated = true;
                    Error(Errors.Unterminated_string_literal);
                    break;
                }

                m_pos++;
            }

            return m_textBuilder.ToString();
        }

        private static bool IsDigit(CharacterCodes ch)
        {
            return ch >= CharacterCodes._0 && ch <= CharacterCodes._9;
        }

        private string ScanNumber()
        {
            var start = m_pos;
            while (IsDigit(m_text.CharCodeAt(m_pos)))
            {
                m_pos++;
            }

            if (m_text.CharCodeAt(m_pos) == CharacterCodes.Dot)
            {
                m_pos++;
                while (IsDigit(m_text.CharCodeAt(m_pos)))
                {
                    m_pos++;
                }
            }

            var end = m_pos;
            if (m_text.CharCodeAt(m_pos) == CharacterCodes.E || m_text.CharCodeAt(m_pos) == CharacterCodes.e)
            {
                m_pos++;
                if (m_text.CharCodeAt(m_pos) == CharacterCodes.Plus || m_text.CharCodeAt(m_pos) == CharacterCodes.Minus)
                {
                    m_pos++;
                }

                if (IsDigit(m_text.CharCodeAt(m_pos)))
                {
                    m_pos++;
                    while (IsDigit(m_text.CharCodeAt(m_pos)))
                    {
                        m_pos++;
                    }

                    end = m_pos;
                }
                else
                {
                    Error(Errors.Digit_expected);
                }
            }

            var stringValue = m_text.SubstringFromTo(start, end);

            // In some cases stringValue could ends with ., like 1.
            // JavaScript/TypeScript can successfully convert it to number
            // So if the last character is not a number, then we need to remove it from the candidate
            if (!char.IsDigit(stringValue[stringValue.Length - 1]))
            {
                stringValue = stringValue.Substring(0, stringValue.Length - 1);
            }

            return stringValue;
        }

        private int ScanBinaryOrOctalDigits(int base0)
        {
            Contract.Assert(base0 != 2 || base0 != 8, "Expected either base0 2 or base0 8");

            var value = 0;

            // For counting int of digits; Valid binaryIntegerLiteral must have at least one binary digit following B or b.
            // Similarly valid octalIntegerLiteral must have at least one octal digit following o or O.
            var intOfDigits = 0;

            while (true)
            {
                var ch = m_text.CharCodeAt(m_pos);
                var valueOfCh = ch - CharacterCodes._0;
                if (!IsDigit(ch) || valueOfCh >= base0)
                {
                    break;
                }

                value = (value * base0) + valueOfCh;
                m_pos++;
                intOfDigits++;
            }

            // Invalid binaryIntegerLiteral or octalIntegerLiteral
            if (intOfDigits == 0)
            {
                return -1;
            }

            return value;
        }

        private int ScanOctalDigits()
        {
            var start = m_pos;

            while (IsOctalDigit(m_text.CharCodeAt(m_pos)))
            {
                m_pos++;
            }

            return int.Parse(m_text.SubstringFromTo(start, m_pos));
        }

        // Current character is known to be a backslash. Check for Unicode escape of the form '\uXXXX'
        // and return code point value if valid Unicode escape is found. Otherwise return -1.
        private int PeekUnicodeEscape()
        {
            if (m_pos + 5 < m_end && m_text.CharCodeAt(m_pos + 1) == CharacterCodes.u)
            {
                var start = m_pos;
                m_pos += 2;
                var value = ScanExactNumberOfHexDigits(4);
                m_pos = start;
                return (int)value;
            }

            return -1;
        }

        private SyntaxKind GetIdentifierToken()
        {
            // Reserved words are between 2 and 11 characters long and start with a lowercase letter
            var len = m_tokenValue.Length;
            if (len >= 2 && len <= 11)
            {
                var ch = m_tokenValue.CharCodeAt(0);
                SyntaxKind token0;
                if (ch >= CharacterCodes.a && ch <= CharacterCodes.z && s_textToToken.TryGetValue(m_tokenValue, out token0))
                {
                    return m_token = token0;
                }
            }

            return m_token = SyntaxKind.Identifier;
        }

        private string ScanIdentifierParts()
        {
            m_textBuilder.Clear();
            var start = m_pos;
            while (m_pos < m_end)
            {
                var ch = m_text.CharCodeAt(m_pos);
                if (IsIdentifierPart(ch, m_languageVersion))
                {
                    m_pos++;
                }
                else if (ch == CharacterCodes.Backslash)
                {
                    ch = (CharacterCodes)PeekUnicodeEscape();
                    if (!(ch >= 0 && IsIdentifierPart(ch, m_languageVersion)))
                    {
                        break;
                    }

                    m_textBuilder += m_text.SubstringFromTo(start, m_pos);
                    m_textBuilder += ch.FromCharCode();

                    // Valid Unicode escape is always six characters
                    m_pos += 6;
                    start = m_pos;
                }
                else
                {
                    break;
                }
            }

            m_textBuilder += m_text.SubstringFromTo(start, m_pos);
            return m_textBuilder.ToString();
        }

        /// <summary>
        /// Returns true if <paramref name="chcode"/> is a line break.
        /// </summary>
        public static bool IsLineBreak(CharacterCodes chcode)
        {
            // ES5 7.3:
            // The ECMAScript line terminator characters are listed in Table 3.
            //     Table 3: Line Terminator Characters
            //     Code Unit Value     Name                    Formal Name
            //     \u000A              Line Feed               <LF>
            //     \u000D              Carriage Return         <CR>
            //     \u2028              Line separator          <LS>
            //     \u2029              Paragraph separator     <PS>
            // Only the characters in Table 3 are treated as line terminators. Other new line or line
            // breaking characters are treated as white space but not as line terminators.
            return chcode == CharacterCodes.LineFeed ||
                   chcode == CharacterCodes.CarriageReturn ||
                   chcode == CharacterCodes.LineSeparator ||
                   chcode == CharacterCodes.ParagraphSeparator;
        }

        /// <nodoc />
        public static int SkipOverTrivia(ISourceFile sourceFile, int startPosition)
        {
            return SkipOverTrivia(sourceFile.Text, sourceFile.BackslashesAllowedInPathInterpolation, startPosition);
        }

        /// <nodoc />
        public static int SkipOverTrivia(TextSource text, bool allowBackslashesInPathInterpolation, int startPosition)
        {
            using (var textBuilderWrapper = Utilities.Pools.TextBuilderPool.GetInstance())
            {
                using (var scannerWrapper = CreateScanner(text, allowBackslashesInPathInterpolation, startPosition, textBuilderWrapper))
                {
                    var scanner = scannerWrapper.Instance;
                    scanner.Scan();
                    return scanner.TokenPos;
                }
            }
        }

        /// <summary>
        /// We assume the first line starts at position 0 and 'position' is non-negative.
        /// </summary>
        public static LineAndColumn ExpensiveComputeLineAndCharacterOfPositionSeeTask646652(int[] lineStarts, int position)
        {
            // This method is still expensive, but the line information is computed lazily.
            var lineNumber = Array.BinarySearch(lineStarts, position);
            if (lineNumber < 0)
            {
                // If the actual position was not found,
                // the binary search returns the 2's-complement of the next line start
                // e.g. if the line starts at [5, 10, 23, 80] and the position requested was 20
                // then the search will return -2.
                //
                // We want the index of the previous line start, so we subtract 1.
                // Review 2's-complement if this is confusing.
                lineNumber = ~lineNumber - 1;
                Contract.Assert(lineNumber != -1, "position cannot precede the beginning of the file");
            }

            return new LineAndColumn(
                // Lines are started with 0, we need to add 1
                lineNumber + 1,
                position - lineStarts[lineNumber] + 1);
        }

        /// <nodoc />
        internal static int SkipTrivia(TextSource text, int pos, bool? stopAfterLineBreak = null)
        {
            // Using ! with a greater than test is a fast way of testing the following conditions:
            //  pos === undefined || pos === null || isNaN(pos) || pos < 0;
            if (!(pos >= 0))
            {
                return pos;
            }

            // Keep in sync with couldStartTrivia
            while (true)
            {
                var ch = text.CharCodeAt(pos);
                switch (ch)
                {
                    case CharacterCodes.CarriageReturn:
                        if (text.CharCodeAt(pos + 1) == CharacterCodes.LineFeed)
                        {
                            pos++;
                        }

                        goto case CharacterCodes.LineFeed;
                    case CharacterCodes.LineFeed:
                        pos++;
                        if (stopAfterLineBreak == true)
                        {
                            return pos;
                        }

                        continue;
                    case CharacterCodes.Tab:
                    case CharacterCodes.VerticalTab:
                    case CharacterCodes.FormFeed:
                    case CharacterCodes.Space:
                        pos++;
                        continue;
                    case CharacterCodes.Slash:
                        if (text.CharCodeAt(pos + 1) == CharacterCodes.Slash)
                        {
                            pos += 2;
                            while (pos < text.Length)
                            {
                                if (IsLineBreak(text.CharCodeAt(pos)))
                                {
                                    break;
                                }

                                pos++;
                            }

                            continue;
                        }

                        if (text.CharCodeAt(pos + 1) == CharacterCodes.Asterisk)
                        {
                            pos += 2;
                            while (pos < text.Length)
                            {
                                if (text.CharCodeAt(pos) == CharacterCodes.Asterisk &&
                                    text.CharCodeAt(pos + 1) == CharacterCodes.Slash)
                                {
                                    pos += 2;
                                    break;
                                }

                                pos++;
                            }

                            continue;
                        }

                        break;

                    case CharacterCodes.LessThan:
                    case CharacterCodes.equals:
                    case CharacterCodes.GreaterThan:
                        if (IsConflictMarkerTrivia(text, pos))
                        {
                            pos = ScanConflictMarkerTrivia(text, pos);
                            continue;
                        }

                        break;

                    case CharacterCodes.Hash:
                        if (pos == 0 && IsShebangTrivia(text, pos))
                        {
                            pos = ScanShebangTrivia(text, pos);
                            continue;
                        }

                        break;

                    default:
                        if (ch > CharacterCodes.MaxAsciiCharacter)
                        {
                            if (IsWhiteSpace(ch) || IsLineBreak(ch))
                            {
                                pos++;
                                continue;
                            }
                        }

                        break;
                }

                return pos;
            }
        }

        private static bool IsIdentifierStart(CharacterCodes ch, ScriptTarget languageVersion)
        {
            return (ch >= CharacterCodes.A && ch <= CharacterCodes.Z) || (ch >= CharacterCodes.a && ch <= CharacterCodes.z) ||
                   ch == CharacterCodes.Dollar || ch == CharacterCodes._ ||
                   (ch > CharacterCodes.MaxAsciiCharacter && IsUnicodeIdentifierStart(ch, languageVersion));
        }

        private static bool IsUnicodeIdentifierStart(CharacterCodes code, ScriptTarget languageVersion)
        {
            return languageVersion >= ScriptTarget.Es5
                ? LookupInUnicodeMap((int)code, UnicodeEs5IdentifierStart)
                : LookupInUnicodeMap((int)code, UnicodeEs3IdentifierStart);
        }

        private static bool IsUnicodeIdentifierPart(CharacterCodes code, ScriptTarget languageVersion)
        {
            return languageVersion >= ScriptTarget.Es5
                ? LookupInUnicodeMap((int)code, UnicodeEs5IdentifierPart)
                : LookupInUnicodeMap((int)code, UnicodeEs3IdentifierPart);
        }

        private static bool LookupInUnicodeMap(int code, int[] map)
        {
            // Bail out quickly if it couldn't possibly be in the map.
            if (code < map[0])
            {
                return false;
            }

            // Perform binary search in one of the Unicode range maps
            var lo = 0;
            int hi = map.Length;

            while (lo + 1 < hi)
            {
                var mid = lo + ((hi - lo) / 2);

                // mid has to be even to catch a range's beginning
                mid -= mid % 2;
                if (map[mid] <= code && code <= map[mid + 1])
                {
                    return true;
                }

                if (code < map[mid])
                {
                    hi = mid;
                }
                else
                {
                    lo = mid + 2;
                }
            }

            return false;
        }

        [SuppressMessage("Microsoft.Performance", "CA1801")]
        private static bool IsShebangTrivia(TextSource text, int pos)
        {
            // Shebangs check must only be done at the start of the file
            Contract.Requires(pos == 0);

            // DScript doesn't support shebangs
            return false;

            // return ShebangTriviaRegex.IsMatch(text);
        }

        [SuppressMessage("Microsoft.Performance", "CA1801")]
        private static int ScanShebangTrivia(TextSource text, int pos)
        {
            // DScript doesn't support shebangs
            return pos;

            // var shebang = ShebangTriviaRegex.Match(text).Groups[0];
            // pos = pos + shebang.Length;
            // return pos;
        }

        /// <nodoc />
        public static bool IsWhiteSpace(CharacterCodes ch)
        {
            // nextLine Note is in the Zs space, and should be considered to be a whitespace.
            // It is explicitly not a line-break as it isn't in the exact set specified by EcmaScript.
            return ch == CharacterCodes.Space ||
                   ch == CharacterCodes.Tab ||
                   ch == CharacterCodes.VerticalTab ||
                   ch == CharacterCodes.FormFeed ||
                   ch == CharacterCodes.NonBreakingSpace ||
                   ch == CharacterCodes.NextLine ||
                   ch == CharacterCodes.Ogham ||
                   (ch >= CharacterCodes.EnQuad && ch <= CharacterCodes.ZeroWidthSpace) ||
                   ch == CharacterCodes.NarrowNoBreakSpace ||
                   ch == CharacterCodes.MathematicalSpace ||
                   ch == CharacterCodes.IdeographicSpace ||
                   ch == CharacterCodes.ByteOrderMark;
        }

        /// <nodoc />
        internal static bool IsOctalDigit(CharacterCodes ch)
        {
            return ch >= CharacterCodes._0 && ch <= CharacterCodes._7;
        }

        private static bool IsConflictMarkerTrivia(TextSource text, int pos)
        {
            Contract.Requires(pos >= 0);

            // Conflict markers must be at the start of a line.
            if (pos == 0 || IsLineBreak(text.CharCodeAt(pos - 1)))
            {
                var ch = text.CharCodeAt(pos);

                if (pos + MergeConflictMarkerLength < text.Length)
                {
                    for (int i = 0, n = MergeConflictMarkerLength; i < n; i++)
                    {
                        if (text.CharCodeAt(pos + i) != ch)
                        {
                            return false;
                        }
                    }

                    return ch == CharacterCodes.equals ||
                           text.CharCodeAt(pos + MergeConflictMarkerLength) == CharacterCodes.Space;
                }
            }

            return false;
        }

        private static int ScanConflictMarkerTrivia(TextSource text, int pos, ErrorCallback error = null)
        {
            error?.Invoke(Errors.Merge_conflict_marker_encountered, MergeConflictMarkerLength);

            var ch = text.CharCodeAt(pos);
            var len = text.Length;

            if (ch == CharacterCodes.LessThan || ch == CharacterCodes.GreaterThan)
            {
                while (pos < len && !IsLineBreak(text.CharCodeAt(pos)))
                {
                    pos++;
                }
            }
            else
            {
                Contract.Assert(ch == CharacterCodes.@equals);

                // Consume everything from the start of the mid-conlict marker to the start of the next
                // end-conflict marker.
                while (pos < len)
                {
                    ch = text.CharCodeAt(pos);
                    if (ch == CharacterCodes.GreaterThan && IsConflictMarkerTrivia(text, pos))
                    {
                        break;
                    }

                    pos++;
                }
            }

            return pos;
        }

        /// <summary>
        /// Gets the number of new lines when parsing whitespace and comments encountered.
        /// After calling it the triva values will be reset.
        /// </summary>
        public void CollectAccumulatedTriviaAndReset(INode node)
        {
            if (m_needSkipTrivia)
            {
                return;
            }

            if (m_newLineTriviaCount != 0 || m_comments.Count > 0)
            {
                var trivia = new Trivia();

                trivia.LeadingNewLineCount = m_newLineTriviaCount;

                if (m_comments.Count > 0)
                {
                    trivia.LeadingComments = m_comments.ToArray();
                }

                m_sourceFile.RecordTrivia(node, trivia);
            }

            m_newLineTriviaCount = 0;
            m_comments.Clear();
            m_lastNodeForTrivia = null;
        }

        /// <summary>
        /// Helper method for nodes don't get created in logical DFS order.
        /// </summary>
        public void MoveTrivia(INode from, INode to)
        {
            if (m_needSkipTrivia)
            {
                return;
            }

            m_sourceFile.MoveTriva(from, to);
        }

        /// <summary>
        /// This allows the parse to indicate which node the trailing trivia should be placed on.
        /// An example is in lists, i.e. statement list just before the separator the statement in the list is set here,
        /// so that the trailing trivia is associated with the statement and not the last node in the statement i.e. 42 in const x = 42;
        /// </summary>
        public void AllowTrailingTriviaOnNode(INode node)
        {
            m_lastNodeForTrivia = node?.GetActualNode();
        }

        /// <summary>
        /// Indicates to the scanner to process trailing trivia. Examples can be end of file, end of line and a good place in the parser
        /// where we are known to have a separator. For example the comma separator in an argument list.
        /// </summary>
        public void AssociateTrailingCommentsWithLastTrivia()
        {
            if (m_needSkipTrivia || m_lastNodeForTrivia == null)
            {
                return;
            }

            if (m_comments.Count > 0)
            {
                Trivia trivia;
                if (!m_sourceFile.PerNodeTrivia.TryGetValue(m_lastNodeForTrivia, out trivia))
                {
                    trivia = new Trivia();
                    m_sourceFile.RecordTrivia(m_lastNodeForTrivia, trivia);
                }

                if (trivia.TrailingComments == null)
                {
                    trivia.TrailingComments = m_comments.ToArray();
                }
                else
                {
                    trivia.TrailingComments = trivia.TrailingComments.Concat(m_comments).ToArray();
                }

                m_comments.Clear();
            }

            m_lastNodeForTrivia = null;
        }

        /// <summary>
        /// Skips over all non-comment trivia nodes, keeping the start position
        /// </summary>
        public SyntaxKind ScanAndSkipOverNonCommentTrivia()
        {
            var token = Scan();
            var start = m_startPos;

            while (IsNonCommentTrivia(token))
            {
                token = Scan();
                m_startPos = start;
            }

            return token;
        }

        /// <summary>
        /// Whether this is trivia different than a single or multiline comment
        /// /// </summary>
        private static bool IsNonCommentTrivia(SyntaxKind kind)
        {
            return kind >= SyntaxKind.FirstNonCommentTriviaToken && kind <= SyntaxKind.LastNonCommentTriviaToken;
        }
    }
}

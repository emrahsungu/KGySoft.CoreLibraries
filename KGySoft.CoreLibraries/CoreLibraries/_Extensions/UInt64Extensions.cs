﻿#region Copyright

///////////////////////////////////////////////////////////////////////////////
//  File: UInt64Extensions.cs
///////////////////////////////////////////////////////////////////////////////
//  Copyright (C) KGy SOFT, 2005-2019 - All Rights Reserved
//
//  You should have received a copy of the LICENSE file at the top-level
//  directory of this distribution. If not, then this file is considered as
//  an illegal copy.
//
//  Unauthorized copying of this file, via any medium is strictly prohibited.
///////////////////////////////////////////////////////////////////////////////

#endregion

#region Usings

using System;
#if NETCOREAPP3_0
using System.Numerics;
#endif
using System.Runtime.CompilerServices;
using System.Security; 

#endregion

namespace KGySoft.CoreLibraries
{
    internal static class UInt64Extensions
    {
        #region Methods

        [MethodImpl(MethodImpl.AggressiveInlining)]
        internal static bool IsSingleFlag(this ulong value) => value != 0 && (value & (value - 1UL)) == 0UL;

        [MethodImpl(MethodImpl.AggressiveInlining)]
        internal static int GetFlagsCount(this ulong value)
        {
#if NET35 || NET40 || NET45 || NET472 || NETSTANDARD2_0 || NETSTANDARD2_1 || NETCOREAPP2_0
            // There are actually better general solutions than this but for enums we usually expect
            // only a few flags set. Up to 3-4 flags this solution is faster than the optimal Hamming weight solution.
            int result = 0;
            while (value != 0)
            {
                result++;
                value &= value - 1;
            }

            return result;
#else
            return BitOperations.PopCount(value);
#endif
        }

        internal static int DecimalDigitsCount(this ulong value)
        {
            return value >= 10000000000000000000UL ? 20
                : value >= 1000000000000000000UL ? 19
                : value >= 100000000000000000UL ? 18
                : value >= 10000000000000000UL ? 17
                : value >= 1000000000000000UL ? 16
                : value >= 100000000000000UL ? 15
                : value >= 10000000000000UL ? 14
                : value >= 1000000000000UL ? 13
                : value >= 100000000000UL ? 12
                : value >= 10000000000UL ? 11
                : value >= 1000000000UL ? 10
                : value >= 100000000UL ? 9
                : value >= 10000000UL ? 8
                : value >= 1000000UL ? 7
                : value >= 100000UL ? 6
                : value >= 10000UL ? 5
                : value >= 1000UL ? 4
                : value >= 100UL ? 3
                : value >= 10UL ? 2
                : 1;
        }

        [SecuritySafeCritical]
        internal static unsafe string QuickToString(this ulong value, bool isNegative)
        {
            if (value == 0)
                return "0";

            char* buf = stackalloc char[21];
            int size = 0;
            while (value > 0)
            {
                buf[size] = (char)(value % 10 + '0');
                size += 1;
                value /= 10;
            }

            if (isNegative)
            {
                buf[size] = '-';
                size += 1;
            }

            string result = new String('\0', size);
            fixed (char* s = result)
            {
                for (int i = size - 1; i >= 0; i--)
                    s[size - i - 1] = buf[i];
            }

            return result;
        }

        #endregion
    }
}

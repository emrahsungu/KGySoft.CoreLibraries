﻿#region Copyright

///////////////////////////////////////////////////////////////////////////////
//  File: MutableStringBuilder.cs
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
using System.Runtime.CompilerServices;
using System.Security; 

#endregion

namespace KGySoft.CoreLibraries
{
    [SecurityCritical]
    internal struct MutableStringBuilder
    {
        #region Fields

        private readonly MutableString str;

        private int pos;

        #endregion

        #region Properties and Indexers

        #region Properties

        internal int Capacity => str.Length;

        internal int Length => pos;

        #endregion

        #region Indexers

        internal char this[int index]
        {
            get
            {
                Debug.Assert(index < Length, "Invalid index");
                return str[index];
            }
            set
            {
                Debug.Assert(index < Length, "Invalid index");
                str[index] = value;
            }
        }

        #endregion

        #endregion

        #region Constructors

        internal MutableStringBuilder(in MutableString s)
        {
            str = s;
            pos = 0;
        }

        internal unsafe MutableStringBuilder(char* s, int len)
        {
            str = new MutableString(s, len);
            pos = 0;
        }


        #endregion

        #region Methods

        #region Public Methods

        [SecuritySafeCritical]
        public override string ToString() => str.Substring(0, Length).ToString();

        #endregion

        #region Internal Methods

        [MethodImpl(MethodImpl.AggressiveInlining)]
        internal void Append(char c)
        {
            Debug.Assert(Length < Capacity, "Not enough capacity");
            str[pos] = c;
            pos += 1;
        }

        [MethodImpl(MethodImpl.AggressiveInlining)]
        internal void Append(string s)
        {
            Debug.Assert(Length + s.Length <= Capacity, "Not enough capacity");
            WriteString(pos, s);
            pos += s.Length;
        }

        [MethodImpl(MethodImpl.AggressiveInlining)]
        internal void Append(string s, int startIndex, int count)
        {
            Debug.Assert(Length + count <= Capacity, "Not enough capacity");
            Debug.Assert(startIndex + count <= s.Length, "Invalid arguments");
            WriteString(pos, s, startIndex, count);
            pos += count;
        }

        internal void Append(ulong value, bool isNegative, int size)
        {
            Debug.Assert(Length + size <= Capacity, "Not enough capacity");
            if (value == 0)
            {
                Append('0');
                return;
            }

            int i = size + pos;
            while (value > 0)
            {
                str[--i] = (char)(value % 10 + '0');
                value /= 10;
            }

            if (isNegative)
                str[--i] = '-';

            Debug.Assert(i == pos, "Invalid size");
            pos += size;
        }

        internal void Insert(int index, char c)
        {
            Debug.Assert(index <= Length, "Invalid index");
            if (index == pos)
            {
                Append(c);
                return;
            }

            for (int i = pos - 1; i >= index; i--)
                str[i + 1] = str[i];
            str[index] = c;
            pos += 1;
        }

        internal void Insert(int index, string s)
        {
            Debug.Assert(index <= Length, "Invalid index");
            if (index == pos)
            {
                Append(s);
                return;
            }

            int len = s.Length;
            for (int i = pos - 1; i >= index; i--)
                str[i + len] = str[i];
            WriteString(index, s);
            pos += s.Length;
        }

        #endregion

        #region Private Methods

        [SecurityCritical]
        [MethodImpl(MethodImpl.AggressiveInlining)]
        private unsafe void WriteString(int index, string s)
        {
            int len = s.Length;
            if (len > 8)
            {
                fixed (char* ptr = s)
                    Buffer.MemoryCopy(ptr, str.AddressOf(pos), (Capacity - pos) << 1, len << 1);
                return;
            }

            for (int i = 0; i < len; i++)
                str[index + i] = s[i];
        }

        [SecurityCritical]
        [MethodImpl(MethodImpl.AggressiveInlining)]
        private unsafe void WriteString(int targetIndex, string s, int sourceIndex, int count)
        {
            if (count > 8)
            {
                fixed (char* ptr = s)
                    Buffer.MemoryCopy(ptr + sourceIndex, str.AddressOf(pos), (Capacity - pos) << 1, count << 1);
                return;
            }

            for (int i = 0; i < count; i++)
                str[targetIndex + i] = s[sourceIndex + i];
        }

        #endregion

        #endregion
    }
}

﻿#region Copyright

///////////////////////////////////////////////////////////////////////////////
//  File: EnumerableExtensions.cs
///////////////////////////////////////////////////////////////////////////////
//  Copyright (C) KGy SOFT, 2018 - All Rights Reserved
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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KGySoft.Libraries.Annotations;
using KGySoft.Libraries.Collections;
using KGySoft.Libraries.Reflection;
using KGySoft.Libraries.Resources;

#endregion

namespace KGySoft.Libraries
{
    /// <summary>
    /// Extension methods for <see cref="IEnumerable{T}"/> and <see cref="IEnumerator"/> types.
    /// </summary>
    public static class EnumerableExtensions
    {
        #region Methods

        #region Public Methods

        /// <summary>
        /// Similarly to <see cref="List{T}.ForEach">List{T}.ForEach</see> processes an action on each element of an enumerable collection.
        /// </summary>
        /// <returns>Returns the original list making possible to link it into a LINQ chain.</returns>
        public static IEnumerable<T> ForEach<T>(this IEnumerable<T> source, Action<T> action)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source), Res.Get(Res.ArgumentNull));
            if (action == null)
                throw new ArgumentNullException(nameof(action), Res.Get(Res.ArgumentNull));

            foreach (T item in source)
            {
                action(item);
            }

            return source;
        }

        /// <summary>
        /// Creates a <see cref="CircularList{T}"/> from an <see cref="IEnumerable{T}"/>.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">The <see cref="IEnumerable{T}"/> to create a <see cref="CircularList{T}"/> from.</param>
        /// <returns>A <see cref="CircularList{T}"/> that contains elements from the input sequence.</returns>
        /// <remarks>
        /// The method forces immediate query evaluation and returns a <see cref="CircularList{T}"/> that contains the query results.
        /// You can append this method to your query in order to obtain a cached copy of the query results.
        /// </remarks>
        public static CircularList<TSource> ToCircularList<TSource>(this IEnumerable<TSource> source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source), Res.Get(Res.ArgumentNull));

            return new CircularList<TSource>(source);
        }

        /// <summary>
        /// Shuffles an enumerable <paramref name="source"/> (randomizes its elements) using the provided <paramref name="seed"/>.
        /// </summary>
        /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">The <see cref="IEnumerable{T}"/> to shuffle its elements.</param>
        /// <param name="seed">The seed to use for the shuffling.</param>
        /// <returns>An <see cref="IEnumerable{T}"/> which contains the elements of the <paramref name="source"/> in randomized order.</returns>
        public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source, int seed)
        {
            return Shuffle(source, new Random(seed));
        }

        /// <summary>
        /// Shuffles an enumerable <paramref name="source"/> (randomizes its elements).
        /// </summary>
        /// <typeparam name="T">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">The <see cref="IEnumerable{T}"/> to shuffle its elements.</param>
        /// <returns>An <see cref="IEnumerable{T}"/> which contains the elements of the <paramref name="source"/> in randomized order.</returns>
        public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source)
        {
            return Shuffle(source, new Random());
        }

        ///// <summary>
        ///// Converts an <see cref="IEnumerable{T}"/> source to <see cref="DataTable"/>. All of the readable public properties will be put in the result table.
        ///// </summary>
        //public static DataTable ToDataTable<T>(this IEnumerable<T> source)
        //{
        //    if (source == null)
        //        throw new ArgumentNullException("source", Res.Get(Res.ArgumentNull));

        //    PropertyInfo[] columns = (from p in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
        //                              where p.CanRead && p.GetGetMethod().GetParameters().Length == 0
        //                              select p).ToArray();

        //    return ToDataTable(source, columns);
        //}

        ///// <summary>
        ///// Converts an <see cref="IEnumerable{T}"/> source to <see cref="DataTable"/>. Only defined properties will be put in the result table.
        ///// </summary>
        ///// <param name="source">Source collection.</param>
        ///// <param name="columns">Instance properties of <typeparamref name="T"/> that will be converted to columns in given order.</param>
        //public static DataTable ToDataTable<T>(this IEnumerable<T> source, params string[] columns)
        //{
        //    if (source == null)
        //        throw new ArgumentNullException("source", Res.Get(Res.ArgumentNull));

        //    if (columns == null)
        //        throw new ArgumentNullException("columns", Res.Get(Res.ArgumentNull));

        //    Type type = typeof(T);

        //    PropertyInfo[] props = (from propName in columns
        //                            select type.GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)).ToArray();

        //    return ToDataTable(source, props);
        //}

        ///// <summary>
        ///// Converts an <see cref="IEnumerable{T}"/> source to <see cref="DataTable"/>. Only defined properties will be put in the result table.
        ///// </summary>
        ///// <param name="source">Source collection.</param>
        ///// <param name="columns">Properties of <typeparamref name="T"/> will be converted to columns in given order.</param>
        //private static DataTable ToDataTable<T>(this IEnumerable<T> source, PropertyInfo[] columns)
        //{
        //    if (source == null)
        //        throw new ArgumentNullException("source", Res.Get(Res.ArgumentNull));

        //    if (columns == null)
        //        throw new ArgumentNullException("columns", Res.Get(Res.ArgumentNull));

        //    DataTable result = new DataTable();

        //    foreach (PropertyInfo prop in columns)
        //    {
        //        result.Columns.Add(prop.Name, prop.PropertyType);
        //    }

        //    foreach (T item in source)
        //    {
        //        DataRow row = result.NewRow();
        //        for (int i = 0; i < columns.Length; i++)
        //        {
        //            row[i] = Reflector.GetProperty(item, columns[i]);
        //        }
        //        result.Rows.Add(row);
        //    }
        //    return result;
        //}

        #endregion

        #region Internal Methods

        /// <summary>
        /// Adds an element to an enumerable collection if possible.
        /// That is, if <paramref name="source"/> implements either the non-generic <see cref="IList"/> or <see cref="IDictionary"/> interfaces,
        /// or the generic <see cref="ICollection{T}"/> interface.
        /// </summary>
        internal static void Add(this IEnumerable source, object item)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source), Res.Get(Res.ArgumentNull));

            IList list = source as IList;
            if (list != null
#if NET35
                // IList with null element: skip because generic collections below .NET 4 may not support null elements of nullable types
                && item != null
#elif !(NET40 || NET45)
#error .NET version is not set or not supported!
#endif

                )
            {
                list.Add(item);
                return;
            }

            IDictionary dictionary = source as IDictionary;
            if (item is DictionaryEntry && dictionary != null)
            {
                DictionaryEntry entry = (DictionaryEntry)item;
                dictionary.Add(entry.Key, entry.Value);
                return;
            }

            Type sourceType = source.GetType();
            Type collectionType = typeof(ICollection<>);
            foreach (Type i in sourceType.GetInterfaces())
            {
                if (i.IsGenericType && i.GetGenericTypeDefinition() == collectionType)
                {
                    MethodInfo mi = i.GetMethod("Add");
                    if (mi.GetParameters()[0].ParameterType.CanAcceptValue(item))
                    {
                        MethodInvoker.GetMethodInvoker(mi).Invoke(source, item);
                        return;
                    }
                }
            }

#if NET35
            if (list != null) // && item == null
            {
                list.Add(item);
                return;
            }
#elif !(NET40 || NET45)
#error .NET version is not set or not supported!
#endif

            throw new NotSupportedException(Res.Get(Res.EnumerableCannotAdd, item ?? "null", source.GetType()));
        }

        /// <summary>
        /// Clears an enumerable collection if possible.
        /// That is, if <paramref name="source"/> implements either the non-generic <see cref="IList"/> or <see cref="IDictionary"/> interfaces,
        /// or the generic <see cref="ICollection{T}"/> interface.
        /// </summary>
        internal static void Clear([NoEnumeration]this IEnumerable source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source), Res.Get(Res.ArgumentNull));

            IList list = source as IList;
            if (list != null)
            {
                list.Clear();
                return;
            }

            IDictionary dictionary = source as IDictionary;
            if (dictionary != null)
            {
                dictionary.Clear();
                return;
            }

            Type sourceType = source.GetType();
            Type collectionType = typeof(ICollection<>);
            foreach (Type i in sourceType.GetInterfaces())
            {
                if (i.IsGenericType && i.GetGenericTypeDefinition() == collectionType)
                {
                    MethodInfo mi = i.GetMethod("Clear");
                    MethodInvoker.GetMethodInvoker(mi).Invoke(source);
                    return;
                }
            }

            throw new InvalidOperationException(Res.Get(Res.EnumerableCannotClear, source.GetType().FullName));
        }

        #endregion

        #region Private Methods

        private static IEnumerable<T> Shuffle<T>(IEnumerable<T> source, Random rand)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source), Res.Get(Res.ArgumentNull));

            //return from indexedItem in
            //           (from item in source
            //            select new { Index = rand.Next(), Value = item })
            //       orderby indexedItem.Index
            //       select indexedItem.Value;
            // above is the same as LINQ expression:
            return source.Select(
                item => new { Index = rand.Next(), Value = item }).OrderBy(i => i.Index).Select(i => i.Value);
        }

        #endregion

        #endregion
    }
}

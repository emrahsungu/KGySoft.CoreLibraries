﻿#region Copyright

///////////////////////////////////////////////////////////////////////////////
//  File: BinarySerializationFormatter.cs
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
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security;
using System.Text;

using KGySoft.Collections;
using KGySoft.CoreLibraries;
using KGySoft.Reflection;

#endregion

namespace KGySoft.Serialization
{
    public sealed partial class BinarySerializationFormatter
    {
        /// <summary>
        /// A manager class that provides that stored types will be built up in the same order both at serialization and deserialization for complex types.
        /// </summary>
        private sealed class SerializationManager : SerializationManagerBase
        {
            #region Constants

            private const int ticksPerMinute = 600_000_000;

            #endregion

            #region Fields

            private Dictionary<Assembly, int> assemblyIndexCache;
            private Dictionary<Type, int> typeIndexCache;
#if !NET35 // binders can map type to names only in .NET 4.0 and above
            private Dictionary<Type, (string AssemblyName, string TypeName)> binderCache;
            private Dictionary<string, int> assemblyNameIndexCache;
            private Dictionary<string, int> typeNameIndexCache;
#endif
            private int idCounter;
            private Dictionary<object, int> idCacheByValue;
            private Dictionary<object, int> idCacheByRef;

            #endregion

            #region Properties

            private Dictionary<Assembly, int> AssemblyIndexCache
            {
                get
                {
                    if (assemblyIndexCache == null)
                    {
                        assemblyIndexCache = new Dictionary<Assembly, int>(KnownAssemblies.Length + 1);
                        KnownAssemblies.ForEach(a => assemblyIndexCache.Add(a, assemblyIndexCache.Count));
                    }

                    return assemblyIndexCache;
                }
            }

            private Dictionary<Type, int> TypeIndexCache
            {
                get
                {
                    if (typeIndexCache == null)
                    {
                        typeIndexCache = new Dictionary<Type, int>(KnownTypes.Length + 1);
                        KnownTypes.ForEach(a => typeIndexCache.Add(a, typeIndexCache.Count));
                    }

                    return typeIndexCache;
                }
            }


#if !NET35
            private Dictionary<string, int> AssemblyNameIndexCache
            {
                get
                {
                    return assemblyNameIndexCache ?? (assemblyNameIndexCache = new Dictionary<string, int>(1));
                }
            }

            private Dictionary<string, int> TypeNameIndexCache => typeNameIndexCache ?? (typeNameIndexCache = new Dictionary<string, int>(1));

#endif

            private int AssemblyIndexCacheCount
            {
                get
                {
                    return (assemblyIndexCache?.Count ?? KnownAssemblies.Length)
#if !NET35
                        + (assemblyNameIndexCache?.Count ?? 0)
#endif
                        ;
                }
            }

            private int OmitAssemblyIndex => AssemblyIndexCacheCount;
            private int NewAssemblyIndex => AssemblyIndexCacheCount + 1;
            private int InvariantAssemblyIndex => AssemblyIndexCacheCount + 2; // for natively supported types, which can be in any assembly in different frameworks

            private int TypeIndexCacheCount
            {
                get
                {
                    return (typeIndexCache?.Count ?? KnownTypes.Length)
#if !NET35
                        + (typeNameIndexCache?.Count ?? 0)
#endif
                        ;
                }
            }

            private int NewTypeIndex => TypeIndexCacheCount + 1;

            #endregion

            #region Constructors

            internal SerializationManager(StreamingContext context, BinarySerializationOptions options, SerializationBinder binder, ISurrogateSelector surrogateSelector) :
                base(context, options, binder, surrogateSelector)
            {
            }

            #endregion

            #region Methods

            #region Static Methods

#if !NET35
            private string GetTypeNameIndexCacheKey(Type type, string binderAsmName, string binderTypeName)
                => (binderAsmName ?? type.Assembly.FullName) + ":" + (binderTypeName ?? type.FullName);
#endif

            /// <summary>
            /// Writes a <paramref name="length"/> bytes length value in the possible most compact form.
            /// </summary>
            private static void WriteDynamicInt(BinaryWriter bw, DataTypes dataType, int length, ulong value)
            {
                switch (length)
                {
                    case 2:
                        if (value >= (1UL << 7)) // up to 7 bits
                        {
                            WriteDataType(bw, dataType);
                            bw.Write((ushort)value);
                            return;
                        }

                        break;

                    case 4:
                        if (value >= (1UL << 21)) // up to 3*7 bits
                        {
                            WriteDataType(bw, dataType);
                            bw.Write((uint)value);
                            return;
                        }

                        break;

                    case 8:
                        if (value >= (1UL << 49)) // up to 7*7 bits
                        {
                            WriteDataType(bw, dataType);
                            bw.Write(value);
                            return;
                        }

                        break;

                    default:
                        // should never occur, throwing internal error without resource
                        throw new ArgumentOutOfRangeException(nameof(length));
                }

                // storing the value as 7-bit encoded int, which will be shorter
                dataType |= DataTypes.Store7BitEncoded;
                WriteDataType(bw, dataType);
                Write7BitLong(bw, value);
            }

            private static bool IsSupportedCollection(Type type) => GetSupportedCollectionType(type) != DataTypes.Null;

            private static DataTypes GetSupportedCollectionType(Type type)
            {
                if (type.IsArray)
                    return DataTypes.Array;

                if (type.IsNullable())
                {
                    switch (GetSupportedCollectionType(type.GetGenericArguments()[0]))
                    {
                        case DataTypes.DictionaryEntry:
                            return DataTypes.DictionaryEntryNullable;
                        case DataTypes.KeyValuePair:
                            return DataTypes.KeyValuePairNullable;
                        default:
                            return DataTypes.Null;
                    }
                }

                if (type.IsGenericType)
                    type = type.GetGenericTypeDefinition();
                if (type.IsGenericParameter)
                    type = type.DeclaringType;
                return supportedCollections.GetValueOrDefault(type, DataTypes.Null);
            }

            private static bool IsPureType(DataTypes dt) => (dt & (DataTypes.ImpureType | DataTypes.Enum)) == DataTypes.Null;

            /// <summary>
            /// Retrieves the value type(s) for a dictionary.
            /// </summary>
            [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity", Justification = "Very simple method with many common cases")]
            private static IList<DataTypes> GetDictionaryValueTypes(IList<DataTypes> collectionTypeDescriptor)
            {
                // descriptor must refer a generic dictionary type here
                Debug.Assert(collectionTypeDescriptor.Count > 0, "Type description is invalid: not enough data");
                Debug.Assert((collectionTypeDescriptor[0] & DataTypes.Dictionary) != DataTypes.Null, $"Type description is invalid: {collectionTypeDescriptor[0] & DataTypes.CollectionTypes} is not a dictionary type.");

                CircularList<DataTypes> result = new CircularList<DataTypes>();
                int skipLevel = 0;
                bool startingDictionaryResolved = false;
                foreach (DataTypes dataType in collectionTypeDescriptor)
                {
                    // we reached the value
                    if (startingDictionaryResolved && skipLevel == 0)
                    {
                        result.Add(dataType);
                        continue;
                    }

                    switch (dataType & DataTypes.CollectionTypes)
                    {
                        // No collection type indicated: element type belongs to an already skipped previous collection.
                        case DataTypes.Null:
                            skipLevel--;
                            break;

                        // Collections with a single element: decreasing level if element is specified.
                        // Otherwise it is a nested collection, skip level kept for the next item.
                        case DataTypes.Array:
                        case DataTypes.List:
                        case DataTypes.LinkedList:
                        case DataTypes.HashSet:
                        case DataTypes.Queue:
                        case DataTypes.Stack:
                        case DataTypes.CircularList:
                        case DataTypes.SortedSet:
                        case DataTypes.ArrayList:
                        case DataTypes.QueueNonGeneric:
                        case DataTypes.StackNonGeneric:
                        case DataTypes.StringCollection:
                            if ((dataType & ~DataTypes.CollectionTypes) != DataTypes.Null)
                                skipLevel--;
                            break;

                        // Dictionary type: Entry point of the loop or skipped nested key collections.
                        // If element type is specified, value type starts on next position.
                        // Otherwise, key is a nested collection and we need to skip it.
                        case DataTypes.Dictionary:
                        case DataTypes.SortedList:
                        case DataTypes.SortedDictionary:
                        case DataTypes.CircularSortedList:
                        case DataTypes.Hashtable:
                        case DataTypes.SortedListNonGeneric:
                        case DataTypes.ListDictionary:
                        case DataTypes.HybridDictionary:
                        case DataTypes.OrderedDictionary:
                        case DataTypes.StringDictionary:
                        case DataTypes.KeyValuePair:
                        case DataTypes.DictionaryEntry:
                        case DataTypes.KeyValuePairNullable:
                        case DataTypes.DictionaryEntryNullable:
                            // this check works because flags cannot be combined with collection types (nullable "collections" have different values)
                            if ((dataType & ~DataTypes.CollectionTypes) == DataTypes.Null)
                                skipLevel++;
                            startingDictionaryResolved = true;
                            break;
                    }
                }

                return result;
            }

            private static bool TryWritePrimitive(BinaryWriter bw, object data)
            {
                if (data == null)
                {
                    WriteDataType(bw, DataTypes.Null);
                    return true;
                }

                switch (primitiveTypes.GetValueOrDefault(data.GetType()))
                {
                    case DataTypes.Bool:
                        WriteDataType(bw, DataTypes.Bool);
                        bw.Write((bool)data);
                        return true;
                    case DataTypes.UInt8:
                        WriteDataType(bw, DataTypes.UInt8);
                        bw.Write((byte)data);
                        return true;
                    case DataTypes.Int8:
                        WriteDataType(bw, DataTypes.Int8);
                        bw.Write((sbyte)data);
                        return true;
                    case DataTypes.Int16:
                        WriteDynamicInt(bw, DataTypes.Int16, 2, (ulong)(short)data);
                        return true;
                    case DataTypes.UInt16:
                        WriteDynamicInt(bw, DataTypes.UInt16, 2, (ushort)data);
                        return true;
                    case DataTypes.Int32:
                        WriteDynamicInt(bw, DataTypes.Int32, 4, (ulong)(int)data);
                        return true;
                    case DataTypes.UInt32:
                        WriteDynamicInt(bw, DataTypes.UInt32, 4, (uint)data);
                        return true;
                    case DataTypes.Int64:
                        WriteDynamicInt(bw, DataTypes.Int64, 8, (ulong)(long)data);
                        return true;
                    case DataTypes.UInt64:
                        WriteDynamicInt(bw, DataTypes.UInt64, 8, (ulong)data);
                        return true;
                    case DataTypes.Char:
                        WriteDynamicInt(bw, DataTypes.Char, 2, (char)data);
                        return true;
                    case DataTypes.String:
                        WriteDataType(bw, DataTypes.String);
                        bw.Write((string)data);
                        return true;
                    case DataTypes.Single:
                        WriteDynamicInt(bw, DataTypes.Single, 4, BitConverter.ToUInt32(BitConverter.GetBytes((float)data), 0));
                        return true;
                    case DataTypes.Double:
                        WriteDynamicInt(bw, DataTypes.Double, 8, (ulong)BitConverter.DoubleToInt64Bits((double)data));
                        return true;
                    case DataTypes.IntPtr:
                        WriteDynamicInt(bw, DataTypes.IntPtr, 8, (ulong)(IntPtr)data);
                        return true;
                    case DataTypes.UIntPtr:
                        WriteDynamicInt(bw, DataTypes.UIntPtr, 8, (ulong)(UIntPtr)data);
                        return true;
                    default:
                        return false;
                }
            }

            private static bool TryWriteSimpleNonPrimitive(BinaryWriter bw, object data)
            {
                switch (supportedNonPrimitiveElementTypes.GetValueOrDefault(data.GetType()))
                {
                    case DataTypes.Decimal:
                        WriteDataType(bw, DataTypes.Decimal);
                        bw.Write((decimal)data);
                        return true;
                    case DataTypes.DateTime:
                        WriteDataType(bw, DataTypes.DateTime);
                        WriteDateTime(bw, (DateTime)data);
                        return true;
                    case DataTypes.DateTimeOffset:
                        WriteDataType(bw, DataTypes.DateTimeOffset);
                        WriteDateTimeOffset(bw, (DateTimeOffset)data);
                        return true;
                    case DataTypes.TimeSpan:
                        WriteDataType(bw, DataTypes.TimeSpan);
                        bw.Write(((TimeSpan)data).Ticks);
                        return true;
                    case DataTypes.DBNull:
                        WriteDataType(bw, DataTypes.DBNull);
                        return true;
                    case DataTypes.Guid:
                        WriteDataType(bw, DataTypes.Guid);
                        bw.Write(((Guid)data).ToByteArray());
                        return true;
                    case DataTypes.BitVector32:
                        WriteDataType(bw, DataTypes.BitVector32);
                        bw.Write(((BitVector32)data).Data);
                        return true;
                    case DataTypes.BitVector32Section:
                        WriteDataType(bw, DataTypes.BitVector32Section);
                        WriteSection(bw, (BitVector32.Section)data);
                        return true;
                    case DataTypes.Version:
                        WriteDataType(bw, DataTypes.Version);
                        WriteVersion(bw, (Version)data);
                        return true;
                    case DataTypes.BitArray:
                        WriteDataType(bw, DataTypes.BitArray);
                        WriteBitArray(bw, (BitArray)data);
                        return true;
                    case DataTypes.StringBuilder:
                        WriteDataType(bw, DataTypes.StringBuilder);
                        WriteStringBuilder(bw, (StringBuilder)data);
                        return true;
                    case DataTypes.Object:
                        WriteDataType(bw, DataTypes.Object);
                        return true;
                    case DataTypes.Uri:
                        WriteDataType(bw, DataTypes.Uri);
                        WriteUri(bw, (Uri)data);
                        return true;
                    default:
                        return false;
                }
            }

            private static void WriteDateTime(BinaryWriter bw, DateTime dateTime)
            {
                bw.Write((byte)dateTime.Kind);
                bw.Write(dateTime.Ticks);
            }

            private static void WriteDateTimeOffset(BinaryWriter bw, DateTimeOffset dateTimeOffset)
            {
                bw.Write(dateTimeOffset.Ticks);
                bw.Write((short)(dateTimeOffset.Offset.Ticks / ticksPerMinute));
            }

            private static void WriteVersion(BinaryWriter bw, Version version)
            {
                bw.Write(version.Major);
                bw.Write(version.Minor);
                bw.Write(version.Build);
                bw.Write(version.Revision);
            }

            private static void WriteUri(BinaryWriter bw, Uri uri)
            {
                bw.Write(uri.IsAbsoluteUri);
                bw.Write(uri.GetComponents(UriComponents.SerializationInfoString, UriFormat.UriEscaped));
            }

            private static void WriteBitArray(BinaryWriter bw, BitArray bitArray)
            {
                int length = bitArray.Length;
                Write7BitInt(bw, bitArray.Length);
                if (length > 0)
                {
                    int[] value = bitArray.GetUnderlyingArray();
                    foreach (int i in value)
                        bw.Write(i);
                }
            }

            private static void WriteStringBuilder(BinaryWriter bw, StringBuilder sb)
            {
                Write7BitInt(bw, sb.Capacity);
                bw.Write(sb.ToString());
            }

            private static void WriteSection(BinaryWriter bw, BitVector32.Section section)
            {
                bw.Write(section.Mask);
                bw.Write(section.Offset);
            }

            #endregion

            #region Instance Methods

            #region Internal Methods

            /// <summary>
            /// Writing an object. Can be used both at root and object element level.
            /// </summary>>
            [SecurityCritical]
            internal void Write(BinaryWriter bw, object data, bool isRoot)
            {
                // if an existing id found, returning
                if (!isRoot && WriteId(bw, data))
                    return;

                // a.) Natively supported primitive types including string (no need to distinct nullable types here as they are boxed)
                if (TryWritePrimitive(bw, data))
                    return;

                // b.) Surrogate selector for any type
                Type type = data.GetType();
                if (ForceRecursiveSerializationOfSupportedTypes && !type.IsArray || TryUseSurrogateSelectorForAnyType && CanUseSurrogate(type))
                {
                    WriteRecursively(bw, data, isRoot);
                    return;
                }

                // c.) Natively supported non-primitive single types
                if (TryWriteSimpleNonPrimitive(bw, data))
                    return;

                // d.) enum: storing enum type, assembly qualified name and value: still shorter than by BinaryFormatter
                if (data is Enum)
                {
                    WriteEnum(bw, data);
                    return;
                }

                // e.) Supported collection or compound of collections
                if (TryWriteCollection(bw, data, isRoot))
                    return;

                // f.) Other non-pure types. DataTypes enum does not describe exact type information for them.

                // RuntimeType
                if (type == Reflector.RuntimeType)
                {
                    WriteDataType(bw, DataTypes.RuntimeType);
                    WriteType(bw, (Type)data, true);
                    return;
                }

                // BinarySerializable
                if (!IgnoreIBinarySerializable && data is IBinarySerializable binarySerializable)
                {
                    WriteDataType(bw, DataTypes.BinarySerializable);

                    if (isRoot)
                    {
                        // on root level writing the id even if the object is value type because the boxed reference can be shared
                        if (WriteId(bw, data))
                            Debug.Fail("Id of recursive object should be unknown on top level.");
                    }

                    WriteType(bw, type);
                    WriteBinarySerializable(bw, binarySerializable);
                    return;
                }

                // Any struct if can serialize
                if (CompactSerializationOfStructures && type.IsValueType && BinarySerializer.CanSerializeValueType(type, false))
                {
                    WriteDataType(bw, DataTypes.RawStruct);
                    WriteType(bw, type);
                    WriteValueType(bw, data);
                    return;
                }

                // Recursive serialization: if enabled or surrogate selector supports the type, or when type is serializable
                if (RecursiveSerializationAsFallback || CanUseSurrogate(type) || type.IsSerializable)
                {
                    WriteRecursively(bw, data, isRoot);
                    return;
                }

#pragma warning disable 618, 612
                // Any struct (obsolete but still supported as backward compatibility)
                if (ForcedSerializationValueTypesAsFallback && type.IsValueType)
                {
                    WriteDataType(bw, DataTypes.RawStruct);
                    WriteType(bw, type);
                    WriteValueType(bw, data);
                    return;
                }
#pragma warning restore 618, 612

                ThrowNotSupported(type);
            }

            #endregion

            #region Private Methods

            private void ThrowNotSupported(Type type) => throw new NotSupportedException(Res.BinarySerializationNotSupported(type, Options));

            /// <summary>
            /// Writes AssemblyQualifiedName of element types and array ranks if needed
            /// </summary>
            [SecurityCritical]
            private void WriteTypeNamesAndRanks(BinaryWriter bw, Type type)
            {
                // Impure types: type name
                DataTypes elementType = GetSupportedElementType(type);
                if (!IsPureType(elementType))
                {
                    if ((elementType & DataTypes.Nullable) == DataTypes.Nullable)
                        type = Nullable.GetUnderlyingType(type);
                    WriteType(bw, type);
                    return;
                }

                // Non-abstract array: recursion for element type, then writing rank
                if (type.IsArray)
                {
                    WriteTypeNamesAndRanks(bw, type.GetElementType());
                    byte rank = (byte)type.GetArrayRank();

                    // 0-based generic array is differentiated from nonzero-based 1D array (matters if no instance is created so bounds are not queried)
                    if (rank == 1 && type.IsImplementationOfGenericType(Reflector.IListGenType))
                        rank = 0;
                    bw.Write(rank);
                    return;
                }

                // recursion for generic arguments
                if (type.IsGenericType)
                {
                    foreach (Type genericArgument in type.GetGenericArguments())
                        WriteTypeNamesAndRanks(bw, genericArgument);
                }
            }

            [SecurityCritical]
            private CircularList<DataTypes> EncodeCollectionType(Type type)
            {
                // array
                if (type.IsArray)
                    return EncodeArray(type);

                Debug.Assert(!type.IsGenericTypeDefinition, $"Generic type definition is not expected in {nameof(EncodeCollectionType)}");
                DataTypes collectionType = GetSupportedCollectionType(type);
                type = Nullable.GetUnderlyingType(type) ?? type;

                // generic type
                if (type.IsGenericType)
                    return EncodeGenericCollection(type, collectionType);

                // non-generic types
                switch (collectionType)
                {
                    case DataTypes.ArrayList:
                    case DataTypes.QueueNonGeneric:
                    case DataTypes.StackNonGeneric:
                        return new CircularList<DataTypes> { collectionType | DataTypes.Object };

                    case DataTypes.Hashtable:
                    case DataTypes.SortedListNonGeneric:
                    case DataTypes.ListDictionary:
                    case DataTypes.HybridDictionary:
                    case DataTypes.OrderedDictionary:
                    case DataTypes.DictionaryEntry:
                    case DataTypes.DictionaryEntryNullable:
                        return new CircularList<DataTypes> { collectionType | DataTypes.Object, DataTypes.Object };

                    case DataTypes.StringCollection:
                        return new CircularList<DataTypes> { collectionType | DataTypes.String };

                    case DataTypes.StringDictionary:
                        return new CircularList<DataTypes> { collectionType | DataTypes.String, DataTypes.String };
                    default:
                        // should never occur, throwing internal error without resource
                        throw new InvalidOperationException("Element type of non-generic collection is not defined: " + DataTypeToString(collectionType));
                }
            }

            [SecurityCritical]
            private CircularList<DataTypes> EncodeArray(Type type)
            {
                Type elementType = type.GetElementType();
                if (TryUseSurrogateSelectorForAnyType && CanUseSurrogate(elementType))
                {
                    DataTypes result = DataTypes.Array | DataTypes.RecursiveObjectGraph;
                    if (elementType.IsNullable())
                        result |= DataTypes.Nullable;
                    return new CircularList<DataTypes> { result };
                }

                DataTypes elementDataType = GetSupportedElementType(elementType);
                if (elementDataType != DataTypes.Null)
                    return new CircularList<DataTypes> { DataTypes.Array | elementDataType };

                if (IsSupportedCollection(elementType))
                {
                    CircularList<DataTypes> result = EncodeCollectionType(elementType);
                    if (result != null)
                    {
                        result.AddFirst(DataTypes.Array);
                        return result;
                    }
                }

                // Arrays always require a special handling and cannot be serialized recursively. For unsupported types we encode the array type
                // and if recursive serialization is not supported it will turn out when the non-null elements are serialized (if any)
                return new CircularList<DataTypes> { DataTypes.Array | DataTypes.RecursiveObjectGraph | (elementType.IsNullable() ? DataTypes.Nullable : DataTypes.Null) };
            }

            [SecurityCritical]
            private CircularList<DataTypes> EncodeGenericCollection(Type type, DataTypes collectionType)
            {
                if (collectionType == DataTypes.Null)
                    return null;

                Debug.Assert(!type.ContainsGenericParameters, $"Constructed open generic types are not expected in {nameof(EncodeGenericCollection)}");
                Type[] args = type.GetGenericArguments();
                Type elementType = args[0];
                DataTypes elementDataType = GetSupportedElementType(elementType);

                // generics with 1 argument
                if (args.Length == 1)
                {
                    if (elementDataType != DataTypes.Null)
                        return new CircularList<DataTypes> { collectionType | elementDataType };

                    if (IsSupportedCollection(elementType))
                    {
                        CircularList<DataTypes> innerType = EncodeCollectionType(elementType);
                        if (innerType != null)
                        {
                            innerType.AddFirst(collectionType);
                            return innerType;
                        }
                    }

                    return null;
                }

                // dictionaries
                Type valueType = args[1];
                DataTypes valueDataType = GetSupportedElementType(valueType);

                CircularList<DataTypes> keyTypes;
                CircularList<DataTypes> valueTypes;

                // key
                if (elementDataType != DataTypes.Null)
                    keyTypes = new CircularList<DataTypes> { collectionType | elementDataType };
                else if (IsSupportedCollection(elementType))
                {
                    keyTypes = EncodeCollectionType(elementType);
                    if (keyTypes == null)
                        return null;
                    keyTypes.AddFirst(collectionType);
                }
                else
                    return null;

                // value
                if (valueDataType != DataTypes.Null)
                    valueTypes = new CircularList<DataTypes> { valueDataType };
                else if (IsSupportedCollection(valueType))
                {
                    valueTypes = EncodeCollectionType(valueType);
                    if (valueTypes == null)
                        return null;
                }
                else
                    return null;

                keyTypes.AddRange(valueTypes);
                return keyTypes;
            }

            /// <summary>
            /// Gets the <see cref="DataTypes"/> representation of <paramref name="type"/> as an element type.
            /// </summary>
            [SecurityCritical]
            private DataTypes GetSupportedElementType(Type type)
            {
                DataTypes elementType;

                // a.) nullable (must be before surrogate-support checks)
                if (type.IsNullable())
                {
                    elementType = GetSupportedElementType(type.GetGenericArguments()[0]);
                    if (elementType == DataTypes.Null)
                        return elementType;
                    return DataTypes.Nullable | elementType;
                }

                // b.) Natively supported primitive types
                if (primitiveTypes.TryGetValue(type, out elementType))
                    return elementType;

                // c.) recursion for any type: check even for sub-collections
                if (ForceRecursiveSerializationOfSupportedTypes && !type.IsArray || TryUseSurrogateSelectorForAnyType && CanUseSurrogate(type))
                    return supportedNonPrimitiveElementTypes.GetValueOrDefault(type, DataTypes.RecursiveObjectGraph);

                // e.) Natively supported non-primitive types
                if (supportedNonPrimitiveElementTypes.TryGetValue(type, out elementType))
                    return elementType;

                // d.) enum
                if (type.IsEnum)
                    return DataTypes.Enum | GetSupportedElementType(Enum.GetUnderlyingType(type));

                // Shortcut: If type is a collection, then returning null here
                if (GetSupportedCollectionType(type) != DataTypes.Null)
                    return DataTypes.Null;

                // e.) Other non-pure types

                // RuntimeType
                if (type == Reflector.RuntimeType)
                    return DataTypes.RuntimeType;

                // IBinarySerializable implementation
                if (!IgnoreIBinarySerializable && typeof(IBinarySerializable).IsAssignableFrom(type))
                    return DataTypes.BinarySerializable;

                // Any struct if can be serialized
                if (CompactSerializationOfStructures && type.IsValueType && BinarySerializer.CanSerializeValueType(type, false))
                    return DataTypes.RawStruct;

                // Recursive serialization
                if (RecursiveSerializationAsFallback || type.IsInterface || type.IsSerializable || CanUseSurrogate(type))
                    return DataTypes.RecursiveObjectGraph;

#pragma warning disable 618, 612
                // Any struct (obsolete but still supported as backward compatibility)
                if (ForcedSerializationValueTypesAsFallback && type.IsValueType)
                    return DataTypes.RawStruct;
#pragma warning restore 618, 612

                // It is alright for a collection element type. If no recursive serialization is allowed it will turn out for the items.
                return DataTypes.RecursiveObjectGraph;
            }

            [SecurityCritical]
            private void WriteEnum(BinaryWriter bw, object enumObject)
            {
                Type type = enumObject.GetType();
                DataTypes dataType = primitiveTypes.GetValueOrDefault(Enum.GetUnderlyingType(type));

                ulong enumValue = dataType == DataTypes.UInt64 ? (ulong)enumObject : (ulong)((IConvertible)enumObject).ToInt64(null);

                bool is7Bit = false;
                int size = 1;
                switch (dataType)
                {
                    case DataTypes.Int8:
                    case DataTypes.UInt8:
                        break;
                    case DataTypes.Int16:
                    case DataTypes.UInt16:
                        size = 2;
                        is7Bit = enumValue < (1UL << 7);
                        break;
                    case DataTypes.Int32:
                    case DataTypes.UInt32:
                        size = 4;
                        is7Bit = enumValue < (1UL << 21);
                        break;
                    case DataTypes.Int64:
                    case DataTypes.UInt64:
                        size = 8;
                        is7Bit = enumValue < (1UL << 49);
                        break;
                    default:
                        // should never occur, throwing internal error without resource
                        throw new ArgumentOutOfRangeException(nameof(enumObject));
                }

                dataType |= DataTypes.Enum;
                if (is7Bit)
                    dataType |= DataTypes.Store7BitEncoded;

                WriteDataType(bw, dataType);
                WriteType(bw, type);
                if (is7Bit)
                    Write7BitLong(bw, enumValue);
                else
                    bw.Write(BitConverter.GetBytes(enumValue), 0, size);
            }

            [SecurityCritical]
            private bool TryWriteCollection(BinaryWriter bw, object data, bool isRoot)
            {
                bool CanHaveRecursion(CircularList<DataTypes> dataTypes)
                    => dataTypes.Exists(dt =>
                        (dt & DataTypes.SimpleTypes) == DataTypes.BinarySerializable
                        || (dt & DataTypes.SimpleTypes) == DataTypes.RecursiveObjectGraph
                        || (dt & DataTypes.SimpleTypes) == DataTypes.Object);

                Type type = data.GetType();

                if (!IsSupportedCollection(type))
                    return false;

                CircularList<DataTypes> collectionType = EncodeCollectionType(type);
                if (collectionType == null)
                    return false;

                foreach (DataTypes dataType in collectionType)
                    WriteDataType(bw, dataType);

                if (isRoot && CanHaveRecursion(collectionType))
                {
                    if (WriteId(bw, data))
                        Debug.Fail("Id of recursive object should be unknown on top level.");
                }

                WriteTypeNamesAndRanks(bw, type);
                WriteCollection(bw, collectionType, data);
                return true;
            }

            [SecurityCritical]
            private void WriteCollection(BinaryWriter bw, CircularList<DataTypes> collectionTypeDescriptor, object obj)
            {
                if (collectionTypeDescriptor.Count == 0)
                    // should never occur, throwing internal error without resource
                    throw new ArgumentException("Type description is invalid", nameof(collectionTypeDescriptor));

                DataTypes collectionDataType = collectionTypeDescriptor[0];
                DataTypes elementDataType = collectionDataType & ~(DataTypes.CollectionTypes | DataTypes.Enum);

                // array
                if ((collectionDataType & DataTypes.CollectionTypes) == DataTypes.Array)
                {
                    Array array = (Array)obj;
                    // 1. Dimensions
                    for (int i = 0; i < array.Rank; i++)
                    {
                        Write7BitInt(bw, array.GetLowerBound(i));
                        Write7BitInt(bw, array.GetLength(i));
                    }

                    // 2. Write elements
                    Type elementType = array.GetType().GetElementType();
                    // 2.a.) Primitive array
                    // ReSharper disable once PossibleNullReferenceException - it is an array
                    if (elementType.IsPrimitive)
                    {
                        if (!(array is byte[] rawData))
                        {
                            rawData = new byte[Buffer.ByteLength(array)];
                            Buffer.BlockCopy(array, 0, rawData, 0, rawData.Length);
                        }

                        bw.Write(rawData);
                        return;
                    }

                    // 2.b.) Complex array
                    collectionTypeDescriptor.RemoveFirst();
                    WriteCollectionElements(bw, array, collectionTypeDescriptor, elementDataType, elementType);
                    return;
                }

                // other collections
                CollectionSerializationInfo serInfo = serializationInfo[collectionDataType & DataTypes.CollectionTypes];
                var enumerable = obj as IEnumerable;
                IEnumerable collection = enumerable ?? new object[] { obj };
                // as object[] for DictionaryEntry and KeyValuePair

                // 1. Write specific properties
                serInfo.WriteSpecificProperties(bw, collection, this);

                // 2. Stack: reversing elements
                if (serInfo.ReverseElements)
                    collection = collection.Cast<object>().Reverse();

                // 3. Write elements
                // 3.a.) generic collection with single argument
                if (serInfo.IsGenericCollection)
                {
                    Type elementType = collection.GetType().GetGenericArguments()[0];
                    collectionTypeDescriptor.RemoveFirst();
                    WriteCollectionElements(bw, collection, collectionTypeDescriptor, elementDataType, elementType);
                    return;
                }

                // 3.b.) generic dictionary
                if (serInfo.IsGenericDictionary)
                {
                    Type[] argTypes = (enumerable ?? ((object[])collection)[0]).GetType().GetGenericArguments();
                    Type keyType = argTypes[0];
                    Type valueType = argTypes[1];

                    IList<DataTypes> valueCollectionDataTypes = GetDictionaryValueTypes(collectionTypeDescriptor);
                    collectionTypeDescriptor.RemoveFirst();
                    DataTypes valueDataType = DataTypes.Null;
                    if ((valueCollectionDataTypes[0] & DataTypes.CollectionTypes) == DataTypes.Null)
                        valueDataType = valueCollectionDataTypes[0] & ~DataTypes.Enum;
                    WriteDictionaryElements(bw, collection, collectionTypeDescriptor, elementDataType, valueCollectionDataTypes, valueDataType, keyType, valueType);
                    return;
                }

                // 3.c.) non-generic collection
                if (serInfo.IsNonGenericCollection)
                {
                    WriteCollectionElements(bw, collection, null, elementDataType, null);
                    return;
                }

                // 3.d.) non-generic dictionary
                if (serInfo.IsNonGenericDictionary)
                {
                    DataTypes valueDataType = GetDictionaryValueTypes(collectionTypeDescriptor)[0];
                    WriteDictionaryElements(bw, collection, null, elementDataType, null, valueDataType, null, null);
                    return;
                }

                // should never occur, throwing internal error without resource
                throw new InvalidOperationException("A supported collection expected here but other type found: " + collection.GetType());
            }

            [SecurityCritical]
            private void WriteCollectionElements(BinaryWriter bw, IEnumerable collection, IList<DataTypes> elementCollectionDataTypes, DataTypes elementDataType, Type collectionElementType)
            {
                foreach (object element in collection)
                    WriteElement(bw, element, elementCollectionDataTypes, elementDataType, collectionElementType);
            }

            [SecurityCritical]
            private void WriteDictionaryElements(BinaryWriter bw, IEnumerable collection, IList<DataTypes> keyCollectionDataTypes, DataTypes keyDataType,
                IList<DataTypes> valueCollectionDataTypes, DataTypes valueDataType, Type collectionKeyType, Type collectionValueType)
            {
                if (collection is IDictionary dictionary)
                {
                    foreach (DictionaryEntry element in dictionary)
                    {
                        WriteElement(bw, element.Key, keyCollectionDataTypes, keyDataType, collectionKeyType);
                        WriteElement(bw, element.Value, valueCollectionDataTypes, valueDataType, collectionValueType);
                    }

                    return;
                }

                // Single KeyValuePair only: cannot be cast to a non-generic dictionary, Key and Value properties must be accessed by name
                foreach (object element in collection)
                {
                    WriteElement(bw, Accessors.GetPropertyValue(element, nameof(KeyValuePair<_, _>.Key)), keyCollectionDataTypes, keyDataType, collectionKeyType);
                    WriteElement(bw, Accessors.GetPropertyValue(element, nameof(KeyValuePair<_, _>.Value)), valueCollectionDataTypes, valueDataType, collectionValueType);
                }
            }

            /// <summary>
            /// Writes a collection element
            /// </summary>
            /// <param name="bw">Binary writer</param>
            /// <param name="element">A collection element instance (can be null)</param>
            /// <param name="elementCollectionDataTypes">Data types of embedded elements. Needed in case of arrays and generic collections where embedded types are handled.</param>
            /// <param name="elementDataType">A base data type that is valid for all elements in the collection. <see cref="DataTypes.Null"/> means that element is a nested collection.</param>
            /// <param name="collectionElementType">Needed if <paramref name="elementDataType"/> is <see cref="DataTypes.BinarySerializable"/> or <see cref="DataTypes.RecursiveObjectGraph"/>.
            /// Contains the actual generic type parameter or array base type from which <see cref="IBinarySerializable"/> or the type of the recursively serialized object is assignable.</param>
            [SecurityCritical]
            [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity", Justification = "Simple switch with many cases")]
            private void WriteElement(BinaryWriter bw, object element, IEnumerable<DataTypes> elementCollectionDataTypes, DataTypes elementDataType, Type collectionElementType)
            {
                switch (elementDataType)
                {
                    case DataTypes.Null:
                        // Null element type means that element is a nested collection type: recursion.
                        // Writing id except for value types (KeyValuePair, DictionaryEntry) - for nullables IsNotNull was written in default
                        if (!collectionElementType.IsValueType || collectionElementType.IsNullable())
                        {
                            if (WriteId(bw, element))
                                break;
                            Debug.Assert(element != null, "When element is null, WriteId should return true");
                        }

                        // creating a new copy for this call be cause the processed elements will be consumed
                        WriteCollection(bw, new CircularList<DataTypes>(elementCollectionDataTypes), element);
                        break;
                    case DataTypes.Bool:
                        bw.Write((bool)element);
                        break;
                    case DataTypes.Int8:
                        bw.Write((sbyte)element);
                        break;
                    case DataTypes.UInt8:
                        bw.Write((byte)element);
                        break;
                    case DataTypes.Int16:
                        bw.Write((short)element);
                        break;
                    case DataTypes.UInt16:
                        bw.Write((ushort)element);
                        break;
                    case DataTypes.Int32:
                        bw.Write((int)element);
                        break;
                    case DataTypes.UInt32:
                        bw.Write((uint)element);
                        break;
                    case DataTypes.Int64:
                        bw.Write((long)element);
                        break;
                    case DataTypes.UInt64:
                        bw.Write((ulong)element);
                        break;
                    case DataTypes.Char:
                        bw.Write((ushort)(char)element);
                        break;
                    case DataTypes.String:
                        if (WriteId(bw, element))
                            break;
                        Debug.Assert(element != null, "When element is null, WriteId should return true");
                        bw.Write((string)element);
                        break;
                    case DataTypes.Single:
                        bw.Write((float)element);
                        break;
                    case DataTypes.Double:
                        bw.Write((double)element);
                        break;
                    case DataTypes.Decimal:
                        bw.Write((decimal)element);
                        break;
                    case DataTypes.DateTime:
                        WriteDateTime(bw, (DateTime)element);
                        break;
                    case DataTypes.DBNull:
                        // as a collection element DBNull can be also a null reference, hence writing the id.
                        WriteId(bw, element);
                        break;
                    case DataTypes.IntPtr:
                        bw.Write(((IntPtr)element).ToInt64());
                        break;
                    case DataTypes.UIntPtr:
                        bw.Write(((UIntPtr)element).ToUInt64());
                        break;
                    case DataTypes.Version:
                        if (WriteId(bw, element))
                            break;
                        WriteVersion(bw, (Version)element);
                        break;
                    case DataTypes.Guid:
                        bw.Write(((Guid)element).ToByteArray());
                        break;
                    case DataTypes.TimeSpan:
                        bw.Write(((TimeSpan)element).Ticks);
                        break;
                    case DataTypes.DateTimeOffset:
                        WriteDateTimeOffset(bw, (DateTimeOffset)element);
                        break;
                    case DataTypes.Uri:
                        if (WriteId(bw, element))
                            break;
                        Debug.Assert(element != null, "When element is null, WriteId should return true");
                        WriteUri(bw, (Uri)element);
                        break;
                    case DataTypes.BitArray:
                        if (WriteId(bw, element))
                            break;
                        Debug.Assert(element != null, "When element is null, WriteId should return true");
                        WriteBitArray(bw, (BitArray)element);
                        break;
                    case DataTypes.BitVector32:
                        bw.Write(((BitVector32)element).Data);
                        break;
                    case DataTypes.BitVector32Section:
                        WriteSection(bw, (BitVector32.Section)element);
                        break;
                    case DataTypes.StringBuilder:
                        if (WriteId(bw, element))
                            break;
                        Debug.Assert(element != null, "When element is null, WriteId should return true");
                        WriteStringBuilder(bw, (StringBuilder)element);
                        break;
                    case DataTypes.RuntimeType:
                        if (WriteId(bw, element))
                            break;
                        Debug.Assert(element != null, "When element is null, WriteId should return true");
                        WriteType(bw, (Type)element, true);
                        break;

                    case DataTypes.BinarySerializable:
                        // 1. instance id for classes or when element is defined as interface in the collection (for nullables IsNotNull was already written in default case)
                        if ((!collectionElementType.IsValueType) && WriteId(bw, element))
                            break;

                        Debug.Assert(element != null, "When element is null, WriteId should return true");
                        Type elementType = element.GetType();

                        // 2. Serialize (1: qualify -> is element type, 2: different type -> store type, 3: serialize)
                        bool qualifyAllElements = collectionElementType.CanBeDerived();
                        bool typeNeeded = qualifyAllElements && elementType != collectionElementType;

                        // is type the same as collection element type
                        if (qualifyAllElements)
                            bw.Write(!typeNeeded);

                        if (typeNeeded)
                            WriteType(bw, elementType);
                        WriteBinarySerializable(bw, (IBinarySerializable)element);
                        break;
                    case DataTypes.RecursiveObjectGraph:
                        // When element types may differ, writing element with data type. This prevents the following errors:
                        // - Writing array element as a graph - new IList<int>[] { new int[] {1} }
                        // - Writing primitive/enum/other supported element as a graph - new ValueType[] { 1, ConsoleColor.Black }
                        // - Writing compressible struct or IBinarySerializable as a graph - new IAnything[] { new BinarySerializable(), new MyStruct() }
                        if (collectionElementType.CanBeDerived())
                        {
                            Write(bw, element, false);
                            break;
                        }

                        // 1. instance id for classes or when element is defined as interface in the collection (for nullables IsNotNull was already written in default case)
                        if (!collectionElementType.IsValueType && WriteId(bw, element))
                            break;

                        Debug.Assert(element != null, "When element is null, WriteId should return true");

                        // 2. Serialize
                        WriteObjectGraph(bw, element, collectionElementType);
                        break;
                    case DataTypes.RawStruct:
                        WriteValueType(bw, element);
                        break;
                    case DataTypes.Object:
                        Write(bw, element, false);
                        break;
                    default:
                        if ((elementDataType & DataTypes.Nullable) == DataTypes.Nullable)
                        {
                            // When boxed, nullable elements are either a null reference or a non-nullable instance in the object.
                            // Here writing IsNotNull instead of id; otherwise, nullables would get an id while non-nullables would not.
                            bw.Write(element != null);
                            if (element != null)
                                WriteElement(bw, element, elementCollectionDataTypes, elementDataType & ~DataTypes.Nullable, collectionElementType);
                            break;
                        }

                        // should never occur, throwing internal error without resource
                        throw new InvalidOperationException("Can not serialize elementType " + DataTypeToString(elementDataType));
                }
            }

            [SecurityCritical]
            private void WriteRecursively(BinaryWriter bw, object data, bool isRoot)
            {
                Debug.Assert(!(data is Array), "Arrays cannot be serialized as an object graph.");
                WriteDataType(bw, DataTypes.RecursiveObjectGraph);

                if (isRoot)
                {
                    // on root level writing the id even if the object is value type because the boxed reference can be shared
                    if (WriteId(bw, data))
                    {
                        Debug.Fail("Id of recursive object should be unknown on top level.");
                        return;
                    }
                }

                WriteObjectGraph(bw, data, null);
            }

            /// <summary>
            /// Serializes an object graph.
            /// </summary>
            /// <param name="bw">Writer</param>
            /// <param name="data">The object to serialize</param>
            /// <param name="collectionElementType">Element type of collection or null if not in collection</param>
            [SecurityCritical]
            private void WriteObjectGraph(BinaryWriter bw, object data, Type collectionElementType)
            {
                // Common order: 1: not in a collection -> store type, 2: serialize
                OnSerializing(data);

                Type type = data.GetType();
                if (TryGetSurrogate(type, out ISerializationSurrogate surrogate, out var _) || (!IgnoreISerializable && data is ISerializable))
                    WriteCustomObjectGraph(bw, data, collectionElementType, surrogate);
                else
                {
                    // type
                    if (collectionElementType == null)
                    {
                        if (RecursiveSerializationAsFallback || type.IsSerializable || (ForceRecursiveSerializationOfSupportedTypes && supportedNonPrimitiveElementTypes.ContainsKey(type)))
                            WriteType(bw, type);
                        else
                            ThrowNotSupported(type);
                    }

                    WriteDefaultObjectGraph(bw, data);
                }

                OnSerialized(data);
            }

            [SecurityCritical]
            private void WriteDefaultObjectGraph(BinaryWriter bw, object data)
            {
                // true for IsDefault object graph
                bw.Write(true);
                Type type = data.GetType();
                Debug.Assert(!type.IsArray, "Array cannot be serialized as object graph");

                // iterating through self and base types
                // ReSharper disable once PossibleNullReferenceException - data is an object in all cases
                for (Type t = type; t != Reflector.ObjectType; t = t.BaseType)
                {
                    // writing fields of current level
                    FieldInfo[] fields = BinarySerializer.GetSerializableFields(t);

                    if (fields.Length != 0 || t == type)
                    {
                        // ReSharper disable once PossibleNullReferenceException - type is never null
                        // writing name of base type
                        if (t != type)
                            bw.Write(t.Name);

                        // writing the fields
                        Write7BitInt(bw, fields.Length);
                        foreach (FieldInfo field in fields)
                        {
                            bw.Write(field.Name);
                            Type fieldType = field.FieldType;
                            object fieldValue = FieldAccessor.GetAccessor(field).Get(data);
                            if (fieldValue != null && fieldType.IsEnum)
                                fieldValue = Convert.ChangeType(fieldValue, Enum.GetUnderlyingType(fieldType), CultureInfo.InvariantCulture);
                            Write(bw, fieldValue, false);
                        }
                    }
                }

                // marking end of hierarchy
                bw.Write(String.Empty);
            }

            [SecurityCritical]
            private void WriteCustomObjectGraph(BinaryWriter bw, object data, Type collectionElementType, ISerializationSurrogate surrogate)
            {
                // Common order: 1: not in a collection -> store type, 2: serialize

                Type type = data.GetType();
                SerializationInfo si = new SerializationInfo(type, new FormatterConverter());

                if (surrogate != null)
                    surrogate.GetObjectData(data, si, Context);
                else
                {
                    if (!RecursiveSerializationAsFallback && !type.IsSerializable)
                        ThrowNotSupported(type);
                    ((ISerializable)data).GetObjectData(si, Context);
                }

                bool typeChanged = si.AssemblyName != type.Assembly.FullName || si.FullTypeName != type.FullName;
                if (typeChanged)
                    type = Type.GetType(si.FullTypeName + ", " + si.AssemblyName);

                // 1. type if needed
                if (collectionElementType == null)
                    WriteType(bw, type);

                // 2. Serialization part.
                // a.) writing false for not default object graph method
                bw.Write(false);

                // b.) Here we can sign if type has changed while element types are the same in a collection (sealed class or struct element type)
                if (collectionElementType != null)
                {
                    bw.Write(typeChanged);
                    if (typeChanged)
                        WriteType(bw, type);
                }

                // c.) writing members
                Write7BitInt(bw, si.MemberCount);
                foreach (SerializationEntry entry in si)
                {
                    // name
                    bw.Write(entry.Name);

                    // value
                    Write(bw, entry.Value, false);

                    // type
                    bool typeMatch = entry.Value == null && entry.ObjectType == Reflector.ObjectType
                        || entry.Value != null && entry.Value.GetType() == entry.ObjectType;
                    bw.Write(typeMatch);
                    if (!typeMatch)
                        WriteType(bw, entry.ObjectType);
                }
            }

            private void OnSerializing(object obj) => ExecuteMethodsOfAttribute(obj, typeof(OnSerializingAttribute));

            private void OnSerialized(object obj) => ExecuteMethodsOfAttribute(obj, typeof(OnSerializedAttribute));

            /// <summary>
            /// Writes a type into the serialization stream.
            /// <paramref name="allowOpenTypes"/> can be <see langword="true"/> only when a RuntimeType instance is serialized.
            /// </summary>
            [SecurityCritical]
            private void WriteType(BinaryWriter bw, Type type, bool allowOpenTypes = false)
            {
                Debug.Assert(allowOpenTypes || (!type.IsGenericTypeDefinition && !type.IsGenericParameter), $"Generic type definitions and generic parameters are allowed only when {nameof(allowOpenTypes)} is true.");

                GetBoundNames(type, out string binderAsmName, out string binderTypeName);
                if (binderTypeName == null && binderAsmName == null && TryWriteByDataTypes(bw, type, allowOpenTypes))
                    return;

                int index = GetAssemblyIndex(type, binderAsmName);

                // known assembly
                if (index != -1)
                    Write7BitInt(bw, index);
                // new assembly
                else
                {
                    // storing assembly and type name together and return
                    if (OmitAssemblyQualifiedNames)
                        Write7BitInt(bw, OmitAssemblyIndex);
                    else
                    {
                        Write7BitInt(bw, NewAssemblyIndex);
                        WriteNewAssembly(bw, type, binderAsmName);
                        WriteNewType(bw, type, false, allowOpenTypes, binderAsmName, binderTypeName);
                        return;
                    }
                }

                index = GetTypeIndex(type, binderAsmName, binderTypeName);

                // known type
                if (index != -1)
                {
                    Write7BitInt(bw, index);
                    if (allowOpenTypes && type.IsGenericTypeDefinition)
                        WriteGenericSpecifier(bw, type);
                    return;
                }

                // new type
                WriteNewType(bw, type, true, allowOpenTypes, binderAsmName, binderTypeName);
            }

            private void GetBoundNames(Type type, out string binderAsmName, out string binderTypeName)
            {
                binderAsmName = null;
                binderTypeName = null;
#if NET35
                return;
#else
                if (Binder == null || type.FullName == null)
                    return;

                if (binderCache == null)
                    binderCache = new Dictionary<Type, (string, string)>();

                if (binderCache.TryGetValue(type, out (string AssemblyName, string TypeName) result))
                {
                    binderAsmName = result.AssemblyName;
                    binderTypeName = result.TypeName;
                    return;
                }

                Binder.BindToName(type, out binderAsmName, out binderTypeName);
                binderCache.Add(type, (binderAsmName, binderTypeName));
#endif
            }

            private int GetAssemblyIndex(Type type, string binderAsmName)
            { 
#if !NET35
                if (binderAsmName != null)
                    return AssemblyNameIndexCache.GetValueOrDefault(binderAsmName, -1);
#endif
                return AssemblyIndexCache.GetValueOrDefault(type.Assembly, -1);
            }

            private int GetTypeIndex(Type type, string binderAsmName, string binderTypeName)
            {
#if !NET35
                if (Binder != null)
                    return TypeNameIndexCache.GetValueOrDefault(GetTypeNameIndexCacheKey(type, binderAsmName, binderTypeName), -1);
#endif

                return TypeIndexCache.GetValueOrDefault(type, -1);
            }

            /// <summary>
            /// Trying to write type completely or partially by pure <see cref="DataTypes"/>.
            /// Returning <see langword="true"/> even for partial success (array, generics) because then the beginning of the type is encoded by DataTypes.
            /// </summary>
            [SecurityCritical]
            private bool TryWriteByDataTypes(BinaryWriter bw, Type type, bool allowOpenTypes)
            {
                Debug.Assert(allowOpenTypes || (!type.IsGenericTypeDefinition && !type.IsGenericParameter), $"Generic type definitions and generic parameters are allowed only when {nameof(allowOpenTypes)} is true.");

                DataTypes elementType = GetSupportedElementType(type);
                if (elementType != DataTypes.Null)
                {
                    // No DataTypes encoding
                    if (!IsPureType(elementType))
                        return false;
                    Write7BitInt(bw, InvariantAssemblyIndex);
                    WriteDataType(bw, elementType);
                    return true;
                }

                bool isGeneric = type.IsGenericType;
                bool isTypeDef = type.IsGenericTypeDefinition;
                bool isGenericParam = type.IsGenericParameter;

                Type typeDef = isTypeDef ? type
                    : isGeneric ? type.GetGenericTypeDefinition()
                    : isGenericParam ? type.DeclaringType
                    : null;

                // this still returns the same for generics and their type definition
                DataTypes collectionType = GetSupportedCollectionType(typeDef ?? type);
                if (collectionType == DataTypes.Null)
                    return false;

                // Arrays or non-generic/closed generic collections
                if (!(isTypeDef || isGenericParam || (isGeneric && type.ContainsGenericParameters)))
                {
                    CircularList<DataTypes> encodedCollectionType = EncodeCollectionType(type);
                    Write7BitInt(bw, InvariantAssemblyIndex);
                    encodedCollectionType.ForEach(dt => WriteDataType(bw, dt));
                    WriteTypeNamesAndRanks(bw, type);
                    return true;
                }

                Debug.Assert(typeDef != null, "Generics are expected at this point");
                Write7BitInt(bw, InvariantAssemblyIndex);

                // Here we have a supported generic type definition or a constructed generic type with unsupported or impure arguments.
                WriteDataType(bw, collectionType | DataTypes.GenericTypeDefinition); // note: no multiple DataTypes even for dictionaries!

                // If open types are allowed in current context we write a specifier after the generic type definition
                if (allowOpenTypes)
                {
                    WriteGenericSpecifier(bw, type);
                    if (isTypeDef || isGenericParam)
                        return true;
                }

                // Constructed generic type of the (partially) unsupported or impure arguments:
                // recursion for the arguments and adding the type to the index cache at the end.
                foreach (Type genericArgument in type.GetGenericArguments())
                    WriteType(bw, genericArgument, allowOpenTypes);

                return true;
            }

            private void WriteNewAssembly(BinaryWriter bw, Type type, string binderAsmName)
            {
#if !NET35
                // by binder
                if (binderAsmName != null)
                {
                    bw.Write(binderAsmName);
                    AssemblyNameIndexCache.Add(binderAsmName, AssemblyIndexCacheCount);
                    return;
                }
#endif

                bw.Write(type.Assembly.FullName);
                AssemblyIndexCache.Add(type.Assembly, AssemblyIndexCacheCount);
            }

            /// <summary>
            /// Writes a new non-pure type if a binder did not handle it. Assembly part is already written.
            /// If open types are allowed a generic type definition is followed by a specifier; otherwise, by type arguments.
            /// </summary>
            private void WriteNewType(BinaryWriter bw, Type type, bool knownAssembly, bool allowOpenTypes, string binderAsmName, string binderTypeName)
            {
#if !NET35
                // by binder
                if (binderTypeName != null)
                {
                    // For known assemblies a type index is requested first.
                    if (knownAssembly)
                        Write7BitInt(bw, NewTypeIndex);
                    bw.Write(binderTypeName);
                    if (allowOpenTypes && type.IsGenericTypeDefinition)
                        WriteGenericSpecifier(bw, type);
                    AddToTypeCache(type, binderAsmName, binderTypeName);
                    return;
                } 
#endif

                bool isGeneric = type.IsGenericType;
                bool isTypeDef = type.IsGenericTypeDefinition;
                bool isGenericParam = type.IsGenericParameter;
                bool typeDefWritten = false;

                Debug.Assert(allowOpenTypes || !isTypeDef && !isGenericParam, $"Unexpected type when open types are not allowed: {type}");

                Type typeDef = isTypeDef ? type
                    : isGeneric ? type.GetGenericTypeDefinition()
                    : isGenericParam ? type.DeclaringType
                    : null;

                // For known assemblies a type index is requested first.
                if (knownAssembly)
                {
                    // It can happen that the generic type definition is already known.
                    int index;
                    if (typeDef != null && (index = GetTypeIndex(typeDef, binderAsmName, null)) != -1)
                    {
                        Write7BitInt(bw, index);
                        typeDefWritten = true;
                    }
                    else
                        Write7BitInt(bw, NewTypeIndex);
                }

                // Regular type name
                if (typeDef == null)
                {
                    // ReSharper disable once AssignNullToNotNullAttribute - cannot be null for a non-generic runtime type
                    bw.Write(type.FullName);
                    AddToTypeCache(type, binderAsmName, null);
                    return;
                }

                // Generic type definition name
                if (!typeDefWritten)
                {
                    // ReSharper disable once AssignNullToNotNullAttribute - cannot be null for a type definition
                    bw.Write(typeDef.FullName);
                    AddToTypeCache(typeDef, binderAsmName, null);
                }

                // If open types are allowed in current context we write a specifier after the generic type definition
                if (allowOpenTypes)
                {
                    WriteGenericSpecifier(bw, type);
                    if (isTypeDef || isGenericParam)
                        return;
                }

                // Constructed generic type: arguments (it still can contain generic parameters)
                foreach (Type genericArgument in type.GetGenericArguments())
                    WriteType(bw, genericArgument, allowOpenTypes);
                AddToTypeCache(type, binderAsmName, null);
            }

            private void AddToTypeCache(Type type, string binderAsmName, string binderTypeName)
            {
#if !NET35
                // Even if current binder names are null we must use the string based cache if there is a binder
                // to avoid possibly conflicting type names between the custom and default binding and among binder type names.
                if (Binder != null)
                {
                    TypeNameIndexCache.Add(GetTypeNameIndexCacheKey(type, binderAsmName, binderTypeName), TypeIndexCacheCount);
                    return;
                }
#endif

                TypeIndexCache.Add(type, TypeIndexCacheCount);
            }

            private void WriteGenericSpecifier(BinaryWriter bw, Type type)
            {
                if (type.IsGenericTypeDefinition)
                {
                    bw.Write((byte)GenericTypeSpecifier.TypeDefinition);
                    return;
                }

                if (type.IsGenericParameter)
                {
                    bw.Write((byte)GenericTypeSpecifier.GenericParameter);
                    Write7BitInt(bw, type.GenericParameterPosition);
                    return;
                }

                if (type.IsGenericType)
                    bw.Write((byte)GenericTypeSpecifier.ConstructedType);
            }

            /// <summary>
            /// Writes an ID and returns if it was already known.
            /// </summary>
            private bool WriteId(BinaryWriter bw, object data)
            {
                bool IsComparedByValue(Type type) =>
                    type.IsPrimitive || type.BaseType == Reflector.EnumType || // always instance so can be used than the slower IsEnum
                    type.In(Reflector.StringType, Reflector.DecimalType, Reflector.DateTimeType, Reflector.TimeSpanType, Reflector.DateTimeOffsetType, typeof(Guid));

                // null is always known.
                if (data == null)
                {
                    // actually 7-bit encoded 0
                    bw.Write((byte)0);
                    return true;
                }

                // some dedicated immutable type are compared by value
                if (IsComparedByValue(data.GetType()))
                {
                    if (idCacheByValue == null)
                        idCacheByValue = new Dictionary<object, int>();
                    else
                    {
                        if (idCacheByValue.TryGetValue(data, out int id))
                        {
                            Write7BitInt(bw, id);
                            return true;
                        }
                    }

                    idCacheByValue.Add(data, ++idCounter);
                    Write7BitInt(bw, idCounter);
                    return false;
                }

                // Others are compared by reference. Structs as well, which are boxed into a reference here.
                if (idCacheByRef == null)
                    idCacheByRef = new Dictionary<object, int>(ReferenceEqualityComparer.Comparer);
                else
                {
                    if (idCacheByRef.TryGetValue(data, out int id))
                    {
                        Write7BitInt(bw, id);
                        return true;
                    }
                }

                idCacheByRef.Add(data, ++idCounter);
                Write7BitInt(bw, idCounter);
                return false;
            }

            private void WriteBinarySerializable(BinaryWriter bw, IBinarySerializable instance)
            {
                OnSerializing(instance);
                byte[] rawData = instance.Serialize(Options);
                Write7BitInt(bw, rawData.Length);
                bw.Write(rawData);
                OnSerialized(instance);
            }

            [SecurityCritical]
            private void WriteValueType(BinaryWriter bw, object data)
            {
                OnSerializing(data);
                byte[] rawData = BinarySerializer.SerializeValueType((ValueType)data);
                Write7BitInt(bw, rawData.Length);
                bw.Write(rawData);
                OnSerialized(data);
            }

#endregion

#endregion

#endregion
        }
    }
}

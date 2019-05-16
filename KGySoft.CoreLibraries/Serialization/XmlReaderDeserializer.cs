﻿#region Copyright

///////////////////////////////////////////////////////////////////////////////
//  File: XmlReaderDeserializer.cs
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
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

using KGySoft.CoreLibraries;
using KGySoft.Reflection;
using KGySoft.Security.Cryptography;

#endregion

namespace KGySoft.Serialization
{
    /// <summary>
    /// XmlReader version of XML deserialization.
    /// Actually a static class with base types - hence marked as abstract. Unlike on serialization no fields are used so no instance is needed.
    /// </summary>
    internal abstract class XmlReaderDeserializer : XmlDeserializerBase
    {
        #region Methods

        #region Public Methods

        /// <summary>
        /// Deserializes an object using the provided <see cref="XmlReader"/> in <paramref name="reader"/> parameter.
        /// </summary>
        public static object Deserialize(XmlReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader), Res.ArgumentNull);

            ReadToNodeType(reader, XmlNodeType.Element);
            if (reader.Name != XmlSerializer.ElementObject)
                throw new ArgumentException(Res.XmlSerializationRootObjectExpected(reader.Name), nameof(reader));

            if (reader.IsEmptyElement)
                return null;

            string attrType = reader[XmlSerializer.AttributeType];
            Type objType = null;
            if (attrType != null)
            {
                objType = Reflector.ResolveType(attrType);
                if (objType == null)
                    throw new ReflectionException(Res.XmlSerializationCannotResolveType(attrType));
            }

            if (TryDeserializeObject(objType, reader, null, out var result))
                return result;

            if (attrType == null)
                throw new ArgumentException(Res.XmlSerializationRootTypeMissing, nameof(reader));
            throw new NotSupportedException(Res.XmlSerializationDeserializingTypeNotSupported(objType));
        }

        /// <summary>
        /// Deserializes an object or collection of objects.
        /// Position is before content (on parent start element). On exit position is in parent close element.
        /// </summary>
        public static void DeserializeContent(XmlReader reader, object obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            Type objType = obj.GetType();

            // deserialize IXmlSerializable content
            string attrFormat = reader[XmlSerializer.AttributeFormat];
            if (attrFormat == XmlSerializer.AttributeValueCustom)
            {
                if (!(obj is IXmlSerializable xmlSerializable))
                    throw new ArgumentException(Res.XmlSerializationNotAnIXmlSerializable(objType));
                DeserializeXmlSerializable(xmlSerializable, reader);
                return;
            }

            // deserialize array
            if (objType.IsArray)
            {
                Array array = (Array)obj;
                DeserializeArray(array, null, reader, false);
                return;
            }

            // Populatable collection: clearing it before restoring (root-level DeserializeContent, collection of read-only properties)
            Type collectionElementType = null;
            if (objType.IsCollection())
            {
                if (!objType.IsReadWriteCollection(obj))
                    throw new SerializationException(Res.XmlSerializationCannotDeserializeReadOnlyCollection(objType));

                collectionElementType = objType.GetCollectionElementType();
                IEnumerable collection = (IEnumerable)obj;
                collection.TryClear(false);
            }

            DeserializeMembersAndElements(reader, obj, objType, collectionElementType, null);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Deserializes a non-populatable collection by an initializer collection.
        /// </summary>
        private static object DeserializeContentByInitializerCollection(XmlReader reader, ConstructorInfo collectionCtor, Type collectionElementType, bool isDictionary)
        {
            IEnumerable initializerCollection = collectionElementType.CreateInitializerCollection(isDictionary);
            var members = new Dictionary<MemberInfo, object>();
            DeserializeMembersAndElements(reader, initializerCollection, collectionCtor.DeclaringType, collectionElementType, members);
            return CreateCollectionByInitializerCollection(collectionCtor, initializerCollection, members);
        }

        /// <summary>
        /// Deserializes the members and elements of <paramref name="objRealType"/>.
        /// Type of <paramref name="obj"/> can be different of <paramref name="objRealType"/> if a proxy collection object is populated for initialization.
        /// In this case members have to be stored for later initialization into <paramref name="members"/> and <paramref name="obj"/> is a populatable collection for sure.
        /// <paramref name="collectionElementType"/> is <see langword="null"/>&#160;only if <paramref name="objRealType"/> is not a supported collection.
        /// </summary>
        private static void DeserializeMembersAndElements(XmlReader reader, object obj, Type objRealType, Type collectionElementType, Dictionary<MemberInfo, object> members)
        {
            while (true)
            {
                ReadToNodeType(reader, XmlNodeType.Element, XmlNodeType.EndElement);

                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        ResolveMember(objRealType, reader.Name, reader[XmlSerializer.AttributeDeclaringType], reader[XmlSerializer.AttributeType], out PropertyInfo property, out FieldInfo field, out Type itemType);
                        MemberInfo member = (MemberInfo)property ?? field;

                        // 1.) real member
                        if (member != null)
                        {
                            object existingValue = members != null ? null : property != null ? Reflector.GetProperty(obj, property) : Reflector.GetField(obj, field);
                            if (!TryDeserializeByConverter(member, itemType, () => ReadStringValue(reader), out var result) && !TryDeserializeObject(itemType, reader, existingValue, out result))
                                throw new NotSupportedException(Res.XmlSerializationDeserializingTypeNotSupported(itemType));

                            // 1.c.) Processing result
                            HandleDeserializedMember(obj, member, result, existingValue, members);
                            continue;
                        }

                        // 2.) collection element
                        AssertCollectionItem(objRealType, collectionElementType, reader.Name);
                        IEnumerable collection = (IEnumerable)obj;
                        if (reader.IsEmptyElement)
                        {
                            collection.TryAdd(null, false);
                            continue;
                        }

                        if (TryDeserializeObject(itemType ?? collectionElementType, reader, null, out var item))
                        {
                            collection.TryAdd(item, false);
                            continue;
                        }

                        if (itemType == null)
                            throw new ArgumentException(Res.XmlSerializationCannotDetermineElementType(objRealType));
                        throw new NotSupportedException(Res.XmlSerializationDeserializingTypeNotSupported(itemType));

                    case XmlNodeType.EndElement:
                        return;
                }
            }
        }

        /// <summary>
        /// Deserialize object - XmlReader version.
        /// Reader is at open element at start and is at end element at the end.
        /// If <paramref name="existingInstance"/> is not <see langword="null"/>, then it is preferred to deserialize its content instead of returning a new instance in <paramref name="result"/>.
        /// <paramref name="existingInstance"/> is considered for IXmlSerializable, arrays, collections and recursive objects.
        /// If <paramref name="result"/> is a different instance to <paramref name="existingInstance"/>, then content if existing instance cannot be deserialized.
        /// </summary>
        [SecuritySafeCritical] // due to DeserializeValueType but is safe because serialized by safe version
        private static bool TryDeserializeObject(Type type, XmlReader reader, object existingInstance, out object result)
        {
            // null value
            if (reader.IsEmptyElement && (type == null || !type.IsValueType || type.IsNullable()))
            {
                result = null;
                return true;
            }

            if (type != null && type.IsNullable())
                type = Nullable.GetUnderlyingType(type);

            // a.) If type can be natively parsed, parsing from string
            if (type != null && type.CanBeParsedNatively())
            {
                string value = ReadStringValue(reader);
                result = value.Parse(type);
                return true;
            }

            // b.) Deserialize IXmlSerializable
            string format = reader[XmlSerializer.AttributeFormat];
            if (type != null && format == XmlSerializer.AttributeValueCustom)
            {
                object instance = existingInstance ?? (type.CanBeCreatedWithoutParameters()
                            ? Reflector.CreateInstance(type)
                            : throw new ReflectionException(Res.XmlSerializationNoDefaultCtor(type)));
                if (!(instance is IXmlSerializable xmlSerializable))
                    throw new ArgumentException(Res.XmlSerializationNotAnIXmlSerializable(type));
                DeserializeXmlSerializable(xmlSerializable, reader);
                result = xmlSerializable;
                return true;
            }

            // c.) Using type converter of the type if applicable
            if (type != null)
            {
                TypeConverter converter = TypeDescriptor.GetConverter(type);
                if (converter.CanConvertFrom(Reflector.StringType))
                {
                    result = converter.ConvertFromInvariantString(ReadStringValue(reader));
                    return true;
                }
            }

            // d.) KeyValuePair (DictionaryEntry is deserialized recursively because its properties are settable)
            if (type?.IsGenericTypeOf(Reflector.KeyValuePairType) == true)
            {
                bool keyRead = false;
                bool valueRead = false;
                object key = null;
                object value = null;
                Type keyType = null, valueType = null;

                while (true)
                {
                    ReadToNodeType(reader, XmlNodeType.Element, XmlNodeType.EndElement);
                    switch (reader.NodeType)
                    {
                        case XmlNodeType.Element:
                            switch (reader.Name)
                            {
                                case nameof(KeyValuePair<_, _>.Key):
                                    if (keyRead)
                                        throw new ArgumentException(Res.XmlSerializationMultipleKeys);

                                    keyRead = true;
                                    string attrType = reader[XmlSerializer.AttributeType];
                                    keyType = attrType != null ? Reflector.ResolveType(attrType) : type.GetGenericArguments()[0];
                                    if (!TryDeserializeObject(keyType, reader, null, out key))
                                    {
                                        if (attrType != null && keyType == null)
                                            throw new ReflectionException(Res.XmlSerializationCannotResolveType(attrType));
                                        throw new NotSupportedException(Res.XmlSerializationDeserializingTypeNotSupported(keyType));
                                    }
                                    break;

                                case nameof(KeyValuePair<_, _>.Value):
                                    if (valueRead)
                                        throw new ArgumentException(Res.XmlSerializationMultipleValues);

                                    valueRead = true;
                                    attrType = reader[XmlSerializer.AttributeType];
                                    valueType = attrType != null ? Reflector.ResolveType(attrType) : type.GetGenericArguments()[1];
                                    if (!TryDeserializeObject(valueType, reader, null, out value))
                                    {
                                        if (attrType != null && valueType == null)
                                            throw new ReflectionException(Res.XmlSerializationCannotResolveType(attrType));
                                        throw new NotSupportedException(Res.XmlSerializationDeserializingTypeNotSupported(valueType));
                                    }
                                    break;

                                default:
                                    throw new ArgumentException(Res.XmlSerializationUnexpectedElement(reader.Name));
                            }
                            break;

                        case XmlNodeType.EndElement:
                            // end of KeyValue: checking whether both key and value have been read
                            if (!keyRead)
                                throw new ArgumentException(Res.XmlSerializationKeyValueMissingKey);
                            if (!valueRead)
                                throw new ArgumentException(Res.XmlSerializationKeyValueMissingValue);

                            var ctor = type.GetConstructor(new[] { keyType, valueType });
                            result = Reflector.CreateInstance(ctor, key, value);
                            return true;
                    }
                }
            }

            // e.) ValueType as binary
            if (type != null && format == XmlSerializer.AttributeValueStructBinary && type.IsValueType)
            {
                string attrCrc = reader[XmlSerializer.AttributeCrc];
                ReadToNodeType(reader, XmlNodeType.Text, XmlNodeType.EndElement);
                byte[] data = reader.NodeType == XmlNodeType.Text
                        ? Convert.FromBase64String(reader.Value)
                        : new byte[0];
                if (attrCrc != null)
                {
                    if (Crc32.CalculateHash(data).ToString("X8", CultureInfo.InvariantCulture) != attrCrc)
                        throw new ArgumentException(Res.XmlSerializationCrcError);
                }

                result = BinarySerializer.DeserializeValueType(type, data);
                if (data.Length > 0)
                    ReadToNodeType(reader, XmlNodeType.EndElement);
                return true;
            }

            // f.) Binary
            if (format == XmlSerializer.AttributeValueBinary)
            {
                if (reader.IsEmptyElement)
                    result = null;
                else
                {
                    string attrCrc = reader[XmlSerializer.AttributeCrc];
                    ReadToNodeType(reader, XmlNodeType.Text);
                    byte[] data = Convert.FromBase64String(reader.Value);
                    if (attrCrc != null)
                    {
                        if (Crc32.CalculateHash(data).ToString("X8", CultureInfo.InvariantCulture) != attrCrc)
                            throw new ArgumentException(Res.XmlSerializationCrcError);
                    }

                    result = BinarySerializer.Deserialize(data);
                    ReadToNodeType(reader, XmlNodeType.EndElement);
                }
                return true;
            }

            // g.) recursive deserialization (including collections)
            if (type != null && !reader.IsEmptyElement)
            {
                // g/1.) array (both existing and new)
                if (type.IsArray)
                {
                    result = DeserializeArray(existingInstance as Array, type.GetElementType(), reader, true);
                    return true;
                }

                // g/2.) existing read-write collection
                if (type.IsReadWriteCollection(existingInstance))
                {
                    DeserializeContent(reader, existingInstance);
                    result = existingInstance;
                    return true;
                }

                bool isCollection = type.IsSupportedCollectionForReflection(out var defaultCtor, out var collectionCtor, out var elementType, out bool isDictionary);

                // g/3.) New collection by collectionCtor (only if there is no defaultCtor)
                if (isCollection && defaultCtor == null && !type.IsValueType)
                {
                    result = DeserializeContentByInitializerCollection(reader, collectionCtor, elementType, isDictionary);
                    return true;
                }

                result = existingInstance ?? (type.CanBeCreatedWithoutParameters()
                        ? Reflector.CreateInstance(type)
                        : throw new ReflectionException(Res.XmlSerializationNoDefaultCtor(type)));

                // g/4.) New collection by collectionCtor again (there IS defaultCtor but the new instance is read-only so falling back to collectionCtor)
                if (isCollection && !type.IsReadWriteCollection(result))
                {
                    if (collectionCtor != null)
                    {
                        result = DeserializeContentByInitializerCollection(reader, collectionCtor, elementType, isDictionary);
                        return true;
                    }

                    throw new SerializationException(Res.XmlSerializationCannotDeserializeReadOnlyCollection(type));
                }

                // g/5.) Newly created collection or any other object (both existing and new)
                DeserializeContent(reader, result);
                return true;
            }

            result = null;
            return false;
        }

        /// <summary>
        /// Array deserialization
        /// XmlReader version. Position is before content (on parent start element). On exit position is on parent close element.
        /// Parent is not empty here.
        /// </summary>
        private static Array DeserializeArray(Array array, Type elementType, XmlReader reader, bool canRecreateArray)
        {
            if (array == null && elementType == null)
                throw new ArgumentNullException(nameof(elementType), Res.ArgumentNull);

            ParseArrayDimensions(reader[XmlSerializer.AttributeLength], reader[XmlSerializer.AttributeDim], out int[] lengths, out int[] lowerBounds);

            // checking existing array or creating a new array
            if (array == null || !CheckArray(array, lengths, lowerBounds, !canRecreateArray))
                array = Array.CreateInstance(elementType, lengths, lowerBounds);
            if (elementType == null)
                elementType = array.GetType().GetElementType();

            string attrCrc = reader[XmlSerializer.AttributeCrc];
            uint? crc = null;
            if (attrCrc != null)
            {
                if (!UInt32.TryParse(attrCrc, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result))
                    throw new ArgumentException(Res.XmlSerializationCrcHexExpected(attrCrc));
                crc = result;
            }

            int deserializedItemsCount = 0;
            ArrayIndexer arrayIndexer = lengths.Length > 1 ? new ArrayIndexer(lengths, lowerBounds) : null;
            do
            {
                ReadToNodeType(reader, XmlNodeType.Element, XmlNodeType.Text, XmlNodeType.EndElement);
                switch (reader.NodeType)
                {
                    case XmlNodeType.Text:
                        if (deserializedItemsCount > 0)
                            throw new ArgumentException(Res.XmlSerializationMixedArrayFormats);

                        // primitive array (can be restored by BlockCopy)
                        byte[] data = Convert.FromBase64String(reader.Value);
                        if (crc.HasValue)
                        {
                            if (Crc32.CalculateHash(data) != crc.Value)
                                throw new ArgumentException(Res.XmlSerializationCrcError);
                        }

                        deserializedItemsCount = data.Length / elementType.SizeOf();
                        if (array.Length != deserializedItemsCount)
                            throw new ArgumentException(Res.XmlSerializationInconsistentArrayLength(array.Length, deserializedItemsCount));
                        Buffer.BlockCopy(data, 0, array, 0, data.Length);
                        break;

                    case XmlNodeType.Element:
                        // complex array: recursive deserialization needed
                        if (reader.Name == XmlSerializer.ElementItem)
                        {
                            Type itemType = null;
                            string attrType = reader[XmlSerializer.AttributeType];
                            if (attrType != null)
                                itemType = Reflector.ResolveType(attrType);
                            if (itemType == null)
                                itemType = elementType;

                            if (TryDeserializeObject(itemType, reader, null, out var value))
                            {
                                if (arrayIndexer == null)
                                    array.SetValue(value, deserializedItemsCount + lowerBounds[0]);
                                else
                                {
                                    arrayIndexer.MoveNext();
                                    array.SetValue(value, arrayIndexer.Current);
                                }

                                deserializedItemsCount++;
                                continue;
                            }

                            throw new NotSupportedException(Res.XmlSerializationDeserializingTypeNotSupported(itemType));
                        }

                        throw new ArgumentException(Res.XmlSerializationUnexpectedElement(reader.Name));

                    case XmlNodeType.EndElement:
                        // in end element of parent: checking items count
                        if (deserializedItemsCount != array.Length)
                            throw new ArgumentException(Res.XmlSerializationInconsistentArrayLength(array.Length, deserializedItemsCount));

                        return array;
                }
            }
            while (true);
        }

        private static void DeserializeXmlSerializable(IXmlSerializable xmlSerializable, XmlReader reader)
        {
            // to XmlRoot or type name
            ReadToNodeType(reader, XmlNodeType.Element);

            // passing the reader to the object to read itself
            xmlSerializable.ReadXml(reader);

            // to end of XmlRoot or type name
            ReadToNodeType(reader, XmlNodeType.EndElement);
        }

        /// <summary>
        /// Reads a string from XmlReader.
        /// At start, reader is in container element, at the end in the end element.
        /// </summary>
        private static string ReadStringValue(XmlReader reader)
        {
            // empty: remaining in element position and returning null
            if (reader.IsEmptyElement)
                return null;

            bool escaped = reader[XmlSerializer.AttributeEscaped] == XmlSerializer.AttributeValueTrue;

            // non-empty: reading to en element and returning content
            StringBuilder result = new StringBuilder();
            do
            {
                reader.Read();
                if (reader.NodeType.In(XmlNodeType.Text, XmlNodeType.SignificantWhitespace, XmlNodeType.EntityReference, XmlNodeType.Whitespace))
                    result.Append(reader.Value);
            }
            while (reader.NodeType != XmlNodeType.EndElement);

            if (!escaped)
                return result.ToString();

            return Unescape(result.ToString());
        }

        private static void ReadToNodeType(XmlReader reader, params XmlNodeType[] nodeTypes)
        {
            do
            {
                if (!reader.Read())
                    throw new ArgumentException(Res.XmlSerializationUnexpectedEnd);

                if (reader.NodeType.In(nodeTypes))
                    return;

                if (reader.NodeType.In(XmlNodeType.Whitespace, XmlNodeType.Comment, XmlNodeType.XmlDeclaration))
                    continue;

                throw new ArgumentException(Res.XmlSerializationUnexpectedElement(Enum<XmlNodeType>.ToString(reader.NodeType)));
            }
            while (true);
        }

        #endregion

        #endregion
    }
}
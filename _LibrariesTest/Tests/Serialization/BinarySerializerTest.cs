﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Policy;
using System.Text;
using KGySoft.Collections;
using KGySoft.Libraries;
using KGySoft.Reflection;
using KGySoft.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace _LibrariesTest.Tests.Serialization
{
    /// <summary>
    /// Test of <see cref="BinarySerializer"/> class.
    /// </summary>
    [TestClass]
    public class BinarySerializerTest: TestBase
    {
        private const bool dumpDetails = false;
        private const bool dumpSerContent = false;

        #region Nested Types

        enum TestEnumSByte: sbyte
        {
            Min = SByte.MinValue,
            Max = SByte.MaxValue
        }

        enum TestEnumByte: byte
        {
            Min = Byte.MinValue,
            One = 1,
            Two,
            Max = Byte.MaxValue
        }

        enum TestEnumShort: short
        {
            Min = Int16.MinValue,
            Limit = (1 << 7) - 1,
            Treshold,
            Max = Int16.MaxValue,
        }

        enum TestEnumUShort: ushort
        {
            Min = UInt16.MinValue,
            Limit = (1 << 7) - 1,
            Treshold,
            Max = UInt16.MaxValue,
        }

        enum TestEnumInt: int
        {
            Min = Int32.MinValue,
            Limit = (1 << 21) - 1,
            Treshold,
            Max = Int32.MaxValue,
        }

        enum TestEnumUInt: uint
        {
            Min = UInt32.MinValue,
            Limit = (1 << 21) - 1,
            Treshold,
            Max = UInt32.MaxValue,
        }

        enum TestEnumLong: long
        {
            Min = Int64.MinValue,
            Limit = (1L << 49) - 1,
            Treshold,
            Max = Int64.MaxValue,
        }

        enum TestEnumULong: ulong
        {
            Min = UInt64.MinValue,
            Limit = (1UL << 49) - 1,
            Treshold,
            Max = UInt64.MaxValue,
        }

        private class NonSerializableClass
        {
            public int IntProp { get; set; }
            public string StringProp { get; set; }

            /// <summary>
            /// Overridden for the test equality check
            /// </summary>
            public override bool Equals(object obj)
            {
                if (!(obj is NonSerializableClass))
                    return base.Equals(obj);
                NonSerializableClass other = (NonSerializableClass)obj;
                return StringProp == other.StringProp && IntProp == other.IntProp;
            }
        }

        private sealed class NonSerializableSealedClass: NonSerializableClass
        {
            public int PublicDerivedField;
            private string PrivateDerivedField;

            public NonSerializableSealedClass(int i, string s)
            {
                PublicDerivedField = i;
                PrivateDerivedField = s;
            }
        }

        private struct NonSerializableStruct
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 10)]
            private string str10;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            private byte[] bytes3;

            public int IntProp { get; set; }

            public string Str10
            {
                get { return str10; }
                set { str10 = value; }
            }

            public byte[] Bytes3
            {
                get { return bytes3; }
                set { bytes3 = value; }
            }

            /// <summary>
            /// Overridden for the test equality check
            /// </summary>
            public override bool Equals(object obj)
            {
                if (!(obj is NonSerializableStruct))
                    return base.Equals(obj);
                NonSerializableStruct other = (NonSerializableStruct)obj;
                return str10 == other.str10 && IntProp == other.IntProp
                    && ((bytes3 == null && other.bytes3 == null) || (bytes3 != null && other.bytes3 != null
                    && bytes3[0] == other.bytes3[0] && bytes3[1] == other.bytes3[1] && bytes3[2] == other.bytes3[2]));
            }
        }

        [Serializable]
        private class BinarySerializableClass: AbstractClass, IBinarySerializable
        {
            public int PublicField;

            public int IntProp { get; set; }

            public string StringProp { get; set; }

            [OnDeserializing]
            private void OnDeserializing(StreamingContext ctx)
            {
                IntProp = -1;
            }

            #region IBinarySerializable Members

            public byte[] Serialize(BinarySerializationOptions options)
            {
                MemoryStream ms = new MemoryStream();
                using (BinaryWriter bw = new BinaryWriter(ms))
                {
                    bw.Write(PublicField);
                    bw.Write(IntProp);
                    bw.Write(StringProp);
                }

                return ms.ToArray();
            }

            public void Deserialize(BinarySerializationOptions options, byte[] serData)
            {
                using (BinaryReader br = new BinaryReader(new MemoryStream(serData)))
                {
                    PublicField = br.ReadInt32();
                    IntProp = br.ReadInt32();
                    StringProp = br.ReadString();
                }
            }

            #endregion

            /// <summary>
            /// Overridden for the test equality check
            /// </summary>
            public override bool Equals(object obj)
            {
                if (!(obj is BinarySerializableClass))
                    return base.Equals(obj);
                BinarySerializableClass other = (BinarySerializableClass)obj;
                return PublicField == other.PublicField && StringProp == other.StringProp && IntProp == other.IntProp;
            }
        }

        [Serializable]
        private sealed class BinarySerializableSealedClass: BinarySerializableClass
        {
            /// <summary>
            /// Non-default constructor so the class will be deserialized without constructor
            /// </summary>
            public BinarySerializableSealedClass(int intProp, string stringProp)
            {
                IntProp = intProp;
                StringProp = stringProp;
            }
        }

        [Serializable]
        private struct BinarySerializableStruct: IBinarySerializable
        {
            public int IntProp { get; set; }

            public string StringProp { get; set; }

            [NonSerialized]
            private int nonSerializedInt;

            [OnDeserializing]
            private void OnDeserializing(StreamingContext ctx)
            {
                IntProp = -1;
            }

            public BinarySerializableStruct(BinarySerializationOptions options, byte[] serData)
                : this()
            {
                using (BinaryReader br = new BinaryReader(new MemoryStream(serData)))
                {
                    IntProp = br.ReadInt32();
                    if (br.ReadBoolean())
                        StringProp = br.ReadString();
                }
            }

            #region IBinarySerializable Members

            public byte[] Serialize(BinarySerializationOptions options)
            {
                MemoryStream ms = new MemoryStream();
                using (BinaryWriter bw = new BinaryWriter(ms))
                {
                    bw.Write(IntProp);
                    bw.Write(StringProp != null);
                    if (StringProp != null)
                        bw.Write(StringProp);
                }

                return ms.ToArray();
            }

            public void Deserialize(BinarySerializationOptions options, byte[] serData)
            {
                throw new InvalidOperationException("This method never will be called");
            }

            #endregion
        }

        [Serializable]
        private struct SystemSerializableStruct
        {
            public int IntProp { get; set; }

            public string StringProp { get; set; }

            [NonSerialized]
            private int nonSerializedInt;

            [OnDeserializing]
            private void OnDeserializing(StreamingContext ctx)
            {
                IntProp = -1;
            }
        }

        [Serializable]
        private struct CustomSerializableStruct: ISerializable
        {
            public int IntProp { get; set; }

            public string StringProp { get; set; }

            [OnDeserializing]
            private void OnDeserializing(StreamingContext ctx)
            {
                IntProp = -1;
            }

            private CustomSerializableStruct(SerializationInfo info, StreamingContext context)
                : this()
            {
                IntProp = info.GetInt32("Int");
                StringProp = info.GetString("String");
            }

            #region ISerializable Members

            public void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                info.AddValue("Int", IntProp);
                info.AddValue("String", StringProp);
            }

            #endregion
        }

        [Serializable]
        private struct BinarySerializableStructNoCtor: IBinarySerializable
        {
            public int IntProp { get; set; }

            public string StringProp { get; set; }

            #region IBinarySerializable Members

            public byte[] Serialize(BinarySerializationOptions options)
            {
                MemoryStream ms = new MemoryStream();
                using (BinaryWriter bw = new BinaryWriter(ms))
                {
                    bw.Write(IntProp);
                    bw.Write(StringProp);
                }

                return ms.ToArray();
            }

            public void Deserialize(BinarySerializationOptions options, byte[] serData)
            {
                using (BinaryReader br = new BinaryReader(new MemoryStream(serData)))
                {
                    IntProp = br.ReadInt32();
                    StringProp = br.ReadString();
                }
            }

            #endregion
        }

        [Serializable]
        public abstract class AbstractClass
        {
        }

        [Serializable]
        private class SystemSerializableClass: AbstractClass
        {
            public int IntProp { get; set; }
            public string StringProp { get; set; }
            public bool? Bool { get; set; }

            /// <summary>
            /// Overridden for the test equality check
            /// </summary>
            public override bool Equals(object obj)
            {
                if (!(obj is SystemSerializableClass))
                    return base.Equals(obj);
                SystemSerializableClass other = (SystemSerializableClass)obj;
                return StringProp == other.StringProp && IntProp == other.IntProp && Bool == other.Bool;
            }
        }

        private sealed class NonSerializableClassWithSerializableBase: SystemSerializableClass
        {
            public int PublicDerivedField;
            private string PrivateDerivedField;

            public NonSerializableClassWithSerializableBase(int i, string s)
            {
                PublicDerivedField = i;
                PrivateDerivedField = s;
            }
        }

        [Serializable]
        private sealed class SystemSerializableSealedClass: SystemSerializableClass
        {
        }

        [Serializable]
        private class SerializationEventsClass: IDeserializationCallback
        {
            [NonSerialized]
            private IntPtr privatePointer;

            private static int idCounter;

            [NonSerialized]
            private SerializationEventsClass parent;

            protected readonly Collection<SerializationEventsClass> children = new Collection<SerializationEventsClass>();

            public int Id { get; protected set; }
            public string Name { get; set; }
            public SerializationEventsClass Parent { get { return parent; } }
            public ICollection Children { get { return children; } }

            public SerializationEventsClass()
            {
                Id = ++idCounter;
            }

            public SerializationEventsClass AddChild(string name)
            {
                SerializationEventsClass child = new SerializationEventsClass { Name = name };
                children.Add(child);
                child.parent = this;
                privatePointer = new IntPtr(children.Count);
                return child;
            }

            [OnSerializing]
            private void OnSerializing(StreamingContext ctx)
            {
                //Console.WriteLine("OnSerializing {0}", this);
                privatePointer = IntPtr.Zero;
            }

            [OnSerialized]
            private void OnSerialized(StreamingContext ctx)
            {
                //Console.WriteLine("OnSerialized {0}", this);
                if (children.Count > 0)
                    privatePointer = new IntPtr(children.Count);
            }

            [OnDeserializing]
            private void OnDeserializing(StreamingContext ctx)
            {
                //Console.WriteLine("OnDeserializing {0}", this);
                privatePointer = new IntPtr(-1);
            }

            [OnDeserialized]
            private void OnDeserialized(StreamingContext ctx)
            {
                //Console.WriteLine("OnDeserialized {0}", this);
                if (children != null)
                    privatePointer = new IntPtr(children.Count);
            }

            #region IDeserializationCallback Members

            public virtual void OnDeserialization(object sender)
            {
                //Console.WriteLine("OnDeserialization {0}", this);
                if (children != null)
                {
                    foreach (SerializationEventsClass child in children)
                    {
                        child.parent = this;
                    }
                }
            }

            #endregion

            public override bool Equals(object obj)
            {
                SerializationEventsClass other = obj as SerializationEventsClass;
                if (other == null)
                    return base.Equals(obj);

                return Id == other.Id
                    && privatePointer == other.privatePointer
                    && (parent == null && other.parent == null || parent != null && other.parent != null && parent.Id == other.parent.Id)
                    && children.SequenceEqual(other.children);
            }

            public override string ToString()
            {
                return String.Format("{0} - {1}", Id, Name ?? "<null>");
            }
        }

        [Serializable]
        private class CustomSerializedClass: SerializationEventsClass, ISerializable
        {
            public bool? Bool { get; set; }

            private CustomSerializedClass(SerializationInfo info, StreamingContext context)
            {
                Id = info.GetInt32("Id");
                Name = info.GetString("Name");
                Bool = (bool?)info.GetValue("Bool", typeof(bool?));
                ((Collection<SerializationEventsClass>)info.GetValue("Children", typeof(Collection<SerializationEventsClass>))).ForEach(child => children.Add(child));
            }

            public CustomSerializedClass()
            {
            }

            #region ISerializable Members

            public void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                info.AddValue("Id", Id);
                info.AddValue("Name", Name);
                info.AddValue("Bool", Bool, typeof(bool?));
                info.AddValue("Children", Children);
                info.AddValue("dummy", null, typeof(List<string[]>));
            }

            #endregion

            [OnSerialized]
            private void OnSerialized(StreamingContext ctx)
            {
                //Console.WriteLine("OnSerialized derived {0}", this);
            }

            [OnDeserialized]
            private void OnDeserialized(StreamingContext ctx)
            {
                //Console.WriteLine("OnDeserialized derived {0}", this);
            }

            public override bool Equals(object obj)
            {
                CustomSerializedClass other = obj as CustomSerializedClass;
                if (other == null)
                    return base.Equals(obj);

                return Bool == other.Bool && base.Equals(obj);
            }
        }

        [Serializable]
        private sealed class CustomSerializedSealedClass: CustomSerializedClass, ISerializable
        {

            private CustomSerializedSealedClass(SerializationInfo info, StreamingContext context)
            {
                throw new InvalidOperationException("Never executed");
            }

            // this is what called on deserialization
            internal CustomSerializedSealedClass(int id, string name, IEnumerable<SerializationEventsClass> children, bool? boolean)
            {
                Id = id;
                Name = name;
                children.ForEach(child => this.children.Add(child));
                Bool = boolean;
            }

            public CustomSerializedSealedClass(string name)
            {
                Name = name;
            }

            #region ISerializable Members

            public void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                info.SetType(typeof(CustomAdvancedSerializedClassHelper));
                base.GetObjectData(info, context);
            }

            #endregion
        }

        [Serializable]
        private class CustomAdvancedSerializedClassHelper: IObjectReference, ISerializable, IDeserializationCallback
        {
            readonly CustomSerializedSealedClass toDeserialize;

            private CustomAdvancedSerializedClassHelper(SerializationInfo info, StreamingContext context)
            {
                toDeserialize = new CustomSerializedSealedClass(info.GetInt32("Id"), info.GetString("Name"),
                    (Collection<SerializationEventsClass>)info.GetValue("Children", typeof(Collection<SerializationEventsClass>)),
                    (bool?)info.GetValue("Bool", typeof(bool?)));
            }

            [OnDeserialized]
            private void OnDeserialized(StreamingContext ctx)
            {
                //Console.WriteLine("OnDeserialized Helper");
                Reflector.SetInstanceFieldByName(toDeserialize, "privatePointer", new IntPtr(toDeserialize.Children.Count));
            }

            #region IObjectReference Members

            public object GetRealObject(StreamingContext context)
            {
                return toDeserialize;
            }

            #endregion

            #region ISerializable Members

            public void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                throw new NotImplementedException("Never executed");
            }

            #endregion

            #region IDeserializationCallback Members

            public void OnDeserialization(object sender)
            {
                toDeserialize.OnDeserialization(sender);
            }

            #endregion
        }

        [Serializable]
        private class DefaultGraphObjRef: IObjectReference
        {
            private readonly static DefaultGraphObjRef instance = new DefaultGraphObjRef("singleton instance");
            private readonly string name;

            public static DefaultGraphObjRef Get()
            {
                return instance;
            }

            private DefaultGraphObjRef(string name)
            {
                this.name = name;
            }

            public override string ToString()
            {
                return name;
            }

            public override bool Equals(object obj)
            {
                return ReferenceEquals(obj, instance);
            }

            #region IObjectReference Members

            public object GetRealObject(StreamingContext context)
            {
                return instance;
            }

            #endregion
        }

        [Serializable]
        private sealed class CustomGraphDefaultObjRef: ISerializable
        {
            public string Name { get; set; }

            #region ISerializable Members

            public void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                info.AddValue("name", Name);
                info.SetType(typeof(CustomGraphDefaultObjRefDeserializer));
            }

            #endregion

            public override bool Equals(object obj)
            {
                var other = obj as CustomGraphDefaultObjRef;
                if (other == null)
                    return false;
                return Name == other.Name;
            }
        }

        [Serializable]
        private class CustomGraphDefaultObjRefDeserializer: IObjectReference
        {
            private string name;

            #region IObjectReference Members

            public object GetRealObject(StreamingContext context)
            {
                return new CustomGraphDefaultObjRef { Name = name };
            }

            #endregion
        }

        [Serializable]
        private class CustomGenericCollection<T>: List<T> { }

        [Serializable]
        private class CustomNonGenericCollection: ArrayList { }

        [Serializable]
        private class CustomGenericDictionary<TKey, TValue>: Dictionary<TKey, TValue>
        {
            public CustomGenericDictionary()
            {
            }

            public CustomGenericDictionary(SerializationInfo info, StreamingContext context) :
                base(info, context)
            {
            }
        }

        [Serializable]
        private class CustomNonGenericDictionary: Hashtable
        {
            public CustomNonGenericDictionary()
            {
            }

            public CustomNonGenericDictionary(SerializationInfo info, StreamingContext context) :
                base(info, context)
            {
            }
        }

        [Serializable]
        private sealed class MemoryStreamWithEquals: MemoryStream
        {
            public override bool Equals(object obj)
            {
                MemoryStreamWithEquals other = obj as MemoryStreamWithEquals;
                if (other == null)
                    return base.Equals(obj);

                return this.CanRead == other.CanRead && this.CanSeek == other.CanSeek && this.CanTimeout == other.CanTimeout && this.CanWrite == other.CanWrite
                    && this.Capacity == other.Capacity && this.Length == other.Length && this.Position == other.Position && this.GetBuffer().SequenceEqual(other.GetBuffer());
            }
        }

#if NET40 || NET45
        private class TestSerializationBinder: SerializationBinder
        {
            public override void BindToName(Type serializedType, out string assemblyName, out string typeName)
            {
                if (dumpDetails)
                    Console.WriteLine("BindToName: " + serializedType);
                assemblyName = "rev_" + new string(serializedType.Assembly.FullName.Reverse().ToArray());
                typeName = "rev_" + new string(serializedType.FullName.Reverse().ToArray());
            }

            public override Type BindToType(string assemblyName, string typeName)
            {
                if (dumpDetails)
                    Console.WriteLine("BindToType: {0}, {1}", assemblyName, typeName);
                if (assemblyName.StartsWith("rev_", StringComparison.Ordinal))
                    assemblyName = new string(assemblyName.Substring(4).Reverse().ToArray());

                if (typeName.StartsWith("rev_", StringComparison.Ordinal))
                    typeName = new string(typeName.Substring(4).Reverse().ToArray());

                Assembly assembly = assemblyName.Length == 0 ? null : Reflector.GetLoadedAssemblies().FirstOrDefault(asm => asm.FullName == assemblyName);
                if (assembly == null && assemblyName.Length > 0)
                    return null;

                return assembly == null ? Reflector.ResolveType(typeName) : Reflector.ResolveType(assembly, typeName);
            }
        }
#elif !NET35
#error .NET version is not set or not supported!
#endif

        [Serializable]
        private sealed class CircularReferenceClass
        {
            private static int idCounter;

            private CircularReferenceClass parent;
            private readonly Collection<CircularReferenceClass> children = new Collection<CircularReferenceClass>();

            public int Id { get; private set; }
            public string Name { get; set; }
            public CircularReferenceClass Parent { get { return parent; } }
            public Collection<CircularReferenceClass> Children { get { return children; } }

            public CircularReferenceClass()
            {
                Id = ++idCounter;
            }

            public CircularReferenceClass AddChild(string name)
            {
                CircularReferenceClass child = new CircularReferenceClass { Name = name };
                children.Add(child);
                child.parent = this;
                return child;
            }

            public override bool Equals(object obj)
            {
                CircularReferenceClass other = obj as CircularReferenceClass;
                if (other == null)
                    return base.Equals(obj);

                return Id == other.Id
                    && (parent == null && other.parent == null || parent != null && other.parent != null && parent.Id == other.parent.Id)
                    && children.SequenceEqual(other.children); // can cause stack overflow
            }

            public override string ToString()
            {
                return String.Format("{0} - {1}", Id, Name ?? "<null>");
            }

        }

        [Serializable]
        private class SelfReferencer: ISerializable
        {
            public string Name { get; set; }
            public SelfReferencer Self { get; set; }

            [Serializable]
            private class Box
            {
                internal SelfReferencer owner;
            }

            private readonly Box selfReferenceFromChild;

            public SelfReferencer(string name)
            {
                Name = name;
                Self = this;
                selfReferenceFromChild = new Box{owner = this};
            }

            private SelfReferencer(SerializationInfo info, StreamingContext context)
            {
                Name = info.GetString("name");
                Self = (SelfReferencer)info.GetValue("self", typeof(SelfReferencer));
                selfReferenceFromChild = (Box)info.GetValue("selfBox", typeof(Box));
            }

            public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                info.AddValue("name", Name);
                info.AddValue("self", Self);
                info.AddValue("selfBox", selfReferenceFromChild);
            }

            public override bool Equals(object obj)
            {
                if (obj == null || obj.GetType() != typeof(SelfReferencer))
                    return false;

                var other = (SelfReferencer)obj;
                return other.Name == this.Name && ReferenceEquals(other, other.Self) && ReferenceEquals(this, this.Self);
            }
        }

        [Serializable]
        private class SelfReferencerEvil: SelfReferencer
        {
            public SelfReferencerEvil(string name)
                : base(name)
            {
            }

            public override void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                base.GetObjectData(info, context);
                info.SetType(typeof(SelfReferencerEvilDeserializer));
            }

            public override bool Equals(object obj)
            {
                if (obj == null || obj.GetType() != typeof(SelfReferencerEvil))
                    return false;

                var other = (SelfReferencerEvil)obj;
                return other.Name == this.Name && ReferenceEquals(other, other.Self) && ReferenceEquals(this, this.Self);
            }
        }

        [Serializable]
        private class SelfReferencerEvilDeserializer: IObjectReference, ISerializable
        {
            private SelfReferencer instance;
            private string name;

            protected SelfReferencerEvilDeserializer(SerializationInfo info, StreamingContext context)
            {
                name = info.GetString("name");
                instance = (SelfReferencer)info.GetValue("self", typeof(SelfReferencer));
            }

            public object GetRealObject(StreamingContext context)
            {
                return new SelfReferencerEvil(name);
            }

            public void GetObjectData(SerializationInfo info, StreamingContext context)
            {
                throw new NotImplementedException();
            }
        }

        private class TestWriter: BinaryWriter
        {
            private long pos;
            private readonly bool log;

            public TestWriter(Stream stream, bool log)
                : base(stream)
            {
                this.log = log;
            }

            private void Advance(int offset)
            {
                if (log)
                    Console.Write("{0:X8} ", pos);
                pos += offset;
            }

            public override void Write(bool value)
            {
                Advance(1);
                if (log)
                    Console.WriteLine("bool: {0} ({1}) - {2}", value, Convert.ToInt32(value), new StackTrace().GetFrames()[1].GetMethod().Name);
                base.Write(value);
            }

            public override void Write(byte value)
            {
                Advance(1);
                if (log)
                {
                    var frames = new StackTrace().GetFrames();
                    string name = frames[1].GetMethod().Name;
                    if (name == "Write7BitInt")
                        name += " (" + frames[2].GetMethod().Name + ")";
                    Console.WriteLine("byte: {0} ({0:X2}) - {1}", value, name);
                }
                base.Write(value);
            }

            public override void Write(byte[] buffer)
            {
                Advance(buffer.Length);
                if (log)
                    Console.WriteLine("{0} bytes: {1} ({2}) - {3}", buffer.Length, buffer.ToDecimalValuesString(), buffer.ToHexValuesString(","), new StackTrace().GetFrames()[1].GetMethod().Name);
                base.Write(buffer);
            }

            public override void Write(byte[] buffer, int index, int count)
            {
                Advance(count);
                if (log)
                    Console.WriteLine("{0} bytes: {1} ({2}) - {3}", count, buffer.Skip(index).Take(count).ToArray().ToDecimalValuesString(), buffer.Skip(index).Take(count).ToArray().ToHexValuesString(","), new StackTrace().GetFrames()[1].GetMethod().Name);
                base.Write(buffer, index, count);
            }

            public override void Write(char ch)
            {
                Advance(2);
                if (log)
                    Console.WriteLine("char: {0} ({1:X4}) - {2}", ch, (uint)ch, new StackTrace().GetFrames()[1].GetMethod().Name);
                base.Write(ch);
            }

            public override void Write(char[] chars)
            {
                Advance(2 * chars.Length); // depends on encoding but is alright for comparison
                if (log)
                    Console.WriteLine("{0} chars: {1} - {2}", chars.Length, new string(chars), new StackTrace().GetFrames()[1].GetMethod().Name);
                base.Write(chars);
            }

            public override void Write(char[] chars, int index, int count)
            {
                Advance(2 * count); // depends on encoding but is alright for comparison
                if (log)
                    Console.WriteLine("{0} chars: {1} - {2}", count, new string(chars.Skip(index).Take(count).ToArray()), new StackTrace().GetFrames()[1].GetMethod().Name);
                base.Write(chars, index, count);
            }

            public override void Write(decimal value)
            {
                Advance(16);
                if (log)
                    Console.WriteLine("decimal: {0} - {1}", value, new StackTrace().GetFrames()[1].GetMethod().Name);
                base.Write(value);
            }

            public override void Write(double value)
            {
                Advance(8);
                if (log)
                    Console.WriteLine("double: {0:R} - {1}", value, new StackTrace().GetFrames()[1].GetMethod().Name);
                base.Write(value);
            }

            public override void Write(float value)
            {
                Advance(4);
                if (log)
                    Console.WriteLine("float: {0:R} - {1}", value, new StackTrace().GetFrames()[1].GetMethod().Name);
                base.Write(value);
            }

            public override void Write(int value)
            {
                Advance(4);
                if (log)
                    Console.WriteLine("int: {0} ({0:X8}) - {1}", value, new StackTrace().GetFrames()[1].GetMethod().Name);
                base.Write(value);
            }

            public override void Write(long value)
            {
                Advance(8);
                if (log)
                    Console.WriteLine("long: {0} ({0:X16}) - {1}", value, new StackTrace().GetFrames()[1].GetMethod().Name);
                base.Write(value);
            }

            public override void Write(sbyte value)
            {
                Advance(1);
                if (log)
                    Console.WriteLine("sbyte: {0} ({0:X2}) - {1}", value, new StackTrace().GetFrames()[1].GetMethod().Name);
                base.Write(value);
            }

            public override void Write(short value)
            {
                Advance(2);
                if (log)
                    Console.WriteLine("short: {0} ({0:X4}) - {1}", value, new StackTrace().GetFrames()[1].GetMethod().Name);
                base.Write(value);
            }

            public override void Write(string value)
            {
                base.Write(value);
                Advance(value.Length); // depends on encoding but is alright for comparison
                if (log)
                    Console.WriteLine("string: {0} - {1}", value, new StackTrace().GetFrames()[1].GetMethod().Name);
            }

            public override void Write(uint value)
            {
                Advance(4);
                if (log)
                    Console.WriteLine("uint: {0} ({0:X8}) - {1}", value, new StackTrace().GetFrames()[1].GetMethod().Name);
                base.Write(value);
            }

            public override void Write(ulong value)
            {
                Advance(8);
                if (log)
                    Console.WriteLine("ulong: {0} ({0:X16}) - {1}", value, new StackTrace().GetFrames()[1].GetMethod().Name);
                base.Write(value);
            }

            public override void Write(ushort value)
            {
                Advance(2);
                if (log)
                    Console.WriteLine("ushort: {0} ({0:X4}) - {1}", value, new StackTrace().GetFrames()[1].GetMethod().Name);
                base.Write(value);
            }
        }

        private class TestReader: BinaryReader
        {
            private bool log;
            private long pos;

            public TestReader(Stream s, bool log)
                : base(s)
            {
                this.log = log;
            }

            private void Advance(int offset)
            {
                if (log)
                    Console.Write("{0:X8} ", pos);
                pos += offset;
            }

            public override int Read()
            {
                var result = base.Read();
                Advance(result >= 0 ? 1 : 0);
                if (log)
                    Console.WriteLine("int char: {0} ({0:X}) - {1}", result, new StackTrace().GetFrames()[1].GetMethod().Name);
                return result;
            }

            public override int Read(byte[] buffer, int index, int count)
            {
                var result = base.Read(buffer, index, count);
                Advance(result);
                if (log)
                    Console.WriteLine("{0} bytes: {1} ({2}) - {3}", result, buffer.Skip(index).Take(result).ToArray().ToDecimalValuesString(), buffer.Skip(index).Take(result).ToArray().ToHexValuesString(","), new StackTrace().GetFrames()[1].GetMethod().Name);
                return result;
            }

            public override int Read(char[] buffer, int index, int count)
            {
                var result = base.Read(buffer, index, count);
                Advance(result * 2); // depends on encoding but ok for comparison
                if (log)
                    Console.WriteLine("{0} chars: {1} - {2}", result, new string(buffer.Skip(index).Take(result).ToArray()), new StackTrace().GetFrames()[1].GetMethod().Name);
                return result;
            }

            public override bool ReadBoolean()
            {
                var result = base.ReadBoolean();
                Advance(1);
                if (log)
                    Console.WriteLine("bool: {0} ({1}) - {2}", result, Convert.ToInt32(result), new StackTrace().GetFrames()[1].GetMethod().Name);
                return result;
            }

            public override byte ReadByte()
            {
                var result = base.ReadByte();
                Advance(1);
                if (log)
                {
                    var frames = new StackTrace().GetFrames();
                    string name = frames[1].GetMethod().Name;
                    if (name == "Read7BitInt")
                        name += " (" + frames[2].GetMethod().Name + ")";
                    Console.WriteLine("byte: {0} ({0:X2}) - {1}", result, name);
                }
                return result;
            }

            public override byte[] ReadBytes(int count)
            {
                var result = base.ReadBytes(count);
                Advance(count);
                if (log)
                    Console.WriteLine("{0} bytes: {1} ({2}) - {3}", result.Length, result.ToDecimalValuesString(), result.ToHexValuesString(","), new StackTrace().GetFrames()[1].GetMethod().Name);
                return result;
            }

            public override char ReadChar()
            {
                var result = base.ReadChar();
                Advance(2);
                if (log)
                    Console.WriteLine("char: {0} ({1:X2}) - {2}", result, (uint)result, new StackTrace().GetFrames()[1].GetMethod().Name);
                return result;
            }

            public override char[] ReadChars(int count)
            {
                var result = base.ReadChars(count);
                Advance(2 * count); // depends on encoding but ok for comparison
                if (log)
                    Console.WriteLine("{0} chars: {1} - {2}", result.Length, new string(result), new StackTrace().GetFrames()[1].GetMethod().Name);
                return result;
            }

            public override decimal ReadDecimal()
            {
                var result = base.ReadDecimal();
                Advance(16);
                if (log)
                    Console.WriteLine("decimal: {0} - {1}", result, new StackTrace().GetFrames()[1].GetMethod().Name);
                return result;
            }

            public override double ReadDouble()
            {
                var result = base.ReadDouble();
                Advance(8);
                if (log)
                    Console.WriteLine("double: {0:R} - {1}", result, new StackTrace().GetFrames()[1].GetMethod().Name);
                return result;
            }

            public override short ReadInt16()
            {
                var result = base.ReadInt16();
                Advance(2);
                if (log)
                    Console.WriteLine("short: {0} ({0:X4}) - {1}", result, new StackTrace().GetFrames()[1].GetMethod().Name);
                return result;
            }

            public override int ReadInt32()
            {
                var result = base.ReadInt32();
                Advance(4);
                if (log)
                    Console.WriteLine("int: {0} ({0:X8}) - {1}", result, new StackTrace().GetFrames()[1].GetMethod().Name);
                return result;
            }

            public override long ReadInt64()
            {
                var result = base.ReadInt64();
                Advance(8);
                if (log)
                    Console.WriteLine("long: {0} ({0:X16}) - {1}", result, new StackTrace().GetFrames()[1].GetMethod().Name);
                return result;
            }

            public override sbyte ReadSByte()
            {
                var result = base.ReadSByte();
                Advance(1);
                if (log)
                    Console.WriteLine("sbyte: {0} ({0:X2}) - {1}", result, new StackTrace().GetFrames()[1].GetMethod().Name);
                return result;
            }

            public override float ReadSingle()
            {
                var result = base.ReadSingle();
                Advance(4);
                if (log)
                    Console.WriteLine("float: {0:R} - {1}", result, new StackTrace().GetFrames()[1].GetMethod().Name);
                return result;
            }

            public override string ReadString()
            {
                var result = base.ReadString();
                Advance(result.Length); // depends on encoding but ok for comparison
                if (log)
                    Console.WriteLine("string: {0} - {1}", result, new StackTrace().GetFrames()[1].GetMethod().Name);
                return result;
            }

            public override ushort ReadUInt16()
            {
                var result = base.ReadUInt16();
                Advance(2);
                if (log)
                    Console.WriteLine("ushort: {0} ({0:X4}) - {1}", result, new StackTrace().GetFrames()[1].GetMethod().Name);
                return result;
            }

            public override uint ReadUInt32()
            {
                var result = base.ReadUInt32();
                Advance(4);
                if (log)
                    Console.WriteLine("uint: {0} ({0:X8}) - {1}", result, new StackTrace().GetFrames()[1].GetMethod().Name);
                return result;
            }

            public override ulong ReadUInt64()
            {
                var result = base.ReadUInt64();
                Advance(8);
                if (log)
                    Console.WriteLine("ulong: {0} ({0:X16}) - {1}", result, new StackTrace().GetFrames()[1].GetMethod().Name);
                return result;
            }
        }

        private class TestSurrogateSelector: ISurrogateSelector, ISerializationSurrogate
        {
            #region Fields

            private ISurrogateSelector next;

            #endregion

            #region ISurrogateSelector Members

            public void ChainSelector(ISurrogateSelector selector)
            {
                next = selector;
            }

            public ISurrogateSelector GetNextSelector()
            {
                return next;
            }

            public ISerializationSurrogate GetSurrogate(Type type, StreamingContext context, out ISurrogateSelector selector)
            {
                if (type == null)
                {
                    throw new ArgumentNullException("type");
                }

                if (!type.IsPrimitive && !type.IsArray && !typeof(ISerializable).IsAssignableFrom(type) && !type.In(typeof(string), typeof(UIntPtr)))
                {
                    selector = this;
                    return this;
                }

                if (next != null)
                {
                    return next.GetSurrogate(type, context, out selector);
                }

                selector = null;
                return null;

            }

            #endregion

            #region ISerializationSurrogate Members

            void ISerializationSurrogate.GetObjectData(object obj, SerializationInfo info, StreamingContext context)
            {
                if (obj == null)
                    throw new ArgumentNullException("obj");
                if (info == null)
                    throw new ArgumentNullException("info");

                Type type = obj.GetType();

                for (Type t = type; t != typeof(object); t = t.BaseType)
                {
                    FieldInfo[] fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(f => !f.IsNotSerialized).ToArray();
                    foreach (FieldInfo field in fields)
                    {
                        info.AddValue(field.Name, Reflector.GetField(obj, field));
                    }
                }
            }

            object ISerializationSurrogate.SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector)
            {
                if (obj == null)
                    throw new ArgumentNullException("obj");
                if (info == null)
                    throw new ArgumentNullException("info");

                foreach (SerializationEntry entry in info)
                {
                    Reflector.SetInstanceFieldByName(obj, entry.Name, entry.Value);
                }

                return obj;
            }

            #endregion
        }

        #endregion

        #region Test Methods

        [TestMethod]
        public void SerializeSimpleTypes()
        {
            object[] referenceObjects = 
            {
                null,
                new object(),
                DBNull.Value,
                true,
                (sbyte)1,
                (byte)1,
                (short)1,
                (ushort)1,
                (int)1,
                (uint)1,
                (long)1,
                (ulong)1,
                'a',
                "alma",
                (float)1,
                (double)1,
                (decimal)1,
                DateTime.UtcNow,
                DateTime.Now,
                new IntPtr(1),
                new UIntPtr(1),
                new Version(1, 2, 3, 4),
                new Guid("ca761232ed4211cebacd00aa0057b223"),
                new TimeSpan(1, 1, 1),
                new DateTimeOffset(DateTime.Now),
                new DateTimeOffset(DateTime.UtcNow),
                new DateTimeOffset(DateTime.Now.Ticks, new TimeSpan(1, 1, 0)),
                new Uri(@"x:\teszt"), // 20
                new DictionaryEntry(1, "alma"),
                new KeyValuePair<int,string>(1, "alma"), // 14
                new BitArray(new[] {true, false, true}), // 10 -> 7
                new StringBuilder("alma")
            };

            SystemSerializeObject(referenceObjects);
            SystemSerializeObjects(referenceObjects);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.None); // 296 -> 273
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None); // 267

            referenceObjects = new object[]
            {
                new BitVector32(13),
                BitVector32.CreateSection(13),
            };

            KGySerializeObject(referenceObjects, BinarySerializationOptions.None);
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None);
        }

        [TestMethod]
        public void SerializeValues()
        {
            object[] referenceObjects = 
            {
                // 1 bytes
                SByte.MinValue,
                SByte.MaxValue,
                Byte.MinValue,
                Byte.MaxValue,

                // 2 bytes
                Int16.MinValue,
                (short)TestEnumShort.Treshold,
                Int16.MaxValue,
                (ushort)TestEnumUShort.Treshold,
                UInt16.MaxValue,
                Char.MaxValue,

                // 2 bytes compressed
                (short)TestEnumShort.Limit,
                UInt16.MinValue,
                (ushort)TestEnumUShort.Limit,
                Char.MinValue,

                // 4 bytes
                Int32.MinValue,
                (int)TestEnumInt.Treshold,
                Int32.MaxValue,
                (uint)TestEnumUInt.Treshold,
                UInt32.MaxValue,

                // 4 bytes compressed
                (int)TestEnumInt.Limit,   // 5
                UInt32.MinValue,          // 3
                (uint)TestEnumUInt.Limit, // 5

                // 8 bytes
                Int64.MinValue,
                (long)TestEnumLong.Treshold,
                Int64.MaxValue,
                (ulong)TestEnumULong.Treshold,
                UInt64.MaxValue,

                // 8 bytes compressed
                (long)TestEnumLong.Limit,   // 9
                UInt64.MinValue,            // 3
                (ulong)TestEnumULong.Limit, // 9
            };

            SystemSerializeObject(referenceObjects);
            SystemSerializeObjects(referenceObjects);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.None); // 198
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None); // 165
        }

        [TestMethod]
        public void SerializeEnums()
        {
            object[] referenceObjects = 
            {
                // local enums, testing 7-bit encodings
                TestEnumByte.Min, 
                TestEnumByte.Max, 
                TestEnumSByte.Min,
                TestEnumSByte.Max,

                TestEnumShort.Min,
                TestEnumShort.Limit,
                TestEnumShort.Treshold,
                TestEnumShort.Max,

                TestEnumUShort.Min,
                TestEnumUShort.Limit,
                TestEnumUShort.Treshold,
                TestEnumUShort.Max,

                TestEnumInt.Min,
                TestEnumInt.Limit,
                TestEnumInt.Treshold,
                TestEnumInt.Max,

                TestEnumUInt.Min,
                TestEnumUInt.Limit,
                TestEnumUInt.Treshold,
                TestEnumUInt.Max,

                TestEnumLong.Min,
                TestEnumLong.Limit,
                TestEnumLong.Treshold,
                TestEnumLong.Max,

                TestEnumULong.Min,
                TestEnumULong.Limit,
                TestEnumULong.Treshold,
                TestEnumULong.Max,

                ConsoleColor.White, // mscorlib enum
                ConsoleColor.Black, // mscorlib enum

                UriKind.Absolute, // System enum
                UriKind.Relative, // System enum

                HandleInheritability.Inheritable, // System.Core enum

                BinarySerializationOptions.RecursiveSerializationAsFallback, // KGySoft.Libraries enum
            };

            SystemSerializeObject(referenceObjects);
            SystemSerializeObjects(referenceObjects);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.None); // 871
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.OmitAssemblyQualifiedNames); // ? -> 802
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.OmitAssemblyQualifiedNames);
        }

        [TestMethod]
        public void SerializeTypes()
        {
            object[] referenceObjects = 
            {
                typeof(int), // 166
                typeof(List<int>), // 280
                typeof(CustomGenericCollection<int>), // 303

                typeof(List<>), // 187
                typeof(List<>).GetGenericArguments()[0] // 335
            };
            SystemSerializeObject(referenceObjects);
            SystemSerializeObjects(referenceObjects);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.None); // -> 1148 -> 849
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None);
        }

        [TestMethod]
        public void SerializeComplexTypes()
        {
            object[] referenceObjects = 
            {
                new BinarySerializableSealedClass(3, "cica"), // None: 154
                new BinarySerializableClass{ IntProp = 1, StringProp = "alma"}, // None: 148
                new BinarySerializableStruct{ IntProp = 2, StringProp = "béka" }, // None: 147
                new BinarySerializableStructNoCtor { IntProp = 2, StringProp = "béka" }, // None: 152
                new SystemSerializableClass{ IntProp = 3, StringProp = "cica",  Bool = null }, // None: 224

                new KeyValuePair<int, object>(1, new object[] {1, "alma", DateTime.Now, null}), // None: 36

                new SerializationEventsClass { Name = "Parent" }.AddChild("Child").AddChild("GrandChild").Parent.Parent, // None: 455
                new CustomSerializedClass { Name = "Parent derived", Bool = null }.AddChild("Child base").AddChild("GrandChild base").Parent.Parent, // None: 525
                new CustomSerializedSealedClass("Parent advanced derived").AddChild("Child base").AddChild("GrandChild base").Parent.Parent, // IObjectReference - None: 548
                DefaultGraphObjRef.Get(), // IObjectReference without ISerializable
                new CustomGraphDefaultObjRef{ Name = "alma" } // obj is ISerializable but IObjectReference is not
            };

            SystemSerializeObject(referenceObjects);
            SystemSerializeObjects(referenceObjects);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.None); // 1697 -> 1711
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.OmitAssemblyQualifiedNames); // 1628 -> 1642
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.OmitAssemblyQualifiedNames);

            referenceObjects = new object[]
            {
                new NonSerializableClass{ IntProp = 3, StringProp = "cica" },
                new NonSerializableSealedClass(1, "alma") { IntProp = 1, StringProp = "alma" },
                new NonSerializableStruct{ Bytes3 = new byte[] {1, 2, 3}, IntProp = 1, Str10 = "alma" },
            };

            KGySerializeObject(referenceObjects, BinarySerializationOptions.RecursiveSerializationAsFallback); // 529
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.RecursiveSerializationAsFallback);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.RecursiveSerializationAsFallback | BinarySerializationOptions.CompactSerializationOfStructures); // 492
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.RecursiveSerializationAsFallback | BinarySerializationOptions.CompactSerializationOfStructures);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.RecursiveSerializationAsFallback | BinarySerializationOptions.CompactSerializationOfStructures | BinarySerializationOptions.OmitAssemblyQualifiedNames); // 423
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.RecursiveSerializationAsFallback | BinarySerializationOptions.CompactSerializationOfStructures | BinarySerializationOptions.OmitAssemblyQualifiedNames);
        }

        [TestMethod]
        public void SerializeByteArrays()
        {
            object[] referenceObjects = 
            {
                new byte[] { 1, 2, 3}, // single byte array
                new byte[,] { {11, 12, 13}, {21, 22, 23} }, // multidimensional byte array
                new byte[][] { new byte[] {11, 12, 13}, new byte[] {21, 22, 23, 24, 25}, null }, // jagged byte array
                new byte[][,] { new byte[,] {{11, 12, 13}, {21, 22, 23}}, new byte[,] {{11, 12, 13, 14}, {21, 22, 23, 24}, {31, 32, 33, 34}} }, // crazy jagged byte array 1 (2D matrix of 1D arrays)
                new byte[,][] { {new byte[] {11, 12, 13}, new byte[] { 21, 22, 23}}, { new byte[] {11, 12, 13, 14}, new byte[] {21, 22, 23, 24}} }, // crazy jagged byte array 2 (1D array of 2D matrices)
                new byte[][,,] { new byte[,,] { { {11, 12, 13}, {21, 21, 23} } }, null }, // crazy jagged byte array containing null reference
                Array.CreateInstance(typeof(byte), new int[] {3}, new int[]{-1}), // array with -1..1 index interval
                Array.CreateInstance(typeof(byte), new int[] {3, 3}, new int[]{-1, 1}) // array with [-1..1 and 1..3] index interval
            };

            SystemSerializeObject(referenceObjects);
            SystemSerializeObjects(referenceObjects);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.None); // 175 -> 184
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None);
        }

        [TestMethod]
        public void SerializeSimpleArrays()
        {
            object[] referenceObjects = 
            {
                new object[] { new object(), null}, 
                new DBNull[] { DBNull.Value, null },
                new bool[] { true, false },
                new sbyte[] { 1, 2 },
                new byte[] { 1, 2 },
                new short[] { 1, 2 },
                new ushort[] { 1, 2 },
                new int[] { 1, 2 },
                new uint[] { 1, 2 },
                new long[] { 1, 2 },
                new ulong[] { 1, 2 },
                new char[] { 'a', 'á' }, // Char.ConvertFromUtf32(0x1D161)[0] }, //U+1D161 = MUSICAL SYMBOL SIXTEENTH NOTE, serializing its low-surrogate <- System serializer fails at compare
                new string[] { "alma", null },
                new float[] { 1, 2 },
                new double[] { 1, 2 },
                new decimal[] { 1, 2 },
                new DateTime[] { DateTime.UtcNow, DateTime.Now },
                new IntPtr[] { new IntPtr(1), IntPtr.Zero },
                new UIntPtr[] { new UIntPtr(1), UIntPtr.Zero },
                new Version[] {new Version(1, 2, 3, 4), null},
                new Guid[] { new Guid("ca761232ed4211cebacd00aa0057b223"), Guid.NewGuid() },
                new TimeSpan[] { new TimeSpan(1, 1, 1), new TimeSpan(DateTime.UtcNow.Ticks) },
                new DateTimeOffset[] { new DateTimeOffset(DateTime.Now), new DateTimeOffset(DateTime.UtcNow), new DateTimeOffset(DateTime.Now.Ticks, new TimeSpan(1, 1, 0)) },
                new Uri[] { new Uri(@"x:\teszt"), new Uri("ftp://myUrl/%2E%2E/%2E%2E"), null },
                new DictionaryEntry[] { new DictionaryEntry(1, "alma") },
                new KeyValuePair<int, string>[] { new KeyValuePair<int,string>(1, "alma") },
                new BitArray[]{ new BitArray(new[] {true, false, true}), null },
                new StringBuilder[] { new StringBuilder("alma"), null },
            };

            SystemSerializeObject(referenceObjects);
            SystemSerializeObjects(referenceObjects);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.None); // 501 -> 520
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None);

            referenceObjects = new object[]
            {
                new BitVector32[] { new BitVector32(13) },
                new BitVector32.Section[] { BitVector32.CreateSection(13) },
            };

            KGySerializeObject(referenceObjects, BinarySerializationOptions.None); // 23
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None);
        }

        /// <summary>
        /// Enum types must be described explicitly
        /// </summary>
        [TestMethod]
        public void SerializeEnumArrays()
        {
            object[] referenceObjects = 
            {
                new TestEnumByte[] { TestEnumByte.One, TestEnumByte.Two }, // single enum array
                new TestEnumByte[,] { {TestEnumByte.One}, {TestEnumByte.Two} }, // multidimensional enum array
                new TestEnumByte[][] { new TestEnumByte[] {TestEnumByte.One}, new TestEnumByte[] {TestEnumByte.Two} }, // jagged enum array

                new object[] { TestEnumByte.One, null }, // - 130
                new IConvertible[] { TestEnumByte.One, null }, // - 165 -> 153
                new Enum[] { TestEnumByte.One, null }, // - 157 -> 145
                new ValueType[] { TestEnumByte.One, null }, // - 162 -> 150
            };

            SystemSerializeObject(referenceObjects);
            //SystemSerializeObjects(referenceObjects); // System serializer fails with IConvertible is not serializable

            KGySerializeObject(referenceObjects, BinarySerializationOptions.None); // 267 -> 260
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.OmitAssemblyQualifiedNames); // 198
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.OmitAssemblyQualifiedNames);
        }

        /// <summary>
        /// String has variable length and can be null.
        /// </summary>
        [TestMethod]
        public void SerializeStringArrays()
        {
            object[] referenceObjects = 
            {
                new string[] { "Egy", "Kettő" }, // single string array
                new string[,] { {"Egy", "Kettő"}, {"One", "Two"} }, // multidimensional string array
                new string[][] { new string[] {"Egy", "Kettő", "Három"}, new string[] {"One", "Two", null}, null }, // jagged string array with null values (first null as string, second null as array)
            };

            SystemSerializeObject(referenceObjects);
            SystemSerializeObjects(referenceObjects);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.None); // 100 -> 74
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None);

            referenceObjects = new object[]
            {
                // system serializer fails: cannot cast string[*] to object[]
                Array.CreateInstance(typeof(string), new int[] {3}, new int[]{-1}) // array with -1..1 index interval
            };

            KGySerializeObject(referenceObjects, BinarySerializationOptions.None); // 17 -> 19
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None);
        }

        [TestMethod]
        public void SerializeComplexArrays()
        {
            //Debugger.Launch();
            object[] referenceObjects =
            {
                new BinarySerializableStruct[] { new BinarySerializableStruct{IntProp = 1, StringProp = "alma"}, new BinarySerializableStruct{IntProp = 2, StringProp = "béka"} }, // array of a BinarySerializable struct - None: 161
                new BinarySerializableClass[] {new BinarySerializableClass {IntProp = 1, StringProp = "alma"}, new BinarySerializableClass{IntProp = 2, StringProp = "béka"} }, // array of a BinarySerializable non sealed class - None: 170 
                new BinarySerializableClass[] {new BinarySerializableSealedClass(1, "alma"), new BinarySerializableSealedClass(2, "béka") }, // array of a BinarySerializable non sealed class with derived elements - None: 240
                new BinarySerializableSealedClass[] { new BinarySerializableSealedClass(1, "alma"), new BinarySerializableSealedClass(2, "béka"), new BinarySerializableSealedClass(3, "cica") }, // array of a BinarySerializable sealed class - None: 189
                new SystemSerializableClass[] { new SystemSerializableClass{IntProp = 1, StringProp = "alma"}, new SystemSerializableSealedClass{IntProp = 2, StringProp = "béka"} }, // array of a [Serializable] object - None: 419
                new SystemSerializableStruct[] { new SystemSerializableStruct{ IntProp = 1, StringProp = "alma" }, new SystemSerializableStruct { IntProp = 2, StringProp = "béka" } }, // None: 276 -> 271
                new AbstractClass[] { new SystemSerializableClass{IntProp = 1, StringProp = "alma"}, new SystemSerializableSealedClass{IntProp = 2, StringProp = "béka"} }, // array of a [Serializable] object - None: 467 -> 469
                new AbstractClass[] { new BinarySerializableClass{IntProp = 1, StringProp = "alma"}, new SystemSerializableSealedClass{IntProp = 2, StringProp = "béka"} }, // array of a [Serializable] object, with an IBinarySerializable element - 458 -> 393

                new KeyValuePair<int, object>[] { new KeyValuePair<int, object>(1, "alma"), new KeyValuePair<int, object>(2, new TestEnumByte[] { TestEnumByte.One, TestEnumByte.Two }),  }, // None: 151
                new KeyValuePair<int, CustomSerializedClass>[] { new KeyValuePair<int, CustomSerializedClass>(1, new CustomSerializedClass {Bool = true, Name = "alma" }), new KeyValuePair<int, CustomSerializedClass>(2, null) }, // None: 341
            };

            SystemSerializeObject(referenceObjects);
            SystemSerializeObjects(referenceObjects);

            CheckTestingFramework(); // late ctor invoke
            KGySerializeObject(referenceObjects, BinarySerializationOptions.None); // 1760 -> 1738
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.OmitAssemblyQualifiedNames); // 1691
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.OmitAssemblyQualifiedNames);

            referenceObjects = new object[]
            {
                new SystemSerializableClass[] { new SystemSerializableClass{IntProp = 1, StringProp = "alma"}, new SystemSerializableSealedClass{IntProp = 2, StringProp = "béka"}, new NonSerializableClassWithSerializableBase(3, "cica") }, // a non serializable element among te serializable ones - 660/664/595
                new NonSerializableClass[] { new NonSerializableClass { IntProp = 1, StringProp = "alma"}, new NonSerializableSealedClass(1, "béka") { IntProp = 3, StringProp = "cica" } } , // 411/414/345
                new NonSerializableSealedClass[] { new NonSerializableSealedClass(1, "alma") { IntProp = 2, StringProp = "béka" }, null } , // 280/281/212
                new IBinarySerializable[] {new BinarySerializableStruct { IntProp = 1, StringProp = "alma"}, new BinarySerializableClass {IntProp = 2, StringProp = "béka"}, new BinarySerializableSealedClass(3, "cica") }, // IBinarySerializable array - 316/317/248
                new IBinarySerializable[][] {new IBinarySerializable[] {new BinarySerializableStruct { IntProp = 1, StringProp = "alma"}}, null }, // IBinarySerializable array - 160/161/92
                new NonSerializableStruct[] { new NonSerializableStruct { IntProp = 1, Str10 = "alma", Bytes3 = new byte[] {1, 2, 3}}, new NonSerializableStruct{IntProp = 2, Str10 = "béka", Bytes3 = new byte[] {3, 2, 1}} }, // array custom struct - 254/178/109

                new ValueType[] { new BinarySerializableStruct{ IntProp = 1, StringProp = "alma"}, new SystemSerializableStruct {IntProp = 2, StringProp = "béka"}, null, 1}, // - 309/312/243
                new IConvertible[] { null, 1 }, // - 33/34/34
                new IConvertible[][] { null, new IConvertible[]{ null, 1},  }, // - 56 -> 40/41/41
            };

            KGySerializeObject(referenceObjects, BinarySerializationOptions.RecursiveSerializationAsFallback); // 1849
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.RecursiveSerializationAsFallback);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.RecursiveSerializationAsFallback | BinarySerializationOptions.CompactSerializationOfStructures); // 1788
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.RecursiveSerializationAsFallback | BinarySerializationOptions.CompactSerializationOfStructures);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.RecursiveSerializationAsFallback | BinarySerializationOptions.CompactSerializationOfStructures | BinarySerializationOptions.OmitAssemblyQualifiedNames); // 1719
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.RecursiveSerializationAsFallback | BinarySerializationOptions.CompactSerializationOfStructures | BinarySerializationOptions.OmitAssemblyQualifiedNames);
        }

        [TestMethod]
        public void SerializeNullableArrays()
        {
            object[] referenceObjects = 
            {
                new bool?[] { true, false, null }, // 10
                new sbyte?[] { 1, 2, null }, // 10
                new byte?[] { 1, 2, null }, // 10
                new short?[] { 1, 2, null }, // 12
                new ushort?[] { 1, 2, null }, //12
                new int?[] { 1, 2, null }, // -> 16
                new uint?[] { 1, 2, null }, // 16
                new long?[] { 1, 2, null }, // 24
                new ulong?[] { 1, 2, null }, // 24
                new char?[] { 'a', /*Char.ConvertFromUtf32(0x1D161)[0],*/ null }, // 9
                new float?[] { 1, 2, null }, // 16
                new double?[] { 1, 2, null }, // 24
                new decimal?[] { 1, 2, null }, // 40
                new DateTime?[] { DateTime.UtcNow, DateTime.Now, null }, // 26
                new IntPtr?[] { new IntPtr(1), IntPtr.Zero, null }, // 24
                new UIntPtr?[] { new UIntPtr(1), UIntPtr.Zero, null }, // 24
                new Guid?[] { new Guid("ca761232ed4211cebacd00aa0057b223"), Guid.NewGuid(), null }, // 40
                new TimeSpan?[] { new TimeSpan(1, 1, 1), new TimeSpan(DateTime.UtcNow.Ticks), null }, // 24
                new DateTimeOffset?[] { new DateTimeOffset(DateTime.Now), new DateTimeOffset(DateTime.UtcNow), new DateTimeOffset(DateTime.Now.Ticks, new TimeSpan(1, 1, 0)), null }, // 39

                new TestEnumByte?[] { TestEnumByte.One, TestEnumByte.Two, null }, // 130

                new DictionaryEntry?[] { new DictionaryEntry(1, "alma"), null}, // 21
                new KeyValuePair<int, string>?[] { new KeyValuePair<int,string>(1, "alma"), null}, // 21
                new KeyValuePair<int?, int?>?[] { new KeyValuePair<int?,int?>(1, 2), new KeyValuePair<int?,int?>(2, null), null}, // 28

                new BinarySerializableStruct?[] { new BinarySerializableStruct{IntProp = 1, StringProp = "alma"}, null }, // 151
                new SystemSerializableStruct?[] { new SystemSerializableStruct{IntProp = 1, StringProp = "alma"}, null }, // 206
            };
            SystemSerializeObject(referenceObjects);
            SystemSerializeObjects(referenceObjects);

            CheckTestingFramework(); // late ctor invoke
            KGySerializeObject(referenceObjects, BinarySerializationOptions.None); // 824 -> 841
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None);

            referenceObjects = new object[]
            {
                new NonSerializableStruct?[] { new NonSerializableStruct{ Bytes3 = new byte[] {1,2,3}, IntProp = 10, Str10 = "alma"}, null }, // 195/159/90
                new BitVector32?[] { new BitVector32(13), null }, // 11/11/11
                new BitVector32.Section?[] { BitVector32.CreateSection(13), null }, // 11/11/11
            };

            KGySerializeObject(referenceObjects, BinarySerializationOptions.RecursiveSerializationAsFallback | BinarySerializationOptions.IgnoreIBinarySerializable); // 223 -> 229
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.RecursiveSerializationAsFallback | BinarySerializationOptions.IgnoreIBinarySerializable);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.CompactSerializationOfStructures); // 186
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.CompactSerializationOfStructures);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.CompactSerializationOfStructures | BinarySerializationOptions.OmitAssemblyQualifiedNames); // 117
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.CompactSerializationOfStructures | BinarySerializationOptions.OmitAssemblyQualifiedNames);
        }

        [TestMethod]
        public void SerializeSimpleGenericCollections()
        {
            object[] referenceObjects =
                {
                    new List<int> { 1, 2, 3 }, // 22 -> 7 -> 16
                    new List<int[]> { new int[]{1, 2, 3}, null },

                    new LinkedList<int>(new[]{ 1, 2, 3}),
                    new LinkedList<int[]>(new int[][]{new int[]{1, 2, 3}, null}),

                    new HashSet<int> { 1, 2, 3},
                    new HashSet<int[]> { new int[]{1, 2, 3}, null },
                    new HashSet<string>(StringComparer.CurrentCulture) { "alma", "Alma", "ALMA" },
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alma", "Alma", "ALMA" },
                    new HashSet<TestEnumByte>(EnumComparer<TestEnumByte>.Comparer) { TestEnumByte.One, TestEnumByte.Two },

                    new Queue<int>(new[]{ 1, 2, 3}),
                    new Queue<int[]>(new int[][]{new int[]{1, 2, 3}, null}),

                    new Stack<int>(new[]{ 1, 2, 3}),
                    new Stack<int[]>(new int[][]{new int[]{1, 2, 3}, null}),

                    new CircularList<int>(new[]{ 1, 2, 3}),
                    new CircularList<int[]>(new int[][]{new int[]{1, 2, 3}, null}),

#if NET40 || NET45
                    new SortedSet<int>(new[]{ 1, 2, 3}),
                    new SortedSet<int[]>(new int[][]{new int[]{1, 2, 3}, null}),
                    new SortedSet<string>(StringComparer.CurrentCulture) { "alma", "Alma", "ALMA" },
                    new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "alma", "Alma", "ALMA" },
#elif !NET35
#error .NET version is not set or not supported!
#endif

                    new Dictionary<int, string> { {1, "alma"}, {2, "béka"}, {3, "cica"}},
                    new Dictionary<int, TestEnumByte> { {1, TestEnumByte.One}, {2, TestEnumByte.Two}},
                    new Dictionary<int[], string[]> { {new int[]{1}, new string[] {"alma"}}, {new int[]{2}, null}},
                    new Dictionary<string, int>(StringComparer.CurrentCulture) { {"alma", 1}, {"Alma", 2}, {"ALMA", 3}},
                    new Dictionary<TestEnumByte, int>(EnumComparer<TestEnumByte>.Comparer) { {TestEnumByte.One, 1}, {TestEnumByte.Two, 2}},

                    new SortedList<int, string> { {1, "alma"}, {2, "béka"}, {3, "cica"}},
                    new SortedList<int, string[]> { {1, new string[] {"alma"}}, {2 , null}},
                    new SortedList<string, int>(StringComparer.CurrentCulture) { {"kerek", 1}, {"kerék", 2}, {"keres", 3}, {"kérés", 4}},
                    new SortedList<string, int>(StringComparer.OrdinalIgnoreCase) { {"kerek", 1}, {"kerék", 2}, {"keres", 3}, {"kérés", 4}},
                    new SortedList<TestEnumByte, int>(Comparer<TestEnumByte>.Default) { {TestEnumByte.One, 1}, {TestEnumByte.Two, 2}},
                    new SortedList<TestEnumByte, int>(EnumComparer<TestEnumByte>.Comparer) { {TestEnumByte.One, 1}, {TestEnumByte.Two, 2}},

                    new SortedDictionary<int, string> { {1, "alma"}, {2, "béka"}, {3, "cica"}},
                    new SortedDictionary<int, string[]> { {1, new string[] {"alma"}}, {2 , null}},
                    new SortedDictionary<string, int>(StringComparer.CurrentCulture) { {"kerek", 1}, {"kerék", 2}, {"keres", 3}, {"kérés", 4}},
                    new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase) { {"kerek", 1}, {"kerék", 2}, {"keres", 3}, {"kérés", 4}},
                    new SortedDictionary<TestEnumByte, int>(Comparer<TestEnumByte>.Default) { {TestEnumByte.One, 1}, {TestEnumByte.Two, 2}},
                    new SortedDictionary<TestEnumByte, int>(EnumComparer<TestEnumByte>.Comparer) { {TestEnumByte.One, 1}, {TestEnumByte.Two, 2}},

                    new CircularSortedList<int, string> { {1, "alma"}, {2, "béka"}, {3, "cica"}},
                    new CircularSortedList<int, string[]> { {1, new string[] {"alma"}}, {2 , null}},
                    new CircularSortedList<string, int>(StringComparer.CurrentCulture) { {"kerek", 1}, {"kerék", 2}, {"keres", 3}, {"kérés", 4}},
                    new CircularSortedList<string, int>(StringComparer.OrdinalIgnoreCase) { {"kerek", 1}, {"kerék", 2}, {"keres", 3}, {"kérés", 4}},
                    new CircularSortedList<TestEnumByte, int>(Comparer<TestEnumByte>.Default) { {TestEnumByte.One, 1}, {TestEnumByte.Two, 2}},
                    new CircularSortedList<TestEnumByte, int>(EnumComparer<TestEnumByte>.Comparer) { {TestEnumByte.One, 1}, {TestEnumByte.Two, 2}},
                };

            SystemSerializeObject(referenceObjects);
            SystemSerializeObjects(referenceObjects);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.None); // 1986
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None);
        }

        [TestMethod]
        public void SerializeSimpleNonGenericCollections()
        {
            object[] referenceObjects =
                {
                    new ArrayList { 1, "alma", DateTime.Now }, // 34 -> 25

                    new Hashtable { {1, "alma"}, { (byte)2, "béka"}, {3m, "cica"} },
                    new Hashtable(StringComparer.CurrentCulture) { {"alma", 1}, {"Alma", 2}, {"ALMA", 3}},

                    new Queue(new object[]{ 1, (byte)2, 3m, new string[]{"alma", "béka", "cica"} }),

                    new Stack(new object[]{ 1, (byte)2, 3m, new string[]{"alma", "béka", "cica"} }),

                    new StringCollection{ "alma", "béka", "cica" },

                    new SortedList{ {1, "alma"}, {2, "béka"}, {3, "cica"}},
                    new SortedList(StringComparer.CurrentCulture) { {"kerek", 1}, {"kerék", 2}, {"keres", 3}, {"kérés", 4}},
                    new SortedList(StringComparer.OrdinalIgnoreCase) { {"kerek", 1}, {"kerék", 2}, {"keres", 3}, {"kérés", 4}},

                    new ListDictionary{ {1, "alma"}, {2, "béka"}, {3, "cica"}},
                    new ListDictionary(StringComparer.CurrentCulture) { {"kerek", 1}, {"kerék", 2}, {"keres", 3}, {"kérés", 4}},
                    new ListDictionary(StringComparer.OrdinalIgnoreCase) { {"kerek", 1}, {"kerék", 2}, {"keres", 3}, {"kérés", 4}},

                    new HybridDictionary(false) { {"alma", 1}, {"Alma", 2}, {"ALMA", 3}},

                    new OrderedDictionary { {"alma", 1}, {"Alma", 2}, {"ALMA", 3}},
                    new OrderedDictionary { {"alma", 1}, {"Alma", 2}, {"ALMA", 3}}.AsReadOnly(),
                    new OrderedDictionary(StringComparer.OrdinalIgnoreCase) { {"alma", 1}, {"béka", 2}, {"cica", 3}},

                    new StringDictionary{ {"a", "alma"}, {"b", "béka"}, {"c", "cica"}, {"x", null} },
                };

            SystemSerializeObject(referenceObjects);
            SystemSerializeObjects(referenceObjects);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.None); // 1714 -> 1171
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None);
        }

        [TestMethod]
        public void SerializeRecursiveCollections()
        {
            object[] referenceObjects =
                {
                    new Collection<int> { 1, 2, 3 }, // -> 77/77
                    new Collection<int[]> { new int[]{1, 2, 3}, null }, // -> 85/85
                    new Collection<ReadOnlyCollection<int>>(new Collection<ReadOnlyCollection<int>>{new ReadOnlyCollection<int>(new int[]{ 1, 2, 3})}), // -> 166/166
                    new Collection<BinarySerializableStruct> { new BinarySerializableStruct{IntProp = 1, StringProp = "alma"}, default(BinarySerializableStruct) }, // -> 214/145
                    new Collection<SystemSerializableClass> { new SystemSerializableClass { Bool = null, IntProp = 1, StringProp = "alma" }, new SystemSerializableSealedClass { Bool = true, IntProp = 2, StringProp = "béka" }, null}, // -> 481/412
                    
                    // collections of keyvalue pairs (as object and strongly typed as well)
                    new Collection<object> { new KeyValuePair<int, object>(1, "alma"), new KeyValuePair<int, object>(2, DateTime.Now), new KeyValuePair<int, object>(3, new object()), new KeyValuePair<int, object>(4, new object[] {1, "alma", DateTime.Now, null}), new KeyValuePair<int, object>(5, null) }, // -> 155/155
                    new Collection<KeyValuePair<int, object>> { new KeyValuePair<int, object>(1, "alma"), new KeyValuePair<int, object>(2, DateTime.Now), new KeyValuePair<int, object>(3, new object()), new KeyValuePair<int, object>(4, new object[] {1, "alma", DateTime.Now, null}), new KeyValuePair<int, object>(5, null) } , // -> 141/151

                    new ReadOnlyCollection<int>(new int[]{ 1, 2, 3}), // -> 85/85
                    new ReadOnlyCollection<int[]>(new int[][]{new int[]{1, 2, 3}, null}), // -> 93/93

                    new CustomNonGenericCollection { "alma", 2, null }, // -> 198/129
                    new CustomNonGenericDictionary { { "alma", 2 }, { "béka", null } }, // -> 328/259
                    new CustomGenericCollection<int> { 1, 2, 3 }, // -> 199/130
                    new CustomGenericDictionary<int, string> { { 1, "alma" }, { 2, null } }, // -> 334/265

                    new CustomGenericDictionary<TestEnumByte, CustomSerializedClass> { {TestEnumByte.One, new CustomSerializedClass { Name = "alma"}} }, // -> 618/549
                };

            SystemSerializeObject(referenceObjects);
            SystemSerializeObjects(referenceObjects);

            CheckTestingFramework(); // late ctor invoke
            KGySerializeObject(referenceObjects, BinarySerializationOptions.None); // 2241
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.OmitAssemblyQualifiedNames); // 2172
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.OmitAssemblyQualifiedNames);
        }

        /// <summary>
        /// Test of GetDictionaryValueTypes
        /// </summary>
        [TestMethod]
        public void SerializeSupportedDictionaryValues()
        {
            object[] referenceObjects =
                {
                    // generic collection value
                    new Dictionary<int, int[]>{{1, new[]{1, 2}}, {2, null}}, // array
                    new Dictionary<int, List<int>>{{1, new List<int>{1, 2}}, {2, null}}, // List
                    new Dictionary<int, LinkedList<int>>{{1, new LinkedList<int>(new[]{1,2})}, {2, null}}, // LinkedList
                    new Dictionary<int, HashSet<int>>{{1, new HashSet<int>{1, 2}}, {2, null}}, // HashSet
                    new Dictionary<int, Queue<int>>{{1, new Queue<int>(new[]{1,2})}, {2, null}}, // Queue
                    new Dictionary<int, Stack<int>>{{1, new Stack<int>(new[]{1,2})}, {2, null}}, // Stack
                    new Dictionary<int, CircularList<int>>{{1, new CircularList<int>{1, 2}}, {2, null}}, // CircularList
#if NET40 || NET45
                    new Dictionary<int, SortedSet<int>>{{1, new SortedSet<int>{1, 2}}, {2, null}}, // SortedSet
#elif !NET35
#error .NET version is not set or not supported!
#endif

                    // generic dictionary value
                    new Dictionary<int, Dictionary<int, int>>{{1, new Dictionary<int, int>{{1, 2}}}, {2, null}}, // Dictionary
                    new Dictionary<int, SortedList<int, int>>{{1, new SortedList<int, int>{{1, 2}}}, {2, null}}, // SortedList
                    new Dictionary<int, SortedDictionary<int, int>>{{1, new SortedDictionary<int, int>{{1, 2}}}, {2, null}}, // SortedDictionary
                    new Dictionary<int, KeyValuePair<int, int>>{{1, new KeyValuePair<int, int>(1, 2)}}, // KeyValuePair
                    new Dictionary<int, KeyValuePair<int, int>?>{{1, new KeyValuePair<int, int>(1, 2)}, {2, null}}, // KeyValuePair?
                    new Dictionary<int, CircularSortedList<int, int>>{{1, new CircularSortedList<int, int>{{1, 2}}}, {2, null}}, // CircularSortedList

                    // non-generic collection value
                    new Dictionary<int, ArrayList>{{1, new ArrayList{1, 2}}, {2, null}}, // ArrayList
                    new Dictionary<int, Queue>{{1, new Queue(new[]{1, 2})}, {2, null}}, // Queue
                    new Dictionary<int, Stack>{{1, new Stack(new[]{1, 2})}, {2, null}}, // Stack
                    new Dictionary<int, StringCollection>{{1, new StringCollection()}, {2, null}}, // StringCollection
                    
                    // non-generic dictionary value
                    new Dictionary<int, Hashtable>{{1, new Hashtable{{1, 2}}}, {2, null}}, // Hashtable
                    new Dictionary<int, SortedList>{{1, new SortedList{{1, 2}}}, {2, null}}, // SortedList
                    new Dictionary<int, ListDictionary>{{1, new ListDictionary{{1, 2}}}, {2, null}}, // ListDictionary
                    new Dictionary<int, HybridDictionary>{{1, new HybridDictionary{{1, 2}}}, {2, null}}, // HybridDictionary
                    new Dictionary<int, OrderedDictionary>{{1, new OrderedDictionary{{1, 2}}}, {2, null}}, // OrderedDictionary
                    new Dictionary<int, StringDictionary>{{1, new StringDictionary{{"1", "2"}}}, {2, null}}, // StringDictionary
                    new Dictionary<int, DictionaryEntry>{{1, new DictionaryEntry(1, 2)}}, // DictionaryEntry
                    new Dictionary<int, DictionaryEntry?>{{1, new DictionaryEntry(1, 2)}, {2, null}}, // DictionaryEntry?

                    // non-natively supported value: recursive
                    new Dictionary<int, Collection<int>>{{1, new Collection<int>{1, 2}}, {2, null}}, // Collection
                    new Dictionary<int, ReadOnlyCollection<int>>{{1, new ReadOnlyCollection<int>(new[]{1, 2})}, {2, null}}, // ReadOnlyCollection

                    // other generic dictionary types as outer objects
                    new SortedList<int, int[]>{{1, new[]{1, 2}}, {2, null}},
                    new SortedDictionary<int, int[]>{{1, new[]{1, 2}}, {2, null}},
                    new KeyValuePair<int, int[]>(1, new[]{1, 2}),
                    new CircularSortedList<int, int[]>{{1, new[]{1, 2}}, {2, null}},
                };

            SystemSerializeObject(referenceObjects);
            SystemSerializeObjects(referenceObjects);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.None); // 954
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None);
        }

        [TestMethod]
        public void SerializeComplexGenericCollections()
        {
            object[] referenceObjects =
                {
                    new List<byte>[] { new List<byte>{ 11, 12, 13}, new List<byte>{21, 22} }, // array of lists
                    new List<byte[]> { new byte[]{ 11, 12, 13}, new byte[] {21, 22} }, // list of arrays

                    // a single key-value pair with a dictionary somewhere in value
                    new KeyValuePair<int[], KeyValuePair<string, Dictionary<string, string>>>(new int[1], new KeyValuePair<string, Dictionary<string, string>>("cica", new Dictionary<string, string>{{"alma", "béka"}})),

                    // dictionary with dictionary<int, string> value
                    new Dictionary<string, Dictionary<int, string>> { { "hu", new Dictionary<int, string>{ {1, "alma"}, {2, "béka"}, {3, "cica"}}}, {"en", new Dictionary<int, string>{ {1, "apple"}, {2, "frog"}, {3, "cat"}}} },

                    // dictionary with dictionary<int, IBinarySerializable> value
                    new Dictionary<string, Dictionary<int, IBinarySerializable>> { { "alma", new Dictionary<int, IBinarySerializable>{ {1, null}, {2, new BinarySerializableClass{IntProp = 2, StringProp = "béka"}}, {3, new BinarySerializableStruct{IntProp = 3, StringProp = "cica"}}}}, {"en", null} },

                    // dictionary with array key
                    new Dictionary<string[], Dictionary<int, string>> { { new string[] {"hu"}, new Dictionary<int, string>{ {1, "alma"}, {2, "béka"}, {3, "cica"}}}, {new string[] {"en"}, new Dictionary<int, string>{ {1, "apple"}, {2, "frog"}, {3, "cat"}}} },

                    // dictionary with dictionary key and value
                    new Dictionary<Dictionary<int[], string>, Dictionary<int, string>> { { new Dictionary<int[], string>{{new int[] {1}, "key.value1"}}, new Dictionary<int, string>{ {1, "alma"}, {2, "béka"}, {3, "cica"}}}, {new Dictionary<int[], string>{{new int[] {2}, "key.value2"}}, new Dictionary<int, string>{ {1, "apple"}, {2, "frog"}, {3, "cat"}}} },

                    // dictionary with many non-system types
                    new SortedList<ConsoleColor, Dictionary<BinarySerializationOptions, IBinarySerializable>> { { ConsoleColor.White, new Dictionary<BinarySerializationOptions, IBinarySerializable>{{BinarySerializationOptions.ForcedSerializationValueTypesAsFallback, new BinarySerializableStruct{IntProp = 1, StringProp = "alma"}} }} },

                    // object list vith various elements
                    new List<object> { 1, "alma", new Version(13,0), new SystemSerializableClass{IntProp = 2, StringProp = "béka" }, new object[]{ new BinarySerializableClass{IntProp = 3, StringProp = "cica"}}},

                    // dictionary with object key and value
                    new Dictionary<object, object> { {1, "alma"}, {new object(), "béka"}, {new int[] {3, 4}, null}, { TestEnumByte.One, new BinarySerializableStruct{IntProp = 13, StringProp = "cica"} }},

                    // dictionary with read-only collection value
                    new Dictionary<object, ReadOnlyCollection<int>> { {1, new ReadOnlyCollection<int>(new[]{1, 2})}},

                    // lists with binary serializable elements
                    new List<BinarySerializableStruct> { new BinarySerializableStruct{IntProp = 1, StringProp = "alma"}, default(BinarySerializableStruct) },
                    new List<BinarySerializableStruct?> { new BinarySerializableStruct{IntProp = 1, StringProp = "alma"}, default(BinarySerializableStruct?) },
                    new List<BinarySerializableClass> { new BinarySerializableClass {IntProp = 1, StringProp = "alma"}, new BinarySerializableSealedClass(2, "béka"), null },
                    new List<BinarySerializableSealedClass> { new BinarySerializableSealedClass(1, "alma"), null },
                    new List<IBinarySerializable> { new BinarySerializableClass {IntProp = 1, StringProp = "alma"}, new BinarySerializableSealedClass(2, "béka"), new BinarySerializableStruct{IntProp = 3, StringProp = "cica"}, null },

                    // lists with default recursive elements
                    new List<SystemSerializableStruct> { new SystemSerializableStruct{IntProp = 1, StringProp = "alma"}, default(SystemSerializableStruct) },
                    new List<SystemSerializableStruct?> { new SystemSerializableStruct{IntProp = 1, StringProp = "alma"}, default(SystemSerializableStruct?) },
                    new List<SystemSerializableClass> { new SystemSerializableClass {IntProp = 1, StringProp = "alma"}, new SystemSerializableSealedClass {IntProp = 2, StringProp = "béka"}, null },
                    new List<SystemSerializableSealedClass> { new SystemSerializableSealedClass {IntProp = 1, StringProp = "alma"}, null },

                    // lists with custom recursive elements
                    new List<CustomSerializableStruct> { new CustomSerializableStruct{IntProp = 1, StringProp = "alma"}, default(CustomSerializableStruct) },
                    new List<CustomSerializableStruct?> { new CustomSerializableStruct{IntProp = 1, StringProp = "alma"}, default(CustomSerializableStruct?) },
                    new List<CustomSerializedClass> { new CustomSerializedClass{ Name = "alma", Bool = true }, new CustomSerializedSealedClass("béka") { Bool = null }, null },
                    new List<CustomSerializedSealedClass> { new CustomSerializedSealedClass("alma") { Bool = false }, null },

                    new IList<int>[] { new int[]{1, 2, 3}, new List<int>{1, 2, 3}},
                    new List<IList<int>> { new int[]{1, 2, 3}, new List<int>{1, 2, 3} } 
                };

            SystemSerializeObject(referenceObjects);
            //SystemSerializeObjects(referenceObjects); // System deserialization fails at List<IBinarySerializable>: IBinarySerializable/IList is not marked as serializable.

            CheckTestingFramework(); // late ctor invoke
            KGySerializeObject(referenceObjects, BinarySerializationOptions.None); // 3666 -> 2943
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None);
        }

        [TestMethod]
        public void SerializeCache()
        {
            object[] referenceObjects =
                {
                    new Cache<int, string> { {1, "alma"}, {2, "béka"}, {3, "cica"}},
                    new Cache<int[], string[]> { {new int[]{1}, new string[] {"alma"}}, {new int[]{2}, null}},
                    new Cache<string, int>(StringComparer.CurrentCulture) { {"alma", 1}, {"Alma", 2}, {"ALMA", 3}},
                    new Cache<TestEnumByte, int> { {TestEnumByte.One, 1}, {TestEnumByte.Two, 2}},
                    new Cache<string, string>(s => s.ToUpper()) { {"alma", "ALMA"}},
                };

            SystemSerializeObject(referenceObjects);
            SystemSerializeObjects(referenceObjects);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.None); // 2147
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None);
        }

        [TestMethod]
        public void SerializeMarshalByRefObjects()
        {
            Evidence evidence = new Evidence(AppDomain.CurrentDomain.Evidence);
            AppDomain domain = AppDomain.CreateDomain("TestDomain", evidence, AppDomain.CurrentDomain.BaseDirectory, null, false);
            try
            {
                object[] referenceObjects =
                {
                    new MemoryStreamWithEquals(), // local
                    domain.CreateInstanceAndUnwrap(Assembly.GetExecutingAssembly().FullName, typeof(MemoryStreamWithEquals).FullName) // remote
                };

                // default - does not work for remote objects
                //try
                //{
                //    SystemSerializeObjects(referenceObjects);
                //    KGySerializeObjects(referenceObjects, BinarySerializationOptions.RecursiveSerializationAsFallback);
                //}
                //catch
                //{
                //}

                // with surrogate (deserialization: default again because RemotingSurrogateSelector does not support SetObjectData)
                Console.WriteLine("--------------------------------Serialization with RemotingSurrogateSelector---------------------------------------");
                ISurrogateSelector surrogate = new RemotingSurrogateSelector();
                BinaryFormatter bf = new BinaryFormatter();
                BinarySerializationFormatter bsf = new BinarySerializationFormatter(BinarySerializationOptions.RecursiveSerializationAsFallback);

                Console.WriteLine("------------------System Binaryformatter (Items Count: {0})--------------------", referenceObjects.Length);
                bf.SurrogateSelector = surrogate;
                byte[] raw = SerializeObjects(referenceObjects, bf); // 1097
                bf.SurrogateSelector = null;
                object[] result = DeserializeObjects(raw, bf);
                AssertItemsEqual(referenceObjects, result);

                Console.WriteLine("------------------KGySoft BinarySerializer (Items Count: {0}; Options: {1})--------------------", referenceObjects.Length, bsf.Options);
                bsf.SurrogateSelector = surrogate;
                raw = SerializeObjects(referenceObjects, bsf); // 1017
                bsf.SurrogateSelector = null;
                result = DeserializeObjects(raw, bsf);
                AssertItemsEqual(referenceObjects, result);
            }
            finally
            {
                AppDomain.Unload(domain);
            }
        }

        [TestMethod]
        public void SerializationBinderTest()
        {
            object[] referenceObjects =
                {
                    1, // primitive type
                    new StringBuilder("1"), // natively supported by KGySoft only
                    new List<int>{1}, // generic, natively supported for KGySoft only, in mscorlib
                    new HashSet<int>{1}, // generic, natively supported for KGySoft only, in core
                    TestEnumByte.One, // non standard assembly
                    new CustomGenericCollection<TestEnumByte> { TestEnumByte.One, TestEnumByte.Two },
                    new CustomGenericDictionary<TestEnumByte, CustomSerializedClass> { {TestEnumByte.One, new CustomSerializedClass { Name = "alma"}} },
                    // new CustomSerializedSealedClass("1"), // type is changed on serialization: System BF fail: the binder gets the original type instead of the changed one
                };

            // default
            SystemSerializeObjects(referenceObjects);
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.RecursiveSerializationAsFallback);

            // with WeakAssemblySerializationBinder
            Console.WriteLine("--------------------------------Deserialization with WeakAssemblySerializationBinder---------------------------------------");
            SerializationBinder binder = new WeakAssemblySerializationBinder();
            BinaryFormatter bf = new BinaryFormatter();
            BinarySerializationFormatter bsf = new BinarySerializationFormatter(BinarySerializationOptions.RecursiveSerializationAsFallback);
            bf.Binder = binder;
            bsf.Binder = binder;

            Console.WriteLine("------------------System Binaryformatter (Items Count: {0})--------------------", referenceObjects.Length);
            byte[] raw = SerializeObjects(referenceObjects, bf);
            object[] result = DeserializeObjects(raw, bf);
            AssertItemsEqual(referenceObjects, result);

            Console.WriteLine("------------------KGySoft BinarySerializer (Items Count: {0}; Options: {1})--------------------", referenceObjects.Length, bsf.Options);
            raw = SerializeObjects(referenceObjects, bsf);
            result = DeserializeObjects(raw, bsf);
            AssertItemsEqual(referenceObjects, result);

#if NET40 || NET45
            Console.WriteLine("-------Serialization and deserialization with WeakAssemblySerializationBinder, OmitAssemblyNameOnSerialize enabled-------------");
            Console.WriteLine("------------------System Binaryformatter (Items Count: {0})--------------------", referenceObjects.Length);
            binder = new WeakAssemblySerializationBinder { OmitAssemblyNameOnSerialize = true };
            bf.Binder = binder;
            bsf.Binder = binder;
            raw = SerializeObjects(referenceObjects, bf);
            result = DeserializeObjects(raw, bf);
            AssertItemsEqual(referenceObjects, result);

            Console.WriteLine("------------------KGySoft BinarySerializer (Items Count: {0}; Options: {1})--------------------", referenceObjects.Length, bsf.Options);
            raw = SerializeObjects(referenceObjects, bsf);
            result = DeserializeObjects(raw, bsf);
            AssertItemsEqual(referenceObjects, result);

            bsf.Options |= BinarySerializationOptions.OmitAssemblyQualifiedNames;
            Console.WriteLine("------------------KGySoft BinarySerializer (Items Count: {0}; Options: {1})--------------------", referenceObjects.Length, bsf.Options);
            raw = SerializeObjects(referenceObjects, bsf);
            result = DeserializeObjects(raw, bsf);
            AssertItemsEqual(referenceObjects, result);

            // with TestSerializationBinder
            Console.WriteLine("--------------------------------Serialization and deserialization with TestSerializationBinder---------------------------------------");
            binder = new TestSerializationBinder();
            bf = new BinaryFormatter();
            bsf = new BinarySerializationFormatter(BinarySerializationOptions.RecursiveSerializationAsFallback);
            bf.Binder = binder;
            bsf.Binder = binder;

            Console.WriteLine("------------------System Binaryformatter (Items Count: {0})--------------------", referenceObjects.Length);
            raw = SerializeObjects(referenceObjects, bf);
            result = DeserializeObjects(raw, bf);
            AssertItemsEqual(referenceObjects, result);

            Console.WriteLine("------------------KGySoft BinarySerializer (Items Count: {0}; Options: {1})--------------------", referenceObjects.Length, bsf.Options);
            raw = SerializeObjects(referenceObjects, bsf);
            result = DeserializeObjects(raw, bsf);
            AssertItemsEqual(referenceObjects, result);

            bsf.Options |= BinarySerializationOptions.OmitAssemblyQualifiedNames;
            Console.WriteLine("------------------KGySoft BinarySerializer (Items Count: {0}; Options: {1})--------------------", referenceObjects.Length, bsf.Options);
            raw = SerializeObjects(referenceObjects, bsf);
            result = DeserializeObjects(raw, bsf);
            AssertItemsEqual(referenceObjects, result);
#elif !NET35
#error .NET version is not set or not supported!
#endif
        }

        [TestMethod]
        public void SerializationSurrogateTest()
        {
            object[] referenceObjects =
            {
                // simple types
                new object(),
                DBNull.Value,
                true,
                (sbyte)1,
                (byte)1,
                (short)1,
                (ushort)1,
                (int)1,
                (uint)1,
                (long)1,
                (ulong)1,
                'a',
                "alma",
                (float)1,
                (double)1,
                (decimal)1,
                DateTime.UtcNow,
                DateTime.Now,
                new IntPtr(1),
                new UIntPtr(1),
                new Version(1, 2, 3, 4),
                new Guid("ca761232ed4211cebacd00aa0057b223"),
                new TimeSpan(1, 1, 1),
                new DateTimeOffset(DateTime.Now),
                new DateTimeOffset(DateTime.UtcNow),
                new DateTimeOffset(DateTime.Now.Ticks, new TimeSpan(1, 1, 0)),
                new Uri(@"x:\teszt"),
                new DictionaryEntry(1, "alma"),
                new KeyValuePair<int,string>(1, "alma"),
                new BitArray(new[] {true, false, true}),
                new StringBuilder("alma"),

                TestEnumByte.Two,
                new KeyValuePair<int, object>[] { new KeyValuePair<int, object>(1, "alma"), new KeyValuePair<int, object>(2, new TestEnumByte[] { TestEnumByte.One, TestEnumByte.Two }),  },

                // dictionary with any object key and read-only collection value
                new Dictionary<object, ReadOnlyCollection<int>> { {1, new ReadOnlyCollection<int>(new[]{1, 2})}, { new SystemSerializableClass { IntProp = 1, StringProp = "alma" }, null}},

                // nested default recursion
                new Collection<SystemSerializableClass> { new SystemSerializableClass { Bool = null, IntProp = 1, StringProp = "alma" }, new SystemSerializableSealedClass { Bool = true, IntProp = 2, StringProp = "béka" }, null},
                new CustomSerializedClass { Bool = false, Name = "cica" },

                new CustomGenericCollection<TestEnumByte> {TestEnumByte.One, TestEnumByte.Two},
                new CustomGenericDictionary<TestEnumByte, CustomSerializedClass> {{TestEnumByte.One, new CustomSerializedClass { Name = "alma" }}},

                // nullable arrays
                new BinarySerializableStruct?[] { new BinarySerializableStruct{IntProp = 1, StringProp = "alma"}, null },
                new SystemSerializableStruct?[] { new SystemSerializableStruct{IntProp = 1, StringProp = "alma"}, null },

                // lists with binary serializable elements
                new List<BinarySerializableStruct> { new BinarySerializableStruct{IntProp = 1, StringProp = "alma"}, default(BinarySerializableStruct) },
                new List<BinarySerializableStruct?> { new BinarySerializableStruct{IntProp = 1, StringProp = "alma"}, default(BinarySerializableStruct?) },
                new List<BinarySerializableClass> { new BinarySerializableClass {IntProp = 1, StringProp = "alma"}, new BinarySerializableSealedClass(2, "béka"), null },
                new List<BinarySerializableSealedClass> { new BinarySerializableSealedClass(1, "alma"), null },
                new List<IBinarySerializable> { new BinarySerializableClass {IntProp = 1, StringProp = "alma"}, new BinarySerializableSealedClass(2, "béka"), new BinarySerializableStruct{IntProp = 3, StringProp = "cica"}, null },

                // lists with default recursive elements
                new List<SystemSerializableStruct> { new SystemSerializableStruct{IntProp = 1, StringProp = "alma"}, default(SystemSerializableStruct) },
                new List<SystemSerializableStruct?> { new SystemSerializableStruct{IntProp = 1, StringProp = "alma"}, default(SystemSerializableStruct?) },
                new List<SystemSerializableClass> { new SystemSerializableClass {IntProp = 1, StringProp = "alma"}, new SystemSerializableSealedClass {IntProp = 2, StringProp = "béka"}, null },
                new List<SystemSerializableSealedClass> { new SystemSerializableSealedClass {IntProp = 1, StringProp = "alma"}, null },

                // lists with custom recursive elements
                new List<CustomSerializableStruct> { new CustomSerializableStruct{IntProp = 1, StringProp = "alma"}, default(CustomSerializableStruct) },
                new List<CustomSerializableStruct?> { new CustomSerializableStruct{IntProp = 1, StringProp = "alma"}, default(CustomSerializableStruct?) },
                new List<CustomSerializedClass> { new CustomSerializedClass{ Name = "alma", Bool = true }, new CustomSerializedSealedClass("béka") { Bool = null }, null },
                new List<CustomSerializedSealedClass> { new CustomSerializedSealedClass("alma") { Bool = false }, null },

                // collections with native support
                new CircularList<int>{ 1, 2, 3},
#if NET40 || NET45
                new SortedSet<int>{ 1, 2, 3},
#elif !NET35
#error .NET version is not set or not supported!
#endif
                new CircularSortedList<int, int>{ {1, 1}, {2, 2}, {3, 3}},
            };

            // default
            // SystemSerializeObjects(referenceObjects); system serialization fails: IBinarySerializable is not serializable
            CheckTestingFramework(); // late ctor invoke
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None);

            // with SerializationSurrogate
            Console.WriteLine("================================Serialization with NameInvariantSurrogateSelector=======================================");
            ISurrogateSelector selector = new NameInvariantSurrogateSelector();
            BinaryFormatter bf = new BinaryFormatter();
            BinarySerializationFormatter bsf = new BinarySerializationFormatter(BinarySerializationOptions.None);
            bf.SurrogateSelector = selector;
            bsf.SurrogateSelector = selector;
            byte[] raw;
            object[] result;

            Console.WriteLine("------------------System Binaryformatter (Items Count: {0})--------------------", referenceObjects.Length);
            try
            {
                raw = SerializeObjects(referenceObjects, bf);
                // system deserialization fails: Cannot deserialize an abstract class
                result = DeserializeObjects(raw, bf);
                AssertItemsEqual(referenceObjects, result);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error in system serializer: " + e);
            }

            Console.WriteLine("------------------KGySoft BinarySerializer (Items Count: {0}; Options: {1})--------------------", referenceObjects.Length, bsf.Options);
            raw = SerializeObjects(referenceObjects, bsf);
            result = DeserializeObjects(raw, bsf);
            AssertItemsEqual(referenceObjects, result);

            bsf.Options |= BinarySerializationOptions.TryUseSurrogateSelectorForAnyType;
            Console.WriteLine("------------------KGySoft BinarySerializer (Items Count: {0}; Options: {1})--------------------", referenceObjects.Length, bsf.Options);
            raw = SerializeObjects(referenceObjects, bsf);
            result = DeserializeObjects(raw, bsf);
            AssertItemsEqual(referenceObjects, result);
            Console.WriteLine("================================Serialization only with TestSurrogateSelector=======================================");
            selector = new TestSurrogateSelector();
            bf.SurrogateSelector = selector;
            bsf.SurrogateSelector = selector;

            Console.WriteLine("------------------System Binaryformatter (Items Count: {0})--------------------", referenceObjects.Length);
            try
            {
                raw = SerializeObjects(referenceObjects, bf);
                // system deserialization fails: IBinarySerializable is not serializable
                bf.SurrogateSelector = null;
                result = DeserializeObjects(raw, bf);
                AssertItemsEqual(referenceObjects, result);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error in system serializer: " + e);
            }

            Console.WriteLine("------------------KGySoft BinarySerializer (Items Count: {0}; Options: {1})--------------------", referenceObjects.Length, bsf.Options);
            bsf.Options = BinarySerializationOptions.None;
            raw = SerializeObjects(referenceObjects, bsf);
            bsf.SurrogateSelector = null;
            result = DeserializeObjects(raw, bsf);
            AssertItemsEqual(referenceObjects, result);

            bsf.Options |= BinarySerializationOptions.TryUseSurrogateSelectorForAnyType;
            bsf.SurrogateSelector = selector;
            Console.WriteLine("------------------KGySoft BinarySerializer (Items Count: {0}; Options: {1})--------------------", referenceObjects.Length, bsf.Options);
            raw = SerializeObjects(referenceObjects, bsf);
            bsf.SurrogateSelector = null;
            result = DeserializeObjects(raw, bsf);
            AssertItemsEqual(referenceObjects, result);

            Console.WriteLine("================================Deserialization only with TestSurrogateSelector=======================================");
            bf.SurrogateSelector = null;
            bsf.SurrogateSelector = null;

            Console.WriteLine("------------------System Binaryformatter (Items Count: {0})--------------------", referenceObjects.Length);
            try
            {
                raw = SerializeObjects(referenceObjects, bf);
                // system deserialization fails: Cannot deserialize field: baseclass+backingfield (this is because of the surrogate) - TODO: solve this in TestSurrogate
                bf.SurrogateSelector = selector;
                result = DeserializeObjects(raw, bf);
                AssertItemsEqual(referenceObjects, result);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error in system serializer: " + e);
            }

            Console.WriteLine("------------------KGySoft BinarySerializer (Items Count: {0}; Options: {1})--------------------", referenceObjects.Length, bsf.Options);
            bsf.Options = BinarySerializationOptions.None;
            raw = SerializeObjects(referenceObjects, bsf);
            bsf.SurrogateSelector = selector;
            result = DeserializeObjects(raw, bsf);
            AssertItemsEqual(referenceObjects, result);

            bsf.Options |= BinarySerializationOptions.TryUseSurrogateSelectorForAnyType;
            bsf.SurrogateSelector = null;
            Console.WriteLine("------------------KGySoft BinarySerializer (Items Count: {0}; Options: {1})--------------------", referenceObjects.Length, bsf.Options);
            raw = SerializeObjects(referenceObjects, bsf);
            bsf.SurrogateSelector = selector;
            result = DeserializeObjects(raw, bsf);
            AssertItemsEqual(referenceObjects, result);
        }

        [TestMethod]
        public void SerializeSameValues()
        {
            object one = 1;
            string s1 = "alma";
            string s2 = String.Format("{0}{1}", "al", "ma");
            SystemSerializableClass tc = new SystemSerializableClass { IntProp = 10, StringProp = "s1" };
            object ts = new SystemSerializableStruct { IntProp = 10, StringProp = "s1" };
            object[] referenceObjects =
                {   // *: Id is generated on system serialization
                    new object[] { 1, 2, 3 }, // different objects - 14 -> 18
                    new object[] { 1, 1, 1 }, // same values but different instances - 14 -> 12
                    new object[] { one, one, one }, // same value type boxed reference - 14 -> 12
                    new object[] { s1, s1 }, // same references* - 19 -> 15
                    new object[] { s1, s2 }, // different references but same values - 19 -> 15
                    new string[] { s1, s1 }, // same references* - 17 -> 12
                    new string[] { s1, s2 }, // different references but same values - 17 -> 12
                    new SystemSerializableClass[] { tc }, // custom class, single instance - 230 -> 233
                    new SystemSerializableClass[] { tc, tc, tc, tc }, // custom class, multiple instances* - 509 -> 236
                    new SystemSerializableStruct[] { (SystemSerializableStruct)ts }, // custom struct, single instance - 202 -> 204
                    new SystemSerializableStruct[] { (SystemSerializableStruct)ts, (SystemSerializableStruct)ts, (SystemSerializableStruct)ts, (SystemSerializableStruct)ts }, // custom struct, double instances* - 394 -> 384
                    new object[] { ts }, // custom struct, boxed single instance - 204 -> 207
                    new object[] { ts, ts, ts, ts }, // custom struct, boxed double instances* - 411 -> 210
                };

            SystemSerializeObject(referenceObjects);
            SystemSerializeObjects(referenceObjects);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.None); // 788
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None);
        }

        [TestMethod]
        public void SerializeCircularReferences()
        {
            object[] referenceObjects =
                {
                    new CircularReferenceClass{Name = "Single"}, // no circular reference
                    new CircularReferenceClass{Name = "Parent"}.AddChild("Child").AddChild("Grandchild").Parent.Parent, // circular reference, but logically alright
                    new SelfReferencer("name"),
                    Encoding.GetEncoding("shift_jis") // circular reference via IObjectReference instances but with no custom serialization
                };

            SystemSerializeObject(referenceObjects);
            SystemSerializeObjects(referenceObjects);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.None);
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None);

            var root = new CircularReferenceClass { Name = "root" }.AddChild("child").AddChild("grandchild").Parent.Parent;
            root.Children[0].Children[0].Children.Add(root);
            referenceObjects = new object[]
            {
                root, // grand-grandchild is root again
                null, // placeholder: DictionaryEntry contains the referenceObjects and thus itself
            };
            referenceObjects[1] = new DictionaryEntry(1, referenceObjects);

            SystemSerializeObject(referenceObjects, true);
            SystemSerializeObjects(referenceObjects, true);

            KGySerializeObject(referenceObjects, BinarySerializationOptions.None, true);
            KGySerializeObjects(referenceObjects, BinarySerializationOptions.None, true);

            referenceObjects = new object[]
            {
                new SelfReferencerEvil("evil"), // the IObjectReference references itself in custom serialization: should throw SerializationException
            };

            SystemSerializeObject(referenceObjects);
            SystemSerializeObjects(referenceObjects);

            Throws<SerializationException>(() => KGySerializeObject(referenceObjects, BinarySerializationOptions.None));
            Throws<SerializationException>(() => KGySerializeObjects(referenceObjects, BinarySerializationOptions.None));
        }

        #endregion

        #region PrivateMethods

        private void SystemSerializeObject(object obj, bool safeCompare = false)
        {
            Type type = obj.GetType();
            Console.WriteLine("------------------System BinaryFormatter ({0})--------------------", type);
            try
            {
                BinaryFormatter bf = new BinaryFormatter();
                MemoryStream ms = new MemoryStream();
                bf.Serialize(ms, obj);

                Console.WriteLine("Length: {0}", ms.Length);
                if (dumpSerContent)
                    Console.WriteLine(ToRawString(ms.ToArray()));

                ms.Seek(0, SeekOrigin.Begin);
                object deserializedObject = bf.Deserialize(ms);
                if (!safeCompare)
                    AssertDeepEquals(obj, deserializedObject);
                else
                {
                    MemoryStream ms2 = new MemoryStream();
                    bf.Serialize(ms2, deserializedObject);
                    AssertDeepEquals(ms.ToArray(), ms2.ToArray());
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("System serialization failed: {0}", e);
            }
        }

        private void SystemSerializeObjects(object[] referenceObjects, bool safeCompare = false)
        {
            Console.WriteLine("------------------System BinaryFormatter (Items Count: {0})--------------------", referenceObjects.Length);
            try
            {
                List<object> deserializedObjects = new List<object>();
                BinaryFormatter bf = new BinaryFormatter();
                MemoryStream ms = new MemoryStream();
                foreach (object item in referenceObjects)
                {
                    if (item == null)
                    {
                        Console.WriteLine("Skipping null");
                        deserializedObjects.Add(null);
                        continue;
                    }
                    long pos = ms.Position;
                    bf.Serialize(ms, item);
                    Console.WriteLine("{0} - length: {1}", item.GetType(), ms.Length - pos);
                    ms.Seek(pos, SeekOrigin.Begin);
                    deserializedObjects.Add(bf.Deserialize(ms));
                }
                Console.WriteLine("Full length: {0}", ms.Length);
                if (dumpSerContent)
                    Console.WriteLine(ToRawString(ms.ToArray()));
                if (!safeCompare)
                    AssertItemsEqual(referenceObjects, deserializedObjects.ToArray());
                else
                {
                    MemoryStream ms2 = new MemoryStream();
                    foreach (object item in deserializedObjects)
                    {
                        if (item == null)
                            continue;
                        bf.Serialize(ms2, item);
                    }
                    AssertDeepEquals(ms.ToArray(), ms2.ToArray());
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("System serialization failed: {0}", e);
            }
        }

        private void KGySerializeObject(object obj, BinarySerializationOptions options, bool safeCompare = false)
        {
            Type type = obj.GetType();
            Console.WriteLine("------------------KGySoft BinarySerializer ({0} - {1})--------------------", type, options);
            try
            {
                byte[] serObject; // = BinarySerializer.Serialize(obj, options);
                using (MemoryStream ms = new MemoryStream())
                {
                    using (BinaryWriter bw = new TestWriter(ms, dumpDetails))
                    {
                        BinarySerializer.SerializeByWriter(bw, obj, options);
                    }

                    serObject = ms.ToArray();
                }
                Console.WriteLine("Length: {0}", serObject.Length);
                if (dumpSerContent)
                    Console.WriteLine(ToRawString(serObject.ToArray()));
                object deserializedObject; // = BinarySerializer.Deserialize(serObject);
                using (BinaryReader br = new TestReader(new MemoryStream(serObject), dumpDetails))
                {
                    deserializedObject = BinarySerializer.DeserializeByReader(br);
                }

                if (!safeCompare)
                    AssertDeepEquals(obj, deserializedObject);
                else
                {
                    MemoryStream ms2 = new MemoryStream();
                    BinarySerializer.SerializeToStream(ms2, deserializedObject, options);
                    AssertDeepEquals(serObject, ms2.ToArray());
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("KGySoft serialization failed: {0}", e);
                throw;
            }
        }

        private void KGySerializeObjects(object[] referenceObjects, BinarySerializationOptions options, bool safeCompare = false)
        {
            Console.WriteLine("------------------KGySoft BinarySerializer (Items Count: {0}; Options: {1})--------------------", referenceObjects.Length, options);
            BinarySerializationFormatter bsf = new BinarySerializationFormatter(options);
            try
            {
                byte[] serData = SerializeObjects(referenceObjects, bsf);
                object[] deserializedObjects = DeserializeObjects(serData, bsf);
                if (!safeCompare)
                    AssertItemsEqual(referenceObjects, deserializedObjects);
                else
                    AssertItemsEqual(serData, SerializeObjects(deserializedObjects, bsf));
            }
            catch (Exception e)
            {
                Console.WriteLine("KGySoft serialization failed: {0}", e);
                throw;
            }
        }

        private static byte[] SerializeObjects(object[] objects, IFormatter formatter)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                formatter.Serialize(ms, objects.Length);
                BinaryWriter bw = null;
                BinarySerializationFormatter bsf = null;
                if (dumpDetails && formatter is BinarySerializationFormatter)
                {
                    bw = new TestWriter(ms, dumpDetails);
                    bsf = formatter as BinarySerializationFormatter;
                }

                foreach (object o in objects)
                {
                    long pos = ms.Position;
                    if (bsf != null)
                        bsf.SerializeByWriter(bw, o);
                    else
                        formatter.Serialize(ms, o);
                    Console.WriteLine("{0} - length: {1}", o == null ? "<null>" : o.GetType().ToString(), ms.Position - pos);
                }
                Console.WriteLine("Full length: {0}", ms.Length);
                if (dumpSerContent)
                    Console.WriteLine(ToRawString(ms.ToArray()));
                return ms.ToArray();
            }
        }

        private static object[] DeserializeObjects(byte[] serObjects, IFormatter formatter)
        {
            using (MemoryStream ms = new MemoryStream(serObjects))
            {
                int length;
                object[] result = new object[length = (int)formatter.Deserialize(ms)];

                BinaryReader br = null;
                BinarySerializationFormatter bsf = null;
                if (dumpDetails && formatter is BinarySerializationFormatter)
                {
                    br = new TestReader(ms, dumpDetails);
                    bsf = formatter as BinarySerializationFormatter;
                }

                for (int i = 0; i < length; i++)
                {
                    result[i] = bsf != null ? bsf.DeserializeByReader(br) : formatter.Deserialize(ms);
                }
                return result;
            }
        }

        /// <summary>
        /// Converts the byte array (deemed as extended 8-bit ASCII characters) to raw Unicode UTF-8 string representation.
        /// </summary>
        /// <param name="bytes">The bytes to visualize as a raw UTF-8 data.</param>
        /// <remarks>
        /// <note type="caution">
        /// Please note that the .NET <see cref="string"/> type is always UTF-16 encoded. What this method does is
        /// not parsing an UTF-8 encoded stream but a special conversion that makes possible to display a byte array as a raw UTF-8 data.
        /// To convert a byte array to a regular <see cref="string"/> for usual purposes
        /// use <see cref="Encoding.Convert(System.Text.Encoding,System.Text.Encoding,byte[])"/> method instead.
        /// </note>
        /// </remarks>
        /// <returns>
        /// A <see cref="string"/> instance that is good for visualizing a raw UTF-8 string.</returns>
        private static string ToRawString(byte[] bytes) => Encoding.Default.GetString(bytes).Replace('\0', '\u25A1'); // "\0" to "□" in output

        #endregion
    }
}
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.


namespace System.Xml.Serialization
{
    using System.Reflection;
    using System.Collections;
    using System.IO;
    using System.Xml.Schema;
    using System;
    using System.Text;
    using System.Threading;
    using System.Globalization;
    using System.Security;
    using System.Diagnostics;
    using System.CodeDom.Compiler;
    using System.Collections.Generic;
    using Hashtable = System.Collections.IDictionary;
    using XmlSchema = System.ServiceModel.Dispatcher.XmlSchemaConstants;
    using XmlDeserializationEvents = System.Object;


    ///<internalonly/>
    /// <devdoc>
    ///    <para>[To be supplied.]</para>
    /// </devdoc>
    public abstract class XmlSerializerImplementation
    {
        public virtual XmlSerializationReader Reader { get { throw new NotSupportedException(); } }
        public virtual XmlSerializationWriter Writer { get { throw new NotSupportedException(); } }
        public virtual IDictionary ReadMethods { get { throw new NotSupportedException(); } }
        public virtual IDictionary WriteMethods { get { throw new NotSupportedException(); } }
        public virtual IDictionary TypedSerializers { get { throw new NotSupportedException(); } }
        public virtual bool CanSerialize(Type type) { throw new NotSupportedException(); }
        public virtual XmlSerializer GetSerializer(Type type) { throw new NotSupportedException(); }
    }

    /// <devdoc>
    ///    <para>[To be supplied.]</para>
    /// </devdoc>
    public class XmlSerializer
    {
        private TempAssembly _tempAssembly;
        private bool _typedSerializer;
        private Type _primitiveType;
        private XmlMapping _mapping;
        private XmlDeserializationEvents _events = new XmlDeserializationEvents();
#if NET_NATIVE
        public string DefaultNamespace = null;
        private XmlSerializer innerSerializer;
        private readonly Type rootType;
#endif

        private static TempAssemblyCache s_cache = new TempAssemblyCache();
        private static volatile XmlSerializerNamespaces s_defaultNamespaces;
        private static XmlSerializerNamespaces DefaultNamespaces
        {
            get
            {
                if (s_defaultNamespaces == null)
                {
                    XmlSerializerNamespaces nss = new XmlSerializerNamespaces();
                    nss.AddInternal("xsi", XmlSchema.InstanceNamespace);
                    nss.AddInternal("xsd", XmlSchema.Namespace);
                    if (s_defaultNamespaces == null)
                    {
                        s_defaultNamespaces = nss;
                    }
                }
                return s_defaultNamespaces;
            }
        }

        private static readonly Dictionary<Type, Dictionary<XmlSerializerMappingKey, XmlSerializer>> s_xmlSerializerTable = new Dictionary<Type, Dictionary<XmlSerializerMappingKey, XmlSerializer>>();

        ///<internalonly/>
        protected XmlSerializer()
        {
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public XmlSerializer(Type type, XmlAttributeOverrides overrides, Type[] extraTypes, XmlRootAttribute root, string defaultNamespace) :
            this(type, overrides, extraTypes, root, defaultNamespace, null, null)
        {
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public XmlSerializer(Type type, XmlRootAttribute root) : this(type, null, Array.Empty<Type>(), root, null, null, null)
        {
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
#if !NET_NATIVE
        public XmlSerializer(Type type, Type[] extraTypes) : this(type, null, extraTypes, null, null, null, null)
#else
        public XmlSerializer(Type type, Type[] extraTypes) : this(type)
#endif // NET_NATIVE
        {
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public XmlSerializer(Type type, XmlAttributeOverrides overrides) : this(type, overrides, Array.Empty<Type>(), null, null, null, null)
        {
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public XmlSerializer(XmlTypeMapping xmlTypeMapping)
        {
            _tempAssembly = GenerateTempAssembly(xmlTypeMapping);
            _mapping = xmlTypeMapping;
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public XmlSerializer(Type type) : this(type, (string)null)
        {
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public XmlSerializer(Type type, string defaultNamespace)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

#if NET_NATIVE
            this.DefaultNamespace = defaultNamespace;
            rootType = type;
#endif
            _mapping = GetKnownMapping(type, defaultNamespace);
            if (_mapping != null)
            {
                _primitiveType = type;
                return;
            }
#if !NET_NATIVE
            _tempAssembly = s_cache[defaultNamespace, type];
            if (_tempAssembly == null)
            {
                lock (s_cache)
                {
                    _tempAssembly = s_cache[defaultNamespace, type];
                    if (_tempAssembly == null)
                    {
                        {
                            // need to reflect and generate new serialization assembly
                            XmlReflectionImporter importer = new XmlReflectionImporter(defaultNamespace);
                            _mapping = importer.ImportTypeMapping(type, null, defaultNamespace);
                            _tempAssembly = GenerateTempAssembly(_mapping, type, defaultNamespace);
                        }
                    }
                    s_cache.Add(defaultNamespace, type, _tempAssembly);
                }
            }
            if (_mapping == null)
            {
                _mapping = XmlReflectionImporter.GetTopLevelMapping(type, defaultNamespace);
            }
#else
            XmlSerializerImplementation contract = GetXmlSerializerContractFromGeneratedAssembly();

            if (contract != null)
            {
                this.innerSerializer = contract.GetSerializer(type);
            }
#endif
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        internal XmlSerializer(Type type, XmlAttributeOverrides overrides, Type[] extraTypes, XmlRootAttribute root, string defaultNamespace, object location, object evidence)
        {
#if NET_NATIVE
            throw new PlatformNotSupportedException();
#else
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            XmlReflectionImporter importer = new XmlReflectionImporter(overrides, defaultNamespace);
            if (extraTypes != null)
            {
                for (int i = 0; i < extraTypes.Length; i++)
                    importer.IncludeType(extraTypes[i]);
            }
            _mapping = importer.ImportTypeMapping(type, root, defaultNamespace);
            _tempAssembly = GenerateTempAssembly(_mapping, type, defaultNamespace);
#endif
        }


        internal static TempAssembly GenerateTempAssembly(XmlMapping xmlMapping)
        {
            return GenerateTempAssembly(xmlMapping, null, null);
        }

        internal static TempAssembly GenerateTempAssembly(XmlMapping xmlMapping, Type type, string defaultNamespace)
        {
            if (xmlMapping == null)
                throw new ArgumentNullException(nameof(xmlMapping));
            return new TempAssembly(new XmlMapping[] { xmlMapping }, new Type[] { type }, defaultNamespace, null, null);
        }


        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public void Serialize(TextWriter textWriter, object o)
        {
            Serialize(textWriter, o, null);
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public void Serialize(TextWriter textWriter, object o, XmlSerializerNamespaces namespaces)
        {
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.IndentChars = "  ";
            XmlWriter xmlWriter = XmlWriter.Create(textWriter, settings);
            Serialize(xmlWriter, o, namespaces);
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public void Serialize(Stream stream, object o)
        {
            Serialize(stream, o, null);
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public void Serialize(Stream stream, object o, XmlSerializerNamespaces namespaces)
        {
            XmlWriterSettings settings = new XmlWriterSettings();

            settings.Indent = true;
            settings.IndentChars = "  ";

            XmlWriter xmlWriter = XmlWriter.Create(stream, settings);

            Serialize(xmlWriter, o, namespaces);
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public void Serialize(XmlWriter xmlWriter, object o)
        {
            Serialize(xmlWriter, o, null);
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public void Serialize(XmlWriter xmlWriter, object o, XmlSerializerNamespaces namespaces)
        {
            Serialize(xmlWriter, o, namespaces, null);
        }
        internal void Serialize(XmlWriter xmlWriter, object o, XmlSerializerNamespaces namespaces, string encodingStyle)
        {
            Serialize(xmlWriter, o, namespaces, encodingStyle, null);
        }

        internal void Serialize(XmlWriter xmlWriter, object o, XmlSerializerNamespaces namespaces, string encodingStyle, string id)
        {
            try
            {
                if (_primitiveType != null)
                {
                    SerializePrimitive(xmlWriter, o, namespaces);
                }
#if !NET_NATIVE
                else if (_tempAssembly == null || _typedSerializer)
                {
                    XmlSerializationWriter writer = CreateWriter();
                    writer.Init(xmlWriter, namespaces == null || namespaces.Count == 0 ? DefaultNamespaces : namespaces, encodingStyle, id, _tempAssembly);
                    try
                    {
                        Serialize(o, writer);
                    }
                    finally
                    {
                        writer.Dispose();
                    }
                }
                else
                    _tempAssembly.InvokeWriter(_mapping, xmlWriter, o, namespaces == null || namespaces.Count == 0 ? DefaultNamespaces : namespaces, encodingStyle, id);
#else
                else
                {
                    if (this.innerSerializer == null)
                    {
                        throw new InvalidOperationException(SR.Format(SR.Xml_MissingSerializationCodeException, this.rootType, typeof(XmlSerializer).Name));
                    }

                    if (!string.IsNullOrEmpty(this.DefaultNamespace))
                    {
                        this.innerSerializer.DefaultNamespace = this.DefaultNamespace; 
                    }
                    
                    XmlSerializationWriter writer = this.innerSerializer.CreateWriter();
                    writer.Init(xmlWriter, namespaces == null || namespaces.Count == 0 ? DefaultNamespaces : namespaces, encodingStyle, id);
                    try
                    {
                        this.innerSerializer.Serialize(o, writer);
                    }
                    finally
                    {
                        writer.Dispose();
                    }
                }
#endif
            }
            catch (Exception e)
            {
                if (e is TargetInvocationException)
                    e = e.InnerException;
                throw new InvalidOperationException(SR.XmlGenError, e);
            }
            xmlWriter.Flush();
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public object Deserialize(Stream stream)
        {
            XmlReaderSettings settings = new XmlReaderSettings();
            // WhitespaceHandling.Significant means that you only want events for *significant* whitespace 
            // resulting from elements marked with xml:space="preserve". All other whitespace 
            // (ie. XmlNodeType.Whitespace), deemed as insignificant for the XML infoset, is not 
            // reported by the reader. This mode corresponds to XmlReaderSettings.IgnoreWhitespace = true. 
            settings.IgnoreWhitespace = true;
            settings.DtdProcessing = (DtdProcessing)2; /* DtdProcessing.Parse */
            // Normalization = true, that's the default for the readers created with XmlReader.Create(). 
            // The XmlTextReader has as default a non-conformant mode according to the XML spec 
            // which skips some of the required processing for new lines, hence the need for the explicit 
            // Normalization = true. The mode is no-longer exposed on XmlReader.Create() 
            XmlReader xmlReader = XmlReader.Create(stream, settings);
            return Deserialize(xmlReader, null);
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public object Deserialize(TextReader textReader)
        {
            XmlReaderSettings settings = new XmlReaderSettings();
            // WhitespaceHandling.Significant means that you only want events for *significant* whitespace 
            // resulting from elements marked with xml:space="preserve". All other whitespace 
            // (ie. XmlNodeType.Whitespace), deemed as insignificant for the XML infoset, is not 
            // reported by the reader. This mode corresponds to XmlReaderSettings.IgnoreWhitespace = true. 
            settings.IgnoreWhitespace = true;
            settings.DtdProcessing = (DtdProcessing)2; /* DtdProcessing.Parse */
            // Normalization = true, that's the default for the readers created with XmlReader.Create(). 
            // The XmlTextReader has as default a non-conformant mode according to the XML spec 
            // which skips some of the required processing for new lines, hence the need for the explicit 
            // Normalization = true. The mode is no-longer exposed on XmlReader.Create() 
            XmlReader xmlReader = XmlReader.Create(textReader, settings);
            return Deserialize(xmlReader, null);
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public object Deserialize(XmlReader xmlReader)
        {
            return Deserialize(xmlReader, null);
        }


        internal object Deserialize(XmlReader xmlReader, string encodingStyle)
        {
            return Deserialize(xmlReader, encodingStyle, _events);
        }

        internal object Deserialize(XmlReader xmlReader, string encodingStyle, XmlDeserializationEvents events)
        {
            try
            {
                if (_primitiveType != null)
                {
                    return DeserializePrimitive(xmlReader, events);
                }
#if !NET_NATIVE
                else if (_tempAssembly == null || _typedSerializer)
                {
                    XmlSerializationReader reader = CreateReader();
                    reader.Init(xmlReader, events, encodingStyle, _tempAssembly);
                    try
                    {
                        return Deserialize(reader);
                    }
                    finally
                    {
                        reader.Dispose();
                    }
                }
                else
                {
                    return _tempAssembly.InvokeReader(_mapping, xmlReader, events, encodingStyle);
                }
#else
                else
                {
                    if (this.innerSerializer == null)
                    {
                        throw new InvalidOperationException(SR.Format(SR.Xml_MissingSerializationCodeException, this.rootType, typeof(XmlSerializer).Name));
                    }

                    if (!string.IsNullOrEmpty(this.DefaultNamespace))
                    {
                        this.innerSerializer.DefaultNamespace = this.DefaultNamespace; 
                    }

                    XmlSerializationReader reader = this.innerSerializer.CreateReader();
                    reader.Init(xmlReader, encodingStyle);
                    try
                    {
                        return this.innerSerializer.Deserialize(reader);
                    }
                    finally
                    {
                        reader.Dispose();
                    }
                }
#endif
            }
            catch (Exception e)
            {
                if (e is TargetInvocationException)
                    e = e.InnerException;

                if (xmlReader is IXmlLineInfo)
                {
                    IXmlLineInfo lineInfo = (IXmlLineInfo)xmlReader;
                    throw new InvalidOperationException(SR.Format(SR.XmlSerializeErrorDetails, lineInfo.LineNumber.ToString(CultureInfo.InvariantCulture), lineInfo.LinePosition.ToString(CultureInfo.InvariantCulture)), e);
                }
                else
                {
                    throw new InvalidOperationException(SR.XmlSerializeError, e);
                }
            }
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public virtual bool CanDeserialize(XmlReader xmlReader)
        {
            if (_primitiveType != null)
            {
                TypeDesc typeDesc = (TypeDesc)TypeScope.PrimtiveTypes[_primitiveType];
                return xmlReader.IsStartElement(typeDesc.DataType.Name, string.Empty);
            }
#if !NET_NATIVE
            else if (_tempAssembly != null)
            {
                return _tempAssembly.CanRead(_mapping, xmlReader);
            }
            else
            {
                return false;
            }
#else
            if (this.innerSerializer == null)
            {
                return false;
            }

            return this.innerSerializer.CanDeserialize(xmlReader);
#endif
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public static XmlSerializer[] FromMappings(XmlMapping[] mappings)
        {
            return FromMappings(mappings, (Type)null);
        }

        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public static XmlSerializer[] FromMappings(XmlMapping[] mappings, Type type)
        {
            if (mappings == null || mappings.Length == 0) return Array.Empty<XmlSerializer>();
            XmlSerializerImplementation contract = null;
            TempAssembly tempAssembly = null;
            {
                if (XmlMapping.IsShallow(mappings))
                {
                    return Array.Empty<XmlSerializer>();
                }
                else
                {
                    if (type == null)
                    {
                        tempAssembly = new TempAssembly(mappings, new Type[] { type }, null, null, null);
                        XmlSerializer[] serializers = new XmlSerializer[mappings.Length];

                        contract = tempAssembly.Contract;

                        for (int i = 0; i < serializers.Length; i++)
                        {
                            serializers[i] = (XmlSerializer)contract.TypedSerializers[mappings[i].Key];
                            serializers[i].SetTempAssembly(tempAssembly, mappings[i]);
                        }

                        return serializers;
                    }
                    else
                    {
                        // Use XmlSerializer cache when the type is not null.
                        return GetSerializersFromCache(mappings, type);
                    }
                }
            }
        }

        private static XmlSerializer[] GetSerializersFromCache(XmlMapping[] mappings, Type type)
        {
            XmlSerializer[] serializers = new XmlSerializer[mappings.Length];

            Dictionary<XmlSerializerMappingKey, XmlSerializer> typedMappingTable = null;
            lock (s_xmlSerializerTable)
            {
                if (!s_xmlSerializerTable.TryGetValue(type, out typedMappingTable))
                {
                    typedMappingTable = new Dictionary<XmlSerializerMappingKey, XmlSerializer>();
                    s_xmlSerializerTable[type] = typedMappingTable;
                }
            }

            lock (typedMappingTable)
            {
                var pendingKeys = new Dictionary<XmlSerializerMappingKey, int>();
                for (int i = 0; i < mappings.Length; i++)
                {
                    XmlSerializerMappingKey mappingKey = new XmlSerializerMappingKey(mappings[i]);
                    if (!typedMappingTable.TryGetValue(mappingKey, out serializers[i]))
                    {
                        pendingKeys.Add(mappingKey, i);
                    }
                }

                if (pendingKeys.Count > 0)
                {
                    XmlMapping[] pendingMappings = new XmlMapping[pendingKeys.Count];
                    int index = 0;
                    foreach (XmlSerializerMappingKey mappingKey in pendingKeys.Keys)
                    {
                        pendingMappings[index++] = mappingKey.Mapping;
                    }

                    TempAssembly tempAssembly = new TempAssembly(pendingMappings, new Type[] { type }, null, null, null);
                    XmlSerializerImplementation contract = tempAssembly.Contract;

                    foreach (XmlSerializerMappingKey mappingKey in pendingKeys.Keys)
                    {
                        index = pendingKeys[mappingKey];
                        serializers[index] = (XmlSerializer)contract.TypedSerializers[mappingKey.Mapping.Key];
                        serializers[index].SetTempAssembly(tempAssembly, mappingKey.Mapping);

                        typedMappingTable[mappingKey] = serializers[index];
                    }
                }
            }

            return serializers;
        }



        /// <devdoc>
        ///    <para>[To be supplied.]</para>
        /// </devdoc>
        public static XmlSerializer[] FromTypes(Type[] types)
        {
            if (types == null)
                return Array.Empty<XmlSerializer>();

#if NET_NATIVE
            var serializers = new XmlSerializer[types.Length];
            for (int i = 0; i < types.Length; i++)
            {
                serializers[i] = new XmlSerializer(types[i]);
            }

            return serializers;
#else
            XmlReflectionImporter importer = new XmlReflectionImporter();
            XmlTypeMapping[] mappings = new XmlTypeMapping[types.Length];
            for (int i = 0; i < types.Length; i++)
            {
                mappings[i] = importer.ImportTypeMapping(types[i]);
            }
            return FromMappings(mappings);
#endif
        }

#if NET_NATIVE
        // this the global XML serializer contract introduced for multi-file
        private static XmlSerializerImplementation xmlSerializerContract;

        internal static XmlSerializerImplementation GetXmlSerializerContractFromGeneratedAssembly()
        {
            // hack to pull in SetXmlSerializerContract which is only referenced from the 
            // code injected by MainMethodInjector transform
            // there's probably also a way to do this via [DependencyReductionRoot], 
            // but I can't get the compiler to find that...
            if (xmlSerializerContract == null)
                SetXmlSerializerContract(null);

            // this method body used to be rewritten by an IL transform
            // with the restructuring for multi-file, it has become a regular method
            return xmlSerializerContract;
        }

        public static void SetXmlSerializerContract(XmlSerializerImplementation xmlSerializerImplementation)
        {
            xmlSerializerContract = xmlSerializerImplementation;
        }
#endif

        ///<internalonly/>
        protected virtual XmlSerializationReader CreateReader() { throw new PlatformNotSupportedException(); }
        ///<internalonly/>
        protected virtual object Deserialize(XmlSerializationReader reader) { throw new PlatformNotSupportedException(); }
        ///<internalonly/>
        protected virtual XmlSerializationWriter CreateWriter() { throw new PlatformNotSupportedException(); }
        ///<internalonly/>
        protected virtual void Serialize(object o, XmlSerializationWriter writer) { throw new PlatformNotSupportedException(); }

        internal void SetTempAssembly(TempAssembly tempAssembly, XmlMapping mapping)
        {
            _tempAssembly = tempAssembly;
            _mapping = mapping;
            _typedSerializer = true;
        }

        private static XmlTypeMapping GetKnownMapping(Type type, string ns)
        {
            if (ns != null && ns != string.Empty)
                return null;
            TypeDesc typeDesc;
            if (!TypeScope.PrimtiveTypes.TryGetValue(type, out typeDesc))
                return null;
            ElementAccessor element = new ElementAccessor();
            element.Name = typeDesc.DataType.Name;
            XmlTypeMapping mapping = new XmlTypeMapping(null, element);
            mapping.SetKeyInternal(XmlMapping.GenerateKey(type, null, null));
            return mapping;
        }

        private void SerializePrimitive(XmlWriter xmlWriter, object o, XmlSerializerNamespaces namespaces)
        {
            XmlSerializationPrimitiveWriter writer = new XmlSerializationPrimitiveWriter();
            writer.Init(xmlWriter, namespaces, null, null, null);
            switch (_primitiveType.GetTypeCode())
            {
                case TypeCode.String:
                    writer.Write_string(o);
                    break;
                case TypeCode.Int32:
                    writer.Write_int(o);
                    break;
                case TypeCode.Boolean:
                    writer.Write_boolean(o);
                    break;
                case TypeCode.Int16:
                    writer.Write_short(o);
                    break;
                case TypeCode.Int64:
                    writer.Write_long(o);
                    break;
                case TypeCode.Single:
                    writer.Write_float(o);
                    break;
                case TypeCode.Double:
                    writer.Write_double(o);
                    break;
                case TypeCode.Decimal:
                    writer.Write_decimal(o);
                    break;
                case TypeCode.DateTime:
                    writer.Write_dateTime(o);
                    break;
                case TypeCode.Char:
                    writer.Write_char(o);
                    break;
                case TypeCode.Byte:
                    writer.Write_unsignedByte(o);
                    break;
                case TypeCode.SByte:
                    writer.Write_byte(o);
                    break;
                case TypeCode.UInt16:
                    writer.Write_unsignedShort(o);
                    break;
                case TypeCode.UInt32:
                    writer.Write_unsignedInt(o);
                    break;
                case TypeCode.UInt64:
                    writer.Write_unsignedLong(o);
                    break;

                default:
                    if (_primitiveType == typeof(XmlQualifiedName))
                    {
                        writer.Write_QName(o);
                    }
                    else if (_primitiveType == typeof(byte[]))
                    {
                        writer.Write_base64Binary(o);
                    }
                    else if (_primitiveType == typeof(Guid))
                    {
                        writer.Write_guid(o);
                    }
                    else if (_primitiveType == typeof(TimeSpan))
                    {
                        writer.Write_TimeSpan(o);
                    }
                    else
                    {
                        throw new InvalidOperationException(SR.Format(SR.XmlUnxpectedType, _primitiveType.FullName));
                    }
                    break;
            }
        }

        private object DeserializePrimitive(XmlReader xmlReader, XmlDeserializationEvents events)
        {
            XmlSerializationPrimitiveReader reader = new XmlSerializationPrimitiveReader();
            reader.Init(xmlReader, events, null, null);
            object o;
            switch (_primitiveType.GetTypeCode())
            {
                case TypeCode.String:
                    o = reader.Read_string();
                    break;
                case TypeCode.Int32:
                    o = reader.Read_int();
                    break;
                case TypeCode.Boolean:
                    o = reader.Read_boolean();
                    break;
                case TypeCode.Int16:
                    o = reader.Read_short();
                    break;
                case TypeCode.Int64:
                    o = reader.Read_long();
                    break;
                case TypeCode.Single:
                    o = reader.Read_float();
                    break;
                case TypeCode.Double:
                    o = reader.Read_double();
                    break;
                case TypeCode.Decimal:
                    o = reader.Read_decimal();
                    break;
                case TypeCode.DateTime:
                    o = reader.Read_dateTime();
                    break;
                case TypeCode.Char:
                    o = reader.Read_char();
                    break;
                case TypeCode.Byte:
                    o = reader.Read_unsignedByte();
                    break;
                case TypeCode.SByte:
                    o = reader.Read_byte();
                    break;
                case TypeCode.UInt16:
                    o = reader.Read_unsignedShort();
                    break;
                case TypeCode.UInt32:
                    o = reader.Read_unsignedInt();
                    break;
                case TypeCode.UInt64:
                    o = reader.Read_unsignedLong();
                    break;

                default:
                    if (_primitiveType == typeof(XmlQualifiedName))
                    {
                        o = reader.Read_QName();
                    }
                    else if (_primitiveType == typeof(byte[]))
                    {
                        o = reader.Read_base64Binary();
                    }
                    else if (_primitiveType == typeof(Guid))
                    {
                        o = reader.Read_guid();
                    }
                    else if (_primitiveType == typeof(TimeSpan))
                    {
                        o = reader.Read_TimeSpan();
                    }
                    else
                    {
                        throw new InvalidOperationException(SR.Format(SR.XmlUnxpectedType, _primitiveType.FullName));
                    }
                    break;
            }
            return o;
        }

        private class XmlSerializerMappingKey
        {
            public XmlMapping Mapping;
            public XmlSerializerMappingKey(XmlMapping mapping)
            {
                this.Mapping = mapping;
            }

            public override bool Equals(object obj)
            {
                XmlSerializerMappingKey other = obj as XmlSerializerMappingKey;
                if (other == null)
                    return false;

                if (this.Mapping.Key != other.Mapping.Key)
                    return false;

                if (this.Mapping.ElementName != other.Mapping.ElementName)
                    return false;

                if (this.Mapping.Namespace != other.Mapping.Namespace)
                    return false;

                if (this.Mapping.IsSoap != other.Mapping.IsSoap)
                    return false;

                return true;
            }

            public override int GetHashCode()
            {
                int hashCode = this.Mapping.IsSoap ? 0 : 1;

                if (this.Mapping.Key != null)
                    hashCode ^= this.Mapping.Key.GetHashCode();

                if (this.Mapping.ElementName != null)
                    hashCode ^= this.Mapping.ElementName.GetHashCode();

                if (this.Mapping.Namespace != null)
                    hashCode ^= this.Mapping.Namespace.GetHashCode();

                return hashCode;
            }
        }
    }
}

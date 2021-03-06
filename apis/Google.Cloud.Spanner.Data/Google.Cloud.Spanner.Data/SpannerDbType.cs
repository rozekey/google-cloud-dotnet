﻿// Copyright 2017 Google Inc. All Rights Reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using Google.Api.Gax;
using Google.Cloud.Spanner.V1;
using Google.Protobuf.WellKnownTypes;
using TypeCode = Google.Cloud.Spanner.V1.TypeCode;

namespace Google.Cloud.Spanner.Data
{
    /// <summary>
    /// Represents a Type that can be stored in a Spanner column or returned from a query.
    /// </summary>
    public sealed partial class SpannerDbType
    {
        /// <summary>
        /// Not specified.
        /// </summary>
        public static SpannerDbType Unspecified { get; } = new SpannerDbType(TypeCode.Unspecified);

        /// <summary>
        /// `true` or `false`.
        /// </summary>
        public static SpannerDbType Bool { get; } = new SpannerDbType(TypeCode.Bool);

        /// <summary>
        /// 64 bit signed integer.
        /// </summary>
        public static SpannerDbType Int64 { get; } = new SpannerDbType(TypeCode.Int64);

        /// <summary>
        /// 64 bit floating point number.
        /// </summary>
        public static SpannerDbType Float64 { get; } = new SpannerDbType(TypeCode.Float64);

        /// <summary>
        /// Date and Time.
        /// </summary>
        public static SpannerDbType Timestamp { get; } = new SpannerDbType(TypeCode.Timestamp);

        /// <summary>
        /// A Date.
        /// </summary>
        public static SpannerDbType Date { get; } = new SpannerDbType(TypeCode.Date);

        /// <summary>
        /// A string.
        /// </summary>
        public static SpannerDbType String { get; } = new SpannerDbType(TypeCode.String);

        /// <summary>
        /// A byte array (byte[]).
        /// </summary>
        public static SpannerDbType Bytes { get; } = new SpannerDbType(TypeCode.Bytes);

        private static readonly Dictionary<TypeCode, SpannerDbType> s_simpleTypes
            = new Dictionary<TypeCode, SpannerDbType>
            {
                {TypeCode.Unspecified, Unspecified },
                {TypeCode.Bool, Bool },
                {TypeCode.Int64, Int64 },
                {TypeCode.Float64, Float64 },
                {TypeCode.Timestamp, Timestamp },
                {TypeCode.Date, Date },
                {TypeCode.String, String },
                {TypeCode.Bytes, Bytes }
            };

        internal static SpannerDbType FromTypeCode(TypeCode code) 
            => s_simpleTypes.TryGetValue(code, out SpannerDbType type) ? type : null;

        internal TypeCode TypeCode { get; }

        internal SpannerDbType ArrayElementType { get; }

        internal IDictionary<string, SpannerDbType> StructMembers { get; }

        //Preserving the order which struct members are declared is important because some values come back
        //as list values.
        internal List<string> StructOrder { get; }

        private SpannerDbType(TypeCode typeCode, int? size = null)
        {
            GaxPreconditions.CheckNonNegative(size.GetValueOrDefault(), "Size must be nonnegative.");
            TypeCode = typeCode;
            Size = size;
        }

        /// <summary>
        /// The size of the Type, if set.
        /// </summary>
        public int? Size { get; }

        private SpannerDbType(TypeCode typeCode, SpannerDbType arrayElementType)
            : this(typeCode) => ArrayElementType = arrayElementType;

        private SpannerDbType(TypeCode typeCode, IEnumerable<Tuple<string, SpannerDbType>> structMembers)
            : this(typeCode)
        {
            StructOrder = new List<string>();
            StructMembers = new Dictionary<string, SpannerDbType>();
            foreach (var field in structMembers)
            {
                StructMembers[field.Item1] = field.Item2;
                StructOrder.Add(field.Item1);
            }
        }

        private SpannerDbType(TypeCode typeCode, IEnumerable<StructType.Types.Field> structTypeFields)
            : this(typeCode, structTypeFields.Select(
                    x => new Tuple<string, SpannerDbType>(x.Name, FromProtobufType(x.Type))))
        {
        }

        /// <summary>
        /// The corresponding <see cref="DbType"/> for this Cloud Spanner type.
        /// </summary>
        public DbType DbType
        {
            get
            {
                switch (TypeCode)
                {
                    case TypeCode.Bool:
                        return DbType.Boolean;
                    case TypeCode.Int64:
                        return DbType.Int64;
                    case TypeCode.Float64:
                        return DbType.Double;
                    case TypeCode.Timestamp:
                        return DbType.DateTime;
                    case TypeCode.Date:
                        return DbType.Date;
                    case TypeCode.String:
                        return DbType.String;
                    case TypeCode.Bytes:
                        return DbType.Binary;
                    default:
                        return DbType.Object;
                }
            }
        }

        /// <summary>
        /// The default <see cref="System.Type"/> for this Cloud Spanner type.
        /// </summary>
        public System.Type DefaultClrType
        {
            get
            {
                switch (TypeCode)
                {
                    case TypeCode.Bool:
                        return typeof(bool);
                    case TypeCode.Int64:
                        return typeof(long);
                    case TypeCode.Float64:
                        return typeof(double);
                    case TypeCode.Timestamp:
                    case TypeCode.Date:
                        return typeof(DateTime);
                    case TypeCode.String:
                        return typeof(string);
                    case TypeCode.Bytes:
                        return typeof(byte[]);
                    case TypeCode.Array:
                        return typeof(List<>).MakeGenericType(ArrayElementType.DefaultClrType);
                    case TypeCode.Struct:
                        return typeof(Dictionary<string, object>);
                    default:
                        //if we don't recognize it (or its a struct), we use the google native wellknown type.
                        return typeof(Value);
                }
            }
        }

        internal static SpannerDbType FromProtobufType(V1.Type type)
        {
            switch (type.Code)
            {
                case TypeCode.Array:
                    return new SpannerDbType(
                        TypeCode.Array,
                        FromProtobufType(type.ArrayElementType));
                case TypeCode.Struct:
                    return new SpannerDbType(TypeCode.Struct, type.StructType.Fields);
                default:
                    return FromTypeCode(type.Code);
            }
        }

        internal V1.Type ToProtobufType()
        {
            switch (TypeCode)
            {
                case TypeCode.Array:
                    return new V1.Type
                    {
                        Code = TypeCode,
                        ArrayElementType = ArrayElementType.ToProtobufType()
                    };
                case TypeCode.Struct:
                    return new V1.Type
                    {
                        Code = TypeCode,
                        StructType =
                            new StructType
                            {
                                Fields =
                                {
                                    StructMembers.Select(
                                        kvp => new StructType.Types.Field
                                        {
                                            Name = kvp.Key,
                                            Type = kvp.Value.ToProtobufType()
                                        })
                                }
                            }
                    };
                default: return new V1.Type {Code = TypeCode};
            }
        }

        /// <summary>
        /// Creates an array of the specified type. This can be done on any arbitrary <see cref="SpannerDbType"/>.
        /// You may use any type that implements IEnumerable as a source for the array.  The type of each
        /// item is determined by <paramref name="elementType"/>.
        /// When calling <see cref="SpannerDataReader.GetFieldValue(string)"/> the default type
        /// is <see cref="List{T}"/>. You may, however, specify any type that implements IList which
        /// has a default constructor.
        /// </summary>
        /// <param name="elementType">The type of each item in the array.</param>
        public static SpannerDbType ArrayOf(SpannerDbType elementType) =>
            new SpannerDbType(TypeCode.Array, elementType);


#pragma warning disable DE0006
        /// <summary>
        /// Creates a struct of the specified type.
        /// A struct has field names and field values.
        /// You may use any type that implements IDictionary as a source for the array.
        /// The ToString method will be called on each key of the IDictionary to generate each field name.
        /// Each field value's type is determined by the given argument <paramref name="structMembers"/>.
        /// When calling <see cref="SpannerDataReader.GetFieldValue(string)"/> the default type
        /// is <see cref="Dictionary{String, Object}"/>. You may, however, specify any type that implements IDictionary which
        /// has a default constructor.
        /// </summary>
        /// <param name="structMembers">A dictionary containing the field names and types of each member of the struct.</param>
        /// <returns>A <see cref="SpannerDbType"/> representing a structure.</returns>
#pragma warning restore DE0006
        public static SpannerDbType StructOf(IDictionary<string, SpannerDbType> structMembers) => new SpannerDbType(
            TypeCode.Struct, structMembers.Select(x => new Tuple<string, SpannerDbType>(x.Key, x.Value)));

        /// <summary>
        /// Returns a SpannerDbType given a ClrType.
        /// If the type cannot be determined, <see cref="SpannerDbType.Unspecified"/> is returned.
        /// </summary>
        /// <param name="type">The clrType to convert.</param>
        /// <returns>The best Spanner representation of the given Clr Type.</returns>
        public static SpannerDbType FromClrType(System.Type type)
        {
            if (type == typeof(bool))
            {
                return Bool;
            }
            if (typeof(IEnumerable<byte>).IsAssignableFrom(type))
            {
                return Bytes;
            }
            if (type == typeof(DateTime))
            {
                return Timestamp;
            }
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            {
                return Float64;
            }
            if (type == typeof(short) || type == typeof(int) || type == typeof(long)
                || type == typeof(ulong) || type == typeof(ushort) || type == typeof(uint))
            {
                return Int64;
            }
            if (type == typeof(string))
            {
                return String;
            }
            return Unspecified;
        }

        /// <summary>
        /// Returns a clone of this type with the specified size constraint.
        /// Only valid on <see cref="TypeCode.String"/> and <see cref="TypeCode.Bytes"/>
        /// </summary>
        /// <param name="size">Represents the number of characters (for <see cref="TypeCode.String"/>)
        ///  or bytes (for <see cref="TypeCode.Bytes"/>)</param>
        /// <returns>A new instance of <see cref="SpannerDbType"/> with the same <see cref="TypeCode"/> and new size.</returns>
        public SpannerDbType WithSize(int size)
        {
            if (TypeCode != TypeCode.Bytes && TypeCode != TypeCode.String)
            {
                throw new InvalidOperationException($"Size may only be set on types {nameof(String)} and {nameof(Bytes)}");
            }
            return new SpannerDbType(TypeCode, size);
        }

        /// <inheritdoc />
        public override bool Equals(object obj) => Equals(obj as SpannerDbType);

        private bool Equals(SpannerDbType other) => TypeUtil.DictionaryEquals(StructMembers, other?.StructMembers)
            && TypeCode == other?.TypeCode
            && Size == other.Size
            && Equals(ArrayElementType, other.ArrayElementType);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = StructMembers?.Count ?? 0;
                hashCode = (hashCode * 397) ^ Size.GetValueOrDefault(0);
                hashCode = (hashCode * 397) ^ (int)TypeCode;
                hashCode = (hashCode * 397) ^ (ArrayElementType?.GetHashCode() ?? 0);
                return hashCode;
            }
        }
    }
}

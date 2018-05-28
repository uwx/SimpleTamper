using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Contract = System.Diagnostics.Contracts.Contract;

namespace HSNXT.SimpleTamper
{
    public class TamperClassAttribute : Attribute
    {
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
        public Type Type { get; }

        public TamperClassAttribute(Type type) => Type = type;
    }
}
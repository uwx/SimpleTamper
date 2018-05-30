using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Rocks;

namespace HSNXT.SimpleTamper
{
#if DEPLOY
    public partial class ModuleWeaver
#else
    public partial class SimpleTamper
#endif
    {
        private ModuleDefinition Mod => ModuleDefinition;
        
        private static bool EqualParams(IMethodSignature method, params TypeReference[] args)
        {
            var c = method.Parameters.Count;
            if (args.Length != c) return false;
            for (var i = 0; i < c; i++)
            {
                if (method.Parameters[i].ParameterType.FullName != args[i].FullName)
                {
                    return false;
                }
            }

            return true;
        }

        private static void AssertParams(IMethodSignature method, params TypeReference[] args)
        {
            var c = method.Parameters.Count;
            if (args.Length != c)
                throw new Exception(
                    $"Method [{method}] does not match [{string.Join(",", args.Select(e => e.ToString()))}]");
            for (var i = 0; i < c; i++)
            {
                if (method.Parameters[i].ParameterType.FullName != args[i].FullName)
                {
                    throw new Exception($"Expected {args[i]} but got {method.Parameters[i].ParameterType}");
                }
            }
        }
        
        private static void AssertParams(IMethodSignature method, IEnumerable<TypeReference> args)
            => AssertParams(method, args.ToArray());


        // TODO better way of detecting property methods
        /// <summary>
        /// Checks if a method name is a property getter or setter
        /// </summary>
        /// <param name="methodName">The method name to check</param>
        /// <returns>True if it's a property getter or setter, false otherwise</returns>
        private static bool IsPropertyMethod(string methodName) 
            => methodName.StartsWith("get_") || methodName.StartsWith("set_");
        
        /// <summary>
        /// Throws an exception if a member is a get-only property
        /// </summary>
        /// <param name="fieldOrProp">The member to check</param>
        /// <exception cref="Exception">If the member is not writable</exception>
        private static void AssertIsPropWriteable(IMetadataTokenProvider fieldOrProp)
        {
            if (fieldOrProp is PropertyDefinition p && p.SetMethod == null)
                throw new Exception($"Cannot set a get-only property [{p.Name}]");
        }

        /// <summary>
        /// Finds a Cecil <see cref="TypeDefinition"/> matching a <see cref="System.Type"/> object.
        /// </summary>
        /// <param name="type">The type to search for</param>
        /// <returns>The found Cecil type</returns>
        private TypeDefinition FindMatchingType(Type type) => FindType(type.FullName);

        /// <summary>
        /// Finds a type by name and then builds a generic type from it
        /// </summary>
        /// <param name="type">The type to find</param>
        /// <param name="genericArguments">The generic arguments</param>
        /// <returns>The constructed type</returns>
        private GenericInstanceType FindGenericType(Type type, params TypeReference[] genericArguments) 
            => FindMatchingType(type).MakeGenericInstanceType(genericArguments);

        private GenericInstanceType FindGenericType(Type type, IEnumerable<TypeReference> genericArguments)
            => FindGenericType(type, genericArguments.ToArray());
        
        /// <summary>
        /// Keep this here so Fody will work even if there's no code referencing Cecil
        /// </summary>
        /// <returns>A dummy value</returns>
        public ArrayDimension Unused() => new ArrayDimension();
    }
}
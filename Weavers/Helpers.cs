using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace HSNXT.SimpleTamper
{
#if DEPLOY
    public partial class ModuleWeaver
#else
    public partial class SimpleTamper
#endif
    {
        /// <summary>
        /// Shortcut for ModuleDefinition to keep import statements concise.
        /// </summary>
        private ModuleDefinition Mod => ModuleDefinition;
        
        /// <summary>
        /// Checks if a method's parameters match an array of types.
        /// </summary>
        /// <param name="method">The method to check against</param>
        /// <param name="args">The parameters to check for</param>
        /// <returns>True if both parameter sets match, false otherwise</returns>
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

        /// <summary>
        /// Checks if a method's parameters match an array of types, throwing an exception if they don't.
        /// </summary>
        /// <param name="method">The method to check against</param>
        /// <param name="args">The parameters to check for</param>
        /// <exception cref="Exception">If both parameter sets don't match</exception>
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

        /// <summary>
        /// Gets the signature to be used as the name for the field that holds the Action/Func to call a method.
        /// This uses the method's parameters to ensure that methods with different names with the same signature
        /// can both be introspected without ambiguity.
        /// </summary>
        /// <param name="method">The method to get the signature of.</param>
        /// <returns>The method's signature</returns>
        private static string MakeSignature(MethodReference method) 
            => $"_call_method_{method.Name}_{string.Join(",", method.Parameters.Select(e => e.ParameterType.Name))}";

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
        
        /// <summary>
        /// Finds a type with an amount of generic arguments. If the amount is 0, returns the type as-is. Otherwise,
        /// builds a GenericInstanceType with the arguments provided.
        /// </summary>
        /// <param name="type">The type to look for</param>
        /// <param name="args">The parameters to build the type with</param>
        /// <returns><c>type</c> if <c>args</c> is empty, a new GenericInstanceType otherwise</returns>
        private TypeReference FindMaybeGeneric(Type type, TypeReference[] args)
        {
            if (args.Length == 0) return FindMatchingType(type);
            return FindGenericType(type, args);
        }

        private TypeReference FindMaybeGeneric(Type type, List<TypeReference> args)
            => FindMaybeGeneric(type, args.ToArray());

        /// <summary>
        /// Removes the first ret opcode from a method definition.
        /// </summary>
        /// <param name="method">The method to remove the return call from.</param>
        private static void RemoveRet(MethodDefinition method)
        {
            var inst = method.Body.Instructions;
            var firstRet = inst.FirstOrDefault(e => e.OpCode == OpCodes.Ret);
            if (firstRet != null) inst.Remove(firstRet);
        }

        /// <summary>
        /// Keep this here so Fody will work even if there's no code referencing Cecil
        /// </summary>
        /// <returns>A dummy value</returns>
        public ArrayDimension Unused() => new ArrayDimension();
    }
}
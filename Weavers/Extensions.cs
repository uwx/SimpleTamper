using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Rocks;

namespace HSNXT.SimpleTamper
{
    public static class Extensions
    {
        // is cecil type equal to .net reflection type
        public static bool Match(this TypeReference first, Type second) => first.FullName == second.FullName;

        // FirstOrDefault with an out var and returns bool success
        public static bool TryFirst<T>(this IEnumerable<T> self, out T result, Func<T, bool> func)
        {
            foreach (var e in self)
            {
                if (!func(e)) continue;

                result = e;
                return true;
            }

            result = default;
            return false;
        }

        // make a method static
        // taken from the cecil test utils
        public static GenericInstanceMethod MakeGeneric(this MethodReference method, params TypeReference[] args)
        {
            if (args.Length == 0 && method is GenericInstanceMethod gen)
                return gen;

            if (method.GenericParameters.Count != args.Length)
                throw new ArgumentException("Invalid number of generic type arguments supplied");

            var genericTypeRef = new GenericInstanceMethod(method);
            foreach (var arg in args)
                genericTypeRef.GenericArguments.Add(arg);

            return genericTypeRef;
        }

        // make the declaring type of a method a generic type, required because Resolve() loses generic information
        // taken from the cecil test utils
        public static MethodReference MakeHostGeneric(this MethodReference self,
            params TypeReference[] arguments)
        {
            var reference = new MethodReference(self.Name, self.ReturnType)
            {
                DeclaringType = self.DeclaringType.MakeGenericInstanceType(arguments),
                HasThis = self.HasThis,
                ExplicitThis = self.ExplicitThis,
                CallingConvention = self.CallingConvention,
            };

            foreach (var parameter in self.Parameters)
                reference.Parameters.Add(new ParameterDefinition(parameter.ParameterType));

            foreach (var genericParameter in self.GenericParameters)
                reference.GenericParameters.Add(new GenericParameter(genericParameter.Name, reference));

            return reference;
        }

        // finds a member with a name in member collection (eg fields or methods)
        public static T FindNamed<T>(this IEnumerable<T> collection, string name) where T : MemberReference
            => collection.FirstOrDefault(e => e.Name == name);

        public static MethodDefinition GetMethod(this TypeDefinition type, string name)
            => type.Methods.FindNamed(name);

        public static MethodDefinition GetMethod(this TypeReference type, string name)
            => type.Resolve().GetMethod(name);
        
        // shortcuts for ImportReference
        
        public static TypeReference Import(this Type type, ModuleDefinition moduleDefinition) =>
            moduleDefinition.ImportReference(type);

        public static FieldReference Import(this FieldInfo field, ModuleDefinition moduleDefinition) =>
            moduleDefinition.ImportReference(field);

        public static MethodReference Import(this MethodBase method, ModuleDefinition moduleDefinition) =>
            moduleDefinition.ImportReference(method);

        public static TypeReference Import(this TypeReference type, ModuleDefinition moduleDefinition) =>
            moduleDefinition.ImportReference(type);

        public static FieldReference Import(this FieldReference field, ModuleDefinition moduleDefinition) =>
            moduleDefinition.ImportReference(field);

        public static MethodReference Import(this MethodReference method, ModuleDefinition moduleDefinition) =>
            moduleDefinition.ImportReference(method);
    }
}
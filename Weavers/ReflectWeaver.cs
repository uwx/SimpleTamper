using System;
using System.Collections.Generic;
using System.Linq;
using Fody;
using HSNXT.SimpleTamper.Expressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace HSNXT.SimpleTamper
{
#if DEPLOY
    public partial class ModuleWeaver : BaseModuleWeaver
#else
    public partial class SimpleTamper : BaseModuleWeaver
#endif
    {
        /// <summary>
        /// Attributes to mark a field as <c>private static</c>
        /// </summary>
        private const FieldAttributes StaticField = FieldAttributes.Static | FieldAttributes.Private;

        /// <summary>
        /// Attributes to mark a method named <c>.cctor</c> as a class' static constructor.
        /// </summary>
        private const MethodAttributes StaticConstructorAttributes =
            MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName |
            MethodAttributes.RTSpecialName;

        /// <summary>
        /// The limit of parameters an introspected method can have. This limit can't be set any higher without creating
        /// delegates, since the Func and Action classes only go so big.
        /// </summary>
        private const int MaxParams = /*PARAMS_START*/15 /*PARAMS_END*/;

        // ReSharper disable InconsistentNaming
        /// <summary>
        /// Type currently being processed
        /// </summary>
        private TypeDefinition Type;

        /// <summary>
        /// Static constructor in the <see cref="Type"/> currently being processed
        /// </summary>
        private MethodDefinition StaticConstructor;

        /// <summary>
        /// ILProcessor for the <see cref="StaticConstructor"/>
        /// </summary>
        private ILProcessor CctorProc;

        /// <summary>
        /// Type currently being introspected
        /// </summary>
        private TypeDefinition TargetType;

        /// <summary>
        /// Current method in <see cref="Type"/> being processed
        /// </summary>
        private MethodDefinition Method;

        /// <summary>
        /// ILProcessor for the <see cref="Method"/>
        /// </summary>
        private ILProcessor Proc;

        /// <summary>
        /// Current field or property in the <see cref="TargetType"/> being processed (use only if relevant!)
        /// </summary>
        private MemberReference FieldOrProp;

        /// <summary>
        /// Type of the <see cref="FieldOrProp"/> currently being processed (once again, use only if relevant!)  
        /// </summary>
        private TypeReference FieldOrPropType => FieldOrProp is FieldReference f
            ? f.FieldType
            : FieldOrProp is PropertyReference p
                ? p.PropertyType
                : throw new InvalidCastException();

        /// <summary>
        /// Whether the <see cref="FieldOrProp"/> represents a static member (read above!)
        /// </summary>
        private bool FieldOrPropIsStatic => FieldOrProp is FieldDefinition f
            ? f.IsStatic
            : FieldOrProp is PropertyDefinition p
                ? p.GetMethod.IsStatic
                : throw new InvalidCastException();

        /// <summary>
        /// Current method in the <see cref="TargetType"/>, will only have a valid value when relevant
        /// </summary>
        private MethodDefinition TargetMethod;

        // fields from Util
        private MethodDefinition Getter_MemberInstance;
        private MethodDefinition Getter_MemberStatic;
        private MethodDefinition Setter_MemberInstanceStruct;
        private MethodDefinition Setter_MemberInstanceClass;
        private MethodDefinition Setter_MemberStatic;

        private MethodDefinition[] Caller_Instance;
        private MethodDefinition[] Caller_Static;
        private MethodDefinition[] Caller_InstanceVoid;
        private MethodDefinition[] Caller_StaticVoid;

        private Type[] AllFuncs =
        {
            typeof(Func<>),
            typeof(Func<,>),
            typeof(Func<,,>),
            typeof(Func<,,,>),
            typeof(Func<,,,,>),
            typeof(Func<,,,,,>),
            typeof(Func<,,,,,,>),
            typeof(Func<,,,,,,,>),
            typeof(Func<,,,,,,,,>),
            typeof(Func<,,,,,,,,,>),
            typeof(Func<,,,,,,,,,,>),
            typeof(Func<,,,,,,,,,,,>),
            typeof(Func<,,,,,,,,,,,,>),
            typeof(Func<,,,,,,,,,,,,,>),
            typeof(Func<,,,,,,,,,,,,,,>),
            typeof(Func<,,,,,,,,,,,,,,,>),
            typeof(Func<,,,,,,,,,,,,,,,,>),
        };
        private Type[] AllActions =
        {
            typeof(Action),
            typeof(Action<>),
            typeof(Action<,>),
            typeof(Action<,,>),
            typeof(Action<,,,>),
            typeof(Action<,,,,>),
            typeof(Action<,,,,,>),
            typeof(Action<,,,,,,>),
            typeof(Action<,,,,,,,>),
            typeof(Action<,,,,,,,,>),
            typeof(Action<,,,,,,,,,>),
            typeof(Action<,,,,,,,,,,>),
            typeof(Action<,,,,,,,,,,,>),
            typeof(Action<,,,,,,,,,,,,>),
            typeof(Action<,,,,,,,,,,,,,>),
            typeof(Action<,,,,,,,,,,,,,,>),
            typeof(Action<,,,,,,,,,,,,,,,>),
        };

        // ReSharper restore InconsistentNaming
        
        public override void Execute()
        {
            Console.WriteLine("Execute");

            var getters = FindMatchingType(typeof(Getters));
            var setters = FindMatchingType(typeof(Setters));
            var callers = FindMatchingType(typeof(Callers));
            
            Getter_MemberInstance = getters.GetMethod("MemberInstance");
            Getter_MemberStatic = getters.GetMethod("MemberStatic");
            
            Setter_MemberInstanceStruct = setters.GetMethod("MemberInstanceStruct");
            Setter_MemberInstanceClass = setters.GetMethod("MemberInstanceClass");
            Setter_MemberStatic = setters.GetMethod("MemberStatic");

            var range = Enumerable.Range(0, MaxParams+1).ToArray();
            Caller_Instance = range.Select(e => callers.GetMethod("Instance" + e)).ToArray();
            Caller_Static = range.Select(e => callers.GetMethod("Static" + e)).ToArray();
            Caller_InstanceVoid = range.Select(e => callers.GetMethod("InstanceVoid" + e)).ToArray();
            Caller_StaticVoid = range.Select(e => callers.GetMethod("StaticVoid" + e)).ToArray();

            foreach (var type in Mod.GetTypes())
            {
                Console.WriteLine("[TYPE] " + type);
                if (type.IsInterface)
                    continue;

                if (type.IsEnum)
                    continue;

                Type = type;
                ProcessType();
            }
        }

        private void ProcessType()
        {
            if (!Type.HasCustomAttributes)
                return;

            if (!Type.CustomAttributes.TryFirst(out var attr, e => e.AttributeType.Match(typeof(TamperClassAttribute))))
                return;

            TargetType = ((TypeReference) attr.ConstructorArguments[0].Value).Resolve();

            if (Type.Fields.TryFirst(out var dummyField, e => e.IsStatic && e.Name == "_dummy"))
                Type.Fields.Remove(dummyField);

            ProcessStaticType();
            ProcessInstanceType();
        }

        // ReSharper disable once ConvertIfStatementToReturnStatement
        /// <summary>
        /// Checks if a method matches 
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        private bool IsMethodCandidate(MethodDefinition e)
        {
            if (e.Name != Method.Name)
                return false;
            
            var ourMethodParamTypes = Method.Parameters.Select(e1 => e1.ParameterType);
            if (Method.IsStatic && !e.IsStatic)
                ourMethodParamTypes = ourMethodParamTypes.Skip(1); // skip (instance) parameter
            
#if DEBUG_METHOD_CANDIDATE
            AssertParams(e, ourMethodParamTypes.ToArray());
            return true;
            #else
            return EqualParams(e, ourMethodParamTypes.ToArray());
#endif
        }

        private void ProcessStaticType()
        {
            StaticConstructor = Type.GetStaticConstructor();
            if (StaticConstructor == null)
            {
                StaticConstructor = new MethodDefinition(".cctor", StaticConstructorAttributes, Mod.TypeSystem.Void);
                Type.Methods.Add(StaticConstructor);
            }
            // TODO remove existing ret instruction from static constructor if it's already present in the class
            CctorProc = StaticConstructor.Body.GetILProcessor();

            foreach (var method in Type.Methods)
            {
                // our static constructor isn't binding to any member in the target type, obviously
                if (method == StaticConstructor) continue;
                
                // we only handle static properties or methods binding to static members or instance members.
                // everything else is handled by ProcessInstanceType.
                if (!method.IsStatic) continue;
                
                Method = method;
                
                var methodName = Method.Name;
                var isPropertyMethod = false;
                
                if (Method.IsSpecialName)
                {
                    if (IsPropertyMethod(methodName))
                    {
                        isPropertyMethod = true;
                        methodName = methodName.Substring(4); // remove get_ or set_ from name
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG] SpecialName method: {Method} (you can probably ignore this)");
                        continue;
                    }
                }

                Proc = Method.Body.GetILProcessor();
                Method.Body.Instructions.Clear();

                if (TargetType.Fields.TryFirst(out FieldOrProp, e => e.Name == methodName)
                    || TargetType.Properties.TryFirst(out FieldOrProp, e => e.Name == methodName))
                {
                    if (Method.ReturnType.IsByReference) // TODO this is incorrect, it is valid code
                        throw new Exception("Return by reference is no longer possible as it causes invalid code");

                    if (FieldOrPropIsStatic)
                    {
                        CreateFieldGetSetStatic();
                    }
                    else
                    {
                        if (isPropertyMethod)
                            throw new Exception("A static property can't reference a non-static member");
                        
                        CreateFieldGetSet();
                    }
                }
                else
                {
                    if (isPropertyMethod)
                        throw new Exception($"Not possible to make {methodName} introspect into a method");
                    
                    if (TargetType.Methods.TryFirst(out TargetMethod, IsMethodCandidate))
                    {
                        if (TargetMethod.IsStatic ? TargetMethod.Parameters.Count > MaxParams : TargetMethod.Parameters.Count > MaxParams+1)
                            throw new Exception($"Method exceeds parameter limit of {MaxParams} not including instance: {TargetMethod}");
                        
                        var signature =
                            $"_call_method_{TargetMethod.Name}_{string.Join(",", TargetMethod.Parameters.Select(e => e.ParameterType.Name))}";
                        
                        // TODO this
                        // TODO fix symlinks in HSNXT.ExpressionWeave.Fody
                        // 
                        CreateMethod(signature, TargetMethod.ReturnType.Match(typeof(void)), TargetMethod.IsStatic);

                    }
                    else
                    {
                        throw new Exception($"Field mismatch: [{method.Name}] not present in [{TargetType}]");
                    }
                }
            }
            
            CctorProc.Emit(OpCodes.Ret);
        }

        private void CreateMethod(string signature, bool isVoid, bool isStatic)
        {
            var amtParams = TargetMethod.Parameters.Count;
            var realParams = isStatic ? amtParams : amtParams + 1;

            AssertParams(TargetMethod,
                isStatic
                    ? Method.Parameters.Select(e => e.ParameterType)
                    : Method.Parameters.Skip(1).Select(e => e.ParameterType));

            // Func<...params, ReturnType>
            var aparamsReturn = TargetMethod.Parameters.Select(e => e.ParameterType);
            if (!isVoid)
                aparamsReturn = aparamsReturn.Concat(new[] {Method.ReturnType});
            if (!isStatic)
                aparamsReturn = new[] { TargetType }.Concat(aparamsReturn);
            var paramsReturn = aparamsReturn.ToArray();

            // Callers.StaticN<TargetType, ...params, ReturnType>
            var genericArguments = paramsReturn;
            if (isStatic) // prefix <TargetType if we don't already have it
                genericArguments = new[] {TargetType}.Concat(paramsReturn).ToArray();
            
            // add field
            // Func<...params, ReturnType> _call_method_whatever
            var funcType = isVoid ? AllActions[realParams] : AllFuncs[realParams];
            Console.WriteLine("A:"+funcType);
            Console.WriteLine("B:"+string.Join(",", paramsReturn.Select(e => e.ToString())));
            //Console.WriteLine("C:"+);
            var genericFunc = FindMaybeGeneric(funcType, paramsReturn).Import(Mod);

            var funcField = new FieldDefinition(signature, StaticField, genericFunc);
            Type.Fields.Add(funcField);

            CctorProc.Emit(OpCodes.Ldstr, TargetMethod.Name);
            var callerMethod = GetCaller(amtParams, isVoid, isStatic);
            Console.WriteLine("C:"+callerMethod);
            CctorProc.Emit(OpCodes.Call,
                callerMethod.MakeGeneric(genericArguments).Import(Mod));
            CctorProc.Emit(OpCodes.Stsfld, funcField);
            
            // create invoke method
            // return _call_method_whatever.Invoke(...params);
            Proc.Emit(OpCodes.Ldsfld, funcField);
            for (var i = 0; i < realParams; i++)
                Proc.Emit(OpCodes.Ldarg_S, (byte) i);
            Proc.Emit(OpCodes.Callvirt,
                realParams == 0
                    ? genericFunc.GetMethod("Invoke").Import(Mod)
                    : genericFunc.GetMethod("Invoke").MakeHostGeneric(paramsReturn).Import(Mod));
            Proc.Emit(OpCodes.Ret);
        }

        private TypeReference FindMaybeGeneric(Type type, TypeReference[] @params)
        {
            if (@params.Length == 0) return FindMatchingType(type);
            return FindGenericType(type, @params);
        }

        private MethodDefinition GetCaller(int amtParams, bool isVoid, bool isStatic) 
            => (isStatic ? isVoid ? Caller_StaticVoid : Caller_Static : isVoid ? Caller_InstanceVoid : Caller_Instance)[amtParams];

        private void CreateFieldGetSetStatic()
        {
            switch (Method.Parameters.Count)
            {
                case 0: // getter
                {
                    AssertParams(Method /*none*/);

                    // add field
                    // Func<FieldType> _call_get_fieldName
                    var genericFunc = FindGenericType(typeof(Func<>), FieldOrPropType).Import(Mod);

                    var funcField = new FieldDefinition($"_call_get_{FieldOrProp.Name}", StaticField, genericFunc);
                    Type.Fields.Add(funcField);

                    // add to static constructor
                    // _call_get_fieldName = KSoft.Util.GenerateStaticMemberGetter<AC, float>("f");
                    CctorProc.Emit(OpCodes.Ldstr, FieldOrProp.Name);
                    CctorProc.Emit(OpCodes.Call,
                        Getter_MemberStatic.MakeGeneric(TargetType, FieldOrPropType).Import(Mod));
                    CctorProc.Emit(OpCodes.Stsfld, funcField);

                    // create getter method
                    // return _call_get_fieldName.Invoke();
                    Proc.Emit(OpCodes.Ldsfld, funcField);
                    Proc.Emit(OpCodes.Callvirt,
                        genericFunc.GetMethod("Invoke").MakeHostGeneric(FieldOrPropType).Import(Mod));
                    Proc.Emit(OpCodes.Ret);
                    break;
                }
                case 1: // setter
                {
                    AssertParams(Method, FieldOrPropType);
                    AssertIsPropWriteable(FieldOrProp);

                    // add field
                    // Action<FieldType> _call_set_fieldName
                    var genericFunc = FindGenericType(typeof(Action<>), FieldOrPropType).Import(Mod);

                    var funcField = new FieldDefinition($"_call_set_{FieldOrProp.Name}", StaticField, genericFunc);
                    Type.Fields.Add(funcField);
                    
                    // add to static constructor
                    // _call_get_fieldName = KSoft.Util.GenerateStaticMemberSetter<AC, float>("f");
                    CctorProc.Emit(OpCodes.Ldstr, FieldOrProp.Name);
                    CctorProc.Emit(OpCodes.Call,
                        Setter_MemberStatic.MakeGeneric(TargetType, FieldOrPropType).Import(Mod));
                    CctorProc.Emit(OpCodes.Stsfld, funcField);

                    // create getter method
                    // return _call_set_fieldName.Invoke(args[0]);
                    Proc.Emit(OpCodes.Ldsfld, funcField);
                    Proc.Emit(OpCodes.Ldarg_0);
                    Proc.Emit(OpCodes.Callvirt,
                        genericFunc.GetMethod("Invoke").MakeHostGeneric(FieldOrPropType).Import(Mod));
                    Proc.Emit(OpCodes.Ret);
                    break;
                }
                default:
                    throw new Exception($"Wrong params count for static {Method} {Method.Parameters.Count}, should be 0 or 1");
            }
        }

        private void CreateFieldGetSet()
        {
            switch (Method.Parameters.Count)
            {
                case 1: // getter
                {
                    AssertParams(Method, TargetType);

                    // add field
                    // Func<TargetType, FieldType> _call_get_fieldName
                    var genericFunc = FindGenericType(typeof(Func<,>), TargetType, FieldOrPropType).Import(Mod);

                    var funcField = new FieldDefinition($"_call_get_{FieldOrProp.Name}", StaticField, genericFunc);
                    Type.Fields.Add(funcField);

                    // add to static constructor
                    // _call_get_fieldName = KSoft.Util.GenerateMemberGetter<AC, float>("f");
                    CctorProc.Emit(OpCodes.Ldstr, FieldOrProp.Name);
                    CctorProc.Emit(OpCodes.Call,
                        Getter_MemberInstance.MakeGeneric(TargetType, FieldOrPropType).Import(Mod));
                    CctorProc.Emit(OpCodes.Stsfld, funcField);

                    // create getter method    
                    // return _call_get_fieldName.Invoke(args[0]);
                    Proc.Emit(OpCodes.Ldsfld, funcField);
                    Proc.Emit(OpCodes.Ldarg_0);
                    Proc.Emit(OpCodes.Callvirt,
                        genericFunc.GetMethod("Invoke").MakeHostGeneric(TargetType, FieldOrPropType).Import(Mod));
                    Proc.Emit(OpCodes.Ret);
                    break;
                }
                case 2: // setter
                {
                    AssertParams(Method, TargetType, FieldOrPropType);
                    AssertIsPropWriteable(FieldOrProp);

                    // add field
                    // Action<TargetType, FieldType> _call_set_fieldName
                    var genericFunc = FindGenericType(
                        TargetType.IsValueType ? typeof(StructSetter<,>) : typeof(Action<,>)
                        , TargetType, FieldOrPropType).Import(Mod);

                    var funcField = new FieldDefinition($"_call_set_{FieldOrProp.Name}", StaticField, genericFunc);
                    Type.Fields.Add(funcField);
                    
                    // add to static constructor
                    // _call_get_fieldName = KSoft.Util.GenerateStaticMemberSetter<AC, float>("f");
                    CctorProc.Emit(OpCodes.Ldstr, FieldOrProp.Name);
                    CctorProc.Emit(OpCodes.Call,
                        (TargetType.IsValueType ? Setter_MemberInstanceStruct : Setter_MemberInstanceClass)
                            .MakeGeneric(TargetType, FieldOrPropType).Import(Mod));
                    CctorProc.Emit(OpCodes.Stsfld, funcField);

                    // create getter method
                    // return _call_set_fieldName.Invoke(args[0], args[1]);
                    Proc.Emit(OpCodes.Ldsfld, funcField);
                    Proc.Emit(OpCodes.Ldarg_0);
                    Proc.Emit(OpCodes.Ldarg_1);
                    Proc.Emit(OpCodes.Callvirt,
                        genericFunc.GetMethod("Invoke").MakeHostGeneric(TargetType, FieldOrPropType).Import(Mod));
                    Proc.Emit(OpCodes.Ret);
                    break;
                }
                default:
                    throw new Exception($"Wrong params count for static {Method} {Method.Parameters.Count}, should be 0 or 1");
            }
        }

        private void ProcessInstanceType()
        {
        }

        public override IEnumerable<string> GetAssembliesForScanning()
        {
            Console.WriteLine("GetAssembliesForScanning");
            
            // Read from References in FodyWeavers.xml
            var references = Config.Attribute("References").Value
                .Split(',')
                .Select(e => e.Trim())
                .Where(e => !string.IsNullOrEmpty(e))
                .ToArray();
            Console.WriteLine("Refs: " + string.Join(",",references));
            foreach (var reference in references)
            {
                yield return reference;
            }

            // We need to reference ourselves to build expression trees
            yield return typeof(TamperClassAttribute).Assembly.GetName().Name;
            
            // These are all standard .NET stuff
            yield return "mscorlib";
            yield return "System";
            yield return "netstandard";
            yield return "System.Diagnostics.Tools";
            yield return "System.Diagnostics.Debug";
            yield return "System.Runtime";
        }
    }
}
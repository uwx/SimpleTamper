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
        /// Field holding value of <c>instance</c> parameter for instance members being called from instance members
        /// </summary>
        private FieldDefinition InstanceField;

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

        // fields from Expressions.cs
        private MethodDefinition Getter_MemberInstance;
        private MethodDefinition Getter_MemberStatic;
        
        private MethodDefinition Setter_MemberInstanceStruct;
        private MethodDefinition Setter_MemberInstanceClass;
        private MethodDefinition Setter_MemberStatic;

        private readonly MethodDefinition[] Caller_Instance = new MethodDefinition[MaxParams+1];
        private readonly MethodDefinition[] Caller_Static = new MethodDefinition[MaxParams+1];
        private readonly MethodDefinition[] Caller_InstanceVoid = new MethodDefinition[MaxParams+1];
        private readonly MethodDefinition[] Caller_StaticVoid = new MethodDefinition[MaxParams+1];

        private static readonly Type[] AllFuncs =
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
        private static readonly Type[] AllActions =
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

            // initialize all the different param counts
            for (var i = 0; i <= MaxParams; i++)
            {
                Caller_Instance[i] = callers.GetMethod("Instance" + i);
                Caller_Static[i] = callers.GetMethod("Static" + i);
                Caller_InstanceVoid[i] = callers.GetMethod("InstanceVoid" + i);
                Caller_StaticVoid[i] = callers.GetMethod("StaticVoid" + i);
            }

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

            ExecuteType();
        }

        // ReSharper disable once ConvertIfStatementToReturnStatement
        /// <summary>
        /// Checks if a method matches 
        /// </summary>
        /// <param name="targetMethod"></param>
        /// <returns></returns>
        private bool IsMethodCandidate(MethodDefinition targetMethod)
        {
            if (targetMethod.Name != Method.Name)
                return false;
            
            var selfParams = Method.Parameters.Select(e1 => e1.ParameterType);
            if (Method.IsStatic && !targetMethod.IsStatic)
                selfParams = selfParams.Skip(1); // skip (instance) parameter from method
            
#if DEBUG_METHOD_CANDIDATE
            AssertParams(e, ourMethodParamTypes.ToArray());
            return true;
#else
            return EqualParams(targetMethod, selfParams.ToArray());
#endif
        }

        private void ExecuteType()
        {
            StaticConstructor = Type.GetStaticConstructor();
            if (StaticConstructor == null)
            {
                StaticConstructor = new MethodDefinition(".cctor", StaticConstructorAttributes, Mod.TypeSystem.Void);
                Type.Methods.Add(StaticConstructor);
            }
            else
            {
                RemoveRet(StaticConstructor);
            }
            CctorProc = StaticConstructor.Body.GetILProcessor();

            foreach (var method in Type.Methods)
            {
                // our static constructor isn't binding to any member in the target type, obviously
                if (method == StaticConstructor) continue;

                if (!method.IsStatic && InstanceField == null)
                    CreateInstanceField();
                
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
                    if (Method.ReturnType.IsByReference) // TODO this
                        throw new Exception("Return by reference is not possible yet");

                    if (FieldOrPropIsStatic)
                    {
                        CreateFieldGetSetStatic();
                    }
                    else
                    {
                        if (Method.IsStatic && isPropertyMethod)
                            throw new Exception("A static property can't reference a non-static member");
                        
                        CreateFieldGetSet();
                    }
                }
                else
                {
                    if (isPropertyMethod)
                        throw new Exception($"Not possible to make property {methodName} introspect into a method");
                    
                    if (TargetType.Methods.TryFirst(out TargetMethod, IsMethodCandidate))
                    {
                        // if target method is static or source method pulls the instance param from field rather than
                        // an argument, we check for MaxParams. if it's not static, or has an extra parameter for instance
                        // we have to account for it.
                        if (TargetMethod.IsStatic || !Method.IsStatic
                            ? TargetMethod.Parameters.Count > MaxParams 
                            : TargetMethod.Parameters.Count > MaxParams+1)
                            throw new Exception($"Method exceeds parameter limit of {MaxParams} not including instance: {TargetMethod}");
                        
                        // TODO fix symlinks in HSNXT.ExpressionWeave.Fody 
                        CreateMethod(MakeSignature(TargetMethod), TargetMethod.ReturnType.Match(typeof(void)), TargetMethod.IsStatic);

                    }
                    else
                    {
                        throw new Exception($"Field mismatch: [{method.Name}] not present in [{TargetType}]");
                    }
                }
            }
            
            CctorProc.Emit(OpCodes.Ret);
        }

        private void CreateInstanceField()
        {
            InstanceField = new FieldDefinition("_hold_instance", FieldAttributes.Private, TargetType);
            Type.Fields.Add(InstanceField);
            
            var constructor = Type.GetConstructors().SingleOrDefault(e => EqualParams(e, TargetType));
            if (constructor == null)
                throw new Exception($"Add a constructor to {Type} that takes a single {TargetType} parameter with an empty body");
            RemoveRet(constructor);

            var cproc = constructor.Body.GetILProcessor();
            cproc.Emit(OpCodes.Ldarg_0);
            cproc.Emit(OpCodes.Ldarg_1);
            cproc.Emit(OpCodes.Stfld, InstanceField);
            cproc.Emit(OpCodes.Ret);
        }

        private void CreateMethod(string signature, bool isVoid, bool isStatic)
        {
            var amtParams = TargetMethod.Parameters.Count;
            var realParams = isStatic ? amtParams : amtParams + 1;

            AssertParams(TargetMethod,
                isStatic || !Method.IsStatic
                    ? Method.Parameters.Select(e => e.ParameterType)
                    : Method.Parameters.Skip(1).Select(e => e.ParameterType));

            //    Func<...params, ReturnType> for static methods with return
            // or Func<TargetType, ...params, ReturnType> for instance methods with return
            // or Action<...params> for static methods with void return
            // or Action<TargetType, ...params> for instance methods with void return
            var generics = TargetMethod.Parameters.Select(e => e.ParameterType).ToList();
            if (!isVoid)
                generics.Add(Method.ReturnType);
            if (!isStatic)
                generics.Insert(0, TargetType);
            
            // generic signature for Callers.XYZ<TargetType, ...params, ReturnType?>
            // this must always contain TargetType as the first entry, even if the method being static means no instance
            // is needed, so Callers knows where to get the method from
            var constructorGenerics = new List<TypeReference>(generics);
            if (isStatic)
                constructorGenerics.Insert(0, TargetType);
            
            // add field
            // for methods with return value, Func<...params, ReturnType> _call_method_signature
            // for methods with void return, Action<...params> _call_method_signature
            var funcType = isVoid ? AllActions[realParams] : AllFuncs[realParams];
            var genericFunc = FindMaybeGeneric(funcType, generics).Import(Mod);

            var funcField = new FieldDefinition(signature, StaticField, genericFunc);
            Type.Fields.Add(funcField);

            // initialize the field in the static constructor
            //    _call_method_signature = Callers.InstanceN<TargetType, ...params, ReturnType>(targetMethodName);
            // or _call_method_signature = Callers.StaticN<TargetType, ...params, ReturnType>(targetMethodName);
            // or _call_method_signature = Callers.InstanceVoidN<TargetType, ...params>(targetMethodName);
            // or _call_method_signature = Callers.StaticVoidN<TargetType, ...params>(targetMethodName);
            CctorProc.Emit(OpCodes.Ldstr, TargetMethod.Name);
            CctorProc.Emit(OpCodes.Call, 
                GetCaller(amtParams, isVoid, isStatic).MakeGeneric(constructorGenerics).Import(Mod));
            CctorProc.Emit(OpCodes.Stsfld, funcField);
            
            // create invoke method
            // return _call_method_whatever.Invoke(...params);
            Proc.Emit(OpCodes.Ldsfld, funcField);
            PushArgs(realParams, isStatic); // push all the args including instance onto the stack
            var invoke = genericFunc.GetMethod("Invoke");
            Proc.Emit(OpCodes.Callvirt,
                realParams == 0
                    ? invoke.Import(Mod)
                    : invoke.MakeHostGeneric(generics).Import(Mod));
            Proc.Emit(OpCodes.Ret);
        }

        private void PushArgs(int amt, bool isStatic)
        {
            var start = 0;
            // if source method isn't static, remove instance param from count and push the instance from the field onto
            // the stack
            if (!Method.IsStatic && !isStatic)
            {
                Proc.Emit(OpCodes.Ldarg_0);
                Proc.Emit(OpCodes.Ldfld, InstanceField);
                start = 1;
            }

            for (var i = start; i < amt; i++)
                Proc.Emit(OpCodes.Ldarg_S, (byte) i);
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
                    // _call_get_fieldName = Getters.MemberStatic<AC, float>("f");
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
                    // _call_get_fieldName = Setters.MemberStatic<AC, float>("f");
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
            var parametersCount = Method.Parameters.Count;
            if (Method.IsStatic && parametersCount == 1 || !Method.IsStatic && parametersCount == 0)
            {
                if (Method.IsStatic)
                    AssertParams(Method, TargetType);
                else
                    AssertParams(Method /* empty */);

                // add field
                // Func<TargetType, FieldType> _call_get_fieldName
                var genericFunc = FindGenericType(typeof(Func<,>), TargetType, FieldOrPropType).Import(Mod);

                var funcField = new FieldDefinition($"_call_get_{FieldOrProp.Name}", StaticField, genericFunc);
                Type.Fields.Add(funcField);

                // add to static constructor
                // _call_get_fieldName = Getters.MemberInstance<AC, float>("f");
                CctorProc.Emit(OpCodes.Ldstr, FieldOrProp.Name);
                CctorProc.Emit(OpCodes.Call,
                    Getter_MemberInstance.MakeGeneric(TargetType, FieldOrPropType).Import(Mod));
                CctorProc.Emit(OpCodes.Stsfld, funcField);

                // create getter method    
                // return _call_get_fieldName.Invoke(instance);
                Proc.Emit(OpCodes.Ldsfld, funcField);
                // push instance on the stack
                if (Method.IsStatic)
                    Proc.Emit(OpCodes.Ldarg_0);
                else
                {
                    Proc.Emit(OpCodes.Ldarg_0);
                    Proc.Emit(OpCodes.Ldfld, InstanceField);
                }

                Proc.Emit(OpCodes.Callvirt,
                    genericFunc.GetMethod("Invoke").MakeHostGeneric(TargetType, FieldOrPropType).Import(Mod));
                Proc.Emit(OpCodes.Ret);
            }
            else if (Method.IsStatic && parametersCount == 2 || !Method.IsStatic && parametersCount == 1)
            {
                if (Method.IsStatic)
                    AssertParams(Method, TargetType, FieldOrPropType);
                else
                    AssertParams(Method, FieldOrPropType);
                AssertIsPropWriteable(FieldOrProp);

                // add field
                // Action<TargetType, FieldType> _call_set_fieldName
                var genericFunc = FindGenericType(
                    TargetType.IsValueType ? typeof(StructSetter<,>) : typeof(Action<,>)
                    , TargetType, FieldOrPropType).Import(Mod);

                var funcField = new FieldDefinition($"_call_set_{FieldOrProp.Name}", StaticField, genericFunc);
                Type.Fields.Add(funcField);

                // add to static constructor
                // _call_get_fieldName = Setters.MemeberInstanceStruct|MemberInstanceClass<AC, float>("f");
                CctorProc.Emit(OpCodes.Ldstr, FieldOrProp.Name);
                CctorProc.Emit(OpCodes.Call,
                    (TargetType.IsValueType ? Setter_MemberInstanceStruct : Setter_MemberInstanceClass)
                    .MakeGeneric(TargetType, FieldOrPropType).Import(Mod));
                CctorProc.Emit(OpCodes.Stsfld, funcField);

                // create getter method
                // return _call_set_fieldName.Invoke(instance, args[0/1]);
                Proc.Emit(OpCodes.Ldsfld, funcField);
                // push instance and value on the stack
                if (Method.IsStatic)
                {
                    Proc.Emit(OpCodes.Ldarg_0);
                }
                else
                {
                    Proc.Emit(OpCodes.Ldarg_0);
                    Proc.Emit(OpCodes.Ldfld, InstanceField);
                }
                Proc.Emit(OpCodes.Ldarg_1);

                Proc.Emit(OpCodes.Callvirt,
                    genericFunc.GetMethod("Invoke").MakeHostGeneric(TargetType, FieldOrPropType).Import(Mod));
                Proc.Emit(OpCodes.Ret);
            }
            else
            {
                throw new Exception(
                    $"Wrong params count for static {Method} {parametersCount}, should be 1 or 2");
            }
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
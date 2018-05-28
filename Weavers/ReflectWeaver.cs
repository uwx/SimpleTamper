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
        private const MethodAttributes StaticConstructorAttributes = MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName;

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

        // ReSharper restore InconsistentNaming
        
        public override void Execute()
        {
            Console.WriteLine("Execute");

            var gettersMethods = FindMatchingType(typeof(Getters)).Methods;
            var settersMethods = FindMatchingType(typeof(Setters)).Methods;
            var callersMethods = FindMatchingType(typeof(Callers)).Methods;
            
            Getter_MemberInstance = gettersMethods.FindNamed("MemberInstance");
            Getter_MemberStatic = gettersMethods.FindNamed("MemberStatic");
            
            Setter_MemberInstanceStruct = settersMethods.FindNamed("MemberInstanceStruct");
            Setter_MemberInstanceClass = settersMethods.FindNamed("MemberInstanceClass");
            Setter_MemberStatic = settersMethods.FindNamed("MemberStatic");

            var range = Enumerable.Range(0, 16).ToArray();
            Caller_Instance = range.Select(e => callersMethods.FindNamed("Instance" + e)).ToArray();
            Caller_Static = range.Select(e => callersMethods.FindNamed("Static" + e)).ToArray();
            Caller_InstanceVoid = range.Select(e => callersMethods.FindNamed("InstanceVoid" + e)).ToArray();
            Caller_StaticVoid = range.Select(e => callersMethods.FindNamed("StaticVoid" + e)).ToArray();

            foreach (var type in ModuleDefinition.GetTypes())
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
                StaticConstructor = new MethodDefinition(".cctor", StaticConstructorAttributes, ModuleDefinition.TypeSystem.Void);
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
                        methodName = methodName.Substring(4);
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
                    if (Method.ReturnType.IsByReference)
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
                        // TODO this
                        // TODO text template
                        // TODO fix links in HSNXT.ExpressionWeave.Fody
                        // https://sharplab.io/#v2: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
                        if (!TargetMethod.IsStatic)
                        {
                            Proc.Emit(OpCodes.Ldarg_0);
                        }

                        Proc.Emit(OpCodes.Call, TargetMethod);
                        Proc.Emit(OpCodes.Ret);
                    }
                    else
                    {
                        throw new Exception($"Field mismatch: [{method.Name}] not present in [{TargetType}]");
                    }
                }
            }
            
            CctorProc.Emit(OpCodes.Ret);
        }

        private void CreateFieldGetSetStatic()
        {
            switch (Method.Parameters.Count)
            {
                case 0: // getter
                {
                    AssertParams(Method /*none*/);

                    // add field
                    // Func<FieldType> _call_get_fieldName
                    var genericFunc = FindGenericType(typeof(Func<>), FieldOrPropType).Import(ModuleDefinition);

                    var funcField = new FieldDefinition($"_call_get_{FieldOrProp.Name}", StaticField, genericFunc);
                    Type.Fields.Add(funcField);

                    // add to static constructor
                    // _call_get_fieldName = KSoft.Util.GenerateStaticMemberGetter<AC, float>("f");
                    CctorProc.Emit(OpCodes.Ldstr, FieldOrProp.Name);
                    CctorProc.Emit(OpCodes.Call,
                        Getter_MemberStatic.MakeGenericMethod(TargetType, FieldOrPropType).Import(ModuleDefinition));
                    CctorProc.Emit(OpCodes.Stsfld, funcField);

                    // create getter method
                    // return _call_get_fieldName.Invoke();
                    Proc.Emit(OpCodes.Ldsfld, funcField);
                    Proc.Emit(OpCodes.Callvirt,
                        genericFunc.Resolve().Methods.FindNamed("Invoke").MakeHostInstanceGeneric(FieldOrPropType).Import(ModuleDefinition));
                    Proc.Emit(OpCodes.Ret);
                    break;
                }
                case 1: // setter
                {
                    AssertParams(Method, FieldOrPropType);
                    AssertIsPropWriteable(FieldOrProp);

                    // add field
                    // Action<FieldType> _call_set_fieldName
                    var genericFunc = FindGenericType(typeof(Action<>), FieldOrPropType).Import(ModuleDefinition);

                    var funcField = new FieldDefinition($"_call_set_{FieldOrProp.Name}", StaticField, genericFunc);
                    Type.Fields.Add(funcField);
                    
                    // add to static constructor
                    // _call_get_fieldName = KSoft.Util.GenerateStaticMemberSetter<AC, float>("f");
                    CctorProc.Emit(OpCodes.Ldstr, FieldOrProp.Name);
                    CctorProc.Emit(OpCodes.Call,
                        Setter_MemberStatic.MakeGenericMethod(TargetType, FieldOrPropType).Import(ModuleDefinition));
                    CctorProc.Emit(OpCodes.Stsfld, funcField);

                    // create getter method
                    // return _call_set_fieldName.Invoke(args[0]);
                    Proc.Emit(OpCodes.Ldsfld, funcField);
                    Proc.Emit(OpCodes.Ldarg_0);
                    Proc.Emit(OpCodes.Callvirt,
                        genericFunc.Resolve().Methods.FindNamed("Invoke").MakeHostInstanceGeneric(FieldOrPropType).Import(ModuleDefinition));
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
                    var genericFunc = FindGenericType(typeof(Func<,>), TargetType, FieldOrPropType).Import(ModuleDefinition);

                    var funcField = new FieldDefinition($"_call_get_{FieldOrProp.Name}", StaticField, genericFunc);
                    Type.Fields.Add(funcField);

                    // add to static constructor
                    // _call_get_fieldName = KSoft.Util.GenerateMemberGetter<AC, float>("f");
                    CctorProc.Emit(OpCodes.Ldstr, FieldOrProp.Name);
                    CctorProc.Emit(OpCodes.Call,
                        Getter_MemberInstance.MakeGenericMethod(TargetType, FieldOrPropType).Import(ModuleDefinition));
                    CctorProc.Emit(OpCodes.Stsfld, funcField);

                    // create getter method    
                    // return _call_get_fieldName.Invoke(args[0]);
                    Proc.Emit(OpCodes.Ldsfld, funcField);
                    Proc.Emit(OpCodes.Ldarg_0);
                    Proc.Emit(OpCodes.Callvirt,
                        genericFunc.Resolve().Methods.FindNamed("Invoke").MakeHostInstanceGeneric(TargetType, FieldOrPropType).Import(ModuleDefinition));
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
                        , TargetType, FieldOrPropType).Import(ModuleDefinition);

                    var funcField = new FieldDefinition($"_call_set_{FieldOrProp.Name}", StaticField, genericFunc);
                    Type.Fields.Add(funcField);
                    
                    // add to static constructor
                    // _call_get_fieldName = KSoft.Util.GenerateStaticMemberSetter<AC, float>("f");
                    CctorProc.Emit(OpCodes.Ldstr, FieldOrProp.Name);
                    CctorProc.Emit(OpCodes.Call,
                        (TargetType.IsValueType ? Setter_MemberInstanceStruct : Setter_MemberInstanceClass)
                            .MakeGenericMethod(TargetType, FieldOrPropType).Import(ModuleDefinition));
                    CctorProc.Emit(OpCodes.Stsfld, funcField);

                    // create getter method
                    // return _call_set_fieldName.Invoke(args[0], args[1]);
                    Proc.Emit(OpCodes.Ldsfld, funcField);
                    Proc.Emit(OpCodes.Ldarg_0);
                    Proc.Emit(OpCodes.Ldarg_1);
                    Proc.Emit(OpCodes.Callvirt,
                        genericFunc.Resolve().Methods.FindNamed("Invoke").MakeHostInstanceGeneric(TargetType, FieldOrPropType).Import(ModuleDefinition));
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
﻿using System;
using System.Linq.Expressions;
using System.Reflection;
using static System.Linq.Expressions.Expression;
using static HSNXT.SimpleTamper.Expressions.ExpressionsBase;

namespace HSNXT.SimpleTamper.Expressions
{
    // this can't be inside the class or fody type cache dies
    public delegate void StructSetter<T, in TV>(ref T instance, TV value);
    
    public static class Getters
    {
        public static Func<T, TResult> MemberInstance<T, TResult>(string name)
        {
            return Lambda<Func<T, TResult>>(
                PropertyOrField(Parameter(typeof(T), "instance"), name) // return instance.name
                , Parameter(typeof(T), "instance") // (instance) => ...
            ).Compile();
        }

        public static Func<TR> MemberStatic<T, TR>(string name)
        {
            return Lambda<Func<TR>>(
                StaticPropertyOrField(typeof(T), name) // () => return T.name
            ).Compile();
        }
    }

    public static class Setters
    {
        public static StructSetter<T, TV> MemberInstanceStruct<T, TV>(string name) where T : struct
        {
            var instance = ParameterInstance<T>();
            var value = Parameter(typeof(TV), "value");

            return Lambda<StructSetter<T, TV>>(
                Assign(PropertyOrField(instance, name), value) // instance.name = value
                , instance, value).Compile(); // (ref instance, value) => ...
        }

        public static Action<T, TValue> MemberInstanceClass<T, TValue>(string name) where T : class
        {
            var instance = ParameterInstance<T>();
            var value = Parameter(typeof(TValue), "value");

            return Lambda<Action<T, TValue>>(
                Assign(PropertyOrField(instance, name), value) // instance.name = value
                , instance, value).Compile(); // (instance, value) => ...
        }

        public static Action<TValue> MemberStatic<T, TValue>(string name)
        {
            var value = Parameter(typeof(TValue), "value");

            return Lambda<Action<TValue>>(
                Assign(StaticPropertyOrField(typeof(T), name), value) // T.name = value
                , value).Compile(); // (value) => ...
        }
    }

    public static class Callers
    {
        public static Func<T, TArg0, TResult> Instance0<T, TArg0, TResult>(string name)
        {
            var instance = ParameterInstance<T>();
            
            return Lambda<Func<T, TArg0, TResult>>(
                Call(instance, name, null), instance // return instance.name()
            ).Compile(); // (instance) => ...
        }

        public static Func<TArg0, TResult> Static0<T, TArg0, TResult>(string name)
        {
            return Lambda<Func<TArg0, TResult>>(
                Call(Method<T>(name)) // return T.name()
            ).Compile(); // () => ...
        }

        public static Action<T, TArg0> InstanceVoid0<T, TArg0>(string name)
        {
            var instance = ParameterInstance<T>();
            
            return Lambda<Action<T, TArg0>>(
                Call(instance, name, null), instance // instance.name()
            ).Compile(); // (instance) => ...
        }

        public static Action StaticVoid0<T>(string name)
        {
            return Lambda<Action>(
                Call(Method<T>(name)) // T.name()
            ).Compile(); // () => ...
        }
        
        public static Func<T, TArg0, TResult> Instance1<T, TArg0, TResult>(string name)
        {
            var instance = ParameterInstance<T>();
            var arg0 = Parameter(typeof(TArg0), "arg0");

            return Lambda<Func<T, TArg0, TResult>>(
                Call(instance, name, null, arg0), instance, arg0 // return instance.name(arg0)
            ).Compile(); // (instance, arg0) => ...
        }

        public static Func<TArg0, TResult> Static1<T, TArg0, TResult>(string name)
        {
            var arg0 = Parameter(typeof(TArg0), "arg0");
            
            return Lambda<Func<TArg0, TResult>>(
                Call(Method<T>(name), arg0), arg0 // return T.name(arg0)
            ).Compile(); // (arg0) => ...
        }

        public static Action<T, TArg0> InstanceVoid1<T, TArg0>(string name)
        {
            var instance = ParameterInstance<T>();
            var arg0 = Parameter(typeof(TArg0), "arg0");
            
            return Lambda<Action<T, TArg0>>(
                Call(instance, name, null, arg0), instance, arg0 // instance.name(arg0)
            ).Compile(); // (instance, arg0) => ...
        }

        public static Action<TArg0> StaticVoid1<T, TArg0>(string name)
        {
            var arg0 = Parameter(typeof(TArg0), "arg0");
            
            return Lambda<Action<TArg0>>(
                Call(Method<T>(name), arg0), arg0 // T.name(arg0)
            ).Compile(); // (arg0) => ...
        }
    }
    
    internal static class ExpressionsBase
    {
        internal const BindingFlags PropertyStaticPublic = BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.Static;
        internal const BindingFlags PropertyStaticNonPublic = BindingFlags.FlattenHierarchy | BindingFlags.NonPublic | BindingFlags.IgnoreCase | BindingFlags.Static;

        // By BJurado, from http://forums.asp.net/t/1709783.aspx/1
        internal static MemberExpression StaticPropertyOrField(IReflect type, string propertyOrFieldName)
        {
            var property = type.GetProperty(propertyOrFieldName, PropertyStaticPublic);
            if (property != null)
                return Property(null, property);
                
            var field = type.GetField(propertyOrFieldName, PropertyStaticPublic);
            if (field != null)
                return Field(null, field);
                
            property = type.GetProperty(propertyOrFieldName, PropertyStaticNonPublic);
            if (property != null)
                return Property(null, property);
                
            field = type.GetField(propertyOrFieldName, PropertyStaticNonPublic);
            if (field != null)
                return Field(null, field);
                
            throw new ArgumentException($"{propertyOrFieldName} NotAMemberOfType {type}");
        }

        internal static MethodInfo Method<T>(string name)
        {
            var method = typeof(T).GetMethod(name, PropertyStaticPublic);
            if (method == null)
                method = typeof(T).GetMethod(name, PropertyStaticNonPublic);
            if (method == null)
                throw new Exception($"Could not find method {typeof(T)}::{name}");
            return method;
        }

        internal static ParameterExpression ParameterInstance<T>() => Parameter(typeof(T), "instance");
    }
}
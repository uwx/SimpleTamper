﻿using System;
using System.Diagnostics.CodeAnalysis;
using ExpressionWeave.Dummies;
using HSNXT.SimpleTamper;
using HSNXT.SimpleTamper.Expressions;

// ReSharper disable InconsistentNaming

namespace ExpressionWeave
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            Console.WriteLine("");

            var inst = new Dummy();
            
            var getter = Getters.MemberInstance<Dummy, float>("test1");
            Console.WriteLine(getter(inst)); // 5
            
            var staticGetter = Getters.MemberStatic<Dummy, float>("staticTest1");
            Console.WriteLine(staticGetter()); // 45

            var setter = Setters.MemberInstanceClass<Dummy, float>("test1");
            setter(inst, 421f);
            Console.WriteLine(getter(inst)); // 421
            
            var staticSetter = Setters.MemberStatic<Dummy, float>("staticTest1");
            staticSetter(422f);
            Console.WriteLine(staticGetter()); // 422
            
            Console.WriteLine(TamperDummy.staticTest1()); // 422
            
            TamperDummy.staticTest1(423f);
            Console.WriteLine(TamperDummy.staticTest1()); // 423

            Console.WriteLine(TamperDummy.test1(inst)); // 421
            
            TamperDummy.test1(inst, 424f);
            Console.WriteLine(TamperDummy.test1(inst)); // 424

            Console.WriteLine(TamperDummy.propTest2(inst)); // 35

            TamperDummy.propTest2(inst, 425f);
            Console.WriteLine(TamperDummy.propTest2(inst)); // 425

            Console.WriteLine(TamperDummy.staticPropTest2()); // 75

            TamperDummy.staticPropTest2(426f);
            Console.WriteLine(TamperDummy.staticPropTest2()); // 426

            Console.WriteLine(TamperDummy.staticTest3); // 85
            
            TamperDummy.staticTest3 = 427f;
            Console.WriteLine(TamperDummy.staticTest3); // 427

            Console.WriteLine(TamperDummy.staticPropTest3); // 95

            TamperDummy.staticPropTest3 = 428f;
            Console.WriteLine(TamperDummy.staticPropTest3); // 428

            TamperDummy.InstanceMethod(inst); // InstanceMethod() called
            TamperDummy.InstanceMethod(inst, 1000, 2000); // InstanceMethod(1000, 2000) called
            Console.WriteLine(TamperDummy.InstanceMethod(inst, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15)); // 120
            TamperDummy.StaticMethod(); // StaticMethod() called
            TamperDummy.StaticMethod(2000, 3000); // StaticMethod(2000, 3000) called
            Console.WriteLine(TamperDummy.StaticMethod(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15)); // 120

            var wrappedInst = new TamperDummyInst(inst);
            
            Console.WriteLine(wrappedInst.test1()); // 424
            
            wrappedInst.test1(464f);
            Console.WriteLine(wrappedInst.test1()); // 464

            Console.WriteLine(wrappedInst.test3); // 1005
            
            wrappedInst.test3 = 17875f;
            Console.WriteLine(wrappedInst.test3); // 17875

            Console.WriteLine(wrappedInst.propTest2()); // 425

            wrappedInst.propTest2(505f);
            Console.WriteLine(wrappedInst.propTest2()); // 505

            Console.WriteLine(wrappedInst.propTest3); // 995
            wrappedInst.propTest3 = 11111f;
            Console.WriteLine(wrappedInst.propTest3); // 11111

            wrappedInst.InstanceMethod(); // InstanceMethod() called
            wrappedInst.InstanceMethod(9000, 8000); // InstanceMethod(9000, 8000) called
            Console.WriteLine(wrappedInst.InstanceMethod(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 35)); // 140
            
//            var scene = new DownloadSceneDummy();
//            
//            TamperDownloadScene.Start(scene);
//            TamperDownloadScene.StaticMethod();
//
//            TamperDownloadScene.test1(scene).ShouldBe(05f);
//            TamperDownloadScene.test2(scene).ShouldBe(15f);
//
//            TamperDownloadScene.staticTest1().ShouldBe(45f);
//            TamperDownloadScene.staticTest2().ShouldBe(45f);
//
//            TamperDownloadScene.test1(scene, 420f);
//            TamperDownloadScene.test2(scene, 421f);
//            TamperDownloadScene.test1(scene).ShouldBe(420f);
//            TamperDownloadScene.test2(scene).ShouldBe(421f);
//
//            TamperDownloadScene.staticTest1(422f);
//            TamperDownloadScene.staticTest2(423f);
//            TamperDownloadScene.staticTest1().ShouldBe(422f);
//            TamperDownloadScene.staticTest2().ShouldBe(423f);

        }
    }

    [TamperClass(typeof(Dummy))]
    [SuppressMessage("ReSharper", "UnusedParameter.Global")]
    [SuppressMessage("ReSharper", "UnusedMethodReturnValue.Local")]
    public static class TamperDummy
    {
        // fields
        public static float test1(Dummy instance) => default;
        public static float test2(Dummy instance) => default;

        public static void test1(Dummy instance, float value) {}
        public static void test2(Dummy instance, float value) {}

        // properties
        public static float propTest2(Dummy instance) => default;

        public static void propTest2(Dummy instance, float value) {}

        // static fields
        public static float staticTest1() => default;
        public static float staticTest2() => default;

        public static void staticTest1(float value) {}
        public static void staticTest2(float value) {}
        
        public static float staticTest3 { get; set; }

        // static properties
        public static float staticPropTest2() => default;
        
        public static void staticPropTest2(float value) {}
        
        public static float staticPropTest3 { get; set; }

        // methods
        public static void InstanceMethod(Dummy instance) {}
        public static void InstanceMethod(Dummy instance, int ex1, int ex2) {}
        public static float InstanceMethod(Dummy instance, int a1, int a2, int a3, int a4, int a5, int a6,
            int a7, int a8, int a9,
            int a10, int a11, int a12, int a13, int a14, int a15)
            => default;
        
        // static methods
        public static void StaticMethod() {}
        public static void StaticMethod(int ex1, int ex2) {}

        public static float StaticMethod(int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9,
            int a10, int a11, int a12, int a13, int a14, int a15)
            => default;
    }
    
    // dont need to worry about conflicts for 2 reasons
    // get and set fields are separated
    // cant mix properties and methods with the same name in the same class
    
    // do need though to make sure every method signature is unique based on the arguments

    [TamperClass(typeof(Dummy))]
    [SuppressMessage("ReSharper", "UnusedParameter.Global")]
    [SuppressMessage("ReSharper", "MemberCanBeMadeStatic.Global")]
    public class TamperDummyInst
    {
        public TamperDummyInst(Dummy instance)
        {
            
        }

        // fields
        public float test1() => default;
        public float test2() => default;

        public void test1(float value) {}
        public void test2(float value) {}

        public float test3 { get; set; }
        
        // properties
        public float propTest2() => default;

        public void propTest2(float value) {}
        
        public float propTest3 { get; set; }

        // methods
        public void InstanceMethod() {}
        public void InstanceMethod(int ex1, int ex2) {}
        public float InstanceMethod(int a1, int a2, int a3, int a4, int a5, int a6,
            int a7, int a8, int a9,
            int a10, int a11, int a12, int a13, int a14, int a15)
            => default;

    }
}
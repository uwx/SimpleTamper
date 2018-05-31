// ReSharper disable UnusedMember.Local
// ReSharper disable InconsistentNaming

using System;

#pragma warning disable 169
namespace ExpressionWeave.Dummies
{
    public class Dummy
    {
        private float test1 = 05f;
        private float test2 = 15f;
        private float test3 = 1005f;

        private float propTest2 { get; set; } = 35f;
        private float propTest3 { get; set; } = 995f;

        private static float staticTest1 = 45f;
        private static float staticTest2 = 55f;
        private static float staticTest3 = 85f;

        private static float staticPropTest2 { get; set; } = 75f;
        private static float staticPropTest3 { get; set; } = 95f;

        private void InstanceMethod() 
            => Console.WriteLine("InstanceMethod() called");

        private void InstanceMethod(int ex1, int ex2) 
            => Console.WriteLine($"InstanceMethod({ex1},{ex2}) called");
        
        public float InstanceMethod(int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9, int a10, int a11, int a12, int a13, int a14, int a15)
            => a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13 + a14 + a15;
        
        private static void StaticMethod() 
            => Console.WriteLine("StaticMethod() called");

        private static void StaticMethod(int ex1, int ex2) 
            => Console.WriteLine($"StaticMethod({ex1},{ex2}) called");

        private static float StaticMethod(int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9, int a10, int a11, int a12, int a13, int a14, int a15) 
            => a1 + a2 + a3 + a4 + a5 + a6 + a7 + a8 + a9 + a10 + a11 + a12 + a13 + a14 + a15;
    }
}
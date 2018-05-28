// ReSharper disable UnusedMember.Local
// ReSharper disable InconsistentNaming

using System;

#pragma warning disable 169
namespace ExpressionWeave.Dummies
{
    public class DownloadSceneDummy
    {
        private float test1 = 05f;
        private float test2 = 15f;

        private float propTest2 { get; set; } = 35f;

        private static float staticTest1 = 45f;
        private static float staticTest2 = 55f;
        private static float staticTest3 = 85f;

        private static float staticPropTest2 { get; set; } = 75f;
        private static float staticPropTest3 { get; set; } = 95f;

        private void Start()
        {
            Console.WriteLine("Start() called");
        }
        
        private static void StaticMethod()
        {
            Console.WriteLine("StaticMethod() called");
        }
        
        private static void StaticMethod2(int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9, int a10)
        {
            Console.WriteLine($"StaticMethod2() called {a1},{a2},{a3},{a4},{a5},{a6},{a7},{a8},{a9},{a10}");
        }
    }
}
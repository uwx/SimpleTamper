using System;
using System.Linq.Expressions;

namespace WeaveExpressionDummies
{
    public class Class1
    {
        private float test1 = 05f;
        private float test2 = 15f;

        private float propTest1 { get; } = 25f;
        private float propTest2 { get; set; } = 35f;

        private static float staticTest1 = 45f;
        private static float staticTest2 = 55f;

        private static float staticPropTest1 { get; } = 65f;
        private static float staticPropTest2 { get; set; } = 75f;

        private void Start()
        {
        }
        
        private static void StaticMethod()
        {
        }

        public static void FuckShit1() { Expression<Func<Class1, float>> expression = o => o.test1; }
        public static void FuckShit2() { Expression<Func<Class1, float>> expression2 = o => o.propTest1; }
        public static void FuckShit3() { Expression<Func<float>> expression3 = () => Class1.staticTest1; }
        public static void FuckShit4() { Expression<Func<float>> expression4 = () => Class1.staticPropTest1; }
        public static void FuckShit5() { Expression<Action<Class1>> expression5 = o => o.Start(); }
        public static void FuckShit6() { Expression<Action> expression6 = () => StaticMethod(); }
    }
}
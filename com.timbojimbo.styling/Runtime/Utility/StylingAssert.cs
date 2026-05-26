#if !TJ_STYLING_STRIP_ASSERTIONS
#define TJ_STYLING_ASSERTIONS
#endif
using UnityEngine.Assertions;

namespace TimboJimbo.Styling
{
    internal class StylingAssert
    {
        const string TJAssertionSymbol = "TJ_STYLING_ASSERTIONS";
        
        [System.Diagnostics.Conditional(TJAssertionSymbol)]
        public static void IsTrue(bool condition, string message = "")
        {
            Assert.IsTrue(condition, message);
        }
        
        [System.Diagnostics.Conditional(TJAssertionSymbol)]
        public static void IsFalse(bool condition, string message = "")
        {
            Assert.IsFalse(condition, message);
        }
        
        [System.Diagnostics.Conditional(TJAssertionSymbol)]
        public static void IsNotNull<T>(T value, string message = "") where T : class
        {
            Assert.IsNotNull(value, message);
        }
        
        [System.Diagnostics.Conditional(TJAssertionSymbol)]
        public static void AreEqual<T>(T expected, T actual, string message = "")
        {
            Assert.AreEqual(expected, actual, message);
        }
    }
}

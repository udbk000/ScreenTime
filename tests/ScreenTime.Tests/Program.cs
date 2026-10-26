using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ScreenTime.Tests;

internal static class Program
{
    public static int Main()
    {
        Console.WriteLine("=== ScreenTime.Tests In-Process Test Runner ===");
        int passed = 0;
        int failed = 0;

        var testClass = new ScreenTimeCoreTests();
        var methods = typeof(ScreenTimeCoreTests)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<TestMethodAttribute>() is not null)
            .ToList();

        foreach (var method in methods)
        {
            try
            {
                testClass.Setup();
                method.Invoke(testClass, null);
                Console.WriteLine($"[PASS] {method.Name}");
                passed++;
            }
            catch (Exception ex)
            {
                var actual = ex is TargetInvocationException tie && tie.InnerException is not null ? tie.InnerException : ex;
                Console.WriteLine($"[FAIL] {method.Name}: {actual.GetType().Name} - {actual.Message}");
                failed++;
            }
            finally
            {
                testClass.Cleanup();
            }
        }

        Console.WriteLine($"=== Ergebnis: {passed} bestanden, {failed} fehlgeschlagen ===");
        return failed == 0 ? 0 : 1;
    }
}

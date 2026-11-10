using System.Reflection;
using Xunit;

namespace Vectorer.Tests.Infrastructure;

/// <summary>
/// Lightweight in-process xUnit test runner entry point for sandboxed AI agent environments
/// (such as Codex / Antigravity) where VSTest's TCP loopback socket between vstest.console and testhost is blocked.
/// Standard <c>dotnet test</c> in unsandboxed environments continues to use the xUnit VisualStudio runner normally.
/// </summary>
internal static class InProcessTestRunner
{
    public static async Task<int> Main()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        Console.WriteLine($"[InProcessTestRunner] Running tests in {assembly.GetName().Name}...");

        var passed = 0;
        var failed = 0;

        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var factAttr = method.GetCustomAttribute<FactAttribute>();
                if (factAttr is null)
                {
                    continue;
                }

                var inlineDataAttrs = method.GetCustomAttributes<InlineDataAttribute>().ToList();
                var testCases = inlineDataAttrs.Count > 0
                    ? inlineDataAttrs.Select(d => d.GetData(method).SelectMany(x => x).ToArray()).ToList()
                    : [Array.Empty<object?>()];

                foreach (var args in testCases)
                {
                    var testLabel = args.Length > 0
                        ? $"{type.Name}.{method.Name}({string.Join(", ", args)})"
                        : $"{type.Name}.{method.Name}";

                    try
                    {
                        var instance = method.IsStatic ? null : Activator.CreateInstance(type);
                        try
                        {
                            var result = method.Invoke(instance, args);
                            if (result is Task task)
                            {
                                await task.ConfigureAwait(false);
                            }

                            Console.WriteLine($"  [PASS] {testLabel}");
                            passed++;
                        }
                        finally
                        {
                            if (instance is IDisposable disposable)
                            {
                                disposable.Dispose();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        var actual = ex is TargetInvocationException tie && tie.InnerException is not null
                            ? tie.InnerException
                            : ex;
                        Console.Error.WriteLine($"  [FAIL] {testLabel}: {actual.GetType().Name} - {actual.Message}");
                        failed++;
                    }
                }
            }
        }

        Console.WriteLine($"[InProcessTestRunner] Completed {assembly.GetName().Name}: {passed} passed, {failed} failed.");
        return failed > 0 ? 1 : 0;
    }
}

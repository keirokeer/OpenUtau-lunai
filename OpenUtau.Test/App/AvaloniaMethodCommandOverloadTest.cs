using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OpenUtau.App.ViewModels;
using Xunit;

namespace OpenUtau.App {
    /// <summary>
    /// Avalonia 12 MethodToCommandConverter prefers a single-parameter overload over
    /// a parameterless one. Binding Command="{Binding Foo}" when both Foo() and Foo(T)
    /// exist then invokes Foo with a null CommandParameter and NREs on value types
    /// (see mute ToggleMute crash). Guard every ViewModel type against that pattern.
    /// </summary>
    public class AvaloniaMethodCommandOverloadTest {
        static readonly HashSet<string> AllowedExceptions = new(StringComparer.Ordinal) {
            // Add "TypeName.MethodName" only with a comment explaining why binding is impossible.
        };

        [Fact]
        public void ViewModels_HaveNoZeroAndOneParameterMethodOverloads() {
            var assembly = typeof(TrackHeaderViewModel).Assembly;
            var violations = new List<string>();

            foreach (var type in assembly.GetTypes()) {
                if (type.IsAbstract || type.IsInterface) {
                    continue;
                }
                // Nested helpers (e.g. log sinks) still get Command-bound if exposed as DataContext.
                // Limit to ViewModels (+ nested types): that is where MethodToCommandConverter binds.
                if (type.Namespace == null ||
                    (!type.Namespace.StartsWith("OpenUtau.App.ViewModels", StringComparison.Ordinal) &&
                     type.DeclaringType?.Namespace?.StartsWith("OpenUtau.App.ViewModels", StringComparison.Ordinal) != true)) {
                    continue;
                }
                var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName)
                    .Where(m => m.ReturnType == typeof(void) ||
                                m.ReturnType == typeof(System.Threading.Tasks.Task) ||
                                (m.ReturnType.IsGenericType &&
                                 m.ReturnType.GetGenericTypeDefinition() == typeof(System.Threading.Tasks.Task<>)))
                    .ToList();

                foreach (var group in methods.GroupBy(m => m.Name)) {
                    var zero = group.Where(m => m.GetParameters().Length == 0).ToList();
                    var one = group.Where(m => m.GetParameters().Length == 1).ToList();
                    string key = $"{type.FullName}.{group.Key}";
                    bool allowed = AllowedExceptions.Contains($"{type.Name}.{group.Key}") ||
                                   AllowedExceptions.Contains(key);

                    // Avalonia prefers Foo(T) over Foo() → null CommandParameter → NRE on value types.
                    if (!allowed && zero.Count >= 1 && one.Count >= 1) {
                        string oneSigs = string.Join(", ", one.Select(m =>
                            m.GetParameters()[0].ParameterType.Name));
                        violations.Add(
                            $"{key}: parameterless + single-parameter overloads ({oneSigs}). " +
                            "Rename the parameterized overload (e.g. SetX).");
                    }

                    // Several Foo(T1)/Foo(T2) without Foo(object) → undefined/unsupported method binding.
                    if (!allowed && one.Count >= 2 &&
                        one.All(m => m.GetParameters()[0].ParameterType != typeof(object))) {
                        string oneSigs = string.Join(", ", one.Select(m =>
                            m.GetParameters()[0].ParameterType.Name));
                        violations.Add(
                            $"{key}: multiple single-parameter overloads without object ({oneSigs}). " +
                            "Rename so Avalonia Command binding has a unique target.");
                    }
                }
            }

            Assert.True(violations.Count == 0,
                "Avalonia MethodToCommandConverter trap:\n" + string.Join("\n", violations));
        }
    }
}

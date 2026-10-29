using System;
using System.Collections.Generic;

namespace TaskImporter.Core.Tests;

/// <summary>
/// A test runner in eighty lines. Records failures rather than throwing on the
/// first one, so a run reports everything that is wrong at once.
/// </summary>
public sealed class TestRunner
{
    private readonly List<string> _failures = new();
    private int _passed;
    private string _current = "(none)";

    public void Test(string name, Action body)
    {
        _current = name;
        try
        {
            body();
            _passed++;
        }
        catch (AssertionException ex)
        {
            _failures.Add($"{name}\n      {ex.Message}");
        }
        catch (Exception ex)
        {
            _failures.Add($"{name}\n      threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void AreEqual<T>(T expected, T actual, string? because = null)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual)) return;

        var suffix = because is null ? "" : $" ({because})";
        throw new AssertionException(
            $"expected [{Describe(expected)}] but got [{Describe(actual)}]{suffix}");
    }

    public void IsTrue(bool condition, string message)
    {
        if (!condition) throw new AssertionException("expected true: " + message);
    }

    public void IsFalse(bool condition, string message)
    {
        if (condition) throw new AssertionException("expected false: " + message);
    }

    public void IsNull(object? value, string message)
    {
        if (value is not null) throw new AssertionException($"expected null ({message}), got [{value}]");
    }

    public void IsNotNull(object? value, string message)
    {
        if (value is null) throw new AssertionException("expected a value: " + message);
    }

    public void SequenceEqual(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        if (expected.Count == actual.Count)
        {
            var same = true;
            for (var i = 0; i < expected.Count; i++)
            {
                if (!string.Equals(expected[i], actual[i], StringComparison.Ordinal)) same = false;
            }

            if (same) return;
        }

        throw new AssertionException(
            $"expected [{string.Join(", ", expected)}] but got [{string.Join(", ", actual)}]");
    }

    private static string Describe<T>(T value) => value switch
    {
        null => "null",
        string s => s,
        _ => value.ToString() ?? "?",
    };

    public int Report(string suiteName)
    {
        Console.WriteLine();
        if (_failures.Count == 0)
        {
            Console.WriteLine($"  {suiteName}: {_passed} passed");
            return 0;
        }

        Console.WriteLine($"  {suiteName}: {_passed} passed, {_failures.Count} FAILED");
        foreach (var failure in _failures) Console.WriteLine("    x " + failure);
        return _failures.Count;
    }

    private sealed class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
    }
}

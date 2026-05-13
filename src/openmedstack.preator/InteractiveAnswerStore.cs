namespace OpenMedStack.Preator;

using System;
using System.Collections.Generic;

internal sealed class InteractiveAnswerStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _flags = new(StringComparer.OrdinalIgnoreCase);

    public string? GetValue(string key)
    {
        return _values.TryGetValue(key, out var value) ? value : null;
    }

    public string GetRequiredValue(string key)
    {
        return GetValue(key) ?? throw new InvalidOperationException($"Missing required interactive answer '{key}'.");
    }

    public bool GetFlag(string key)
    {
        return _flags.TryGetValue(key, out var value) && value;
    }

    public void SetValue(string key, string value)
    {
        _values[key] = value;
    }

    public void SetFlag(string key, bool value)
    {
        _flags[key] = value;
    }
}

namespace OpenMedStack.BioSharp.Model;

using System;
using System.Collections.Generic;

internal static class DictionaryExtensions
{
    public static T GetSafe<TKey, TState, T>(this IDictionary<TKey, T> dictionary, TKey key, Func<TKey, TState, T> valueFactory, TState state)
    {
        lock (dictionary)
        {
            if (!dictionary.ContainsKey(key))
            {
                dictionary[key] = valueFactory(key, state);
            }
        }
        return dictionary[key];
    }

    public static T GetSafe<TKey, T>(this IDictionary<TKey, T> dictionary, TKey key)
        where T : new()
    {
        lock (dictionary)
        {
            if (!dictionary.ContainsKey(key))
            {
                dictionary[key] = new T();
            }
        }
        return dictionary[key];
    }
}
using System;

namespace PgTranslateLive;

internal static class Il2CppStringHelper
{
    internal static string Read(object value)
    {
        if (value == null)
            return null;

        if (value is string managed)
            return managed;

        // Il2CppSystem.String and other wrappers expose text via ToString().
        return value.ToString();
    }

    /// <summary>
    /// Il2CppInterop marshals managed strings into Il2Cpp at invoke time.
    /// </summary>
    internal static object Write(string value) => value;

    internal static bool IsStringType(Type type) =>
        type == typeof(string) || type.FullName == "Il2CppSystem.String";
}

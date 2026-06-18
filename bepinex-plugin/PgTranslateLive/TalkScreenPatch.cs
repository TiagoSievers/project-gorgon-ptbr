using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace PgTranslateLive;

internal static class TalkScreenPatch
{
    private static readonly string[] TypeNames =
    {
        "UIInteractionController",
    };

    private static readonly (string Method, string Prefix, string Postfix)[] Methods =
    {
        ("ShowTalk", nameof(TextPatchHelper.ShowTalkPrefix), null),
        ("DisplayTalkScreen", nameof(TextPatchHelper.DisplayTalkScreenPrefix), null),
        ("UpdateFancyMenuOptions", null, nameof(TextPatchHelper.UpdateFancyMenuOptionsPostfix)),
    };

    internal static int PatchAll(Harmony harmony)
    {
        var patched = new HashSet<MethodBase>();
        var count = 0;

        foreach (var typeName in TypeNames)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null)
                continue;

            count += PatchType(harmony, type, patched);
        }

        if (count == 0)
            count = PatchByScan(harmony, patched);

        return count;
    }

    private static int PatchType(Harmony harmony, Type type, HashSet<MethodBase> patched)
    {
        var count = 0;
        foreach (var (methodName, prefixName, postfixName) in Methods)
        {
            foreach (var method in AccessTools.GetDeclaredMethods(type))
            {
                if (method.Name != methodName || !patched.Add(method))
                    continue;

                var prefix = prefixName != null
                    ? new HarmonyMethod(typeof(TextPatchHelper), prefixName)
                    : null;
                var postfix = postfixName != null
                    ? new HarmonyMethod(typeof(TextPatchHelper), postfixName)
                    : null;

                harmony.Patch(method, prefix: prefix, postfix: postfix);
                count++;
                LogAscii.LogInfo($"  patch {type.Name}.{method.Name}");
            }
        }

        return count;
    }

    private static int PatchByScan(Harmony harmony, HashSet<MethodBase> patched)
    {
        var count = 0;
        foreach (var type in AccessTools.AllTypes())
        {
            if (type.Name != "UIInteractionController")
                continue;

            count += PatchType(harmony, type, patched);
        }

        return count;
    }
}

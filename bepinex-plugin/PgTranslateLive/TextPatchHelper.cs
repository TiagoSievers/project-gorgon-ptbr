using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace PgTranslateLive;

internal static class TextPatchHelper
{
    private sealed class ChoiceTarget
    {
        internal object Item;
        internal Type ItemType;
    }

    private sealed class StringArraySlot
    {
        internal object Array;
        internal int Index;
    }

    [ThreadStatic]
    private static bool _guard;

    public static void ShowTalkPrefix(object[] __args, MethodBase __originalMethod)
    {
        if (!TranslateClient.Enabled || _guard || __args == null)
            return;

        try
        {
            _guard = true;
            TranslateShowTalkArgs(__args);
        }
        catch (Exception ex)
        {
            TalkLog.Warn($"Erro em ShowTalk: {ex.Message}");
        }
        finally
        {
            _guard = false;
        }
    }

    public static void DisplayTalkScreenPrefix(ref string text, object dialogChoices, MethodBase __originalMethod)
    {
        if (!TranslateClient.Enabled || _guard)
            return;

        try
        {
            _guard = true;
            TranslateTalkScreen(ref text, dialogChoices, LivePhase.DisplayTalkScreen);
        }
        catch (Exception ex)
        {
            TalkLog.Warn($"Erro em DisplayTalkScreen: {ex.Message}");
        }
        finally
        {
            _guard = false;
        }
    }

    public static void UpdateFancyMenuOptionsPostfix(object choices, MethodBase __originalMethod)
    {
        if (!TranslateClient.Enabled || _guard)
            return;

        try
        {
            _guard = true;
            var dummy = "";
            TranslateTalkScreen(ref dummy, choices, LivePhase.UpdateFancyMenuOptions, choicesOnly: true);
        }
        catch (Exception ex)
        {
            TalkLog.Warn($"Erro em UpdateFancyMenuOptions: {ex.Message}");
        }
        finally
        {
            _guard = false;
        }
    }

    private static void TranslateShowTalkArgs(object[] args)
    {
        var texts = new List<string>();
        var kinds = new List<TextKind>();
        var arraySlots = new List<StringArraySlot>();

        AddStringArg(args, 2, texts, kinds);
        CollectStringArrayArg(args, 5, texts, kinds, arraySlots);

        if (texts.Count == 0)
            return;

        var translated = TranslateClient.TryTranslateBatch(
            texts.ToArray(), kinds.ToArray(), LivePhase.ShowTalk);
        ApplyBatchResults(kinds, translated, args, arraySlots);
    }

    private static void TranslateTalkScreen(
        ref string text,
        object dialogChoices,
        LivePhase phase,
        bool choicesOnly = false)
    {
        var texts = new List<string>();
        var kinds = new List<TextKind>();
        var choiceTargets = new List<ChoiceTarget>();

        var hasMain = !choicesOnly && !string.IsNullOrWhiteSpace(text);
        if (hasMain)
        {
            texts.Add(text);
            kinds.Add(TextKind.Dialogue);
        }

        CollectChoiceTexts(dialogChoices, texts, kinds, choiceTargets);
        if (texts.Count == 0)
            return;

        string[] translated;
        if (phase != LivePhase.ShowTalk && TalkTurn.HasTurn)
        {
            if (phase == LivePhase.DisplayTalkScreen)
                TalkLog.Info("fluxo: Falar > Coletar > Google > Aplicar na tela (DisplayTalkScreen, sem HTTP)");
            translated = TranslateClient.ApplyFromTurn(texts.ToArray(), kinds.ToArray(), phase);
        }
        else
            translated = TranslateClient.TryTranslateBatch(texts.ToArray(), kinds.ToArray(), phase);

        if (translated.Length != texts.Count)
            return;

        var offset = 0;
        if (hasMain)
        {
            if (!string.IsNullOrEmpty(translated[0]))
            {
                text = translated[0];
                if (phase == LivePhase.DisplayTalkScreen)
                    TalkLog.ShowingTranslated(text);
            }

            offset = 1;
        }

        for (var i = 0; i < choiceTargets.Count; i++)
        {
            var result = translated[offset + i];
            if (string.IsNullOrEmpty(result))
                continue;

            var target = choiceTargets[i];
            WriteChoiceText(target.Item, target.ItemType, result);
        }
    }

    private static void AddStringArg(
        object[] args,
        int index,
        List<string> texts,
        List<TextKind> kinds)
    {
        if (index < 0 || index >= args.Length)
            return;

        var value = Il2CppStringHelper.Read(args[index]);
        if (string.IsNullOrWhiteSpace(value))
            return;

        texts.Add(value);
        kinds.Add(TextKind.Dialogue);
    }

    private static void CollectStringArrayArg(
        object[] args,
        int index,
        List<string> texts,
        List<TextKind> kinds,
        List<StringArraySlot> slots)
    {
        if (index < 0 || index >= args.Length || args[index] == null)
            return;

        CollectStringArray(args[index], texts, kinds, slots);
    }

    private static void CollectStringArray(
        object arrayValue,
        List<string> texts,
        List<TextKind> kinds,
        List<StringArraySlot> slots)
    {
        if (arrayValue is string[] strings)
        {
            for (var i = 0; i < strings.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(strings[i]))
                    continue;

                slots.Add(new StringArraySlot { Array = arrayValue, Index = i });
                texts.Add(strings[i]);
                kinds.Add(TextKind.Choice);
            }

            return;
        }

        var arrayType = arrayValue.GetType();
        var lengthProp = arrayType.GetProperty("Length", BindingFlags.Public | BindingFlags.Instance);
        var getItem = arrayType.GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance);
        if (lengthProp == null || getItem == null)
            return;

        var length = (int)lengthProp.GetValue(arrayValue)!;
        for (var i = 0; i < length; i++)
        {
            var current = Il2CppStringHelper.Read(getItem.Invoke(arrayValue, new object[] { i }));
            if (string.IsNullOrWhiteSpace(current))
                continue;

            slots.Add(new StringArraySlot { Array = arrayValue, Index = i });
            texts.Add(current);
            kinds.Add(TextKind.Choice);
        }
    }

    private static void ApplyBatchResults(
        List<TextKind> kinds,
        string[] translated,
        object[] args,
        List<StringArraySlot> arraySlots,
        int dialogueArgIndex = 2)
    {
        if (translated.Length != kinds.Count)
            return;

        for (var i = 0; i < kinds.Count; i++)
        {
            if (kinds[i] == TextKind.Dialogue && !string.IsNullOrEmpty(translated[i]))
                args[dialogueArgIndex] = Il2CppStringHelper.Write(translated[i]);
        }

        var slotCursor = 0;
        for (var i = 0; i < kinds.Count; i++)
        {
            if (kinds[i] != TextKind.Choice)
                continue;

            if (slotCursor >= arraySlots.Count)
                break;

            if (!string.IsNullOrEmpty(translated[i]))
                WriteStringArraySlot(arraySlots[slotCursor], translated[i]);

            slotCursor++;
        }
    }

    private static void WriteStringArraySlot(StringArraySlot slot, string translated)
    {
        if (slot.Array is string[] strings)
        {
            strings[slot.Index] = translated;
            return;
        }

        var arrayType = slot.Array.GetType();
        var setItem = arrayType.GetMethod("set_Item", BindingFlags.Public | BindingFlags.Instance);
        setItem?.Invoke(slot.Array, new object[] { slot.Index, Il2CppStringHelper.Write(translated) });
    }

    private static void CollectChoiceTexts(
        object dialogChoices,
        List<string> texts,
        List<TextKind> kinds,
        List<ChoiceTarget> targets)
    {
        if (dialogChoices == null)
            return;

        var listType = dialogChoices.GetType();

        if (dialogChoices is IEnumerable managedEnumerable && listType.Namespace?.StartsWith("System") == true)
        {
            foreach (var item in managedEnumerable)
                TryAddChoice(item, texts, kinds, targets);
            return;
        }

        var countProp = listType.GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
        var getItem = listType.GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance);
        if (countProp == null || getItem == null)
            return;

        var count = (int)countProp.GetValue(dialogChoices)!;
        for (var i = 0; i < count; i++)
        {
            var item = getItem.Invoke(dialogChoices, new object[] { i });
            TryAddChoice(item, texts, kinds, targets);
        }
    }

    private static void TryAddChoice(
        object item,
        List<string> texts,
        List<TextKind> kinds,
        List<ChoiceTarget> targets)
    {
        if (item == null)
            return;

        var itemType = item.GetType();
        var label = ReadChoiceText(item, itemType);
        if (string.IsNullOrWhiteSpace(label))
            return;

        texts.Add(label);
        kinds.Add(TextKind.Choice);
        targets.Add(new ChoiceTarget { Item = item, ItemType = itemType });
    }

    private static string? ReadChoiceText(object item, Type itemType)
    {
        var prop = itemType.GetProperty("choiceLabel", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop?.CanRead == true)
        {
            var value = Il2CppStringHelper.Read(prop.GetValue(item));
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        var getter = itemType.GetMethod("get_choiceLabel", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (getter != null)
        {
            var value = Il2CppStringHelper.Read(getter.Invoke(item, null));
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        foreach (var field in itemType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (!Il2CppStringHelper.IsStringType(field.FieldType))
                continue;

            if (!field.Name.Contains("choiceLabel", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = Il2CppStringHelper.Read(field.GetValue(item));
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static void WriteChoiceText(object item, Type itemType, string translated)
    {
        var written = false;

        if (itemType.GetProperty("choiceLabel", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) is { } prop)
        {
            if (prop.CanWrite)
            {
                prop.SetValue(item, Il2CppStringHelper.Write(translated));
                written = true;
            }
        }

        if (!written)
        {
            var setter = itemType.GetMethod("set_choiceLabel", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (setter != null)
            {
                setter.Invoke(item, new[] { Il2CppStringHelper.Write(translated) });
                written = true;
            }
        }

        if (!written)
        {
            foreach (var field in itemType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!Il2CppStringHelper.IsStringType(field.FieldType)
                    || !field.Name.Contains("choiceLabel", StringComparison.OrdinalIgnoreCase))
                    continue;

                field.SetValue(item, Il2CppStringHelper.Write(translated));
                written = true;
                break;
            }
        }

        if (!written)
            TalkLog.Warn($"Nao foi possivel gravar choiceLabel em {itemType.Name}");
    }
}

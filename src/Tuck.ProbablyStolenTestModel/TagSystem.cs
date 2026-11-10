// Ported from dependencies/probably-stolen/decompiled/TagSystem.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Original data/geometry semantics; Unity serialization attributes and diagnostic logging removed.
#nullable disable
using System;
using System.Collections.Generic;

public class TagSystem
{
    public Dictionary<string, TagState> dict { get; private set; }

    public TagSystem()
    {
        dict = new Dictionary<string, TagState>();
    }

    public TagSystem(TagSystem tagSystem)
    {
        dict = new Dictionary<string, TagState>(tagSystem.dict);
    }

    public TagSystem Clear()
    {
        dict.Clear();
        return this;
    }

    public TagSystem Clone()
    {
        return new TagSystem(this);
    }

    public bool IsTag(string tag)
    {
        return dict.GetValueOrDefault(tag)?.IsEnabled() ?? false;
    }

    private bool InitTagString(string tag)
    {
        if (!dict.ContainsKey(tag))
        {
            dict.Add(tag, new TagState(tag, StringToDisplayString(tag)));
            return true;
        }
        return false;
    }

    public TagState GetTag(string tag)
    {
        InitTagString(tag);
        return dict[tag];
    }

    public TagSystem EnableTag(string tag)
    {
        InitTagString(tag);
        dict[tag].Enable();
        return this;
    }

    public TagSystem DisableTag(string tag)
    {
        InitTagString(tag);
        dict[tag].Disable();
        return this;
    }

    public TagSystem ToggleTag(string tag)
    {
        InitTagString(tag);
        dict[tag].ToggleEnabled();
        return this;
    }

    private bool InitTagLong(string tag)
    {
        if (!dict.ContainsKey(tag))
        {
            dict.Add(tag, new TagState(tag, tag));
            return true;
        }
        return false;
    }

    public bool IsTag(long targetTag)
    {
        string key = LongToString(targetTag);
        return dict.GetValueOrDefault(key)?.IsEnabled() ?? false;
    }

    public TagState GetTag(long targetTag)
    {
        string text = LongToString(targetTag);
        InitTagLong(text);
        return dict[text];
    }

    public TagSystem EnableTag(long targetTag)
    {
        string text = LongToString(targetTag);
        InitTagLong(text);
        dict[text].Enable();
        return this;
    }

    public TagSystem DisableTag(long targetTag)
    {
        string text = LongToString(targetTag);
        InitTagLong(text);
        dict[text].Disable();
        return this;
    }

    public TagSystem ToggleTag(long targetTag)
    {
        string text = LongToString(targetTag);
        InitTagLong(text);
        dict[text].ToggleEnabled();
        return this;
    }

    private bool InitTagEnum(string tag, string displayTag)
    {
        if (!dict.ContainsKey(tag))
        {
            dict.Add(tag, new TagState(tag, displayTag));
            return true;
        }
        dict.GetValueOrDefault(tag)?.UpdateReadableName(displayTag);
        return false;
    }

    public bool IsTag<T>(T targetTag) where T : Enum
    {
        string key = EnumToString(targetTag);
        TagState valueOrDefault = dict.GetValueOrDefault(key);
        valueOrDefault?.UpdateReadableName(EnumToDisplayString(targetTag));
        return valueOrDefault?.IsEnabled() ?? false;
    }

    public TagState GetTag<T>(T targetTag) where T : Enum
    {
        string text = EnumToString(targetTag);
        InitTagEnum(text, EnumToDisplayString(targetTag));
        return dict[text];
    }

    public TagSystem EnableTag<T>(T targetTag) where T : Enum
    {
        string text = EnumToString(targetTag);
        InitTagEnum(text, EnumToDisplayString(targetTag));
        dict[text].Enable();
        return this;
    }

    public TagSystem DisableTag<T>(T targetTag) where T : Enum
    {
        string text = EnumToString(targetTag);
        InitTagEnum(text, EnumToDisplayString(targetTag));
        dict[text].Disable();
        return this;
    }

    public TagSystem ToggleTag<T>(T targetTag) where T : Enum
    {
        string text = EnumToString(targetTag);
        InitTagEnum(text, EnumToDisplayString(targetTag));
        dict[text].ToggleEnabled();
        return this;
    }

    public static string EnumToString<T>(T enumValue) where T : Enum
    {
        return "TYPE-ENUM_" + typeof(T).Name + "_" + Convert.ToDecimal(enumValue).ToString("0");
    }

    public static string EnumToDisplayString<T>(T enumValue) where T : Enum
    {
        return "TYPE-ENUM_" + typeof(T).Name + "_" + Enum.GetName(typeof(T), enumValue);
    }

    public static string LongToString(long longValue)
    {
        return "TYPE-NUMBER_" + longValue;
    }

    public static string StringToDisplayString(string stringValue)
    {
        return "TYPE-STRING_" + stringValue;
    }
}


// Ported from dependencies/probably-stolen/decompiled/TagState.cs.
// Assembly-CSharp MVID: 911508a6-e0eb-4cc0-91e8-e14b16ea28d7.
// Original data/geometry semantics; Unity serialization attributes and diagnostic logging removed.
#nullable disable
using System;

[Serializable]
public class TagState : DirectoryEntry
{
    public bool valueEnabled;
    private string internalValueString;

    public int valueInt;

    public float valueFloat;

    public long valueLong;

    public double valueDouble;

    public bool valueBool;
    public string identifier { get; set; }
    public string identifierName { get; set; }

    public string valueString
    {
        get
        {
            return internalValueString;
        }
        set
        {
            internalValueString = value;
            if (internalValueString == null)
            {
                internalValueString = "";
            }
        }
    }

    public TagState(string identifier, string identifierName)
    {
        this.identifier = identifier;
        this.identifierName = identifierName;
        valueString = "";
    }

    public TagState Clone()
    {
        TagState tagState = new TagState(identifier, identifierName);
        tagState.SetEnabled(valueEnabled);
        tagState.Set(this);
        return tagState;
    }

    public TagState(string key)
        : this(key, TagSystem.StringToDisplayString(key))
    {
    }

    public bool IsEnabled()
    {
        return valueEnabled;
    }

    public TagState SetEnabled(bool enabled)
    {
        valueEnabled = enabled;
        return this;
    }

    public TagState Enable()
    {
        valueEnabled = true;
        return this;
    }

    public TagState Disable()
    {
        valueEnabled = false;
        return this;
    }

    public TagState ToggleEnabled()
    {
        valueEnabled = !valueEnabled;
        return this;
    }

    public TagState Destroy()
    {
        Reset();
        valueEnabled = false;
        return this;
    }

    public string GetString()
    {
        return valueString;
    }

    public int GetInt()
    {
        return valueInt;
    }

    public float GetFloat()
    {
        return valueFloat;
    }

    public long GetLong()
    {
        return valueLong;
    }

    public double GetDouble()
    {
        return valueDouble;
    }

    public bool GetBool()
    {
        return valueBool;
    }

    public TagState SetString(string val)
    {
        valueString = val;
        return this;
    }

    public TagState SetInt(int val)
    {
        valueInt = val;
        return this;
    }

    public TagState SetFloat(float val)
    {
        valueFloat = val;
        return this;
    }

    public TagState SetLong(long val)
    {
        valueLong = val;
        return this;
    }

    public TagState SetDouble(double val)
    {
        valueDouble = val;
        return this;
    }

    public TagState SetBool(bool val)
    {
        valueBool = val;
        return this;
    }

    public TagState Set(TagState state)
    {
        valueString = state.valueString;
        valueInt = state.valueInt;
        valueFloat = state.valueFloat;
        valueLong = state.valueLong;
        valueDouble = state.valueDouble;
        valueBool = state.valueBool;
        return this;
    }

    public TagState Reset()
    {
        valueString = "";
        valueInt = 0;
        valueFloat = 0f;
        valueLong = 0L;
        valueDouble = 0.0;
        valueBool = false;
        return this;
    }

    public void UpdateReadableName(string keyReadableName)
    {
        if (keyReadableName == null)
        {
            keyReadableName = identifier;
        }
        if (identifierName != keyReadableName)
        {
            identifierName = keyReadableName;
        }
    }

    public override string ToString()
    {
        return "TAG_" + identifierName;
    }
}


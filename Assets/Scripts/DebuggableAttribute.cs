using System;

/// <summary>
/// Attribute to mark a field or property for inclusion in the DebugManager's runtime UI.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class DebuggableAttribute : Attribute
{
    public string DisplayName { get; }

    public DebuggableAttribute(string displayName = null)
    {
        DisplayName = displayName;
    }
} 
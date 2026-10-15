using System;

/// <summary>
/// Attribute to mark a method that provides a dictionary of key-value pairs for the DebugManager.
/// The method must have no parameters and return a Dictionary<string, object>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class DebuggableProviderAttribute : Attribute
{
} 
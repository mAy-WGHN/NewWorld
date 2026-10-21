namespace Shared.Messaging;

/// <summary>Classifies a message type. Every message carries exactly one; a class change happens here only.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class SensitivityAttribute(SensitivityClass sensitivityClass) : Attribute
{
    public SensitivityClass Class { get; } = sensitivityClass;
}

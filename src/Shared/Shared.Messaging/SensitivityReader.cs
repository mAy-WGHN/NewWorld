using System.Reflection;

namespace Shared.Messaging;

public static class SensitivityReader
{
    public static SensitivityClass Of(Type messageType) =>
        messageType.GetCustomAttribute<SensitivityAttribute>(inherit: false)?.Class ?? SensitivityClass.Unknown;
}

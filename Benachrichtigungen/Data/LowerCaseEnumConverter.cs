using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Benachrichtigungen.Data;

/// <summary>Speichert Enums als Kleinbuchstaben-String (virus, bug, ...), passend zu den Bestandsdaten.</summary>
public class LowerCaseEnumConverter<TEnum>() : ValueConverter<TEnum, string>(
    v => v.ToString().ToLowerInvariant(),
    v => Enum.Parse<TEnum>(v, true))
    where TEnum : struct, Enum;

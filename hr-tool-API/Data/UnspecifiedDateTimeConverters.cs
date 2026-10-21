using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HrTool_API.Data;

// Spalten sind "timestamp without time zone"; Npgsql verweigert dafür DateTime mit Kind=Utc.
// Im Code wird durchgängig UTC gehalten, in der DB liegt der UTC-Wert ohne Zonenangabe.

public class UnspecifiedDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

public class NullableUnspecifiedDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
    v => v == null ? null : DateTime.SpecifyKind(v.Value, DateTimeKind.Unspecified),
    v => v == null ? null : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));

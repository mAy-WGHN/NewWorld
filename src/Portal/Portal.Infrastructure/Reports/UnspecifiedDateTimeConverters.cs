using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Portal.Infrastructure.Reports;

// The columns are "timestamp without time zone"; Npgsql refuses a DateTime of kind Utc for them. The code holds UTC throughout,
// the database the UTC value without a zone. (The wall-clock time noticedAt is stored as sent and also comes back marked as UTC, like in the old service.)

public sealed class UnspecifiedDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

public sealed class NullableUnspecifiedDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
    v => v == null ? null : DateTime.SpecifyKind(v.Value, DateTimeKind.Unspecified),
    v => v == null ? null : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));

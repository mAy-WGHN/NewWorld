using System.Text;
using System.Text.RegularExpressions;

namespace Shared.Messaging;

/// <summary>
/// Naming convention for topics (events) and queues (commands): kebab-case of the message type name.
/// A breaking change is a new message <c>...V2</c> and therefore a new destination; the old one runs in parallel.
/// </summary>
public static partial class MessagingNames
{
    public static string For(Type messageType) => FromTypeName(messageType.Name);

    public static string FromTypeName(string typeName)
    {
        if (!TypeNameRegex().IsMatch(typeName))
        {
            throw new ArgumentException($"'{typeName}' is not a valid message type name.", nameof(typeName));
        }

        var builder = new StringBuilder(typeName.Length + 4);
        for (var i = 0; i < typeName.Length; i++)
        {
            var c = typeName[i];
            if (i > 0 && char.IsUpper(c))
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex TypeNameRegex();
}

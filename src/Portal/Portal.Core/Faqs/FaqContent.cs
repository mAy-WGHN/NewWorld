using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Portal.Core.Faqs;

/// <summary>
/// The answer of a FAQ entry as a rich-text document (TipTap/ProseMirror JSON): checked against a list of allowed elements, cleaned of everything
/// else, and turned into a Markdown text for searching and for old clients. Only what is listed here is ever stored or shown: the nodes
/// <c>doc, paragraph, heading, bulletList, orderedList, listItem, blockquote, horizontalRule, table, tableRow, tableHeader, tableCell, image, text,
/// hardBreak</c> and the marks <c>bold, italic, underline, strike, link, textStyle, highlight</c>. Links are http, https or mailto only, images are the
/// portal's own FAQ images only, colours are hex values and font sizes come from a fixed list. Nothing is run or interpreted as HTML anywhere.
/// </summary>
public static partial class FaqContent
{
    public const int MaxDepth = 12;
    public const int MaxNodes = 5000;
    public const int MaxTextChars = 50_000;
    public const int MaxLinkLength = 2048;
    public const int MaxAltLength = 300;

    public static readonly IReadOnlySet<string> FontSizes = new HashSet<string>(StringComparer.Ordinal) { "12px", "14px", "16px", "18px", "20px", "24px", "28px", "32px" };
    private static readonly HashSet<string> Alignments = new(StringComparer.Ordinal) { "left", "center", "right", "justify" };

    private static readonly string[] BlockTypes = ["paragraph", "heading", "bulletList", "orderedList", "blockquote", "horizontalRule", "table", "image"];
    private static readonly string[] CellBlockTypes = ["paragraph", "heading", "bulletList", "orderedList", "blockquote", "horizontalRule", "image"];
    private static readonly string[] InlineTypes = ["text", "hardBreak"];

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexColor();

    /// <summary>An image of the portal (absolute or relative); only the path <c>/api/faqs/images/{guid}</c> counts.</summary>
    [GeneratedRegex("^(?:https?://[^/\\s?#]+)?/api/faqs/images/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex OwnImage();

    // ------------------------------------------------------------------ checking and cleaning

    /// <summary>
    /// Checks the document and builds a clean copy (unknown attributes are dropped, unknown elements and bad values are refused).
    /// </summary>
    /// <returns><c>null</c> and the reason (in German, for the person) if the document is not acceptable.</returns>
    public static JsonObject? TryClean(JsonNode? input, out string? error)
    {
        error = null;
        if (input is not JsonObject root || root["type"]?.GetValue<string>() != "doc")
        {
            error = "Die Antwort hat kein gültiges Format.";
            return null;
        }

        var state = new Walk();
        try
        {
            var clean = CleanNode(root, state, 0, ["doc"], BlockTypes);
            if (!HasContent(clean))
            {
                error = "Bitte gib eine Antwort ein.";
                return null;
            }

            return clean;
        }
        catch (RejectedException ex)
        {
            error = ex.Message;
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or InvalidCastException)
        {
            error = "Die Antwort hat kein gültiges Format.";
            return null;
        }
    }

    private sealed class Walk
    {
        public int Nodes;
        public int Chars;
    }

    private sealed class RejectedException(string message) : Exception(message);

    private static RejectedException Reject(string message) => new(message);

    private static JsonObject CleanNode(JsonNode? node, Walk walk, int depth, string[] types, string[] allowedChildren, bool inCell = false)
    {
        if (node is not JsonObject source)
        {
            throw Reject("Die Antwort hat kein gültiges Format.");
        }

        if (depth > MaxDepth)
        {
            throw Reject("Die Antwort ist zu tief verschachtelt.");
        }

        if (++walk.Nodes > MaxNodes)
        {
            throw Reject("Die Antwort ist zu umfangreich.");
        }

        var type = source["type"]?.GetValue<string>() ?? throw Reject("Die Antwort hat kein gültiges Format.");
        if (!types.Contains(type, StringComparer.Ordinal))
        {
            throw Reject($"Das Element \"{type}\" ist in einer Antwort nicht erlaubt.");
        }

        var clean = new JsonObject { ["type"] = type };
        var attrs = source["attrs"] as JsonObject;
        switch (type)
        {
            case "doc":
                clean["content"] = CleanChildren(source, walk, depth, allowedChildren, minimum: 1);
                break;

            case "paragraph":
                SetAlign(clean, attrs);
                SetInline(clean, source, walk, depth);
                break;

            case "heading":
                var level = IntAttr(attrs, "level") ?? throw Reject("Eine Überschrift braucht eine Ebene.");
                if (level is < 1 or > 3)
                {
                    throw Reject("Überschriften gibt es nur in den Ebenen 1 bis 3.");
                }

                var headingAttrs = new JsonObject { ["level"] = level };
                if (AlignOf(attrs) is { } headingAlign)
                {
                    headingAttrs["textAlign"] = headingAlign;
                }

                clean["attrs"] = headingAttrs;
                SetInline(clean, source, walk, depth);
                break;

            case "bulletList":
                clean["content"] = CleanChildren(source, walk, depth, ["listItem"], minimum: 1);
                break;

            case "orderedList":
                if (IntAttr(attrs, "start") is { } start)
                {
                    if (start is < 1 or > 100_000)
                    {
                        throw Reject("Die Nummerierung beginnt mit einer ungültigen Zahl.");
                    }

                    clean["attrs"] = new JsonObject { ["start"] = start };
                }

                clean["content"] = CleanChildren(source, walk, depth, ["listItem"], minimum: 1);
                break;

            case "listItem":
                clean["content"] = CleanChildren(source, walk, depth, inCell ? CellBlockTypes : BlockTypes.Where(t => t != "table").ToArray(), minimum: 1);
                break;

            case "blockquote":
                clean["content"] = CleanChildren(source, walk, depth, BlockTypes.Where(t => t != "table").ToArray(), minimum: 1);
                break;

            case "horizontalRule":
                break;

            case "table":
                clean["content"] = CleanChildren(source, walk, depth, ["tableRow"], minimum: 1);
                break;

            case "tableRow":
                clean["content"] = CleanChildren(source, walk, depth, ["tableHeader", "tableCell"], minimum: 1);
                break;

            case "tableHeader":
            case "tableCell":
                var cellAttrs = new JsonObject();
                foreach (var name in new[] { "colspan", "rowspan" })
                {
                    if (IntAttr(attrs, name) is { } span)
                    {
                        if (span is < 1 or > 20)
                        {
                            throw Reject("Eine Tabellenzelle erstreckt sich über zu viele Zeilen oder Spalten.");
                        }

                        cellAttrs[name] = span;
                    }
                }

                if (cellAttrs.Count > 0)
                {
                    clean["attrs"] = cellAttrs;
                }

                clean["content"] = CleanChildren(source, walk, depth, CellBlockTypes, minimum: 1, inCell: true);
                break;

            case "image":
                var src = StringAttr(attrs, "src");
                if (src is null || !OwnImage().IsMatch(src))
                {
                    throw Reject("Bilder müssen aus der Bilderliste des Portals stammen.");
                }

                var imageAttrs = new JsonObject { ["src"] = src };
                if (StringAttr(attrs, "alt") is { Length: > 0 } alt)
                {
                    imageAttrs["alt"] = alt.Length > MaxAltLength ? alt[..MaxAltLength] : alt;
                }

                clean["attrs"] = imageAttrs;
                break;

            case "text":
                var text = source["text"]?.GetValue<string>();
                if (string.IsNullOrEmpty(text))
                {
                    throw Reject("Die Antwort enthält einen leeren Text.");
                }

                walk.Chars += text.Length;
                if (walk.Chars > MaxTextChars)
                {
                    throw Reject("Die Antwort ist zu lang.");
                }

                clean["text"] = text;
                if (CleanMarks(source["marks"]) is { Count: > 0 } marks)
                {
                    clean["marks"] = marks;
                }

                break;

            case "hardBreak":
                break;
        }

        return clean;
    }

    private static JsonArray CleanChildren(JsonObject parent, Walk walk, int depth, string[] allowed, int minimum, bool inCell = false)
    {
        var children = parent["content"] as JsonArray;
        if (children is null || children.Count < minimum)
        {
            throw Reject("Die Antwort hat kein gültiges Format.");
        }

        var result = new JsonArray();
        foreach (var child in children)
        {
            result.Add(CleanNode(child, walk, depth + 1, allowed, allowed, inCell));
        }

        return result;
    }

    private static void SetInline(JsonObject clean, JsonObject source, Walk walk, int depth)
    {
        if (source["content"] is not JsonArray content || content.Count == 0)
        {
            return; // an empty paragraph or heading
        }

        var result = new JsonArray();
        foreach (var child in content)
        {
            result.Add(CleanNode(child, walk, depth + 1, InlineTypes, InlineTypes));
        }

        clean["content"] = result;
    }

    private static void SetAlign(JsonObject clean, JsonObject? attrs)
    {
        if (AlignOf(attrs) is { } align)
        {
            clean["attrs"] = new JsonObject { ["textAlign"] = align };
        }
    }

    private static string? AlignOf(JsonObject? attrs)
    {
        var value = StringAttr(attrs, "textAlign");
        if (value is null)
        {
            return null;
        }

        return Alignments.Contains(value) ? value : throw Reject("Die Ausrichtung des Textes ist nicht erlaubt.");
    }

    private static JsonArray? CleanMarks(JsonNode? marks)
    {
        if (marks is null)
        {
            return null;
        }

        if (marks is not JsonArray list)
        {
            throw Reject("Die Antwort hat kein gültiges Format.");
        }

        var result = new JsonArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in list)
        {
            if (node is not JsonObject mark || mark["type"]?.GetValue<string>() is not { } type)
            {
                throw Reject("Die Antwort hat kein gültiges Format.");
            }

            if (!seen.Add(type))
            {
                continue;
            }

            var attrs = mark["attrs"] as JsonObject;
            var clean = new JsonObject { ["type"] = type };
            switch (type)
            {
                case "bold":
                case "italic":
                case "underline":
                case "strike":
                    break;

                case "link":
                    var href = StringAttr(attrs, "href");
                    if (href is null || href.Length > MaxLinkLength || !IsSafeLink(href))
                    {
                        throw Reject("Links müssen mit http, https oder mailto beginnen.");
                    }

                    clean["attrs"] = new JsonObject { ["href"] = href };
                    break;

                case "textStyle":
                    var style = new JsonObject();
                    if (StringAttr(attrs, "color") is { } color)
                    {
                        style["color"] = IsHex(color) ? color : throw Reject("Eine Textfarbe ist nicht erlaubt.");
                    }

                    if (StringAttr(attrs, "fontSize") is { } size)
                    {
                        style["fontSize"] = FontSizes.Contains(size) ? size : throw Reject("Diese Schriftgröße ist nicht erlaubt.");
                    }

                    if (style.Count == 0)
                    {
                        continue; // nothing left of the mark
                    }

                    clean["attrs"] = style;
                    break;

                case "highlight":
                    if (StringAttr(attrs, "color") is { } background)
                    {
                        clean["attrs"] = new JsonObject { ["color"] = IsHex(background) ? background : throw Reject("Eine Hervorhebungsfarbe ist nicht erlaubt.") };
                    }

                    break;

                default:
                    throw Reject($"Die Formatierung \"{type}\" ist in einer Antwort nicht erlaubt.");
            }

            result.Add(clean);
        }

        return result;
    }

    private static bool IsHex(string value) => HexColor().IsMatch(value);

    private static bool IsSafeLink(string href) =>
        Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto";

    private static string? StringAttr(JsonObject? attrs, string name) => attrs?[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int? IntAttr(JsonObject? attrs, string name)
    {
        if (attrs?[name] is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<int>(out var number) ? number : value.TryGetValue<long>(out var big) && big is >= int.MinValue and <= int.MaxValue ? (int)big : null;
    }

    // ------------------------------------------------------------------ what is in the document

    /// <summary>Whether the document says something: text that is more than blanks, an image, or a table with something in it.</summary>
    public static bool HasContent(JsonNode? node) => node switch
    {
        JsonObject o when o["type"]?.GetValue<string>() is "image" => true,
        JsonObject o when o["type"]?.GetValue<string>() is "text" => !string.IsNullOrWhiteSpace(o["text"]?.GetValue<string>()),
        JsonObject o => o["content"] is JsonArray children && children.Any(HasContent),
        _ => false,
    };

    /// <summary>A document for a text without formatting (old clients that only know the field <c>description</c>): one paragraph per blank-line block.</summary>
    public static JsonObject FromPlainText(string? text)
    {
        var paragraphs = new JsonArray();
        foreach (var block in Regex.Split((text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal), "\n{2,}"))
        {
            if (string.IsNullOrWhiteSpace(block))
            {
                continue;
            }

            var inline = new JsonArray();
            var lines = block.Trim('\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    inline.Add(new JsonObject { ["type"] = "hardBreak" });
                }

                if (lines[i].Length > 0)
                {
                    inline.Add(new JsonObject { ["type"] = "text", ["text"] = lines[i] });
                }
            }

            paragraphs.Add(new JsonObject { ["type"] = "paragraph", ["content"] = inline });
        }

        if (paragraphs.Count == 0)
        {
            paragraphs.Add(new JsonObject { ["type"] = "paragraph" });
        }

        return new JsonObject { ["type"] = "doc", ["content"] = paragraphs };
    }

    // ------------------------------------------------------------------ Markdown for searching and old clients

    /// <summary>
    /// The document as Markdown: headings, lists, quotes, bold, italic, links and images keep their form; what Markdown cannot say (underline,
    /// colours, sizes, alignment) is left out, tables become lines with bars.
    /// </summary>
    public static string ToMarkdown(JsonNode? doc)
    {
        var builder = new StringBuilder();
        Blocks(doc?["content"] as JsonArray, builder, string.Empty);
        return builder.ToString().Trim();
    }

    private static void Blocks(JsonArray? blocks, StringBuilder builder, string prefix)
    {
        if (blocks is null)
        {
            return;
        }

        var first = true;
        foreach (var node in blocks.OfType<JsonObject>())
        {
            if (!first)
            {
                builder.Append(prefix.TrimEnd()).Append('\n');
            }

            first = false;
            switch (node["type"]?.GetValue<string>())
            {
                case "paragraph":
                    builder.Append(prefix).Append(InlineText(node["content"] as JsonArray, prefix)).Append('\n');
                    break;
                case "heading":
                    var level = node["attrs"]?["level"]?.GetValue<int>() ?? 1;
                    builder.Append(prefix).Append(new string('#', Math.Clamp(level, 1, 3))).Append(' ').Append(InlineText(node["content"] as JsonArray, prefix)).Append('\n');
                    break;
                case "bulletList":
                case "orderedList":
                    List(node, builder, prefix);
                    break;
                case "blockquote":
                    Blocks(node["content"] as JsonArray, builder, prefix + "> ");
                    break;
                case "horizontalRule":
                    builder.Append(prefix).Append("---\n");
                    break;
                case "image":
                    builder.Append(prefix).Append("![").Append(node["attrs"]?["alt"]?.GetValue<string>() ?? "Bild").Append("](").Append(node["attrs"]?["src"]?.GetValue<string>()).Append(")\n");
                    break;
                case "table":
                    Table(node, builder, prefix);
                    break;
            }
        }
    }

    private static void List(JsonObject list, StringBuilder builder, string prefix)
    {
        var ordered = list["type"]?.GetValue<string>() == "orderedList";
        var number = list["attrs"]?["start"]?.GetValue<int>() ?? 1;
        foreach (var item in (list["content"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var marker = ordered ? $"{number++.ToString(CultureInfo.InvariantCulture)}. " : "- ";
            var inner = new StringBuilder();
            Blocks(item["content"] as JsonArray, inner, string.Empty);
            var lines = inner.ToString().TrimEnd('\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                builder.Append(prefix).Append(i == 0 ? marker : new string(' ', marker.Length)).Append(lines[i]).Append('\n');
            }
        }
    }

    private static void Table(JsonObject table, StringBuilder builder, string prefix)
    {
        foreach (var row in (table["content"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var cells = (row["content"] as JsonArray ?? []).OfType<JsonObject>()
                .Select(c => string.Join(' ', (c["content"] as JsonArray ?? []).OfType<JsonObject>().Select(b => InlineText(b["content"] as JsonArray, string.Empty))).Trim());
            builder.Append(prefix).Append("| ").Append(string.Join(" | ", cells)).Append(" |\n");
        }
    }

    private static string InlineText(JsonArray? content, string prefix)
    {
        var builder = new StringBuilder();
        foreach (var node in (content ?? []).OfType<JsonObject>())
        {
            switch (node["type"]?.GetValue<string>())
            {
                case "hardBreak":
                    builder.Append("  \n").Append(prefix);
                    break;
                case "text":
                    builder.Append(Marked(node));
                    break;
            }
        }

        return builder.ToString();
    }

    private static string Marked(JsonObject text)
    {
        var value = text["text"]?.GetValue<string>() ?? string.Empty;
        var marks = (text["marks"] as JsonArray ?? []).OfType<JsonObject>().Select(m => m["type"]?.GetValue<string>()).ToHashSet();
        if (marks.Contains("bold"))
        {
            value = $"**{value}**";
        }

        if (marks.Contains("italic"))
        {
            value = $"*{value}*";
        }

        var link = (text["marks"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(m => m["type"]?.GetValue<string>() == "link");
        return link?["attrs"]?["href"]?.GetValue<string>() is { } href ? $"[{value}]({href})" : value;
    }
}

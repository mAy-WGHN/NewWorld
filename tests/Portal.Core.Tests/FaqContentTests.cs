using System.Text.Json.Nodes;
using Portal.Core.Faqs;

namespace Portal.Core.Tests;

public class FaqContentTests
{
    private const string Image = "https://portal.example/api/faqs/images/3f2b8c1e-1111-4222-8333-944455556666";

    private static JsonNode Parse(string json) => JsonNode.Parse(json)!;

    private static string Doc(string blocks) => $$$"""{"type":"doc","content":[{{{blocks}}}]}""";

    private static string P(string text, string? marks = null) =>
        $$$"""{"type":"paragraph","content":[{"type":"text","text":"{{{text}}}"{{{(marks is null ? "" : ",\"marks\":" + marks)}}}}]}""";

    private static (JsonObject? Clean, string? Error) Clean(string json)
    {
        var clean = FaqContent.TryClean(Parse(json), out var error);
        return (clean, error);
    }

    // ------------------------------------------------------------------ what is accepted

    [Fact]
    public void A_plain_paragraph_is_accepted_and_comes_back_unchanged()
    {
        var (clean, error) = Clean(Doc(P("Hallo")));

        error.ShouldBeNull();
        clean!.ToJsonString().ShouldBe(Doc(P("Hallo")));
    }

    [Fact]
    public void Every_allowed_element_and_mark_is_accepted()
    {
        var table = """{"type":"table","content":[{"type":"tableRow","content":[{"type":"tableHeader","attrs":{"colspan":1,"rowspan":1},"content":[{"type":"paragraph","content":[{"type":"text","text":"Kopf"}]}]},{"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"Zelle"}]}]}]}]}""";
        var list = """{"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Punkt"}]}]}]}""";
        var numbered = """{"type":"orderedList","attrs":{"start":3},"content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"Drei"}]}]}]}""";
        var heading = """{"type":"heading","attrs":{"level":2,"textAlign":"center"},"content":[{"type":"text","text":"Titel"}]}""";
        var quote = """{"type":"blockquote","content":[{"type":"paragraph","content":[{"type":"text","text":"Zitat"}]}]}""";
        var rule = """{"type":"horizontalRule"}""";
        var image = $$$"""{"type":"image","attrs":{"src":"{{{Image}}}","alt":"Bild"}}""";
        var marks = """[{"type":"bold"},{"type":"italic"},{"type":"underline"},{"type":"strike"},{"type":"link","attrs":{"href":"https://example.org"}},{"type":"textStyle","attrs":{"color":"#ff0000","fontSize":"24px"}},{"type":"highlight","attrs":{"color":"#ffff00"}}]""";
        var withBreak = """{"type":"paragraph","attrs":{"textAlign":"right"},"content":[{"type":"text","text":"a"},{"type":"hardBreak"},{"type":"text","text":"b"}]}""";

        var (clean, error) = Clean(Doc(string.Join(',', heading, P("Alles", marks), withBreak, list, numbered, quote, rule, image, table)));

        error.ShouldBeNull();
        clean.ShouldNotBeNull();
    }

    [Fact]
    public void Unknown_attributes_are_dropped_and_null_values_ignored()
    {
        var (clean, _) = Clean(Doc("""{"type":"paragraph","attrs":{"textAlign":null,"class":"x","onclick":"alert(1)"},"content":[{"type":"text","text":"a","marks":[{"type":"link","attrs":{"href":"https://example.org","target":"_blank","rel":"x","class":"y"}}]}]}"""));

        clean!.ToJsonString().ShouldBe(Doc("""{"type":"paragraph","content":[{"type":"text","text":"a","marks":[{"type":"link","attrs":{"href":"https://example.org"}}]}]}"""));
    }

    [Fact]
    public void An_empty_text_style_mark_disappears_and_marks_are_not_doubled()
    {
        var (clean, _) = Clean(Doc(P("a", """[{"type":"textStyle","attrs":{"color":null,"fontSize":null}},{"type":"bold"},{"type":"bold"}]""")));

        clean!.ToJsonString().ShouldBe(Doc(P("a", """[{"type":"bold"}]""")));
    }

    // ------------------------------------------------------------------ what is refused

    [Theory]
    [InlineData("""{"type":"script","content":[{"type":"text","text":"x"}]}""")]
    [InlineData("""{"type":"codeBlock","content":[{"type":"text","text":"x"}]}""")]
    [InlineData("""{"type":"html","content":[]}""")]
    public void An_element_that_is_not_in_the_list_is_refused(string block)
    {
        var (clean, error) = Clean(Doc(block));

        clean.ShouldBeNull();
        error!.ShouldContain("nicht erlaubt");
    }

    [Theory]
    [InlineData("""[{"type":"code"}]""")]
    [InlineData("""[{"type":"script"}]""")]
    public void A_mark_that_is_not_in_the_list_is_refused(string marks) =>
        Clean(Doc(P("x", marks))).Clean.ShouldBeNull();

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("vbscript:x")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.org")]
    [InlineData("//example.org")]
    [InlineData("example.org")]
    [InlineData("")]
    public void Links_with_other_schemes_are_refused(string href)
    {
        var (clean, error) = Clean(Doc(P("x", $$$"""[{"type":"link","attrs":{"href":"{{{href}}}"}}]""")));

        clean.ShouldBeNull();
        error!.ShouldContain("http");
    }

    [Theory]
    [InlineData("http://example.org")]
    [InlineData("https://example.org/pfad?x=1#a")]
    [InlineData("mailto:personal@example.org")]
    public void Links_with_http_https_or_mailto_are_accepted(string href) =>
        Clean(Doc(P("x", $$$"""[{"type":"link","attrs":{"href":"{{{href}}}"}}]"""))).Clean.ShouldNotBeNull();

    [Theory]
    [InlineData("""{"type":"image","attrs":{"src":"https://evil.example/pixel.png"}}""")]
    [InlineData("""{"type":"image","attrs":{"src":"javascript:alert(1)"}}""")]
    [InlineData("""{"type":"image","attrs":{"src":"data:image/png;base64,AAAA"}}""")]
    [InlineData("""{"type":"image","attrs":{"src":"/api/faqs/images/nicht-eine-guid"}}""")]
    [InlineData("""{"type":"image","attrs":{"src":"https://portal.example/api/faqs/images/3f2b8c1e-1111-4222-8333-944455556666/../x"}}""")]
    [InlineData("""{"type":"image","attrs":{"src":"https://portal.example/api/faqs/images/3f2b8c1e-1111-4222-8333-944455556666?x=1"}}""")]
    [InlineData("""{"type":"image"}""")]
    public void Only_images_of_the_portal_are_accepted(string image)
    {
        var (clean, error) = Clean(Doc(image));

        clean.ShouldBeNull();
        error!.ShouldContain("Bilderliste");
    }

    [Fact]
    public void A_relative_portal_image_is_accepted()
    {
        Clean(Doc("""{"type":"image","attrs":{"src":"/api/faqs/images/3f2b8c1e-1111-4222-8333-944455556666"}}""")).Clean.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("red")]
    [InlineData("rgb(1,2,3)")]
    [InlineData("#12")]
    [InlineData("#gggggg")]
    [InlineData("#ff0000; background:url(x)")]
    [InlineData("expression(alert(1))")]
    public void Colours_have_to_be_hex_values(string color)
    {
        Clean(Doc(P("x", $$$"""[{"type":"textStyle","attrs":{"color":"{{{color}}}"}}]"""))).Clean.ShouldBeNull();
        Clean(Doc(P("x", $$$"""[{"type":"highlight","attrs":{"color":"{{{color}}}"}}]"""))).Clean.ShouldBeNull();
    }

    [Theory]
    [InlineData("#fff")]
    [InlineData("#FF00aa")]
    public void Hex_colours_are_accepted(string color) =>
        Clean(Doc(P("x", $$$"""[{"type":"textStyle","attrs":{"color":"{{{color}}}"}}]"""))).Clean.ShouldNotBeNull();

    [Theory]
    [InlineData("10px")]
    [InlineData("1000px")]
    [InlineData("2em")]
    [InlineData("16px; color:red")]
    [InlineData("large")]
    public void Only_sizes_from_the_list_are_accepted(string size) =>
        Clean(Doc(P("x", $$$"""[{"type":"textStyle","attrs":{"fontSize":"{{{size}}}"}}]"""))).Clean.ShouldBeNull();

    [Fact]
    public void Every_size_of_the_list_is_accepted()
    {
        foreach (var size in FaqContent.FontSizes)
        {
            Clean(Doc(P("x", $$$"""[{"type":"textStyle","attrs":{"fontSize":"{{{size}}}"}}]"""))).Clean.ShouldNotBeNull(size);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void Headings_have_levels_one_to_three(int level) =>
        Clean(Doc($$$"""{"type":"heading","attrs":{"level":{{{level}}}},"content":[{"type":"text","text":"x"}]}""")).Clean.ShouldBeNull();

    [Fact]
    public void A_heading_without_a_level_and_a_wrong_alignment_are_refused()
    {
        Clean(Doc("""{"type":"heading","content":[{"type":"text","text":"x"}]}""")).Clean.ShouldBeNull();
        Clean(Doc("""{"type":"paragraph","attrs":{"textAlign":"justify-all"},"content":[{"type":"text","text":"x"}]}""")).Clean.ShouldBeNull();
    }

    [Fact]
    public void The_start_of_a_numbered_list_and_the_span_of_a_cell_have_limits()
    {
        Clean(Doc("""{"type":"orderedList","attrs":{"start":0},"content":[{"type":"listItem","content":[{"type":"paragraph"}]}]}""")).Clean.ShouldBeNull();
        Clean(Doc("""{"type":"table","content":[{"type":"tableRow","content":[{"type":"tableCell","attrs":{"colspan":99},"content":[{"type":"paragraph","content":[{"type":"text","text":"x"}]}]}]}]}""")).Clean.ShouldBeNull();
    }

    [Fact]
    public void Tables_cannot_be_nested_and_a_row_holds_only_cells()
    {
        var nested = """{"type":"table","content":[{"type":"tableRow","content":[{"type":"tableCell","content":[{"type":"table","content":[{"type":"tableRow","content":[{"type":"tableCell","content":[{"type":"paragraph"}]}]}]}]}]}]}""";
        var wrongRow = """{"type":"table","content":[{"type":"paragraph","content":[{"type":"text","text":"x"}]}]}""";

        Clean(Doc(nested)).Clean.ShouldBeNull();
        Clean(Doc(wrongRow)).Clean.ShouldBeNull();
    }

    [Fact]
    public void Inline_elements_cannot_stand_where_blocks_belong_and_the_other_way_round()
    {
        Clean(Doc("""{"type":"text","text":"lose"}""")).Clean.ShouldBeNull();
        Clean(Doc("""{"type":"paragraph","content":[{"type":"paragraph"}]}""")).Clean.ShouldBeNull();
    }

    [Fact]
    public void An_empty_answer_is_refused_with_a_plain_message()
    {
        var (clean, error) = Clean(Doc("""{"type":"paragraph"},{"type":"paragraph","content":[{"type":"text","text":"   "}]}"""));

        clean.ShouldBeNull();
        error.ShouldBe("Bitte gib eine Antwort ein.");
    }

    [Fact]
    public void An_image_alone_is_an_answer()
    {
        Clean(Doc($$$"""{"type":"image","attrs":{"src":"{{{Image}}}"}}""")).Clean.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("{}")]
    [InlineData("""{"type":"paragraph"}""")]
    [InlineData("""{"type":"doc"}""")]
    [InlineData("""{"type":"doc","content":[]}""")]
    [InlineData("""{"type":"doc","content":"x"}""")]
    [InlineData("""{"type":"doc","content":[{"type":7}]}""")]
    [InlineData("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":5}]}]}""")]
    public void Anything_that_is_not_a_document_is_refused_without_an_error(string json)
    {
        var clean = FaqContent.TryClean(json.Length == 0 ? null : Parse(json), out var error);

        clean.ShouldBeNull();
        error.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void An_empty_text_node_is_refused()
    {
        Clean(Doc("""{"type":"paragraph","content":[{"type":"text","text":""}]}""")).Clean.ShouldBeNull();
    }

    // ------------------------------------------------------------------ limits

    [Fact]
    public void Too_deep_nesting_is_refused()
    {
        var open = string.Concat(Enumerable.Repeat("""{"type":"blockquote","content":[""", FaqContent.MaxDepth + 2));
        var close = new string(']', FaqContent.MaxDepth + 2).Replace("]", "]}");

        var (clean, error) = Clean(Doc(open + P("x") + close));

        clean.ShouldBeNull();
        error!.ShouldContain("verschachtelt");
    }

    [Fact]
    public void Too_many_nodes_are_refused()
    {
        var blocks = string.Join(',', Enumerable.Repeat(P("x"), (FaqContent.MaxNodes / 2) + 10));

        var (clean, error) = Clean(Doc(blocks));

        clean.ShouldBeNull();
        error!.ShouldContain("umfangreich");
    }

    [Fact]
    public void Too_much_text_is_refused()
    {
        var (clean, error) = Clean(Doc(P(new string('a', FaqContent.MaxTextChars + 1))));

        clean.ShouldBeNull();
        error!.ShouldContain("zu lang");
    }

    [Fact]
    public void A_very_long_link_is_refused_and_a_very_long_alt_text_is_cut()
    {
        Clean(Doc(P("x", $$$"""[{"type":"link","attrs":{"href":"https://example.org/{{{new string('a', FaqContent.MaxLinkLength)}}}"}}]"""))).Clean.ShouldBeNull();
        var (clean, _) = Clean(Doc($$$"""{"type":"image","attrs":{"src":"{{{Image}}}","alt":"{{{new string('b', 1000)}}}"}}"""));
        clean!["content"]![0]!["attrs"]!["alt"]!.GetValue<string>().Length.ShouldBe(FaqContent.MaxAltLength);
    }

    // ------------------------------------------------------------------ what is in a document

    [Fact]
    public void A_document_has_content_with_text_an_image_or_a_cell_with_text()
    {
        FaqContent.HasContent(Parse(Doc(P("a")))).ShouldBeTrue();
        FaqContent.HasContent(Parse(Doc($$$"""{"type":"image","attrs":{"src":"{{{Image}}}"}}"""))).ShouldBeTrue();
        FaqContent.HasContent(Parse(Doc("""{"type":"paragraph"}"""))).ShouldBeFalse();
        FaqContent.HasContent(Parse(Doc(P("  ")))).ShouldBeFalse();
        FaqContent.HasContent(null).ShouldBeFalse();
    }

    // ------------------------------------------------------------------ text from old clients

    [Fact]
    public void A_text_becomes_one_paragraph_per_blank_line_block_with_line_breaks_inside()
    {
        var doc = FaqContent.FromPlainText("Erste Zeile\nzweite Zeile\n\nNeuer Absatz\r\n\r\n\r\nNoch einer");

        var blocks = doc["content"]!.AsArray();
        blocks.Count.ShouldBe(3);
        blocks[0]!["content"]!.AsArray().Select(n => n!["type"]!.GetValue<string>()).ShouldBe(["text", "hardBreak", "text"]);
        FaqContent.HasContent(doc).ShouldBeTrue();
    }

    [Fact]
    public void A_text_of_old_clients_is_a_valid_document_even_if_it_looks_like_code()
    {
        var doc = FaqContent.FromPlainText("<script>alert(1)</script> [x](javascript:alert(1))");

        FaqContent.TryClean(doc, out var error).ShouldNotBeNull(error);
        doc["content"]![0]!["content"]![0]!["text"]!.GetValue<string>().ShouldStartWith("<script>"); // stays text
    }

    [Fact]
    public void An_empty_text_gives_a_document_with_an_empty_paragraph()
    {
        FaqContent.HasContent(FaqContent.FromPlainText("")).ShouldBeFalse();
        FaqContent.HasContent(FaqContent.FromPlainText(null)).ShouldBeFalse();
    }

    // ------------------------------------------------------------------ Markdown for searching and old clients

    [Fact]
    public void The_Markdown_keeps_headings_lists_quotes_bold_italic_links_and_images()
    {
        var doc = Parse(Doc(string.Join(',',
            """{"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Titel"}]}""",
            P("fett", """[{"type":"bold"}]"""),
            """{"type":"paragraph","content":[{"type":"text","text":"schräg","marks":[{"type":"italic"}]},{"type":"text","text":" und "},{"type":"text","text":"Link","marks":[{"type":"link","attrs":{"href":"https://example.org"}}]}]}""",
            """{"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"eins"}]}]},{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"zwei"}]}]}]}""",
            """{"type":"orderedList","attrs":{"start":3},"content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"drei"}]}]}]}""",
            """{"type":"blockquote","content":[{"type":"paragraph","content":[{"type":"text","text":"zitiert"}]}]}""",
            """{"type":"horizontalRule"}""",
            $$$"""{"type":"image","attrs":{"src":"{{{Image}}}","alt":"Skizze"}}""")));

        var markdown = FaqContent.ToMarkdown(doc);

        markdown.ShouldContain("## Titel");
        markdown.ShouldContain("**fett**");
        markdown.ShouldContain("*schräg* und [Link](https://example.org)");
        markdown.ShouldContain("- eins\n- zwei");
        markdown.ShouldContain("3. drei");
        markdown.ShouldContain("> zitiert");
        markdown.ShouldContain("---");
        markdown.ShouldContain($"![Skizze]({Image})");
    }

    [Fact]
    public void The_Markdown_has_the_image_address_so_that_the_images_of_an_entry_can_be_found()
    {
        var markdown = FaqContent.ToMarkdown(Parse(Doc($$$"""{"type":"image","attrs":{"src":"{{{Image}}}"}}""")));

        FaqImageFormat.ReferencedImages(markdown).ShouldBe([Guid.Parse("3f2b8c1e-1111-4222-8333-944455556666")]);
    }

    [Fact]
    public void What_Markdown_cannot_say_is_left_out_and_tables_become_lines()
    {
        var doc = Parse(Doc(
            P("bunt", """[{"type":"underline"},{"type":"textStyle","attrs":{"color":"#ff0000","fontSize":"24px"}},{"type":"highlight","attrs":{"color":"#ffff00"}}]""") + "," +
            """{"type":"table","content":[{"type":"tableRow","content":[{"type":"tableHeader","content":[{"type":"paragraph","content":[{"type":"text","text":"A"}]}]},{"type":"tableHeader","content":[{"type":"paragraph","content":[{"type":"text","text":"B"}]}]}]},{"type":"tableRow","content":[{"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"1"}]}]},{"type":"tableCell","content":[{"type":"paragraph","content":[{"type":"text","text":"2"}]}]}]}]}"""));

        var markdown = FaqContent.ToMarkdown(doc);

        markdown.ShouldContain("bunt");
        markdown.ShouldNotContain("ff0000");
        markdown.ShouldContain("| A | B |\n| 1 | 2 |");
    }

    [Fact]
    public void A_line_break_stays_a_line_break()
    {
        var markdown = FaqContent.ToMarkdown(FaqContent.FromPlainText("eins\nzwei"));

        markdown.ShouldBe("eins  \nzwei");
    }

    [Fact]
    public void The_Markdown_of_nothing_is_empty()
    {
        FaqContent.ToMarkdown(null).ShouldBe(string.Empty);
        FaqContent.ToMarkdown(FaqContent.FromPlainText("")).ShouldBe(string.Empty);
    }
}

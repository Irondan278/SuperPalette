using System;
using System.Collections.Generic;
using System.Linq;
using TaskImporter.Core;
using TaskImporter.Core.Clipboard;
using TaskImporter.Core.Enrichment;
using TaskImporter.Core.Parsing;
using TaskImporter.Core.Routing;
using TaskImporter.Core.Timing;
using TaskImporter.SuperProductivity;

namespace TaskImporter.Core.Tests;

public static class Program
{
    // A fixed "today" so weekday and relative-date tests never depend on when
    // they run. 2026-09-22 is a Tuesday.
    private static readonly DateOnly Today = new(2026, 9, 22);

    private static readonly ReferenceSnapshot Reference = new(
        projects:
        [
            new NamedItem("p-acme", "Acme IT"),
            new NamedItem("p-inbox", "Inbox"),
            new NamedItem("p-home", "Home"),
        ],
        tags:
        [
            new NamedItem("t-onsite", "onsite"),
            new NamedItem("t-network", "network"),
            new NamedItem("t-waiting", "waiting on someone"),
        ]);

    private static QuickAddParser NewParser() => new(Reference, () => Today);

    public static int Main()
    {
        Console.WriteLine("TaskImporter test suite");

        var failures = 0;
        failures += ParserTests();
        failures += DateTests();
        failures += DurationTests();
        failures += ClipboardTests();
        failures += TitleTests();
        failures += DraftTests();
        failures += RoutingTests();
        failures += RequestTests();
        failures += WireTests();
        failures += TimingTests();

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? "All suites passed."
            : $"{failures} test(s) failed.");

        return failures == 0 ? 0 : 1;
    }

    private static int ParserTests()
    {
        var t = new TestRunner();
        var parser = NewParser();

        t.Test("plain text is the title", () =>
        {
            var r = parser.Parse("Replace UPS batteries");
            t.AreEqual("Replace UPS batteries", r.Title);
            t.IsNull(r.ProjectId, "no project given");
            t.IsNull(r.DueDay, "no due date given");
        });

        t.Test("the worked example from the plan", () =>
        {
            var r = parser.Parse("Replace UPS batteries +Acme IT #onsite @friday 45m // rack 3, APC 1500");
            t.AreEqual("Replace UPS batteries", r.Title);
            t.AreEqual("p-acme", r.ProjectId);
            t.AreEqual("Acme IT", r.ProjectName);
            t.SequenceEqual(["t-onsite"], r.TagIds);
            t.AreEqual(new DateOnly(2026, 9, 25), r.DueDay!.Value);
            t.AreEqual(2_700_000L, r.TimeEstimateMs!.Value);
            t.AreEqual("rack 3, APC 1500", r.Notes);
        });

        t.Test("multi-word project resolves without quoting", () =>
        {
            var r = parser.Parse("Check switch +Acme IT");
            t.AreEqual("p-acme", r.ProjectId);
            t.AreEqual("Check switch", r.Title);
        });

        t.Test("greedy match stops at a word boundary", () =>
        {
            var partial = new QuickAddParser(
                new ReferenceSnapshot([new NamedItem("p-c", "Acme")], []), () => Today);
            var r = partial.Parse("Do thing +Acme IT");
            t.AreEqual("p-c", r.ProjectId);
            t.AreEqual("Do thing IT", r.Title, "the stray word stays in the title");
        });

        t.Test("multi-word tag resolves", () =>
        {
            var r = parser.Parse("Chase invoice #waiting on someone");
            t.SequenceEqual(["t-waiting"], r.TagIds);
            t.AreEqual("Chase invoice", r.Title);
        });

        t.Test("several tags accumulate", () =>
        {
            var r = parser.Parse("Patch firewall #network #onsite");
            t.SequenceEqual(["t-network", "t-onsite"], r.TagIds);
        });

        t.Test("a repeated tag is not duplicated", () =>
        {
            var r = parser.Parse("Patch firewall #network #network");
            t.AreEqual(1, r.TagIds.Count);
        });

        t.Test("unknown tag warns and stays in the title", () =>
        {
            var r = parser.Parse("Order parts #newtag");
            t.AreEqual(0, r.TagIds.Count);
            t.SequenceEqual(["newtag"], r.UnresolvedTags);
            t.AreEqual("Order parts #newtag", r.Title);
            t.IsTrue(r.HasWarnings, "an unresolved tag is a warning");
        });

        t.Test("unknown project warns and stays in the title", () =>
        {
            var r = parser.Parse("Order parts +Nowhere");
            t.IsNull(r.ProjectId, "nothing matched");
            t.AreEqual("Nowhere", r.UnresolvedProject);
            t.AreEqual("Order parts +Nowhere", r.Title);
        });

        t.Test("C# in a title is not a tag", () =>
        {
            var r = parser.Parse("Learn C# properly");
            t.AreEqual("Learn C# properly", r.Title);
            t.AreEqual(0, r.UnresolvedTags.Count);
        });

        t.Test("an email address is not a due date", () =>
        {
            var r = parser.Parse("Email rob@example.com about the quote");
            t.AreEqual("Email rob@example.com about the quote", r.Title);
            t.IsNull(r.DueDay, "@example.com is not a date");
        });

        t.Test("an unparseable @token stays in the title", () =>
        {
            var r = parser.Parse("Ping @someone about it");
            t.AreEqual("Ping @someone about it", r.Title);
            t.IsNull(r.DueDay, "someone is not a date");
        });

        t.Test("backslash escapes a token", () =>
        {
            var r = parser.Parse(@"Write \#hashtag guide");
            t.AreEqual("Write #hashtag guide", r.Title);
            t.AreEqual(0, r.UnresolvedTags.Count);
        });

        t.Test("due date with a time becomes DueWithTime", () =>
        {
            var r = parser.Parse("Standup @tomorrow 9am");
            t.IsNull(r.DueDay, "the timed value wins");
            t.IsNotNull(r.DueWithTime, "a time was given");
            t.AreEqual(new DateOnly(2026, 9, 23), DateOnly.FromDateTime(r.DueWithTime!.Value.DateTime));
            t.AreEqual(9, r.DueWithTime!.Value.Hour);
            t.AreEqual("Standup", r.Title);
        });

        t.Test("notes separator ignores the slashes in a URL", () =>
        {
            var r = parser.Parse("Read https://example.com/docs // later");
            t.AreEqual("later", r.Notes);
            t.IsNotNull(r.Url, "the URL survived");
            t.AreEqual("https://example.com/docs", r.Url!.ToString());
        });

        t.Test("a bare URL is flagged as the title", () =>
        {
            var r = parser.Parse("https://www.zebra.com/us/en/products/mobile-computers/tc53.html");
            t.IsTrue(r.TitleIsUrl, "the whole line was a link");
            t.IsNotNull(r.Url, "and it parsed");
        });

        t.Test("a URL with other words is not the title", () =>
        {
            var r = parser.Parse("Read https://example.com later");
            t.IsFalse(r.TitleIsUrl, "there were other words");
        });

        t.Test("empty input is harmless", () =>
        {
            var r = parser.Parse("   ");
            t.AreEqual("", r.Title);
            t.IsFalse(r.HasWarnings, "nothing to warn about");
        });

        t.Test("null input is harmless", () =>
        {
            var r = parser.Parse(null);
            t.AreEqual("", r.Title);
        });

        t.Test("only the first estimate counts", () =>
        {
            var r = parser.Parse("Do thing 45m 2h");
            t.AreEqual(2_700_000L, r.TimeEstimateMs!.Value);
            t.AreEqual("Do thing 2h", r.Title);
        });

        t.Test("Describe summarises the line", () =>
        {
            var r = parser.Parse("Task +Acme IT #onsite @friday 45m");
            var text = r.Describe();
            t.IsTrue(text.Contains("Acme IT"), "names the project: " + text);
            t.IsTrue(text.Contains("#onsite"), "names the tag: " + text);
            t.IsTrue(text.Contains("45m"), "names the estimate: " + text);
        });

        return t.Report("parser");
    }

    private static int DateTests()
    {
        var t = new TestRunner();

        void Parses(string token, DateOnly expected) =>
            t.Test($"@{token}", () =>
            {
                t.IsTrue(DateGrammar.TryParseDate(token, Today, out var date), token + " should parse");
                t.AreEqual(expected, date);
            });

        Parses("today", Today);
        Parses("tomorrow", new DateOnly(2026, 9, 23));
        Parses("tmr", new DateOnly(2026, 9, 23));
        Parses("friday", new DateOnly(2026, 9, 25));
        Parses("fri", new DateOnly(2026, 9, 25));
        Parses("monday", new DateOnly(2026, 9, 28));
        Parses("tuesday", Today);            // today is a Tuesday
        Parses("+3d", new DateOnly(2026, 9, 25));
        Parses("3d", new DateOnly(2026, 9, 25));
        Parses("2w", new DateOnly(2026, 10, 6));
        Parses("eow", new DateOnly(2026, 9, 25));
        Parses("eom", new DateOnly(2026, 9, 30));
        Parses("2026-10-05", new DateOnly(2026, 10, 5));
        Parses("oct5", new DateOnly(2026, 10, 5));
        Parses("5oct", new DateOnly(2026, 10, 5));

        t.Test("a month-day already past rolls to next year", () =>
        {
            t.IsTrue(DateGrammar.TryParseDate("jan3", Today, out var date), "should parse");
            t.AreEqual(new DateOnly(2027, 1, 3), date);
        });

        t.Test("nonsense does not parse", () =>
        {
            t.IsFalse(DateGrammar.TryParseDate("someone", Today, out _), "not a date");
            t.IsFalse(DateGrammar.TryParseDate("example.com", Today, out _), "not a date");
            t.IsFalse(DateGrammar.TryParseDate("", Today, out _), "not a date");
        });

        void Time(string token, int hour, int minute) =>
            t.Test($"time {token}", () =>
            {
                t.IsTrue(DateGrammar.TryParseTime(token, out var time), token + " should parse");
                t.AreEqual(hour, time.Hour);
                t.AreEqual(minute, time.Minute);
            });

        Time("9am", 9, 0);
        Time("9pm", 21, 0);
        Time("12am", 0, 0);
        Time("12pm", 12, 0);
        Time("14:30", 14, 30);
        Time("9:30pm", 21, 30);
        Time("1430", 14, 30);

        t.Test("a bare number is not a time", () =>
            t.IsFalse(DateGrammar.TryParseTime("45", out _), "45 is an estimate, not a time"));

        return t.Report("dates");
    }

    private static int DurationTests()
    {
        var t = new TestRunner();

        void Duration(string token, long expectedMs) =>
            t.Test($"estimate {token}", () =>
            {
                t.IsTrue(DurationGrammar.TryParse(token, out var ms), token + " should parse");
                t.AreEqual(expectedMs, ms);
            });

        Duration("45m", 2_700_000);
        Duration("2h", 7_200_000);
        Duration("1h30m", 5_400_000);
        Duration("90min", 5_400_000);
        Duration("1.5h", 5_400_000);
        Duration("1d", 28_800_000);

        t.Test("bare numbers and words do not parse", () =>
        {
            t.IsFalse(DurationGrammar.TryParse("45", out _), "no unit");
            t.IsFalse(DurationGrammar.TryParse("h", out _), "no number");
            t.IsFalse(DurationGrammar.TryParse("home", out _), "not a duration");
            t.IsFalse(DurationGrammar.TryParse("", out _), "empty");
        });

        return t.Report("durations");
    }

    private static int ClipboardTests()
    {
        var t = new TestRunner();

        const string cfHtml = """
            Version:0.9
            StartHTML:00000097
            EndHTML:00000250
            StartFragment:00000131
            EndFragment:00000214
            <html><body>
            <!--StartFragment--><a href="https://example.com/tc53">Zebra TC53 &amp; friends</a><!--EndFragment-->
            </body></html>
            """;

        t.Test("anchor text and href come out of CF_HTML", () =>
        {
            var link = HtmlClipboardParser.Parse(cfHtml);
            t.AreEqual("https://example.com/tc53", link.Href);
            t.AreEqual("Zebra TC53 & friends", link.Text);
            t.IsTrue(link.HasText, "there is text");
        });

        t.Test("nested markup inside the anchor is flattened", () =>
        {
            var html = "<!--StartFragment--><a href='/x'><span>Hello</span> <b>world</b></a><!--EndFragment-->";
            var link = HtmlClipboardParser.Parse(html);
            t.AreEqual("Hello world", link.Text);
        });

        t.Test("two anchors means no single link", () =>
        {
            var html = "<!--StartFragment--><a href='/a'>A</a><a href='/b'>B</a><!--EndFragment-->";
            var link = HtmlClipboardParser.Parse(html);
            t.IsFalse(link.HasText, "ambiguous selection");
        });

        t.Test("no anchor is handled", () =>
        {
            t.IsFalse(HtmlClipboardParser.Parse("<!--StartFragment-->plain<!--EndFragment-->").HasText, "no link");
            t.IsFalse(HtmlClipboardParser.Parse(null).HasText, "null");
            t.IsFalse(HtmlClipboardParser.Parse("").HasText, "empty");
        });

        t.Test("entities decode", () =>
        {
            t.AreEqual("a & b", HtmlText.Decode("a &amp; b"));
            t.AreEqual("caf\u00e9", HtmlText.Decode("caf&#233;"));
            t.AreEqual("caf\u00e9", HtmlText.Decode("caf&#xE9;"));
            t.AreEqual("50% & rising", HtmlText.Decode("50% &amp; rising"));
            t.AreEqual("a & b", HtmlText.Decode("a & b"), "a bare ampersand survives");
        });

        return t.Report("clipboard");
    }

    private static int TitleTests()
    {
        var t = new TestRunner();

        t.Test("title tag is read", () =>
            t.AreEqual("Hello", PageTitleExtractor.Extract("<html><head><title>Hello</title></head></html>")));

        t.Test("og:title wins over title", () =>
        {
            const string html = """
                <head><meta property="og:title" content="The good one">
                <title>The noisy one | Site</title></head>
                """;
            t.AreEqual("The good one", PageTitleExtractor.Extract(html));
        });

        t.Test("og:title with content first also works", () =>
        {
            const string html = """<meta content="Reversed" property="og:title">""";
            t.AreEqual("Reversed", PageTitleExtractor.Extract(html));
        });

        t.Test("whitespace is collapsed and entities decoded", () =>
            t.AreEqual("A & B", PageTitleExtractor.Extract("<title>\n  A &amp;\t B  </title>")));

        t.Test("no title returns null", () =>
        {
            t.IsNull(PageTitleExtractor.Extract("<html><body>nothing</body></html>"), "no title tag");
            t.IsNull(PageTitleExtractor.Extract(""), "empty");
        });

        t.Test("a site-name suffix is trimmed", () =>
            t.AreEqual("Zebra TC53", PageTitleExtractor.TrimSiteSuffix("Zebra TC53 | Zebra", "www.zebra.com")));

        t.Test("a legitimate dash is left alone", () =>
            t.AreEqual("Before - After",
                PageTitleExtractor.TrimSiteSuffix("Before - After", "example.com")));

        t.Test("the tidy URL fallback reads well", () =>
        {
            var url = new Uri("https://github.com/super-productivity/super-productivity");
            t.AreEqual("github.com \u2014 super productivity", CaptureContext.DescribeUrl(url));
        });

        t.Test("a bare host falls back to the host", () =>
            t.AreEqual("example.com", CaptureContext.DescribeUrl(new Uri("https://www.example.com/"))));

        return t.Report("titles");
    }

    private static int DraftTests()
    {
        var t = new TestRunner();
        var parser = NewParser();

        t.Test("a captured link takes its title from the page", () =>
        {
            var context = DraftFactory.Capture("https://example.com/tc53", parser);
            context.PageTitle = "Zebra TC53";
            var draft = DraftFactory.Build(context);

            t.AreEqual("Zebra TC53", draft.Title);
            t.IsNotNull(draft.Notes, "the URL went to the notes");
            t.IsTrue(draft.Notes!.StartsWith("https://example.com/tc53", StringComparison.Ordinal),
                "URL is the first line: " + draft.Notes);
        });

        t.Test("anchor text is used when there is no page title", () =>
        {
            var context = DraftFactory.Capture("https://example.com/tc53", parser, anchorText: "TC53 datasheet");
            var draft = DraftFactory.Build(context);
            t.AreEqual("TC53 datasheet", draft.Title);
        });

        t.Test("the page title beats the anchor text", () =>
        {
            var context = DraftFactory.Capture("https://example.com/x", parser, anchorText: "click here");
            context.PageTitle = "The real title";
            t.AreEqual("The real title", DraftFactory.Build(context).Title);
        });

        t.Test("with neither, the URL is tidied", () =>
        {
            var context = DraftFactory.Capture("https://github.com/super-productivity", parser);
            t.AreEqual("github.com \u2014 super productivity", DraftFactory.Build(context).Title);
        });

        t.Test("a link inside a sentence leaves the words alone", () =>
        {
            var context = DraftFactory.Capture("Read https://example.com/docs before Friday", parser);
            var draft = DraftFactory.Build(context);
            t.AreEqual("Read before Friday", draft.Title);
            t.IsTrue(draft.Notes!.Contains("https://example.com/docs"), "link moved to notes");
        });

        t.Test("the URL sits above the user's own notes", () =>
        {
            var context = DraftFactory.Capture("https://example.com // check pricing", parser);
            var draft = DraftFactory.Build(context);
            t.IsTrue(draft.Notes!.StartsWith("https://example.com", StringComparison.Ordinal), draft.Notes);
            t.IsTrue(draft.Notes!.Contains("check pricing"), draft.Notes);
        });

        t.Test("explicit fields are marked so routing will not clobber them", () =>
        {
            var draft = parser.Parse("Task +Acme IT #onsite @friday").ToDraft();
            t.IsTrue(draft.WasSetExplicitly(nameof(TaskDraft.ProjectId)), "project was typed");
            t.IsTrue(draft.WasSetExplicitly(nameof(TaskDraft.TagIds)), "tag was typed");
            t.IsTrue(draft.WasSetExplicitly(nameof(TaskDraft.DueDay)), "due date was typed");
            t.IsFalse(draft.WasSetExplicitly(nameof(TaskDraft.Notes)), "no notes were typed");
        });

        t.Test("an over-long title is clamped", () =>
        {
            var draft = new TaskDraft { Title = new string('a', 50) + " " + new string('b', 400) };
            draft.Clamp();
            t.IsTrue(draft.Title.Length <= TaskDraft.MaxTitleLength,
                "clamped to " + draft.Title.Length);
        });

        t.Test("a file path is detected", () =>
        {
            var context = DraftFactory.Capture(@"C:\Users\me\report.docx", parser);
            t.AreEqual(CaptureKind.FilePath, context.Kind);
        });

        t.Test("a UNC path is detected", () =>
            t.AreEqual(CaptureKind.FilePath,
                DraftFactory.Capture(@"\\fileserver\share\thing.pdf", parser).Kind));

        t.Test("a URL capture is kind Url", () =>
            t.AreEqual(CaptureKind.Url, DraftFactory.Capture("https://example.com", parser).Kind));

        return t.Report("drafts");
    }

    private static int RoutingTests()
    {
        var t = new TestRunner();
        var parser = NewParser();

        t.Test("the pass-through router changes nothing", () =>
        {
            var context = DraftFactory.Capture("Do a thing", parser);
            var draft = DraftFactory.Build(context);
            var routed = PassThroughRouter.Instance.RouteAsync(context, draft).AsTask().Result;
            t.AreEqual("Do a thing", routed.Title);
            t.IsNull(routed.ProjectId, "still no project");
        });

        t.Test("defaults fill an empty project", () =>
        {
            var context = DraftFactory.Capture("Do a thing", parser);
            var draft = DraftFactory.Build(context);
            var router = new DefaultsRouter(() => "p-inbox");
            var routed = router.RouteAsync(context, draft).AsTask().Result;
            t.AreEqual("p-inbox", routed.ProjectId);
        });

        t.Test("defaults never overwrite what was typed", () =>
        {
            var context = DraftFactory.Capture("Do a thing +Acme IT", parser);
            var draft = DraftFactory.Build(context);
            var router = new DefaultsRouter(() => "p-inbox");
            var routed = router.RouteAsync(context, draft).AsTask().Result;
            t.AreEqual("p-acme", routed.ProjectId, "the typed project wins");
        });

        t.Test("default tags apply only when none were typed", () =>
        {
            var withTag = DraftFactory.Capture("Thing #onsite", parser);
            var routerA = new DefaultsRouter(() => null, () => ["t-network"]);
            var routedA = routerA.RouteAsync(withTag, DraftFactory.Build(withTag)).AsTask().Result;
            t.SequenceEqual(["t-onsite"], routedA.TagIds.ToList());

            var without = DraftFactory.Capture("Thing", parser);
            var routedB = routerA.RouteAsync(without, DraftFactory.Build(without)).AsTask().Result;
            t.SequenceEqual(["t-network"], routedB.TagIds.ToList());
        });

        t.Test("a pipeline runs routers in order", () =>
        {
            var context = DraftFactory.Capture("Thing", parser);
            var pipeline = new RouterPipeline(
                new DefaultsRouter(() => "p-inbox"),
                PassThroughRouter.Instance);
            var routed = pipeline.RouteAsync(context, DraftFactory.Build(context)).AsTask().Result;
            t.AreEqual("p-inbox", routed.ProjectId);
        });

        // The default project is typed as a name in settings but has to leave
        // as an id, and an unresolvable one must not reach the API.

        t.Test("the default project resolves by name", () =>
            t.AreEqual("p-acme", DefaultProjectResolver.Resolve(Reference, "Acme IT")));

        t.Test("the default project matches a name case-insensitively", () =>
            t.AreEqual("p-inbox", DefaultProjectResolver.Resolve(Reference, "inbox")));

        t.Test("the default project tolerates surrounding space", () =>
            t.AreEqual("p-home", DefaultProjectResolver.Resolve(Reference, "  Home  ")));

        t.Test("an id pasted into the default project is accepted", () =>
            t.AreEqual("p-acme", DefaultProjectResolver.Resolve(Reference, "p-acme")));

        t.Test("an unknown default project resolves to nothing", () =>
            t.IsNull(DefaultProjectResolver.Resolve(Reference, "Nowhere"),
                "better the Inbox than a rejected POST"));

        t.Test("a blank default project resolves to nothing", () =>
        {
            t.IsNull(DefaultProjectResolver.Resolve(Reference, null), "null");
            t.IsNull(DefaultProjectResolver.Resolve(Reference, ""), "empty");
            t.IsNull(DefaultProjectResolver.Resolve(Reference, "   "), "whitespace");
        });

        t.Test("no reference data yet means no default project", () =>
            t.IsNull(DefaultProjectResolver.Resolve(ReferenceSnapshot.Empty, "Inbox"),
                "the cache has not been filled, so nothing can be confirmed"));

        t.Test("a name is preferred over an id that collides with it", () =>
        {
            // A project literally named after another project's id must not
            // silently retarget the capture.
            var snapshot = new ReferenceSnapshot(
                projects: [new NamedItem("x-1", "p-acme"), new NamedItem("p-acme", "Acme IT")],
                tags: []);

            t.AreEqual("x-1", DefaultProjectResolver.Resolve(snapshot, "p-acme"));
        });

        return t.Report("routing");
    }

    private static int RequestTests()
    {
        var t = new TestRunner();
        var parser = NewParser();

        t.Test("a draft maps onto the POST body", () =>
        {
            var context = DraftFactory.Capture(
                "Replace UPS batteries +Acme IT #onsite @friday 45m // rack 3", parser);
            var request = SuperProductivityClient.ToRequest(DraftFactory.Build(context));

            t.AreEqual("Replace UPS batteries", request.Title);
            t.AreEqual("p-acme", request.ProjectId);
            t.SequenceEqual(["t-onsite"], request.TagIds!.ToList());
            t.AreEqual("2026-09-25", request.DueDay);
            t.AreEqual(2_700_000L, request.TimeEstimate!.Value);
            t.AreEqual("rack 3", request.Notes);
            t.IsNull(request.DueWithTime, "no time was given");
        });

        t.Test("empty optional fields are left null so they are omitted", () =>
        {
            var context = DraftFactory.Capture("Just a title", parser);
            var request = SuperProductivityClient.ToRequest(DraftFactory.Build(context));

            t.AreEqual("Just a title", request.Title);
            t.IsNull(request.Notes, "no notes");
            t.IsNull(request.ProjectId, "no project");
            t.IsNull(request.TagIds, "no tags");
            t.IsNull(request.DueDay, "no due date");
            t.IsNull(request.TimeEstimate, "no estimate");
        });

        t.Test("a timed due date sends both fields", () =>
        {
            var context = DraftFactory.Capture("Standup @tomorrow 9am", parser);
            var request = SuperProductivityClient.ToRequest(DraftFactory.Build(context));
            t.IsNotNull(request.DueWithTime, "timestamp sent");
            t.AreEqual("2026-09-23", request.DueDay);
        });

        t.Test("options build sane URLs", () =>
        {
            var options = new SuperProductivityOptions();
            t.AreEqual("http://127.0.0.1:3876/tasks", options.BuildUri("tasks").ToString());
            t.AreEqual("http://127.0.0.1:3876/health", options.BuildUri("/health").ToString());
            t.IsFalse(options.HasToken, "no token by default");
        });

        t.Test("a trailing slash in the base URL is tolerated", () =>
        {
            var options = new SuperProductivityOptions { BaseUrl = "http://127.0.0.1:3876/" };
            t.AreEqual("http://127.0.0.1:3876/tasks", options.BuildUri("tasks").ToString());
        });

        t.Test("today follows the tag's order, not the API's", () =>
        {
            var tasks = new[] { Task("henry"), Task("soundbars"), Task("ryan"), Task("news") };
            var ordered = SuperProductivityClient.InListOrder(tasks, ["ryan", "done-and-absent", "news", "henry", "soundbars"]);
            t.SequenceEqual(["ryan", "news", "henry", "soundbars"], ordered.Select(task => task.Id).ToArray());
        });

        t.Test("a task the tag does not mention is kept, at the end", () =>
        {
            var tasks = new[] { Task("stray"), Task("a"), Task("b") };
            var ordered = SuperProductivityClient.InListOrder(tasks, ["b", "a"]);
            t.SequenceEqual(["b", "a", "stray"], ordered.Select(task => task.Id).ToArray());
        });

        t.Test("no tag order leaves the API's order alone", () =>
        {
            var tasks = new[] { Task("x"), Task("y") };
            t.SequenceEqual(["x", "y"], SuperProductivityClient.InListOrder(tasks, null).Select(task => task.Id).ToArray());
        });

        return t.Report("requests");

        static TaskImporter.SuperProductivity.Models.TaskDto Task(string id) => new() { Id = id, Title = id };
    }

    /// <summary>
    /// The client's JSON, end to end, against a fake API. The test project turns
    /// reflection-based System.Text.Json off, as a trimmed Release build does, so
    /// a type missing from the source-generated context fails here rather than
    /// in the Store build.
    /// </summary>
    private static int WireTests()
    {
        var t = new TestRunner();
        var parser = NewParser();

        static SuperProductivityClient Client(FakeApi api)
            => new(new SuperProductivityOptions { AccessToken = "test-token" }, new System.Net.Http.HttpClient(api));

        static T Wait<T>(System.Threading.Tasks.Task<T> task) => task.GetAwaiter().GetResult();

        t.Test("creating a task sends camelCase and omits empty fields", () =>
        {
            var api = new FakeApi().On("POST /tasks",
                """{"ok":true,"data":{"id":"new-1","title":"Replace UPS batteries","timeEstimate":2700000}}""");
            using var client = Client(api);

            var draft = DraftFactory.Build(DraftFactory.Capture("Replace UPS batteries +Acme IT 45m", parser));
            var created = Wait(client.CreateTaskAsync(draft));

            t.AreEqual("new-1", created.Id);
            t.AreEqual(2_700_000L, created.TimeEstimate);
            t.AreEqual(
                """{"title":"Replace UPS batteries","projectId":"p-acme","timeEstimate":2700000}""",
                api.Requests[0].Body);
        });

        t.Test("completing a task sends only isDone", () =>
        {
            var api = new FakeApi().On("PATCH /tasks/a%2Fb",
                """{"ok":true,"data":{"id":"a/b","title":"x","isDone":true}}""");
            using var client = Client(api);

            var done = Wait(client.CompleteTaskAsync("a/b"));
            t.IsTrue(done.IsDone, "the returned task is done");
            t.AreEqual("""{"isDone":true}""", api.Requests[0].Body);
        });

        t.Test("today reads tasks and tags and follows the tag's order", () =>
        {
            var api = new FakeApi()
                .On("GET /tasks?tagId=TODAY",
                    """{"ok":true,"data":[{"id":"b","title":"B","timeSpent":60000},{"id":"a","title":"A"}]}""")
                .On("GET /tags",
                    """{"ok":true,"data":[{"id":"TODAY","title":"Today","taskIds":["a","b"]}]}""");
            using var client = Client(api);

            var today = Wait(client.GetTodayAsync());
            t.SequenceEqual(["a", "b"], today.Select(task => task.Id).ToArray());
            t.AreEqual(60_000L, today[1].TimeSpent);
        });

        t.Test("projects accept either title or name", () =>
        {
            var api = new FakeApi().On("GET /projects",
                """{"ok":true,"data":[{"id":"p1","title":"Acme IT"},{"id":"p2","name":"Home"}]}""");
            using var client = Client(api);

            var projects = Wait(client.GetProjectsAsync());
            t.SequenceEqual(["Acme IT", "Home"], projects.Select(p => p.DisplayName).ToArray());
        });

        t.Test("no running task comes back as null", () =>
        {
            var api = new FakeApi().On("GET /task-control/current", """{"ok":true,"data":null}""");
            using var client = Client(api);
            t.IsNull(Wait(client.GetCurrentTaskAsync()), "nothing is running");
        });

        t.Test("starting and stopping ignore whatever data comes back", () =>
        {
            var api = new FakeApi()
                .On("POST /tasks/t1/start", """{"ok":true,"data":{"started":true}}""")
                .On("POST /task-control/stop", """{"ok":true}""");
            using var client = Client(api);

            client.StartTaskAsync("t1").GetAwaiter().GetResult();
            client.StopCurrentTaskAsync().GetAwaiter().GetResult();
            t.AreEqual(2, api.Requests.Count);
        });

        t.Test("an error envelope becomes a fault the palette can explain", () =>
        {
            var api = new FakeApi().On("GET /tags",
                """{"ok":false,"error":{"code":"RENDERER_NOT_READY","message":"starting"}}""",
                System.Net.HttpStatusCode.ServiceUnavailable);
            using var client = Client(api);

            try
            {
                Wait(client.GetTagsAsync());
                t.IsTrue(false, "should have thrown");
            }
            catch (SuperProductivityException ex)
            {
                t.AreEqual(SuperProductivityFault.NotReady, ex.Fault);
                t.AreEqual("RENDERER_NOT_READY", ex.Code);
            }
        });

        t.Test("health is read without a token", () =>
        {
            var api = new FakeApi().On("GET /health", """{"ok":true,"data":{"server":"sp","rendererReady":true}}""");
            using var client = new SuperProductivityClient(new SuperProductivityOptions(), new System.Net.Http.HttpClient(api));

            var health = Wait(client.GetHealthAsync());
            t.IsNotNull(health, "health parsed");
            t.IsTrue(health!.RendererReady, "renderer ready");
        });

        return t.Report("wire");
    }

    private static int TimingTests()
    {
        var t = new TestRunner();
        var fetched = new DateTimeOffset(2026, 9, 22, 9, 0, 0, TimeSpan.Zero);

        t.Test("a running timer counts forward from the fetch", () =>
        {
            t.AreEqual(2_705_000L, TimeProgress.Extrapolate(2_700_000, true, fetched, fetched.AddSeconds(5)));
        });

        t.Test("a stopped timer does not move", () =>
        {
            t.AreEqual(2_700_000L, TimeProgress.Extrapolate(2_700_000, false, fetched, fetched.AddMinutes(5)));
        });

        t.Test("a clock that went backwards adds nothing", () =>
        {
            t.AreEqual(60_000L, TimeProgress.Extrapolate(60_000, true, fetched, fetched.AddSeconds(-3)));
        });

        t.Test("stopped reads in minutes against the estimate", () =>
        {
            t.AreEqual("45m / 1h", TimeProgress.Describe(2_700_000, 3_600_000, running: false));
        });

        t.Test("running reads as a clock so the seconds visibly tick", () =>
        {
            t.AreEqual("42:07 / 1h", TimeProgress.Describe(2_527_000, 3_600_000, running: true));
            t.AreEqual("1:02:07 / 1h30m", TimeProgress.Describe(3_727_000, 5_400_000, running: true));
        });

        t.Test("no estimate shows the time spent alone", () =>
        {
            t.AreEqual("0m", TimeProgress.Describe(0, 0, running: false));
            t.AreEqual("0:00", TimeProgress.Describe(0, 0, running: true));
        });

        t.Test("an overrun is spelled out, but not under a minute", () =>
        {
            t.AreEqual("1h15m / 1h · 15m over", TimeProgress.Describe(4_500_000, 3_600_000, running: false));
            t.AreEqual("1:00:30 / 1h", TimeProgress.Describe(3_630_000, 3_600_000, running: true));
        });

        return t.Report("timing");
    }
}

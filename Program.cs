using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

// Test console app: sends 3 hardcoded alerts of different structure, either to individual users or to a
// Team channel, through a Power Automate Workflows webhook.
// Payloads sent to the flow:
//   users:   { "target": "users",   "userEmails": ["...", "..."], "card": { Adaptive Card JSON } }
//   channel: { "target": "channel", "teamId": "...", "channelId": "...", "card": { Adaptive Card JSON } }
// The flow has a Condition on "target" (see README section 4).
//
// Settings load in this order (later wins): appsettings.json, appsettings.local.json, environment variables.
// The flow URL is a secret: put it in appsettings.local.json (git-ignored) or the TeamsAlert__FlowUrl
// environment variable. Channels are named in TeamsAlert:Channels:<name>:TeamId / :ChannelId
// (see appsettings.local.example.json).
//
// Usage:
//   TeamsAlert                             -> asks: users or channel
//   TeamsAlert a@x.com,b@x.com             -> users (separate with comma, semicolon or space)
//   TeamsAlert channel <name>              -> the channel configured under <name>

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.local.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var flowUrl = config["TeamsAlert:FlowUrl"];
var timeoutSeconds = config.GetValue("TeamsAlert:TimeoutSeconds", 15);
var maxAttempts = config.GetValue("TeamsAlert:MaxAttempts", 3);

if (string.IsNullOrWhiteSpace(flowUrl))
{
    Console.Error.WriteLine("TeamsAlert:FlowUrl is not set.");
    Console.Error.WriteLine("Set it in appsettings.local.json or the TeamsAlert__FlowUrl environment variable.");
    return 1;
}

var target = args.Length >= 1
    ? (args[0].Equals("channel", StringComparison.OrdinalIgnoreCase) ? "channel" : "users")
    : (Prompt("Send to users or channel? [u/c]: ").Trim().StartsWith("c", StringComparison.OrdinalIgnoreCase) ? "channel" : "users");

var userEmails = new List<string>();
string? teamId = null;
string? channelId = null;
string destination;

if (target == "channel")
{
    var known = config.GetSection("TeamsAlert:Channels").GetChildren().Select(c => c.Key).ToList();
    var channelName = args.Length >= 2
        ? args[1]
        : Prompt($"Channel name ({(known.Count > 0 ? string.Join(", ", known) : "none configured")}): ").Trim();

    var channel = config.GetSection($"TeamsAlert:Channels:{channelName}");
    teamId = channel["TeamId"];
    channelId = channel["ChannelId"];

    // Reject missing or placeholder values before anything is posted
    if (!Guid.TryParse(teamId, out _) || channelId is null || !channelId.StartsWith("19:") || !channelId.Contains("@thread"))
    {
        Console.Error.WriteLine($"Channel '{channelName}' is not configured correctly.");
        Console.Error.WriteLine("Set TeamsAlert:Channels:<name>:TeamId (a GUID) and :ChannelId (19:...@thread.tacv2) in appsettings.local.json.");
        return 1;
    }

    destination = $"channel '{channelName}'";
}
else
{
    var rawEmails = args.Length >= 1 ? string.Join(' ', args) : Prompt("Receiver emails (comma separated): ");
    var tokens = rawEmails.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    userEmails = tokens
        .Where(t => t.Contains('@') && !t.StartsWith('@') && !t.EndsWith('@'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    foreach (var bad in tokens.Except(userEmails, StringComparer.OrdinalIgnoreCase))
        Console.Error.WriteLine($"Ignoring invalid email: {bad}");

    if (userEmails.Count == 0)
    {
        Console.Error.WriteLine("No valid receiver email.");
        return 1;
    }

    Console.WriteLine($"Receivers ({userEmails.Count}): {string.Join(", ", userEmails)}");
    destination = $"{userEmails.Count} receiver(s)";
}

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };

var alerts = new (string Name, object Card)[]
{
    ("1. Simple text alert (critical)", BuildSimpleAlert()),
    ("2. Facts table alert (warning)", BuildFactsAlert()),
    ("3. Image + button alert (info)", BuildImageAlert()),
};

var failed = 0;
foreach (var (name, card) in alerts)
{
    Console.WriteLine($"Sending {name} ...");
    if (!await SendAsync(card))
        failed++;
    await Task.Delay(TimeSpan.FromSeconds(1)); // avoid bursting the flow
}

Console.WriteLine(failed == 0
    ? $"All {alerts.Length} alerts sent to {destination}. Check Teams for messages from Flow bot."
    : $"{failed} of {alerts.Length} alerts failed. Check the flow's Run history in Power Automate.");
return failed == 0 ? 0 : 2;

// ---------- Hardcoded alert cards (different structures) ----------

// 1) Title + message only
static object BuildSimpleAlert() => new
{
    type = "AdaptiveCard",
    version = "1.4",
    body = new object[]
    {
        new
        {
            type = "Container",
            style = "attention",
            items = new object[]
            {
                new { type = "TextBlock", text = "CRITICAL: Furnace 2 over temperature", weight = "Bolder", size = "Medium", wrap = true },
                new { type = "TextBlock", text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}", isSubtle = true, spacing = "None" },
            }
        },
        new { type = "TextBlock", text = "Temperature reached 412 C, above the 400 C limit. Check the furnace immediately.", wrap = true },
    }
};

// 2) Header + message + facts table
static object BuildFactsAlert() => new
{
    type = "AdaptiveCard",
    version = "1.4",
    body = new object[]
    {
        new
        {
            type = "Container",
            style = "warning",
            items = new object[]
            {
                new { type = "TextBlock", text = "WARNING: Parameter drifting", weight = "Bolder", size = "Medium", wrap = true },
                new { type = "TextBlock", text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}", isSubtle = true, spacing = "None" },
            }
        },
        new { type = "TextBlock", text = "Pressure is trending toward the upper limit.", wrap = true },
        new
        {
            type = "FactSet",
            facts = new object[]
            {
                new { title = "Machine", value = "Press 4" },
                new { title = "Parameter", value = "Hydraulic pressure" },
                new { title = "Value", value = "182 bar" },
                new { title = "Limit", value = "200 bar" },
                new { title = "Shift", value = "B" },
            }
        },
    }
};

// 3) Image + message + action button (image must be reachable by Teams; replace with your own public URL)
static object BuildImageAlert() => new
{
    type = "AdaptiveCard",
    version = "1.4",
    body = new object[]
    {
        new
        {
            type = "Container",
            style = "accent",
            items = new object[]
            {
                new { type = "TextBlock", text = "INFO: Daily report ready", weight = "Bolder", size = "Medium", wrap = true },
            }
        },
        new { type = "TextBlock", text = "Yesterday's production summary is available. Chart below.", wrap = true },
        new { type = "Image", url = "https://yavuzceliker.github.io/sample-images/image-20.jpg", size = "Large", altText = "Sample chart" },
    },
    actions = new object[]
    {
        new { type = "Action.OpenUrl", title = "Open dashboard", url = "https://example.com/dashboard" },
    }
};

// ---------- Sending ----------

async Task<bool> SendAsync(object card)
{
    object payload = target == "channel"
        ? new { target, teamId, channelId, card }
        : new { target, userEmails, card };

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            using var resp = await http.PostAsJsonAsync(flowUrl, payload);
            Console.WriteLine($"  Attempt {attempt}: HTTP {(int)resp.StatusCode} {resp.StatusCode}");

            if (resp.IsSuccessStatusCode)
                return true;

            var body = await resp.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(body))
                Console.Error.WriteLine("  " + Truncate(body, 500));

            // 4xx (except throttling) will not succeed on retry
            if ((int)resp.StatusCode is >= 400 and < 500 && (int)resp.StatusCode != 429)
                return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Console.Error.WriteLine($"  Attempt {attempt}: {ex.Message}");
        }

        if (attempt < maxAttempts)
            await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
    }

    return false;
}

static string Prompt(string label)
{
    Console.Write(label);
    return Console.ReadLine() ?? string.Empty;
}

static string Truncate(string value, int max) =>
    value.Length <= max ? value : value[..max];

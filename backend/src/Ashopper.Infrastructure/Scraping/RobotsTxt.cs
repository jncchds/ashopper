using System.Text;
using System.Text.RegularExpressions;

namespace Ashopper.Infrastructure.Scraping;

/// <summary>
/// robots.txt per RFC 9309: pick the group for our product token (or "*"), then the longest matching Allow/Disallow
/// rule wins, with Allow winning ties. Supports "*" and "$" in paths and the common Crawl-delay extension.
/// </summary>
public sealed class RobotsTxt
{
    private readonly List<(bool Allow, string Pattern, Regex Regex)> _rules;

    public TimeSpan? CrawlDelay { get; }

    private RobotsTxt(List<(bool, string, Regex)> rules, TimeSpan? crawlDelay)
    {
        _rules = rules;
        CrawlDelay = crawlDelay;
    }

    public static RobotsTxt AllowAll { get; } = new([], null);

    public static RobotsTxt Parse(string content, string productToken)
    {
        var groups = new List<(List<string> Agents, List<(string Key, string Value)> Lines)>();
        List<string>? agents = null;
        List<(string, string)>? lines = null;
        var lastWasAgent = false;

        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Split('#', 2)[0].Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();

            if (key == "user-agent")
            {
                // Consecutive user-agent lines share one group.
                if (!lastWasAgent || agents is null)
                {
                    agents = [];
                    lines = [];
                    groups.Add((agents, lines));
                }
                agents.Add(value.ToLowerInvariant());
                lastWasAgent = true;
            }
            else
            {
                lines?.Add((key, value));
                lastWasAgent = false;
            }
        }

        // Our own group(s) if any name our token; otherwise the "*" group(s). Same-named groups merge.
        var token = productToken.ToLowerInvariant();
        var chosen = groups.Where(g => g.Agents.Any(a => a != "*" && token.StartsWith(a, StringComparison.Ordinal) && a.Length > 0)).ToList();
        if (chosen.Count == 0)
            chosen = groups.Where(g => g.Agents.Contains("*")).ToList();

        var rules = new List<(bool, string, Regex)>();
        TimeSpan? delay = null;
        foreach (var (key, value) in chosen.SelectMany(g => g.Lines))
        {
            switch (key)
            {
                case "allow" or "disallow" when value.Length > 0:
                    rules.Add((key == "allow", value, ToRegex(value)));
                    break;
                case "crawl-delay" when double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds > 0:
                    delay = TimeSpan.FromSeconds(Math.Min(seconds, 600));
                    break;
            }
        }
        return new RobotsTxt(rules, delay);
    }

    /// <param name="pathAndQuery">e.g. "/p/123-item.html?x=1".</param>
    public bool IsAllowed(string pathAndQuery)
    {
        if (pathAndQuery == "/robots.txt")
            return true;
        var best = _rules
            .Where(r => r.Regex.IsMatch(pathAndQuery))
            .OrderByDescending(r => r.Pattern.Length)
            .ThenByDescending(r => r.Allow)
            .Select(r => (bool?)r.Allow)
            .FirstOrDefault();
        return best ?? true;
    }

    private static Regex ToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        var anchored = pattern.EndsWith('$');
        foreach (var c in anchored ? pattern[..^1] : pattern)
            sb.Append(c == '*' ? ".*" : Regex.Escape(c.ToString()));
        if (anchored)
            sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }
}

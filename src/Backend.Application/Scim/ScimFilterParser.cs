using System.Text.RegularExpressions;
using Backend.Domain.Exceptions;

namespace Backend.Application.Scim;

public static class ScimFilterParser
{
    private static readonly Regex _filterPattern = new(
        @"^(\w+)\s+eq\s+""([^""]*)""$",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    public static ScimFilter Parse(string filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var trimmed = filter.Trim();
        var match = _filterPattern.Match(trimmed);

        if (!match.Success)
        {
            throw new ValidationException(
                $"Unsupported filter '{filter}'. Only 'attributeName eq \"value\"' is supported.");
        }

        return new ScimFilter(match.Groups[1].Value, "eq", match.Groups[2].Value);
    }
}

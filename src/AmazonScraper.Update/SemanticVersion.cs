using System.Globalization;
using System.Text.RegularExpressions;

namespace AmazonScraper.Update;

/// <summary>
/// Semantic Version 2.0.0 precedence, including prerelease ordering. Build
/// metadata is accepted but ignored for comparison, as required by SemVer.
/// </summary>
public sealed partial class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private SemanticVersion(int major, int minor, int patch, string? prerelease, string? build)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        Build = build;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public string? Prerelease { get; }

    public string? Build { get; }

    public bool IsPrerelease => Prerelease is not null;

    public static SemanticVersion Parse(string value)
    {
        if (!TryParse(value, out var version))
        {
            throw new FormatException($"Invalid version: '{value}'.");
        }

        return version;
    }

    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = VersionRegex().Match(value.Trim());
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        var prerelease = match.Groups["pre"].Success
            ? match.Groups["pre"].Value
            : null;
        var build = match.Groups["build"].Success
            ? match.Groups["build"].Value
            : null;

        version = new SemanticVersion(major, minor, patch, prerelease, build);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        if (result != 0)
        {
            return result;
        }

        result = Patch.CompareTo(other.Patch);
        if (result != 0)
        {
            return result;
        }

        if (Prerelease is null && other.Prerelease is null)
        {
            return 0;
        }
        if (Prerelease is null)
        {
            return 1;
        }
        if (other.Prerelease is null)
        {
            return -1;
        }

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    public bool Equals(SemanticVersion? other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    public override int GetHashCode()
    {
        // Build metadata is intentionally excluded.
        return HashCode.Combine(Major, Minor, Patch, Prerelease);
    }

    public override string ToString()
    {
        var result = $"{Major}.{Minor}.{Patch}";
        if (Prerelease is not null)
        {
            result += "-" + Prerelease;
        }
        if (Build is not null)
        {
            result += "+" + Build;
        }
        return result;
    }

    private static int ComparePrerelease(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        var count = Math.Max(leftParts.Length, rightParts.Length);

        for (var index = 0; index < count; index++)
        {
            if (index >= leftParts.Length)
            {
                return -1;
            }
            if (index >= rightParts.Length)
            {
                return 1;
            }

            var leftPart = leftParts[index];
            var rightPart = rightParts[index];
            var leftNumeric = int.TryParse(leftPart, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
            var rightNumeric = int.TryParse(rightPart, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);

            if (leftNumeric && rightNumeric)
            {
                var numeric = leftNumber.CompareTo(rightNumber);
                if (numeric != 0)
                {
                    return numeric;
                }
                continue;
            }

            if (leftNumeric != rightNumeric)
            {
                return leftNumeric ? -1 : 1;
            }

            var text = string.CompareOrdinal(leftPart, rightPart);
            if (text != 0)
            {
                return text;
            }
        }

        return 0;
    }

    [GeneratedRegex(
        @"^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+(?<build>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();
}

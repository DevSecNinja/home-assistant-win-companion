namespace WindowsCompanion.Core.Updates;

/// <summary>A SemVer 2.0 version used for release ordering.</summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private readonly string[] _preRelease;

    private SemanticVersion(int major, int minor, int patch, string[] preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        _preRelease = preRelease;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public IReadOnlyList<string> PreRelease => _preRelease;

    public bool IsPreRelease => _preRelease.Length > 0;

    public static bool TryParse(string? value, out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var candidate = value.Trim();
        if (candidate.Length > 1 && candidate[0] is 'v' or 'V')
            candidate = candidate[1..];

        var metadataSeparator = candidate.IndexOf('+');
        if (metadataSeparator >= 0)
        {
            var metadata = candidate[(metadataSeparator + 1)..];
            if (!ValidIdentifiers(metadata, allowLeadingZeroes: true)) return false;
            candidate = candidate[..metadataSeparator];
        }

        string[] preRelease = [];
        var preReleaseSeparator = candidate.IndexOf('-');
        if (preReleaseSeparator >= 0)
        {
            var identifiers = candidate[(preReleaseSeparator + 1)..];
            if (!ValidIdentifiers(identifiers, allowLeadingZeroes: false)) return false;
            preRelease = identifiers.Split('.');
            candidate = candidate[..preReleaseSeparator];
        }

        var core = candidate.Split('.');
        if (core.Length != 3
            || !TryParseCoreNumber(core[0], out var major)
            || !TryParseCoreNumber(core[1], out var minor)
            || !TryParseCoreNumber(core[2], out var patch))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, preRelease);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;

        var core = Major.CompareTo(other.Major);
        if (core != 0) return core;
        core = Minor.CompareTo(other.Minor);
        if (core != 0) return core;
        core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;

        if (_preRelease.Length == 0) return other._preRelease.Length == 0 ? 0 : 1;
        if (other._preRelease.Length == 0) return -1;

        for (var index = 0; index < Math.Min(_preRelease.Length, other._preRelease.Length); index++)
        {
            var left = _preRelease[index];
            var right = other._preRelease[index];
            var leftNumeric = left.All(char.IsAsciiDigit);
            var rightNumeric = right.All(char.IsAsciiDigit);

            int comparison;
            if (leftNumeric && rightNumeric)
                comparison = left.Length == right.Length
                    ? string.Compare(left, right, StringComparison.Ordinal)
                    : left.Length.CompareTo(right.Length);
            else if (leftNumeric)
                comparison = -1;
            else if (rightNumeric)
                comparison = 1;
            else
                comparison = string.Compare(left, right, StringComparison.Ordinal);

            if (comparison != 0) return comparison;
        }

        return _preRelease.Length.CompareTo(other._preRelease.Length);
    }

    public bool Equals(SemanticVersion? other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Major);
        hash.Add(Minor);
        hash.Add(Patch);
        foreach (var identifier in _preRelease) hash.Add(identifier, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    public override string ToString() =>
        _preRelease.Length == 0
            ? $"{Major}.{Minor}.{Patch}"
            : $"{Major}.{Minor}.{Patch}-{string.Join('.', _preRelease)}";

    public static bool operator >(SemanticVersion left, SemanticVersion right) =>
        left.CompareTo(right) > 0;

    public static bool operator <(SemanticVersion left, SemanticVersion right) =>
        left.CompareTo(right) < 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) =>
        left.CompareTo(right) >= 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) =>
        left.CompareTo(right) <= 0;

    private static bool TryParseCoreNumber(string value, out int number)
    {
        number = 0;
        return value.Length > 0
               && (value.Length == 1 || value[0] != '0')
               && value.All(char.IsAsciiDigit)
               && int.TryParse(value, out number);
    }

    private static bool ValidIdentifiers(string value, bool allowLeadingZeroes)
    {
        var identifiers = value.Split('.');
        return identifiers.All(identifier =>
            identifier.Length > 0
            && identifier.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            && (allowLeadingZeroes
                || !identifier.All(char.IsAsciiDigit)
                || identifier.Length == 1
                || identifier[0] != '0'));
    }
}

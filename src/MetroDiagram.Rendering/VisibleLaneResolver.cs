using System.Globalization;

namespace MetroDiagram.Rendering;

internal static class VisibleLaneResolver
{
    public static VisibleLaneKey CreateKey(DisplayLineFamily family)
    {
        string colorKey = NormalizeColorKey(family.Color);
        int? lineNumber = ExtractLineNumber(family.DisplayName);
        if (lineNumber.HasValue)
        {
            string badge = lineNumber.Value.ToString(CultureInfo.InvariantCulture);
            return new VisibleLaneKey(
                $"badge:{badge}|{colorKey}",
                badge,
                colorKey,
                "same-line-number-same-color");
        }

        return new VisibleLaneKey(
            $"{family.FamilyKey}|{colorKey}",
            family.FamilyKey,
            colorKey,
            "family-key-color");
    }

    public static string GetBadgeText(string? displayName)
    {
        int? lineNumber = ExtractLineNumber(displayName);
        if (lineNumber.HasValue)
        {
            return lineNumber.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return string.Empty;
        }

        string trimmed = displayName.Trim();
        return trimmed.Length <= 4 ? trimmed : trimmed[..4];
    }

    public static int ComparePrimaryFamily(DisplayLineFamily left, DisplayLineFamily right)
    {
        int branchComparison = IsBranchLikeDisplayName(left.DisplayName).CompareTo(IsBranchLikeDisplayName(right.DisplayName));
        if (branchComparison != 0)
        {
            return branchComparison;
        }

        int lengthComparison = left.DisplayName.Length.CompareTo(right.DisplayName.Length);
        if (lengthComparison != 0)
        {
            return lengthComparison;
        }

        return string.Compare(left.FamilyKey, right.FamilyKey, StringComparison.Ordinal);
    }

    public static bool IsBranchLikeDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        return displayName.Contains("支线", StringComparison.CurrentCultureIgnoreCase)
            || displayName.Contains("branch", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("spur", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeColorKey(string? color)
    {
        return string.IsNullOrWhiteSpace(color)
            ? string.Empty
            : color.Trim().ToUpperInvariant();
    }

    private static int? ExtractLineNumber(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        int? value = null;
        foreach (char character in name)
        {
            double numericValue = char.GetNumericValue(character);
            if (numericValue >= 0 && numericValue <= 9 && Math.Floor(numericValue) == numericValue)
            {
                value = (value ?? 0) * 10 + (int)numericValue;
            }
            else if (value.HasValue)
            {
                return value;
            }
        }

        return value;
    }
}

internal sealed record VisibleLaneKey(
    string Key,
    string DisplayToken,
    string ColorKey,
    string Reason);

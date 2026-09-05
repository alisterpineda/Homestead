using System.Globalization;
using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Warranties;

/// <summary>Text normalisation and labelling shared by the warranty commands and the activity feed.</summary>
public static class ItemWarrantyText
{
    /// <summary>
    /// Trims a user field and collapses an empty result to null — all three warranty fields are
    /// optional, so "" and "   " must land as null rather than as a blank string.
    /// </summary>
    public static string? NormalizeText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// The one-line label an audit row keeps for a warranty — "Bosch · until 2028-03-12",
    /// "Manufacturer · no expiry". Null <see cref="ItemWarranty.Provider"/> renders as the word
    /// "Manufacturer" (the brand name is a display-time embellishment the API doesn't join here).
    /// </summary>
    public static string Describe(ItemWarranty warranty) =>
        $"{warranty.Provider ?? "Manufacturer"} · {DescribeExpiry(warranty.ExpiresOn)}";

    private static string DescribeExpiry(DateOnly? expiresOn) =>
        expiresOn is null ? "no expiry" : $"until {expiresOn.Value.ToString("O", CultureInfo.InvariantCulture)}";
}

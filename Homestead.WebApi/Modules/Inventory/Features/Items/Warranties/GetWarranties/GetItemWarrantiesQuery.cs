namespace Homestead.WebApi.Modules.Inventory.Features.Items.Warranties.GetWarranties;

/// <summary>Null result = the item doesn't exist (404); an empty list = the item has no warranties.</summary>
public record GetItemWarrantiesQuery(Guid Id);

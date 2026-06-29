using Going.Plaid;
using Going.Plaid.Entity;
using Going.Plaid.Link;
using Going.Plaid.Item;
using BatHouseholdHub.Models;

namespace BatHouseholdHub.Services;

/// <summary>Wraps the Plaid Link handshake: create a link token for the browser widget,
/// then exchange the public token it returns for a long-lived access token we store
/// against a PlaidItem. Sandbox-safe by default — set PLAID_ENV=production once real
/// bank credentials are ready.</summary>
public class PlaidService(PlaidClient client, IConfiguration config, HouseholdStore store)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(config["PLAID_CLIENT_ID"]) && !string.IsNullOrWhiteSpace(config["PLAID_SECRET"]);

    public async Task<string> CreateLinkTokenAsync(string owner)
    {
        var response = await client.LinkTokenCreateAsync(new LinkTokenCreateRequest
        {
            User = new LinkTokenCreateRequestUser { ClientUserId = owner },
            ClientName = "Household Hub",
            Products = [Products.Transactions],
            CountryCodes = [CountryCode.Us],
            Language = Language.English
        });
        if (response.Error is not null) throw new InvalidOperationException(response.Error.ErrorMessage);
        return response.LinkToken;
    }

    public async Task<PlaidItem> ExchangePublicTokenAsync(string publicToken, string institutionName, string owner)
    {
        var response = await client.ItemPublicTokenExchangeAsync(new ItemPublicTokenExchangeRequest { PublicToken = publicToken });
        if (response.Error is not null) throw new InvalidOperationException(response.Error.ErrorMessage);
        return await store.AddPlaidItemAsync(response.ItemId, response.AccessToken, institutionName, owner);
    }
}

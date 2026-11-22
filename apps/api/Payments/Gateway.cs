using System.Globalization;
using System.Text.Json;

namespace FragSplit;

public record HostedSession(string Key, string Url);

public record GatewayResult(
    string Status,
    string Reference,
    string ValidationId,
    decimal? Amount,
    string Currency,
    string OriginalCurrency,
    decimal? OriginalAmount,
    string Risk,
    string SessionKey,
    string ApiConnect
);

public interface IPaymentGateway
{
    Task<HostedSession> Create(Order order);
    Task<GatewayResult> Validate(string validationId);
    Task<GatewayResult[]> Query(Order order);
}

public sealed class SslCommerz(HttpClient http, IConfiguration config) : IPaymentGateway
{
    string Host =>
        config["SSLCOMMERZ_MODE"] switch
        {
            "production" => "https://securepay.sslcommerz.com",
            null or "" or "sandbox" => "https://sandbox.sslcommerz.com",
            _ => throw new ShopError("Invalid gateway configuration.", 503),
        };

    Dictionary<string, string> Credentials() =>
        new()
        {
            ["store_id"] = config["SSLCOMMERZ_STORE_ID"]!,
            ["store_passwd"] = config["SSLCOMMERZ_STORE_PASSWORD"]!,
        };

    public async Task<HostedSession> Create(Order o)
    {
        var fields = Credentials();
        var cb = config["CALLBACK_BASE_URL"]!.TrimEnd('/') + "/api/v1/payments/sslcommerz/";
        foreach (
            var (name, value) in new Dictionary<string, string>
            {
                ["tran_id"] = o.Reference,
                ["total_amount"] = o.Total.ToString("F2", CultureInfo.InvariantCulture),
                ["currency"] = "BDT",
                ["success_url"] = cb + "success",
                ["fail_url"] = cb + "fail",
                ["cancel_url"] = cb + "cancel",
                ["ipn_url"] = cb + "ipn",
                ["cus_name"] = o.Name,
                ["cus_email"] = o.Email,
                ["cus_phone"] = o.Phone,
                ["cus_add1"] = o.Address,
                ["cus_add2"] = o.Area,
                ["cus_city"] = o.City,
                ["cus_state"] = o.City,
                ["cus_postcode"] = o.Postcode,
                ["cus_country"] = "Bangladesh",
                ["shipping_method"] = "Courier",
                ["ship_name"] = o.Name,
                ["ship_add1"] = o.Address,
                ["ship_add2"] = o.Area,
                ["ship_city"] = o.City,
                ["ship_state"] = o.City,
                ["ship_postcode"] = o.Postcode,
                ["ship_country"] = "Bangladesh",
                ["num_of_item"] = o.Lines.Sum(l => l.Quantity).ToString(),
                ["product_name"] = "Perfume decants",
                ["product_category"] = "Perfume",
                ["product_profile"] = "physical-goods",
            }
        )
            fields[name] = value;
        using var request = new HttpRequestMessage(HttpMethod.Post, Host + "/gwprocess/v4/api.php")
        {
            Content = new FormUrlEncodedContent(fields),
        };
        var json = await Send(request);
        var url = Get(json, "GatewayPageURL");
        var key = Get(json, "sessionkey");
        if (
            Get(json, "status") != "SUCCESS"
            || key.Length is 0 or > 250
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != "https"
            || uri.UserInfo != ""
            || !(
                uri.Host == "sslcommerz.com"
                || uri.Host.EndsWith(".sslcommerz.com", StringComparison.OrdinalIgnoreCase)
            )
        )
            throw new ShopError("The provider did not return an eligible hosted session.", 502);
        return new(key, url);
    }

    public async Task<GatewayResult> Validate(string validationId) =>
        Read(await Lookup("validationserverAPI.php", "val_id", validationId));

    public async Task<GatewayResult[]> Query(Order o)
    {
        var json = await Lookup(
            "merchantTransIDvalidationAPI.php",
            o.SessionKey == null ? "tran_id" : "sessionkey",
            o.SessionKey ?? o.Reference
        );
        if (Get(json, "APIConnect") != "DONE")
            return [];
        if (
            json.TryGetProperty("element", out var elements)
            && elements.ValueKind == JsonValueKind.Array
        )
            return elements.EnumerateArray().Select(Read).ToArray();
        return [Read(json)];
    }

    async Task<JsonElement> Lookup(string path, string field, string value)
    {
        var args = Credentials();
        args[field] = value;
        args["format"] = "json";
        using var query = new FormUrlEncodedContent(args);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            Host + "/validator/api/" + path + "?" + await query.ReadAsStringAsync()
        );
        return await Send(request);
    }

    async Task<JsonElement> Send(HttpRequestMessage req)
    {
        try
        {
            using var response = await http.SendAsync(req);
            if (!response.IsSuccessStatusCode)
                throw new ShopError("Provider temporarily unavailable.", 503);
            using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
            if (json.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException();
            return json.RootElement.Clone();
        }
        catch (Exception ex)
            when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new ShopError(
                "Could not verify the provider response. Check the existing order again.",
                503
            );
        }
    }

    static string Get(JsonElement j, string k) =>
        j.TryGetProperty(k, out var v) ? v.ToString() : "";

    static decimal? Amount(JsonElement j, string k) =>
        decimal.TryParse(
            Get(j, k),
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var n
        )
            ? n
            : null;

    static GatewayResult Read(JsonElement j) =>
        new(
            Get(j, "status"),
            Get(j, "tran_id"),
            Get(j, "val_id"),
            Amount(j, "amount"),
            Get(j, "currency"),
            Get(j, "currency_type"),
            Amount(j, "currency_amount"),
            Get(j, "risk_level"),
            Get(j, "sessionkey"),
            Get(j, "APIConnect")
        );
}

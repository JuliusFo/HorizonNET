namespace HorizonNET.Domain.Interfaces;

public interface IAppSettingRepository
{
    // Wert zum Schlüssel, oder null wenn nie gesetzt.
    Task<string?> GetAsync(string key);

    // Legt den Wert an oder ersetzt den bestehenden (Upsert).
    Task SetAsync(string key, string value);
}

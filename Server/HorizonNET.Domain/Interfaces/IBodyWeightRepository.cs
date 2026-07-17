using HorizonNET.Domain.Entities;

namespace HorizonNET.Domain.Interfaces;

public interface IBodyWeightRepository
{
    // Chronologisch (ältester zuerst) – so kommt die Kurve fertig sortiert an.
    Task<IEnumerable<BodyWeightEntry>> GetAsync(DateOnly? from, DateOnly? to);

    // Legt an oder überschreibt den Eintrag des Tages (höchstens einer pro Tag).
    Task<BodyWeightEntry> SetAsync(DateOnly measuredOn, double weightKg);

    Task<bool> DeleteAsync(int id);

    Task<bool> RestoreAsync(int id);
}

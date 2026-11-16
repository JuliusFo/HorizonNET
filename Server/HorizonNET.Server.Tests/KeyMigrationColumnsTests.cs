using HorizonNET.KeyMigration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace HorizonNET.Server.Tests;

// Das Schlüsselring-Tool (Tools/HorizonNET.KeyMigration) führt eine eigene Liste der
// verschlüsselten Spalten, weil es ohne EF arbeitet. Fehlt dort eine Spalte, wird sie beim
// Umzug des Schlüsselrings nicht umgeschlüsselt und ist danach unlesbar – so geschehen mit
// dem Azure-DevOps-PAT (PR #1). Diese Tests halten Liste und Modell zusammen.
public class KeyMigrationColumnsTests
{
    // Verschlüsselte Spalten sind im Modell genau die mit einem Konverter string → string
    // (EncryptedConverter). Enums mit HasConversion<string>() haben das Enum als Modelltyp.
    private static List<(string Table, string Column, IProperty Property)> EncryptedInModel()
    {
        using var db = new TestDatabase();
        using var ctx = db.NewContext();

        return ctx.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties().Select(property => (entity, property)))
            .Where(x => x.property.GetValueConverter() is { } converter
                        && converter.ModelClrType == typeof(string)
                        && converter.ProviderClrType == typeof(string))
            .Select(x => (x.entity.GetTableName()!, x.property.GetColumnName(), (IProperty)x.property))
            .ToList();
    }

    [Fact]
    public void Every_encrypted_column_of_the_model_is_in_the_key_migration_list()
    {
        var inModel = EncryptedInModel().Select(c => $"{c.Table}.{c.Column}").Order().ToList();
        var inTool = EncryptedColumns.All.Select(c => $"{c.Table}.{c.Column}").Order().ToList();

        Assert.NotEmpty(inModel);
        Assert.Equal(inModel, inTool);
    }

    [Fact]
    public void The_purpose_of_every_listed_column_matches_the_model()
    {
        var inModel = EncryptedInModel();

        foreach (var column in EncryptedColumns.All)
        {
            var property = Assert.Single(inModel, c => c.Table == column.Table && c.Column == column.Column).Property;

            // Was der Konverter des Modells schreibt, muss das Tool mit seinem Purpose lesen können.
            var stored = (string)property.GetValueConverter()!.ConvertToProvider("probe")!;
            var read = TestDatabase.Protection.CreateProtector(column.Purpose).Unprotect(stored);

            Assert.Equal("probe", read);
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HorizonNET.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedJournalTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Zwei Startvorlagen, damit die Leiste im Journal nicht leer ist – gegen das
            // leere Blatt hilft eine Vorlage nur, wenn sie schon da ist.
            //
            // Bewusst kurz: Drei Fragen beantwortet man abends noch, sieben nicht. Es sind
            // ganz normale Datensätze; löschen oder umschreiben geht in den Einstellungen.
            //
            // Jeweils nur einfügen, wenn es sie noch nicht gibt – wer sie bewusst gelöscht
            // hat, soll sie nicht zurückbekommen.
            migrationBuilder.Sql("""
                INSERT INTO JournalTemplates (Name, Content, SortOrder, CreatedAt)
                SELECT 'Morgen',
                       '<p><strong>Worauf freue ich mich heute?</strong></p><p><br></p>'
                       || '<p><strong>Was ist heute das Wichtigste?</strong></p><p><br></p>'
                       || '<p><strong>Was könnte im Weg stehen?</strong></p><p><br></p>',
                       0, datetime('now', 'localtime')
                WHERE NOT EXISTS (SELECT 1 FROM JournalTemplates WHERE Name = 'Morgen');
                """);

            migrationBuilder.Sql("""
                INSERT INTO JournalTemplates (Name, Content, SortOrder, CreatedAt)
                SELECT 'Abend',
                       '<p><strong>Was lief heute gut?</strong></p><p><br></p>'
                       || '<p><strong>Was hat mich beschäftigt?</strong></p><p><br></p>'
                       || '<p><strong>Wofür bin ich dankbar?</strong></p><p><br></p>',
                       1, datetime('now', 'localtime')
                WHERE NOT EXISTS (SELECT 1 FROM JournalTemplates WHERE Name = 'Abend');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nur die Startvorlagen zurücknehmen – eigene Vorlagen darf ein Rollback
            // nicht mitreißen.
            migrationBuilder.Sql(
                "DELETE FROM JournalTemplates WHERE Name IN ('Morgen', 'Abend');");
        }
    }
}

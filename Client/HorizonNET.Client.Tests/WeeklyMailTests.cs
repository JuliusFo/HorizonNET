using HorizonNET.App.Components;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Client.Tests;

// Text und mailto:-Link der Wochenabschluss-Mail.
public class WeeklyMailTests
{
    private static readonly DateOnly Monday = new(2026, 11, 9);

    [Fact]
    public void Subject_UsesIsoWeek()
    {
        Assert.Equal("Wochenabschluss KW 46/2026", WeeklyMail.Subject(Monday));
        // 29.12.2025 ist Montag der KW 1/2026.
        Assert.Equal("Wochenabschluss KW 1/2026", WeeklyMail.Subject(new DateOnly(2025, 12, 29)));
    }

    [Fact]
    public void Body_GroupsSectionsAndSkipsEmptyOnes()
    {
        var report = new WeeklyReportDto(Monday,
            [Task("Release", WorkStatus.Done, project: "Shop", completedAt: new DateTime(2026, 11, 12)),
             Task("Login testen", WorkStatus.Done, parent: "Auth", completedAt: new DateTime(2026, 11, 10))],
            [Task("Review", WorkStatus.InProgress, project: "Shop"),
             Task("Freigabe", WorkStatus.Paused, waitingFor: "Anna"),
             Task("Doku", WorkStatus.PlannedPriority, due: new DateTime(2026, 11, 18))]);

        var body = WeeklyMail.Body(report, "Hallo zusammen,", "Viele Grüße\nMax");

        Assert.Equal("""
            Hallo zusammen,

            hier mein Wochenabschluss für KW 46 (09.11.–13.11.2026).

            Erledigt:
            - Shop: Release
            - Auth › Login testen

            In Arbeit:
            - Shop: Review

            Pausiert / wartet:
            - Freigabe (wartet auf: Anna)

            Als Nächstes:
            - Doku (fällig 18.11.)

            Viele Grüße
            Max
            """.ReplaceLineEndings(), body.ReplaceLineEndings());
    }

    [Fact]
    public void Body_WithoutGreeting_StartsCapitalized() =>
        Assert.StartsWith("Hier mein", WeeklyMail.Body(new WeeklyReportDto(Monday, [], []), "", null));

    [Fact]
    public void ParseAddresses_SplitsOnCommonSeparatorsAndDedupes() =>
        Assert.Equal(["a@x.de", "b@y.de", "c@z.de"],
            WeeklyMail.ParseAddresses(" a@x.de; b@y.de,c@z.de\nA@x.de "));

    [Theory]
    [InlineData("anna@firma.de", true)]
    [InlineData("anna", false)]
    [InlineData("anna@firma", false)]
    [InlineData("a@b.de?cc=evil@x.de", false)]
    public void IsValidAddress(string address, bool expected) =>
        Assert.Equal(expected, WeeklyMail.IsValidAddress(address));

    [Fact]
    public void MailtoUri_HasRecipientsCcAndEncodedCrlfBody()
    {
        var uri = WeeklyMail.MailtoUri(["a@x.de", "b@y.de"], ["c@z.de"], "KW 46 & mehr", "Zeile 1\nZeile 2");

        Assert.Equal("mailto:a@x.de,b@y.de?cc=c@z.de&subject=KW%2046%20%26%20mehr&body=Zeile%201%0D%0AZeile%202", uri);
    }

    [Fact]
    public void MailtoUri_EncodesAddresses()
    {
        var uri = WeeklyMail.MailtoUri(["a@x.de?subject=fake"], ["b&c@y.de"], "S", "B");
        Assert.StartsWith("mailto:a@x.de%3Fsubject%3Dfake?cc=b%26c@y.de&subject=S", uri);
    }

    [Fact]
    public void MailtoUri_OmitsCcWhenEmpty() =>
        Assert.DoesNotContain("cc=", WeeklyMail.MailtoUri(["a@x.de"], [], "S", "B"));

    private static WeeklyReportTaskDto Task(
        string title, WorkStatus status, string? project = null, string? parent = null,
        string? waitingFor = null, DateTime? due = null, DateTime? completedAt = null) =>
        new(1, title, parent, project, status, Priority.Medium, waitingFor, due, completedAt);
}

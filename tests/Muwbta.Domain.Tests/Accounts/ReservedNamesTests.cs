using Muwbta.Domain.Accounts;

namespace Muwbta.Domain.Tests.Accounts;

public sealed class ReservedNamesTests
{
    [Theory]
    [InlineData("Admin")]
    [InlineData("ADMINISTRATOR")]
    [InlineData("Moderator")]
    [InlineData("staff")]
    [InlineData("System")]
    [InlineData("Support")]
    [InlineData("GM")]
    [InlineData("Muwbta")]
    // Inside a longer name, for the words that cannot be there honestly.
    [InlineData("Adminah")]
    [InlineData("SuperAdmin")]
    [InlineData("StaffKael")]
    [InlineData("SupportDesk")]
    public void A_name_that_reads_as_staff_is_reserved(string name) =>
        Assert.True(ReservedNames.IsReserved(name));

    [Theory]
    [InlineData("Kael")]
    [InlineData("Bram")]
    // The substring rule is deliberately narrow: these contain a reserved word and are honest.
    [InlineData("Modesty")]
    [InlineData("Sigmund")]
    [InlineData("Devon")]
    [InlineData("Helper")]
    public void An_ordinary_name_is_not(string name) =>
        Assert.False(ReservedNames.IsReserved(name));
}

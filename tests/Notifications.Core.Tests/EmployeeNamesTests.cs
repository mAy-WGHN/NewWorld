using Notifications.Core.Templates;

namespace Notifications.Core.Tests;

public class EmployeeNamesTests
{
    [Theory]
    [InlineData("Hans Dampf", "Hans", "Dampf")]
    [InlineData("  Hans   Dampf ", "Hans", "Dampf")]
    [InlineData("Dampf, Hans", "Hans", "Dampf")]
    [InlineData("Maria Anna Müller", "Maria", "Anna Müller")]
    [InlineData("Cher", "Cher", "")]
    [InlineData("", "", "")]
    public void The_display_name_is_split_into_first_and_last_name(string displayName, string first, string last) =>
        EmployeeNames.Split(displayName).ShouldBe(new RecipientName(first, last));
}

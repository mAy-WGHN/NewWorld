using Portal.Core.CashRegisters;

namespace Portal.Core.Tests;

public class CashRegisterInitialsTests
{
    [Theory]
    [InlineData("Anna Schmidt", "AS")]
    [InlineData("Anna von Schmidt", "AvS")]
    [InlineData("Anna-Lena Müller", "ALM")]
    [InlineData("  Ben   Beispiel ", "BB")]
    [InlineData("Müller, Anna", "MA")]
    [InlineData("Cher", "C")]
    [InlineData("élodie Dupont", "ÉD")]
    public void The_initials_take_the_first_letter_of_each_name_part_and_keep_particles_lower_case(string name, string expected) =>
        CashRegisterInitials.Of(name).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_name_there_is_a_question_mark(string? name) => CashRegisterInitials.Of(name).ShouldBe("?");
}

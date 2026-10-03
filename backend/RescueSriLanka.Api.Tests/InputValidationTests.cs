using System.ComponentModel.DataAnnotations;
using RescueSriLanka.Api.DTOs;

namespace RescueSriLanka.Api.Tests;

public sealed class InputValidationTests
{
    [Theory]
    [InlineData("0771234567", "0771234567")]
    [InlineData("077 123 4567", "0771234567")]
    [InlineData("077-123-4567", "0771234567")]
    [InlineData("+94771234567", "0771234567")]
    public void SriLankanPhonesNormaliseToTenDigits(string input, string expected) =>
        Assert.Equal(expected, PhoneNumbers.Normalize(input));

    [Theory]
    [InlineData("077123456")]
    [InlineData("07712345678")]
    [InlineData("1771234567")]
    [InlineData("077123456a")]
    [InlineData("phone")]
    [InlineData("+1771234567")]
    [InlineData("077+1234567")]
    public void OtherPhonesAreRefused(string input) =>
        Assert.Null(PhoneNumbers.Normalize(input));

    [Theory]
    [InlineData("Nimali Perera", true)]
    [InlineData("O'Brien-Smith", true)]
    [InlineData("Dr. A. Silva", true)]
    [InlineData("Nimal2", false)]
    [InlineData("<script>", false)]
    [InlineData("A", false)]
    public void NamesAllowLettersAndPunctuationOnly(string name, bool valid) =>
        Assert.Equal(valid, new PersonNameAttribute().IsValid(name));

    [Theory]
    [InlineData("Colombo", true)]
    [InlineData("colombo", true)]
    [InlineData("Atlantis", false)]
    public void DistrictsMustBeOnTheOfficialList(string district, bool valid) =>
        Assert.Equal(valid, new SriLankaDistrictAttribute().IsValid(district));

    [Fact]
    public void AnEmptyOptionalPhoneIsLeftAlone() =>
        Assert.True(new SriLankaPhoneAttribute().IsValid(""));
}

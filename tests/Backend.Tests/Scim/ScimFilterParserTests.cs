using Backend.Application.Scim;
using Backend.Idp.Domain.Exceptions;
using Xunit;

namespace Backend.Tests.Scim;

public sealed class ScimFilterParserTests
{
    [Fact]
    public void Parse_ValidUserNameFilter_ReturnsFilter()
    {
        var result = ScimFilterParser.Parse("userName eq \"alice\"");

        Assert.Equal("userName", result.AttributeName);
        Assert.Equal("eq", result.Operator);
        Assert.Equal("alice", result.Value);
    }

    [Fact]
    public void Parse_ValidClientIdFilter_ReturnsFilter()
    {
        var result = ScimFilterParser.Parse("clientId eq \"orders-service\"");

        Assert.Equal("clientId", result.AttributeName);
        Assert.Equal("eq", result.Operator);
        Assert.Equal("orders-service", result.Value);
    }

    [Fact]
    public void Parse_FilterWithLeadingAndTrailingWhitespace_ReturnsFilter()
    {
        var result = ScimFilterParser.Parse("  userName eq \"bob\"  ");

        Assert.Equal("userName", result.AttributeName);
        Assert.Equal("bob", result.Value);
    }

    [Fact]
    public void Parse_EmptyValue_ReturnsFilterWithEmptyValue()
    {
        var result = ScimFilterParser.Parse("userName eq \"\"");

        Assert.Equal("userName", result.AttributeName);
        Assert.Equal(string.Empty, result.Value);
    }

    [Theory]
    [InlineData("userName sw \"alice\"")]
    [InlineData("userName contains \"alice\"")]
    [InlineData("userName ne \"alice\"")]
    public void Parse_UnsupportedOperator_ThrowsValidationException(string filter)
    {
        Assert.Throws<ValidationException>(() => ScimFilterParser.Parse(filter));
    }

    [Theory]
    [InlineData("userName alice")]
    [InlineData("eq \"alice\"")]
    [InlineData("just-a-string")]
    [InlineData("userName eq alice")]
    public void Parse_MalformedFilter_ThrowsValidationException(string filter)
    {
        Assert.Throws<ValidationException>(() => ScimFilterParser.Parse(filter));
    }

    [Fact]
    public void Parse_NullFilter_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ScimFilterParser.Parse(null!));
    }
}

using Template.Core;
using Xunit;

namespace Template.Core.Tests;

public class CalculatorTests
{
    [Theory]
    [InlineData(1, 2, 3)]
    [InlineData(-1, 1, 0)]
    [InlineData(0, 0, 0)]
    public void Add_ReturnsCorrectSum(double a, double b, double expected)
    {
        Assert.Equal(expected, Calculator.Add(a, b));
    }

    [Theory]
    [InlineData(5, 3, 2)]
    [InlineData(0, 5, -5)]
    public void Subtract_ReturnsCorrectDifference(double a, double b, double expected)
    {
        Assert.Equal(expected, Calculator.Subtract(a, b));
    }

    [Theory]
    [InlineData(3, 4, 12)]
    [InlineData(-2, 5, -10)]
    [InlineData(0, 100, 0)]
    public void Multiply_ReturnsCorrectProduct(double a, double b, double expected)
    {
        Assert.Equal(expected, Calculator.Multiply(a, b));
    }

    [Fact]
    public void Divide_ReturnsCorrectQuotient()
    {
        Assert.Equal(2.5, Calculator.Divide(5, 2));
    }

    [Fact]
    public void Divide_ByZero_ThrowsDivideByZeroException()
    {
        Assert.Throws<DivideByZeroException>(() => Calculator.Divide(1, 0));
    }

    [Theory]
    [InlineData(2, 3, 8)]
    [InlineData(5, 0, 1)]
    [InlineData(3, 1, 3)]
    public void Power_ReturnsCorrectResult(double baseValue, int exponent, double expected)
    {
        Assert.Equal(expected, Calculator.Power(baseValue, exponent));
    }

    [Fact]
    public void Power_NegativeExponent_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculator.Power(2, -1));
    }
}

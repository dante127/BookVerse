using BookVerse.Application.Common.Exceptions;
using BookVerse.Application.Common.Extensions;
using BookVerse.Application.Common.Interfaces;
using BookVerse.Application.Common.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace BookVerse.UnitTests.Application;

/// <summary>
/// Phase 5 code-quality regressions: the shared slug algorithm (CQ-02)
/// and the single auth-guard helper (CQ-02).
/// </summary>
public class Phase5CodeQualityTests
{
    [Theory]
    [InlineData("Anna-Marie O'Brien", "anna-marie-o-brien")]
    [InlineData("  Gabriel   García   Márquez  ", "gabriel-garcia-marquez")]
    [InlineData("../../../etc/passwd", "etc-passwd")]
    [InlineData("Ça va! Really?? Yes", "ca-va-really-yes")]
    [InlineData("Dragon Slayer 2", "dragon-slayer-2")]
    public void Slugify_ProducesCleanAsciiSlugs(string input, string expected)
    {
        Slug.Slugify(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("!!!")]
    [InlineData("???")]
    [InlineData("...")]
    public void Slugify_WithoutLettersOrDigits_ThrowsValidation(string input)
    {
        var act = () => Slug.Slugify(input);
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void RequireUserId_AnonymousPrincipal_ThrowsUnauthorized()
    {
        var service = new Mock<ICurrentUserService>();
        service.Setup(s => s.IsAuthenticated).Returns(false);

        var act = () => service.Object.RequireUserId();

        act.Should().Throw<UnauthorizedException>();
    }

    [Fact]
    public void RequireUserId_AuthenticatedPrincipal_ReturnsUserId()
    {
        var id = Guid.NewGuid();
        var service = new Mock<ICurrentUserService>();
        service.Setup(s => s.IsAuthenticated).Returns(true);
        service.Setup(s => s.UserId).Returns(id);

        service.Object.RequireUserId().Should().Be(id);
    }
}

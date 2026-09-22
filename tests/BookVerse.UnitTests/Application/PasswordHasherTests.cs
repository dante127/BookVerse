using BookVerse.Infrastructure.Identity;
using FluentAssertions;
using Xunit;

namespace BookVerse.UnitTests.Application;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void HashPassword_ShouldGenerateDistinctSaltsAndHashes()
    {
        // Act
        var (hash1, salt1) = _hasher.HashPassword("Password123!");
        var (hash2, salt2) = _hasher.HashPassword("Password123!");

        // Assert
        hash1.Should().NotBeEmpty();
        salt1.Should().NotBeEmpty();
        salt1.Should().NotBe(salt2);
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void VerifyPassword_WithCorrectPassword_ShouldReturnTrue()
    {
        // Arrange
        var password = "SecureEnterprisePassword2026!";
        var (hash, salt) = _hasher.HashPassword(password);

        // Act
        var isValid = _hasher.VerifyPassword(password, hash, salt);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_WithWrongPassword_ShouldReturnFalse()
    {
        // Arrange
        var (hash, salt) = _hasher.HashPassword("CorrectPassword");

        // Act
        var isValid = _hasher.VerifyPassword("WrongPassword", hash, salt);

        // Assert
        isValid.Should().BeFalse();
    }
}

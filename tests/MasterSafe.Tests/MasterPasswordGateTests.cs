// File:    MasterPasswordGateTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.App.ViewModels;
using Xunit;

namespace MasterSafe.Tests;

public sealed class MasterPasswordGateTests
{
    private static string? TryCreate(string password)
    {
        var login = new LoginViewModel { IsCreateMode = true, MasterPassword = password, ConfirmPassword = password };
        string? accepted = null;
        login.UnlockRequested += (_, p) => accepted = p;

        login.UnlockCommand.Execute(null);

        return accepted is null ? login.Error : null;
    }

    [Theory]
    [InlineData("aaaaaaaaaaaaaaaa")]
    [InlineData("123456789012")]
    [InlineData("abcdefghijkl")]
    [InlineData("passwordpassword")]
    public void EasyToGuessMasterPasswords_AreRefusedWhenCreatingAVault(string password)
        => TryCreate(password).Should().Contain("too easy to guess");

    [Theory]
    [InlineData("correct horse battery staple")]
    [InlineData("K7QF2-M9XTD-W3HPA-8RCNB")]
    [InlineData("a long test master password")]
    public void LongPhrasesAndRandomPasswords_AreAccepted(string password)
        => TryCreate(password).Should().BeNull();

    [Fact]
    public void ShortPasswords_StillGetTheLengthMessage()
        => TryCreate("short").Should().Contain("at least 12");

    [Fact]
    public void SigningIn_NeverChecksStrength_SoAnExistingShortPasswordStillWorks()
    {
        var login = new LoginViewModel { IsCreateMode = false, MasterPassword = "x" };
        string? passed = null;
        login.UnlockRequested += (_, p) => passed = p;

        login.UnlockCommand.Execute(null);

        passed.Should().Be("x");
    }

    [Fact]
    public void TheHintFollowsWhatIsTyped()
    {
        var login = new LoginViewModel { IsCreateMode = true, MasterPassword = "short" };
        login.PasswordHint.Should().Contain("more");

        login.MasterPassword = "aaaaaaaaaaaaaaaa";
        login.PasswordHint.Should().MatchRegex("(Very weak|Weak)");

        login.MasterPassword = "correct horse battery staple";
        login.PasswordHint.Should().NotContain("Weak");
    }
}

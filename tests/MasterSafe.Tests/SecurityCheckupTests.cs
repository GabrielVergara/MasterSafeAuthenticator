// File:    SecurityCheckupTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.App.Services;
using Xunit;

namespace MasterSafe.Tests;

public sealed class SecurityCheckupTests
{
    private static readonly CheckupInput Strong = new(5, true, true, 30, true, true, true, 2);

    [Fact]
    public void StrongSettings_AllPass()
        => SecurityCheckup.Run(Strong).Should().OnlyContain(i => i.IsGood && i.Mark == "Good");

    [Fact]
    public void EachWeakSetting_IsReported_WithAdvice()
    {
        var weak = new CheckupInput(60, false, false, 0, false, false, false, 1);

        var items = SecurityCheckup.Run(weak);

        items.Should().OnlyContain(i => !i.IsGood && i.Mark == "Improve" && i.Detail.Length > 20);
        items.Select(i => i.Title).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void TheLockTimeLimit_IsTenMinutes(int minutes, bool good)
        => SecurityCheckup.Run(Strong with { AutoLockMinutes = minutes }).First(i => i.Title == "Automatic lock").IsGood.Should().Be(good);

    [Theory]
    [InlineData(60, true)]
    [InlineData(61, false)]
    [InlineData(0, false)]
    public void TheClipboardLimit_IsSixtySeconds(int seconds, bool good)
        => SecurityCheckup.Run(Strong with { ClipboardClearSeconds = seconds }).First(i => i.Title == "Clipboard clearing").IsGood.Should().Be(good);
}

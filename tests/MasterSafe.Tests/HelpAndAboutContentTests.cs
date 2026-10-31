// File:    HelpAndAboutContentTests.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using FluentAssertions;
using MasterSafe.App;
using MasterSafe.App.Help;
using MasterSafe.App.ViewModels;
using Xunit;

namespace MasterSafe.Tests;

public sealed class HelpAndAboutContentTests
{
    [Fact]
    public void ParagraphsFlow_WhileListsHeadingsAndBlankLinesStay()
    {
        var text = string.Join('\n',
            "A long first line of a paragraph that is certainly wrapped because it is more than sixty characters",
            "and continues here.",
            string.Empty,
            "Heading",
            "- First item that is long enough to be wrapped because it keeps going past the sixty character mark",
            "  and its second line.",
            "- Second item",
            "1. Numbered");

        var result = HelpContent.Reflow(text).Split('\n');

        result.Should().Equal(
            "A long first line of a paragraph that is certainly wrapped because it is more than sixty characters and continues here.",
            string.Empty,
            "Heading",
            "- First item that is long enough to be wrapped because it keeps going past the sixty character mark and its second line.",
            "- Second item",
            "1. Numbered");
    }

    [Fact]
    public void ShortLinesAreNotJoined()
        => HelpContent.Reflow("Heading\nNext line").Should().Be("Heading\nNext line");

    [Fact]
    public void EveryTopic_IsFilledIn_AndTitlesAreUnique()
    {
        HelpContent.Topics.Select(t => t.Title).Should().OnlyHaveUniqueItems();
        HelpContent.Topics.Should().OnlyContain(t => t.Body.Length > 120 && t.Summary.Length > 10);
        HelpContent.Topics.Count.Should().BeGreaterThanOrEqualTo(15);
    }

    [Theory]
    [InlineData("Passwords")]
    [InlineData("Folders and icons")]
    [InlineData("Attached files")]
    [InlineData("Recycle bin and history")]
    [InlineData("Password health")]
    [InlineData("Keyboard and mouse")]
    [InlineData("Privacy and locking")]
    [InlineData("Updates")]
    [InlineData("Recovery and the forgotten master password")]
    [InlineData("Troubleshooting")]
    public void TheHelpCoversTheFeature(string title)
        => HelpContent.Topics.Should().Contain(t => t.Title == title);

    [Fact]
    public void TheHelpNamesTheArticle_AndNoTextContainsATypoLikePlaceholder()
    {
        HelpContent.Topics.Single(t => t.Title.StartsWith("About")).Body.Should().Contain(AppInfo.ArticleUrl);
        HelpContent.Topics.Should().OnlyContain(t => !t.Body.Contains("TODO") && !t.Body.Contains("lorem", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheHelpCanBeSearched()
    {
        var vm = new HelpViewModel();
        var all = vm.Topics.Count;

        vm.SearchText = "clipboard";
        vm.Topics.Count.Should().BeInRange(2, all - 1);
        vm.Topics.Should().Contain(t => t.Title == "Privacy and locking");
        vm.SelectedTopic.Should().Be(vm.Topics[0]);

        vm.SearchText = "zzzz no such thing";
        vm.Topics.Should().BeEmpty();
        vm.HasNoResults.Should().BeTrue();
        vm.SelectedTopic.Should().BeNull();

        vm.SearchText = string.Empty;
        vm.Topics.Should().HaveCount(all);
        vm.HasNoResults.Should().BeFalse();
        vm.SelectedTopic.Should().NotBeNull();
    }

    [Fact]
    public void ASearchKeepsTheSelectedTopic_WhenItStillMatches()
    {
        var vm = new HelpViewModel();
        vm.SelectedTopic = vm.Topics.Single(t => t.Title == "Passwords");

        vm.SearchText = "password";

        vm.SelectedTopic!.Title.Should().Be("Passwords");
    }

    // ------------------------------------------------------------------ About

    [Fact]
    public void AboutLinksTheArticleFirst_AndEverythingIsHttps()
    {
        var about = new AboutViewModel();

        about.Links[0].Url.Should().Be("https://sitio.gabriel-vergara.com/articles/mastersafe-authenticator/");
        about.ArticleUrl.Should().Be(AppInfo.ArticleUrl);
        about.Links.Should().OnlyContain(l => l.Url.StartsWith("https://") && l.Title.Length > 0 && l.Description.Length > 0);
        about.Links.Select(l => l.Title).Should().Contain(["Article", "Website", "Source code", "Releases", "Security policy", "Licence", "Third-party notices"]);
    }

    [Fact]
    public void TheArticleAndTheOtherLinks_AreOpenedThroughTheBrowserHook()
    {
        var about = new AboutViewModel();
        var opened = new List<string>();
        about.UrlOpener = opened.Add;

        about.OpenArticleCommand.Execute(null);
        about.OpenWebsiteCommand.Execute(null);
        about.OpenProjectCommand.Execute(null);
        about.OpenLinkCommand.Execute(about.Links.Single(l => l.Title == "Releases"));

        opened.Should().Equal(
            "https://sitio.gabriel-vergara.com/articles/mastersafe-authenticator/",
            "https://www.gabriel-vergara.com/",
            "https://github.com/GabrielVergara/MasterSafeAuthenticator",
            "https://github.com/GabrielVergara/MasterSafeAuthenticator/releases");
    }

    [Theory]
    [InlineData("https://sitio.gabriel-vergara.com/articles/mastersafe-authenticator/", true)]
    [InlineData("https://www.gabriel-vergara.com/", true)]
    [InlineData("https://github.com/GabrielVergara/MasterSafeAuthenticator/issues", true)]
    [InlineData("http://www.gabriel-vergara.com/", false)]
    [InlineData("https://github.com/someone-else/repo", false)]
    [InlineData("https://evil.example.com/", false)]
    [InlineData("https://gabriel-vergara.com.evil.example/", false)]
    [InlineData("https://user:pw@www.gabriel-vergara.com/", false)]
    [InlineData("file:///c:/windows/system32/cmd.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyTheProjectAndTheAuthorsSitesCanBeOpened(string? url, bool allowed)
        => AboutViewModel.IsAllowedLink(url).Should().Be(allowed);

    [Fact]
    public void AnAddressOutsideTheAllowList_IsNeverPassedToTheBrowser()
    {
        var about = new AboutViewModel();
        var opened = new List<string>();
        about.UrlOpener = opened.Add;

        about.OpenLinkCommand.Execute(new AboutLink("Bad", "x", "https://evil.example.com/"));

        opened.Should().BeEmpty();
    }

    [Fact]
    public void TheAboutWindowShowsTheSigningKeyFingerprint_ThatMatchesThePublishedOne()
    {
        var about = new AboutViewModel();

        about.SigningKeyFingerprint.Replace(" ", string.Empty).Should().Be("28ad8deb1624d6f04fe9100e25edb2666ad904e02824af14ef2fe6c8d2a74b8a");
        about.SigningKeyFingerprint.Split(' ').Should().HaveCount(8);
    }

    [Fact]
    public void TheVersionLineHasTheVersion_AndTheTaglineDescribesTheProduct()
    {
        var about = new AboutViewModel();

        about.VersionText.Should().StartWith("Version ").And.Contain(AppInfo.Version);
        about.Tagline.Should().Contain("encrypted").And.Contain("offline");
        about.LicenseText.Should().Contain("non-commercial");
    }
}

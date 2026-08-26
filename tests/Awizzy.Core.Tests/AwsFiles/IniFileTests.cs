using Awizzy.Core.AwsFiles;

namespace Awizzy.Core.Tests.AwsFiles;

public class IniFileTests
{
    private const string Marker = "; test-marker";

    [Test]
    public async Task Parse_ThenToString_RoundTripsUntouchedContent()
    {
        var content = "; top comment\n\n[work]\naws_access_key_id = AKIA123\n# inline note\naws_secret_access_key = abc\n\n[other]\nkey = value\n";

        var ini = IniFile.Parse(content);

        await Assert.That(ini.ToString()).IsEqualTo(content);
    }

    [Test]
    public async Task Parse_PreservesCrlfNewlines()
    {
        var content = "[work]\r\nkey = value\r\n";

        var ini = IniFile.Parse(content);

        await Assert.That(ini.ToString()).IsEqualTo(content);
    }

    [Test]
    public async Task SetSection_OnForeignFile_LeavesOtherSectionsUntouched()
    {
        var ini = IniFile.Parse("[personal]\naws_access_key_id = AKIAPERSONAL\n; my comment\n");

        ini.SetSection("managed", [new("aws_access_key_id", "AKIANEW")], Marker);
        var result = ini.ToString();

        await Assert.That(result).Contains("[personal]");
        await Assert.That(result).Contains("aws_access_key_id = AKIAPERSONAL");
        await Assert.That(result).Contains("; my comment");
        await Assert.That(result).Contains("[managed]");
        await Assert.That(result).Contains(Marker);
        await Assert.That(result).Contains("aws_access_key_id = AKIANEW");
    }

    [Test]
    public async Task SetSection_OnExistingSection_ReplacesItsContent()
    {
        var ini = IniFile.Parse($"[managed]\n{Marker}\naws_access_key_id = OLD\naws_session_token = OLDTOKEN\n");

        ini.SetSection("managed", [new("aws_access_key_id", "NEW")], Marker);
        var result = ini.ToString();

        await Assert.That(result).Contains("aws_access_key_id = NEW");
        await Assert.That(result).DoesNotContain("OLD");
        await Assert.That(result).DoesNotContain("aws_session_token");
    }

    [Test]
    public async Task RemoveSection_RemovesOnlyThatSection()
    {
        var ini = IniFile.Parse("[a]\nkey = 1\n\n[b]\nkey = 2\n\n[c]\nkey = 3\n");

        ini.RemoveSection("b");
        var result = ini.ToString();

        await Assert.That(result).Contains("[a]");
        await Assert.That(result).DoesNotContain("[b]");
        await Assert.That(result).Contains("[c]");
    }

    [Test]
    public async Task SectionHasMarker_DistinguishesManagedSections()
    {
        var ini = IniFile.Parse($"[managed]\n{Marker}\nkey = 1\n\n[foreign]\nkey = 2\n");

        await Assert.That(ini.SectionHasMarker("managed", Marker)).IsTrue();
        await Assert.That(ini.SectionHasMarker("foreign", Marker)).IsFalse();
        await Assert.That(ini.SectionHasMarker("missing", Marker)).IsFalse();
    }

    [Test]
    public async Task Parse_ToleratesGarbageLines_AndPreservesThem()
    {
        var content = "[work]\nthis is not a key value pair\nkey = value\n";

        var ini = IniFile.Parse(content);

        await Assert.That(ini.ToString()).IsEqualTo(content);
    }

    [Test]
    public async Task Parse_StripsUtf8Bom()
    {
        var ini = IniFile.Parse("﻿[work]\nkey = value\n");

        await Assert.That(ini.HasSection("work")).IsTrue();
        await Assert.That(ini.ToString()).IsEqualTo("[work]\nkey = value\n");
    }

    [Test]
    public async Task Parse_HandlesMissingTrailingNewline()
    {
        var content = "[work]\nkey = value";

        var ini = IniFile.Parse(content);

        await Assert.That(ini.ToString()).IsEqualTo(content);
    }

    [Test]
    public async Task SetSection_OnEmptyFile_CreatesSection()
    {
        var ini = IniFile.Empty();

        ini.SetSection("default", [new("aws_access_key_id", "AKIA123")], Marker);
        var result = ini.ToString();

        await Assert.That(result).Contains("[default]");
        await Assert.That(result).Contains("aws_access_key_id = AKIA123");
    }

    [Test]
    public async Task SectionNames_AreCaseSensitive_LikeAwsProfiles()
    {
        var ini = IniFile.Parse("[Work]\nkey = value\n");

        await Assert.That(ini.HasSection("work")).IsFalse();
        await Assert.That(ini.HasSection("Work")).IsTrue();
    }

    [Test]
    public async Task Parse_TrimsWhitespaceAroundSectionNames()
    {
        var ini = IniFile.Parse("  [ work ]  \nkey = value\n");

        await Assert.That(ini.HasSection("work")).IsTrue();
    }
}

using MSAVA_API.Diagnostics;

namespace MSAVA_API.Tests;

public class RequestLogValueSanitizerTests
{
    [Test]
    public void Sanitize_ReturnsEmptyStringForMissingValue()
    {
        RequestLogValueSanitizer.Sanitize(null).Should().BeEmpty();
        RequestLogValueSanitizer.Sanitize(string.Empty).Should().BeEmpty();
    }

    [Test]
    public void Sanitize_ReplacesLineBreaksAndTabsWithSingleLineSpaces()
    {
        string sanitized = RequestLogValueSanitizer.Sanitize("client\r\nagent\tvalue");

        sanitized.Should().Be("client  agent value");
    }

    [Test]
    public void Sanitize_DropsOtherControlCharacters()
    {
        string sanitized = RequestLogValueSanitizer.Sanitize("prefix\u0001suffix");

        sanitized.Should().Be("prefixsuffix");
    }

    [Test]
    public void Sanitize_LimitsClientControlledValues()
    {
        string value = new('a', RequestLogValueSanitizer.MaximumValueLength + 20);

        string sanitized = RequestLogValueSanitizer.Sanitize(value);

        sanitized.Should().HaveLength(RequestLogValueSanitizer.MaximumValueLength);
        sanitized.Should().Be(new string('a', RequestLogValueSanitizer.MaximumValueLength));
    }
}

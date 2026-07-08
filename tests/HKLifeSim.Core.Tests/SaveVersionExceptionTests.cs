using FluentAssertions;
using HKLifeSim.Core.Persistence;

namespace HKLifeSim.Core.Tests;

public sealed class SaveVersionExceptionTests
{
    [Fact]
    public void Parameterless_Constructor_Produces_A_Usable_Exception()
    {
        var exception = new SaveVersionException();

        exception.Should().BeAssignableTo<Exception>();
    }

    [Fact]
    public void Message_Constructor_Sets_The_Message()
    {
        var exception = new SaveVersionException("bad save");

        exception.Message.Should().Be("bad save");
    }

    [Fact]
    public void Message_And_InnerException_Constructor_Sets_Both()
    {
        var inner = new InvalidOperationException("root cause");

        var exception = new SaveVersionException("bad save", inner);

        exception.Message.Should().Be("bad save");
        exception.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void Version_Constructor_Sets_Found_And_MaxSupported_Versions_And_A_Clear_Message()
    {
        var exception = new SaveVersionException(foundVersion: 99, maxSupportedVersion: 1);

        exception.FoundVersion.Should().Be(99);
        exception.MaxSupportedVersion.Should().Be(1);
        exception.Message.Should().Contain("99").And.Contain("1");
    }
}

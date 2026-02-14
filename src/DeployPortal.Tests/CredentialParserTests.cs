using DeployPortal.Services;
using NUnit.Framework;

namespace DeployPortal.Tests;

[TestFixture]
public class CredentialParserTests
{
    [Test]
    public void Parse_ReturnsEmpty_WhenNullOrWhiteSpace()
    {
        var r = CredentialParser.Parse(null!);
        Assert.That(r.ApplicationId, Is.Null);
        Assert.That(r.HasCredentials, Is.False);

        r = CredentialParser.Parse("");
        Assert.That(r.HasCredentials, Is.False);

        r = CredentialParser.Parse("   ");
        Assert.That(r.HasCredentials, Is.False);
    }

    [Test]
    public void Parse_ExtractsApplicationId_FromCommonFormats()
    {
        var text = "Application (Client) ID: abc-123-def";
        var r = CredentialParser.Parse(text);
        Assert.That(r.ApplicationId, Is.EqualTo("abc-123-def"));
    }

    [Test]
    public void Parse_ExtractsTenantId_FromCommonFormats()
    {
        var text = "Directory (Tenant) ID: tenant-456";
        var r = CredentialParser.Parse(text);
        Assert.That(r.TenantId, Is.EqualTo("tenant-456"));
    }

    [Test]
    public void Parse_ExtractsEnvironments_FromEnvironmentsLine()
    {
        var text = "Environments (OK):  env1.crm.dynamics.com, env2.crm.dynamics.com";
        var r = CredentialParser.Parse(text);
        Assert.That(r.Environments, Has.Count.EqualTo(2));
        Assert.That(r.Environments, Does.Contain("env1.crm.dynamics.com"));
        Assert.That(r.Environments, Does.Contain("env2.crm.dynamics.com"));
    }

    [Test]
    public void Parse_ExtractsEnvironments_FromQuotedUrls()
    {
        var text = @"--environment ""myenv.crm.dynamics.com""";
        var r = CredentialParser.Parse(text);
        Assert.That(r.Environments, Does.Contain("myenv.crm.dynamics.com"));
    }

    [Test]
    public void Parse_IgnoresInvalidEnvironmentUrls()
    {
        var text = "Environments (OK):  not-a-env, valid.crm.dynamics.com";
        var r = CredentialParser.Parse(text);
        Assert.That(r.Environments, Has.Count.EqualTo(1));
        Assert.That(r.Environments[0], Is.EqualTo("valid.crm.dynamics.com"));
    }
}

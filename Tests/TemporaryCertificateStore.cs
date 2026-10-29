using NUnit.Framework;

namespace Coflnet.Sky.PlayerName.Tests;

[SetUpFixture]
public class TemporaryCertificateStore
{
    private string? previousHome;
    private string directory = null!;

    [OneTimeSetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), "skyplayername-test-home-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        previousHome = Environment.GetEnvironmentVariable("HOME");
        Environment.SetEnvironmentVariable("HOME", directory);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("HOME", previousHome);
        Directory.Delete(directory, true);
    }
}

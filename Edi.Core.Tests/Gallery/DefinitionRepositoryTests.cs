using Edi.Core.Gallery.Definition;
using Edi.Core.Services;
using ConfigurationManager = Edi.Core.Services.ConfigurationManager;

namespace Edi.Core.Tests.Gallery;

public class DefinitionRepositoryTests
{
    [Fact]
    public async Task GeneratesDefinitionForFractionalMetadataDuration()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "edi-definition-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(temporaryDirectory, "PMV MIX7.funscript"),
                """
                {
                  "metadata": { "duration": 941.76 },
                  "actions": [
                    { "at": 0, "pos": 0 },
                    { "at": 941760, "pos": 100 }
                  ]
                }
                """,
                TestContext.Current.CancellationToken);
            var configuration = new ConfigurationManager(
                Path.Combine(temporaryDirectory, "EdiConfig.json"),
                Path.Combine(temporaryDirectory, "UserConfig.json"));
            var repository = new DefinitionRepository(configuration);

            await repository.Init(temporaryDirectory);

            var definition = Assert.Single(repository.GetAll());
            Assert.Equal("PMV MIX7", definition.Name);
            Assert.Equal(941760, definition.EndTime);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}

using Edi.Core.Funscript.FileJson;
using Edi.Core.Tests.Support;

namespace Edi.Core.Tests.Players;

public class GalleryRepositoryIntegrationTests
{
    [Fact]
    public async Task TestGalleriesLoadDefinitionsAndFunscriptCommands()
    {
        await using var rig = await PlayerTestRig.CreateAsync();

        var definition = rig.Definitions.Get("hit");
        var funscript = rig.Funscripts.Get("hit", "default");

        Assert.NotNull(definition);
        Assert.Equal("reaction", definition.Type);
        Assert.False(definition.Loop);
        Assert.Equal(80, definition.Duration);

        Assert.NotNull(funscript);
        Assert.Equal(80, funscript.Duration);
        Assert.False(funscript.Loop);
        Assert.True(funscript.AxesCommands.TryGetValue(Axis.Default, out var commands));
        Assert.NotEmpty(commands);
    }

    [Fact]
    public async Task EveryDefinitionHasAResolvableDefaultFunscript()
    {
        await using var rig = await PlayerTestRig.CreateAsync();

        foreach (var definition in rig.Definitions.GetAll())
        {
            var funscript = rig.Funscripts.Get(definition.Name, "default");

            Assert.NotNull(funscript);
            Assert.Equal(definition.Duration, funscript.Duration);
            Assert.Equal(definition.Loop, funscript.Loop);
        }
    }
}

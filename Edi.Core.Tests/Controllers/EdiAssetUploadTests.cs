using Edi.Core.Controllers;

namespace Edi.Core.Tests.Controllers;

public class EdiAssetUploadTests
{
    [Theory]
    [InlineData("scene.funscript")]
    [InlineData("scene.Stroke.funscript")]
    [InlineData("Definitions.csv")]
    [InlineData("Definitions_auto.csv")]
    [InlineData("BundleDefinition.txt")]
    [InlineData("BundleDefinition.fast.txt")]
    [InlineData("ambient.mp3")]
    public void RecognizesAssetsUsedByEdiRepositories(string fileName)
    {
        Assert.True(EdiController.IsRecognizedAssetFileName(fileName));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("Definitions.csv.exe")]
    [InlineData("scene.script")]
    [InlineData("scene.mp4")]
    [InlineData("scene.webm")]
    [InlineData("scene.avi")]
    [InlineData("scene.mkv")]
    [InlineData("scene.mov")]
    public void RejectsVideosAndUnknownFiles(string fileName)
    {
        Assert.False(EdiController.IsRecognizedAssetFileName(fileName));
    }
}

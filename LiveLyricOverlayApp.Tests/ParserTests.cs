using System.IO;
using Xunit;
using LiveLyricOverlayApp.Engine;

namespace LiveLyricOverlayApp.Tests
{
    public class ParserTests
    {
        [Fact]
        public void TestParseLrc()
        {
            // The test.lrc file is in the same directory as the project, 
            // but during test execution the working directory might be bin/Debug/net8.0.
            // Let's create it dynamically in the test.
            string content = "[offset:500]\n[00:10.00][01:20.00]Hello\n[00:10.00][01:20.00]你好\n[00:13.50]World";
            string tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, content, System.Text.Encoding.UTF8);

            var doc = LyricParser.Parse(tempFile);

            Assert.Equal(500, doc.OffsetMs);
            Assert.Equal(3, doc.Lines.Count);

            // First line: 10 seconds (10000 ms)
            Assert.Equal(10000, doc.Lines[0].TimeMs);
            Assert.Equal("Hello", doc.Lines[0].Text);
            Assert.Equal("你好", doc.Lines[0].Translation);

            // Second line: 13.5 seconds (13500 ms)
            Assert.Equal(13500, doc.Lines[1].TimeMs);
            Assert.Equal("World", doc.Lines[1].Text);
            Assert.Empty(doc.Lines[1].Translation);

            // Third line (multi-tag): 80 seconds (80000 ms)
            Assert.Equal(80000, doc.Lines[2].TimeMs);
            Assert.Equal("Hello", doc.Lines[2].Text);
            Assert.Equal("你好", doc.Lines[2].Translation);

            File.Delete(tempFile);
        }
    }
}

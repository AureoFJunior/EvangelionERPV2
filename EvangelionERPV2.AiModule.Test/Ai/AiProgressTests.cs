using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    public class AiProgressTests
    {
        [Fact]
        public void Chunk_RejoinsToTheOriginal()
        {
            var text = new string('a', 250) + "fim";

            var chunks = AiTextChunker.Split(text, size: 100).ToList();

            Assert.Equal(text, string.Concat(chunks));
        }

        [Fact]
        public void Chunk_OfShortText_IsOneChunk()
        {
            var chunks = AiTextChunker.Split("curto", size: 100).ToList();

            Assert.Single(chunks);
            Assert.Equal("curto", chunks[0]);
        }

        [Fact]
        public void Chunk_OfEmptyText_IsNothing()
        {
            Assert.Empty(AiTextChunker.Split(string.Empty, size: 100));
        }
    }
}

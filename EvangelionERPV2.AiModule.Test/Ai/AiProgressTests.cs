using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    /// <summary>
    /// Chunking is not cosmetic: a single 800-character frame defeats the point of the hub. These
    /// pin that the whole text survives the split, because a chunker that drops the tail would
    /// silently truncate every long answer and look like a model problem.
    /// </summary>
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

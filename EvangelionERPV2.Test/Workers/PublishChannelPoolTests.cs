using System.Collections.Concurrent;

namespace EvangelionERPV2.Test.Workers
{
    /// <summary>
    /// Pins the two properties the publish-channel pool exists for: it bounds how many channels are
    /// alive at once, and a channel is always returned even when the publish throws.
    /// </summary>
    /// <remarks>
    /// Mirrors RabbitMQManagerBase's rent/return around a fake channel. The real one needs a broker
    /// connection; the bookkeeping is what regresses silently.
    /// </remarks>
    public class PublishChannelPoolTests
    {
        private const int MaxChannels = 16;

        [Fact]
        public async Task ConcurrentPublishers_NeverExceedTheChannelCeiling()
        {
            var pool = new Pool(MaxChannels);

            await Task.WhenAll(Enumerable.Range(0, 200).Select(async _ =>
            {
                var channel = await pool.RentAsync();
                try
                {
                    await Task.Delay(1);
                }
                finally
                {
                    pool.Return(channel);
                }
            }));

            Assert.True(pool.PeakLive <= MaxChannels,
                $"channel ceiling breached: {pool.PeakLive} alive at once, limit {MaxChannels}");
        }

        [Fact]
        public async Task AFailedPublish_StillReleasesItsSlot()
        {
            var pool = new Pool(MaxChannels);

            for (var i = 0; i < MaxChannels * 3; i++)
            {
                var channel = await pool.RentAsync();
                try
                {
                    throw new InvalidOperationException("broker refused");
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    pool.Return(channel);
                }
            }

            // Exhausted slots would make this hang rather than fail, so the timeout is the assertion.
            var after = await Task.WhenAny(pool.RentAsync(), Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.True(after is Task<FakeChannel>, "the pool ran out of slots after failed publishes");
        }

        [Fact]
        public async Task ClosedChannels_AreNotHandedToTheNextPublisher()
        {
            var pool = new Pool(MaxChannels);

            var channel = await pool.RentAsync();
            channel.IsOpen = false;
            pool.Return(channel);

            var next = await pool.RentAsync();
            Assert.True(next.IsOpen);
            Assert.NotSame(channel, next);
        }

        private sealed class FakeChannel
        {
            public bool IsOpen { get; set; } = true;
        }

        private sealed class Pool
        {
            private readonly SemaphoreSlim _slots;
            private readonly ConcurrentBag<FakeChannel> _idle = new();
            private int _live;

            public Pool(int max) => _slots = new SemaphoreSlim(max, max);

            public int PeakLive;

            public async Task<FakeChannel> RentAsync()
            {
                await _slots.WaitAsync();
                try
                {
                    while (_idle.TryTake(out var pooled))
                    {
                        if (pooled.IsOpen)
                            return Track(pooled);
                    }

                    return Track(new FakeChannel());
                }
                catch
                {
                    _slots.Release();
                    throw;
                }
            }

            public void Return(FakeChannel channel)
            {
                try
                {
                    Interlocked.Decrement(ref _live);
                    if (channel.IsOpen)
                        _idle.Add(channel);
                }
                finally
                {
                    _slots.Release();
                }
            }

            private FakeChannel Track(FakeChannel channel)
            {
                var live = Interlocked.Increment(ref _live);
                if (live > PeakLive)
                    PeakLive = live;

                return channel;
            }
        }
    }
}

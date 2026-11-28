using System.Threading.Channels;
using Microsoft.Extensions.Options;
using SwfCrypt.Api.Config;

namespace SwfCrypt.Api.Services;

public sealed class AnalysisQueue
{
    private readonly Channel<Guid> _channel;

    public AnalysisQueue(IOptions<AnalysisOptions> options)
    {
        var capacity = Math.Max(1, options.Value.QueueCapacity);
        _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });
    }

    public ChannelReader<Guid> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(jobId, ct);
}

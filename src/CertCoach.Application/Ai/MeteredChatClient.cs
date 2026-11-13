using System.Runtime.CompilerServices;
using CertCoach.Application.Billing;
using Microsoft.Extensions.AI;

namespace CertCoach.Application.Ai;

/// <summary>
/// Wraps every model call: refuses it once the learner's monthly allowance is used, and adds the tokens it used
/// afterwards. All AI features go through <see cref="IChatClient"/>, so none can skip the meter.
/// </summary>
public sealed class MeteredChatClient(IChatClient inner, AiAllowance allowance) : DelegatingChatClient(inner)
{
    /// <summary>The service key of the unmetered provider client, which only this class should use.</summary>
    public const string ProviderClientKey = "ai-provider";

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        await allowance.EnsureAvailableAsync(cancellationToken);
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        await allowance.RecordAsync(response.Usage, CancellationToken.None);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await allowance.EnsureAvailableAsync(cancellationToken);
        UsageDetails? usage = null;
        try
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                foreach (var content in update.Contents.OfType<UsageContent>())
                {
                    usage ??= new UsageDetails();
                    usage.Add(content.Details);
                }

                yield return update;
            }
        }
        finally
        {
            // Also when the stream fails or is abandoned, for whatever usage it reported before that.
            await allowance.RecordAsync(usage, CancellationToken.None);
        }
    }
}

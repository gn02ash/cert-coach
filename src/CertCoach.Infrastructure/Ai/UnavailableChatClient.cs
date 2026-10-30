using Microsoft.Extensions.AI;

namespace CertCoach.Infrastructure.Ai;

/// <summary>
/// Registered in offline mode so services can depend on <see cref="IChatClient"/> unconditionally.
/// Callers check <see cref="Application.Ai.AiCapabilities.ChatAvailable"/> first; reaching this is a bug.
/// </summary>
internal sealed class UnavailableChatClient : IChatClient
{
    private const string Message = "No chat provider is configured. Set Ai:Chat:Provider to use AI answers.";

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Message);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(Message);

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}

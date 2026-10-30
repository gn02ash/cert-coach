namespace CertCoach.Application.Ask;

public enum AskRole
{
    User,
    Assistant,
}

/// <summary>An earlier turn of the conversation on the page. Conversations are not stored.</summary>
public sealed record AskTurn(AskRole Role, string Text);

public sealed record AskRequest(string Question, Guid? CertificationId = null, IReadOnlyList<AskTurn>? History = null);

/// <summary>A numbered source the answer may cite as [Number].</summary>
public sealed record AskSource(
    int Number,
    Guid ChunkId,
    Guid DocumentId,
    string DocumentTitle,
    string HeadingPath,
    int PageStart,
    int PageEnd,
    string Text);

public enum AnswerKind
{
    /// <summary>Written by the chat model from the sources.</summary>
    Ai,

    /// <summary>Offline mode: the most relevant sentences, quoted.</summary>
    Quoted,

    /// <summary>Nothing in the documents was close enough to the question; no model was called.</summary>
    NotCovered,

    /// <summary>The model declined to answer.</summary>
    Declined,
}

/// <summary>What the answer stream sends, in order: one <see cref="AskSourcesEvent"/>, any number of <see cref="AskDeltaEvent"/>s, one <see cref="AskDoneEvent"/>.</summary>
public abstract record AskEvent(string Type);

public sealed record AskSourcesEvent(IReadOnlyList<AskSource> Sources) : AskEvent("sources");

public sealed record AskDeltaEvent(string Text) : AskEvent("delta");

/// <param name="Cited">Source numbers the answer actually cites. Markers for any other number should not be shown.</param>
public sealed record AskDoneEvent(AnswerKind Kind, IReadOnlyList<int> Cited, string? Notice) : AskEvent("done");

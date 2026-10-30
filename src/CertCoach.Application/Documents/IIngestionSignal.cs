namespace CertCoach.Application.Documents;

/// <summary>Wakes the ingestion worker so a new upload is processed at once rather than on its next poll.</summary>
public interface IIngestionSignal
{
    void Notify();
}

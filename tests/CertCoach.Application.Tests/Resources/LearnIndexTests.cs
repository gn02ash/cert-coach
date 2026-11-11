using CertCoach.Application.Resources;

namespace CertCoach.Application.Tests.Resources;

public class LearnIndexTests
{
    private static LearnModule Module(string title, string summary) =>
        new(title, title, summary, new Uri("https://learn.microsoft.com/training/modules/" + Uri.EscapeDataString(title)), 30, ["intermediate"]);

    private static readonly LearnIndex Index = new(
    [
        Module("Implement retrieval augmented generation with Azure AI Search", "Ground a generative AI app in your own data using vector and keyword search."),
        Module("Get started with vector search in Azure AI Search", "Create embeddings and run vector queries over an index."),
        Module("Introduction to Azure virtual machines", "Create and size virtual machines, and connect to them."),
        Module("Monitor virtual machines with Azure Monitor", "Collect metrics and logs from virtual machines and set alerts."),
        Module("Introduction to Power BI", "Build reports and dashboards for your business data."),
    ]);

    [Fact]
    public void Finds_the_modules_sharing_an_objectives_distinctive_words_best_first()
    {
        var matches = Index.Search("Combine vector and keyword search", 5);

        matches[0].Module.Title.ShouldBe("Implement retrieval augmented generation with Azure AI Search");
        matches.ShouldNotContain(m => m.Module.Title.Contains("virtual machines") || m.Module.Title.Contains("Power BI"));
        matches.ShouldAllBe(m => m.Relevance >= LearnIndex.MinRelevance && m.Relevance <= 1);
    }

    [Fact]
    public void Suggests_nothing_for_an_objective_no_module_covers()
    {
        Index.Search("Negotiate enterprise licensing discounts", 5).ShouldBeEmpty();
        Index.Search("Implement and configure solutions", 5).ShouldBeEmpty(); // Only stop words.
    }

    [Fact]
    public void A_word_in_the_title_counts_for_more_than_one_in_the_summary()
    {
        var matches = Index.Search("Monitor virtual machines", 5);

        matches[0].Module.Title.ShouldBe("Monitor virtual machines with Azure Monitor");
        matches.Count.ShouldBe(2);
    }
}

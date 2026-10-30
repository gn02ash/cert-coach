using CertCoach.Application.Certifications.Outlines;

namespace CertCoach.Application.Tests.Certifications;

public class OutlineParserTests
{
    [Fact]
    public void Parses_the_sample_study_guide()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "DEMO-101-outline.md"));

        var draft = OutlineParser.Parse(text);

        draft.Warnings.ShouldBeEmpty();
        draft.Domains.Select(d => (d.Name, d.WeightMinPercent, d.WeightMaxPercent)).ShouldBe(
        [
            ("Plan and manage an AI solution", 20, 25),
            ("Build generative AI applications", 30, 35),
            ("Implement retrieval-augmented generation", 20, 25),
            ("Monitor and secure AI workloads", 15, 20),
        ]);
        draft.Domains[1].SkillAreas.Select(s => s.Name).ShouldBe(["Engineer prompts", "Orchestrate tools and agents"]);
        draft.Domains[1].SkillAreas[0].Objectives[2].ShouldBe("Request structured output with a JSON schema");
        draft.ObjectiveCount.ShouldBe(21);
    }

    [Fact]
    public void A_summary_list_and_the_detailed_section_merge_into_one_domain()
    {
        var draft = OutlineParser.Parse("""
            - Design (40-60%)
            - Operate (40-60%)
            ## Design (40-60%)
            - Draw the diagram
            ## Operate (40-60%)
            - Run it
            """);

        draft.Domains.Count.ShouldBe(2);
        draft.Domains.ShouldAllBe(d => d.SkillAreas.Single().Objectives.Count == 1);
    }

    [Fact]
    public void Plain_text_with_bullets_detects_skill_areas_from_the_lines_before_bullets()
    {
        var draft = OutlineParser.Parse("""
            Secure the platform (100%)
            Manage identities
            • Configure single sign-on
            • Assign roles
            Protect data
            • Encrypt storage
            """);

        var domain = draft.Domains.Single();
        domain.SkillAreas.Select(s => s.Name).ShouldBe(["Manage identities", "Protect data"]);
        domain.SkillAreas[0].Objectives.ShouldBe(["Configure single sign-on", "Assign roles"]);
        draft.Warnings.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Plan (15–20%)", 15, 20)]
    [InlineData("Plan (15-20%)", 15, 20)]
    [InlineData("Plan (15 – 20 %)", 15, 20)]
    [InlineData("Plan (15% - 20%)", 15, 20)]
    [InlineData("Plan 15 to 20%", 15, 20)]
    [InlineData("Plan (20–15%)", 15, 20)]
    [InlineData("Plan (10%)", 10, 10)]
    [InlineData("**Plan (15–20%)**", 15, 20)]
    public void Recognizes_weight_formats(string line, int min, int max)
    {
        var domain = OutlineParser.Parse($"{line}\n- Do something").Domains.Single();

        domain.Name.ShouldBe("Plan");
        (domain.WeightMinPercent, domain.WeightMaxPercent).ShouldBe((min, max));
    }

    [Fact]
    public void Objectives_without_a_skill_area_go_under_general()
    {
        var draft = OutlineParser.Parse("""
            ## Build (100%)
            - Write code
            1. Review code
            """);

        var area = draft.Domains.Single().SkillAreas.Single();
        area.Name.ShouldBe(OutlineParser.DefaultSkillAreaName);
        area.Objectives.ShouldBe(["Write code", "Review code"]);
    }

    [Fact]
    public void Sections_after_the_outline_are_ignored()
    {
        var draft = OutlineParser.Parse("""
            ## Build (100%)
            - Write code
            ## Study resources
            - Some link
            ### More reading
            - Another link
            """);

        draft.Domains.Single().SkillAreas.Single().Objectives.ShouldBe(["Write code"]);
    }

    [Fact]
    public void Windows_line_endings_are_handled()
    {
        var draft = OutlineParser.Parse("## Build (100%)\r\n### Code\r\n- Write code\r\n");

        draft.Domains.Single().SkillAreas.Single().Objectives.ShouldBe(["Write code"]);
    }

    [Fact]
    public void Text_without_weights_yields_no_domains_and_a_warning()
    {
        var draft = OutlineParser.Parse("Just some notes\n- not an outline");

        draft.Domains.ShouldBeEmpty();
        draft.Warnings.Single().ShouldContain("No domains found");
    }

    [Fact]
    public void Warns_about_empty_domains_and_weights_that_do_not_add_up()
    {
        var draft = OutlineParser.Parse("""
            ## Build (20-30%)
            - Write code
            ## Ship (10%)
            """);

        draft.Warnings.ShouldContain(w => w.Contains("\"Ship\" has no objectives", StringComparison.Ordinal));
        draft.Warnings.ShouldContain(w => w.Contains("add up to about 35%", StringComparison.Ordinal));
    }

    [Fact]
    public void Unmarked_text_is_kept_as_objectives_with_a_warning()
    {
        var draft = OutlineParser.Parse("""
            Build (100%)
            Write code
            Review code
            """);

        draft.Domains.Single().SkillAreas.Single().Objectives.ShouldBe(["Write code", "Review code"]);
        draft.Warnings.ShouldContain(w => w.Contains("No headings or bullets", StringComparison.Ordinal));
    }
}

using System.Text.RegularExpressions;
using CertCoach.Application.Careers.Catalogue;

namespace CertCoach.Application.Careers;

/// <summary>
/// The words recommendations understand: canonical skill names, the other spellings people use for them, what a
/// skill implies ("PostgreSQL" implies SQL), and the roles job titles map to. Deterministic, so reading a CV and
/// scoring work without a model.
/// </summary>
public static partial class SkillVocabulary
{
    /// <summary>Skills worth recognising in a CV that no catalogue entry lists by name.</summary>
    private static readonly string[] ExtraSkills =
    [
        "java", "javascript", "typescript", "react", "angular", "node.js", ".net", "asp.net core", "golang", "rust", "php", "ruby",
        "postgresql", "mysql", "mongodb", "redis", "kafka", "rabbitmq", "elasticsearch", "pandas", "pytorch", "tensorflow",
        "scikit-learn", "openai", "langchain", "bicep", "ansible", "jenkins", "azure functions", "aws lambda", "service bus",
        "pgvector", "tableau", "snowflake", "airflow", "dbt", "graphql", "rest api", "microservices", "agile", "scrum",
    ];

    /// <summary>Other spellings, mapped to the canonical name.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["k8s"] = "kubernetes", ["csharp"] = "c#", ["dotnet"] = ".net", [".net core"] = ".net", ["asp.net"] = "asp.net core",
        ["gcp"] = "google cloud", ["google cloud platform"] = "google cloud", ["amazon web services"] = "aws", ["microsoft azure"] = "azure",
        ["azure ad"] = "entra id", ["azure active directory"] = "entra id", ["microsoft entra"] = "entra id", ["entra"] = "entra id",
        ["llm"] = "generative ai", ["llms"] = "generative ai", ["large language models"] = "generative ai", ["genai"] = "generative ai",
        ["gen ai"] = "generative ai", ["retrieval-augmented generation"] = "rag", ["retrieval augmented generation"] = "rag",
        ["ml"] = "machine learning", ["nlp"] = "natural language processing", ["iac"] = "infrastructure as code",
        ["continuous integration"] = "ci/cd", ["continuous delivery"] = "ci/cd", ["continuous deployment"] = "ci/cd", ["github actions"] = "ci/cd",
        ["cicd"] = "ci/cd", ["postgres"] = "postgresql", ["mssql"] = "sql server", ["t-sql"] = "sql server", ["nodejs"] = "node.js",
        ["ai agent"] = "ai agents", ["agentic ai"] = "ai agents", ["agentic"] = "ai agents", ["vector database"] = "vector search",
        ["vector databases"] = "vector search", ["embeddings"] = "vector search", ["restful"] = "rest api", ["rest apis"] = "rest api",
        ["web api"] = "api development", ["web apis"] = "api development", ["apis"] = "api development", ["data modeling"] = "data modelling",
        ["data visualization"] = "data visualisation", ["dashboards"] = "data visualisation", ["etl/elt"] = "etl", ["elt"] = "etl",
        ["vm"] = "virtual machines", ["vms"] = "virtual machines", ["sentinel"] = "microsoft sentinel", ["defender"] = "microsoft defender",
        ["fabric"] = "microsoft fabric", ["m365"] = "microsoft 365", ["office 365"] = "microsoft 365", ["github copilot"] = "copilot",
        ["apache spark"] = "spark", ["pyspark"] = "spark", ["apache kafka"] = "kafka",
        ["mlflow"] = "mlops", ["sso"] = "authentication", ["openid connect"] = "oauth", ["oidc"] = "oauth", ["oauth2"] = "oauth", ["oauth 2.0"] = "oauth",
        ["unit testing"] = "testing", ["unit tests"] = "testing", ["tdd"] = "testing", ["xunit"] = "testing",
    };

    /// <summary>Knowing the key means knowing the values too.</summary>
    private static readonly Dictionary<string, string[]> Implies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["postgresql"] = ["sql", "relational databases"], ["sql server"] = ["sql", "relational databases"], ["mysql"] = ["sql", "relational databases"],
        ["mongodb"] = ["nosql"], ["redis"] = ["nosql"], ["docker"] = ["containers"], ["kubernetes"] = ["containers"],
        ["terraform"] = ["infrastructure as code"], ["bicep"] = ["infrastructure as code", "azure"], ["ansible"] = ["infrastructure as code"],
        ["asp.net core"] = ["c#", ".net", "api development"], [".net"] = ["c#"], ["rest api"] = ["api development"], ["graphql"] = ["api development"],
        ["openai"] = ["generative ai"], ["langchain"] = ["generative ai", "python"], ["rag"] = ["generative ai", "vector search"],
        ["ai agents"] = ["generative ai"], ["pgvector"] = ["vector search", "postgresql", "sql"], ["azure devops"] = ["ci/cd"], ["jenkins"] = ["ci/cd"],
        ["github"] = ["git"], ["azure functions"] = ["serverless", "azure"], ["aws lambda"] = ["serverless", "aws"],
        ["service bus"] = ["messaging", "azure"], ["kafka"] = ["messaging", "streaming"], ["rabbitmq"] = ["messaging"],
        ["pandas"] = ["python"], ["pytorch"] = ["python", "machine learning"], ["tensorflow"] = ["python", "machine learning"],
        ["scikit-learn"] = ["python", "machine learning"], ["power bi"] = ["data visualisation", "data analytics"], ["tableau"] = ["data visualisation", "data analytics"],
        ["databricks"] = ["spark"], ["airflow"] = ["data pipelines"], ["dbt"] = ["data pipelines", "sql"], ["snowflake"] = ["data warehousing", "sql"],
        ["bigquery"] = ["data warehousing", "sql", "google cloud"], ["microsoft sentinel"] = ["siem", "security"], ["entra id"] = ["identity"],
        ["mlops"] = ["machine learning"], ["oauth"] = ["authentication"],
    };

    private static readonly (Regex Title, string Role)[] RolePatterns =
    [
        (MlEngineer(), "ml engineer"), (AiEngineer(), "ai engineer"), (DataEngineer(), "data engineer"), (DataScientist(), "data scientist"),
        (DataAnalyst(), "data analyst"), (DevOps(), "devops engineer"), (Security(), "security engineer"), (Network(), "network engineer"),
        (Dba(), "database administrator"), (Architect(), "architect"), (CloudEngineer(), "cloud engineer"), (Administrator(), "administrator"),
        (Developer(), "developer"), (Student(), "student"),
    ];

    private static readonly Lazy<IReadOnlyList<string>> AllSkills = new(() =>
        CertificationCatalogue.Default.Entries.SelectMany(e => e.Skills).Concat(ExtraSkills).Distinct(StringComparer.OrdinalIgnoreCase).ToList());

    private static readonly Lazy<Regex> SkillPattern = new(() =>
    {
        // Longest first, so "sql server" wins over "sql" at the same place.
        var terms = AllSkills.Value.Concat(Aliases.Keys).OrderByDescending(t => t.Length).Select(Regex.Escape);
        return new Regex($@"(?<![\p{{L}}\p{{N}}])(?:{string.Join('|', terms)})(?![\p{{L}}\p{{N}}#+])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    });

    /// <summary>The roles catalogue entries are tagged with.</summary>
    public static IReadOnlyList<string> Roles { get; } =
    [
        "developer", "cloud engineer", "administrator", "architect", "data engineer", "data analyst", "data scientist", "ml engineer",
        "ai engineer", "devops engineer", "security engineer", "network engineer", "database administrator", "business user", "student",
    ];

    /// <summary>The canonical name for a skill as typed: lower case, with known other spellings resolved.</summary>
    public static string Canonical(string skill)
    {
        var trimmed = string.Join(' ', skill.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        return Aliases.GetValueOrDefault(trimmed, trimmed);
    }

    /// <summary>The skills plus everything they imply, canonical.</summary>
    public static HashSet<string> Expand(IEnumerable<string> skills)
    {
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(skills.Select(Canonical));
        while (queue.TryDequeue(out var skill))
        {
            if (all.Add(skill) && Implies.TryGetValue(skill, out var implied))
            {
                foreach (var next in implied)
                {
                    queue.Enqueue(next);
                }
            }
        }

        return all;
    }

    /// <summary>Known skills mentioned in free text, canonical, most mentioned first.</summary>
    public static IReadOnlyList<string> FindSkills(string text) =>
        SkillPattern.Value.Matches(text)
            .Select(m => Canonical(m.Value))
            .CountBy(s => s, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(p => p.Value)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Key)
            .ToList();

    /// <summary>The roles the job titles in free text map to, most mentioned first.</summary>
    public static IReadOnlyList<string> FindRoles(string text) =>
        RolePatterns
            .Select(p => (p.Role, Count: p.Title.Count(text)))
            .Where(p => p.Count > 0)
            .OrderByDescending(p => p.Count)
            .Select(p => p.Role)
            .ToList();

    [GeneratedRegex(@"\b(machine learning|ml) engineer", RegexOptions.IgnoreCase)]
    private static partial Regex MlEngineer();

    [GeneratedRegex(@"\b(ai|artificial intelligence|genai|llm) (engineer|developer)", RegexOptions.IgnoreCase)]
    private static partial Regex AiEngineer();

    [GeneratedRegex(@"\b(data|analytics|etl) engineer", RegexOptions.IgnoreCase)]
    private static partial Regex DataEngineer();

    [GeneratedRegex(@"\bdata scientist", RegexOptions.IgnoreCase)]
    private static partial Regex DataScientist();

    [GeneratedRegex(@"\b(data|bi|business intelligence|reporting) analyst", RegexOptions.IgnoreCase)]
    private static partial Regex DataAnalyst();

    [GeneratedRegex(@"\b(devops|site reliability|platform|release) engineer|\bsre\b", RegexOptions.IgnoreCase)]
    private static partial Regex DevOps();

    [GeneratedRegex(@"\b(security|soc|cybersecurity|cyber security) (engineer|analyst|specialist)", RegexOptions.IgnoreCase)]
    private static partial Regex Security();

    [GeneratedRegex(@"\bnetwork (engineer|administrator)", RegexOptions.IgnoreCase)]
    private static partial Regex Network();

    [GeneratedRegex(@"\bdatabase administrator|\bdba\b", RegexOptions.IgnoreCase)]
    private static partial Regex Dba();

    [GeneratedRegex(@"\barchitect\b", RegexOptions.IgnoreCase)]
    private static partial Regex Architect();

    [GeneratedRegex(@"\b(cloud|infrastructure) engineer", RegexOptions.IgnoreCase)]
    private static partial Regex CloudEngineer();

    [GeneratedRegex(@"\b(system|systems|it|cloud|azure|windows|linux) administrator|\bsysadmin\b", RegexOptions.IgnoreCase)]
    private static partial Regex Administrator();

    [GeneratedRegex(@"\b(software|back[- ]?end|front[- ]?end|full[- ]?stack|web|application|\.net|java|python) (engineer|developer)|\bdeveloper\b|\bprogrammer\b", RegexOptions.IgnoreCase)]
    private static partial Regex Developer();

    [GeneratedRegex(@"\bstudent\b|\bintern(ship)?\b|\bgraduate\b", RegexOptions.IgnoreCase)]
    private static partial Regex Student();
}

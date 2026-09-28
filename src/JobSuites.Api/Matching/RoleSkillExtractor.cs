using System.Text.RegularExpressions;
using JobSuites.Api.Models;

namespace JobSuites.Api.Matching;

/// <summary>
/// Pulls the required skills out of a job description.
///
/// A JD is prose, so this reads it conservatively: it only claims a skill when the
/// text says the role uses it. Phrases like "familiarity with" and "working
/// knowledge of" are treated as requirements too, because employers list them for
/// exactly that purpose, and missing them would understate every technical role.
/// </summary>
public static class RoleSkillExtractor
{
    private static readonly string[] Catalog = BuildCatalog();

    public static List<string> Extract(Role role)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var text = string.Join('\n',
            role.Description,
            role.Title,
            role.Qualification,
            role.Field);

        foreach (var skill in Catalog)
        {
            // Word boundaries, plus the variants that appear constantly in JDs:
            // "C#" written as "C#", "JS" for JavaScript, ".NET" for "DOT NET".
            foreach (var variant in Variants(skill))
            {
                var pattern = $@"(?<![\w+#]){Regex.Escape(variant)}(?![\w+#])";
                if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                {
                    found.Add(skill);
                    break;
                }
            }
        }

        // "5+ years of Python" style phrasing sometimes uses a bare technology
        // name we do not have in the catalog, so also pick up capitalised or
        // known-format tokens that follow a requirement cue.
        foreach (Match m in Regex.Matches(
            text,
            @"(?:experience|knowledge|proficiency|skilled|skills|familiarity)\s+(?:in|with|of)?\s*([A-Za-z][A-Za-z0-9+#.\-]{1,24})",
            RegexOptions.IgnoreCase))
        {
            var candidate = m.Groups[1].Value.Trim(' ', '.', ',');
            if (candidate.Length is < 2 or > 24) continue;
            if (StopWords.Contains(candidate.ToLowerInvariant())) continue;
            if (!Regex.IsMatch(candidate, @"^[A-Z]")) continue;

            found.Add(candidate);
        }

        return found.ToList();
    }

    private static IEnumerable<string> Variants(string skill) => skill switch
    {
        "C#" => ["C#", "C Sharp", "Csharp"],
        "C++" => ["C++"],
        ".NET" => [".NET", "ASP.NET", "DOT NET", "DotNet"],
        "Node.js" => ["Node.js", "NodeJS", "Node JS"],
        "JavaScript" => ["JavaScript", "ECMAScript"],
        "TypeScript" => ["TypeScript"],
        "PostgreSQL" => ["PostgreSQL", "Postgres"],
        "Amazon Web Services" => ["Amazon Web Services", "AWS"],
        "Microsoft Excel" => ["Microsoft Excel", "MS Excel", "Excel"],
        "Microsoft Word" => ["Microsoft Word", "MS Word", "Word"],
        "Microsoft PowerPoint" => ["Microsoft PowerPoint", "MS PowerPoint", "PowerPoint"],
        "Human Resources" => ["Human Resources", "HR", "HRM", "Personnel Management"],
        "Communication" => ["Communication", "Written Communication", "Verbal Communication"],
        "Leadership" => ["Leadership", "Team Leadership", "Team Lead"],
        "Oracle Database" => ["Oracle Database", "Oracle DB", "Oracle"],
        "SQL Server" => ["SQL Server", "MS SQL", "T-SQL"],
        "SAP" => ["SAP", "SAP ERP", "SAP B1", "SAP Business One"],
        "R Programming" => ["R Programming", "R Language", "RStats"],
        "Vue.js" => ["Vue.js", "VueJS", "Vue"],
        "React" => ["React", "React.js", "ReactJS"],
        "React Native" => ["React Native"],
        "CI/CD" => ["CI/CD", "Continuous Integration", "Continuous Delivery"],
        "Power BI" => ["Power BI", "PowerBI"],
        "User Experience" => ["User Experience", "UX", "UI/UX"],
        _ => [skill],
    };

    private static readonly HashSet<string> StopWords =
    [
        "the", "this", "that", "these", "those", "a", "an", "and", "or", "our", "your",
        "their", "its", "any", "all", "some", "each", "other", "others", "such", "well",
        "strong", "good", "excellent", "ability", "ability", "experience", "knowledge",
        "skills", "skill", "work", "working", "role", "job", "team", "teams", "company",
        "candidate", "candidates", "applicant", "applicants", "position", "opportunity",
        "environment", "business", "industry", "sector", "organisation", "organization",
        "plus", "minimum", "years", "year", "least", "least", "must", "should", "will",
        "have", "has", "having", "using", "use", "used", "new", "existing", "related",
    ];

    private static string[] BuildCatalog() =>
    [
        ".NET", "ABAP", "ACCOUNTING", "Adobe Photoshop", "Aerospace Engineering",
        "Agile", "Android", "Angular", "Ansible", "Apache", "Artificial Intelligence",
        "Audit", "AWS", "Azure", "Bash", "Business Analysis", "Business Intelligence",
        "C#", "C++", "CAML", "Change Management", "Cisco", "Citrix", "Cloud Computing",
        "COBOL", "Communication", "CompTIA", "Configuration Management", "C#",
        "Customer Service", "Cyber Security", "Data Analysis", "Data Entry", "Data Mining",
        "Data Modeling", "Data Warehousing", "Database Administration", "DevOps",
        "Digital Marketing", "Docker", "Electrical Engineering", "Elasticsearch",
        "Email Marketing", "ERP", "ETL", "Excel", "Field Service", "Firewall",
        "Financial Accounting", "Financial Reporting", "Flutter", "Git",
        "Google Analytics", "Google Cloud", "Graphic Design", "HTML", "Human Resources",
        "IBM Cognos", "Incident Management", "Information Security", "iOS", "ITIL",
        "Java", "JavaScript", "Jenkins", "Kafka", "Kubernetes", "Laravel", "Leadership",
        "Linux", "Machine Learning", "Maintenance", "Management", "Manufacturing",
        "MariaDB", "Mechanical Engineering", "Microsoft Excel", "Microsoft Office",
        "Microsoft Outlook", "Microsoft PowerPoint", "Microsoft Word", "MongoDB", "MySQL",
        "Network Administration", "Networking", "Node.js", "NOSQL", "Office 365", "Oracle",
        "Oracle Database", "PHP", "PostgreSQL", "Power BI", "PowerPoint", "Process Improvement",
        "Procurement", "Project Management", "Python", "Qlik", "R Programming", "React",
        "React Native", "Redis", "Risk Management", "SAP", "Salesforce", "SAS", "SPSS",
        "SQL", "SQL Server", "Statistics", "Supply Chain", "Tableau", "TCP/IP",
        "Technical Support", "TensorFlow", "Troubleshooting", "TypeScript", "Unix",
        "User Experience", "User Support", "VBA", "VMware", "Vue.js", "Web Design",
        "Web Development", "Windows Server", "Word", "WordPress", "Xero", "YAML",
    ];
}

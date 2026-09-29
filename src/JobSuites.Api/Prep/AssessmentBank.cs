using JobSuites.Api.Models;

namespace JobSuites.Api.Prep;

/// <summary>
/// The aptitude item bank (docs/PRODUCT.md §7.2).
///
/// These are written out rather than generated. A scored assessment has to be
/// reproducible: the same item must carry the same answer key every time it is
/// served, or a candidate's score moves without their ability moving, and the
/// verdict machinery that matching relies on stops meaning anything. A seeded
/// generator would look more impressive and would break that guarantee across
/// runtime versions.
///
/// Domain mix follows the observed source corpus, which is heavily banking and
/// finance, and Nigerian banks routinely screen with numerical and verbal
/// assessment before interview — so numerical and verbal carry the weight.
///
/// Every item is deterministic and self-contained. Worked steps ship with the
/// item so a wrong answer still teaches something; that was the point of
/// including a bank rather than a scoring-only service.
/// </summary>
public static class AssessmentBank
{
    public static IReadOnlyList<AssessmentItem> Items { get; } = Build();

    private static IReadOnlyList<AssessmentItem> Build() => new List<AssessmentItem>
    {
        // ---- Numerical ------------------------------------------------------
        Item("numerical", 1,
            "A settlement service processes 4,800 transactions per hour. How many would it " +
            "process in 6 hours at a steady rate?",
            ["9,600", "24,000", "28,800", "32,400"],
            answerKey: 2,
            worked: "4,800 x 6 = 28,800. Doubling the rate or the time is the usual trap here."),

        Item("numerical", 1,
            "A department's budget rose from ₦2.5m to ₦3.0m. What is the percentage increase?",
            ["16.7%", "20%", "25%", "50%"],
            answerKey: 1,
            worked: "Increase = 0.5m. 0.5 / 2.5 = 0.20, so 20%. Note 3.0/2.5 = 1.2 is the " +
                    "answer expressed as a ratio — 120%, not 20%."),

        Item("numerical", 2,
            "A team of 9 shares an equal bonus of ₦45,000. How much does each person receive?",
            ["₦4,500", "₦5,000", "₦5,500", "₦9,000"],
            answerKey: 1,
            worked: "45,000 / 9 = 5,000. ₦9,000 is the trap: dividing instead of sharing."),

        Item("numerical", 2,
            "A reconciliation control fails once in every 200 runs. Across 6,000 runs, how " +
            "many failures would you expect?",
            ["12", "24", "30", "60"],
            answerKey: 2,
            worked: "6,000 / 200 = 30. The rate is already per 200 runs, so no further division " +
                    "is needed."),

        // ---- Verbal ---------------------------------------------------------
        Item("verbal", 1,
            "All auditors at the firm are Chartered Accountants. Ngozi is an auditor at the " +
            "firm. Which conclusion must be true?",
            ["Ngozi is a Chartered Accountant",
             "Ngozi manages the audit team",
             "Every Chartered Accountant is an auditor",
             "Nothing can be concluded"],
            answerKey: 0,
            worked: "This is a valid syllogism: all A are B, Ngozi is A, therefore Ngozi is B. " +
                    "Option C reverses the premise and is not supported."),

        Item("verbal", 1,
            "The expenses policy applies to all staff on probation. Tobi is not on probation. " +
            "Which conclusion must be true?",
            ["Tobi is not an auditor",
             "The expenses policy does not apply to Tobi",
             "Tobi is exempt from all policies",
             "Nothing can be concluded"],
            answerKey: 1,
            worked: "The rule covers only probationary staff, and Tobi is excluded, so the " +
                    "policy does not reach him. Option C overreaches: he is outside one " +
                    "policy, not all of them."),

        Item("verbal", 2,
            "Migrating the ledger to the new platform reduces downtime. The ledger was not " +
            "migrated. Which conclusion must be true?",
            ["Downtime was not reduced by the migration",
             "The migration will reduce downtime next year",
             "The old platform is superior",
             "Nothing can be concluded"],
            answerKey: 0,
            worked: "The reduction followed the migration, which did not happen. Option D is " +
                    "the common mistake: a conditional guarantees its consequent when the " +
                    "condition is false."),

        Item("verbal", 2,
            "Some reconciliations are automated. No reconciliation in last month's batch was " +
            "automated. Which conclusion must be true?",
            ["Last month's batch was empty",
             "No reconciliations are ever automated",
             "Last month's batch was not empty",
             "Nothing can be concluded"],
            answerKey: 2,
            worked: "Because some reconciliations exist, a batch containing none cannot be " +
                    "empty. If it were empty it would contain no reconciliations at all, and " +
                    "the premise says some exist."),

        // ---- Analytical -----------------------------------------------------
        Item("analytical", 1,
            "Roles posted this week by state: Lagos 18, Abuja 9, Port Harcourt 6, Kano 3. " +
            "Which statement is true?",
            ["Lagos has at least twice as many roles as Abuja",
             "Abuja and Port Harcourt together exceed Lagos",
             "Kano has more roles than Abuja",
             "Lagos and Abuja together equal the other states"],
            answerKey: 0,
            worked: "Lagos 18 is exactly double Abuja 9, so 'at least twice' holds. The other " +
                    "options: 9 + 6 = 15, short of 18; 3 is fewer than 9; and 18 + 9 = 27 " +
                    "against 6 + 3 = 9."),

        Item("analytical", 1,
            "Two teams each ship 3 releases a week for 4 weeks. How many releases is that " +
            "in total?",
            ["12", "18", "24", "36"],
            answerKey: 2,
            worked: "3 per week x 4 weeks = 12 per team, and 12 x 2 teams = 24. 36 is the " +
                    "trap: multiplying the three numbers without counting the second team."),

        Item("analytical", 2,
            "A candidate lists 14 skills on their CV. A posting names 9 of them. How many " +
            "of the candidate's skills does the posting not mention?",
            ["5", "9", "14", "23"],
            answerKey: 0,
            worked: "14 - 9 = 5. Those 5 are the surplus to this particular posting, and are " +
                    "what a tailored CV should leave out."),

        Item("analytical", 2,
            "One role requires 5+ years of experience and a second requires 9+. A candidate " +
            "has 8 years. How many of the two roles ask for more than the candidate has?",
            ["0", "1", "2", "3"],
            answerKey: 1,
            worked: "8 clears the 5+ requirement, so the first role does not ask for more than " +
                    "they have. 8 is one short of 9+, so only the second does. Answer: 1."),

        // ---- Abstract -------------------------------------------------------
        Item("abstract", 1, "What number comes next: 2, 4, 8, 16, ?", ["24", "30", "32", "64"],
            answerKey: 2, worked: "Each term doubles: 2, 4, 8, 16, 32."),

        Item("abstract", 2, "What number comes next: 3, 6, 11, 18, ?", ["25", "26", "27", "29"],
            answerKey: 2,
            worked: "Differences are 3, 5, 7, so the next difference is 9: 18 + 9 = 27."),

        Item("abstract", 1, "What number comes next: 100, 81, 64, 49, ?", ["36", "42", "48", "25"],
            answerKey: 0, worked: "These are squares: 10², 9², 8², 7², so next is 6² = 36."),

        Item("abstract", 3, "What number comes next: 2, 6, 12, 20, ?", ["26", "28", "30", "32"],
            answerKey: 2,
            worked: "Differences are 4, 6, 8, so the next is 10: 20 + 10 = 30."),
    };

    private static AssessmentItem Item(
        string domain, int difficulty, string prompt, string[] options, int answerKey, string worked) => new()
    {
        Domain = domain,
        Difficulty = difficulty,
        Prompt = prompt,
        Options = options.ToList(),
        AnswerKey = answerKey,
        WorkedSteps = worked,
    };
}

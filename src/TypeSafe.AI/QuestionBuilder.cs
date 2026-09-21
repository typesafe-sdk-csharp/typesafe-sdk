using System.Collections.ObjectModel;

namespace TypeSafe.AI;

/// <summary>Entry point for fluently building the system-one questions map.</summary>
/// <example>
/// <code>
/// Questions.Build(q => q
///     .Noul("billing", "Is this ticket about billing?")
///     .Choice("tone", "What is the customer's tone?", "calm", "frustrated", "angry")
///     .Score("urgency", "How urgent is this ticket?", "can wait", "this week", "today"))
/// </code>
/// </example>
public static class Questions
{
    /// <summary>Builds the questions map through a chained builder lambda.</summary>
    /// <param name="build">The builder delegate that configures questions.</param>
    /// <returns>An immutable dictionary of validated questions keyed by question id.</returns>
    public static IReadOnlyDictionary<string, TypeSafeQuestion> Build(
        Func<QuestionBuilder, QuestionBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        return new QuestionBuilder().BuildCore(build);
    }
}

/// <summary>Accumulates questions by id; every method returns the builder for chaining.</summary>
/// <remarks>Constructed only through <see cref="Questions.Build"/>. Duplicate and blank ids are
/// rejected eagerly with the id named; structural counts stay validated in <see cref="SystemOneRequest.Create"/>.</remarks>
public sealed class QuestionBuilder
{
    private readonly Dictionary<string, TypeSafeQuestion> _questions = [];

    internal QuestionBuilder()
    {
    }

    /// <summary>Adds a yes/no question. Optional outcome descriptions map to the documented true/false criteria.</summary>
    /// <param name="id">The unique identifier for the question.</param>
    /// <param name="instructions">The text instructions for what to evaluate.</param>
    /// <param name="whenTrue">Optional criteria for an affirmative (true) outcome.</param>
    /// <param name="whenFalse">Optional criteria for a negative (false) outcome.</param>
    /// <returns>This builder instance for chaining.</returns>
    public QuestionBuilder Noul(string id, string instructions,
        string? whenTrue = null, string? whenFalse = null)
    {
        NoulCriteria? criteria = whenTrue is null && whenFalse is null
            ? null
            : new NoulCriteria { WhenTrue = ToContent(whenTrue), WhenFalse = ToContent(whenFalse) };
        return Add(id, new Noul { Instructions = TypeSafeContent.FromString(instructions), Criteria = criteria });
    }

    /// <summary>Adds a yes/no question with structured instructions and optional structured criteria.</summary>
    /// <param name="id">The unique identifier for the question.</param>
    /// <param name="instructions">The structured instructions for what to evaluate.</param>
    /// <param name="whenTrue">Optional structured criteria for an affirmative (true) outcome.</param>
    /// <param name="whenFalse">Optional structured criteria for a negative (false) outcome.</param>
    /// <returns>This builder instance for chaining.</returns>
    public QuestionBuilder Noul(string id, TypeSafeContent instructions,
        TypeSafeContent? whenTrue = null, TypeSafeContent? whenFalse = null)
    {
        NoulCriteria? criteria = whenTrue is null && whenFalse is null
            ? null
            : new NoulCriteria { WhenTrue = whenTrue, WhenFalse = whenFalse };
        return Add(id, new Noul { Instructions = instructions, Criteria = criteria });
    }

    /// <summary>Adds a yes/no question with pre-built criteria.</summary>
    /// <param name="id">The unique identifier for the question.</param>
    /// <param name="instructions">The structured instructions for what to evaluate.</param>
    /// <param name="criteria">The pre-built criteria object.</param>
    /// <returns>This builder instance for chaining.</returns>
    public QuestionBuilder Noul(string id, TypeSafeContent instructions, NoulCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        return Add(id, new Noul { Instructions = instructions, Criteria = criteria });
    }

    /// <summary>Adds a choice question with undescribed labels.</summary>
    /// <param name="id">The unique identifier for the question.</param>
    /// <param name="instructions">The text instructions for what to evaluate.</param>
    /// <param name="labels">The option labels to choose among.</param>
    /// <returns>This builder instance for chaining.</returns>
    public QuestionBuilder Choice(string id, string instructions, params string[] labels)
        => Choice(id, TypeSafeContent.FromString(instructions), labels);

    /// <summary>Adds a choice question with structured instructions and undescribed labels.</summary>
    /// <param name="id">The unique identifier for the question.</param>
    /// <param name="instructions">The structured instructions for what to evaluate.</param>
    /// <param name="labels">The option labels to choose among.</param>
    /// <returns>This builder instance for chaining.</returns>
    public QuestionBuilder Choice(string id, TypeSafeContent instructions, params string[] labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var criteria = new Dictionary<string, TypeSafeContent?>(labels.Length);
        foreach (var label in labels)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
            if (!criteria.TryAdd(label, null))
                throw new ArgumentException($"An option with the label '{label}' was added more than once.", nameof(labels));
        }
        return Add(id, new Choice { Instructions = instructions, Criteria = criteria });
    }

    /// <summary>Adds a choice question whose options are described through a sub-builder lambda.</summary>
    /// <param name="id">The unique identifier for the question.</param>
    /// <param name="instructions">The text instructions for what to evaluate.</param>
    /// <param name="options">The sub-builder delegate configuring choice options and descriptions.</param>
    /// <returns>This builder instance for chaining.</returns>
    public QuestionBuilder Choice(string id, string instructions, Func<ChoiceOptions, ChoiceOptions> options)
        => Choice(id, TypeSafeContent.FromString(instructions), options);

    /// <summary>Adds a choice question with structured instructions whose options are described through a sub-builder lambda.</summary>
    /// <param name="id">The unique identifier for the question.</param>
    /// <param name="instructions">The structured instructions for what to evaluate.</param>
    /// <param name="options">The sub-builder delegate configuring choice options and descriptions.</param>
    /// <returns>This builder instance for chaining.</returns>
    public QuestionBuilder Choice(string id, TypeSafeContent instructions, Func<ChoiceOptions, ChoiceOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var choiceOptions = options(new ChoiceOptions())
            ?? throw new ArgumentException("The options lambda must return the options builder.", nameof(options));
        return Add(id, new Choice { Instructions = instructions, Criteria = choiceOptions.ToCriteria() });
    }

    /// <summary>Adds a rubric question with text levels.</summary>
    /// <param name="id">The unique identifier for the question.</param>
    /// <param name="instructions">The text instructions for what to evaluate.</param>
    /// <param name="levels">The text labels for each rubric level.</param>
    /// <returns>This builder instance for chaining.</returns>
    public QuestionBuilder Score(string id, string instructions, params string[] levels)
        => Score(id, TypeSafeContent.FromString(instructions), levels);

    /// <summary>Adds a rubric question with structured instructions and text levels.</summary>
    /// <param name="id">The unique identifier for the question.</param>
    /// <param name="instructions">The structured instructions for what to evaluate.</param>
    /// <param name="levels">The text labels for each rubric level.</param>
    /// <returns>This builder instance for chaining.</returns>
    public QuestionBuilder Score(string id, TypeSafeContent instructions, params string[] levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        var criteria = new List<TypeSafeContent>(levels.Length);
        foreach (var level in levels)
        {
            ArgumentNullException.ThrowIfNull(level);
            criteria.Add(level);
        }
        return Add(id, new Score { Instructions = instructions, Criteria = criteria });
    }

    /// <summary>Adds a rubric question with structured instructions and structured levels.</summary>
    /// <param name="id">The unique identifier for the question.</param>
    /// <param name="instructions">The structured instructions for what to evaluate.</param>
    /// <param name="levels">The structured content for each rubric level.</param>
    /// <returns>This builder instance for chaining.</returns>
    public QuestionBuilder Score(string id, TypeSafeContent instructions, params TypeSafeContent[] levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        var criteria = new List<TypeSafeContent>(levels.Length);
        foreach (var level in levels)
        {
            ArgumentNullException.ThrowIfNull(level);
            criteria.Add(level);
        }
        return Add(id, new Score { Instructions = instructions, Criteria = criteria });
    }

    /// <summary>Adds a rubric question whose levels are built through a sub-builder lambda.</summary>
    /// <param name="id">The unique identifier for the question.</param>
    /// <param name="instructions">The text instructions for what to evaluate.</param>
    /// <param name="levels">The sub-builder delegate configuring rubric levels.</param>
    /// <returns>This builder instance for chaining.</returns>
    public QuestionBuilder Score(string id, string instructions, Func<ScoreLevels, ScoreLevels> levels)
        => Score(id, TypeSafeContent.FromString(instructions), levels);

    /// <summary>Adds a rubric question with structured instructions whose levels are built through a sub-builder lambda.</summary>
    /// <param name="id">The unique identifier for the question.</param>
    /// <param name="instructions">The structured instructions for what to evaluate.</param>
    /// <param name="levels">The sub-builder delegate configuring rubric levels.</param>
    /// <returns>This builder instance for chaining.</returns>
    public QuestionBuilder Score(string id, TypeSafeContent instructions, Func<ScoreLevels, ScoreLevels> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        var scoreLevels = levels(new ScoreLevels())
            ?? throw new ArgumentException("The levels lambda must return the levels builder.", nameof(levels));
        return Add(id, new Score { Instructions = instructions, Criteria = scoreLevels.ToCriteria() });
    }

    private QuestionBuilder Add(string id, TypeSafeQuestion question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!_questions.TryAdd(id, question))
            throw new ArgumentException($"A question with the id '{id}' has already been added.", nameof(id));
        return this;
    }

    private static TypeSafeContent? ToContent(string? text) => text is null ? null : TypeSafeContent.FromString(text);

    internal IReadOnlyDictionary<string, TypeSafeQuestion> BuildCore(Func<QuestionBuilder, QuestionBuilder> build)
    {
        if (build(this) is null)
            throw new ArgumentException("The build lambda must return the builder.", nameof(build));
        if (_questions.Count == 0)
            throw new ArgumentException("At least one question is required.", nameof(build));
        return SystemOneRequest.SnapshotQuestions(_questions);
    }
}

/// <summary>Accumulates choice options; undescribed options map to the documented null criteria values.</summary>
public sealed class ChoiceOptions
{
    private readonly Dictionary<string, TypeSafeContent?> _options = [];

    internal ChoiceOptions()
    {
    }

    /// <summary>Adds an option; a null description leaves the label undescribed.</summary>
    /// <param name="label">The option label name.</param>
    /// <param name="description">Optional description explaining the option.</param>
    /// <returns>This options builder instance for chaining.</returns>
    public ChoiceOptions Option(string label, TypeSafeContent? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (!_options.TryAdd(label, description))
            throw new ArgumentException($"An option with the label '{label}' was added more than once.", nameof(label));
        return this;
    }

    internal IReadOnlyDictionary<string, TypeSafeContent?> ToCriteria()
        => new ReadOnlyDictionary<string, TypeSafeContent?>(new Dictionary<string, TypeSafeContent?>(_options));
}

/// <summary>Accumulates ordered score rubric levels.</summary>
public sealed class ScoreLevels
{
    private readonly List<TypeSafeContent> _levels = [];

    internal ScoreLevels()
    {
    }

    /// <summary>Adds one rubric level; plain strings convert implicitly.</summary>
    /// <param name="level">The rubric level content.</param>
    /// <returns>This levels builder instance for chaining.</returns>
    public ScoreLevels Level(TypeSafeContent level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _levels.Add(level);
        return this;
    }

    internal IReadOnlyList<TypeSafeContent> ToCriteria() => Array.AsReadOnly(_levels.ToArray());
}

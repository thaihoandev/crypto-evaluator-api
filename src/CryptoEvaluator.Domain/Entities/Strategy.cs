namespace CryptoEvaluator.Domain.Entities;

public class Strategy
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private readonly List<StrategyRule> _rules = new();
    public IReadOnlyCollection<StrategyRule> Rules => _rules.AsReadOnly();

    private Strategy() { }

    public static Strategy Create(Guid userId, string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Strategy name cannot be empty.", nameof(name));

        return new Strategy
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name.Trim(),
            Description = description,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void AddRule(string indicator, string op, decimal value, int weight)
    {
        _rules.Add(StrategyRule.Create(Id, indicator, op, value, weight));
    }
}

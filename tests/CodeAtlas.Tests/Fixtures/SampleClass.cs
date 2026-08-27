namespace Fixtures.Sample;

/// <summary>A sample person entity for parser testing.</summary>
public class PersonService
{
    public string Name { get; set; } = string.Empty;

    public string GetGreeting(string prefix) => $"{prefix} {Name}";

    private void PrivateHelper() { }
}

public interface IPersonRepository
{
    PersonService? GetById(int id);
}

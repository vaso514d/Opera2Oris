namespace Opera2Oris.Entities;

public sealed record BofColumnDefinition(int Ordinal, string Name, string Key)
{
    public override string ToString() => $"{Ordinal:000} {Name}";
}

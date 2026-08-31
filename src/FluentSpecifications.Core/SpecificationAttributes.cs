namespace FluentSpecifications;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SpecificationSetAttribute<T> : Attribute
{
    public bool GenerateSearch { get; init; }
}

[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false,
    Inherited = false)]
public sealed class ExposeAttribute : Attribute;

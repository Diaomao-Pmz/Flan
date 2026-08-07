using System;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class GMCommandAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }

    public GMCommandAttribute(string name, string description = "")
    {
        Name = name;
        Description = description;
    }
}
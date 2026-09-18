using System;

namespace Cutie
{
    internal enum ToolKind { Read, Edit, External, Background, Control }

    // Every tool declares its execution contract. Discovery fails for unclassified tools.
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class ToolExecutionAttribute : Attribute
    {
        public ToolExecutionAttribute(ToolKind kind) { Kind = kind; }
        public ToolKind Kind { get; }
    }
}

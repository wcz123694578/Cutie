using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
namespace Cutie
{
    internal static class ToolRegistry
    {
        internal static readonly Lazy<IReadOnlyDictionary<string, ToolRegistration>> Tools =
            new Lazy<IReadOnlyDictionary<string, ToolRegistration>>(DiscoverTools);
        internal static IReadOnlyDictionary<string, ToolRegistration>
            DiscoverTools()
        {
            return typeof(McpHttpServer)
                .Assembly
                .GetTypes()
                .Where(type => HasAttribute(
                    type,
                    "McpServerToolTypeAttribute"))
                .SelectMany(type =>
                    type.GetMethods(
                        BindingFlags.Public |
                        BindingFlags.Instance |
                        BindingFlags.Static)
                    .Where(method => HasAttribute(
                        method,
                        "McpServerToolAttribute"))
                    .Select(method => new ToolRegistration(
                        ToToolName(method.Name),
                        GetToolDescription(method),
                        method.Name == "ExecuteBatch" || method.Name == "StartBatch" ? BatchSchema.Create() : BuildInputSchema(method),
                        type,
                        method)))
                .ToDictionary(
                    tool => tool.Name,
                    StringComparer.OrdinalIgnoreCase);
        }

        private static bool HasAttribute(
            MemberInfo member,
            string attributeTypeName)
        {
            return member
                .GetCustomAttributes(false)
                .Any(attribute => string.Equals(
                    attribute.GetType().Name,
                    attributeTypeName,
                    StringComparison.Ordinal));
        }

        private static string GetToolDescription(
            MethodInfo method)
        {
            return method
                .GetCustomAttributes(typeof(DescriptionAttribute), false)
                .OfType<DescriptionAttribute>()
                .Select(attribute => attribute.Description)
                .FirstOrDefault()
                ?? method.Name;
        }

        private static object BuildInputSchema(
            MethodInfo method)
        {
            var parameters = method.GetParameters();
            var properties = new Dictionary<string, object>();
            var required = new List<string>();

            foreach (var parameter in parameters)
            {
                properties[parameter.Name] = new Dictionary<string, object>
                {
                    ["type"] = MapJsonType(parameter.ParameterType)
                };

                if (!parameter.IsOptional)
                    required.Add(parameter.Name);
            }

            var schema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["additionalProperties"] = false
            };

            if (required.Count > 0)
                schema["required"] = required.ToArray();

            return schema;
        }

        private static string MapJsonType(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type == typeof(string) || type == typeof(Guid))
                return "string";

            if (type == typeof(bool))
                return "boolean";

            if (type.IsEnum)
                return "string";

            if (type == typeof(byte) ||
                type == typeof(ushort) ||
                type == typeof(uint) ||
                type == typeof(short) ||
                type == typeof(int) ||
                type == typeof(long))
            {
                return "integer";
            }

            if (type == typeof(float) ||
                type == typeof(double) ||
                type == typeof(decimal))
            {
                return "number";
            }

            return "object";
        }

        private static string ToToolName(string methodName)
        {
            var builder = new StringBuilder();

            for (var i = 0; i < methodName.Length; i++)
            {
                var character = methodName[i];

                if (char.IsUpper(character) && i > 0)
                    builder.Append('_');

                builder.Append(char.ToLowerInvariant(character));
            }

            return builder.ToString();
        }

    }
}

using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Cutie
{
    internal sealed class ToolRegistration
    {
        private readonly Type _type;
        private readonly MethodInfo _method;
        private readonly ParameterInfo[] _parameters;
        public ToolRegistration(string name, string description, object inputSchema, Type declaringType, MethodInfo method)
        {
            Name = name; Description = description; InputSchema = inputSchema;
            _type = declaringType; _method = method; _parameters = method.GetParameters();
            Kind = method.GetCustomAttribute<ToolExecutionAttribute>()?.Kind
                ?? throw new InvalidOperationException("Missing execution policy: " + name);
            if (Kind != ToolKind.Control && typeof(System.Threading.Tasks.Task).IsAssignableFrom(method.ReturnType))
                throw new InvalidOperationException("Async tools need an explicit completion adapter: " + name);
        }
        public string Name { get; }
        public string Description { get; }
        public object InputSchema { get; }
        public ToolKind Kind { get; }

        public void Validate(JObject arguments, bool allowReferences = false)
        {
            foreach (var property in (arguments ?? new JObject()).Properties())
                if (!_parameters.Any(p => p.Name == property.Name))
                    throw new ArgumentException("Unknown argument '" + property.Name + "' for " + Name);
            foreach (var parameter in _parameters)
            {
                var token = arguments?[parameter.Name];
                if (allowReferences && BatchReferences.IsReference(token)) continue;
                ConvertArgument(token, parameter);
            }
        }

        public object Invoke(JObject arguments)
        {
            Validate(arguments);
            var values = _parameters.Select(p => ConvertArgument(arguments?[p.Name], p)).ToArray();
            try { return _method.Invoke(_method.IsStatic ? null : Activator.CreateInstance(_type), values); }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
        }

        private static object ConvertArgument(JToken token, ParameterInfo parameter)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                if (parameter.IsOptional) return parameter.DefaultValue;
                throw new ArgumentException("Missing required argument '" + parameter.Name + "'.");
            }
            var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
            // Avoid JSON.NET's silent float-to-int rounding and scalar coercions.
            if (type == typeof(string) && token.Type != JTokenType.String ||
                type == typeof(bool) && token.Type != JTokenType.Boolean ||
                (type == typeof(int) || type == typeof(uint) || type == typeof(long)) && token.Type != JTokenType.Integer ||
                (type == typeof(double) || type == typeof(float)) && token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                throw new ArgumentException("Invalid JSON type for '" + parameter.Name + "'.");
            var value = token.ToObject(type, Newtonsoft.Json.JsonSerializer.Create(new Newtonsoft.Json.JsonSerializerSettings
            { MissingMemberHandling = Newtonsoft.Json.MissingMemberHandling.Error }));
            if (value is double number && (double.IsNaN(number) || double.IsInfinity(number)))
                throw new ArgumentException("Non-finite number for '" + parameter.Name + "'.");
            if (value is float single && (float.IsNaN(single) || float.IsInfinity(single)))
                throw new ArgumentException("Non-finite number for '" + parameter.Name + "'.");
            return value;
        }
    }
}

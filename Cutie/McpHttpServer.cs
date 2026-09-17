using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Cutie
{
    internal sealed class ToolRegistration
    {
        public ToolRegistration(
            string name,
            string description,
            object inputSchema,
            Type declaringType,
            MethodInfo method)
        {
            Name = name;
            Description = description;
            InputSchema = inputSchema;
            DeclaringType = declaringType;
            Method = method;
        }

        public string Name { get; }

        public string Description { get; }

        public object InputSchema { get; }

        private Type DeclaringType { get; }

        private MethodInfo Method { get; }

        public async Task<object> InvokeAsync(JObject arguments)
        {
            var parameterValues = Method
                .GetParameters()
                .Select(parameter => GetArgumentValue(
                    arguments,
                    parameter))
                .ToArray();

            var result = await VegasContext.InvokeAsync(
                () =>
                {
                    var target = Method.IsStatic
                        ? null
                        : Activator.CreateInstance(DeclaringType);
                    return Method.Invoke(target, parameterValues);
                });

            if (!(result is Task task))
                return result;

            await task.ConfigureAwait(false);

            var resultProperty =
                task.GetType().GetProperty("Result");

            return resultProperty?.GetValue(task);
        }

        private static object GetArgumentValue(
            JObject arguments,
            ParameterInfo parameter)
        {
            var token = arguments?[parameter.Name];

            if (token == null || token.Type == JTokenType.Null)
            {
                if (parameter.IsOptional)
                    return parameter.DefaultValue;

                throw new InvalidOperationException(
                    "Missing required argument '" +
                    parameter.Name +
                    "'.");
            }

            var targetType =
                Nullable.GetUnderlyingType(parameter.ParameterType)
                ?? parameter.ParameterType;

            return token.ToObject(targetType);
        }
    }

    public sealed class McpHttpServer
    {
        private readonly HttpListener _listener;
        private static readonly Lazy<IReadOnlyDictionary<string, ToolRegistration>> _toolRegistrations =
            new Lazy<IReadOnlyDictionary<string, ToolRegistration>>(DiscoverTools);
        private CancellationTokenSource _cts;

        public McpHttpServer(string prefix)
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
        }

        public void Start()
        {
            if (_listener.IsListening)
                return;

            _cts = new CancellationTokenSource();

            _listener.Start();

            Task.Run(() => ListenLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();

            if (_listener.IsListening)
                _listener.Stop();
        }

        private async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext context;

                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch
                {
                    if (token.IsCancellationRequested)
                        break;

                    continue;
                }

                _ = Task.Run(() => HandleRequest(context));
            }
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            try
            {
                if (context.Request.HttpMethod != "POST")
                {
                    context.Response.StatusCode = 405;
                    context.Response.Close();
                    return;
                }

                string body;

                using (var reader = new StreamReader(
                    context.Request.InputStream,
                    context.Request.ContentEncoding))
                {
                    body = await reader.ReadToEndAsync();
                }

                var request =
                    JsonConvert.DeserializeObject<McpRequest>(body);

                if (request?.id == null &&
                    !string.IsNullOrEmpty(request?.method))
                {
                    context.Response.StatusCode = 202;
                    context.Response.Close();
                    return;
                }

                var response = await HandleMcpRequest(request);

                var json =
                    JsonConvert.SerializeObject(response);

                var bytes =
                    Encoding.UTF8.GetBytes(json);

                context.Response.StatusCode = 200;
                context.Response.ContentType =
                    "application/json";

                context.Response.ContentEncoding =
                    Encoding.UTF8;

                context.Response.ContentLength64 =
                    bytes.Length;

                await context.Response.OutputStream.WriteAsync(
                    bytes,
                    0,
                    bytes.Length);

                context.Response.Close();
            }
            catch (Exception ex)
            {
                try
                {
                    context.Response.StatusCode = 500;

                    var bytes = Encoding.UTF8.GetBytes(
                        ex.ToString());

                    await context.Response.OutputStream.WriteAsync(
                        bytes,
                        0,
                        bytes.Length);

                    context.Response.Close();
                }
                catch
                {
                }
            }
        }

        private Task<McpResponse> HandleMcpRequest(
            McpRequest request)
        {
            switch (request.method)
            {
                case "initialize":
                    return Task.FromResult(
                        HandleInitialize(request));

                case "tools/list":
                    return Task.FromResult(
                        HandleToolsList(request));

                case "tools/call":
                    return HandleToolsCall(request);

                default:
                    return Task.FromResult(new McpResponse
                    {
                        id = request.id,
                        error = new McpError
                        {
                            code = -32601,
                            message = "Method not found"
                        }
                    });
            }
        }

        private McpResponse HandleInitialize(
            McpRequest request)
        {
            return new McpResponse
            {
                id = request.id,

                result = new
                {
                    protocolVersion = "2025-11-25",

                    capabilities = new
                    {
                        tools = new
                        {
                            listChanged = false
                        }
                    },

                    serverInfo = new
                    {
                        name = "VegasMcp",
                        version = "0.1.0"
                    }
                }
            };
        }

        private McpResponse HandleToolsList(
            McpRequest request)
        {
            return new McpResponse
            {
                id = request.id,

                result = new
                {
                    tools = _toolRegistrations.Value.Values
                        .Select(tool => new
                        {
                            name = tool.Name,
                            description = tool.Description,
                            inputSchema = tool.InputSchema
                        })
                        .ToArray()
                }
            };
        }

        private async Task<McpResponse> HandleToolsCall(
            McpRequest request)
        {
            var name =
                request.@params?["name"]?.ToString();

            if (!string.IsNullOrWhiteSpace(name) &&
                _toolRegistrations.Value.TryGetValue(name, out var tool))
            {
                object result;
                try
                {
                    result = await tool.InvokeAsync(GetToolArguments(request));
                }
                catch (Exception ex)
                {
                    var cause = ex is TargetInvocationException invocation && invocation.InnerException != null
                        ? invocation.InnerException
                        : ex;
                    return new McpResponse
                    {
                        id = request.id,
                        result = new
                        {
                            content = new[] { new { type = "text", text = cause.Message } },
                            isError = true
                        }
                    };
                }

                return new McpResponse
                {
                    id = request.id,

                    result = new
                    {
                        content = new object[]
                        {
                        new
                        {
                            type = "text",
                            text = JsonConvert.SerializeObject(
                                result,
                                Formatting.Indented)
                        }
                        },

                        isError = false
                    }
                };
            }

            return new McpResponse
            {
                id = request.id,

                error = new McpError
                {
                    code = -32602,
                    message = "Unknown tool"
                }
            };
        }

        private static JObject GetToolArguments(
            McpRequest request)
        {
            var arguments =
                request.@params?["arguments"] as JObject;

            if (arguments != null)
                return arguments;

            if (request.@params == null)
                return null;

            var fallbackArguments = new JObject();

            foreach (var property in request.@params.Properties())
            {
                if (!string.Equals(
                    property.Name,
                    "name",
                    StringComparison.OrdinalIgnoreCase))
                {
                    fallbackArguments[property.Name] =
                        property.Value;
                }
            }

            return fallbackArguments.HasValues
                ? fallbackArguments
                : null;
        }

        private static IReadOnlyDictionary<string, ToolRegistration>
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
                        BuildInputSchema(method),
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
                ["properties"] = properties
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

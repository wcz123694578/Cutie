using Cutie;
using Cutie.Models;
using Cutie.Tools;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

internal static class Program
{
    private static int _passed;
    private static readonly IReadOnlyDictionary<string, ToolRegistration> Tools = typeof(FakeTools).GetMethods()
        .Where(m => m.GetCustomAttributes(typeof(ToolExecutionAttribute), false).Any())
        .ToDictionary(m => m.Name, m => new ToolRegistration(m.Name, m.Name, new { }, typeof(FakeTools), m));

    private static int Main()
    {
        try
        {
            Test("ordered references and one undo scope", () => {
                var host = new FakeHost();
                var batch = Plan("[{'id':'a','tool':'Edit','arguments':{'value':3}}, {'id':'b','tool':'Edit','arguments':{'value':{'$ref':'a#/value'}}}]", host);
                Run(batch); Equal("completed", batch.Result.Status); Equal(1, host.Opened); Equal(1, host.Closed);
                Equal("3,3", string.Join(",", FakeTools.Values));
            });
            Test("preflight rejects all work before an external step", () => {
                Throws<ArgumentException>(() => Plan("[{'id':'a','tool':'Edit','arguments':{'value':1}},{'id':'b','tool':'External'}]"));
                Equal(0, FakeTools.Values.Count);
            });
            Test("staged accepts external tools", () => {
                var plan = Plan("[{'id':'a','tool':'External'}]", mode:"staged"); Run(plan); Equal("completed", plan.Result.Status);
            });
            Test("failed edit keeps partial changes and closes scope once", () => {
                var host = new FakeHost();
                var plan = Plan("[{'id':'a','tool':'Edit','arguments':{'value':1}},{'id':'b','tool':'Fail'},{'id':'c','tool':'Edit','arguments':{'value':2}}]", host);
                Run(plan); Equal("failed", plan.Result.Status); Equal("not_executed", plan.Result.Steps[2].Status);
                Equal("InvalidOperationException", plan.Result.Steps[1].ErrorType); Equal("1,99", string.Join(",", FakeTools.Values)); Equal(1, host.Closed);
                Equal(false, plan.Result.RollbackPerformed);
            });
            Test("continue skips failed dependencies but runs independent steps", () => {
                var plan = Plan("[{'id':'a','tool':'Fail'},{'id':'b','tool':'Edit','arguments':{'value':{'$ref':'a#/value'}}},{'id':'c','tool':'Edit','arguments':{'value':7}}]", onError:"continue");
                Run(plan); Equal("skipped", plan.Result.Steps[1].Status); Equal("succeeded", plan.Result.Steps[2].Status);
            });
            Test("forward references are rejected", () => Throws<ArgumentException>(() => Plan("[{'id':'a','tool':'Edit','arguments':{'value':{'$ref':'b#/value'}}},{'id':'b','tool':'Edit','arguments':{'value':3}}]")));
            Test("duplicate IDs are rejected", () => Throws<ArgumentException>(() => Plan("[{'id':'a','tool':'External'},{'id':'a','tool':'External'}]", mode:"staged")));
            Test("unknown tool and arguments are rejected", () => {
                Throws<ArgumentException>(() => Plan("[{'id':'a','tool':'Missing'}]"));
                Throws<ArgumentException>(() => Plan("[{'id':'a','tool':'Edit','arguments':{'value':1,'typo':3}}]"));
            });
            Test("strict numeric types prevent fractional indices", () => Throws<ArgumentException>(() => Plan("[{'id':'a','tool':'Edit','arguments':{'value':1.2}}]")));
            Test("nested controls are rejected", () => Throws<ArgumentException>(() => Plan("[{'id':'a','tool':'Control'}]", mode:"staged")));
            Test("reference shapes are validated", () => Throws<ArgumentException>(() => Plan("[{'id':'a','tool':'Edit','arguments':{'value':{'$ref':'a#/value','extra':1}}}]")));
            Test("missing output path fails with a useful error", () => {
                var plan = Plan("[{'id':'a','tool':'Edit','arguments':{'value':1}},{'id':'b','tool':'Edit','arguments':{'value':{'$ref':'a#/missing'}}}]");
                Run(plan); Equal("failed", plan.Result.Steps[1].Status);
            });
            Test("cancellation before dispatch creates no undo scope", () => {
                var host = new FakeHost(); var plan = Plan("[{'id':'a','tool':'Edit','arguments':{'value':1}}]", host);
                Run(plan, new CancellationToken(true)); Equal("cancelled", plan.Result.Status); Equal(0, host.Opened); Equal(0, FakeTools.Values.Count);
            });
            Test("cancellation between operations retains one undo", () => {
                var host = new FakeHost(); var source = new CancellationTokenSource();
                var plan = Plan("[{'id':'a','tool':'Edit','arguments':{'value':1}},{'id':'b','tool':'Edit','arguments':{'value':2}}]", host);
                plan.ExecuteRange(0,2,"test",source.Token,()=>source.Cancel()); plan.Finish();
                Equal("cancelled", plan.Result.Status); Equal(1, FakeTools.Values.Count); Equal(1, host.Closed);
            });
            Test("handle resolves current index after insertion", () => {
                var host = new FakeHost();
                var plan = Plan("[{'id':'a','tool':'Edit','arguments':{'value':0},'capture':[{'name':'track','kind':'track','selector':{'trackIndex':{'$ref':'a#/value'}}}]},{'id':'b','tool':'Edit','arguments':{'value':{'$handle':'track','property':'trackIndex'}}}]", host);
                plan.ExecuteRange(0,1,"test",CancellationToken.None,null); host.Index = 4;
                plan.ExecuteRange(1,1,"test",CancellationToken.None,null); plan.Finish(); Equal(4, FakeTools.Values.Last());
            });
            Test("deleted handle fails rather than addressing replacement", () => {
                var host = new FakeHost();
                var plan = Plan("[{'id':'a','tool':'Edit','arguments':{'value':0},'capture':[{'name':'t','kind':'track','selector':{'trackIndex':0}}]},{'id':'b','tool':'Edit','arguments':{'value':{'$handle':'t','property':'trackIndex'}}}]",host);
                plan.ExecuteRange(0,1,"test",CancellationToken.None,null); host.Deleted=true;
                plan.ExecuteRange(1,1,"test",CancellationToken.None,null); plan.Finish(); Equal("failed",plan.Result.Steps[1].Status); Equal(1,FakeTools.Values.Count);
            });
            Test("read-only batches do not open edit scopes", () => {
                var host = new FakeHost(); var plan = Plan("[{'id':'a','tool':'Read'}]",host); Run(plan); Equal(0, host.Opened);
            });
            Test("1000 steps preserve order with one undo", () => {
                var ops = Enumerable.Range(0,1000).Select(i=>new BatchOperation { Id="step"+i,Tool="Edit",Arguments=new JObject { ["value"]=i } }).ToArray();
                var host = new FakeHost(); var plan=new BatchExecutor(Tools,host,ops,"large","single","stop");
                Run(plan); Equal(1000,FakeTools.Values.Count); Equal(999,FakeTools.Values.Last()); Equal(1,host.Opened);
                Throws<ArgumentException>(()=>new BatchExecutor(Tools,host,ops.Concat(new[]{ops[0]}).ToArray(),"large","single","stop"));
            });
            Test("every production tool has execution policy and batch schema", () => {
                var registry=ToolRegistry.Tools.Value;
                Equal(65,registry.Count); Equal(60,registry.Values.Count(t=>t.Kind!=ToolKind.Control));
                Equal(ToolKind.External,registry["render_project"].Kind); Equal(ToolKind.Read,registry["list_track_motion_keyframes"].Kind);
                Equal("array",(string)JObject.FromObject(registry["execute_batch"].InputSchema)["properties"]["operations"]["type"]);
            });
            Test("real batch contract rejects unknown nested properties", () => {
                Throws<Newtonsoft.Json.JsonSerializationException>(()=>ToolRegistry.Tools.Value["execute_batch"].Validate(JObject.Parse("{'operations':[{'id':'a','tool':'list_tracks','typo':true}]}")));
            });
            Test("all business tools accept staged plans", () => {
                foreach(var tool in ToolRegistry.Tools.Value.Values.Where(t=>t.Kind!=ToolKind.Control)) {
                    var schema=JObject.FromObject(tool.InputSchema); var args=new JObject();
                    foreach(var required in (JArray)schema["required"]??new JArray()) {
                        var key=(string)required; var type=(string)schema["properties"][key]["type"];
                        args[key]=type=="string" ? (JToken)new JValue("fixture") : type=="boolean" ? new JValue(false) : new JValue(0);
                    }
                    var plan=new BatchExecutor(ToolRegistry.Tools.Value,new FakeHost(),new[]{new BatchOperation{Id="probe",Tool=tool.Name,Arguments=args}},"coverage","staged","stop");
                    Equal(1,plan.Operations.Length);
                }
            });
            Test("invalid capture is rejected before any editing", () => {
                Throws<ArgumentException>(()=>Plan("[{'id':'a','tool':'Edit','arguments':{'value':1},'capture':[{'name':'bad','kind':'track','selector':{'trackIndex':-1}}]}]"));
                Throws<ArgumentException>(()=>Plan("[{'id':'a','tool':'Edit','arguments':{'value':1},'capture':[{'name':'bad','kind':'effect','selector':{'targetType':'event','trackIndex':0,'effectIndex':0}}]}]"));
                Equal(0,FakeTools.Values.Count);
            });
            Test("resolved capture values are validated", () => {
                var plan=Plan("[{'id':'a','tool':'Edit','arguments':{'value':-1},'capture':[{'name':'bad','kind':'track','selector':{'trackIndex':{'$ref':'a#/value'}}}]}]");
                Run(plan); Equal("failed",plan.Result.Status);
            });
            Test("single precision overflow is rejected", () => {
                Throws<ArgumentException>(()=>ToolRegistry.Tools.Value["set_pan_crop_keyframe"].Validate(JObject.Parse("{'trackIndex':0,'eventIndex':0,'atMs':0,'interpolation':'Linear','moveX':1e100}")));
            });
            Test("Pan/Crop scale is caller-supplied in the existing tool", () => {
                var tool=ToolRegistry.Tools.Value["set_pan_crop_keyframe"];
                Equal(ToolKind.Edit,tool.Kind);
                tool.Validate(JObject.Parse("{'trackIndex':0,'eventIndex':0,'atMs':0,'interpolation':'Hold','scaleX':-1,'scaleY':1}"));
                Equal(false,ToolRegistry.Tools.Value.ContainsKey("scale_pan_crop_keyframe"));
                Throws<ArgumentOutOfRangeException>(()=>new KeyframeTools().SetPanCropKeyframe(0,0,0,"Hold",scaleX:0));
                Throws<ArgumentOutOfRangeException>(()=>new KeyframeTools().SetPanCropKeyframe(0,0,0,"Hold",scaleY:float.NaN));
            });
            Console.WriteLine("PASS: " + _passed + " batch tests"); return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static BatchExecutor Plan(string json, FakeHost host=null,string mode="single",string onError="stop") =>
        new BatchExecutor(Tools,host??new FakeHost(),JArray.Parse(json).ToObject<BatchOperation[]>(),"test",mode,onError);
    private static void Run(BatchExecutor plan,CancellationToken token=default(CancellationToken))
    { plan.ExecuteRange(0,plan.Operations.Length,"test",token,null); plan.Finish(); }
    private static void Test(string name,Action action) { FakeTools.Values.Clear(); action(); _passed++; Console.WriteLine("PASS " + name); }
    private static void Equal<T>(T expected,T actual) { if(!Equals(expected,actual)) throw new Exception("Expected "+expected+", got "+actual); }
    private static void Throws<T>(Action action) where T:Exception
    { try { action(); } catch(T) {return;} throw new Exception("Expected "+typeof(T).Name); }

    public sealed class FakeTools
    {
        internal static readonly List<int> Values=new List<int>();
        [ToolExecution(ToolKind.Edit)] public object Edit(int value) { Values.Add(value); return new {value}; }
        [ToolExecution(ToolKind.Edit)] public object Fail() { Values.Add(99); throw new InvalidOperationException("Injected failure after an edit"); }
        [ToolExecution(ToolKind.Read)] public object Read() => new {count=Values.Count};
        [ToolExecution(ToolKind.External)] public object External() => new {ok=true};
        [ToolExecution(ToolKind.Control)] public object Control() => null;
    }
    private sealed class FakeHost:IBatchHost
    {
        internal int Opened,Closed,Index;
        internal bool Deleted;
        public IDisposable BeginUndo(string label) { Opened++; return new Scope(()=>Closed++); }
        public object Capture(string kind,JObject selector) => new object();
        public JToken ResolveHandle(object handle,string property)
        { if(Deleted) throw new InvalidOperationException("deleted"); return new JValue(Index); }
    }
    private sealed class Scope:IDisposable
    { private readonly Action _close; internal Scope(Action close) {_close=close;} public void Dispose()=>_close(); }
}

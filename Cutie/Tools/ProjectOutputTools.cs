using ModelContextProtocol.Server;
using ScriptPortal.Vegas;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace Cutie.Tools
{
    [McpServerToolType]
    public class ProjectOutputTools
    {
        [McpServerTool, Description("Create a new empty active project. Refuses to discard unsaved changes.")]
        [ToolExecution(ToolKind.External)]
        public object NewProject()
        {
            if (VegasToolSupport.Project.IsModified)
                throw new InvalidOperationException("Save the modified active project before creating a new one.");
            var created = VegasContext.Current.NewProject(false, false);
            if (!created) throw new InvalidOperationException("VEGAS did not create the project.");
            return new { created, filePath = VegasToolSupport.Project.FilePath,
                trackCount = VegasToolSupport.Project.Tracks.Count };
        }

        private static Vegas _subscribedVegas;
        private static object _renderState;
        private static string _renderStatus;
        private static Vegas _openVegas;
        private static long _openRequest;
        private static long _openCompleted;
        private static string _openPath;

        internal static bool IsOpenComplete(long request, string path) =>
            _openCompleted == request && string.Equals(VegasToolSupport.Project.FilePath, path, StringComparison.OrdinalIgnoreCase);

        private static void OnProjectOpened(object sender, EventArgs args)
        {
            if (string.Equals(VegasToolSupport.Project.FilePath, _openPath, StringComparison.OrdinalIgnoreCase))
                _openCompleted = _openRequest;
        }

        [McpServerTool, Description("Save the active project. An absolute path is required for an untitled project.")]
        [ToolExecution(ToolKind.External)]
        public object SaveProject(string path = null)
        {
            var target = path ?? VegasToolSupport.Project.FilePath;
            if (string.IsNullOrWhiteSpace(target) || !Path.IsPathRooted(target))
                throw new ArgumentException("An absolute project path is required.");
            target = Path.GetFullPath(target);
            if (!string.Equals(Path.GetExtension(target), ".veg", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Project path must have a .veg extension.");
            var saved = VegasToolSupport.Project.SaveProject(target);
            if (!saved) throw new InvalidOperationException("VEGAS did not save the project.");
            return new { saved, path = target };
        }

        [McpServerTool, Description("Open a .veg project. Refuses to replace a modified active project.")]
        [ToolExecution(ToolKind.External)]
        public object OpenProject(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || !File.Exists(path) ||
                !string.Equals(Path.GetExtension(path), ".veg", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("An existing absolute .veg path is required.");
            if (VegasToolSupport.Project.IsModified)
                throw new InvalidOperationException("Save the modified active project before opening another project.");
            var vegas = VegasContext.Current;
            if (!ReferenceEquals(_openVegas, vegas))
            {
                if (_openVegas != null) _openVegas.ProjectOpened -= OnProjectOpened;
                _openVegas = vegas;
                vegas.ProjectOpened += OnProjectOpened;
            }
            _openPath = Path.GetFullPath(path);
            var request = ++_openRequest;
            vegas.OpenFile(_openPath);
            return new { opened = true, path = VegasToolSupport.Project.FilePath, requestedPath = _openPath, openRequest = request };
        }

        [McpServerTool, Description("List installed render templates. Use offset and limit to page results.")]
        [ToolExecution(ToolKind.Read)]
        public object ListRenderTemplates(int offset = 0, int limit = 100)
        {
            if (offset < 0 || limit < 1 || limit > 500) throw new ArgumentOutOfRangeException();
            var templates = VegasContext.Current.Renderers.SelectMany(renderer =>
                renderer.Templates.Select(template => new
                {
                    rendererId = renderer.ID,
                    rendererName = renderer.Name,
                    templateId = template.TemplateID,
                    templateName = template.Name,
                    width = template.VideoWidth,
                    height = template.VideoHeight,
                    frameRate = template.VideoFrameRate,
                    extension = renderer.FileExtension
                })).ToArray();
            return new { total = templates.Length, offset,
                items = templates.Skip(offset).Take(limit).ToArray() };
        }

        [McpServerTool, Description("Start rendering with an installed template. Returns initial status; call get_render_status for progress. Times are milliseconds.")]
        [ToolExecution(ToolKind.External)]
        public object RenderProject(string outputPath, uint rendererId, uint templateId,
            double? startMs = null, double? lengthMs = null, bool overwrite = false)
        {
            if (_renderStatus == "Queued" || _renderStatus == "Rendering" ||
                _renderStatus == "Stitching" || _renderStatus == "starting")
                throw new InvalidOperationException("A Cutie render is already in progress.");
            if (string.IsNullOrWhiteSpace(outputPath) || !Path.IsPathRooted(outputPath))
                throw new ArgumentException("An absolute output path is required.");
            outputPath = Path.GetFullPath(outputPath);
            if (File.Exists(outputPath) && !overwrite)
                throw new IOException("Output file exists. Set overwrite to true to replace it.");
            if (startMs.HasValue != lengthMs.HasValue)
                throw new ArgumentException("Specify both startMs and lengthMs, or neither.");
            if (lengthMs.HasValue && lengthMs.Value <= 0)
                throw new ArgumentException("Render length must be positive.");

            var renderer = VegasContext.Current.Renderers.FirstOrDefault(item => item.ID == rendererId)
                ?? throw new ArgumentException("Renderer ID was not found.");
            var template = renderer.Templates.FirstOrDefault(item => item.TemplateID == templateId)
                ?? throw new ArgumentException("Template ID was not found.");
            var args = new RenderArgs(VegasToolSupport.Project)
            {
                OutputFile = outputPath,
                RenderTemplate = template
            };
            if (startMs.HasValue)
            {
                args.Start = VegasToolSupport.Time(startMs.Value);
                args.Length = VegasToolSupport.Time(lengthMs.Value);
            }
            SubscribeToRenderEvents();
            _renderStatus = "starting";
            _renderState = new { status = "starting", outputPath };
            RenderStatus status;
            try
            {
                status = VegasContext.Current.BeginRender(args);
            }
            catch
            {
                _renderStatus = "Failed";
                _renderState = new { status = "Failed", outputPath };
                throw;
            }
            if (_renderStatus == "starting")
            {
                _renderStatus = status.ToString();
                _renderState = new { status = status.ToString(), outputPath };
            }
            return _renderState;
        }

        [McpServerTool, Description("Get latest render status from a render started through Cutie.")]
        [ToolExecution(ToolKind.Read)]
        public object GetRenderStatus() => _renderState ?? new { status = "not_started" };

        private static void SubscribeToRenderEvents()
        {
            var vegas = VegasContext.Current;
            if (ReferenceEquals(_subscribedVegas, vegas)) return;
            if (_subscribedVegas != null)
            {
                _subscribedVegas.RenderProgress -= OnRenderProgress;
                _subscribedVegas.RenderFinished -= OnRenderFinished;
            }
            vegas.RenderProgress += OnRenderProgress;
            vegas.RenderFinished += OnRenderFinished;
            _subscribedVegas = vegas;
        }

        private static void OnRenderProgress(object sender, RenderStatusEventArgs args)
        {
            _renderStatus = args.Status.ToString();
            _renderState = new { status = args.Status.ToString(), outputPath = args.FilePath,
                percentComplete = args.PercentComplete, timeElapsed = args.TimeElapsed,
                timeLeft = args.TimeLeft, error = args.ErrorMessage };
        }

        private static void OnRenderFinished(object sender, RenderStatusEventArgs args)
        {
            OnRenderProgress(sender, args);
        }
    }
}
